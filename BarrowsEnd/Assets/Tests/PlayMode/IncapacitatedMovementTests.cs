using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Actors;
using CRPG.Core;
using CRPG.InputSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>Live play: "I can still move dead party members around". A downed (dying) or dead (0 HP) member must stay where they
    /// fell when the player orders the party somewhere; everyone else still goes.</summary>
    public class IncapacitatedMovementTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private ShellBootstrap shell;
        private string savePath;
        private RenderTexture rt;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-incap-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty;
            ShellBootstrap.MembersOverride = GateLayout.DefaultMembers(); // the old 4-member roster
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            rt = new RenderTexture(1920, 1080, 24); // a real-sized view, so a ground click is not under the HUD panels
            Camera.main.targetTexture = rt;
            var waited = 0f;
            while (!shell.IsReady && waited < 20f) { waited += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(shell.IsReady);
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            ShellBootstrap.MembersOverride = null;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (rt != null) { rt.Release(); rt = null; }
            if (File.Exists(savePath)) File.Delete(savePath);
        }

        private void Click(Vector3 world)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<InputSystemCommandSource>();
            var router = typeof(InputSystemCommandSource).GetProperty("Router", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(world);
            var ray = cam.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            router.GetType().GetMethod("HandlePointerDown").Invoke(router, new object[] { screen, ray });
            router.GetType().GetMethod("HandlePointerUp").Invoke(router, new object[] { screen, ray, cam, false });
        }

        [UnityTest]
        public IEnumerator ADownedMemberAndADeadMemberStayPutWhileTheRestOfThePartyWalks()
        {
            var session = shell.Services.Session;
            var members = session.Party.Members.ToList();
            Assert.AreEqual(4, members.Count);
            var downed = members[1];
            var dead = members[2];

            session.Characters.TryGetCharacter(downed, out var downedState);
            downedState.DeathSaves = new DeathSaveState();
            session.Characters.TryGetCharacter(dead, out var deadState);
            deadState.TryGetResource(HitPoints, out var deadHp);
            deadHp.Spend(deadHp.Current);
            Assert.AreEqual(0, deadHp.Current);

            session.Party.SelectAll();
            var before = members.ToDictionary(m => m, m => GateSessionServices.FindAgent(m).transform.position);
            var hero = GateSessionServices.FindAgent(members[0]);
            Click(hero.transform.position + new Vector3(4f, 0f, 0f));
            yield return new WaitForSeconds(2.5f);

            float Moved(RuntimeEntityId id) => Vector3.Distance(before[id], GateSessionServices.FindAgent(id).transform.position);
            Assert.Less(Moved(downed), 0.1f, "a downed member is not walked around");
            Assert.Less(Moved(dead), 0.1f, "neither is a dead one");
            Assert.Greater(Moved(members[0]), 0.5f, "the healthy members still move");
            Assert.Greater(Moved(members[3]), 0.5f);
        }
    }
}
