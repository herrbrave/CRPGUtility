using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Scenes;
using CRPG.Rules;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 3.6: SessionLoadCoordinator.LoadCoroutine's cross-scene extension -- a save captured while scene A
    /// is active, loaded while scene B is active, must load A back in, place the restored party member's view
    /// into "Core" (the test's own base scene, standing in for the persistent Core scene), and leave B unloaded
    /// -- not the reverse.
    /// </summary>
    public class CrossSceneLoadTests
    {
        private const string SceneAPath = "Assets/Scenes/Tests/SceneTravelTestA.unity";
        private const string SceneBPath = "Assets/Scenes/Tests/SceneTravelTestB.unity";

        private string tempDir;
        private string savePath;
        private GameObject hostGo;
        private GameObject partyPrefabTemplate;
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "crpg-crossscene-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            savePath = Path.Combine(tempDir, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
            if (partyPrefabTemplate != null) UnityEngine.Object.DestroyImmediate(partyPrefabTemplate);
            foreach (var go in spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            foreach (var stray in UnityEngine.Object.FindObjectsByType<CharacterAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(stray.gameObject);

            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path == SceneAPath || scene.path == SceneBPath) SceneManager.UnloadSceneAsync(scene);
            }

            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }

        [UnityTest]
        public IEnumerator LoadingASaveCapturedInADifferentSceneLoadsThatSceneAndUnloadsTheCurrentOne()
        {
            var coreScene = SceneManager.GetActiveScene(); // the test's own base scene, standing in for Core

            // Built BEFORE scene A loads/activates -- these must land in coreScene, not in A, or A being
            // unloaded later (every load eventually does this) would destroy them right along with it. This
            // was a real bug in an earlier draft of this test, not in production code.
            hostGo = new GameObject("Host");
            var host = hostGo.AddComponent<GameSessionHost>();
            var rulesDatabase = new RulesDatabase();
            var session = host.StartSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
            var spawner = new WorldSpawnService(session);

            var partyMemberId = RuntimeEntityId.NewId();
            session.Characters.Register(new CharacterState(partyMemberId) { Level = 7 });
            session.Party.AddMember(partyMemberId);
            partyPrefabTemplate = new GameObject("PartyPrefabTemplate");
            spawner.BindRestoredView(partyMemberId.ToString(), partyMemberId, default, partyPrefabTemplate, Vector3.zero, Quaternion.identity, default, startInactive: false);

            yield return SceneManager.LoadSceneAsync(SceneAPath, LoadSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(SceneAPath));

            // Saved while A is active -- SaveRequestService stamps the header with SceneManager.GetActiveScene().path.
            var saveResult = new SaveRequestService(session, spawner, new SafePointGate()).RequestSave(savePath);
            Assert.AreEqual(SaveRequestOutcome.Success, saveResult.Outcome, saveResult.Message);

            // Now "travel" to B by hand (this test targets the load path specifically, not SceneTravelCoordinator).
            yield return SceneManager.LoadSceneAsync(SceneBPath, LoadSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(SceneBPath));
            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(SceneAPath));

            var partyPrefabs = new Dictionary<RuntimeEntityId, GameObject> { [partyMemberId] = partyPrefabTemplate };
            var gate = new WorldTransitionGate();
            var coordinator = new SessionLoadCoordinator(gate: gate);

            LoadResult result = null;
            yield return coordinator.LoadCoroutine(savePath, rulesDatabase, new DiceService(new SeededRandomSource(2)), host, spawner, coreScene, partyPrefabs, r => result = r);

            Assert.AreEqual(LoadOutcome.Success, result.Outcome, result.Message);
            Assert.IsTrue(SceneManager.GetSceneByPath(SceneAPath).isLoaded); // A is back
            Assert.IsFalse(SceneManager.GetSceneByPath(SceneBPath).isLoaded); // B is gone, not A
            Assert.AreEqual(SceneManager.GetSceneByPath(SceneAPath), SceneManager.GetActiveScene());

            host.Session.Characters.TryGetCharacter(partyMemberId, out var restoredCharacter);
            Assert.AreEqual(7, restoredCharacter.Level);

            var restoredAgent = result.WorldSpawnService.OwnedAgents.Single(a => a.EntityId == partyMemberId);
            spawned.Add(restoredAgent.gameObject);
            Assert.AreEqual(coreScene, restoredAgent.gameObject.scene); // party view lands in Core, not in A
        }
    }
}
