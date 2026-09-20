using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Presentation.Equipment
{
    /// <summary>
    /// Task 2.4 (Production Foundation): makes EquipmentService's ItemEquipped/ItemUnequipped events visually
    /// real on a spawned character, per Docs/Design/Production-Foundation.md sec.4.5. Migrated from
    /// <c>CRPG.Inventory.CharacterVisualEquipment</c> (Task 1.1) -- not a GUID-preserving move like Task 2.2's
    /// CharacterPlayableAnimator migration, deliberately: that old class had zero [SerializeField] fields, so
    /// there was no authored data on the real prefab for a preserved GUID to protect; this is a genuine
    /// redesign built around <see cref="EquipmentVisualProfile"/> instead.
    /// <para>Replaces the old "match by leaf Transform.name, silently leave an unmatched bone pointing at the
    /// garment's own unused copy" behavior with three explicit, individually-validated paths: Rigid (a named
    /// socket Transform, works across skeletons -- no rig check needed), Skinned (an explicit, authored
    /// <see cref="RigBindingMap"/> of relative bone PATHS, rejected up front on a rig-family mismatch or any
    /// missing/duplicate path -- nothing touches a SkinnedMeshRenderer until every path in the map is
    /// confirmed to resolve), and None (no visual, no failure). A validation failure never disturbs whatever
    /// visual already occupied that slot -- "visual failure retains the known-good appearance," decoupled
    /// from EquipmentService's own gameplay-layer equip state, which always succeeds/fails independently.</para>
    /// <para>Shared body-region hiding (design: "removing one piece cannot expose a region still covered by
    /// another") is reference-counted: each currently-equipped profile's <see cref="EquipmentVisualProfile.HiddenBodyRegions"/>
    /// contributes +1/-1 per region on equip/unequip, and a slot's own visual is active only while its
    /// <see cref="EquipmentVisualProfile.BodyRegionId"/> has a zero ref count -- so two garments hiding the
    /// same region, removed in either order, only reveals it once the LAST one is gone.</para>
    /// <para>"Initial full reconciliation from inventory on bind/load, then targeted updates" (design sec.4.5)
    /// is <see cref="TryReconcile"/>, run from both <see cref="Initialize"/> and <see cref="RefreshBoneMap"/>
    /// -- the same mechanism naturally covers "load before view bind" (an ItemEquipped event arriving before
    /// any skeleton is bound is simply deferred: <see cref="OnItemEquipped"/> no-ops while unbound, and the
    /// next RefreshBoneMap's reconciliation picks up the truth from PartyInventoryState) and "view bind before
    /// load" (Initialize's own reconciliation call covers a skeleton that happened to bind first).</para>
    /// </summary>
    public sealed class EquipmentVisualBinder : MonoBehaviour
    {
        private RuntimeEntityId character;
        private IRulesDatabase rulesDatabase;
        private PartyInventoryState inventory;
        private IGameEventBus eventBus;
        private CharacterRigDefinition rigDefinition;

        // eventBus == null is this component's "unbound" signal (see Unbind); isSubscribed tracks only
        // whether the currently-retained binding is actively listening right now.
        private bool isSubscribed;

        // Null until a skeleton has been bound at least once (RefreshBoneMap) -- see TryReconcile's
        // load-before-view-bind handling.
        private Dictionary<string, Transform> transformsByPath;
        private Dictionary<string, Transform> transformsByName;

        private readonly Dictionary<DefinitionId, GameObject> equippedVisuals = new();
        private readonly Dictionary<DefinitionId, EquipmentVisualProfile> equippedProfiles = new();
        private readonly Dictionary<DefinitionId, DefinitionId> occupiedRegionBySlot = new();
        private readonly Dictionary<DefinitionId, int> regionHideRefCounts = new();

        // Phase 2 correction pass (R5): EquipmentService publishes ItemUnequipped for a displaced occupant
        // BEFORE ItemEquipped for its replacement (both synchronously, within one Equip() call) -- so the OLD
        // "OnItemUnequipped destroys immediately" behavior tore down the known-good visual before the
        // replacement was ever validated, defeating "visual failure retains the known-good appearance" for
        // every REAL same-slot replacement (only direct same-slot ItemEquipped-without-a-preceding-Unequipped
        // was ever actually protected). Fix: an unequip only marks a slot pending removal; a same-slot equip
        // that arrives synchronously right after (the normal replacement case) reclaims or truly discards the
        // pending entry depending on whether the new visual actually succeeds. LateUpdate flushes anything
        // still pending at the end of the frame -- a genuine unequip with no replacement.
        private readonly HashSet<DefinitionId> warnedSpriteProfiles = new();
        private readonly Dictionary<DefinitionId, GameObject> pendingRemovalVisuals = new();
        private readonly Dictionary<DefinitionId, EquipmentVisualProfile> pendingRemovalProfiles = new();

        /// <summary>Diagnostic: the currently-instantiated visual GameObject for one slot, or null if that
        /// slot has no visual bound right now (unequipped, no profile authored, or the profile's Kind is
        /// None).</summary>
        public GameObject GetEquippedVisual(DefinitionId slot) => equippedVisuals.TryGetValue(slot, out var visual) ? visual : null;

        /// <summary>Binds this component to a character/event bus/inventory. Rebinding the identical
        /// (character, rulesDatabase, inventory, eventBus) tuple is a no-op; binding to a different tuple
        /// while already bound throws (call <see cref="Unbind"/> first) -- same contract Task 1.1 established
        /// for this class's predecessor. Actually subscribing is deferred to <see cref="OnEnable"/> if this
        /// component is inactive/disabled right now.</summary>
        public void Initialize(RuntimeEntityId character, IRulesDatabase rulesDatabase, PartyInventoryState inventory, IGameEventBus eventBus)
        {
            if (this.eventBus != null)
            {
                if (this.character == character && ReferenceEquals(this.rulesDatabase, rulesDatabase) &&
                    ReferenceEquals(this.inventory, inventory) && ReferenceEquals(this.eventBus, eventBus))
                    return;

                throw new InvalidOperationException(
                    $"{name} is already bound to {this.character} -- call Unbind() before binding to a different character/session.");
            }

            this.character = character;
            this.rulesDatabase = rulesDatabase;
            this.inventory = inventory;
            this.eventBus = eventBus;

            if (isActiveAndEnabled) Subscribe();
            TryReconcile(); // covers a skeleton that happened to bind before Initialize did
        }

        /// <summary>Fully detaches this component: unsubscribes, clears the binding identity, forgets the
        /// bound skeleton, and destroys every currently-tracked equipped-visual instance. Idempotent. Called
        /// automatically on destroy; call directly before rebinding to a different character/session.</summary>
        public void Unbind()
        {
            Unsubscribe();

            character = RuntimeEntityId.None;
            rulesDatabase = null;
            inventory = null;
            eventBus = null;
            rigDefinition = null;
            transformsByPath = null;
            transformsByName = null;
            warnedSpriteProfiles.Clear();

            DestroyAllVisuals();
        }

        /// <summary>Rebuilds the bone/socket path lookup from the current hierarchy, destroys every
        /// currently-tracked equipped-visual instance (their bone references point at the OLD skeleton), and
        /// performs a full reconciliation against live inventory state. Called by
        /// <see cref="CRPG.Presentation.CharacterView"/> for both the initial Model bind and every subsequent
        /// Model swap.</summary>
        public void RefreshBoneMap(CharacterRigDefinition rig)
        {
            rigDefinition = rig;
            transformsByPath = BuildPathMap(transform);
            transformsByName = BuildNameMap(transform);

            DestroyAllVisuals();
            TryReconcile();
        }

        private void Subscribe()
        {
            if (isSubscribed) return;
            eventBus.Subscribe(GameEvent.ItemEquipped, OnItemEquipped);
            eventBus.Subscribe(GameEvent.ItemUnequipped, OnItemUnequipped);
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed) return;
            eventBus?.Unsubscribe(GameEvent.ItemEquipped, OnItemEquipped);
            eventBus?.Unsubscribe(GameEvent.ItemUnequipped, OnItemUnequipped);
            isSubscribed = false;
        }

        private void OnDestroy() => Unbind();

        // Deliberately does NOT unsubscribe (Task 1.1 code review, issue 4): missing an equip/unequip event
        // while disabled would leave worn gear silently wrong once re-enabled. A disabled GameObject's
        // children still instantiate/destroy correctly; they just don't render/update until reactivated.
        private void OnDisable()
        {
        }

        private void OnEnable()
        {
            if (eventBus != null) Subscribe();
        }

        private void OnItemEquipped(GameEventContext context)
        {
            if (context.Target != character || !TryGetSlot(context, out var slot)) return;
            if (!TryGetItemDefinitionId(context, out var itemDefinitionId)) return;
            EquipSlot(slot, itemDefinitionId);
        }

        private void OnItemUnequipped(GameEventContext context)
        {
            if (context.Target != character || !TryGetSlot(context, out var slot)) return;
            MarkPendingRemoval(slot);
        }

        // R5: does NOT destroy -- moves the slot's current visual/profile into a pending-removal holding area
        // so a same-slot ItemEquipped arriving synchronously right after (the normal EquipmentService
        // replacement sequence) can still reclaim it if the new visual fails validation. LateUpdate flushes
        // anything still pending once all of this frame's events have been processed.
        private void MarkPendingRemoval(DefinitionId slot)
        {
            if (equippedProfiles.TryGetValue(slot, out var profile))
            {
                equippedProfiles.Remove(slot);
                pendingRemovalProfiles[slot] = profile;
            }

            if (equippedVisuals.TryGetValue(slot, out var visual))
            {
                equippedVisuals.Remove(slot);
                pendingRemovalVisuals[slot] = visual;
            }
        }

        // The replacement failed (or there was no replacement content to show) -- put the pending entry back
        // exactly as it was, so the known-good appearance is genuinely retained rather than already destroyed.
        private void RestorePendingRemoval(DefinitionId slot)
        {
            if (pendingRemovalProfiles.TryGetValue(slot, out var profile))
            {
                pendingRemovalProfiles.Remove(slot);
                equippedProfiles[slot] = profile;
            }

            if (pendingRemovalVisuals.TryGetValue(slot, out var visual))
            {
                pendingRemovalVisuals.Remove(slot);
                if (visual != null) equippedVisuals[slot] = visual;
            }

            RecomputeVisibility();
        }

        // The replacement succeeded, or nothing ever came to reclaim this slot by end of frame -- now safe to
        // actually tear the old visual down and release its region-hiding contribution.
        private void DiscardPendingRemoval(DefinitionId slot)
        {
            if (pendingRemovalProfiles.TryGetValue(slot, out var profile))
            {
                pendingRemovalProfiles.Remove(slot);
                ApplyRegionEffects(slot, profile, equipping: false);
            }

            if (pendingRemovalVisuals.TryGetValue(slot, out var visual))
            {
                pendingRemovalVisuals.Remove(slot);
                if (visual != null) Destroy(visual);
            }
        }

        // A genuine unequip with no same-frame replacement: whatever is still pending once every event this
        // frame has been processed gets truly torn down here.
        private void LateUpdate()
        {
            if (pendingRemovalVisuals.Count == 0 && pendingRemovalProfiles.Count == 0) return;

            var slots = new HashSet<DefinitionId>(pendingRemovalVisuals.Keys);
            slots.UnionWith(pendingRemovalProfiles.Keys);
            foreach (var slot in slots) DiscardPendingRemoval(slot);

            RecomputeVisibility();
        }

        /// <summary>Reads every item PartyInventoryState says is currently equipped by this character and
        /// (re)binds each one's visual. A no-op while no skeleton is bound yet, or before Initialize -- safe
        /// to call unconditionally.</summary>
        private void TryReconcile()
        {
            if (transformsByPath == null || inventory == null) return;

            foreach (var item in inventory.ItemsEquippedBy(character))
            {
                var slot = inventory.GetLocation(item.InstanceId).Slot;
                EquipSlot(slot, item.DefinitionId);
            }
        }

        private void EquipSlot(DefinitionId slot, DefinitionId itemDefinitionId)
        {
            if (transformsByPath == null) return; // no skeleton yet -- the next RefreshBoneMap's reconciliation will pick this up

            if (!rulesDatabase.TryResolve<ItemDefinition>(itemDefinitionId, out var itemDef)) return;

            EquipmentVisualProfile profile = null;
            if (itemDef.EquipmentVisualProfileId.IsValid)
                rulesDatabase.TryResolve(itemDef.EquipmentVisualProfileId, out profile);

            if (profile == null || profile.Kind == EquipmentVisualKind.None || profile.Kind == EquipmentVisualKind.Sprite)
            {
                // A Sprite profile is not a 3D visual: treat it like "no 3D visual" (whatever this binder showed in the
                // slot is gone), and say so ONCE per profile -- it means a 3D character was given sprite gear.
                if (profile != null && profile.Kind == EquipmentVisualKind.Sprite && warnedSpriteProfiles.Add(profile.Id))
                    Debug.LogWarning($"[EquipmentVisualBinder] '{profile.name}' is a Sprite profile; sprite characters use SpriteEquipmentBinder. " +
                                     "It draws nothing on a 3D character.");
                DiscardPendingRemoval(slot); // no replacement content -- whatever was pending really is gone
                RemoveVisual(slot);
                return;
            }

            if (!TryBuildVisual(profile, out var instance, out var error))
            {
                Debug.LogWarning($"[EquipmentVisualBinder] Could not bind visual for '{itemDefinitionId}' in slot " +
                                  $"'{slot}': {error} -- leaving the previous appearance (if any) untouched.");
                RestorePendingRemoval(slot); // put back whatever this replacement would have displaced
                return;
            }

            DiscardPendingRemoval(slot); // the replacement succeeded -- now safe to truly discard the old one
            RemoveVisual(slot); // also covers a direct re-equip with no preceding Unequipped event at all
            if (instance != null) equippedVisuals[slot] = instance;
            equippedProfiles[slot] = profile;
            ApplyRegionEffects(slot, profile, equipping: true);
            RecomputeVisibility();
        }

        private void RemoveVisual(DefinitionId slot)
        {
            if (equippedProfiles.TryGetValue(slot, out var oldProfile))
            {
                equippedProfiles.Remove(slot);
                ApplyRegionEffects(slot, oldProfile, equipping: false);
            }

            occupiedRegionBySlot.Remove(slot);

            if (equippedVisuals.TryGetValue(slot, out var existing))
            {
                equippedVisuals.Remove(slot);
                if (existing != null) Destroy(existing);
            }

            RecomputeVisibility();
        }

        private void DestroyAllVisuals()
        {
            foreach (var visual in equippedVisuals.Values)
                if (visual != null) Destroy(visual);
            foreach (var visual in pendingRemovalVisuals.Values)
                if (visual != null) Destroy(visual);

            equippedVisuals.Clear();
            equippedProfiles.Clear();
            pendingRemovalVisuals.Clear();
            pendingRemovalProfiles.Clear();
            occupiedRegionBySlot.Clear();
            regionHideRefCounts.Clear();
        }

        private void ApplyRegionEffects(DefinitionId slot, EquipmentVisualProfile profile, bool equipping)
        {
            if (equipping && profile.BodyRegionId.IsValid) occupiedRegionBySlot[slot] = profile.BodyRegionId;

            var delta = equipping ? 1 : -1;
            foreach (var region in profile.HiddenBodyRegions)
            {
                var count = regionHideRefCounts.TryGetValue(region, out var existing) ? existing : 0;
                count += delta;
                if (count <= 0) regionHideRefCounts.Remove(region);
                else regionHideRefCounts[region] = count;
            }
        }

        private void RecomputeVisibility()
        {
            foreach (var pair in equippedVisuals)
            {
                var hidden = occupiedRegionBySlot.TryGetValue(pair.Key, out var region) && region.IsValid &&
                             regionHideRefCounts.TryGetValue(region, out var count) && count > 0;
                if (pair.Value != null) pair.Value.SetActive(!hidden);
            }
        }

        private bool TryBuildVisual(EquipmentVisualProfile profile, out GameObject instance, out string error)
        {
            instance = null;

            if (profile.VisualPrefab == null)
            {
                error = $"'{profile.name}' has no VisualPrefab authored.";
                return false;
            }

            return profile.Kind == EquipmentVisualKind.Rigid
                ? TryBuildRigid(profile, out instance, out error)
                : TryBuildSkinned(profile, out instance, out error);
        }

        private bool TryBuildRigid(EquipmentVisualProfile profile, out GameObject instance, out string error)
        {
            instance = null;

            if (!profile.SocketId.IsValid)
            {
                error = $"'{profile.name}' is a Rigid profile with no SocketId authored.";
                return false;
            }

            if (!transformsByName.TryGetValue(profile.SocketId.Value, out var socket))
            {
                error = $"Socket '{profile.SocketId}' was not found on the bound skeleton.";
                return false;
            }

            instance = Instantiate(profile.VisualPrefab, socket);
            instance.transform.localPosition = profile.LocalPosition;
            instance.transform.localRotation = profile.LocalRotation;
            instance.transform.localScale = profile.LocalScale;
            error = null;
            return true;
        }

        private bool TryBuildSkinned(EquipmentVisualProfile profile, out GameObject instance, out string error)
        {
            instance = null;

            var map = profile.BoneMap;
            if (map == null)
            {
                error = $"'{profile.name}' is a Skinned profile with no RigBindingMap authored.";
                return false;
            }

            if (rigDefinition != null && map.RigFamilyId.IsValid && rigDefinition.Id != map.RigFamilyId)
            {
                error = $"'{profile.name}''s RigBindingMap is authored for rig family '{map.RigFamilyId}' but this " +
                        $"character's rig is '{rigDefinition.Id}'.";
                return false;
            }

            if (!map.Validate(out var structuralError))
            {
                error = structuralError;
                return false;
            }

            // Resolve every TARGET path against the live character skeleton before touching any renderer. A
            // path absent from transformsByPath covers both "genuinely missing" and "ambiguous" (two distinct
            // Transforms in the hierarchy that computed the same path) -- BuildPathMap deliberately omits
            // ambiguous paths entirely rather than silently picking one (R6).
            var targetsBySource = new Dictionary<string, Transform>();
            foreach (var entry in map.BonePaths)
            {
                if (!transformsByPath.TryGetValue(entry.TargetPath, out var targetBone))
                {
                    error = $"'{profile.name}': missing (or ambiguous) target bone path '{entry.TargetPath}' on the bound skeleton.";
                    return false;
                }

                targetsBySource[entry.SourcePath] = targetBone;
            }

            var candidate = Instantiate(profile.VisualPrefab);
            var sourceLookup = BuildPathMap(candidate.transform);

            // Resolve every SOURCE path against the freshly-instantiated garment before touching any renderer.
            var boneRemap = new Dictionary<Transform, Transform>();
            foreach (var entry in map.BonePaths)
            {
                if (!sourceLookup.TryGetValue(entry.SourcePath, out var sourceBone))
                {
                    error = $"'{profile.name}': missing (or ambiguous) source bone path '{entry.SourcePath}' on the garment prefab.";
                    Destroy(candidate);
                    return false;
                }

                boneRemap[sourceBone] = targetsBySource[entry.SourcePath];
            }

            var renderers = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            // R6: reject the WHOLE binding if any renderer references a bone the map doesn't cover, rather
            // than silently leaving that one bone pointed at the garment's own (unrebound) copy -- the exact
            // half-bound failure mode Task 2.4 set out to eliminate. A map only had to prove its OWN entries
            // resolve before; this additionally proves the entries actually cover what the renderers need.
            foreach (var renderer in renderers)
            {
                foreach (var bone in renderer.bones)
                {
                    if (bone != null && !boneRemap.ContainsKey(bone))
                    {
                        error = $"'{profile.name}': renderer '{renderer.name}' references bone '{bone.name}', which " +
                                "the bone map does not cover -- add it, or the mesh would be half-bound.";
                        Destroy(candidate);
                        return false;
                    }
                }

                if (renderer.rootBone != null && !boneRemap.ContainsKey(renderer.rootBone) &&
                    (string.IsNullOrEmpty(map.RootBonePath) || !transformsByPath.ContainsKey(map.RootBonePath)))
                {
                    error = $"'{profile.name}': renderer '{renderer.name}''s root bone is not covered by the map " +
                            "and no valid RootBonePath fallback is authored.";
                    Destroy(candidate);
                    return false;
                }
            }

            foreach (var renderer in renderers)
            {
                var bones = renderer.bones;
                for (var i = 0; i < bones.Length; i++)
                    if (bones[i] != null) bones[i] = boneRemap[bones[i]]; // coverage already proven above

                renderer.bones = bones;

                if (renderer.rootBone != null)
                {
                    renderer.rootBone = boneRemap.TryGetValue(renderer.rootBone, out var remappedRoot)
                        ? remappedRoot
                        : transformsByPath[map.RootBonePath]; // the only other case the check above allowed through
                }
            }

            candidate.transform.SetParent(transform, false);
            instance = candidate;
            error = null;
            return true;
        }

        /// <summary>Maps every descendant of root to its path relative to root ("" for root itself,
        /// "Child/Grandchild" and so on) -- the same convention EquipmentVisualBinder uses for both the
        /// character's own skeleton and a garment's source hierarchy, so a RigBindingMap's paths mean the
        /// same thing on both sides. A path that TWO different Transforms both compute (two siblings sharing
        /// an exact name under the same parent) is genuinely ambiguous and is deliberately OMITTED from the
        /// result entirely (R6) -- a lookup against it then correctly reports "not found" and rejects the
        /// binding with a clear error, rather than silently resolving to whichever one was visited last.</summary>
        private static Dictionary<string, Transform> BuildPathMap(Transform root)
        {
            var map = new Dictionary<string, Transform>();
            var ambiguous = new HashSet<string>();
            Walk(root, string.Empty, map, ambiguous);
            foreach (var path in ambiguous) map.Remove(path);
            return map;
        }

        private static void Walk(Transform current, string path, Dictionary<string, Transform> map, HashSet<string> ambiguous)
        {
            if (map.ContainsKey(path)) ambiguous.Add(path);
            else map[path] = current;

            for (var i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                Walk(child, path.Length == 0 ? child.name : path + "/" + child.name, map, ambiguous);
            }
        }

        // Sockets are found by leaf Transform.name (first match wins) rather than by full path -- a rigid
        // attachment is meant to work across different skeletons via a stable, semantic socket name, not a
        // rig-specific path. No rig-family authoring convention exists for sockets yet since no real content
        // uses Rigid profiles today; this is deliberately the simple case the design calls out to do first.
        private static Dictionary<string, Transform> BuildNameMap(Transform root)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map[t.name] = t;
            return map;
        }

        private static bool TryGetSlot(GameEventContext context, out DefinitionId slot)
        {
            if (context.Data.TryGetValue("SlotId", out var value) && value is DefinitionId id) { slot = id; return true; }
            slot = default;
            return false;
        }

        private static bool TryGetItemDefinitionId(GameEventContext context, out DefinitionId itemDefinitionId)
        {
            if (context.Data.TryGetValue("ItemDefinitionId", out var value) && value is DefinitionId id) { itemDefinitionId = id; return true; }
            itemDefinitionId = default;
            return false;
        }
    }
}
