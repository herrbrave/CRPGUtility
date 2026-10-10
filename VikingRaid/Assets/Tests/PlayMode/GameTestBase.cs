using System;
using System.Collections;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>
    /// Shared fixture for the game-loop tests: loads the REAL scene with a REAL <see cref="PlayerCommandRouter"/> injected as the input (so clicks go through the hittable mask and the
    /// collider layers exactly as in play, not around them), and offers a click helper that aims a camera ray at a world point. Fights are run at a raised time scale (the Task 4 review:
    /// a live E1 takes about a minute), and assert outcome classes rather than traces (frame timing changes AI choices; seeded dice cannot make a live fight deterministic).
    /// </summary>
    public abstract class GameTestBase
    {
        protected VikingGame Game;
        protected Camera Cam;
        protected PlayerCommandRouter Router;

        private sealed class NoUiCapture : IPointerCaptureProvider { public bool IsPointerOverUI(Vector2 screenPosition) => false; }

        protected virtual int Seed => 12345;

        private readonly System.Collections.Generic.List<string> loggedErrors = new();
        private void OnLog(string condition, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) loggedErrors.Add($"{type}: {condition}"); }

        /// <summary>No error or exception was logged since the test began (warnings, such as the watchdog's, are allowed).</summary>
        protected void AssertNoErrorsLogged() => Assert.IsEmpty(loggedErrors, string.Join("\n", loggedErrors));

        [UnitySetUp]
        public IEnumerator BaseSetUp()
        {
            Checkpoint.Reset();
            Time.timeScale = 1f;
            loggedErrors.Clear();
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            LogAssert.ignoreFailingMessages = true;   // errors are collected by OnLog and asserted explicitly by the tests that care
            InstallInput();
            yield return LoadScene();
        }

        [TearDown]
        public void BaseTearDown()
        {
            VikingGame.InputFactory = null;
            Checkpoint.Reset();
            Time.timeScale = 1f;
            Application.logMessageReceived -= OnLog;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTearDown]
        public IEnumerator UnloadWorldAfterTest() => SceneIsolation.UnloadAll();

        protected void InstallInput()
        {
            VikingGame.InputFactory = () =>
            {
                Router = new PlayerCommandRouter(new SceneSelectableSource(), new NoUiCapture(), WorldLayers.Hittable);
                return new VikingGame.InputBinding { Source = Router, SetGate = gate => Router.TargetingGate = gate };
            };
        }

        protected IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/VikingRaid.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return WaitUntil(() => VikingGame.Instance != null && VikingGame.Instance.Session != null && VikingGame.Instance.State != GameState.Booting, 10f, "the game to boot");
            Game = VikingGame.Instance;
            Game.Seed = Seed;
            Cam = Camera.main;
            yield return null; yield return null;   // let spawned agents bind and the camera settle
        }

        protected static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
        {
            var start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds) Assert.Fail($"timed out after {timeoutSeconds:F0}s waiting for {what}");
                yield return null;
            }
        }

        /// <summary>A pointer click on a world point, aimed through the camera like a real click (so the hittable mask and collider layers apply).</summary>
        protected void Click(Vector3 worldPoint)
        {
            var screen = (Vector2)Cam.WorldToScreenPoint(worldPoint);
            var ray = Cam.ScreenPointToRay(screen);
            Router.HandlePointerDown(screen, ray);
            Router.HandlePointerUp(screen, ray, Cam, false);
        }

        /// <summary>Places the whole party inside an encounter trigger box, as walking into it would.</summary>
        protected IEnumerator EnterTrigger(int encounter)
        {
            var trigger = Game.Anchors.encounters[encounter].trigger;
            for (var i = 0; i < Game.Heroes.Count; i++)
            {
                var spot = trigger.bounds.center; spot.y = 0f;
                spot += trigger.transform.forward * ((i - 1.5f) * 0.8f);
                Assert.IsTrue(VikingGame.FindAgent(Game.Heroes[i]).Motor.Warp(spot), $"could not place hero {i} in trigger {encounter + 1}");
            }

            yield return null;
        }

        /// <summary>Reloads the scene as if the game had been played up to a checkpoint: <paramref name="encountersWon"/> fights won and a post-wipe boot.</summary>
        protected IEnumerator ReloadAtCheckpoint(int encountersWon)
        {
            Checkpoint.Reset();
            for (var i = 0; i < encountersWon; i++) Checkpoint.EncounterWon(i);
            Checkpoint.WipeReload();
            yield return LoadScene();
        }

        /// <summary>The real zero-hit-point path: hit points to 0 and the events DamageEffect publishes, so the real DeathSaveService reacts (writing HP alone sets no death saves).</summary>
        protected void DropToZero(RuntimeEntityId target)
        {
            Game.Session.Characters.TryGetCharacter(target, out var state);
            state.TryGetResource(PartyVitals.Hp, out var hp);
            hp.Spend(hp.Current);
            Game.Session.EventBus.Publish(new GameEventContext(GameEvent.AfterDamage, target, target));
            var dropped = new GameEventContext(GameEvent.CreatureDroppedToZeroHP, target, target);
            dropped.Data["Overkill"] = 0; dropped.Data["IsCritical"] = false;
            Game.Session.EventBus.Publish(dropped);
        }

        protected IEnumerator PlayFight(ScriptedParty party, float timeoutSeconds, System.Action each = null)
        {
            var start = Time.realtimeSinceStartup;
            while (Game.State == GameState.Fighting)
            {
                party.Tick();
                each?.Invoke();
                if (Time.realtimeSinceStartup - start > timeoutSeconds) Assert.Fail($"the fight did not finish in {timeoutSeconds:F0} s (abilities {party.AbilitiesStarted}, moves {party.Moves}, turns ended {party.TurnsEnded}, watchdog cancels {Game.Combat.WatchdogCancels})");
                yield return null;
            }
        }

        /// <summary>One line of combat state for failure messages: who is up, whether the scheduler is busy, the round.</summary>
        protected string Describe()
        {
            if (Game == null) return "no game";
            if (!Game.Combat.IsActive) return $"state={Game.State}, no active fight";
            var encounter = Game.Combat.Controller.EncounterService.Encounter;
            var turn = encounter?.CurrentTurn;
            var who = turn == null ? "nobody" : (Game.Heroes.Contains(turn.Id) ? "hero " + GameDesign.Heroes.ElementAt(Game.Heroes.ToList().IndexOf(turn.Id)).Id : "goblin") + (Game.Session.Characters.TryGetCharacter(turn.Id, out var s) && PartyVitals.IsDown(s) ? " (down)" : "");
            return $"state={Game.State}, round={encounter?.Round}, turn={who}, schedulerBusy={Game.Combat.Controller.Scheduler.IsBusy} ({Game.Combat.Controller.Scheduler.CurrentAction?.GetType().Name}), combatants={encounter?.Combatants.Count}, watchdogCancels={Game.Combat.WatchdogCancels}";
        }

        protected Vector3 Position(RuntimeEntityId id) => VikingGame.FindAgent(id).transform.position;
        protected Vector3 PartyCentre() => Game.Heroes.Aggregate(Vector3.zero, (sum, h) => sum + Position(h)) / Game.Heroes.Count;
    }
}
