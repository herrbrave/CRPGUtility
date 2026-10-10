using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Combat;
using CRPG.Navigation;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Tests.PlayMode
{
    /// <summary>The optional move-adapter parameters (a downed combatant cannot be walked, a destination can be adjusted or refused, reactions can be switched off) and the NavMesh destination
    /// adjuster Viking Raid wrote by hand. Real session, real spawn service, real scheduler, a real baked NavMesh plane; the movers record what the scheduler asks of them.</summary>
    public class TacticalPlayerMoveAdapterOptionsTests
    {
        private static readonly DefinitionId PlayerFaction = new("faction-player-tpa");
        private static readonly DefinitionId AiFaction = new("faction-ai-tpa");

        private readonly List<GameObject> spawned = new();
        private GameSession session;
        private WorldSpawnService worldSpawnService;
        private ResourceDefinition actionResource;
        private ResourceDefinition movementResource;

        private sealed class RecordingMovable : MonoBehaviour, IMovable
        {
            public Vector3? LastDestination;
            public int Requests;

            public MoveResult SetDestination(Vector3 destination)
            {
                LastDestination = destination;
                Requests++;
                return new MoveResult(MoveStatus.Pending, MoveRequestId.NewId());
            }

            public void Stop() { }
            public bool Warp(Vector3 position) => true;
            public MotionSnapshot Snapshot => MotionSnapshot.None;
        }

        private sealed class FirstInitiativeRule : IInitiativeRule
        {
            private readonly RuntimeEntityId first;
            public FirstInitiativeRule(RuntimeEntityId first) => this.first = first;

            public RollResult RollInitiative(RuntimeEntityId combatant) =>
                new(new RollRequest(new DiceExpression(1, 20), source: combatant), new[] { new DieRollResult(20, combatant == first ? 20 : 10, true) }, 0);
        }

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            worldSpawnService = new WorldSpawnService(session);
            actionResource = TacticalTestFactory.CreateResourceDefinition("resource-action-tpa", "Action");
            movementResource = TacticalTestFactory.CreateResourceDefinition("resource-movement-tpa", "Movement");

            var surface = new GameObject("NavMeshSurface-tpa");
            spawned.Add(surface);
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.localScale = new Vector3(8f, 1f, 8f);   // 80 x 80 m
            plane.isStatic = true;
            plane.transform.SetParent(surface.transform, worldPositionStays: true);
            var nav = surface.AddComponent<NavMeshSurface>();
            nav.collectObjects = CollectObjects.Children;
            nav.BuildNavMesh();
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
            NavMesh.RemoveAllNavMeshData();
        }

        private (RuntimeEntityId Id, RecordingMovable Motor, CharacterAgent Agent) Spawn(string spawnId, Vector3 position, bool withMotor = true, float navAgentRadius = 0f)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(movementResource.Id, max: 30));
            character.AddResource(new ResourceState(actionResource.Id, max: 1));
            session.Characters.Register(character);

            var template = new GameObject($"Combatant-{spawnId}");
            if (withMotor) template.AddComponent<RecordingMovable>();
            var instance = worldSpawnService.BindRestoredView(spawnId, character.Id, default, template, position, Quaternion.identity, PlayerFaction, startInactive: false);
            Object.DestroyImmediate(template);
            spawned.Add(instance);
            if (navAgentRadius > 0f)
            {
                var nav = instance.AddComponent<NavMeshAgent>();
                nav.radius = navAgentRadius;
            }

            return (character.Id, instance.GetComponent<RecordingMovable>(), instance.GetComponent<CharacterAgent>());
        }

        private (TacticalEncounterController Controller, TestPositionProvider Positions) StartEncounter(RuntimeEntityId first, params (RuntimeEntityId Id, Vector3 Position, DefinitionId Faction)[] combatants)
        {
            var positions = new TestPositionProvider();
            foreach (var c in combatants) positions.Set(c.Id, c.Position);
            var abilityResolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus, positions: positions);
            var movementResolution = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            var encounterService = new CombatEncounterService(session.Characters, new FirstInitiativeRule(first), new TurnResourceSet(new[] { actionResource, movementResource }), session.EventBus);
            var controller = new TacticalEncounterController(session.Characters, session.EventBus, positions, abilityResolution, movementResolution, new NavMeshService(), encounterService,
                worldSpawnService, combatants.Select(c => (c.Id, c.Faction)));
            return (controller, positions);
        }

        // ------------------------------------------------------------------ adapter options
        [Test]
        public void ADefaultAdapterStillStartsTheMoveAtTheClickedDestination()
        {
            var hero = Spawn("hero", Vector3.zero);
            var (controller, _) = StartEncounter(hero.Id, (hero.Id, Vector3.zero, PlayerFaction));
            var adapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, PlayerFaction);

            var execution = adapter.TryHandleMove(new MoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward));

            Assert.IsNotNull(execution);
            controller.Tick(0.1f);
            Assert.IsTrue(hero.Motor.LastDestination.HasValue, "the real NavMesh accepted the plan and the motor was sent");
            Assert.Less(Vector3.Distance(hero.Motor.LastDestination.Value, new Vector3(5f, 0f, 0f)), 0.6f);
            controller.Dispose();
        }

        [Test]
        public void CanMoveFalseRefusesSilentlyAndStartsNothing()
        {
            var hero = Spawn("hero", Vector3.zero);
            var (controller, _) = StartEncounter(hero.Id, (hero.Id, Vector3.zero, PlayerFaction));
            var adapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, PlayerFaction, canMove: id => id != hero.Id);
            var refused = 0;
            adapter.MoveRefused += _ => refused++;

            var execution = adapter.TryHandleMove(new MoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward));

            Assert.IsNull(execution, "a downed combatant is not walked");
            Assert.IsFalse(controller.Scheduler.IsBusy);
            Assert.AreEqual(0, hero.Motor.Requests);
            Assert.AreEqual(0, refused, "silent: this is not a click the player should be told about");
            controller.Dispose();
        }

        [Test]
        public void AdjustDestinationChangesWhereTheSchedulerSendsTheMover()
        {
            var hero = Spawn("hero", Vector3.zero);
            var (controller, _) = StartEncounter(hero.Id, (hero.Id, Vector3.zero, PlayerFaction));
            RuntimeEntityId? askedFor = null;
            var adapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, PlayerFaction,
                adjustDestination: (id, destination) => { askedFor = id; return (new Vector3(0f, 0f, 6f), null); });

            var execution = adapter.TryHandleMove(new MoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward));
            controller.Tick(0.1f);

            Assert.IsNotNull(execution);
            Assert.AreEqual(hero.Id, askedFor, "the adjuster is told who is moving");
            Assert.IsTrue(hero.Motor.LastDestination.HasValue);
            Assert.Less(Vector3.Distance(hero.Motor.LastDestination.Value, new Vector3(0f, 0f, 6f)), 0.6f, "the adjusted destination, not the clicked one");
            controller.Dispose();
        }

        [Test]
        public void ARefusalRaisesMoveRefusedWithTheMessageAndStartsNothing()
        {
            var hero = Spawn("hero", Vector3.zero);
            var (controller, _) = StartEncounter(hero.Id, (hero.Id, Vector3.zero, PlayerFaction));
            var adapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, PlayerFaction, adjustDestination: (_, destination) => (destination, "Nope."));
            string message = null;
            adapter.MoveRefused += m => message = m;

            var execution = adapter.TryHandleMove(new MoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward));

            Assert.IsNull(execution);
            Assert.AreEqual("Nope.", message);
            Assert.IsFalse(controller.Scheduler.IsBusy);
            Assert.AreEqual(0, hero.Motor.Requests);
            controller.Dispose();
        }

        [Test]
        public void NoRefusalIsRaisedWhenThereIsNoMoverToMoveBecauseTheAgentCheckComesFirst()
        {
            var hero = Spawn("hero", Vector3.zero, withMotor: false);   // an agent with no motor
            var (controller, _) = StartEncounter(hero.Id, (hero.Id, Vector3.zero, PlayerFaction));
            var adapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, PlayerFaction, adjustDestination: (_, destination) => (destination, "Nope."));
            var refused = 0;
            adapter.MoveRefused += _ => refused++;

            Assert.IsNull(adapter.TryHandleMove(new MoveCommand(new Vector3(5f, 0f, 0f), Vector3.forward)));
            Assert.AreEqual(0, refused);
            controller.Dispose();
        }

        [Test]
        public void SwitchingReactionsOffStillStartsAndSendsTheMove()
        {
            var hero = Spawn("hero", Vector3.zero);
            var enemy = Spawn("enemy", new Vector3(10f, 0f, 10f));
            var (controller, _) = StartEncounter(hero.Id, (hero.Id, Vector3.zero, PlayerFaction), (enemy.Id, new Vector3(10f, 0f, 10f), AiFaction));
            var adapter = new TacticalPlayerMoveAdapter(controller, movementResource.Id, PlayerFaction, provokesReactions: false);

            var execution = adapter.TryHandleMove(new MoveCommand(new Vector3(4f, 0f, 0f), Vector3.forward));
            controller.Tick(0.1f);

            Assert.IsNotNull(execution);
            Assert.IsTrue(hero.Motor.LastDestination.HasValue);
            controller.Dispose();
        }

        // ------------------------------------------------------------------ NavMesh adjuster
        // the NavMesh snaps a point onto its surface (a few centimetres up): compare on the ground plane
        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        [Test]
        public void AClickWellOffTheNavMeshIsRefusedWithTheStandardMessage()
        {
            var hero = Spawn("hero", Vector3.zero);

            var (_, refusal) = NavMeshDestinationAdjuster.Adjust(hero.Id, new Vector3(500f, 0f, 500f), new[] { hero.Id });

            Assert.AreEqual(NavMeshDestinationAdjuster.OffMeshMessage, refusal);
        }

        [Test]
        public void AClickNearTheMeshEdgeIsSnappedOntoIt()
        {
            var hero = Spawn("hero", Vector3.zero);

            var (destination, refusal) = NavMeshDestinationAdjuster.Adjust(hero.Id, new Vector3(40.8f, 0f, 0f), new[] { hero.Id });   // the plane ends at x = 40

            Assert.IsNull(refusal);
            Assert.LessOrEqual(destination.x, 40.01f);
            Assert.Greater(destination.x, 39f);
        }

        [Test]
        public void ADestinationOnTopOfAnotherCombatantIsPushedOutOfItsFootprint()
        {
            var hero = Spawn("hero", Vector3.zero);
            var goblin = Spawn("goblin", new Vector3(6f, 0f, 0f));

            var (destination, refusal) = NavMeshDestinationAdjuster.Adjust(hero.Id, new Vector3(6.2f, 0f, 0f), new[] { hero.Id, goblin.Id });

            Assert.IsNull(refusal);
            var gap = Vector3.Distance(new Vector3(destination.x, 0f, destination.z), new Vector3(6f, 0f, 0f));
            Assert.GreaterOrEqual(gap, NavMeshDestinationAdjuster.DefaultRadius * 2f + NavMeshDestinationAdjuster.Clearance - 0.02f, "outside the two default radii plus the clearance");
        }

        [Test]
        public void ADestinationExactlyOnAnotherCombatantIsPushedTowardWhereTheMoverComesFrom()
        {
            var hero = Spawn("hero", Vector3.zero);
            var goblin = Spawn("goblin", new Vector3(6f, 0f, 0f));

            var (destination, _) = NavMeshDestinationAdjuster.Adjust(hero.Id, new Vector3(6f, 0f, 0f), new[] { hero.Id, goblin.Id });

            Assert.Less(destination.x, 6f, "pushed back toward the mover at the origin, not through the goblin");
            Assert.Greater(Vector3.Distance(destination, new Vector3(6f, 0f, 0f)), 0.8f);
        }

        [Test]
        public void ABiggerAgentRadiusMakesTheClearanceBigger()
        {
            var hero = Spawn("hero", Vector3.zero);
            var troll = Spawn("troll", new Vector3(6f, 0f, 0f), navAgentRadius: 1.0f);

            var (destination, _) = NavMeshDestinationAdjuster.Adjust(hero.Id, new Vector3(6.3f, 0f, 0f), new[] { hero.Id, troll.Id });

            Assert.GreaterOrEqual(Vector3.Distance(destination, new Vector3(6f, 0f, 0f)), 1.0f + NavMeshDestinationAdjuster.DefaultRadius + NavMeshDestinationAdjuster.Clearance - 0.05f);
        }

        [Test]
        public void TheMoverItselfAndFarAwayCombatantsDoNotMoveTheDestination()
        {
            var hero = Spawn("hero", Vector3.zero);
            var goblin = Spawn("goblin", new Vector3(30f, 0f, 30f));

            var (destination, refusal) = NavMeshDestinationAdjuster.Adjust(hero.Id, new Vector3(0.1f, 0f, 0.1f), new[] { hero.Id, goblin.Id });   // right on the mover itself

            Assert.IsNull(refusal);
            Assert.Less(Flat(destination, new Vector3(0.1f, 0f, 0.1f)), 0.05f);
        }

        [Test]
        public void CreateBuildsTheDelegateTheAdapterTakesAndReadsTheOthersLazilyEachClick()
        {
            var hero = Spawn("hero", Vector3.zero);
            var goblin = Spawn("goblin", new Vector3(6f, 0f, 0f));
            var others = new List<RuntimeEntityId> { hero.Id };
            var adjust = NavMeshDestinationAdjuster.Create(() => others);

            var before = adjust(hero.Id, new Vector3(6.2f, 0f, 0f));
            others.Add(goblin.Id);
            var after = adjust(hero.Id, new Vector3(6.2f, 0f, 0f));

            Assert.Less(Flat(before.Destination, new Vector3(6.2f, 0f, 0f)), 0.05f, "nobody to keep clear of yet");
            Assert.Greater(Flat(after.Destination, new Vector3(6.2f, 0f, 0f)), 0.3f, "the goblin was added: the same click is now pushed");
        }
    }
}
