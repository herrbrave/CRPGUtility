using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>
    /// Task 4.4: ticks <see cref="ConditionInstance.RemainingDuration"/> down at each condition's authored
    /// <see cref="DurationBoundary"/>. Self-subscribes to TurnStarted/TurnEnded on construction, same precedent
    /// as <see cref="Rules.SRD5.ConcentrationService"/>/<see cref="Rules.SRD5.DeathSaveService"/> -- neither of
    /// those has a Dispose/unsubscribe method either (a real, pre-existing, wider gap, not introduced here).
    /// Only active inside a running <see cref="CombatEncounterService.Encounter"/> -- out-of-combat duration
    /// ticking is out of scope, matching the design's confirmed turn-based-only tactical scope.
    /// </summary>
    /// <remarks>
    /// Semantics, each deliberately defined and tested rather than left ambiguous:
    /// <list type="bullet">
    /// <item>Zero/negative duration: RemainingDuration counts remaining boundary firings; decrement-then-check
    /// &lt;=0 removes it on the very same tick it reaches zero. An authored duration of 0 is a degenerate alias
    /// for 1 under this rule (both expire on the first firing).</item>
    /// <item>No immediate/same-turn tick: ticking is purely event-driven off real TurnStarted/TurnEnded
    /// publications, never synchronous at apply time -- a condition applied mid-turn only ever ticks on the
    /// next actual firing of its boundary's event.</item>
    /// <item>Source leaves combat/dies: a StartOfSourceTurn/EndOfSourceTurn condition whose source is removed
    /// from Encounter.Combatants becomes dormant (stops ticking, since that entity never fires another turn
    /// event) rather than being auto-removed -- a non-concentration effect outliving its dead source is
    /// correct SRD flavor (only concentration effects end on the caster's death; see ConcentrationService).</item>
    /// </list>
    /// </remarks>
    public sealed class ConditionDurationService
    {
        private readonly CombatEncounterService encounterService;
        private readonly ICharacterRegistry characters;
        private readonly IGameEventBus eventBus;
        private readonly ICombatLog combatLog;

        public ConditionDurationService(
            CombatEncounterService encounterService, ICharacterRegistry characters, IGameEventBus eventBus, ICombatLog combatLog = null)
        {
            this.encounterService = encounterService;
            this.characters = characters;
            this.eventBus = eventBus;
            this.combatLog = combatLog;

            eventBus.Subscribe(GameEvent.TurnStarted, context => Tick(context.Source, isStart: true));
            eventBus.Subscribe(GameEvent.TurnEnded, context => Tick(context.Source, isStart: false));
        }

        private void Tick(RuntimeEntityId firingEntity, bool isStart)
        {
            if (encounterService.Encounter == null) return;

            foreach (var combatant in encounterService.Encounter.Combatants)
            {
                if (!characters.TryGetCharacter(combatant.Id, out var character)) continue;

                // Decisions are made off a snapshot so a ConditionRemoved subscriber that itself applies or
                // removes a condition on this same character mid-sweep can never corrupt this iteration.
                var snapshot = character.Conditions.ToList();
                var expired = new List<ConditionInstance>();

                foreach (var condition in snapshot)
                {
                    if (!condition.RemainingDuration.HasValue) continue;
                    if (!Matches(condition, firingEntity, isStart)) continue;

                    condition.RemainingDuration--;
                    if (condition.RemainingDuration <= 0) expired.Add(condition);
                }

                if (expired.Count == 0) continue;

                foreach (var removed in character.RemoveConditions(c => expired.Contains(c)))
                {
                    combatLog?.Log(new ConditionLogEntry(removed.Source, removed.Target, removed.ConditionId, applied: false));
                    eventBus?.Publish(new GameEventContext(GameEvent.ConditionRemoved, removed.Source, removed.Target));
                }
            }
        }

        private static bool Matches(ConditionInstance condition, RuntimeEntityId firingEntity, bool isStart) =>
            condition.Boundary switch
            {
                DurationBoundary.StartOfTargetTurn => isStart && condition.Target == firingEntity,
                DurationBoundary.EndOfTargetTurn => !isStart && condition.Target == firingEntity,
                DurationBoundary.StartOfSourceTurn => isStart && condition.Source == firingEntity,
                DurationBoundary.EndOfSourceTurn => !isStart && condition.Source == firingEntity,
                _ => false,
            };
    }
}
