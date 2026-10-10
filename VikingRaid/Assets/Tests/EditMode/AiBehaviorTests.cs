using System.Linq;
using CRPG.AI;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The goblin brains, driven by the real UtilityAIDecisionProvider over real positions, do the right thing in each situation. The ones that fail
    /// silently in play: a mis-keyed HP consideration vetoes every attack (the goblin just walks), a too-harsh HP curve makes Move beat Attack, and a goblin
    /// standing next to a downed hero must go on to a living one instead of idling.</summary>
    public class AiBehaviorTests
    {
        private ContentFixture content;

        [SetUp]
        public void SetUp() => content = ContentFixture.Load();

        private static void MakeCurrent(Arena arena, Fighter who)
        {
            for (var i = 0; i < 20 && arena.Encounter.Encounter.CurrentTurn.Id != who.Id; i++) arena.Encounter.EndTurn();
            Assert.AreEqual(who.Id, arena.Encounter.Encounter.CurrentTurn.Id);
        }

        private static TurnDecision Decide(Arena arena, Fighter goblin)
        {
            MakeCurrent(arena, goblin);
            var e = arena.Encounter.Encounter;
            return goblin.Brain.GetDecision(new TurnContext(goblin.Id, e.GetEnemiesOf(goblin.Id), e.GetAlliesOf(goblin.Id)));
        }

        private static void Down(Arena arena, Fighter hero)
        {
            hero.State.TryGetResource(new DefinitionId(GameDesign.Hp), out var hp);
            hp.Spend(hp.Max);
        }

        private static void SetHpFraction(Fighter f, float fraction)
        {
            f.State.TryGetResource(new DefinitionId(GameDesign.Hp), out var hp);
            hp.Spend(hp.Current - Mathf.Max(1, Mathf.RoundToInt(hp.Max * fraction)));
        }

        private Arena Scene(string goblin, float distance, string hero = "bjorn", int seed = 11)
        {
            var arena = new Arena(content, seed);
            arena.Add(hero, Vector3.zero);
            arena.Add(goblin, new Vector3(distance, 0, 0));
            arena.Start();
            return arena;
        }

        [TestCase("goblin-grunt-a")]
        [TestCase("goblin-warrior-a")]
        [TestCase("goblin-troll")]
        [TestCase("goblin-king")]
        public void AMeleeGoblinFarAwayClosesInOnTheNearestHero(string unit)
        {
            using var arena = Scene(unit, 14f);
            var decision = Decide(arena, arena.Find(unit));
            Assert.IsNotNull(decision);
            Assert.AreEqual(BehaviorKind.Move, decision.Kind);
            Assert.AreEqual(arena.Find("bjorn").Id, decision.Targets[0]);
        }

        [TestCase("goblin-grunt-a")]
        [TestCase("goblin-warrior-a")]
        [TestCase("goblin-troll")]
        [TestCase("goblin-king")]
        public void AMeleeGoblinInReachAttacksAtEveryHitPointFractionOfItsTarget(string unit)
        {
            foreach (var fraction in new[] { 1f, 0.75f, 0.5f, 0.25f, 0.05f })
            {
                using var arena = Scene(unit, 1.5f);
                SetHpFraction(arena.Find("bjorn"), fraction);
                var decision = Decide(arena, arena.Find(unit));
                Assert.IsNotNull(decision, $"{unit} vs a hero at {fraction:P0}: no decision at all");
                Assert.AreEqual(BehaviorKind.UseAbility, decision.Kind, $"{unit} must attack a hero at {fraction:P0} HP, not walk");
            }
        }

        [Test]
        public void AGoblinNextToADownedHeroGoesAfterALivingOneInsteadOfIdling()
        {
            using var arena = new Arena(content, 12);
            var downed = arena.Add("bjorn", Vector3.zero);
            var living = arena.Add("ulf", new Vector3(10, 0, 0));
            var grunt = arena.Add("goblin-grunt-a", new Vector3(1.5f, 0, 0));
            arena.Start();
            Down(arena, downed);

            var decision = Decide(arena, grunt);
            Assert.IsNotNull(decision, "idling beside a downed hero");
            Assert.AreEqual(BehaviorKind.Move, decision.Kind);
            Assert.AreEqual(living.Id, decision.Targets[0], "must walk to the hero who is still standing");
        }

        [Test]
        public void AGoblinWithNoMovementLeftBesideADownedHeroDoesNothingRatherThanHittingThem()
        {
            // regression found by the balance simulation: the raw utility AI returns its best decision even when that scores 0 (vetoed). A boss that has walked
            // toward a standing hero and spent its movement would otherwise swing at the downed hero next to it.
            using var arena = new Arena(content, 13);
            var downed = arena.Add("bjorn", Vector3.zero);
            arena.Add("ulf", new Vector3(8, 0, 0));
            var king = arena.Add("goblin-king", new Vector3(1.5f, 0, 0));
            arena.Start();
            Down(arena, downed);
            MakeCurrent(arena, king);
            king.State.TryGetResource(new DefinitionId(GameDesign.Movement), out var movement);
            movement.Spend(movement.Current);

            Assert.IsNull(Decide(arena, king), "the only legal attack is on a downed hero: the goblin should end its turn");
        }

        [Test]
        public void AGoblinNeverAttacksADownedHeroWhileAnotherStands()
        {
            for (var seed = 0; seed < 20; seed++)
            {
                using var arena = new Arena(content, seed);
                var downed = arena.Add("bjorn", Vector3.zero);
                arena.Add("ulf", new Vector3(2, 0, 0));
                var grunt = arena.Add("goblin-grunt-a", new Vector3(1.5f, 0, 0));
                arena.Start();
                Down(arena, downed);
                var decision = Decide(arena, grunt);
                if (decision != null && decision.Kind == BehaviorKind.UseAbility)
                    Assert.AreNotEqual(downed.Id, decision.Targets[0], "attacked the downed hero (seed " + seed + ")");
            }
        }

        [Test]
        public void AnArcherShootsFromRangeAndStabsWhenAHeroIsAdjacent()
        {
            using var far = Scene("goblin-archer-m", 9f);
            var shot = Decide(far, far.Find("goblin-archer-m"));
            Assert.AreEqual(BehaviorKind.UseAbility, shot.Kind);
            Assert.AreEqual("ability-vr-shortbow", shot.Ability.Id.Value);

            using var near = Scene("goblin-archer-m", 1.5f);
            var stab = Decide(near, near.Find("goblin-archer-m"));
            Assert.AreEqual(BehaviorKind.UseAbility, stab.Kind);
            Assert.AreEqual("ability-vr-goblin-dagger", stab.Ability.Id.Value, "a goblin archer in melee reach draws its dagger");
        }

        [Test]
        public void AnArcherOutOfRangeWalksIntoRange()
        {
            using var arena = Scene("goblin-archer-m", 20f);
            var decision = Decide(arena, arena.Find("goblin-archer-m"));
            Assert.AreEqual(BehaviorKind.Move, decision.Kind);
            Assert.AreEqual(10f, decision.StopDistance, 1e-3f, "stops at 10 m, inside its 12 m range");
        }

        [Test]
        public void TheShamanCastsFireBoltWhenInRangeAndWalksUpWhenNot()
        {
            using var near = Scene("goblin-shaman", 8f);
            var bolt = Decide(near, near.Find("goblin-shaman"));
            Assert.AreEqual(BehaviorKind.UseAbility, bolt.Kind);
            Assert.AreEqual("ability-vr-fire-bolt", bolt.Ability.Id.Value);

            using var far = Scene("goblin-shaman", 25f);
            Assert.AreEqual(BehaviorKind.Move, Decide(far, far.Find("goblin-shaman")).Kind);
        }

        [TestCase("goblin-grunt-a", 1.5f)]
        [TestCase("goblin-warrior-a", 1.5f)]
        [TestCase("goblin-archer-m", 9f)]
        [TestCase("goblin-archer-m", 1.5f)]
        [TestCase("goblin-shaman", 8f)]
        [TestCase("goblin-troll", 1.5f)]
        [TestCase("goblin-king", 1.5f)]
        public void AGoblinTurnSpendsItsActionOnceAndNeverRepeatsAnAbility(string unit, float distance)
        {
            using var arena = Scene(unit, distance);
            var goblin = arena.Find(unit);
            MakeCurrent(arena, goblin);
            var before = arena.Log.Entries.OfType<AttackRollLogEntry>().Count();
            arena.GoblinTurn(goblin);
            var rolls = arena.Log.Entries.OfType<AttackRollLogEntry>().Count() - before;
            Assert.AreEqual(goblin.Spec.Multiattack ? 2 : 1, rolls, $"{unit}: attack rolls in one turn (one Action = one attack; bosses roll their two)");
        }
    }
}
