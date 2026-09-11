using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Inventory
{
    /// <summary>
    /// Makes EquipmentService's ItemEquipped/ItemUnequipped events visually real on a spawned character:
    /// instantiates an equipped item's <see cref="ItemDefinition.VisualPrefab"/> as a child and rebinds its
    /// SkinnedMeshRenderer(s) onto this character's own skeleton by bone name, so a modular outfit piece
    /// (e.g. separately-modeled arms/body/legs/feet, all sharing one rig with the base body) deforms with
    /// exactly the same animation as the body underneath it -- no retargeting needed as long as the piece's
    /// source skeleton uses the same bone names as this character's (true for every pack this was built
    /// against; verified once with a throwaway live Editor query rather than assumed from file names).
    /// A bone name with no match on this character is left pointing at the piece's own (unused) copy, which
    /// is harmless for the packs this ships against but would visibly fail to deform correctly for a
    /// hand-authored VisualPrefab on a genuinely different skeleton.
    /// <para>Composition-root wiring, not auto-discovery: whoever spawns this character calls
    /// <see cref="Initialize"/> with its RuntimeEntityId and the same rules database/event bus the rest of
    /// that character's services use -- matches the "no singletons, inject everything" rule the rest of the
    /// engine follows.</para>
    /// </summary>
    public sealed class CharacterVisualEquipment : MonoBehaviour
    {
        private RuntimeEntityId character;
        private IRulesDatabase rulesDatabase;
        private IGameEventBus eventBus;
        private Dictionary<string, Transform> bonesByName;
        private readonly Dictionary<DefinitionId, GameObject> equippedVisuals = new();

        // eventBus == null is this component's "unbound" signal (see Unbind); isSubscribed tracks only
        // whether the currently-retained binding is actively listening right now.
        private bool isSubscribed;

        /// <summary>Binds this component to a character/event bus. Rebinding the identical (character,
        /// rulesDatabase, eventBus) tuple is a no-op -- repeat spawn-time wiring must not destroy gear
        /// already equipped under the same binding. Binding to a different character/session while already
        /// bound is an error; callers must call <see cref="Unbind"/> first (silently replacing a binding is
        /// exactly what let a rebind leak equipped-visual state onto the wrong character -- Task 1.1 code
        /// review). Actually subscribing is deferred to <see cref="OnEnable"/> if this component is
        /// inactive/disabled right now, matching <see cref="CharacterPlayableAnimator"/>'s policy.</summary>
        public void Initialize(RuntimeEntityId character, IRulesDatabase rulesDatabase, IGameEventBus eventBus)
        {
            if (this.eventBus != null)
            {
                if (this.character == character && ReferenceEquals(this.rulesDatabase, rulesDatabase) && ReferenceEquals(this.eventBus, eventBus))
                    return;

                throw new InvalidOperationException(
                    $"{name} is already bound to {this.character} -- call Unbind() before binding to a different character/session.");
            }

            this.character = character;
            this.rulesDatabase = rulesDatabase;
            this.eventBus = eventBus;

            bonesByName = new Dictionary<string, Transform>();
            foreach (var bone in GetComponentsInChildren<Transform>(true)) bonesByName[bone.name] = bone;

            if (isActiveAndEnabled) Subscribe();
        }

        /// <summary>Fully detaches this component: unsubscribes, clears the binding identity, and destroys
        /// every currently-tracked equipped-visual instance -- a rebind means those visuals belong to a
        /// character this component no longer represents, so they must go. Idempotent. Called automatically
        /// on destroy; call directly before rebinding to a different character/session.</summary>
        public void Unbind()
        {
            Unsubscribe();

            character = RuntimeEntityId.None;
            rulesDatabase = null;
            eventBus = null;
            bonesByName = null;

            foreach (var visual in equippedVisuals.Values) Destroy(visual);
            equippedVisuals.Clear();
        }

        /// <summary>Rebuilds the bone-name lookup from the current hierarchy and destroys every
        /// currently-tracked equipped-visual instance -- their bone references point at the OLD skeleton and
        /// would otherwise silently deform against destroyed Transforms once it's gone. Called by
        /// <see cref="CRPG.Presentation.CharacterView"/> both for the initial Model bind (where
        /// equippedVisuals is empty, so this is just the bone-map build) and every subsequent Model swap.
        /// Deliberately does NOT re-instantiate what was equipped -- full reconciliation from live inventory
        /// state on rebind is Task 2.4's job; the interim contract here is simpler and stricter: never leave a
        /// visual bound to a destroyed skeleton, even if that means it visibly disappears until re-equipped.</summary>
        public void RefreshBoneMap()
        {
            foreach (var visual in equippedVisuals.Values) Destroy(visual);
            equippedVisuals.Clear();

            bonesByName = new Dictionary<string, Transform>();
            foreach (var bone in GetComponentsInChildren<Transform>(true)) bonesByName[bone.name] = bone;
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

        // Deliberately does NOT unsubscribe (unlike CharacterPlayableAnimator's purely-presentational cues):
        // missing an ItemEquipped/ItemUnequipped event while disabled would leave this character's worn gear
        // silently wrong once re-enabled, and full inventory reconciliation-on-enable is Task 2.4 work, not
        // this one's. Interim policy instead (Task 1.1 code review, issue 4): keep processing equip/unequip
        // events even while inactive/disabled, so equipped-visual state never has a chance to drift in the
        // first place. A disabled GameObject's children still instantiate/destroy correctly; they just don't
        // render/update until the parent reactivates.
        private void OnDisable()
        {
        }

        // Resumes a retained binding; a no-op if there's no binding (never Initialize()'d, or explicitly
        // Unbind()'d) or if Initialize already subscribed because this component was active/enabled then, or
        // if OnDisable never unsubscribed in the first place (see OnDisable above).
        private void OnEnable()
        {
            if (eventBus != null) Subscribe();
        }

        private void OnItemEquipped(GameEventContext context)
        {
            if (context.Target != character || !TryGetSlot(context, out var slot)) return;

            RemoveVisual(slot);

            if (!TryGetItemDefinitionId(context, out var itemDefinitionId)) return;
            if (!rulesDatabase.TryResolve<ItemDefinition>(itemDefinitionId, out var definition) || definition.VisualPrefab == null) return;

            var instance = Instantiate(definition.VisualPrefab, transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            RebindSkinnedMeshes(instance);
            equippedVisuals[slot] = instance;
        }

        private void OnItemUnequipped(GameEventContext context)
        {
            if (context.Target != character || !TryGetSlot(context, out var slot)) return;
            RemoveVisual(slot);
        }

        private void RemoveVisual(DefinitionId slot)
        {
            if (!equippedVisuals.TryGetValue(slot, out var existing)) return;
            equippedVisuals.Remove(slot);
            Destroy(existing);
        }

        private void RebindSkinnedMeshes(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = renderer.bones;
                for (var i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null && bonesByName.TryGetValue(bones[i].name, out var characterBone))
                        bones[i] = characterBone;
                }

                renderer.bones = bones;
                if (renderer.rootBone != null && bonesByName.TryGetValue(renderer.rootBone.name, out var characterRoot))
                    renderer.rootBone = characterRoot;
            }
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
