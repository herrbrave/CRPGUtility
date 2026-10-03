using System;
using System.Collections;
using System.IO;
using System.Linq;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.2: the data-driven encounter trigger, exercised the way 6.1 never did -- by actually walking a
    /// party member into the REAL authored Town palisade trigger -- plus the review's B8 case (a busy combat session
    /// must never silently consume a second encounter).</summary>
    public class EncounterTriggerTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";

        private ShellBootstrap shell;
        private string savePath;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-trigger-test-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        [UnityTest]
        public IEnumerator WalkingAPartyMemberIntoTheAuthoredPalisadeTriggerSpawnsTheRatsAndStartsTheEncounterOnce()
        {
            var session = shell.Services.Session;
            var trigger = UnityEngine.Object.FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            Assert.AreEqual(EncounterTrigger.PalisadeFiredKey, trigger.FiredKey);
            Assert.IsFalse(session.Timeline.HasOccurred(trigger.FiredKey));

            var agent = GateSessionServices.FindAgent(session.Party.Members[0]);
            agent.Motor.Warp(trigger.transform.position);
            Physics.SyncTransforms();

            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, "the palisade encounter started");
            Assert.IsTrue(session.Timeline.HasOccurred(trigger.FiredKey), "the persisted fired flag is recorded once the encounter really started");
            var rats = CharacterAgentsOf("palisade-breach-triggered-");
            Assert.AreEqual(3, rats, "the default configuration spawns three giant rats");
        }

        [UnityTest]
        public IEnumerator ABusyCombatSessionDoesNotConsumeASecondEncountersTrigger()
        {
            var session = shell.Services.Session;
            var palisade = UnityEngine.Object.FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            var protagonist = GateSessionServices.FindAgent(session.Party.Members[0]);

            // Start the palisade fight by walking in, then drop a second trigger on the same spot while it is active.
            protagonist.Motor.Warp(palisade.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, "the first encounter started");

            var go = new GameObject("SecondTrigger");
            go.transform.position = palisade.transform.position;
            go.AddComponent<SphereCollider>().isTrigger = true;
            var second = go.AddComponent<EncounterTrigger>();
            typeof(EncounterTrigger).GetField("firedKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(second, "second-test-fired");
            second.Session = session;
            second.Spawner = shell.Services.Spawner;
            second.CombatSession = shell.CombatSession;

            yield return new WaitForSeconds(0.5f);
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(session.Timeline.HasOccurred("second-test-fired"), "an encounter that could not start must not be recorded as fired");
            Assert.AreEqual(0, CharacterAgentsOf("second-test-fired-"), "and must not have spawned any hostiles");
            UnityEngine.Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator ALostFightReloadsTheCheckpointFromJustBeforeItAndReArmsTheTrigger()
        {
            var session = shell.Services.Session;
            var trigger = UnityEngine.Object.FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            var protagonistId = session.Party.Members[0];
            var protagonist = GateSessionServices.FindAgent(protagonistId);
            protagonist.Motor.Warp(trigger.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, "the palisade encounter started");
            Assert.IsTrue(File.Exists(shell.CheckpointPath), "a checkpoint was written before the hostiles spawned");
            Assert.IsFalse(File.ReadAllText(shell.CheckpointPath).Contains(EncounterTrigger.PalisadeFiredKey), "the checkpoint predates the fired flag");

            // Wipe the (one-member) party.
            Assert.IsTrue(session.Characters.TryGetCharacter(protagonistId, out var state));
            state.DeathSaves = new CRPG.Actors.DeathSaveState();

            yield return WaitUntil(() => !shell.CombatSession.IsActive, 5f, "the lost encounter ended");
            Assert.AreEqual("The party has fallen...", shell.LastMessage);

            // After the beat the checkpoint reloads into a brand-new session. The restored party stands inside the
            // trigger volume (that is where it was when the fight began), so the re-armed trigger retries at once.
            yield return WaitUntil(() => shell.Services.Session != session && shell.IsReady && !shell.IsLoading, 15f, "the checkpoint reloaded");
            var reloaded = shell.Services.Session;
            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, "the re-armed trigger fired again");
            Assert.IsTrue(reloaded.Characters.TryGetCharacter(protagonistId, out var restored));
            Assert.IsNull(restored.DeathSaves, "the protagonist is back on their feet for the retry");
            Assert.AreEqual(3, CharacterAgentsOf("palisade-breach-triggered-"), "a fresh set of hostiles, not the old ones");
        }

        private int CharacterAgentsOf(string spawnIdPrefix) =>
            Enumerable.Range(1, 8).Count(n => shell.Services.Spawner.HasRecord($"{spawnIdPrefix}{n}"));
    }
}
