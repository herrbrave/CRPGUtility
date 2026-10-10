using System.Collections;
using System.Linq;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>Staging and a whole live fight on the real scene, played by the scripted party through the real combat session.</summary>
    public class GameFightTests : GameTestBase
    {
        private IEnumerator WalkIntoTrigger(int encounter) => EnterTrigger(encounter);

        [UnityTest]
        public IEnumerator EnteringTheTriggerStagesThePartyOnItsAnchorsWithoutWarpsThenStartsTheFightFromTheSimulatedFormation()
        {
            Time.timeScale = 3f;
            yield return WalkIntoTrigger(0);
            yield return WaitUntil(() => Game.State == GameState.Staging || Game.State == GameState.Fighting, 5f, "staging to begin");
            yield return WaitUntil(() => Game.State == GameState.Fighting, 30f, "the fight to start");

            Assert.AreEqual(0, Game.StageWarps, "the stage walk must succeed on its own: a warp would hide a broken walk");
            Assert.Less(Game.StageSeconds, 9f, "the walk must finish before the timeout");
            var stage = Game.Anchors.encounters[0].partyStage;
            for (var i = 0; i < 4; i++) Assert.Less(Vector3.Distance(Position(Game.Heroes[i]), stage[i].position), 0.6f, $"hero {i} is not on its stage anchor");
            Assert.IsTrue(Game.Combat.IsActive);
            Assert.AreEqual(7, Game.Combat.Controller.EncounterService.Encounter.Combatants.Count, "4 heroes + 3 grunts");
            Assert.IsFalse(Game.Anchors.encounters[0].trigger.GetComponent<VikingEncounterTrigger>().Armed, "the trigger disarms once it fires");
        }

        [UnityTest]
        public IEnumerator AScriptedPartyWinsTheFirstFightAndIsHealedAndRevivedAndFreeToMoveAgain()
        {
            Time.timeScale = 4f;
            yield return WalkIntoTrigger(0);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the fight to start");

            var party = new ScriptedParty(Game);
            var start = Time.realtimeSinceStartup;
            while (Game.State == GameState.Fighting)
            {
                party.Tick();
                if (Time.realtimeSinceStartup - start > 240f) Assert.Fail($"the fight did not finish in 240 s (abilities {party.AbilitiesStarted}, moves {party.Moves}, turns ended {party.TurnsEnded}, watchdog cancels {Game.Combat.WatchdogCancels})");
                yield return null;
            }

            Assert.AreEqual(GameState.Exploring, Game.State, "the party should have won E1");
            Assert.AreEqual(1, Checkpoint.Index);
            foreach (var hero in Game.Heroes)
            {
                Game.Session.Characters.TryGetCharacter(hero, out var state);
                Assert.IsFalse(PartyVitals.IsDown(state)); Assert.IsNull(state.DeathSaves); Assert.IsFalse(state.HasCondition(PartyVitals.Unconscious));
                state.TryGetResource(PartyVitals.Hp, out var hp);
                Assert.AreEqual(hp.Max, hp.Current, "everyone is healed to full after a win");
            }

            Assert.IsFalse(Game.Combat.IsActive);
            foreach (var (_, id) in Game.GoblinsOf(0)) Assert.IsNull(VikingGame.FindAgent(id), "a dead goblin's agent is gone");

            // the road continues: a click ahead moves the party again
            var before = PartyCentre();
            Click(WorldLayout.PointAt(WorldLayout.Arenas[1].WestS - 40f));
            yield return WaitUntil(() => Vector3.Distance(PartyCentre(), before) > 6f, 20f, "the party to move after the fight");
        }
    }
}
