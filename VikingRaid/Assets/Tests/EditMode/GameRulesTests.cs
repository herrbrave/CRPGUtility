using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The pure rules of the game loop, each one the answer to a finding from the Task 4 review: a single "down" predicate (a hero STABILISED at 0 HP must not get a turn), the
    /// win/lose, when a player's turn ends by itself, the move watchdog and the checkpoint index.</summary>
    public class GameRulesTests
    {
        private ContentFixture content;
        private CharacterRegistry chars;
        private GameEventBus bus;

        [SetUp]
        public void SetUp()
        {
            content = ContentFixture.Load();
            chars = new CharacterRegistry();
            bus = new GameEventBus();
            Checkpoint.Reset();
        }

        private CharacterState Spawn(string unit) => content.Spawn(chars, unit);
        private static ResourceState Hp(CharacterState c) { c.TryGetResource(PartyVitals.Hp, out var hp); return hp; }
        private static ResourceState Res(CharacterState c, string id) { c.TryGetResource(new DefinitionId(id), out var r); return r; }
        private static void MakeStable(CharacterState c)
        {
            // what DeathSaveService leaves after three successes: DeathSaves cleared, HP still 0, Unconscious still on
            Hp(c).Spend(Hp(c).Current);
            c.DeathSaves = null;
            c.ApplyCondition(new ConditionInstance(PartyVitals.Unconscious, c.Id, c.Id));
        }

        // ------------------------------------------------------------------ down / revive
        [Test]
        public void DownMeansZeroHitPointsNotDeathSaves()
        {
            var hero = Spawn("bjorn");
            Assert.IsFalse(PartyVitals.IsDown(hero));
            Hp(hero).Spend(Hp(hero).Current);
            Assert.IsTrue(PartyVitals.IsDown(hero), "0 HP is down");
            hero.DeathSaves = new DeathSaveState();
            Assert.IsTrue(PartyVitals.IsDown(hero));
            hero.DeathSaves = null;
            Assert.IsTrue(PartyVitals.IsDown(hero), "a hero STABILISED at 0 HP (DeathSaves cleared) is still down: it must not get a normal turn");
        }

        // reviving by a heal is the toolkit's DeathSaveService (reviveOnHeal, DeathSaveServiceTests) and is exercised through the game's real Rallying Verse in AbilityRulesTests

        [Test]
        public void FullHealAndReviveRestoresEveryoneIncludingTheDownedAndTheStable()
        {
            var downed = Spawn("astrid"); Hp(downed).Spend(Hp(downed).Current); downed.DeathSaves = new DeathSaveState { Failures = 1 };
            var stable = Spawn("bjorn"); MakeStable(stable);
            foreach (var h in new[] { downed, stable }) PartyVitals.FullHealAndRevive(h, bus);
            foreach (var h in new[] { downed, stable })
            {
                Assert.AreEqual(Hp(h).Max, Hp(h).Current); Assert.IsNull(h.DeathSaves); Assert.IsFalse(h.HasCondition(PartyVitals.Unconscious)); Assert.IsFalse(PartyVitals.IsDown(h));
            }
        }

        // ------------------------------------------------------------------ outcome
        [Test]
        public void TheFightIsWonWhenNoGoblinHasHitPointsLeftAndLostWhenEveryHeroIsDown()
        {
            var party = new[] { "bjorn", "ulf", "sigrid", "astrid" }.Select(Spawn).ToList();
            var goblins = new[] { "goblin-grunt-a", "goblin-grunt-b" }.Select(Spawn).ToList();
            Assert.AreEqual(FightOutcome.Ongoing, PartyVitals.Evaluate(party, goblins));

            Hp(goblins[0]).Spend(Hp(goblins[0]).Current);
            Assert.AreEqual(FightOutcome.Ongoing, PartyVitals.Evaluate(party, goblins));
            Hp(goblins[1]).Spend(Hp(goblins[1]).Current);
            Assert.AreEqual(FightOutcome.Won, PartyVitals.Evaluate(party, goblins));

            Hp(goblins[1]).Restore(3);
            foreach (var h in party.Take(3)) Hp(h).Spend(Hp(h).Current);
            Assert.AreEqual(FightOutcome.Ongoing, PartyVitals.Evaluate(party, goblins), "one hero still stands");
            MakeStable(party[3]);
            Assert.AreEqual(FightOutcome.Lost, PartyVitals.Evaluate(party, goblins), "stable at 0 HP is out of the fight, not a survivor");
        }

        // ------------------------------------------------------------------ when a player turn ends by itself
        private static readonly System.Func<RuntimeEntityId, Vector3> Nowhere = _ => Vector3.zero;

        [Test]
        public void TheTurnDoesNotEndWhileTheActionIsUnspent()
        {
            var hero = Spawn("bjorn");
            Assert.IsFalse(TurnEndRule.ShouldAutoEnd(hero, new[] { hero }, Nowhere));
        }

        [Test]
        public void AfterTheActionTheTurnEndsUnlessABonusAbilityWouldActuallyDoSomething()
        {
            var bjorn = Spawn("bjorn"); Res(bjorn, GameDesign.Action).Spend(1);
            Assert.IsTrue(TurnEndRule.ShouldAutoEnd(bjorn, new[] { bjorn }, Nowhere), "full HP: Second Wind is useless, end the turn");

            Hp(bjorn).Spend(10);
            Assert.IsFalse(TurnEndRule.ShouldAutoEnd(bjorn, new[] { bjorn }, Nowhere), "wounded with a Second Wind left: keep the turn open for it");
            Res(bjorn, GameDesign.BonusAction).Spend(1);
            Assert.IsTrue(TurnEndRule.ShouldAutoEnd(bjorn, new[] { bjorn }, Nowhere), "bonus action spent too");

            var bjorn2 = Spawn("bjorn"); Res(bjorn2, GameDesign.Action).Spend(1); Hp(bjorn2).Spend(10); Res(bjorn2, GameDesign.SecondWindUses).Spend(1);
            Assert.IsTrue(TurnEndRule.ShouldAutoEnd(bjorn2, new[] { bjorn2 }, Nowhere), "no Second Wind use left");
        }

        [Test]
        public void RecklessAndTheOtherSelfBuffsNeverKeepATurnOpenAfterTheAction()
        {
            var ulf = Spawn("ulf"); Res(ulf, GameDesign.Action).Spend(1); Hp(ulf).Spend(10);
            Assert.IsTrue(TurnEndRule.ShouldAutoEnd(ulf, new[] { ulf }, Nowhere), "Ulf has only Reckless Attack as a bonus ability: useless once the Action is spent");
        }

        [Test]
        public void AstridKeepsHerTurnOpenOnlyForAWoundedAllyWithinEightMetres()
        {
            var astrid = Spawn("astrid"); var bjorn = Spawn("bjorn");
            Res(astrid, GameDesign.Action).Spend(1);
            var near = new Dictionary<RuntimeEntityId, Vector3> { [astrid.Id] = Vector3.zero, [bjorn.Id] = new Vector3(5, 0, 0) };
            Assert.IsTrue(TurnEndRule.ShouldAutoEnd(astrid, new[] { astrid, bjorn }, id => near[id]), "everyone at full HP");

            Hp(bjorn).Spend(12);
            Assert.IsFalse(TurnEndRule.ShouldAutoEnd(astrid, new[] { astrid, bjorn }, id => near[id]), "a wounded ally 5 m away: the verse can help");
            near[bjorn.Id] = new Vector3(9, 0, 0);
            Assert.IsTrue(TurnEndRule.ShouldAutoEnd(astrid, new[] { astrid, bjorn }, id => near[id]), "9 m is out of range");

            near[bjorn.Id] = new Vector3(3, 0, 0); Hp(bjorn).Spend(Hp(bjorn).Current);
            Assert.IsFalse(TurnEndRule.ShouldAutoEnd(astrid, new[] { astrid, bjorn }, id => near[id]), "a DOWNED ally in range is the best use of a verse");
        }

        [Test]
        public void ADownedHeroAlwaysEndsItsTurn()
        {
            var hero = Spawn("ulf"); MakeStable(hero);
            Assert.IsTrue(TurnEndRule.ShouldAutoEnd(hero, new[] { hero }, Nowhere));
        }

        // ------------------------------------------------------------------ move watchdog
        [Test]
        public void TheWatchdogLeavesAMoveThatIsProgressingAlone()
        {
            var dog = new MoveWatchdog(budgetMetres: 8f, speed: 4f);   // 2 s of walking + 1 s slack
            for (var i = 0; i < 100; i++) Assert.IsFalse(dog.ShouldCancel(0.02f, 3.9f), $"frame {i}");
        }

        [Test]
        public void TheWatchdogCancelsAMoveThatStopsMovingButNeverArrives()
        {
            var dog = new MoveWatchdog(8f, 4f);
            for (var i = 0; i < 25; i++) Assert.IsFalse(dog.ShouldCancel(0.02f, 3.9f));   // 0.5 s of real progress
            var cancelled = false;
            for (var i = 0; i < 50 && !cancelled; i++) cancelled = dog.ShouldCancel(0.02f, 0.01f);   // then jitters in place
            Assert.IsTrue(cancelled, "a stalled move must be cancelled within about half a second");
        }

        [Test]
        public void TheWatchdogCancelsAMoverThatWobblesInACrowdAtSpeedButNeverGetsCloser()
        {
            var dog = new MoveWatchdog(8f, 4f);
            var cancelled = false;
            for (var frames = 0; frames < 200 && !cancelled; frames++) cancelled = dog.ShouldCancel(0.02f, 2.5f, 4.0f + (frames % 2) * 0.1f);   // moving, but the distance to go never shrinks
            Assert.IsTrue(cancelled, "no progress toward the destination must be caught even when the speed is high");
            Assert.That(dog.Elapsed, Is.InRange(1.0f, 1.2f), "one second without progress, long before the 3 s budget limit");
        }

        [Test]
        public void TheWatchdogLeavesAMoveThatKeepsGettingCloserAlone()
        {
            var dog = new MoveWatchdog(8f, 4f);
            var remaining = 8f;
            for (var i = 0; i < 100; i++) { remaining -= 0.075f; Assert.IsFalse(dog.ShouldCancel(0.02f, 3.8f, remaining), $"frame {i}"); }
        }

        [Test]
        public void TheWatchdogCancelsAMoveThatOutlivesWhatItsBudgetCouldTake()
        {
            var dog = new MoveWatchdog(4f, 4f);     // 1 s + 1 s slack
            var cancelled = false;
            for (var i = 0; i < 200 && !cancelled; i++) cancelled = dog.ShouldCancel(0.02f, 1.0f);   // creeping along, never stalled, never done
            Assert.IsTrue(cancelled);
            Assert.That(dog.Elapsed, Is.InRange(1.9f, 2.2f));
        }

        // ------------------------------------------------------------------ checkpoint
        [Test]
        public void TheCheckpointIndexIsTheNumberOfEncountersWonAndNeverGoesBackwards()
        {
            Assert.AreEqual(0, Checkpoint.Index);
            Checkpoint.EncounterWon(0); Assert.AreEqual(1, Checkpoint.Index);
            Checkpoint.EncounterWon(1); Assert.AreEqual(2, Checkpoint.Index);
            Checkpoint.EncounterWon(0); Assert.AreEqual(2, Checkpoint.Index, "re-winning an earlier fight must not rewind");
            Assert.IsFalse(Checkpoint.CampDestroyed);
            Checkpoint.EncounterWon(3); Assert.AreEqual(4, Checkpoint.Index); Assert.IsTrue(Checkpoint.CampDestroyed);
            Checkpoint.EncounterWon(9); Assert.AreEqual(4, Checkpoint.Index, "clamped");
            Checkpoint.Reset(); Assert.AreEqual(0, Checkpoint.Index);
        }
    }
}
