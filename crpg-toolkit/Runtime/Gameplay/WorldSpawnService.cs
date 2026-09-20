using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using CRPG.Saving;
using UnityEngine;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Wraps <see cref="NpcSpawnService"/>'s existing state-only spawn (unchanged) with the GameObject/faction/
    /// inventory/motor/view wiring every demo bootstrap has so far reimplemented by hand -- closes the gap
    /// Docs/Design/Production-Foundation.md's repository-grounded-baseline table flagged: "NpcSpawnService.cs
    /// builds state only; visual, equipment, faction and dialogue composition are caller responsibilities."
    /// Dialogue composition is not addressed here (Task 3.x's interaction/dialogue-trigger work); this covers
    /// exactly the five things Task 1.2 scoped: spawn binding state, faction, inventory, motor and view.
    /// <para>
    /// Task 3.5: also the authoritative, save-relevant record of every spawn this session knows about --
    /// <see cref="recordsBySpawnId"/> outlives any single GameObject (a defeated spawn keeps reporting
    /// Defeated=true on every later <see cref="CaptureSnapshot"/> even with nothing instantiated for it), and
    /// <see cref="OwnedAgents"/> is the correct ownership registry for session teardown -- unlike
    /// <see cref="CharacterAgent.AllLive"/>, which only tracks currently-ENABLED agents and would silently skip
    /// a disabled one.
    /// </para>
    /// </summary>
    public sealed class WorldSpawnService
    {
        private sealed class SpawnRecord
        {
            public RuntimeEntityId? EntityId;
            public DefinitionId NpcDefinitionId;
            public bool Defeated;

            /// <summary>Task 3.6: last-known world transform, kept up to date whenever a live agent exists
            /// (CaptureSnapshot, and just before ReleaseSpawnsExcept destroys the view) so a spawn whose
            /// location is currently unloaded still reports a real position (not the origin) to a save taken
            /// while it's away, and so a later re-entry can rebind its view at the same spot.</summary>
            public Vector3 Position;
            public float FacingYaw;
        }

        private readonly GameSession session;
        private readonly EquipmentService equipmentService;
        private readonly Dictionary<RuntimeEntityId, DefinitionId> factionsByCharacter = new();
        private readonly Dictionary<string, SpawnRecord> recordsBySpawnId = new();
        private readonly Dictionary<RuntimeEntityId, CharacterAgent> agentsByCharacter = new();

        public WorldSpawnService(GameSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            equipmentService = new EquipmentService(session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);
        }

        /// <summary>Optional grid-movement backend. When set, every spawned character whose prefab carries a
        /// <see cref="GridCharacterMotor"/> gets this pathfinder and its own entity id as its occupancy identity
        /// at bind time (composition-root injection, not a static lookup). Leave null for NavMesh characters.</summary>
        public GridPathfindingService GridNavigation { get; set; }

        /// <summary>The faction tag this spawn's NpcDefinition authored, if any. Deliberately not
        /// Combat.Combatant.Faction (that's a per-encounter concept -- CRPG.Combat.CombatEncounter.AddCombatant
        /// takes its own faction parameter when a fight actually starts); this is the standing, out-of-combat
        /// association a future AI/interaction system can query without holding onto the original
        /// NpcDefinition asset reference.</summary>
        public bool TryGetFaction(RuntimeEntityId character, out DefinitionId factionId) => factionsByCharacter.TryGetValue(character, out factionId);

        public bool TryGetSpawnId(RuntimeEntityId character, out string spawnId)
        {
            foreach (var pair in recordsBySpawnId)
            {
                if (pair.Value.EntityId == character) { spawnId = pair.Key; return true; }
            }

            spawnId = null;
            return false;
        }

        /// <summary>Task 3.6: forward lookup counterpart to TryGetSpawnId -- the character id (and whether it's
        /// still live vs defeated) a known spawnId maps to. False only if HasRecord(spawnId) is also false.</summary>
        public bool TryGetCharacterId(string spawnId, out RuntimeEntityId characterId, out DefinitionId npcDefinitionId, out bool defeated)
        {
            if (!string.IsNullOrEmpty(spawnId) && recordsBySpawnId.TryGetValue(spawnId, out var record))
            {
                characterId = record.EntityId ?? RuntimeEntityId.None;
                npcDefinitionId = record.NpcDefinitionId;
                defeated = record.Defeated;
                return true;
            }

            characterId = RuntimeEntityId.None;
            npcDefinitionId = default;
            defeated = false;
            return false;
        }

        /// <summary>Task 3.6: whether this session already knows about spawnId, whether from an earlier spawn
        /// this session, an earlier visit to its location, or an imported save/defeated tombstone. A location's
        /// bootstrap uses this to decide SpawnNpc (fresh, grants starting items) vs BindRestoredView (restore,
        /// grants nothing) for each of its authored spawn points -- the same fresh-vs-restored distinction
        /// SessionLoadCoordinator already draws for save-file restoration, reused here for ordinary in-session
        /// revisits.</summary>
        public bool HasRecord(string spawnId) => !string.IsNullOrEmpty(spawnId) && recordsBySpawnId.ContainsKey(spawnId);

        /// <summary>Task 3.6: the position/facing a spawn was last known to be at -- populated whenever a live
        /// agent exists (see SpawnRecord.Position), including the instant before ReleaseSpawnsExcept destroys
        /// its view. False only for a spawnId this session has no record of at all yet.</summary>
        public bool TryGetLastKnownTransform(string spawnId, out Vector3 position, out float facingYaw)
        {
            if (!string.IsNullOrEmpty(spawnId) && recordsBySpawnId.TryGetValue(spawnId, out var record))
            {
                position = record.Position;
                facingYaw = record.FacingYaw;
                return true;
            }

            position = Vector3.zero;
            facingYaw = 0f;
            return false;
        }

        /// <summary>Every CharacterAgent this service instantiated/bound, independent of Unity's enabled state
        /// -- the correct set to enumerate for teardown (Task 3.5). Unlike CharacterAgent.AllLive, a disabled
        /// agent stays in this collection.</summary>
        public IReadOnlyCollection<CharacterAgent> OwnedAgents => agentsByCharacter.Values;

        /// <summary>
        /// Builds and registers this NPC's CharacterState (via <see cref="NpcSpawnService.Spawn"/>, unchanged),
        /// records its faction, grants/equips its starting items, then instantiates its VisualPrefab and binds
        /// a <see cref="CharacterAgent"/> plus any <see cref="CharacterPlayableAnimator"/>/
        /// <see cref="EquipmentVisualBinder"/> found on it. Follows the state lifetime order design sec.4.2
        /// specifies: allocate entity id -> construct/register state -> establish inventory/faction
        /// associations -> instantiate/bind view. The only path that ever grants starting items -- restoring a
        /// saved character must never repeat that (see <see cref="BindRestoredView"/>).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="definition"/> has no VisualPrefab -- use <see cref="NpcSpawnService.Spawn"/>
        /// directly for a state-only NPC with no GameObject at all. Also thrown (wrapping the original
        /// failure) if binding fails partway; the character registration, any granted items, faction record,
        /// spawn record, and any partially-instantiated GameObject are all rolled back first, so a failed spawn
        /// never leaves a half-registered entity in the session. Also thrown for a <paramref name="spawnId"/>
        /// already used by this session (a save needs each spawn id to stay unique and stable).
        /// </exception>
        public CharacterAgent SpawnNpc(string spawnId, NpcDefinition definition, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(spawnId)) throw new ArgumentException("spawnId must not be null/empty.", nameof(spawnId));
            if (recordsBySpawnId.ContainsKey(spawnId)) throw new InvalidOperationException($"spawnId '{spawnId}' is already in use in this session.");
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!session.AcceptsCommands) throw new InvalidOperationException("Cannot spawn into a disposed session.");
            if (definition.VisualPrefab == null)
            {
                throw new InvalidOperationException(
                    $"'{definition.DisplayName}' has no VisualPrefab -- use NpcSpawnService.Spawn directly for a state-only NPC.");
            }

            var entityId = RuntimeEntityId.NewId();
            var character = NpcSpawnService.Spawn(definition, session.RulesDatabase, entityId);
            session.Characters.Register(character);
            recordsBySpawnId[spawnId] = new SpawnRecord { EntityId = entityId, NpcDefinitionId = definition.Id };

            var grantedItemIds = new List<RuntimeEntityId>();
            try
            {
                if (definition.Faction.IsValid) factionsByCharacter[entityId] = definition.Faction;
                GrantStartingItems(definition, entityId, grantedItemIds);

                return BindView(spawnId, entityId, definition.VisualPrefab, position, rotation, definition.Faction, startInactive: false);
            }
            catch (Exception ex)
            {
                recordsBySpawnId.Remove(spawnId);
                factionsByCharacter.Remove(entityId);
                foreach (var itemId in grantedItemIds) session.Inventory.RemoveItem(itemId);
                session.Characters.Unregister(entityId);

                throw new InvalidOperationException($"Failed to spawn '{definition.DisplayName}'; rolled back registration.", ex);
            }
        }

        /// <summary>
        /// Task 3.5: instantiates/binds a view onto an ALREADY-RESTORED character -- never constructs
        /// CharacterState, never grants items. This is the load path's counterpart to SpawnNpc's second half;
        /// calling SpawnNpc during a load would create a duplicate character with fresh stats and re-grant
        /// starting items, which is exactly the bug this split exists to avoid. <paramref name="characterId"/>
        /// must already be registered in this service's session (by SaveService.RestoreCharacters).
        /// <paramref name="npcDefinitionId"/> is DefinitionId.None for a party member (no defeat semantics, no
        /// definition-driven respawn -- its view is rebuilt straight from the caller-supplied prefab).
        /// <paramref name="startInactive"/> stages the instance deactivated (SetActive(false) immediately after
        /// Instantiate, before Bind) so it never participates in selection/input/physics until the caller
        /// explicitly activates it post-commit (SessionLoadCoordinator's build-then-commit transaction).
        /// <para>
        /// Task 3.6: also idempotent w.r.t. an ALREADY-KNOWN record for the same spawnId/character -- e.g.
        /// LocationInitializer rebinding a view after ReleaseSpawnsExcept destroyed it on an earlier visit to
        /// this same location, in the SAME WorldSpawnService/session (as opposed to SessionLoadCoordinator's
        /// use, always against a brand-new WorldSpawnService that has no records yet). A genuine conflict
        /// (spawnId already claimed by a DIFFERENT character) still throws.
        /// </para>
        /// </summary>
        public GameObject BindRestoredView(string spawnId, RuntimeEntityId characterId, DefinitionId npcDefinitionId, GameObject visualPrefab, Vector3 position, Quaternion rotation, DefinitionId faction, bool startInactive)
        {
            if (!session.Characters.TryGetCharacter(characterId, out _))
                throw new InvalidOperationException($"Cannot bind a view for character '{characterId}' -- it is not registered in this session (restore it first).");

            var insertedNewRecord = false;
            if (!string.IsNullOrEmpty(spawnId))
            {
                if (recordsBySpawnId.TryGetValue(spawnId, out var existingRecord))
                {
                    if (existingRecord.EntityId != characterId)
                        throw new InvalidOperationException($"spawnId '{spawnId}' is already in use in this session by a different character.");
                    // Already-known record for the SAME character -- rebinding a view, not importing a new
                    // record. Leave it (and its last-known transform) exactly as-is.
                }
                else
                {
                    recordsBySpawnId[spawnId] = new SpawnRecord { EntityId = characterId, NpcDefinitionId = npcDefinitionId };
                    insertedNewRecord = true;
                }
            }

            try
            {
                var agent = BindView(spawnId, characterId, visualPrefab, position, rotation, faction, startInactive);
                return agent.gameObject;
            }
            catch (Exception)
            {
                // Only undo the record if THIS call just inserted it -- a rebind failure onto an
                // already-known/pre-existing record must never erase that record (Task 3.6).
                if (insertedNewRecord) recordsBySpawnId.Remove(spawnId);
                throw;
            }
        }

        private CharacterAgent BindView(string spawnId, RuntimeEntityId entityId, GameObject visualPrefab, Vector3 position, Quaternion rotation, DefinitionId faction, bool startInactive)
        {
            if (visualPrefab == null) throw new InvalidOperationException($"No VisualPrefab available to bind a view for '{entityId}'.");

            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(visualPrefab, position, rotation);
                if (startInactive) instance.SetActive(false); // see BindRestoredView's remarks -- staged, not yet live

                if (GridNavigation != null)
                {
                    foreach (var gridMotor in instance.GetComponentsInChildren<GridCharacterMotor>(true))
                    {
                        gridMotor.Pathfinding = GridNavigation;
                        gridMotor.OccupantId = entityId;
                    }
                }

                var agent = instance.GetComponent<CharacterAgent>();
                if (agent == null) agent = instance.AddComponent<CharacterAgent>();
                agent.Bind(entityId, session);
                agentsByCharacter[entityId] = agent;

                var animator = instance.GetComponentInChildren<CharacterPlayableAnimator>(true);
                animator?.Initialize(entityId, session.EventBus);

                var visualEquipment = instance.GetComponentInChildren<EquipmentVisualBinder>(true);
                visualEquipment?.Initialize(entityId, session.RulesDatabase, session.Inventory, session.EventBus);

                var spriteEquipment = instance.GetComponentInChildren<SpriteEquipmentBinder>(true);
                spriteEquipment?.Initialize(entityId, session.RulesDatabase, session.Inventory, session.EventBus);

                var spriteAppearance = instance.GetComponentInChildren<SpriteAppearanceBinder>(true);
                spriteAppearance?.Initialize(entityId, session.Characters, session.RulesDatabase);

                var spriteCues = instance.GetComponentInChildren<SpriteActionCues>(true);
                spriteCues?.Initialize(entityId, session.EventBus, session.Characters);

                if (faction.IsValid) factionsByCharacter[entityId] = faction;

                return agent;
            }
            catch (Exception)
            {
                // Task 3.6: record cleanup is deliberately NOT done here -- it's each caller's own
                // responsibility (SpawnNpc's own catch; BindRestoredView's own catch, which must only remove a
                // record IT just inserted, never one that already existed before this call).
                agentsByCharacter.Remove(entityId);
                if (instance != null) DestroyGameObject(instance);
                throw;
            }
        }

        /// <summary>Real, permanent removal (e.g. a future CreatureDroppedToZeroHP handler) -- the ONLY API
        /// that sets a spawn's Defeated tombstone. Session-swap teardown destroys agents directly and discards
        /// the whole old WorldSpawnService instance; it must never call this, or replacing a session would be
        /// indistinguishable from every one of its NPCs actually dying (Task 3.5).</summary>
        public void MarkDefeated(RuntimeEntityId characterId)
        {
            foreach (var pair in recordsBySpawnId)
            {
                if (pair.Value.EntityId != characterId) continue;

                if (agentsByCharacter.TryGetValue(characterId, out var agent))
                {
                    // KNOWN LIMITATIONS of the deferred-defeat path (2D campaign Task 2.6) -- keep in mind if odd corpse/cleanup behaviour appears:
                    //  * This runs synchronously INSIDE the CreatureDied publish (TacticalEncounterController's handler), so without an
                    //    IDefeatPresentation the view is gone before any death cue could render. With one, the GameObject stays ALIVE
                    //    after this method returns and is destroyed later, from the presentation's own callback.
                    //  * There is NO timeout: a presentation that never calls onFinished leaves an inert GameObject in the scene forever.
                    //  * The corpse is no longer in agentsByCharacter, so a session swap (which destroys owned agents directly) does not
                    //    see it; it finishes and destroys itself. A scene unload destroys it regardless.
                    //  * "Inert" means: colliders off (not clickable/targetable), the IMovable mover disabled (its grid cell is freed at
                    //    once, although the corpse is still visible for a moment), the CharacterAgent component disabled (out of
                    //    CharacterAgent.AllLive). Anything else that scans the scene for GameObjects (a custom selectable source, ...) can
                    //    still see it until it is destroyed.
                    //  * If BeginDefeat throws we fall back to the original immediate destroy.
                    //  * Gameplay bookkeeping (tombstone, faction, agent unbind) is identical on both paths.
                    var view = agent.gameObject;
                    var defeatPresentation = view.GetComponentInChildren<IDefeatPresentation>(true);
                    if (defeatPresentation == null || !TryDeferDestroy(view, agent, defeatPresentation))
                    {
                        view.SetActive(false);
                        agent.Unbind();
                        DestroyGameObject(view);
                    }

                    agentsByCharacter.Remove(characterId);
                }

                pair.Value.EntityId = null;
                pair.Value.Defeated = true;
                factionsByCharacter.Remove(characterId);
                return;
            }
        }

        /// <summary>Makes the view inert (no colliders, no mover, out of CharacterAgent.AllLive), lets <paramref name="presentation"/> play,
        /// and destroys the GameObject when it reports back. Returns false, having destroyed nothing, if anything throws -- the caller then
        /// falls back to the immediate destroy.</summary>
        private static bool TryDeferDestroy(GameObject view, CharacterAgent agent, IDefeatPresentation presentation)
        {
            try
            {
                agent.Unbind();
                foreach (var collider in view.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var behaviour in view.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour is IMovable) behaviour.enabled = false; // GridCharacterMotor.OnDisable releases its cell
                foreach (var navAgent in view.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) navAgent.enabled = false;
                agent.enabled = false;

                var finished = false;
                presentation.BeginDefeat(() =>
                {
                    if (finished) return;
                    finished = true;
                    if (view != null) DestroyGameObject(view);
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        /// <summary>Task 3.6: releases every owned agent whose GameObject is in releaseScene -- destroys the
        /// view only, never the spawn record (the opposite of MarkDefeated, which tombstones the record and
        /// destroys the view). Used when a location scene unloads during travel: its NPCs' GameObjects go away,
        /// but CaptureSnapshot must keep reporting them (at their last-known position, see
        /// SpawnRecord.Position), and a later re-entry to the same location must rebind to the SAME record via
        /// BindRestoredView, not spawn a duplicate or re-grant starting items. Filtered by SCENE rather than by
        /// a party-members keep-set deliberately: by the time a travel calls this, the DESTINATION location's
        /// own NPCs have already been spawned/bound into agentsByCharacter too (LocationInitializer.Prepare ran
        /// against it first) -- a keep-set of "just the party" would incorrectly also release those brand-new
        /// agents, since they aren't party members either. Party members are never affected either way (they
        /// live in the persistent Core scene, never in releaseScene). Best-effort per agent: by the time this
        /// runs the party has already committed to the destination location, so a failure here is cleanup, not
        /// part of a reversible transaction.</summary>
        public void ReleaseSpawnsInScene(UnityEngine.SceneManagement.Scene releaseScene)
        {
            var toRelease = agentsByCharacter.Where(pair => pair.Value.gameObject.scene == releaseScene).Select(pair => pair.Key).ToList();
            foreach (var characterId in toRelease)
            {
                try
                {
                    var agent = agentsByCharacter[characterId];
                    foreach (var pair in recordsBySpawnId)
                    {
                        if (pair.Value.EntityId == characterId) UpdateRecordTransform(pair.Value, agent);
                    }

                    agent.gameObject.SetActive(false);
                    agent.Unbind();
                    DestroyGameObject(agent.gameObject);
                    agentsByCharacter.Remove(characterId);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"ReleaseSpawnsExcept: failed to release agent for '{characterId}': {ex}");
                }
            }
        }

        private static void UpdateRecordTransform(SpawnRecord record, CharacterAgent agent)
        {
            record.Position = agent.transform.position;
            record.FacingYaw = agent.transform.eulerAngles.y;
        }

        /// <summary>Task 3.6: true, full undo of a spawn this call itself just created -- unbinds/destroys the
        /// view AND removes the record entirely (unlike ReleaseSpawnsExcept, which keeps the record on purpose,
        /// and unlike MarkDefeated, which tombstones rather than erases). Used only by LocationInitializer to
        /// unwind a partially-succeeded Prepare() -- a spawn that was never really committed to the session
        /// should leave no trace, not report Defeated=true forever.</summary>
        public void Unspawn(string spawnId)
        {
            if (string.IsNullOrEmpty(spawnId) || !recordsBySpawnId.TryGetValue(spawnId, out var record)) return;

            if (record.EntityId is { } characterId)
            {
                if (agentsByCharacter.TryGetValue(characterId, out var agent))
                {
                    agent.gameObject.SetActive(false);
                    agent.Unbind();
                    DestroyGameObject(agent.gameObject);
                    agentsByCharacter.Remove(characterId);
                }

                factionsByCharacter.Remove(characterId);
                session.Characters.Unregister(characterId);
            }

            recordsBySpawnId.Remove(spawnId);
        }

        /// <summary>Registers a defeated spawn with no live GameObject -- used once per defeated
        /// WorldSpawnSnapshot while restoring a save, so a freshly-built WorldSpawnService starts with the
        /// correct tombstone set already in place (required for save -> load -> save -> load to keep reporting
        /// Defeated=true, Task 3.5).</summary>
        public void ImportDefeatedTombstone(string spawnId, DefinitionId npcDefinitionId)
        {
            if (string.IsNullOrEmpty(spawnId)) return; // a party member is never defeated this way
            recordsBySpawnId[spawnId] = new SpawnRecord { EntityId = null, NpcDefinitionId = npcDefinitionId, Defeated = true };
        }

        /// <summary>Assembles plain-data snapshots for SaveService.Capture -- CRPG.Saving must never reference
        /// CRPG.Gameplay/CharacterAgent (Task 3.5's assembly-cycle fix), so this is the one place that reads
        /// live transforms and converts them to WorldSpawnSnapshot before handing them across the boundary.
        /// FacingController is the sole writer of rotation and NavMeshAgent.updateRotation is false, so the
        /// GameObject's own transform is already the authoritative source -- nothing needed on CharacterAgent/
        /// MotionSnapshot to expose this.</summary>
        public IEnumerable<WorldSpawnSnapshot> CaptureSnapshot()
        {
            foreach (var pair in recordsBySpawnId)
            {
                var spawnId = pair.Key;
                var record = pair.Value;

                if (record.Defeated || record.EntityId == null)
                {
                    yield return new WorldSpawnSnapshot(spawnId, record.EntityId?.ToString() ?? string.Empty, record.NpcDefinitionId.Value, Vector3.zero, 0f, defeated: true);
                    continue;
                }

                var characterId = record.EntityId.Value;
                if (agentsByCharacter.TryGetValue(characterId, out var agent)) UpdateRecordTransform(record, agent);
                // else: this spawn's location is currently unloaded (Task 3.6, ReleaseSpawnsExcept) -- record
                // already holds its last-known transform from the moment its view was released, not the origin.

                yield return new WorldSpawnSnapshot(spawnId, characterId.ToString(), record.NpcDefinitionId.Value, record.Position, record.FacingYaw, defeated: false);
            }
        }

        /// <summary>UnityEngine.Object.Destroy() logs an error and refuses to actually destroy anything when
        /// called outside Play mode -- a real constraint that only surfaced once a test exercised a genuine
        /// rollback/despawn path in EditMode (this class's real runtime usage is always in Play mode, but its
        /// unit tests deliberately aren't, for speed). DestroyImmediate is the Editor-safe equivalent.</summary>
        private static void DestroyGameObject(GameObject go)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }

        private void GrantStartingItems(NpcDefinition definition, RuntimeEntityId character, List<RuntimeEntityId> grantedItemIds)
        {
            foreach (var itemDefinitionId in definition.StartingItemIds)
            {
                if (!session.RulesDatabase.TryResolve<ItemDefinition>(itemDefinitionId, out var itemDefinition)) continue;

                var item = new ItemInstance(RuntimeEntityId.NewId(), itemDefinitionId);
                session.Inventory.AddItem(item);
                grantedItemIds.Add(item.InstanceId);

                if (itemDefinition.IsEquippable) equipmentService.Equip(item.InstanceId, character, itemDefinition.EquipmentSlotId);
            }
        }
    }
}
