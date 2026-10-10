using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>
    /// Task 5, the scripted playthrough: the whole game from boot to the Camp Destroyed overlay as the player would do it. Between fights the party is WALKED with real pointer clicks through the
    /// real router (each aimed through the camera at a point on the road, kept inside the view like a player's clicks); fights are played by <see cref="ScriptedParty"/> through the real combat
    /// session; a wipe is not a failure but the game's own retry (banner, scene reload, checkpoint), because E4 is balanced to be beaten about two times in three. The run fails if it cannot finish
    /// within a handful of wipes, if the party gets stuck walking, if a fight hangs, or if anything logs an error on the way.
    /// </summary>
    public class GameFullRunTests : GameTestBase
    {
        private const int MaxWipes = 8;

        /// <summary>Where along the road (metres) the party currently is: the nearest sample of the centreline.</summary>
        private float PartyRoadS()
        {
            var centre = PartyCentre(); var best = 0f; var bestD = float.MaxValue;
            for (var s = 0f; s <= WorldLayout.RoadLength; s += 1f)
            {
                var d = (WorldLayout.PointAt(s) - centre).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }

            return best;
        }

        private bool OnScreen(Vector3 world)
        {
            var v = Cam.WorldToViewportPoint(world);
            return v.z > 0f && v.x > 0.12f && v.x < 0.88f && v.y > 0.12f && v.y < 0.88f;
        }

        private bool PartyStopped() => Game.Heroes.All(h => VikingGame.FindAgent(h).Motor.Snapshot.Velocity.sqrMagnitude < 0.01f);

        [UnityTest, Timeout(2_400_000)]
        public IEnumerator TheWholeGameCanBePlayedFromBootToTheCampDestroyedOverlay()
        {
            Time.timeScale = 4f;
            var wipes = 0; var fights = 0; var clicks = 0;
            var wonFirstTime = new List<int>();
            var frameSum = 0f; var frameCount = 0;
            var runStart = Time.realtimeSinceStartup;
            Assert.AreEqual(GameState.Exploring, Game.State);

            while (!Checkpoint.CampDestroyed)
            {
                var encounter = Checkpoint.Index;
                var arena = WorldLayout.Arenas[encounter];

                // 1. walk to the trigger with real clicks: one waypoint at a time, always a visible point on the road ahead
                var walkStart = Time.realtimeSinceStartup; var lastProgress = Time.realtimeSinceStartup; var lastS = PartyRoadS();
                while (Game.State == GameState.Exploring)
                {
                    if (Time.realtimeSinceStartup - walkStart > 240f) Assert.Fail($"the party never reached encounter {encounter + 1}'s trigger (at road {PartyRoadS():F0} of {arena.TriggerS:F0}; clicks {clicks})");
                    frameSum += Time.unscaledDeltaTime; frameCount++;
                    if (PartyStopped())
                    {
                        var s = PartyRoadS();
                        if (s > lastS + 0.5f) { lastS = s; lastProgress = Time.realtimeSinceStartup; }
                        else if (Time.realtimeSinceStartup - lastProgress > 20f) Assert.Fail($"the party is stuck at road {s:F0} on the way to encounter {encounter + 1} after {clicks} clicks");
                        for (var reach = 22f; reach >= 4f; reach -= 6f)
                        {
                            var waypoint = WorldLayout.PointAt(Mathf.Min(s + reach, arena.TriggerS + 1f));
                            if (!OnScreen(waypoint)) continue;
                            Click(waypoint); clicks++;
                            break;
                        }
                    }

                    yield return null;
                }

                // 2. the staging walk, then the fight, played by the scripted party
                yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, $"encounter {encounter + 1}'s fight to start");
                fights++;
                var old = Game;
                yield return PlayFight(new ScriptedParty(Game), 420f);

                // 3. the result: a win leaves the checkpoint one further; a wipe reloads the scene and the loop retries from the checkpoint
                if (old.State == GameState.Defeated)
                {
                    wipes++;
                    Assert.LessOrEqual(wipes, MaxWipes, $"the party was wiped {wipes} times; the game is too hard or the scripted party too weak to finish");
                    yield return WaitUntil(() => VikingGame.Instance != null && VikingGame.Instance != old && VikingGame.Instance.Session != null && VikingGame.Instance.State == GameState.Exploring, 30f, "the scene to reload after the wipe");
                    Game = VikingGame.Instance; Cam = Camera.main;
                    yield return null; yield return null;
                    Assert.AreEqual(encounter, Checkpoint.Index, "a wipe must not lose or skip progress");
                }
                else if (!Checkpoint.CampDestroyed)
                {
                    Assert.AreEqual(encounter + 1, Checkpoint.Index, $"winning encounter {encounter + 1} must advance the checkpoint");
                    Assert.AreEqual(GameState.Exploring, Game.State);
                    if (wipes == 0) wonFirstTime.Add(encounter);
                }
            }

            Assert.AreEqual(GameState.Won, Game.State);
            Assert.IsTrue(Game.OverlayShown, "the Camp destroyed overlay");
            Assert.IsNotNull(Game.OverlayElement.Q<Button>("overlay-button"));
            var seconds = Time.realtimeSinceStartup - runStart;
            Debug.Log($"[Task5] full run done: {fights} fights, {wipes} wipes, {clicks} walking clicks, {seconds:F0} s real time at 4x; first-try wins {string.Join(",", wonFirstTime.Select(e => e + 1))}; walking frame time {(frameCount > 0 ? frameSum / frameCount * 1000f : 0f):F1} ms (batch rendering, not a measure of the player's machine)");
            AssertNoErrorsLogged();
        }
    }
}
