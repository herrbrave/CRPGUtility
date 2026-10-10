using System.Collections;
using System.Collections.Generic;
using CRPG.AI;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Actions;
using CRPG.Gameplay.Combat;
using CRPG.Navigation;
using CRPG.Saving;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>Task 4.5: <see cref="TacticalEncounterController"/> against a real <see cref="GameSession"/>/
    /// <see cref="WorldSpawnService"/>/<see cref="CharacterAgent"/> pair -- the bridge nothing before this task
    /// exercised outside a demo bootstrap. Deliberately does not need a baked NavMesh (no test here issues a
    /// Move decision) -- that combination is already proven by <see cref="MoveActionExecutionTests"/> and by
    /// <see cref="TacticalAiTurnDriverTests"/>' own Move-decision coverage below.</summary>
    public class TacticalEncounterControllerTests
    {
        private static readonly DefinitionId HeroesFaction = new("faction-heroes-tec");
        private static readonly DefinitionId GoblinsFaction = new("faction-goblins-tec");

        private readonly List<GameObject> spawned = new();
        private GameSession session;
        private WorldSpawnService worldSpawnService;
        private ResourceDefinition actionResource;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            worldSpawnService = new WorldSpawnService(session);
            actionResource = TacticalTestFactory.CreateResourceDefinition("resource-action-tec", "Action");
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        /// <summary>A bare, state-only combatant: real CharacterState registered in the real session, real
        /// CharacterAgent bound via WorldSpawnService.BindRestoredView (same production call
        /// AdventureSliceBootstrap.SpawnParty itself uses) -- no Motor/View/Animator, since none of these
        /// tests exercise movement or presentation.</summary>
        private RuntimeEntityId SpawnCombatant(string spawnId, DefinitionId faction)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(actionResource.Id, max: 1));
            session.Characters.Register(character);

            var template = new GameObject($"Combatant-{spawnId}");
            var instance = worldSpawnService.BindRestoredView(spawnId, character.Id, default, template, Vector3.zero, Quaternion.identity, faction, startInactive: false);
            Object.DestroyImmediate(template);
            spawned.Add(instance);

            return character.Id;
        }

        private TacticalEncounterController CreateController(params (RuntimeEntityId Id, DefinitionId Faction)[] combatants)
        {
            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus);
            var positions = new TestPositionProvider();
            foreach (var c in combatants) positions.Set(c.Id, Vector3.zero);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var turnResources = new TurnResourceSet(new[] { actionResource });
            var encounterService = new CombatEncounterService(session.Characters, new FixedInitiativeRule(), turnResources, session.EventBus);

            return new TacticalEncounterController(
                session.Characters, session.EventBus, positions, abilityResolution, movementResolution,
                new NavMeshService(), encounterService, worldSpawnService, combatants);
        }

        private sealed class FixedInitiativeRule : IInitiativeRule
        {
            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, 10, true) }, 0);
        }

        [Test]
        public void StartCombatTagsEachCombatantWithItsRealPerEncounterFaction()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);
            var controller = CreateController((hero, HeroesFaction), (goblin, GoblinsFaction));
            var provider = new EncounterFactionProvider(controller.EncounterService);

            Assert.IsTrue(provider.TryGetFaction(hero, out var heroFaction));
            Assert.AreEqual(HeroesFaction, heroFaction);
            Assert.IsTrue(provider.TryGetFaction(goblin, out var goblinFaction));
            Assert.AreEqual(GoblinsFaction, goblinFaction);

            controller.Dispose();
        }

        /// <summary>Task 5.2 gap fix: a ruleset with no IInitiativeRule at all (e.g. Fate Condensed's elective
        /// turn order) can still drive a TacticalEncounterController, via useExplicitTurnOrder.</summary>
        [Test]
        public void UseExplicitTurnOrderDrivesTheControllerWithoutAnyInitiativeRule()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);

            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus);
            var positions = new TestPositionProvider();
            positions.Set(hero, Vector3.zero);
            positions.Set(goblin, Vector3.zero);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var turnResources = new TurnResourceSet(new[] { actionResource });
            var encounterService = new CombatEncounterService(session.Characters, initiativeRule: null, turnResources, session.EventBus);

            var combatants = new[] { (goblin, GoblinsFaction), (hero, HeroesFaction) };
            var controller = new TacticalEncounterController(
                session.Characters, session.EventBus, positions, abilityResolution, movementResolution,
                new NavMeshService(), encounterService, worldSpawnService, combatants, useExplicitTurnOrder: true);

            Assert.AreEqual(goblin, controller.EncounterService.Encounter.CurrentTurn.Id, "The first entry in the given order must go first.");

            controller.Dispose();
        }

        [Test]
        public void OnlyTheCurrentTurnCombatantCanStartAnActionThroughTheSharedScheduler()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);
            var controller = CreateController((hero, HeroesFaction), (goblin, GoblinsFaction));
            var ability = TacticalTestFactory.CreateAbility("ability-tec-noop", actionResource, 1, 0f, 0f);

            Assert.AreEqual(hero, controller.EncounterService.Encounter.CurrentTurn.Id, "FixedInitiativeRule ties -- first-added wins the tie in Combatant.Initiative sort stability.");

            var rejected = controller.Scheduler.StartAbility(goblin, ability, new[] { hero });
            Assert.IsNull(rejected, "Not goblin's turn -- the shared scheduler must reject it exactly like it would for the player.");

            var accepted = controller.Scheduler.StartAbility(hero, ability, new[] { goblin });
            Assert.IsNotNull(accepted);
            Assert.AreEqual(ActionPhase.Preparing, accepted.Phase, "0-second Prepare only actually commits on the next Tick, never inside the constructor.");
            controller.Tick(0.1f);
            Assert.AreEqual(ActionOutcome.Succeeded, accepted.Outcome);

            controller.Dispose();
        }

        [Test]
        public void CreatureDiedMarksTheSpawnDefeatedAndRemovesItFromTheEncounter()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);
            var controller = CreateController((hero, HeroesFaction), (goblin, GoblinsFaction));
            var killAbility = TacticalTestFactory.CreateAbility(
                "ability-tec-kill", actionResource, 1, 0f, 0f, effects: new AbilityEffect[] { new PublishCreatureDiedEffect() });

            controller.Scheduler.StartAbility(hero, killAbility, new[] { goblin });
            controller.Tick(0.1f); // 0-second Prepare/Recover -- one Tick fully commits and runs the effect

            Assert.IsTrue(worldSpawnService.TryGetCharacterId("goblin", out _, out _, out var defeated));
            Assert.IsTrue(defeated);
            Assert.IsFalse(EncounterContains(controller, goblin));

            controller.Dispose();
        }

        [Test]
        public void CreatureDroppedToZeroHPAloneNeverDefeatsOrRemovesTheCombatant()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);
            var controller = CreateController((hero, HeroesFaction), (goblin, GoblinsFaction));

            session.EventBus.Publish(new GameEventContext(GameEvent.CreatureDroppedToZeroHP, hero, goblin));

            Assert.IsTrue(worldSpawnService.TryGetCharacterId("goblin", out _, out _, out var defeated));
            Assert.IsFalse(defeated, "Unconscious-but-stable must never be treated as defeated.");
            Assert.IsTrue(EncounterContains(controller, goblin), "Must stay live/targetable in the encounter.");

            controller.Dispose();
        }

        /// <summary>The seam a ruleset without death uses (Fate's "Taken Out"): the same teardown a real CreatureDied gets, with no CreatureDied published.</summary>
        [Test]
        public void DefeatMarksTheSpawnDefeatedAndRemovesItFromTheEncounterWithoutPublishingCreatureDied()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);
            var controller = CreateController((hero, HeroesFaction), (goblin, GoblinsFaction));
            var died = 0;
            session.EventBus.Subscribe(GameEvent.CreatureDied, _ => died++);

            controller.Defeat(goblin);

            Assert.IsTrue(worldSpawnService.TryGetCharacterId("goblin", out _, out _, out var defeated));
            Assert.IsTrue(defeated);
            Assert.IsFalse(EncounterContains(controller, goblin));
            Assert.IsTrue(EncounterContains(controller, hero), "only the defeated combatant leaves");
            Assert.AreEqual(0, died, "non-lethal: nobody died");

            controller.Dispose();
        }

        [Test]
        public void DefeatingTheSameCombatantTwiceIsHarmless()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);
            var controller = CreateController((hero, HeroesFaction), (goblin, GoblinsFaction));

            controller.Defeat(goblin);
            Assert.DoesNotThrow(() => controller.Defeat(goblin));

            Assert.IsFalse(EncounterContains(controller, goblin));
            controller.Dispose();
        }

        [Test]
        public void DefeatAfterDisposeDoesNothing()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);
            var controller = CreateController((hero, HeroesFaction), (goblin, GoblinsFaction));
            var encounter = controller.EncounterService.Encounter;
            controller.Dispose();

            controller.Defeat(goblin);

            Assert.IsTrue(worldSpawnService.TryGetCharacterId("goblin", out _, out _, out var defeated));
            Assert.IsFalse(defeated, "a disposed controller no longer owns the encounter");
            Assert.IsNotNull(encounter);
        }

        [Test]
        public void TheStaticDefeatDoesTheSameForACallerThatHasNoController()
        {
            var hero = SpawnCombatant("hero", HeroesFaction);
            var goblin = SpawnCombatant("goblin", GoblinsFaction);
            var controller = CreateController((hero, HeroesFaction), (goblin, GoblinsFaction));

            TacticalEncounterController.Defeat(worldSpawnService, controller.EncounterService, goblin);

            Assert.IsTrue(worldSpawnService.TryGetCharacterId("goblin", out _, out _, out var defeated));
            Assert.IsTrue(defeated);
            Assert.IsFalse(EncounterContains(controller, goblin));
            Assert.Throws<System.ArgumentNullException>(() => TacticalEncounterController.Defeat(null, controller.EncounterService, goblin));

            controller.Dispose();
        }

        private static bool EncounterContains(TacticalEncounterController controller, RuntimeEntityId id)
        {
            foreach (var combatant in controller.EncounterService.Encounter.Combatants)
            {
                if (combatant.Id == id) return true;
            }

            return false;
        }

        /// <summary>Test-only effect publishing real CreatureDied directly -- exercises
        /// TacticalEncounterController's own teardown wiring without needing a full HP/DamageEffect/
        /// DeathSaveService pipeline, which is SRD-specific and out of scope for this generic-layer test.</summary>
        private sealed class PublishCreatureDiedEffect : AbilityEffect
        {
            public override void Apply(AbilityExecutionContext context)
            {
                foreach (var targetId in context.Targets)
                    context.EventBus?.Publish(new GameEventContext(GameEvent.CreatureDied, context.Source, targetId));
            }
        }
    }

    /// <summary>Task 4.5: <see cref="TacticalAiTurnDriver"/>'s decide -&gt; execute -&gt; reassess -&gt; decide-again
    /// loop, against the same real controller/session fixture as <see cref="TacticalEncounterControllerTests"/>.</summary>
    public class TacticalAiTurnDriverTests
    {
        private static readonly DefinitionId GoblinsFaction = new("faction-goblins-taitd");

        private readonly List<GameObject> spawned = new();
        private GameSession session;
        private WorldSpawnService worldSpawnService;
        private ResourceDefinition actionResource;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            worldSpawnService = new WorldSpawnService(session);
            actionResource = TacticalTestFactory.CreateResourceDefinition("resource-action-taitd", "Action");
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        private RuntimeEntityId SpawnCombatant(string spawnId, DefinitionId faction, ResourceState resource)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            if (resource != null) character.AddResource(resource);
            session.Characters.Register(character);

            var template = new GameObject($"Combatant-{spawnId}");
            var instance = worldSpawnService.BindRestoredView(spawnId, character.Id, default, template, Vector3.zero, Quaternion.identity, faction, startInactive: false);
            Object.DestroyImmediate(template);
            spawned.Add(instance);

            return character.Id;
        }

        private sealed class FixedInitiativeRule : IInitiativeRule
        {
            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, 10, true) }, 0);
        }

        /// <summary>Returns a decision exactly once per distinct target (in order), then null -- lets a test
        /// prove "keeps deciding until nothing legal/useful remains," not just "one decision, then stop."</summary>
        private sealed class QueuedDecisionProvider : ITurnDecisionProvider
        {
            private readonly Queue<TurnDecision> decisions;
            public int CallCount { get; private set; }

            public QueuedDecisionProvider(params TurnDecision[] decisions) => this.decisions = new Queue<TurnDecision>(decisions);

            public TurnDecision GetDecision(TurnContext context)
            {
                CallCount++;
                return decisions.Count > 0 ? decisions.Dequeue() : null;
            }
        }

        private sealed class AlwaysSameDecisionProvider : ITurnDecisionProvider
        {
            private readonly TurnDecision decision;
            public int CallCount { get; private set; }
            public AlwaysSameDecisionProvider(TurnDecision decision) => this.decision = decision;

            public TurnDecision GetDecision(TurnContext context)
            {
                CallCount++;
                return decision;
            }
        }

        private (TacticalEncounterController Controller, RuntimeEntityId Goblin, RuntimeEntityId Hero) CreateGoblinVsHero(int goblinActionMax = 3)
        {
            var goblin = SpawnCombatant("goblin", GoblinsFaction, new ResourceState(actionResource.Id, max: goblinActionMax));
            var hero = SpawnCombatant("hero", new DefinitionId("faction-heroes-taitd"), new ResourceState(actionResource.Id, max: 1));

            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus);
            var positions = new TestPositionProvider();
            positions.Set(goblin, Vector3.zero);
            positions.Set(hero, Vector3.zero);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var turnResources = new TurnResourceSet(new[] { actionResource });
            var encounterService = new CombatEncounterService(session.Characters, new FixedInitiativeRule(), turnResources, session.EventBus);

            var controller = new TacticalEncounterController(
                session.Characters, session.EventBus, positions, abilityResolution, movementResolution,
                new NavMeshService(), encounterService, worldSpawnService,
                new[] { (goblin, GoblinsFaction), (hero, new DefinitionId("faction-heroes-taitd")) });

            return (controller, goblin, hero);
        }

        [Test]
        public void KeepsDecidingWithinOneTurnUntilTheBrainReturnsNull()
        {
            var (controller, goblin, hero) = CreateGoblinVsHero(goblinActionMax: 3);
            var ability = TacticalTestFactory.CreateAbility("ability-taitd-noop", actionResource, 1, 0f, 0f);
            var brain = new QueuedDecisionProvider(
                new TurnDecision(ability, new[] { hero }, 1f),
                new TurnDecision(ability, new[] { hero }, 1f),
                new TurnDecision(ability, new[] { hero }, 1f));
            var driver = new TacticalAiTurnDriver(controller, session.Characters, brain, GoblinsFaction);

            // Each cycle: driver decides+starts (if not already busy), then controller.Tick actually commits
            // the 0-second-Prepare/Recover action to completion within that same call -- exactly what the
            // real bootstrap's Update() does by ticking both every frame. 4 cycles: 3 real decisions each
            // fully resolving, then a 4th cycle where the brain returns null and ends the turn.
            for (var i = 0; i < 4; i++)
            {
                driver.Tick(0.1f);
                controller.Tick(0.1f);
            }

            Assert.AreEqual(4, brain.CallCount, "3 successful decisions, then one more call returning null.");
            session.Characters.TryGetCharacter(goblin, out var goblinState);
            goblinState.TryGetResource(actionResource.Id, out var action);
            Assert.AreEqual(0, action.Current, "All 3 affordable actions must actually have been spent, not just decided.");

            controller.Dispose();
        }

        [Test]
        public void EndsTheTurnAssoonAsTheBrainReturnsNull()
        {
            var (controller, _, hero) = CreateGoblinVsHero(goblinActionMax: 1);
            var brain = new QueuedDecisionProvider(); // null on the very first call
            var driver = new TacticalAiTurnDriver(controller, session.Characters, brain, GoblinsFaction);

            driver.Tick(0.1f);

            Assert.AreNotEqual(new DefinitionId("faction-goblins-taitd"), controller.EncounterService.Encounter.CurrentTurn?.Faction);

            controller.Dispose();
        }

        [Test]
        public void ABoundedGuardStopsARepeatedlyRejectedDecisionFromSpinningForever()
        {
            var (controller, _, hero) = CreateGoblinVsHero(goblinActionMax: 1);
            // A "use an ability that doesn't exist on this character's turn" style decision that always starts
            // fine at the scheduler level is hard to construct as ALWAYS-rejected without an encounter that's
            // already busy -- instead, directly prove the bound by having the brain keep returning a decision
            // whose ability costs a resource the goblin doesn't have, so Execute (and therefore the
            // ActionScheduler-level start) keeps failing at commit time every single call.
            var expensiveAbility = TacticalTestFactory.CreateAbility("ability-taitd-too-costly", actionResource, 99, 0f, 0f);
            var brain = new AlwaysSameDecisionProvider(new TurnDecision(expensiveAbility, new[] { hero }, 1f));
            var driver = new TacticalAiTurnDriver(controller, session.Characters, brain, GoblinsFaction);

            for (var i = 0; i < 20; i++)
            {
                driver.Tick(0.1f);
                controller.Tick(0.1f); // resolves each unaffordable attempt to Rejected so the next cycle can decide again
            }

            Assert.Less(brain.CallCount, 20, "Must stop calling the brain once the bounded decisions-per-turn guard trips, not loop forever.");

            controller.Dispose();
        }
    }

    /// <summary>Task 4.5: <see cref="AbilityTargetingGate"/>, the first real <see cref="IPointerTargetingGate"/>
    /// implementation. The unarmed/empty-space/non-agent paths need no live scene at all; the "real target hit"
    /// path needs one real collider-bearing CharacterAgent, built the same minimal way as the other tactical
    /// fixtures in this file.</summary>
    public class AbilityTargetingGateTests
    {
        private readonly List<GameObject> spawned = new();
        private GameSession session;
        private WorldSpawnService worldSpawnService;
        private ResourceDefinition actionResource;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            worldSpawnService = new WorldSpawnService(session);
            actionResource = TacticalTestFactory.CreateResourceDefinition("resource-action-atg", "Action");
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        private RuntimeEntityId SpawnTargetableCombatant(Vector3 position)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(actionResource.Id, max: 5));
            session.Characters.Register(character);

            var template = new GameObject("TargetableCombatant");
            template.AddComponent<SphereCollider>();
            var instance = worldSpawnService.BindRestoredView(character.Id.ToString(), character.Id, default, template, position, Quaternion.identity, default, startInactive: false);
            Object.DestroyImmediate(template);
            spawned.Add(instance);

            return character.Id;
        }

        [Test]
        public void FallsThroughWhenNothingIsArmed()
        {
            var gate = new AbilityTargetingGate(scheduler: null, abilityResolution: null, hittableMask: ~0);
            Assert.IsFalse(gate.TryConsume(new Ray(Vector3.up * 10f, Vector3.down)));
        }

        [Test]
        public void ClickingEmptySpaceWhileArmedDisarmsAndStillConsumesTheClick()
        {
            var gate = new AbilityTargetingGate(scheduler: null, abilityResolution: null, hittableMask: ~0);
            var ability = TacticalTestFactory.CreateAbility("ability-atg-noop-1", actionResource, 1, 0f, 0f);
            gate.Arm(RuntimeEntityId.NewId(), ability);

            var consumed = gate.TryConsume(new Ray(new Vector3(500f, 500f, 500f), Vector3.up));

            Assert.IsTrue(consumed, "Every click while armed must be consumed, even one that lands on nothing.");
            Assert.IsFalse(gate.IsArmed, "A click on empty space cancels targeting.");
        }

        [Test]
        public void HittingARealCharacterAgentStartsTheAbilityThroughTheSchedulerAndDisarms()
        {
            var target = SpawnTargetableCombatant(Vector3.zero);
            var positions = new TestPositionProvider();
            positions.Set(target, Vector3.zero);
            var source = RuntimeEntityId.NewId();
            var sourceState = new CharacterState(source);
            sourceState.AddResource(new ResourceState(actionResource.Id, max: 5));
            session.Characters.Register(sourceState);

            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus, positions: positions);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), session.Characters, positions);
            var ability = TacticalTestFactory.CreateAbility("ability-atg-noop-2", actionResource, 1, 0f, 0f);

            var gate = new AbilityTargetingGate(scheduler, abilityResolution, hittableMask: ~0);
            gate.Arm(source, ability);

            var ray = new Ray(new Vector3(0f, 10f, 0f), Vector3.down);
            var consumed = gate.TryConsume(ray);

            Assert.IsTrue(consumed);
            Assert.IsFalse(gate.IsArmed, "A successfully-started ability disarms -- targeting is one-shot.");
            Assert.IsTrue(scheduler.IsBusy, "The real scheduler must now own an in-flight action for the source.");
        }

        [Test]
        public void AnInvalidTargetStaysArmedForAnotherAttempt()
        {
            var target = SpawnTargetableCombatant(Vector3.zero);
            var positions = new TestPositionProvider();
            positions.Set(target, Vector3.zero);
            var source = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(source)); // no Action resource granted -- Validate must reject on cost

            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus, positions: positions);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var scheduler = new ActionScheduler(abilityResolution, movementResolution, new NavMeshService(), session.Characters, positions);
            var ability = TacticalTestFactory.CreateAbility("ability-atg-noop-3", actionResource, 1, 0f, 0f);

            var gate = new AbilityTargetingGate(scheduler, abilityResolution, hittableMask: ~0);
            gate.Arm(source, ability);

            var consumed = gate.TryConsume(new Ray(new Vector3(0f, 10f, 0f), Vector3.down));

            Assert.IsTrue(consumed);
            Assert.IsTrue(gate.IsArmed, "An invalid target must not cancel targeting -- the player can just try again.");
            Assert.IsFalse(scheduler.IsBusy, "Nothing should have actually started.");
        }
    }

    /// <summary>Task 4.5: <see cref="TacticalPlayerMoveAdapter"/> -- player click-to-move must go through the
    /// real scheduler and only ever move the current turn's own combatant.</summary>
    public class TacticalPlayerMoveAdapterTests
    {
        private static readonly DefinitionId PlayerFaction = new("faction-heroes-tpma");
        private static readonly DefinitionId AiFaction = new("faction-goblins-tpma");

        private readonly List<GameObject> spawned = new();
        private GameSession session;
        private WorldSpawnService worldSpawnService;
        private ResourceDefinition actionResource;
        private ResourceDefinition movementResource;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            worldSpawnService = new WorldSpawnService(session);
            actionResource = TacticalTestFactory.CreateResourceDefinition("resource-action-tpma", "Action");
            movementResource = TacticalTestFactory.CreateResourceDefinition("resource-movement-tpma", "Movement");
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        private sealed class FixedInitiativeRule : IInitiativeRule
        {
            private readonly RuntimeEntityId first;
            public FixedInitiativeRule(RuntimeEntityId first) => this.first = first;

            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, combatant == first ? 20 : 10, true) }, 0);
        }

        /// <summary>Records whether SetDestination was ever called -- the cheapest possible way to prove
        /// "the adapter actually reached into the scheduler for THIS combatant's motor" without needing a
        /// real baked NavMesh (already covered by MoveActionExecutionTests).</summary>
        private sealed class RecordingMovable : MonoBehaviour, IMovable
        {
            public bool WasAskedToMove { get; private set; }

            public MoveResult SetDestination(Vector3 destination)
            {
                WasAskedToMove = true;
                return new MoveResult(MoveStatus.Pending, MoveRequestId.NewId());
            }

            public void Stop() { }
            public bool Warp(Vector3 position) => true;
            public MotionSnapshot Snapshot => MotionSnapshot.None;
        }

        private (RuntimeEntityId Id, RecordingMovable Motor) SpawnCombatant(string spawnId, DefinitionId faction)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(movementResource.Id, max: 10));
            session.Characters.Register(character);

            var template = new GameObject($"Combatant-{spawnId}");
            template.AddComponent<RecordingMovable>();
            var instance = worldSpawnService.BindRestoredView(spawnId, character.Id, default, template, Vector3.zero, Quaternion.identity, faction, startInactive: false);
            Object.DestroyImmediate(template);
            spawned.Add(instance);

            return (character.Id, instance.GetComponent<RecordingMovable>());
        }

        [Test]
        public void AClickDuringAnotherFactionsTurnIsSilentlyIgnored()
        {
            var (hero, heroMotor) = SpawnCombatant("hero", PlayerFaction);
            var (goblin, _) = SpawnCombatant("goblin", AiFaction);
            var positions = new TestPositionProvider();
            positions.Set(hero, Vector3.zero);
            positions.Set(goblin, Vector3.zero);
            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var turnResources = new TurnResourceSet(new[] { actionResource });
            // Goblin acts first -- it is NOT the player's turn.
            var encounterService = new CombatEncounterService(session.Characters, new FixedInitiativeRule(goblin), turnResources, session.EventBus);
            var controller = new TacticalEncounterController(
                session.Characters, session.EventBus, positions, abilityResolution, movementResolution,
                new NavMeshService(), encounterService, worldSpawnService, new[] { (goblin, AiFaction), (hero, PlayerFaction) });
            var adapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, PlayerFaction);

            var result = adapter.TryHandleMove(new MoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward));

            Assert.IsNull(result, "A click while it's the AI faction's turn must never reach ActionScheduler.StartMove at all.");
            Assert.IsFalse(heroMotor.WasAskedToMove);
            Assert.IsFalse(controller.Scheduler.IsBusy);

            controller.Dispose();
        }

        [Test]
        public void AClickDuringThePlayersTurnMovesOnlyTheCurrentTurnCombatantThroughTheScheduler()
        {
            var (hero, heroMotor) = SpawnCombatant("hero", PlayerFaction);
            var (goblin, goblinMotor) = SpawnCombatant("goblin", AiFaction);
            var positions = new TestPositionProvider();
            positions.Set(hero, Vector3.zero);
            positions.Set(goblin, Vector3.zero);
            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var turnResources = new TurnResourceSet(new[] { actionResource });
            // Hero acts first -- it IS the player's turn.
            var encounterService = new CombatEncounterService(session.Characters, new FixedInitiativeRule(hero), turnResources, session.EventBus);
            var controller = new TacticalEncounterController(
                session.Characters, session.EventBus, positions, abilityResolution, movementResolution,
                new NavMeshService(), encounterService, worldSpawnService, new[] { (hero, PlayerFaction), (goblin, AiFaction) });
            var adapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, PlayerFaction);

            // No baked NavMesh in this bare fixture -- real path planning (already proven by
            // MoveActionExecutionTests) will reject, but ActionScheduler.StartMove must still have been
            // reached FOR THE HERO specifically: a real, non-null execution comes back either way, since
            // StartMove only ever returns null at the busy/authorization gate, never on a planning rejection.
            var result = adapter.TryHandleMove(new MoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward));

            Assert.IsNotNull(result, "The current turn's own combatant must have reached ActionScheduler.StartMove.");
            Assert.IsFalse(heroMotor.WasAskedToMove, "No baked NavMesh -- path planning rejects before SetDestination is ever called.");
            Assert.IsFalse(goblinMotor.WasAskedToMove, "Only the current turn's combatant may ever be considered -- never anyone else.");

            controller.Dispose();
        }
    }

    /// <summary>Task 4.5: the "full engagement" proof Gate C's own acceptance text asks for -- both factions
    /// AI-controlled (a simple test-only greedy decision provider, not the full utility-AI content-authoring
    /// pipeline, which is a separate concern already exercised by <c>UtilityAIDecisionProvider</c>'s own
    /// tests), driven through real yielded frames (not a live-Editor `eval_file` script -- that approach was
    /// tried live and abandoned: manually calling `Tick` from a blocking script cannot reliably drive a real
    /// `NavMeshAgent`'s per-frame engine-internal movement, which only advances via Unity's own real player
    /// loop). Ends with a real `EndCombat`+save, then an independent load, proving the save->load round trip
    /// from a real encounter outcome, not just hand-built `SaveData`.</summary>
    public class FullEngagementTests
    {
        private static readonly DefinitionId HeroesFaction = new("faction-heroes-fet");
        private static readonly DefinitionId GoblinsFaction = new("faction-goblins-fet");

        private readonly List<GameObject> spawned = new();
        private GameSession session;
        private WorldSpawnService worldSpawnService;
        private ResourceDefinition actionResource;
        private ResourceDefinition movementResource;
        private NavMeshSurface bakeSurface;
        private string savePath;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            worldSpawnService = new WorldSpawnService(session);
            actionResource = TacticalTestFactory.CreateResourceDefinition("resource-action-fet", "Action");
            movementResource = TacticalTestFactory.CreateResourceDefinition("resource-movement-fet", "Movement");
            savePath = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, $"fet-save-{System.Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
            NavMesh.RemoveAllNavMeshData();
            if (System.IO.File.Exists(savePath)) System.IO.File.Delete(savePath);
            var backup = savePath + ".bak";
            if (System.IO.File.Exists(backup)) System.IO.File.Delete(backup);
        }

        private NavMeshSurface BakeSurface()
        {
            if (bakeSurface == null)
            {
                var go = new GameObject("NavMeshSurface-fet");
                spawned.Add(go);
                bakeSurface = go.AddComponent<NavMeshSurface>();
                bakeSurface.collectObjects = CollectObjects.Children;
            }

            return bakeSurface;
        }

        private void CreateGroundPlane(Vector3 center, float halfSize)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.position = center;
            plane.transform.localScale = new Vector3(halfSize / 5f, 1f, halfSize / 5f);
            plane.isStatic = true;
            plane.transform.SetParent(BakeSurface().transform, worldPositionStays: true);
            spawned.Add(plane);
        }

        private void CreateWallObstacle(Vector3 center, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = center;
            wall.transform.localScale = size;
            wall.isStatic = true;
            wall.transform.SetParent(BakeSurface().transform, worldPositionStays: true);
            spawned.Add(wall);
        }

        /// <summary>Simplified, deterministic "kill" for this test's purposes -- publishes real CreatureDied
        /// directly rather than routing through HP/DamageEffect/DeathSaveService (SRD-specific, already
        /// exercised by other tests). This test's own job is proving the tactical loop -- navigation, real
        /// per-frame ticking, reassessment, death/removal, win condition, save/load -- not re-proving SRD5
        /// damage math.</summary>
        private sealed class KillOnHitEffect : AbilityEffect
        {
            public override void Apply(AbilityExecutionContext context)
            {
                foreach (var targetId in context.Targets)
                    context.EventBus?.Publish(new GameEventContext(GameEvent.CreatureDied, context.Source, targetId));
            }
        }

        /// <summary>Test-only greedy AI: closes on the nearest enemy, attacks once in range. Deliberately not
        /// the full BehaviorDefinition/Consideration content-authoring pipeline -- that machinery is
        /// UtilityAIDecisionProvider's own concern and already has its own tests.</summary>
        private sealed class GreedyMeleeDecisionProvider : ITurnDecisionProvider
        {
            private readonly AbilityDefinition meleeAbility;
            private readonly DefinitionId movementResourceId;
            private readonly IPositionProvider positions;
            private readonly float meleeRange;

            public GreedyMeleeDecisionProvider(AbilityDefinition meleeAbility, DefinitionId movementResourceId, IPositionProvider positions, float meleeRange)
            {
                this.meleeAbility = meleeAbility;
                this.movementResourceId = movementResourceId;
                this.positions = positions;
                this.meleeRange = meleeRange;
            }

            public TurnDecision GetDecision(TurnContext context)
            {
                if (context.PotentialEnemies.Count == 0) return null;

                var selfPos = positions.GetPosition(context.Self);
                RuntimeEntityId nearest = context.PotentialEnemies[0];
                var nearestDist = Vector3.Distance(selfPos, positions.GetPosition(nearest));
                foreach (var enemy in context.PotentialEnemies)
                {
                    var d = Vector3.Distance(selfPos, positions.GetPosition(enemy));
                    if (d < nearestDist) { nearestDist = d; nearest = enemy; }
                }

                return nearestDist <= meleeRange
                    ? new TurnDecision(meleeAbility, new[] { nearest }, 1f)
                    : new TurnDecision(movementResourceId, meleeRange * 0.6f, new[] { nearest }, 1f);
            }
        }

        private RuntimeEntityId SpawnMover(string spawnId, Vector3 position, DefinitionId faction, TestPositionProvider positions)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(actionResource.Id, max: 1));
            character.AddResource(new ResourceState(movementResource.Id, max: 8));
            session.Characters.Register(character);
            session.Party.AddMember(character.Id);

            var template = new GameObject($"Mover-{spawnId}");
            var navAgent = template.AddComponent<NavMeshAgent>();
            navAgent.speed = 6f;
            navAgent.acceleration = 40f;
            template.AddComponent<NavMeshCharacterMotor>();

            var instance = worldSpawnService.BindRestoredView(spawnId, character.Id, default, template, position, Quaternion.identity, faction, startInactive: false);
            Object.DestroyImmediate(template);
            spawned.Add(instance);

            instance.GetComponent<NavMeshAgent>().Warp(position);
            positions.Track(character.Id, instance.transform);

            return character.Id;
        }

        [UnityTest]
        public IEnumerator AFullTwoSidedEngagementReachesARealWinConditionThenSavesAndLoads()
        {
            CreateGroundPlane(Vector3.zero, 20f);
            CreateWallObstacle(new Vector3(0f, 1f, 0f), new Vector3(10f, 2f, 1f));
            BakeSurface().BuildNavMesh();
            yield return null;

            var positions = new TestPositionProvider();
            var hero1 = SpawnMover("hero-1", new Vector3(-3f, 0f, -8f), HeroesFaction, positions);
            var hero2 = SpawnMover("hero-2", new Vector3(3f, 0f, -8f), HeroesFaction, positions);
            var goblin1 = SpawnMover("goblin-1", new Vector3(-3f, 0f, 8f), GoblinsFaction, positions);
            var goblin2 = SpawnMover("goblin-2", new Vector3(3f, 0f, 8f), GoblinsFaction, positions);

            var meleeAbility = TacticalTestFactory.CreateAbility(
                "ability-fet-melee", actionResource, 1, 0f, 0f, AbilityTag.Attack | AbilityTag.Melee, new KillOnHitEffect());
            TacticalTestFactory.SetField(meleeAbility, "targeting", BuildMeleeTargeting());

            foreach (var id in new[] { hero1, hero2, goblin1, goblin2 })
            {
                session.Characters.TryGetCharacter(id, out var state);
                state.LearnAbility(meleeAbility.Id);
            }

            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus, positions: positions);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var turnResources = new TurnResourceSet(new[] { actionResource, movementResource });
            var encounterService = new CombatEncounterService(
                session.Characters, new RoundRobinInitiativeRule(new[] { hero1, goblin1, hero2, goblin2 }), turnResources, session.EventBus);

            var controller = new TacticalEncounterController(
                session.Characters, session.EventBus, positions, abilityResolution, movementResolution, new NavMeshService(),
                encounterService, worldSpawnService, new[] { (hero1, HeroesFaction), (hero2, HeroesFaction), (goblin1, GoblinsFaction), (goblin2, GoblinsFaction) });

            var heroBrain = new GreedyMeleeDecisionProvider(meleeAbility, movementResource.Id, positions, 2.5f);
            var goblinBrain = new GreedyMeleeDecisionProvider(meleeAbility, movementResource.Id, positions, 2.5f);
            var heroDriver = new TacticalAiTurnDriver(controller, session.Characters, heroBrain, HeroesFaction);
            var goblinDriver = new TacticalAiTurnDriver(controller, session.Characters, goblinBrain, GoblinsFaction);

            var elapsed = 0f;
            const float maxSimulatedSeconds = 30f;
            bool WinConditionReached()
            {
                var encounter = controller.EncounterService.Encounter;
                if (encounter == null) return true;
                var heroesAlive = false;
                var goblinsAlive = false;
                foreach (var c in encounter.Combatants)
                {
                    if (c.Faction == HeroesFaction) heroesAlive = true;
                    else if (c.Faction == GoblinsFaction) goblinsAlive = true;
                }

                return !heroesAlive || !goblinsAlive;
            }

            while (elapsed < maxSimulatedSeconds && !WinConditionReached())
            {
                controller.Tick(Time.deltaTime);
                heroDriver.Tick(Time.deltaTime);
                goblinDriver.Tick(Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(WinConditionReached(), $"Combat never reached a win condition within {maxSimulatedSeconds} simulated seconds.");

            var finalEncounter = controller.EncounterService.Encounter;
            int heroesLeft = 0, goblinsLeft = 0;
            if (finalEncounter != null)
            {
                foreach (var c in finalEncounter.Combatants)
                {
                    if (c.Faction == HeroesFaction) heroesLeft++;
                    else if (c.Faction == GoblinsFaction) goblinsLeft++;
                }
            }

            Assert.IsTrue(heroesLeft == 0 || goblinsLeft == 0, "One whole faction must have been eliminated.");
            Assert.IsFalse(heroesLeft == 0 && goblinsLeft == 0, "A real melee exchange should decide this before both sides are simultaneously wiped.");

            // Post-combat save -- SafePointGate must now allow it (EndCombat already cleared Encounter).
            controller.EncounterService.EndCombat();
            var safePointGate = new SafePointGate(combat: controller.EncounterService);
            var saveRequestService = new SaveRequestService(session, worldSpawnService, safePointGate);
            var saveResult = saveRequestService.RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome, $"Post-combat save must succeed: {saveResult.Message}");

            // Independent load -- proves the round trip, not just that Save() returned success.
            var loadResult = new SaveFileStore().Load(savePath);
            Assert.AreEqual(SaveLoadOutcome.Success, loadResult.Outcome, "The just-written save file must load back successfully.");
            var survivorCount = heroesLeft + goblinsLeft;
            Assert.AreEqual(4, loadResult.Data.Characters.Count, "All 4 combatants (survivors and the defeated) must round-trip through Characters.");
            Assert.AreEqual(4, loadResult.Data.WorldSpawns.Count, "All 4 world spawns, including the defeated tombstone(s), must round-trip.");
            Assert.AreEqual(4 - survivorCount, loadResult.Data.WorldSpawns.FindAll(s => s.Defeated).Count, "Exactly the eliminated combatant(s) must be recorded as defeated.");

            controller.Dispose();
        }

        private static TargetingDefinition BuildMeleeTargeting()
        {
            var targeting = new TargetingDefinition();
            TacticalTestFactory.SetField(targeting, "mode", TargetMode.SingleUnit);
            TacticalTestFactory.SetField(targeting, "relation", TargetRelation.Any);
            TacticalTestFactory.SetField(targeting, "minTargets", 1);
            TacticalTestFactory.SetField(targeting, "maxTargets", 1);
            TacticalTestFactory.SetField(targeting, "range", 2.5f);
            TacticalTestFactory.SetField(targeting, "requireLineOfSight", false);
            TacticalTestFactory.SetField(targeting, "requireLineOfEffect", false);
            TacticalTestFactory.SetField(targeting, "requireVisibleTarget", false);
            return targeting;
        }

        /// <summary>Deterministic turn order (no dice) so this test's own outcome depends only on the
        /// geometry/AI logic under test, never on initiative RNG.</summary>
        private sealed class RoundRobinInitiativeRule : IInitiativeRule
        {
            private readonly List<RuntimeEntityId> order;
            public RoundRobinInitiativeRule(IReadOnlyList<RuntimeEntityId> order) => this.order = new List<RuntimeEntityId>(order);

            public RollResult RollInitiative(RuntimeEntityId combatant)
            {
                var index = order.IndexOf(combatant);
                var value = order.Count - index; // earlier in the list = higher "initiative"
                return new RollResult(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, value, true) }, 0);
            }
        }
    }
}
