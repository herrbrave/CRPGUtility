using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Gate;
using BarrowsEnd.Shell;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.InputSystem;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>The palisade fight played the way a player plays it: New Game through the real creator (a Barbarian),
    /// recruit a companion, walk into the REAL Town trigger, then take turns through the real ability bar and real
    /// clicks -- no weakened rats, no authored-party shortcut. Exists because two earlier rounds of tests passed while
    /// live play still showed "Strike does nothing" and "rats do no damage".</summary>
    public class RealPlayCombatTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private ShellBootstrap shell;
        private string savePath;
        private RenderTexture rt;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-realplay-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.Title;
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            shell.SavePath = savePath;
            rt = new RenderTexture(1920, 1080, 24);
            Camera.main.targetTexture = rt;
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Title, 20f, "the title");
            yield return Seconds(0.3f);
            UiClick.Pointer(RootOf<ShellTitleScreen>().Q<Button>("new-game-button"));
            yield return WaitUntil(() => shell.Phase == ShellStartPhase.Creating, 5f, "the creator");
            yield return Seconds(0.3f);
            shell.CreatorModel.ClassRow.Select(new DefinitionId("class-barbarian"));
            yield return null;
            UiClick.Pointer(RootOf<CRPG.UI.CharacterCreatorScreen>().Q<Button>("confirm-button"));
            yield return WaitUntil(() => shell.IsReady, 30f, "the shell finished booting");
            yield return Seconds(0.3f);
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (Camera.main != null) Camera.main.targetTexture = null;
            if (rt != null) { rt.Release(); rt = null; }
            if (File.Exists(savePath)) File.Delete(savePath);
            if (File.Exists(savePath + ".checkpoint")) File.Delete(savePath + ".checkpoint");
        }

        private static VisualElement RootOf<T>() where T : Component => UnityEngine.Object.FindFirstObjectByType<T>().GetComponent<UIDocument>().rootVisualElement;
        private VisualElement Hud() => UnityEngine.Object.FindFirstObjectByType<HudRoot>().GetComponent<UIDocument>().rootVisualElement;

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds) { elapsed += Mathf.Min(Time.deltaTime, 0.1f); yield return null; }
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

        private int Hp(RuntimeEntityId id) =>
            shell.Services.Session.Characters.TryGetCharacter(id, out var c) && c.TryGetResource(HitPoints, out var hp) ? hp.Current : -1;

        private RuntimeEntityId? CurrentTurn() => shell.CombatSession.CurrentTurnId;

        [UnityTest]
        public IEnumerator ABarbarianPlayerCanActuallyHurtTheRatsAndTheRatsActuallyHurtTheParty()
        {
            shell.RecruitCompanion("silas");
            yield return null;
            var session = shell.Services.Session;
            var protagonistId = session.Party.Members[0];
            var trigger = UnityEngine.Object.FindObjectsByType<EncounterTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();

            GateSessionServices.FindAgent(protagonistId).Motor.Warp(trigger.transform.position);
            Physics.SyncTransforms();
            yield return WaitUntil(() => shell.CombatSession.IsActive, 5f, "the palisade encounter started");
            yield return Seconds(0.5f);

            var ratIds = Enumerable.Range(1, 3).Select(n => (ok: shell.Services.Spawner.TryGetCharacterId($"{EncounterTrigger.PalisadeFiredKey}-{n}", out var id, out _, out _), id)).Where(t => t.ok).Select(t => t.id).ToList();
            Assert.AreEqual(3, ratIds.Count, "three rats spawned");

            var partyIds = session.Party.Members.ToList();
            var partyHpStart = partyIds.Sum(Hp);
            var ratHpStart = ratIds.Sum(Hp);
            Debug.Log($"[RealPlay] party HP {partyHpStart}, rat HP {ratHpStart}, turn={DescribeTurn()}");

            // Play up to 12 turns of the party: step toward the nearest rat, then Strike it.
            var strikeAttempts = 0;
            for (var guard = 0; guard < 400 && ratIds.Sum(Hp) == ratHpStart; guard++)
            {
                var turn = CurrentTurn();
                if (turn.HasValue && partyIds.Contains(turn.Value))
                {
                    var actor = GateSessionServices.FindAgent(turn.Value);
                    var rat = ratIds.Where(r => Hp(r) > 0).Select(GateSessionServices.FindAgent).Where(a => a != null)
                        .OrderBy(a => Vector3.Distance(a.transform.position, actor.transform.position)).FirstOrDefault();
                    if (rat == null) break;

                    var distance = Vector3.Distance(actor.transform.position, rat.transform.position);
                    if (distance > 1.3f)
                    {
                        var toward = Vector3.MoveTowards(actor.transform.position, rat.transform.position, distance - 1.0f);
                        Click(toward);
                        yield return Seconds(2.5f);
                    }

                    var strike = Hud().Query<Button>().ToList().FirstOrDefault(b => b.text == "Strike");
                    Assert.IsNotNull(strike, $"a Strike button on {DescribeTurn()}'s turn");
                    Debug.Log($"[RealPlay] Strike enabled={strike.enabledSelf}, dist={Vector3.Distance(actor.transform.position, rat.transform.position):F2}");
                    UiClick.Pointer(strike);
                    yield return null;
                    Click(rat.transform.position);
                    strikeAttempts++;
                    yield return Seconds(1.0f);
                    var feedback = Hud().Query<Label>().ToList().FirstOrDefault(l => l.text != null && l.text.StartsWith("Strike:"));
                    Debug.Log($"[RealPlay] after Strike: rat HP {ratIds.Sum(Hp)} (was {ratHpStart}); feedback='{feedback?.text}'; turn={DescribeTurn()}");
                }
                else yield return Seconds(0.3f);
            }

            Assert.Less(ratIds.Sum(Hp), ratHpStart, $"Strike hurt a rat after {strikeAttempts} real attempts");

            // Now let the rats take their turns: pass every party turn and the party's total HP must drop.
            for (var guard = 0; guard < 60 && partyIds.Sum(Hp) >= partyHpStart; guard++)
            {
                var turn = CurrentTurn();
                if (turn.HasValue && partyIds.Contains(turn.Value))
                {
                    var endTurn = Hud().Query<Button>().ToList().FirstOrDefault(b => b.text == "End Turn");
                    if (endTurn != null) UiClick.Pointer(endTurn);
                }

                yield return Seconds(0.4f);
            }

            Debug.Log($"[RealPlay] party HP {partyIds.Sum(Hp)} (was {partyHpStart}) after letting the rats act");
            Assert.Less(partyIds.Sum(Hp), partyHpStart, "the rats' bites hurt the party");
        }

        private string DescribeTurn()
        {
            var turn = CurrentTurn();
            return turn.HasValue ? (shell.DisplayNameFor(turn.Value) ?? "hostile") : "none";
        }
    }
}
