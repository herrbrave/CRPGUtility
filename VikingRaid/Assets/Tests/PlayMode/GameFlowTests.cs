using System.Collections;
using System.Linq;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>The whole-game transitions on the real scene: a wipe reloads at the checkpoint, the last boss ends the game, fights follow one another without leaking, and a trigger reached
    /// early waits for its turn and then fires even though the party is already standing in it.</summary>
    public class GameFlowTests : GameTestBase
    {
        [UnityTest]
        public IEnumerator AWipeShowsTheBannerThenReloadsTheSceneWithThePartyOnTheCheckpointAtFullHealthAndTheTriggerRearmed()
        {
            Time.timeScale = 4f;
            foreach (var hero in Game.Heroes) { Game.Session.Characters.TryGetCharacter(hero, out var s); s.TryGetResource(PartyVitals.Hp, out var hp); hp.Spend(hp.Current - 1); }
            yield return EnterTrigger(0);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the fight to start");

            var old = Game;
            var start = Time.realtimeSinceStartup;
            while (Game != null && Game.State == GameState.Fighting)
            {
                // the heroes do nothing: their turns are ended, so the goblins finish them
                var combat = Game.Combat;
                if (combat.IsActive && combat.Controller.EncounterService.Encounter?.CurrentTurn is { } turn && turn.Faction == VikingCombatSession.HeroFaction && !combat.Controller.Scheduler.IsBusy)
                    combat.Controller.EncounterService.EndTurn();
                if (Time.realtimeSinceStartup - start > 180f) Assert.Fail("the party was never wiped");
                yield return null;
            }

            Assert.AreEqual(GameState.Defeated, old.State);
            StringAssert.Contains("fallen", old.Banner);

            yield return WaitUntil(() => VikingGame.Instance != null && VikingGame.Instance != old && VikingGame.Instance.Session != null && VikingGame.Instance.State == GameState.Exploring, 30f, "the scene to reload");
            Game = VikingGame.Instance; Cam = Camera.main;
            yield return null; yield return null;

            Assert.AreEqual(0, Checkpoint.Index); Assert.IsTrue(Checkpoint.SpawnAtCheckpoint);
            var slots = Game.Anchors.encounters[0].checkpointSlots;
            for (var i = 0; i < 4; i++)
            {
                Assert.Less(Vector3.Distance(Position(Game.Heroes[i]), slots[i].position), 0.5f, $"hero {i} is not on the E1 checkpoint");
                Game.Session.Characters.TryGetCharacter(Game.Heroes[i], out var s); s.TryGetResource(PartyVitals.Hp, out var hp);
                Assert.AreEqual(hp.Max, hp.Current, "a reload starts everyone at full health");
            }

            Assert.AreEqual(3, Game.GoblinsOf(0).Count, "E1's goblins are back");
            Assert.IsTrue(Game.Anchors.encounters[0].trigger.GetComponent<VikingEncounterTrigger>().Armed);
        }

        [UnityTest]
        public IEnumerator KillingTheBossShowsTheCampDestroyedOverlayAndPlayAgainStartsOver()
        {
            yield return ReloadAtCheckpoint(3);           // as if E1-E3 were won and the party was wiped in E4
            Assert.AreEqual(0, Game.GoblinsOf(0).Count, "the earlier encounters' goblins are not spawned");
            Assert.AreEqual(3, Game.GoblinsOf(3).Count);

            Time.timeScale = 4f;
            yield return EnterTrigger(3);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the boss fight to start");
            foreach (var (_, id) in Game.GoblinsOf(3)) { Game.Session.Characters.TryGetCharacter(id, out var s); s.TryGetResource(PartyVitals.Hp, out var hp); hp.Spend(hp.Current - 1); }   // shortcut the length, not the flow

            yield return PlayFight(new ScriptedParty(Game), 240f);
            Assert.AreEqual(GameState.Won, Game.State);
            Assert.AreEqual(4, Checkpoint.Index); Assert.IsTrue(Checkpoint.CampDestroyed);
            Assert.IsTrue(Game.OverlayShown, "the Camp destroyed overlay");
            Assert.IsNotNull(Game.OverlayElement.Q<Label>("overlay-title"));

            var old = Game;
            UiClick.Submit(Game.OverlayElement.Q<Button>("overlay-button"));
            yield return WaitUntil(() => VikingGame.Instance != null && VikingGame.Instance != old && VikingGame.Instance.Session != null && VikingGame.Instance.State == GameState.Exploring, 30f, "Play Again to reload");
            Game = VikingGame.Instance;
            Assert.AreEqual(0, Checkpoint.Index); Assert.IsFalse(Checkpoint.SpawnAtCheckpoint);
            Assert.AreEqual(14, Enumerable.Range(0, 4).Sum(e => Game.GoblinsOf(e).Count));
        }

        [UnityTest]
        public IEnumerator TwoFightsInARowWorkAndLeaveNoHudOrStateBehind()
        {
            Time.timeScale = 4f;
            var hudChildren = Game.HudChildCount;

            yield return EnterTrigger(0);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "fight 1");
            yield return PlayFight(new ScriptedParty(Game), 240f);
            Assert.AreEqual(GameState.Exploring, Game.State, "fight 1 should be won");
            Assert.AreEqual(1, Checkpoint.Index);

            yield return EnterTrigger(1);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "fight 2");
            yield return PlayFight(new ScriptedParty(Game), 300f);
            Assert.AreEqual(GameState.Exploring, Game.State, "fight 2 should be won");
            Assert.AreEqual(2, Checkpoint.Index);
            Assert.AreEqual(hudChildren, Game.HudChildCount, "the HUD must not accumulate elements across fights");
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ATriggerReachedEarlyWaitsForItsTurnThenFiresWithoutTheStepOutAndInThatE4NeedsAfterE3()
        {
            yield return ReloadAtCheckpoint(2);           // E3 is next; E4's trigger stands in the gate
            yield return EnterTrigger(3);                 // the party is already inside E4's trigger
            for (var i = 0; i < 90; i++) yield return null;
            Assert.AreEqual(GameState.Exploring, Game.State, "E4 must not fire before E3 is won");
            Assert.IsFalse(Game.Combat.IsActive);

            Checkpoint.EncounterWon(2);                   // E3 won
            yield return WaitUntil(() => Game.State is GameState.Staging or GameState.Fighting, 5f, "E4 to fire for a party already standing in its trigger");
        }
    }
}
