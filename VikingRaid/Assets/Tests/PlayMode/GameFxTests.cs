using System.Collections;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>The combat effects in a live fight: an arrow is released when the bow animation releases and a hit reaction waits for it to land; a melee hit bursts at the target; a heal shows.</summary>
    public class GameFxTests : GameTestBase
    {
        private IEnumerator StartFight()
        {
            Time.timeScale = 3f;
            yield return EnterTrigger(0);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the fight to start");
        }

        /// <summary>Waits for the given hero's turn (the other heroes' turns are ended: nobody is playing them).</summary>
        private IEnumerator WaitForHeroTurn(int heroIndex)
        {
            var hero = Game.Heroes[heroIndex];
            yield return WaitUntil(() =>
            {
                if (!Game.Combat.IsActive || Game.Combat.CurrentTurnId is not { } id || Game.Combat.Controller.Scheduler.IsBusy) return false;
                if (id == hero) return true;
                if (Game.Heroes.Contains(id)) Game.Combat.Controller.EncounterService.EndTurn();
                return false;
            }, 60f, $"hero {heroIndex}'s turn");
        }

        private AbilityDefinition Ability(string id) { Game.Session.RulesDatabase.TryResolve<AbilityDefinition>(new DefinitionId(id), out var a); return a; }

        [UnityTest]
        public IEnumerator ABowAttackLaunchesAnArrowAtTheReleaseAndTheTargetsReactionWaitsForItToLand()
        {
            Assert.IsNotNull(Game.Fx, "the scene has no CombatFx");
            yield return StartFight();
            Assert.IsNotNull(ModelActionCues.LandingDelayRule, "the hit-reaction timing rule must be installed");
            yield return WaitForHeroTurn(2);       // Sigrid
            var sigrid = Game.Heroes[2];
            var goblin = Game.GoblinsOf(0)[1].Id;   // the middle grunt
            Assert.IsTrue(VikingGame.FindAgent(sigrid).Motor.Warp(Position(goblin) - WorldLayout.Arenas[0].Forward * 9f));
            yield return null;

            var before = Game.Fx.ProjectilesSpawned;
            Assert.Greater(ModelActionCues.LandingDelayRule(sigrid, goblin), CombatFx.BowRelease, "a hit from 9 m lands after the release plus the arrow's flight, not after the melee delay");
            Assert.IsNotNull(Game.Combat.Controller.Scheduler.StartAbility(sigrid, Ability("ability-vr-longbow"), new[] { goblin }), "the longbow shot did not start");

            yield return WaitUntil(() => Game.Fx.ProjectilesSpawned > before, 6f, "the arrow to be released");
            Assert.AreEqual(before + 1, Game.Fx.ProjectilesSpawned);
            var landed = Game.Fx.ImpactsSpawned + Game.Fx.MissesSpawned;
            yield return WaitUntil(() => Game.Fx.ImpactsSpawned + Game.Fx.MissesSpawned > landed, 6f, "the arrow to land (burst on a hit, dust on a miss)");
        }

        [UnityTest]
        public IEnumerator AMeleeHitBurstsAtTheTargetAndAHealShowsAHealEffect()
        {
            yield return StartFight();
            var bjorn = Game.Heroes[0];
            var goblin = Game.GoblinsOf(0)[0].Id;
            Game.Session.Characters.TryGetCharacter(bjorn, out var bjornState);
            bjornState.TryGetResource(new DefinitionId(GameDesign.Action), out var action);

            // melee: the swing lands 0.35 s after it starts. A miss shows no burst, so swing until one hits (an AC 13 grunt: 75% per swing). The Action ends the turn, so each
            // attempt waits for Bjorn's next turn (and stands him beside the goblin again, who has been moving).
            var before = Game.Fx.ImpactsSpawned;
            for (var attempt = 0; attempt < 10 && Game.Fx.ImpactsSpawned == before; attempt++)
            {
                yield return WaitForHeroTurn(0);       // Bjorn
                Assert.IsTrue(VikingGame.FindAgent(bjorn).Motor.Warp(Position(goblin) - WorldLayout.Arenas[0].Forward * 1.4f));
                yield return null;
                action.RestoreToFull();
                Assert.IsNotNull(Game.Combat.Controller.Scheduler.StartAbility(bjorn, Ability("ability-vr-sword-strike"), new[] { goblin }));
                yield return WaitUntil(() => !Game.Combat.Controller.Scheduler.IsBusy, 10f, "the swing to finish");
                for (var i = 0; i < 30 && Game.Fx.ImpactsSpawned == before; i++) yield return null;     // the burst is due 0.35 s after the swing starts
            }

            Assert.Greater(Game.Fx.ImpactsSpawned, before, "no impact burst after ten swings");

            // heal: Astrid's verse on a wounded Bjorn
            bjornState.TryGetResource(PartyVitals.Hp, out var hp); hp.Spend(12);
            yield return WaitForHeroTurn(3);       // Astrid
            var astrid = Game.Heroes[3];
            Assert.IsTrue(VikingGame.FindAgent(astrid).Motor.Warp(Position(bjorn) + Vector3.back * 2f));
            yield return null;
            var healsBefore = Game.Fx.HealsSpawned;
            Assert.IsNotNull(Game.Combat.Controller.Scheduler.StartAbility(astrid, Ability("ability-vr-rallying-verse"), new[] { bjorn }), "the verse did not start");
            yield return WaitUntil(() => Game.Fx.HealsSpawned > healsBefore, 5f, "the heal effect");
        }
    }
}
