using System;
using CRPG.Abilities;
using CRPG.Core;
using CRPG.UI;

namespace CRPG.Gameplay.Combat
{
    /// <summary>
    /// Task 4.5: the one place player intent crosses from <see cref="CRPG.UI"/> (which must never reference
    /// <see cref="CRPG.Gameplay"/> -- the reverse reference already exists and reversing it would cycle) into
    /// real <see cref="ActionScheduler"/>/<see cref="AbilityTargetingGate"/> authority. Subscribes to
    /// <see cref="AbilityBarView"/>'s plain events and to <see cref="IGameEventBus"/>'s TurnStarted/TurnEnded
    /// to keep the ability bar showing whichever combatant currently belongs to <see cref="playerFaction"/>
    /// (and nothing when it's the AI's turn).
    /// </summary>
    public sealed class TacticalCombatUIBridge : IDisposable
    {
        private readonly TacticalEncounterController controller;
        private readonly AbilityTargetingGate targetingGate;
        private readonly AbilityBarView abilityBarView;
        private readonly IGameEventBus eventBus;
        private readonly DefinitionId playerFaction;
        private readonly Action<RuntimeEntityId> rollDeathSave;

        private readonly Action<GameEventContext> onTurnChanged;
        private bool disposed;

        /// <summary><paramref name="rollDeathSave"/> is a plain delegate, not a direct SRD5 service reference
        /// -- see <see cref="TacticalAiTurnDriver"/>'s own identical reasoning.</summary>
        public TacticalCombatUIBridge(
            TacticalEncounterController controller, AbilityTargetingGate targetingGate, AbilityBarView abilityBarView,
            IGameEventBus eventBus, DefinitionId playerFaction, Action<RuntimeEntityId> rollDeathSave = null)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.targetingGate = targetingGate ?? throw new ArgumentNullException(nameof(targetingGate));
            this.abilityBarView = abilityBarView ?? throw new ArgumentNullException(nameof(abilityBarView));
            this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            this.playerFaction = playerFaction;
            this.rollDeathSave = rollDeathSave;

            abilityBarView.AbilityChosen += OnAbilityChosen;
            abilityBarView.EndTurnRequested += OnEndTurnRequested;
            abilityBarView.RollDeathSaveRequested += OnRollDeathSaveRequested;

            onTurnChanged = _ => RefreshCurrentCharacter();
            eventBus.Subscribe(GameEvent.CombatStarted, onTurnChanged);
            eventBus.Subscribe(GameEvent.TurnStarted, onTurnChanged);
            eventBus.Subscribe(GameEvent.TurnEnded, onTurnChanged);
            eventBus.Subscribe(GameEvent.CombatEnded, onTurnChanged);

            RefreshCurrentCharacter();
        }

        private void OnAbilityChosen(RuntimeEntityId source, AbilityDefinition ability) => targetingGate.Arm(source, ability);

        private void OnEndTurnRequested()
        {
            if (controller.Scheduler.IsBusy) return; // matches the review's explicit "block End Turn while an action is active"
            if (controller.EncounterService.Encounter?.CurrentTurn == null) return;

            controller.EncounterService.EndTurn();
        }

        private void OnRollDeathSaveRequested(RuntimeEntityId character) => rollDeathSave?.Invoke(character);

        private void RefreshCurrentCharacter()
        {
            var current = controller.EncounterService.Encounter?.CurrentTurn;
            if (current != null && current.Faction == playerFaction) abilityBarView.SetCurrentCharacter(current.Id);
            else abilityBarView.ClearCurrentCharacter();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            abilityBarView.AbilityChosen -= OnAbilityChosen;
            abilityBarView.EndTurnRequested -= OnEndTurnRequested;
            abilityBarView.RollDeathSaveRequested -= OnRollDeathSaveRequested;

            eventBus.Unsubscribe(GameEvent.CombatStarted, onTurnChanged);
            eventBus.Unsubscribe(GameEvent.TurnStarted, onTurnChanged);
            eventBus.Unsubscribe(GameEvent.TurnEnded, onTurnChanged);
            eventBus.Unsubscribe(GameEvent.CombatEnded, onTurnChanged);
        }
    }
}
