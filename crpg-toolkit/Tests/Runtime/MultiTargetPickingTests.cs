using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Actions;
using CRPG.Gameplay.Combat;
using CRPG.Navigation;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// An ability that takes several creatures (Bless, Magic Missile, Prayer of Healing) is not cast by the first click: each click picks one more creature, the ability is cast when the last allowed one is picked, and clicking a creature already picked (or <see cref="AbilityTargetingGate.Confirm"/>)
    /// casts it with what is picked. A pick that fails validation is not added and the picks already made stay. A single-target ability is unchanged (the first click casts).
    /// </summary>
    public class MultiTargetPickingTests
    {
        private readonly List<GameObject> spawned = new();
        private GameSession session;
        private WorldSpawnService worldSpawnService;
        private ResourceDefinition actionResource;
        private TestPositionProvider positions;
        private AbilityTargetingGate gate;
        private ActionScheduler scheduler;
        private RuntimeEntityId source;
        private readonly List<RuntimeEntityId> rejected = new();

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            worldSpawnService = new WorldSpawnService(session);
            actionResource = TacticalTestFactory.CreateResourceDefinition("resource-action-mtp", "Action");
            positions = new TestPositionProvider();
            source = RuntimeEntityId.NewId();
            var sourceState = new CharacterState(source); sourceState.AddResource(new ResourceState(actionResource.Id, max: 5)); session.Characters.Register(sourceState);
            positions.Set(source, Vector3.zero);
            var resolution = new AbilityResolutionService(session.Characters, session.DiceService, eventBus: session.EventBus, positions: positions);
            var movement = new MovementResolutionService(session.Characters, positions, new NoOpMovementController());
            scheduler = new ActionScheduler(resolution, movement, new NavMeshService(), session.Characters, positions);
            gate = new AbilityTargetingGate(scheduler, resolution, hittableMask: ~0);
            gate.TargetRejected += (_, id, __) => rejected.Add(id);
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private RuntimeEntityId SpawnAt(Vector3 position)
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.AddResource(new ResourceState(actionResource.Id, max: 5));
            session.Characters.Register(character);
            var template = new GameObject("Pickable"); template.AddComponent<SphereCollider>();
            spawned.Add(worldSpawnService.BindRestoredView(character.Id.ToString(), character.Id, default, template, position, Quaternion.identity, default, startInactive: false));
            Object.DestroyImmediate(template);
            positions.Set(character.Id, position);
            return character.Id;
        }

        private AbilityDefinition Ability(string id, int maxTargets, float range = 10f)
        {
            var ability = TacticalTestFactory.CreateAbility(id, actionResource, 1, 0f, 0f);
            TacticalTestFactory.SetField(ability.Targeting, "maxTargets", maxTargets);
            TacticalTestFactory.SetField(ability.Targeting, "range", range);
            TacticalTestFactory.SetField(ability.Targeting, "relation", TargetRelation.Any);
            TacticalTestFactory.SetField(ability.Targeting, "requireLineOfSight", false);
            return ability;
        }

        private bool Click(Vector3 at) => gate.TryConsume(new Ray(at + Vector3.up * 10f, Vector3.down));

        [Test]
        public void TheFirstClickOnlyPicksAndTheAbilityStaysArmed()
        {
            var a = SpawnAt(new Vector3(2, 0, 0)); SpawnAt(new Vector3(5, 0, 0));
            gate.Arm(source, Ability("mtp-1", 3));
            Assert.IsTrue(gate.IsMultiTarget);
            Assert.IsTrue(Click(new Vector3(2, 0, 0)));
            Assert.IsTrue(gate.IsArmed); Assert.IsFalse(scheduler.IsBusy, "nothing cast yet");
            CollectionAssert.AreEqual(new[] { a }, gate.PickedTargets);
        }

        [Test]
        public void ThePickThatFillsTheLastSlotCastsAndDisarms()
        {
            var ids = new[] { SpawnAt(new Vector3(2, 0, 0)), SpawnAt(new Vector3(5, 0, 0)), SpawnAt(new Vector3(8, 0, 0)) };
            gate.Arm(source, Ability("mtp-2", 3));
            Click(new Vector3(2, 0, 0)); Click(new Vector3(5, 0, 0)); Assert.IsFalse(scheduler.IsBusy);
            Click(new Vector3(8, 0, 0));
            Assert.IsTrue(scheduler.IsBusy, "three picked: cast"); Assert.IsFalse(gate.IsArmed); Assert.IsEmpty(gate.PickedTargets, "picks are cleared once cast");
            Assert.IsNotNull(ids);
        }

        [Test]
        public void ClickingAPickedCreatureAgainCastsWithWhatIsPicked()
        {
            SpawnAt(new Vector3(2, 0, 0)); SpawnAt(new Vector3(5, 0, 0));
            gate.Arm(source, Ability("mtp-3", 3));
            Click(new Vector3(2, 0, 0)); Click(new Vector3(5, 0, 0));
            Click(new Vector3(5, 0, 0));
            Assert.IsTrue(scheduler.IsBusy, "the second click on the same creature confirms"); Assert.IsFalse(gate.IsArmed);
        }

        [Test]
        public void ConfirmCastsWithThePicksAndDoesNothingWithNone()
        {
            SpawnAt(new Vector3(2, 0, 0));
            gate.Arm(source, Ability("mtp-4", 3));
            Assert.IsFalse(gate.Confirm(), "nothing picked"); Assert.IsFalse(scheduler.IsBusy); Assert.IsTrue(gate.IsArmed);
            Click(new Vector3(2, 0, 0));
            Assert.IsTrue(gate.Confirm()); Assert.IsTrue(scheduler.IsBusy); Assert.IsFalse(gate.IsArmed);
        }

        [Test]
        public void ARejectedPickIsNotAddedAndTheEarlierPicksStay()
        {
            var near = SpawnAt(new Vector3(2, 0, 0)); var far = SpawnAt(new Vector3(30, 0, 0));
            gate.Arm(source, Ability("mtp-5", 3, range: 10f));
            Click(new Vector3(2, 0, 0)); Click(new Vector3(30, 0, 0));
            CollectionAssert.AreEqual(new[] { near }, gate.PickedTargets); CollectionAssert.AreEqual(new[] { far }, rejected);
            Assert.IsTrue(gate.IsArmed); Assert.IsFalse(scheduler.IsBusy);
        }

        [Test]
        public void DisarmingAndArmingAgainStartFromNoPicks()
        {
            SpawnAt(new Vector3(2, 0, 0));
            var ability = Ability("mtp-6", 3);
            gate.Arm(source, ability); Click(new Vector3(2, 0, 0)); Assert.AreEqual(1, gate.PickedTargets.Count);
            gate.Disarm(); Assert.IsEmpty(gate.PickedTargets);
            gate.Arm(source, ability); Click(new Vector3(2, 0, 0)); gate.Arm(source, ability); Assert.IsEmpty(gate.PickedTargets, "choosing the ability again starts the choice over");
        }

        [Test]
        public void ASingleTargetAbilityStillCastsOnTheFirstClick()
        {
            SpawnAt(new Vector3(2, 0, 0));
            gate.Arm(source, Ability("mtp-7", 1));
            Assert.IsFalse(gate.IsMultiTarget);
            Click(new Vector3(2, 0, 0));
            Assert.IsTrue(scheduler.IsBusy); Assert.IsFalse(gate.IsArmed);
        }

        [Test]
        public void PicksChangedFiresForEveryChange()
        {
            SpawnAt(new Vector3(2, 0, 0)); SpawnAt(new Vector3(5, 0, 0));
            var changes = 0; gate.PicksChanged += () => changes++;
            gate.Arm(source, Ability("mtp-8", 3));
            Click(new Vector3(2, 0, 0)); Click(new Vector3(5, 0, 0));
            Assert.AreEqual(2, changes);
        }
    }
}
