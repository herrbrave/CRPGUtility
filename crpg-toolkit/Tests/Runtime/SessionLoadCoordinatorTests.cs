using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Rules;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 3.5: SessionLoadCoordinator's load transaction against a real GameSessionHost/CharacterAgent/
    /// WorldSpawnService -- build-and-validate-first, commit-last. Builds NpcDefinitions via a small local
    /// reflection helper since Tests/Editor's TestDefinitionFactory isn't visible from this PlayMode assembly
    /// (mirrors ConversationControllerTests/CharacterBindingTests' own precedent).
    /// </summary>
    public class SessionLoadCoordinatorTests
    {
        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }

        private static NpcDefinition CreateNpcDefinition(string id, GameObject visualPrefab)
        {
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            SetField(definition, "id", id);
            SetField(definition, "displayName", id);
            SetField(definition, "visualPrefab", visualPrefab);
            return definition;
        }

        private string tempDir;
        private string savePath;
        private RulesDatabase rulesDatabase;
        private GameObject hostGo;
        private GameObject npcPrefabTemplate;
        private GameObject partyPrefabTemplate;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "crpg-sessionload-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            savePath = Path.Combine(tempDir, "save.json");
            rulesDatabase = new RulesDatabase();
        }

        [TearDown]
        public void TearDown()
        {
            if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
            if (npcPrefabTemplate != null) UnityEngine.Object.DestroyImmediate(npcPrefabTemplate);
            if (partyPrefabTemplate != null) UnityEngine.Object.DestroyImmediate(partyPrefabTemplate);
            foreach (var stray in UnityEngine.Object.FindObjectsByType<CharacterAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(stray.gameObject);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }

        private (GameSessionHost host, WorldSpawnService spawner, RuntimeEntityId partyMemberId, RuntimeEntityId npcId) BuildInitialWorld(NpcDefinition npcDefinition = null)
        {
            hostGo = new GameObject("Host");
            var host = hostGo.AddComponent<GameSessionHost>();
            var session = host.StartSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);

            var partyMemberId = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(partyMemberId) { Level = 1 });
            session.Party.AddMember(partyMemberId);
            partyPrefabTemplate = new GameObject("PartyPrefabTemplate");
            spawner.BindRestoredView(partyMemberId.ToString(), partyMemberId, default, partyPrefabTemplate, new Vector3(0, 0, 0), Quaternion.identity, default, startInactive: false);

            npcPrefabTemplate = new GameObject("NpcPrefabTemplate");
            npcDefinition ??= CreateNpcDefinition("npc-guard", npcPrefabTemplate);
            rulesDatabase.Register(npcDefinition);
            var npcAgent = spawner.SpawnNpc("npc-guard-spawn", npcDefinition, new Vector3(5, 0, 5), Quaternion.identity);

            return (host, spawner, partyMemberId, npcAgent.EntityId);
        }

        private Dictionary<RuntimeEntityId, GameObject> PartyPrefabs(RuntimeEntityId partyMemberId) =>
            new() { [partyMemberId] = partyPrefabTemplate };

        [UnityTest]
        public IEnumerator FullSameSceneRoundTripRestoresStateAndReplacesWorldAgentsWithoutDuplication()
        {
            var (host, spawner, partyMemberId, npcId) = BuildInitialWorld();
            host.Session.Characters.TryGetCharacter(npcId, out var npcBeforeSave);
            npcBeforeSave.Level = 5;

            var saveResult = new SaveRequestService(host.Session, spawner, new SafePointGate()).RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome);

            npcBeforeSave.Level = 99; // live drift after the save point -- load must overwrite this, not keep it
            host.Session.Characters.TryGetCharacter(npcId, out var oldNpcCharacter);
            var oldNpcAgent = spawner.OwnedAgents.Single(a => a.EntityId == npcId);
            var oldPartyAgent = spawner.OwnedAgents.Single(a => a.EntityId == partyMemberId);

            var coordinator = new SessionLoadCoordinator();
            var result = coordinator.Load(savePath, rulesDatabase, new DiceService(new SeededRandomSource(2)), host, spawner, PartyPrefabs(partyMemberId));

            Assert.AreEqual(LoadOutcome.Success, result.Outcome, result.Message);
            Assert.IsFalse(oldNpcAgent.IsBound, "Old NPC agent must be unbound synchronously during the commit phase.");
            Assert.IsFalse(oldPartyAgent.IsBound, "Old party agent must be unbound synchronously during the commit phase.");

            yield return null; // let deferred Destroy() actually land
            Assert.IsTrue(oldNpcAgent == null, "Old NPC GameObject should be destroyed.");
            Assert.IsTrue(oldPartyAgent == null, "Old party GameObject should be destroyed.");

            Assert.AreSame(result.Session, host.Session, "AdoptSession must leave the host pointing at the new session.");
            Assert.IsTrue(result.Session.Characters.TryGetCharacter(npcId, out var restoredNpc));
            Assert.AreEqual(5, restoredNpc.Level, "Restored state must reflect the SAVED snapshot, not the live drift after it.");

            var newNpcAgents = result.WorldSpawnService.OwnedAgents.Where(a => a.EntityId == npcId).ToList();
            Assert.AreEqual(1, newNpcAgents.Count, "Exactly one live agent for the NPC -- no duplicate spawn.");
            Assert.AreEqual(new Vector3(5, 0, 5), newNpcAgents[0].transform.position, "Restored view must be placed at the saved position.");
        }

        [Test]
        public void AValidationFailureLeavesTheOldSessionCompletelyUntouched()
        {
            var (host, spawner, _, _) = BuildInitialWorld();
            var originalSession = host.Session;

            var badData = new SaveData();
            badData.Header.FormatVersion = SaveData.CurrentFormatVersion;
            badData.Header.ScenePath = SceneManager.GetActiveScene().path;
            badData.Quests.Add(new QuestSaveData { QuestId = "quest-does-not-exist" });
            new SaveFileStore().Save(savePath, SaveService.ToJson(badData));

            var coordinator = new SessionLoadCoordinator();
            var result = coordinator.Load(savePath, rulesDatabase, new DiceService(new SeededRandomSource(1)), host, spawner);

            Assert.AreEqual(LoadOutcome.ContentValidationFailed, result.Outcome);
            Assert.AreSame(originalSession, host.Session);
            Assert.IsFalse(originalSession.IsDisposed);
        }

        [Test]
        public void ASceneMismatchIsRejectedBeforeTouchingAnything()
        {
            var (host, spawner, _, _) = BuildInitialWorld();
            var originalSession = host.Session;

            var badData = new SaveData();
            badData.Header.FormatVersion = SaveData.CurrentFormatVersion;
            badData.Header.ScenePath = "Assets/Scenes/SomeOtherSceneThatIsNotThisOne.unity";
            new SaveFileStore().Save(savePath, SaveService.ToJson(badData));

            var coordinator = new SessionLoadCoordinator();
            var result = coordinator.Load(savePath, rulesDatabase, new DiceService(new SeededRandomSource(1)), host, spawner);

            Assert.AreEqual(LoadOutcome.SceneMismatch, result.Outcome);
            Assert.AreSame(originalSession, host.Session);
            Assert.IsFalse(originalSession.IsDisposed);
        }

        [Test]
        public void AViewBindingFailureAfterAtLeastOneStagedViewLeavesTheOldSessionUsable()
        {
            var (host, spawner, partyMemberId, firstNpcId) = BuildInitialWorld();
            host.Session.Characters.TryGetCharacter(firstNpcId, out var firstNpc);
            firstNpc.Level = 3;

            // A second NPC definition with NO VisualPrefab -- resolvable content (passes ContentValidator,
            // which doesn't check VisualPrefab), but view-binding must fail on it.
            var brokenDefinition = CreateNpcDefinition("npc-broken", null);
            rulesDatabase.Register(brokenDefinition);
            var brokenCharacterId = RuntimeEntityId.NewId();
            host.Session.Characters.Register(new CharacterState(brokenCharacterId));

            var data = new SaveData();
            data.Header.FormatVersion = SaveData.CurrentFormatVersion;
            data.Header.ScenePath = SceneManager.GetActiveScene().path;
            data.Characters.Add(new CharacterSaveData { Id = firstNpcId.ToString(), Level = 3 });
            data.Characters.Add(new CharacterSaveData { Id = brokenCharacterId.ToString() });
            data.WorldSpawns.Add(new WorldSpawnSaveEntry { SpawnId = "npc-guard-spawn", CharacterId = firstNpcId.ToString(), NpcDefinitionId = "npc-guard", PosX = 5, PosZ = 5 });
            data.WorldSpawns.Add(new WorldSpawnSaveEntry { SpawnId = "npc-broken-spawn", CharacterId = brokenCharacterId.ToString(), NpcDefinitionId = "npc-broken" });
            new SaveFileStore().Save(savePath, SaveService.ToJson(data));

            var originalSession = host.Session;
            var oldNpcAgent = spawner.OwnedAgents.Single(a => a.EntityId == firstNpcId);

            var coordinator = new SessionLoadCoordinator();
            var result = coordinator.Load(savePath, rulesDatabase, new DiceService(new SeededRandomSource(1)), host, spawner);

            Assert.AreEqual(LoadOutcome.ViewBindingFailed, result.Outcome);
            Assert.AreSame(originalSession, host.Session, "The old session must still be the live one.");
            Assert.IsFalse(originalSession.IsDisposed);
            Assert.IsTrue(oldNpcAgent.IsBound, "The old, already-working NPC agent must be untouched.");
            Assert.IsTrue(oldNpcAgent != null && oldNpcAgent.gameObject != null);
        }

        [UnityTest]
        public IEnumerator ADisabledCharacterAgentBelongingToTheOldSessionIsStillTornDown()
        {
            var (host, spawner, partyMemberId, npcId) = BuildInitialWorld();
            var npcAgent = spawner.OwnedAgents.Single(a => a.EntityId == npcId);
            npcAgent.gameObject.SetActive(false); // CharacterAgent.AllLive would now silently skip this one

            var saveResult = new SaveRequestService(host.Session, spawner, new SafePointGate()).RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome);

            var coordinator = new SessionLoadCoordinator();
            var result = coordinator.Load(savePath, rulesDatabase, new DiceService(new SeededRandomSource(1)), host, spawner, PartyPrefabs(partyMemberId));

            Assert.AreEqual(LoadOutcome.Success, result.Outcome, result.Message);
            Assert.IsFalse(npcAgent.IsBound, "A disabled agent must still be unbound during teardown.");

            yield return null;
            Assert.IsTrue(npcAgent == null, "A disabled agent must still be destroyed during teardown.");
        }

        // --- Task 5.2 gap fix: a session started via IRulesetDefinition must keep its registered policies
        // after a save/load round trip, and a save recorded for a different ruleset must be rejected before
        // touching the old session. Local fakes mirror GameSessionTests.FakeRulesetProvider's shape (not
        // visible from this PlayMode assembly). ---

        private interface IFakePolicy
        {
        }

        private sealed class FakePolicy : IFakePolicy
        {
        }

        private sealed class FakeRulesetProvider : IRulesetDefinition
        {
            public FakeRulesetProvider(string rulesetId = "fake-ruleset") => RulesetId = rulesetId;
            public string RulesetId { get; }
            public void Register(IRulesetRegistrationBuilder builder) => builder.RegisterPolicy<IFakePolicy>(new FakePolicy());
        }

        [Test]
        public void ARulesetAwareRoundTripKeepsTheRegisteredPolicyAfterLoad()
        {
            hostGo = new GameObject("RulesetHost");
            var host = hostGo.AddComponent<GameSessionHost>();
            var provider = new FakeRulesetProvider();
            var session = host.StartSession(provider, new DiceService(new SeededRandomSource(1)), new[] { typeof(IFakePolicy) });
            Assert.IsTrue(session.Policies.TryGet<IFakePolicy>(out _));

            var spawner = new WorldSpawnService(session);
            var saveResult = new SaveRequestService(session, spawner, new SafePointGate(), rulesetId: provider.RulesetId).RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome);

            var coordinator = new SessionLoadCoordinator();
            var result = coordinator.Load(savePath, provider, new DiceService(new SeededRandomSource(2)), new[] { typeof(IFakePolicy) }, host, spawner);

            Assert.AreEqual(LoadOutcome.Success, result.Outcome, result.Message);
            Assert.IsTrue(result.Session.Policies.TryGet<IFakePolicy>(out _), "The reloaded session must still have the ruleset's policy registered.");
        }

        [Test]
        public void ALoadForADifferentRulesetIsRejectedBeforeTouchingTheOldSession()
        {
            hostGo = new GameObject("RulesetHost");
            var host = hostGo.AddComponent<GameSessionHost>();
            var originalProvider = new FakeRulesetProvider("ruleset-a");
            var session = host.StartSession(originalProvider, new DiceService(new SeededRandomSource(1)), new[] { typeof(IFakePolicy) });
            var spawner = new WorldSpawnService(session);

            var saveResult = new SaveRequestService(session, spawner, new SafePointGate(), rulesetId: "ruleset-a").RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome);

            var originalSession = host.Session;
            var mismatchedProvider = new FakeRulesetProvider("ruleset-b");
            var coordinator = new SessionLoadCoordinator();
            var result = coordinator.Load(savePath, mismatchedProvider, new DiceService(new SeededRandomSource(2)), new[] { typeof(IFakePolicy) }, host, spawner);

            Assert.AreEqual(LoadOutcome.RulesetMismatch, result.Outcome);
            Assert.AreSame(originalSession, host.Session);
            Assert.IsFalse(originalSession.IsDisposed);
        }

        [Test]
        public void SaveLoadSaveLoadKeepsADefeatedNpcDefeated()
        {
            var (host, spawner, partyMemberId, npcId) = BuildInitialWorld();
            spawner.MarkDefeated(npcId);

            var saveService = new SaveRequestService(host.Session, spawner, new SafePointGate());
            Assert.AreEqual(SaveRequestOutcome.Success, saveService.RequestSave(savePath).Outcome);

            var coordinator = new SessionLoadCoordinator();
            var firstLoad = coordinator.Load(savePath, rulesDatabase, new DiceService(new SeededRandomSource(1)), host, spawner, PartyPrefabs(partyMemberId));
            Assert.AreEqual(LoadOutcome.Success, firstLoad.Outcome, firstLoad.Message);
            Assert.IsFalse(firstLoad.WorldSpawnService.OwnedAgents.Any(a => a.EntityId == npcId), "A defeated NPC must not be respawned.");

            var secondSaveService = new SaveRequestService(firstLoad.Session, firstLoad.WorldSpawnService, new SafePointGate());
            Assert.AreEqual(SaveRequestOutcome.Success, secondSaveService.RequestSave(savePath).Outcome);

            var secondLoad = coordinator.Load(savePath, rulesDatabase, new DiceService(new SeededRandomSource(1)), host, firstLoad.WorldSpawnService, PartyPrefabs(partyMemberId));
            Assert.AreEqual(LoadOutcome.Success, secondLoad.Outcome, secondLoad.Message);
            Assert.IsFalse(secondLoad.WorldSpawnService.OwnedAgents.Any(a => a.EntityId == npcId), "The NPC must still be reported defeated after a second save/load round-trip.");
        }
    }
}
