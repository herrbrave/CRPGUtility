using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.InputSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.1 (task breakdown item 8): proves Garrick's own real, authored talk-point fixture
    /// recruits him through the REAL click -> TalkInteractionAdapter -> ConversationController -> DialogueRunner
    /// -> RecruitToPartyAction -> DialogueContext.RequestRecruit -> ShellBootstrap.RecruitCompanion chain --
    /// the one integration path RecruitCompanionTests.cs (direct API call) and Act1ContentTests.cs
    /// (DialogueRunner driven directly against Mayor's own dialogue) don't cover: a companion's RecruitToPartyAction
    /// firing through a REAL adapter for the first time in this project. Mirrors TownProofFixturesTests.cs's own
    /// "one click is enough" finding: RecruitToPartyAction/RecordEventAction are both ActionNodes that auto-settle
    /// before the trailing greeting LineNode ever displays, so a single click already proves the recruit.</summary>
    public class Act1RecruitingIntegrationTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private ShellBootstrap shell;
        private string savePath;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-act1-recruit-test-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty; // skip the title screen; real 1-member boot
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");
        }

        [TearDown]
        public void Cleanup()
        {
            // ConversationControllerTests.cs's own established reason for this: leaving Garrick's conversation
            // open (this test never advances past his greeting LineNode) leaks UIToolkitPointerCapture's static
            // modal scope into whatever test runs next in the same suite -- found live (CharacterCreatorScreenTests
            // failed only when run after this one in the full suite, never in isolation). InteractionController.
            // Cancel() is idempotent and safe even if nothing is active.
            var interactionHost = UnityEngine.Object.FindFirstObjectByType<CRPG.Gameplay.Interactions.InteractionControllerBehaviour>();
            interactionHost?.Controller?.Cancel();

            ShellBootstrap.StartModeOverride = null;
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        // Mirrors TownProofFixturesTests.cs's/RecruitCompanionTests.cs's own Click helper (the real click stack).
        private void Click(Vector3 world)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<InputSystemCommandSource>();
            var router = typeof(InputSystemCommandSource).GetProperty("Router", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
            var routerType = router.GetType();
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(world);
            var ray = cam.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            routerType.GetMethod("HandlePointerDown").Invoke(router, new object[] { screen, ray });
            routerType.GetMethod("HandlePointerUp").Invoke(router, new object[] { screen, ray, cam, false });
        }

        [UnityTest]
        public IEnumerator ClickingGarricksRealTalkPointRecruitsHimThroughTheRealDialogueChain()
        {
            var fixtureName = CompanionRoster.FixtureName("garrick");
            var garrickGo = GameObject.Find(fixtureName);
            Assert.IsNotNull(garrickGo, $"{fixtureName} should exist in the real authored Town");

            var session = shell.Services.Session;
            var garrickId = CompanionRoster.IdFor("garrick");
            Assert.IsFalse(session.Party.Members.Contains(garrickId), "not recruited yet");
            var before = session.Party.Members.Count;

            Click(garrickGo.transform.position);

            yield return WaitUntil(() => session.Party.Members.Contains(garrickId), 10f, "RecruitToPartyAction recruited Garrick through the real click/dialogue chain");
            Assert.AreEqual(before + 1, session.Party.Members.Count);
            Assert.AreEqual("Garrick", shell.DisplayNameFor(garrickId));
            Assert.IsTrue(session.Timeline.HasOccurred("companion-recruited"));

            var agent = GateSessionServices.FindAgent(garrickId);
            Assert.IsNotNull(agent, "a real, controllable CharacterAgent exists for the recruit");
        }
    }
}
