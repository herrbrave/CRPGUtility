using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
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
    /// </summary>
    public sealed class WorldSpawnService
    {
        private readonly GameSession session;
        private readonly EquipmentService equipmentService;
        private readonly Dictionary<RuntimeEntityId, DefinitionId> factionsByCharacter = new();

        public WorldSpawnService(GameSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            equipmentService = new EquipmentService(session.Characters, session.RulesDatabase, session.Inventory, session.EventBus);
        }

        /// <summary>The faction tag this spawn's NpcDefinition authored, if any. Deliberately not
        /// Combat.Combatant.Faction (that's a per-encounter concept -- CRPG.Combat.CombatEncounter.AddCombatant
        /// takes its own faction parameter when a fight actually starts); this is the standing, out-of-combat
        /// association a future AI/interaction system can query without holding onto the original
        /// NpcDefinition asset reference.</summary>
        public bool TryGetFaction(RuntimeEntityId character, out DefinitionId factionId) => factionsByCharacter.TryGetValue(character, out factionId);

        /// <summary>
        /// Builds and registers this NPC's CharacterState (via <see cref="NpcSpawnService.Spawn"/>, unchanged),
        /// records its faction, grants/equips its starting items, then instantiates its VisualPrefab and binds
        /// a <see cref="CharacterAgent"/> plus any <see cref="CharacterPlayableAnimator"/>/
        /// <see cref="CharacterVisualEquipment"/> found on it. Follows the state lifetime order design sec.4.2
        /// specifies: allocate entity id -> construct/register state -> establish inventory/faction
        /// associations -> instantiate/bind view.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="definition"/> has no VisualPrefab -- use <see cref="NpcSpawnService.Spawn"/>
        /// directly for a state-only NPC with no GameObject at all. Also thrown (wrapping the original
        /// failure) if binding fails partway; the character registration, any granted items, faction record,
        /// and any partially-instantiated GameObject are all rolled back first, so a failed spawn never leaves
        /// a half-registered entity in the session.
        /// </exception>
        public CharacterAgent SpawnNpc(NpcDefinition definition, Vector3 position, Quaternion rotation)
        {
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

            // Rollback below has no naturally-reachable trigger with today's real component set: Instantiate
            // always clones a fresh, never-yet-bound copy (non-[SerializeField] runtime fields don't survive
            // the clone), so CharacterAgent.Bind/CharacterPlayableAnimator.Initialize/CharacterVisualEquipment.
            // Initialize can never see an "already bound to something else" conflict here, and none of
            // GrantStartingItems' calls can throw for well-formed content. Kept anyway per this task's
            // explicit requirement ("roll back partial registration if required binding fails") -- it will
            // become reachable once Task 1.3+ adds real binding validation (rig-family checks, required
            // sockets/clips, ...) that CAN fail mid-spawn. Not covered by a dedicated failing-path unit test
            // for that reason; flagged as an unverified item rather than faked.
            var grantedItemIds = new List<RuntimeEntityId>();
            GameObject instance = null;
            try
            {
                if (definition.Faction.IsValid) factionsByCharacter[entityId] = definition.Faction;

                GrantStartingItems(definition, entityId, grantedItemIds);

                instance = UnityEngine.Object.Instantiate(definition.VisualPrefab, position, rotation);

                var agent = instance.GetComponent<CharacterAgent>();
                if (agent == null) agent = instance.AddComponent<CharacterAgent>();
                agent.Bind(entityId, session);

                var animator = instance.GetComponentInChildren<CharacterPlayableAnimator>(true);
                animator?.Initialize(entityId, session.EventBus);

                var visualEquipment = instance.GetComponentInChildren<EquipmentVisualBinder>(true);
                visualEquipment?.Initialize(entityId, session.RulesDatabase, session.Inventory, session.EventBus);

                return agent;
            }
            catch (Exception ex)
            {
                factionsByCharacter.Remove(entityId);
                foreach (var itemId in grantedItemIds) session.Inventory.RemoveItem(itemId);
                session.Characters.Unregister(entityId);
                if (instance != null) UnityEngine.Object.Destroy(instance);

                throw new InvalidOperationException($"Failed to spawn '{definition.DisplayName}'; rolled back registration.", ex);
            }
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
