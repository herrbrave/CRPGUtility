using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The game's special abilities behave as designed under the REAL rules engine: Reckless gives advantage both ways and expires on schedule,
    /// Second Wind and Rallying Verse heal what they say (once / capped / allies only), the bosses roll two attacks, and a crit doubles dice but not the flat bonus.</summary>
    public class AbilityRulesTests
    {
        private ContentFixture content;

        [SetUp]
        public void SetUp() => content = ContentFixture.Load();

        private static IEnumerable<AttackRollLogEntry> AttackRolls(Arena a) => a.Log.Entries.OfType<AttackRollLogEntry>();

        private static Arena Pair(ContentFixture content, string heroOrGoblinA, string b, int seed = 1)
        {
            var arena = new Arena(content, seed);
            arena.Add(heroOrGoblinA, Vector3.zero);
            arena.Add(b, new Vector3(1.5f, 0, 0));
            arena.Start();
            return arena;
        }

        private static void AdvanceTo(Arena a, Fighter who)
        {
            for (var i = 0; i < 20 && a.Encounter.Encounter.CurrentTurn.Id != who.Id; i++) a.Encounter.EndTurn();
            Assert.AreEqual(who.Id, a.Encounter.Encounter.CurrentTurn.Id, "could not reach the turn");
        }

        /// <summary>Ends the current turn at least once, then cycles until it is `who`'s turn again (their Action / Bonus Action / Movement refill at the start).</summary>
        private static void NextTurnOf(Arena a, Fighter who)
        {
            a.Encounter.EndTurn();
            AdvanceTo(a, who);
        }

        [Test]
        public void RecklessAttackGivesAdvantageToUlfAndToEveryoneAttackingHimThenExpiresAtTheStartOfHisNextTurn()
        {
            // the troll is a sturdy dummy (50 HP): a grunt dies to one axe swing and leaves the encounter
            using var arena = Pair(content, "ulf", "goblin-troll");
            var ulf = arena.Find("ulf"); var grunt = arena.Find("goblin-troll");
            var reckless = new DefinitionId(GameDesign.RecklessCondition);

            AdvanceTo(arena, ulf);
            arena.Cast(ulf, "ability-vr-great-axe", grunt);
            Assert.AreEqual(1, AttackRolls(arena).Last().Roll.Dice.Count, "without Reckless: a single d20");

            Assert.IsTrue(arena.Cast(ulf, "ability-vr-reckless-attack", ulf));
            Assert.IsTrue(ulf.State.HasCondition(reckless));

            arena.Encounter.EndTurn(); // the grunt's turn
            Assert.AreEqual(grunt.Id, arena.Encounter.Encounter.CurrentTurn.Id);
            Assert.IsTrue(ulf.State.HasCondition(reckless), "still reckless during other creatures' turns");
            var before = AttackRolls(arena).Count();
            arena.Cast(grunt, "ability-vr-troll-rend", ulf);
            Assert.AreEqual(2, AttackRolls(arena).Count() - before, "the troll swings twice");
            foreach (var roll in AttackRolls(arena).Skip(before)) Assert.AreEqual(2, roll.Roll.Dice.Count, "attackers roll with advantage against a reckless Ulf");

            arena.Encounter.EndTurn(); // Ulf's next turn
            Assert.AreEqual(ulf.Id, arena.Encounter.Encounter.CurrentTurn.Id);
            Assert.IsFalse(ulf.State.HasCondition(reckless), "Reckless must expire at the start of Ulf's next turn");
        }

        [Test]
        public void RecklessAttackGivesUlfHimselfAdvantageOnHisOwnAttacks()
        {
            using var arena = Pair(content, "ulf", "goblin-troll");
            var ulf = arena.Find("ulf"); var grunt = arena.Find("goblin-troll");
            AdvanceTo(arena, ulf);
            arena.Cast(ulf, "ability-vr-reckless-attack", ulf);
            arena.Cast(ulf, "ability-vr-great-axe", grunt);
            Assert.AreEqual(2, AttackRolls(arena).Last().Roll.Dice.Count, "Ulf attacks with advantage");
        }

        [Test]
        public void SecondWindHealsOneD10PlusThreeOnceAndNeverPastMax()
        {
            using var arena = Pair(content, "bjorn", "goblin-grunt-a");
            var bjorn = arena.Find("bjorn");
            var hp = bjorn.State.TryGetResource(new DefinitionId(GameDesign.Hp), out var r) ? r : null;
            hp.Spend(20);
            var hurt = arena.Hp(bjorn);
            Assert.IsTrue(arena.Cast(bjorn, GameDesign.SecondWindId, bjorn));
            var healed = arena.Hp(bjorn) - hurt;
            Assert.That(healed, Is.InRange(4, 13), "1d10+3");
            NextTurnOf(arena, bjorn);
            Assert.IsTrue(bjorn.State.TryGetResource(new DefinitionId(GameDesign.BonusAction), out var bonus) && bonus.Current == 1, "the bonus action is back, so only the spent use pool can refuse a second Second Wind");
            Assert.IsFalse(arena.Cast(bjorn, GameDesign.SecondWindId, bjorn), "one use per fight");

            hp.RestoreToFull();
            // a fresh Bjorn at 29/31 can never exceed max
            for (var seed = 0; seed < 30; seed++)
            {
                using var a2 = Pair(content, "bjorn", "goblin-grunt-a", seed);
                var b2 = a2.Find("bjorn");
                b2.State.TryGetResource(new DefinitionId(GameDesign.Hp), out var hp2);
                hp2.Spend(2);
                a2.Cast(b2, GameDesign.SecondWindId, b2);
                Assert.AreEqual(a2.MaxHp(b2), a2.Hp(b2));
            }
        }

        [Test]
        public void RallyingVerseHealsAnAllyUpToThreeTimesAndIsRefusedOnEnemies()
        {
            using var arena = new Arena(content, 3);
            arena.Add("astrid", Vector3.zero); arena.Add("bjorn", new Vector3(3, 0, 0)); arena.Add("goblin-grunt-a", new Vector3(4, 0, 0));
            arena.Start();
            var astrid = arena.Find("astrid"); var bjorn = arena.Find("bjorn"); var grunt = arena.Find("goblin-grunt-a");
            bjorn.State.TryGetResource(new DefinitionId(GameDesign.Hp), out var hp);
            hp.Spend(25);
            var low = arena.Hp(bjorn);

            Assert.IsTrue(arena.Cast(astrid, "ability-vr-rallying-verse", bjorn));
            Assert.That(arena.Hp(bjorn) - low, Is.InRange(4, 11), "1d8+3");
            Assert.IsFalse(arena.Cast(astrid, "ability-vr-rallying-verse", grunt), "a Verse on an enemy is a wrong-relation rejection");
            Assert.IsFalse(arena.Cast(astrid, "ability-vr-rallying-verse", bjorn), "a second Verse in the same turn needs a second bonus action");
            NextTurnOf(arena, astrid);
            Assert.IsTrue(arena.Cast(astrid, "ability-vr-rallying-verse", bjorn));
            NextTurnOf(arena, astrid);
            Assert.IsTrue(arena.Cast(astrid, "ability-vr-rallying-verse", bjorn));
            NextTurnOf(arena, astrid);
            Assert.IsFalse(arena.Cast(astrid, "ability-vr-rallying-verse", bjorn), "three verse uses per fight");
            Assert.LessOrEqual(arena.Hp(bjorn), arena.MaxHp(bjorn));
        }

        [Test]
        public void RallyingVerseOnADownedAllyStandsHimUpAtTheHealedAmountThroughTheRealDeathSaveService()
        {
            using var arena = new Arena(content, 3);
            arena.Add("astrid", Vector3.zero); arena.Add("bjorn", new Vector3(3, 0, 0)); arena.Add("goblin-grunt-a", new Vector3(40, 0, 0));
            arena.Start();
            var astrid = arena.Find("astrid"); var bjorn = arena.Find("bjorn");
            bjorn.State.TryGetResource(new DefinitionId(GameDesign.Hp), out var hp);
            hp.Spend(hp.Current);                                      // the real zero-HP path: the events DamageEffect publishes, so the real DeathSaveService reacts
            arena.Bus.Publish(new GameEventContext(GameEvent.AfterDamage, bjorn.Id, bjorn.Id));
            var dropped = new GameEventContext(GameEvent.CreatureDroppedToZeroHP, bjorn.Id, bjorn.Id);
            dropped.Data["Overkill"] = 0; dropped.Data["IsCritical"] = false;
            arena.Bus.Publish(dropped);
            Assert.IsNotNull(bjorn.State.DeathSaves, "dying");
            Assert.IsTrue(PartyVitals.IsDown(bjorn.State));
            var recovered = 0;
            arena.Bus.Subscribe(GameEvent.CreatureStabilized, _ => recovered++);

            Assert.IsTrue(arena.Cast(astrid, "ability-vr-rallying-verse", bjorn), "a verse works on a downed ally");

            Assert.IsNull(bjorn.State.DeathSaves);
            Assert.IsFalse(bjorn.State.HasCondition(PartyVitals.Unconscious));
            Assert.IsFalse(PartyVitals.IsDown(bjorn.State));
            Assert.That(arena.Hp(bjorn), Is.InRange(4, 11), "the verse's own 1d8+3, not 1 hit point");
            Assert.AreEqual(1, recovered, "the recovered cue the model listens to to stand up");
        }

        [Test]
        public void RallyingVerseIsRejectedBeyondItsRangeOfEight()
        {
            using var arena = new Arena(content, 4);
            arena.Add("astrid", Vector3.zero); arena.Add("bjorn", new Vector3(9, 0, 0)); arena.Add("goblin-grunt-a", new Vector3(30, 0, 0));
            arena.Start();
            Assert.IsFalse(arena.Cast(arena.Find("astrid"), "ability-vr-rallying-verse", arena.Find("bjorn")));
        }

        [Test]
        public void TheTrollAndTheKingRollTwoAttacksPerSwingAndEveryoneElseRollsOne()
        {
            foreach (var (attacker, ability, expected) in new[] { ("goblin-troll", "ability-vr-troll-rend", 2), ("goblin-king", "ability-vr-king-greatsword", 2), ("goblin-warrior-a", "ability-vr-goblin-sword", 1) })
            {
                using var arena = Pair(content, attacker, "bjorn");
                arena.Cast(arena.Find(attacker), ability, arena.Find("bjorn"));
                Assert.AreEqual(expected, AttackRolls(arena).Count(), $"{attacker}: attack rolls per Action");
            }
        }

        [Test]
        public void ACriticalHitDoublesTheDiceButNotTheFlatBonus()
        {
            // Troll Rend is 2d6+3: 5..15 normally, 7..27 on a crit (4d6+3)
            int crits = 0, hits = 0, bigCrit = 0;
            for (var seed = 0; seed < 600; seed++)
            {
                using var arena = Pair(content, "goblin-troll", "bjorn", seed);
                arena.Cast(arena.Find("goblin-troll"), "ability-vr-troll-rend", arena.Find("bjorn"));
                var entries = arena.Log.Entries;
                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i] is not AttackRollLogEntry roll || !roll.IsHit) continue;
                    var damage = entries.Skip(i + 1).OfType<DamageLogEntry>().First().Amount;
                    hits++;
                    if (roll.IsCriticalHit) { crits++; Assert.That(damage, Is.InRange(7, 27)); if (damage > 15) bigCrit++; }
                    else Assert.That(damage, Is.InRange(5, 15), "a normal hit is 2d6+3");
                }
            }

            Assert.Greater(crits, 20, "not enough crits sampled to judge");
            Assert.Greater(bigCrit, 0, "no crit ever exceeded the normal 2d6+3 maximum of 15: crits are not doubling dice");
        }
    }
}
