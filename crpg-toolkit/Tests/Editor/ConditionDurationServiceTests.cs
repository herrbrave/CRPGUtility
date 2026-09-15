using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class ConditionDurationServiceTests
    {
        private sealed class FixedInitiativeRule : IInitiativeRule
        {
            private readonly Dictionary<RuntimeEntityId, int> rolls;
            public FixedInitiativeRule(Dictionary<RuntimeEntityId, int> rolls) => this.rolls = rolls;

            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, rolls[combatant], true) }, 0);
        }

        /// <summary>Two combatants, A first. Returns the started service (already wired to the SAME event bus
        /// every test's ConditionDurationService must also use -- CombatEncounterService only ever publishes
        /// TurnStarted/TurnEnded on the bus it was constructed with) plus both ids.</summary>
        private static (CombatEncounterService Service, CharacterRegistry Registry, GameEventBus EventBus, RuntimeEntityId A, RuntimeEntityId B) StartTwoCombatantEncounter()
        {
            var registry = new CharacterRegistry();
            var a = new CharacterState(RuntimeEntityId.NewId());
            var b = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(a);
            registry.Register(b);

            var eventBus = new GameEventBus();
            var initiative = new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int> { [a.Id] = 20, [b.Id] = 10 });
            var service = new CombatEncounterService(registry, initiative, new TurnResourceSet(System.Array.Empty<ResourceDefinition>()), eventBus);
            service.StartCombat(new[] { a.Id, b.Id });

            return (service, registry, eventBus, a.Id, b.Id);
        }

        [Test]
        public void StartOfTargetTurnExpiresWhenTheTargetsTurnStarts()
        {
            var (service, registry, eventBus, a, b) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var conditionId = new DefinitionId("condition-frightened");
            characterA.ApplyCondition(new ConditionInstance(conditionId, b, a, remainingDuration: 1, boundary: DurationBoundary.StartOfTargetTurn));
            _ = new ConditionDurationService(service, registry, eventBus);

            service.EndTurn(); // TurnEnded(A), TurnStarted(B) -- doesn't match A's boundary
            Assert.IsTrue(characterA.HasCondition(conditionId), "Must not tick on B's turn.");

            service.EndTurn(); // TurnEnded(B), TurnStarted(A) -- the first real match

            Assert.IsFalse(characterA.HasCondition(conditionId));
        }

        [Test]
        public void EndOfTargetTurnExpiresWhenTheTargetsTurnEnds()
        {
            var (service, registry, eventBus, a, b) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var conditionId = new DefinitionId("condition-frightened");
            characterA.ApplyCondition(new ConditionInstance(conditionId, b, a, remainingDuration: 1, boundary: DurationBoundary.EndOfTargetTurn));
            _ = new ConditionDurationService(service, registry, eventBus);

            service.EndTurn(); // TurnEnded(A) -- matches immediately, A's turn was already in progress

            Assert.IsFalse(characterA.HasCondition(conditionId));
        }

        [Test]
        public void StartOfSourceTurnExpiresWhenTheSourcesTurnStartsRegardlessOfWhichCharacterHoldsTheCondition()
        {
            var (service, registry, eventBus, a, b) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA); // the condition lives on A's list...
            var conditionId = new DefinitionId("condition-bane");
            characterA.ApplyCondition(new ConditionInstance(conditionId, b, a, remainingDuration: 1, boundary: DurationBoundary.StartOfSourceTurn)); // ...but is keyed to B's (the source's) turn
            _ = new ConditionDurationService(service, registry, eventBus);

            service.EndTurn(); // TurnEnded(A), TurnStarted(B) -- matches B's turn starting

            Assert.IsFalse(characterA.HasCondition(conditionId));
        }

        [Test]
        public void EndOfSourceTurnExpiresWhenTheSourcesTurnEnds()
        {
            var (service, registry, eventBus, a, _) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var conditionId = new DefinitionId("condition-bane");
            characterA.ApplyCondition(new ConditionInstance(conditionId, a, a, remainingDuration: 1, boundary: DurationBoundary.EndOfSourceTurn)); // self-sourced, ends on A's own turn ending
            _ = new ConditionDurationService(service, registry, eventBus);

            service.EndTurn(); // TurnEnded(A) -- matches

            Assert.IsFalse(characterA.HasCondition(conditionId));
        }

        [Test]
        public void NoneBoundaryNeverTicksAcrossManyTurns()
        {
            var (service, registry, eventBus, a, _) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var conditionId = new DefinitionId("condition-inert");
            characterA.ApplyCondition(new ConditionInstance(conditionId, a, a, remainingDuration: 1)); // Boundary defaults to None
            _ = new ConditionDurationService(service, registry, eventBus);

            for (var i = 0; i < 6; i++) service.EndTurn();

            Assert.IsTrue(characterA.HasCondition(conditionId), "A None-boundary condition must never tick, preserving pre-Task-4.4 behavior.");
        }

        [Test]
        public void ZeroDurationIsADegenerateAliasForOneAndExpiresOnTheFirstMatchingFiring()
        {
            var (service, registry, eventBus, a, b) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var conditionId = new DefinitionId("condition-frightened");
            characterA.ApplyCondition(new ConditionInstance(conditionId, b, a, remainingDuration: 0, boundary: DurationBoundary.StartOfTargetTurn));
            _ = new ConditionDurationService(service, registry, eventBus);

            service.EndTurn(); // TurnStarted(B) -- no match yet
            service.EndTurn(); // TurnStarted(A) -- first real match

            Assert.IsFalse(characterA.HasCondition(conditionId));
        }

        [Test]
        public void MultiRoundConditionSurvivesTheFirstFiringAndExpiresOnTheSecond()
        {
            var (service, registry, eventBus, a, b) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var conditionId = new DefinitionId("condition-frightened");
            characterA.ApplyCondition(new ConditionInstance(conditionId, b, a, remainingDuration: 2, boundary: DurationBoundary.StartOfTargetTurn));
            _ = new ConditionDurationService(service, registry, eventBus);

            service.EndTurn(); // TurnStarted(B)
            service.EndTurn(); // TurnStarted(A) -- 1st firing: 2 -> 1
            Assert.IsTrue(characterA.HasCondition(conditionId), "Duration 2 must survive the first firing.");

            service.EndTurn(); // TurnStarted(B)
            service.EndTurn(); // TurnStarted(A) -- 2nd firing: 1 -> 0, removed

            Assert.IsFalse(characterA.HasCondition(conditionId));
        }

        [Test]
        public void SourceLeavingTheEncounterMakesASourceScopedConditionDormantRatherThanRemovingOrCrashing()
        {
            var (service, registry, eventBus, a, b) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var conditionId = new DefinitionId("condition-bane");
            // Sourced by B, ends on B's turn -- but B is about to leave the encounter entirely.
            characterA.ApplyCondition(new ConditionInstance(conditionId, b, a, remainingDuration: 1, boundary: DurationBoundary.EndOfSourceTurn));
            _ = new ConditionDurationService(service, registry, eventBus);

            service.Encounter.RemoveCombatant(b); // B fled/died -- never gets another turn event

            Assert.DoesNotThrow(() =>
            {
                for (var i = 0; i < 6; i++) service.EndTurn();
            });
            Assert.IsTrue(characterA.HasCondition(conditionId), "A source-scoped condition must go dormant, not vanish, when its source leaves the encounter.");
        }

        [Test]
        public void AConditionRemovedSubscriberThatMutatesConditionsMidSweepDoesNotCorruptTheSweep()
        {
            var (service, registry, eventBus, a, b) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var expiringId = new DefinitionId("condition-frightened");
            var reentrantId = new DefinitionId("condition-reentrant");
            characterA.ApplyCondition(new ConditionInstance(expiringId, b, a, remainingDuration: 1, boundary: DurationBoundary.StartOfTargetTurn));
            eventBus.Subscribe(GameEvent.ConditionRemoved, _ => characterA.ApplyCondition(new ConditionInstance(reentrantId, b, a)));
            _ = new ConditionDurationService(service, registry, eventBus);

            Assert.DoesNotThrow(() =>
            {
                service.EndTurn(); // TurnStarted(B)
                service.EndTurn(); // TurnStarted(A) -- expiringId removed, reentrant handler fires mid-sweep
            });

            Assert.IsFalse(characterA.HasCondition(expiringId));
            Assert.IsTrue(characterA.HasCondition(reentrantId));
        }

        [Test]
        public void NoEncounterInProgressIsANoOp()
        {
            var registry = new CharacterRegistry();
            var initiative = new FixedInitiativeRule(new Dictionary<RuntimeEntityId, int>());
            var service = new CombatEncounterService(registry, initiative, new TurnResourceSet(System.Array.Empty<ResourceDefinition>()));
            var eventBus = new GameEventBus();
            _ = new ConditionDurationService(service, registry, eventBus);

            // service.Encounter is still null -- StartCombat was never called.
            Assert.DoesNotThrow(() => eventBus.Publish(new GameEventContext(GameEvent.TurnStarted, RuntimeEntityId.NewId())));
        }

        [Test]
        public void DisposeStopsAnyFurtherTicking()
        {
            var (service, registry, eventBus, a, b) = StartTwoCombatantEncounter();
            registry.TryGetCharacter(a, out var characterA);
            var conditionId = new DefinitionId("condition-frightened");
            characterA.ApplyCondition(new ConditionInstance(conditionId, b, a, remainingDuration: 1, boundary: DurationBoundary.StartOfTargetTurn));
            var durations = new ConditionDurationService(service, registry, eventBus);

            durations.Dispose();

            service.EndTurn(); // TurnEnded(A), TurnStarted(B)
            service.EndTurn(); // TurnEnded(B), TurnStarted(A) -- would have expired it if still subscribed

            Assert.IsTrue(characterA.HasCondition(conditionId), "A disposed service must never tick again.");
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var (service, registry, eventBus, _, _) = StartTwoCombatantEncounter();
            var durations = new ConditionDurationService(service, registry, eventBus);

            durations.Dispose();

            Assert.DoesNotThrow(() => durations.Dispose());
        }
    }
}
