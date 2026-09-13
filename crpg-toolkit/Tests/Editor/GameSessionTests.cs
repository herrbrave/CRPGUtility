using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Presentation.Equipment;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 1.2 (Production Foundation): GameSession/GameSessionHost/CharacterAgent/WorldSpawnService
    /// -- the composition boundary that wraps NpcSpawnService's existing state-only spawn with the GameObject/
    /// faction/inventory/motor/view wiring every demo bootstrap has so far reimplemented by hand.</summary>
    public class GameSessionTests
    {
        private static GameSession NewSession(int seed = 1) => new(new RulesDatabase(), new DiceService(new SeededRandomSource(seed)));

        [Test]
        public void ConstructorThrowsWhenRulesDatabaseIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new GameSession(null, new DiceService(new SeededRandomSource(1))));
        }

        [Test]
        public void ConstructorThrowsWhenDiceServiceIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new GameSession(new RulesDatabase(), null));
        }

        [Test]
        public void TwoSessionsDoNotShareState()
        {
            var sessionA = NewSession(1);
            var sessionB = NewSession(2);

            var fighter = RuntimeEntityId.NewId();
            sessionA.Party.AddMember(fighter);
            sessionA.Characters.Register(new CharacterState(fighter));
            sessionA.Timeline.Record("a-only", TimelineValue.Flag);

            Assert.IsFalse(sessionB.Party.IsMember(fighter));
            Assert.IsFalse(sessionB.Characters.TryGetCharacter(fighter, out _));
            Assert.IsFalse(sessionB.Timeline.HasOccurred("a-only"));

            Assert.AreNotSame(sessionA.Party, sessionB.Party);
            Assert.AreNotSame(sessionA.Characters, sessionB.Characters);
            Assert.AreNotSame(sessionA.Inventory, sessionB.Inventory);
            Assert.AreNotSame(sessionA.Timeline, sessionB.Timeline);
            Assert.AreNotSame(sessionA.Quests, sessionB.Quests);
            Assert.AreNotSame(sessionA.EventBus, sessionB.EventBus);
        }

        [Test]
        public void AcceptsCommandsIsTrueUntilDisposed()
        {
            var session = NewSession();
            Assert.IsTrue(session.AcceptsCommands);

            session.Dispose();

            Assert.IsFalse(session.AcceptsCommands);
            Assert.IsTrue(session.IsDisposed);
        }

        [Test]
        public void DisposingASessionTwiceDoesNotThrow()
        {
            var session = NewSession();
            session.Dispose();

            Assert.DoesNotThrow(() => session.Dispose());
            Assert.IsTrue(session.IsDisposed);
        }
    }

    public class GameSessionHostTests
    {
        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        private static GameSession NewSession(int seed = 1) => new(new RulesDatabase(), new DiceService(new SeededRandomSource(seed)));

        private GameSessionHost CreateHost()
        {
            go = new GameObject("TestSessionHost");
            return go.AddComponent<GameSessionHost>();
        }

        [Test]
        public void StartSessionCreatesAndExposesASession()
        {
            var host = CreateHost();

            var session = host.StartSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));

            Assert.IsNotNull(session);
            Assert.AreSame(session, host.Session);
            Assert.IsFalse(session.IsDisposed);
        }

        [Test]
        public void StartSessionWhileAlreadyActiveThrows()
        {
            var host = CreateHost();
            host.StartSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));

            Assert.Throws<InvalidOperationException>(() => host.StartSession(new RulesDatabase(), new DiceService(new SeededRandomSource(2))));
        }

        [Test]
        public void StartSessionAfterEndSessionSucceeds()
        {
            var host = CreateHost();
            var first = host.StartSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            host.EndSession();

            var second = host.StartSession(new RulesDatabase(), new DiceService(new SeededRandomSource(2)));

            Assert.IsTrue(first.IsDisposed);
            Assert.AreNotSame(first, second);
            Assert.IsFalse(second.IsDisposed);
        }

        [Test]
        public void EndSessionIsIdempotent()
        {
            var host = CreateHost();
            host.StartSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));

            host.EndSession();

            Assert.DoesNotThrow(() => host.EndSession());
            Assert.IsNull(host.Session);
        }

        // DestroyingTheHostDisposesItsSession (OnDestroy -> EndSession wiring) is covered in
        // Tests/Runtime/GameSessionDestructionTests.cs (PlayMode), not here: confirmed empirically that
        // DestroyImmediate's OnDestroy callback never fires in headless -batchmode EditMode (no Play session),
        // even after polling many editor ticks -- only a real Play session (PlayMode tests) delivers it
        // synchronously, the same constraint Task 1.1's CharacterBindingTests already worked within.
    }

    public class CharacterAgentTests
    {
        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        private static GameSession NewSession(int seed = 1) => new(new RulesDatabase(), new DiceService(new SeededRandomSource(seed)));

        private CharacterAgent CreateAgent()
        {
            go = new GameObject("TestCharacterAgent");
            return go.AddComponent<CharacterAgent>();
        }

        [Test]
        public void BindSetsEntityIdAndSession()
        {
            var agent = CreateAgent();
            var session = NewSession();
            var entityId = RuntimeEntityId.NewId();

            agent.Bind(entityId, session);

            Assert.AreEqual(entityId, agent.EntityId);
            Assert.AreSame(session, agent.Session);
            Assert.IsTrue(agent.IsBound);
        }

        [Test]
        public void RebindingTheIdenticalTupleIsANoOp()
        {
            var agent = CreateAgent();
            var session = NewSession();
            var entityId = RuntimeEntityId.NewId();
            agent.Bind(entityId, session);

            Assert.DoesNotThrow(() => agent.Bind(entityId, session));

            Assert.AreEqual(entityId, agent.EntityId);
            Assert.AreSame(session, agent.Session);
        }

        [Test]
        public void BindingToADifferentSessionWithoutUnbindingFirstThrows()
        {
            var agent = CreateAgent();
            var sessionA = NewSession(1);
            var sessionB = NewSession(2);
            agent.Bind(RuntimeEntityId.NewId(), sessionA);

            Assert.Throws<InvalidOperationException>(() => agent.Bind(RuntimeEntityId.NewId(), sessionB));

            Assert.AreSame(sessionA, agent.Session, "The original binding must be untouched by the failed rebind attempt.");
        }

        [Test]
        public void UnbindThenBindToADifferentSessionSucceeds()
        {
            var agent = CreateAgent();
            var sessionA = NewSession(1);
            var sessionB = NewSession(2);
            agent.Bind(RuntimeEntityId.NewId(), sessionA);

            agent.Unbind();
            var entityId = RuntimeEntityId.NewId();
            agent.Bind(entityId, sessionB);

            Assert.AreSame(sessionB, agent.Session);
            Assert.AreEqual(entityId, agent.EntityId);
        }

        [Test]
        public void BindThrowsWhenSessionIsNull()
        {
            var agent = CreateAgent();
            Assert.Throws<ArgumentNullException>(() => agent.Bind(RuntimeEntityId.NewId(), null));
        }

        // DestroyingTheGameObjectUnbindsTheAgent (OnDestroy -> Unbind wiring) is covered in
        // Tests/Runtime/GameSessionDestructionTests.cs (PlayMode) -- see the note on
        // GameSessionHostTests above for why this can't be proven in headless EditMode.

        [Test]
        public void BindResolvesTheMotorFromTheSameGameObject()
        {
            go = new GameObject("TestCharacterAgentWithMotor");
            go.AddComponent<NavMeshAgent>();
            var motor = go.AddComponent<CRPG.Navigation.NavMeshCharacterMotor>();
            var agent = go.AddComponent<CharacterAgent>();

            agent.Bind(RuntimeEntityId.NewId(), NewSession());

            Assert.AreSame(motor, agent.Motor);
        }

        [Test]
        public void MotorIsNullWhenTheGameObjectHasNone()
        {
            var agent = CreateAgent();

            agent.Bind(RuntimeEntityId.NewId(), NewSession());

            Assert.IsNull(agent.Motor);
        }
    }

    public class WorldSpawnServiceTests
    {
        private GameObject visualPrefabTemplate;
        private GameObject spawnedInstance;
        private readonly List<GameObject> extraInstances = new();
        private readonly List<GameObject> extraPrefabs = new();

        [TearDown]
        public void TearDown()
        {
            if (spawnedInstance != null) UnityEngine.Object.DestroyImmediate(spawnedInstance);
            if (visualPrefabTemplate != null) UnityEngine.Object.DestroyImmediate(visualPrefabTemplate);
            foreach (var instance in extraInstances) if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            foreach (var prefab in extraPrefabs) if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab);
            extraInstances.Clear();
            extraPrefabs.Clear();
        }

        private NpcDefinition CreateSpawnableNpc(string id, DefinitionId faction = default, params DefinitionId[] startingItemIds)
        {
            var definition = TestDefinitionFactory.CreateNpcDefinition(id, id);
            visualPrefabTemplate = new GameObject($"{id}-VisualPrefab");
            TestDefinitionFactory.SetPrivateField(definition, "visualPrefab", visualPrefabTemplate);
            TestDefinitionFactory.SetPrivateField(definition, "faction", faction);
            TestDefinitionFactory.SetPrivateField(definition, "startingItemIds", new List<DefinitionId>(startingItemIds));
            return definition;
        }

        [Test]
        public void SpawnNpcRegistersCharacterStateAndBindsAgent()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-guard");

            var agent = spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity);
            spawnedInstance = agent.gameObject;

            Assert.IsTrue(session.Characters.TryGetCharacter(agent.EntityId, out _));
            Assert.AreSame(session, agent.Session);
            Assert.IsTrue(agent.IsBound);
        }

        [Test]
        public void SpawnNpcThrowsWhenVisualPrefabIsNull()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = TestDefinitionFactory.CreateNpcDefinition("npc-no-visual", "No Visual");

            Assert.Throws<InvalidOperationException>(() => spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity));
        }

        [Test]
        public void SpawnNpcThrowsWhenSessionIsAlreadyDisposed()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-too-late");
            session.Dispose();

            Assert.Throws<InvalidOperationException>(() => spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity));
        }

        [Test]
        public void SpawnNpcRecordsFaction()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var factionId = new DefinitionId("faction-goblins");
            var definition = CreateSpawnableNpc("npc-goblin", factionId);

            var agent = spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity);
            spawnedInstance = agent.gameObject;

            Assert.IsTrue(spawner.TryGetFaction(agent.EntityId, out var resolvedFaction));
            Assert.AreEqual(factionId, resolvedFaction);
        }

        [Test]
        public void SpawnNpcWithNoFactionRecordsNone()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-neutral");

            var agent = spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity);
            spawnedInstance = agent.gameObject;

            Assert.IsFalse(spawner.TryGetFaction(agent.EntityId, out _));
        }

        [Test]
        public void SpawnNpcGrantsAndEquipsStartingItems()
        {
            var rulesDatabase = new RulesDatabase();
            var slotId = new DefinitionId("slot-mainhand");
            var itemDefinition = TestDefinitionFactory.CreateItemDefinition("item-starting-sword", "Starting Sword", equipmentSlotId: slotId);
            rulesDatabase.Register(itemDefinition);

            var session = new GameSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-armed", default, itemDefinition.Id);

            var agent = spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity);
            spawnedInstance = agent.gameObject;

            Assert.IsTrue(session.Inventory.TryGetEquippedItem(agent.EntityId, slotId, out var equipped));
            Assert.AreEqual(itemDefinition.Id, equipped.DefinitionId);
        }

        [Test]
        public void SpawnNpcSkipsAnUnresolvableStartingItemWithoutFailingTheWholeSpawn()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-unresolvable-item", default, new DefinitionId("item-never-registered"));

            CharacterAgent agent = null;
            Assert.DoesNotThrow(() => agent = spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity));
            spawnedInstance = agent.gameObject;

            Assert.IsTrue(session.Characters.TryGetCharacter(agent.EntityId, out _));
        }

        [Test]
        public void SpawnNpcInitializesAVisualEquipmentComponentPresentOnTheVisualPrefab()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-with-equipment-view");
            visualPrefabTemplate.AddComponent<EquipmentVisualBinder>();

            var agent = spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity);
            spawnedInstance = agent.gameObject;

            var visualEquipment = agent.GetComponent<EquipmentVisualBinder>();
            Assert.IsNotNull(visualEquipment);

            // Indirect proof Initialize was actually called (its binding fields are private): per Task 1.1's
            // contract (Task 2.4: now EquipmentVisualBinder), a bound instance rejects a differently-bound
            // rebind attempt.
            Assert.Throws<InvalidOperationException>(
                () => visualEquipment.Initialize(RuntimeEntityId.NewId(), new RulesDatabase(), new PartyInventoryState(), new GameEventBus()));
        }

        [Test]
        public void SpawnNpcUsesAnExistingCharacterAgentOnTheVisualPrefabRatherThanAddingASecondOne()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-preexisting-agent-component");
            visualPrefabTemplate.AddComponent<CharacterAgent>();

            var agent = spawner.SpawnNpc("spawn-test", definition, Vector3.zero, Quaternion.identity);
            spawnedInstance = agent.gameObject;

            Assert.AreEqual(1, agent.GetComponents<CharacterAgent>().Length);
        }

        [Test]
        public void SpawnNpcThrowsOnDuplicateSpawnId()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-dup");
            var agent = spawner.SpawnNpc("dup-id", definition, Vector3.zero, Quaternion.identity);
            extraInstances.Add(agent.gameObject);
            extraPrefabs.Add(visualPrefabTemplate);

            var secondDefinition = CreateSpawnableNpc("npc-dup-2");

            Assert.Throws<InvalidOperationException>(() => spawner.SpawnNpc("dup-id", secondDefinition, Vector3.zero, Quaternion.identity));
        }

        [Test]
        public void MarkDefeatedTombstonesTheRecordAndUnbindsTheAgent()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-doomed");
            var agent = spawner.SpawnNpc("doomed", definition, Vector3.zero, Quaternion.identity);
            var characterId = agent.EntityId;

            spawner.MarkDefeated(characterId);

            Assert.IsFalse(agent.IsBound, "MarkDefeated should unbind the agent before destroying it.");
            Assert.IsFalse(spawner.OwnedAgents.Contains(agent));

            var snapshot = spawner.CaptureSnapshot().Single(s => s.SpawnId == "doomed");
            Assert.IsTrue(snapshot.Defeated);
        }

        [Test]
        public void ImportDefeatedTombstoneMakesCaptureSnapshotReportDefeatedWithNoLiveAgent()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);

            spawner.ImportDefeatedTombstone("ghost-spawn", new DefinitionId("npc-ghost"));

            var snapshot = spawner.CaptureSnapshot().Single(s => s.SpawnId == "ghost-spawn");
            Assert.IsTrue(snapshot.Defeated);
            Assert.AreEqual("npc-ghost", snapshot.NpcDefinitionId);
        }

        [Test]
        public void OwnedAgentsIncludesADisabledAgentUnlikeCharacterAgentAllLive()
        {
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);
            var definition = CreateSpawnableNpc("npc-disableable");
            var agent = spawner.SpawnNpc("disableable", definition, Vector3.zero, Quaternion.identity);
            spawnedInstance = agent.gameObject;

            agent.gameObject.SetActive(false);

            Assert.IsFalse(CharacterAgent.AllLive.Contains(agent), "Sanity check -- AllLive should NOT include a disabled agent.");
            Assert.IsTrue(spawner.OwnedAgents.Contains(agent), "OwnedAgents must still include a disabled agent -- it's the ownership registry, not the selection-scan registry.");
        }

        [Test]
        public void BindRestoredViewNeverGrantsItemsOrMutatesCharacterState()
        {
            var rulesDatabase = new RulesDatabase();
            var session = new GameSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);

            // Simulates SaveService.RestoreCharacters -- the character already exists before any view work.
            var characterId = RuntimeEntityId.NewId();
            var character = new CharacterState(characterId) { Level = 7 };
            session.Characters.Register(character);

            // StartingItemIds here is deliberately irrelevant -- BindRestoredView must never read it.
            var definition = CreateSpawnableNpc("npc-restored", default, new DefinitionId("item-would-be-granted-by-spawnnpc"));

            var instance = spawner.BindRestoredView("restored-spawn", characterId, definition.Id, visualPrefabTemplate, Vector3.zero, Quaternion.identity, default, startInactive: false);
            spawnedInstance = instance;

            Assert.AreEqual(7, character.Level, "BindRestoredView must never mutate CharacterState.");
            Assert.IsEmpty(session.Inventory.AllItems, "BindRestoredView must never grant starting items -- only SpawnNpc does that.");
        }
    }
}
