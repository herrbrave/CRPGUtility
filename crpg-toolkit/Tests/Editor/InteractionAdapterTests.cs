using CRPG.Core;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 3.3: direct unit coverage of the Loot/Door reference adapters' own logic -- no scene/
    /// NavMesh bake needed for these (Talk needs a live UI Toolkit panel, Teleport needs a real NavMeshAgent to
    /// warp -- both covered elsewhere: Talk's cancel/cleanup contract in InteractionLifecycleTests, Teleport's
    /// failure-reporting and the door's real traversability change in dedicated PlayMode tests).</summary>
    public class InteractionAdapterTests
    {
        private GameObject go;
        private GameObject anchorGo;

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
            if (anchorGo != null) Object.DestroyImmediate(anchorGo);
        }

        /// <summary>Both adapters carry [RequireComponent(typeof(WorldInteractable))], which auto-adds it the
        /// moment the adapter is added -- WorldInteractable.Awake() then runs immediately (Awake fires on
        /// AddComponent for an active GameObject regardless of Edit/Play mode) and logs an error if the
        /// required anchor isn't set yet. Building inactive first defers that Awake until after Configure().</summary>
        private GameObject CreateInactiveFixtureGameObject(string name)
        {
            anchorGo = new GameObject($"{name}Anchor");
            var fixtureGo = new GameObject(name);
            fixtureGo.SetActive(false);
            return fixtureGo;
        }

        private void ConfigureAndActivate(GameObject fixtureGo)
        {
            fixtureGo.GetComponent<WorldInteractable>().Configure(anchorGo.transform, 1.5f);
            fixtureGo.SetActive(true);
        }

        [Test]
        public void LootAdapterIsUnavailableWhenTheContainerIsEmpty()
        {
            go = CreateInactiveFixtureGameObject("Loot");
            var adapter = go.AddComponent<LootInteractionAdapter>();
            ConfigureAndActivate(go);

            Assert.IsFalse(adapter.IsAvailable(RuntimeEntityId.NewId()));
        }

        [Test]
        public void LootAdapterIsAvailableAndTakesEverythingIntoPartyInventory()
        {
            go = CreateInactiveFixtureGameObject("Loot");
            var adapter = go.AddComponent<LootInteractionAdapter>();
            ConfigureAndActivate(go);

            var itemId = new DefinitionId("item-test-sword");
            adapter.Container.AddItem(new ItemInstance(RuntimeEntityId.NewId(), itemId));
            adapter.Inventory = new PartyInventoryState();

            Assert.IsTrue(adapter.IsAvailable(RuntimeEntityId.NewId()));

            InteractionExecutionResult? result = null;
            adapter.BeginExecute(RuntimeEntityId.NewId(), r => result = r);

            Assert.AreEqual(InteractionExecutionResult.Succeeded, result);
            Assert.IsTrue(adapter.Container.IsEmpty);
        }

        [Test]
        public void LootAdapterReportsFailureIfTheContainerIsAlreadyEmptyAtExecuteTime()
        {
            go = CreateInactiveFixtureGameObject("Loot");
            var adapter = go.AddComponent<LootInteractionAdapter>();
            ConfigureAndActivate(go);
            adapter.Inventory = new PartyInventoryState();

            InteractionExecutionResult? result = null;
            adapter.BeginExecute(RuntimeEntityId.NewId(), r => result = r);

            Assert.AreEqual(InteractionExecutionResult.Failed, result);
        }

        [Test]
        public void DoorAdapterTogglesOpenStateAndTheObstacleEachExecute()
        {
            go = CreateInactiveFixtureGameObject("Door");
            var obstacle = go.AddComponent<NavMeshObstacle>();
            var adapter = go.AddComponent<UseDoorInteractionAdapter>();
            TestDefinitionFactory.SetPrivateField(adapter, "obstacle", obstacle);
            ConfigureAndActivate(go);

            Assert.IsFalse(adapter.IsOpen);
            Assert.IsTrue(obstacle.enabled);

            InteractionExecutionResult? result = null;
            adapter.BeginExecute(RuntimeEntityId.NewId(), r => result = r);

            Assert.AreEqual(InteractionExecutionResult.Succeeded, result);
            Assert.IsTrue(adapter.IsOpen);
            Assert.IsFalse(obstacle.enabled, "Open must disable carving so the doorway is passable.");

            adapter.BeginExecute(RuntimeEntityId.NewId(), r => result = r);

            Assert.IsFalse(adapter.IsOpen);
            Assert.IsTrue(obstacle.enabled, "Closed must re-enable carving so the doorway blocks again.");
        }
    }
}
