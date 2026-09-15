using System;
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
    /// those has a Dispose/unsubscribe method either (a real, pre-existing, wider gap; this class fixes it for
    /// itself in Task 4.5, once a real caller -- <c>CRPG.Gameplay.Combat.TacticalEncounterController</c> --
    /// needed to construct a fresh instance per encounter without leaking the previous one's subscriptions).
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
    public sealed class ConditionDurationService : IDisposable
    {
        private readonly CombatEncounterService encounterService;
        private readonly ICharacterRegistry characters;
        private readonly IGameEventBus eventBus;
        private readonly ICombatLog combatLog;

        // Stored so Dispose() can unsubscribe the EXACT delegate instances that were subscribed -- a
        // freshly-allocated lambda with equivalent behavior does not equal the original for IGameEventBus's
        // delegate-subtraction-based Unsubscribe.
        private readonly Action<GameEventContext> onTurnStarted;
        private readonly Action<GameEventContext> onTurnEnded;
        private bool disposed;

        public ConditionDurationService(
            CombatEncounterService encounterService, ICharacterRegistry characters, IGameEventBus eventBus, ICombatLog combatLog = null)
        {
            this.encounterService = encounterService;
            this.characters = characters;
            this.eventBus = eventBus;
            this.combatLog = combatLog;

            onTurnStarted = context => Tick(context.Source, isStart: true);
            onTurnEnded = context => Tick(context.Source, isStart: false);
            eventBus.Subscribe(GameEvent.TurnStarted, onTurnStarted);
            eventBus.Subscribe(GameEvent.TurnEnded, onTurnEnded);
        }

        /// <summary>Unsubscribes from the shared event bus -- idempotent, safe to call more than once. Once
        /// disposed, this instance permanently stops ticking any duration (a restarted encounter must construct
        /// a fresh <see cref="ConditionDurationService"/>, never reuse a disposed one).</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            eventBus.Unsubscribe(GameEvent.TurnStarted, onTurnStarted);
            eventBus.Unsubscribe(GameEvent.TurnEnded, onTurnEnded);
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
