using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// An attack rider is extra damage a RULE adds to a weapon hit (Sneak Attack is the first): after the weapon's own damage, <see cref="IAttackRiderRule"/> is asked about each target that was hit; if it offers a rider the extra dice are rolled and dealt through the same damage path (same hit-point resource, same
    /// damage type, doubled dice on a critical hit), and an <see cref="AttackRiderLogEntry"/> says so. A miss, or no rule, adds nothing.
    /// </summary>
    public class AttackRiderTests
    {
        private static readonly DefinitionId Strength = new("stat-strength");
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private sealed class FixedAttackBonusRule : IAttackBonusRule
        {
            private readonly AdvantageState advantage;
            public FixedAttackBonusRule(AdvantageState advantage = AdvantageState.Normal) => this.advantage = advantage;
            public int ComputeAttackBonus(RuntimeEntityId attacker, DefinitionId governingStatId) => 0;
            public int ComputeCriticalThreshold(RuntimeEntityId attacker) => 20;
            public AdvantageState ComputeAdvantage(RuntimeEntityId attacker, RuntimeEntityId target) => advantage;
            public int ComputeAttackCount(RuntimeEntityId attacker) => 1;
        }

        private sealed class FixedArmorClassRule : IArmorClassRule { public int ComputeArmorClass(RuntimeEntityId target) => 10; }

        private sealed class RecordingRiderRule : IAttackRiderRule
        {
            private readonly AttackRider? offer;
            public RecordingRiderRule(AttackRider? offer) => this.offer = offer;
            public AttackRiderQuery Last { get; private set; }
            public int Asked { get; private set; }
            public bool TryGetRider(in AttackRiderQuery attack, out AttackRider rider) { Asked++; Last = attack; rider = offer ?? default; return offer.HasValue; }
        }

        private static AttackRollEffect Attack(int weaponDice, int weaponSides)
        {
            var damage = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(damage, "hitPointResource", TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points"));
            TestDefinitionFactory.SetPrivateField(damage, "amount", new DiceExpression(weaponDice, weaponSides, 0));
            TestDefinitionFactory.SetPrivateField(damage, "damageTypeId", new DefinitionId("damage-piercing"));
            var effect = new AttackRollEffect();
            TestDefinitionFactory.SetPrivateField(effect, "governingStatId", Strength);
            TestDefinitionFactory.SetPrivateField(effect, "onHit", damage);
            return effect;
        }

        private static (CharacterRegistry Registry, RuntimeEntityId Attacker, RuntimeEntityId Target) Combatants()
        {
            var registry = new CharacterRegistry();
            var attacker = new CharacterState(RuntimeEntityId.NewId()); registry.Register(attacker);
            var target = new CharacterState(RuntimeEntityId.NewId()); target.AddResource(new ResourceState(HitPoints, max: 40)); registry.Register(target);
            return (registry, attacker.Id, target.Id);
        }

        private static int Hp(CharacterRegistry registry, RuntimeEntityId id) { registry.TryGetCharacter(id, out var character); character.TryGetResource(HitPoints, out var hp); return hp.Current; }

        private static AbilityExecutionContext Context(CharacterRegistry registry, RuntimeEntityId attacker, RuntimeEntityId target, TestRandomSource random, CombatLog log, IAttackRiderRule rule, AdvantageState advantage = AdvantageState.Normal, AbilityDefinition ability = null)
            => new(attacker, new[] { target }, registry, new DiceService(random), log, attackBonusRule: new FixedAttackBonusRule(advantage), armorClassRule: new FixedArmorClassRule(), attackRiderRule: rule, ability: ability);

        [Test]
        public void AHitTakesTheRidersExtraDiceAfterTheWeaponsOwnDamage_AndIsLogged()
        {
            var (registry, attacker, target) = Combatants(); var random = new TestRandomSource(); random.EnqueueResults(15, 3, 4);   // d20, weapon 1d6, rider 1d6
            var log = new CombatLog(); var rule = new RecordingRiderRule(new AttackRider(new DiceExpression(1, 6, 0), "Sneak Attack"));
            Attack(1, 6).Apply(Context(registry, attacker, target, random, log, rule));
            Assert.AreEqual(40 - 3 - 4, Hp(registry, target), "weapon 3 plus rider 4");
            var entry = log.Entries.OfType<AttackRiderLogEntry>().Single();
            Assert.AreEqual("Sneak Attack", entry.Label); Assert.AreEqual(attacker, entry.Source); Assert.AreEqual(target, entry.Target);
            var order = log.Entries.Select(e => e.GetType()).ToList();
            Assert.Less(order.IndexOf(typeof(DamageLogEntry)), order.IndexOf(typeof(AttackRiderLogEntry)), "the weapon's own damage is logged first");
        }

        [Test]
        public void AMissIsNeverAskedAndAddsNothing()
        {
            var (registry, attacker, target) = Combatants(); var random = new TestRandomSource(); random.EnqueueResults(5);   // 5 vs AC 10: a miss
            var rule = new RecordingRiderRule(new AttackRider(new DiceExpression(1, 6, 0), "Sneak Attack"));
            Attack(1, 6).Apply(Context(registry, attacker, target, random, new CombatLog(), rule));
            Assert.AreEqual(40, Hp(registry, target)); Assert.AreEqual(0, rule.Asked);
        }

        [Test]
        public void ACriticalHitDoublesTheRidersDiceAsItDoublesTheWeapons()
        {
            var (registry, attacker, target) = Combatants(); var random = new TestRandomSource(); random.EnqueueResults(20, 3, 3, 4, 4);   // a natural 20: weapon 2d6, rider 2d6
            var rule = new RecordingRiderRule(new AttackRider(new DiceExpression(1, 6, 0), "Sneak Attack"));
            Attack(1, 6).Apply(Context(registry, attacker, target, random, new CombatLog(), rule));
            Assert.AreEqual(40 - (3 + 3) - (4 + 4), Hp(registry, target)); Assert.IsTrue(rule.Last.IsCritical);
        }

        [Test]
        public void TheRuleIsToldWhatItNeedsToDecide_TheRollsAdvantageTheAbilityAndTheTarget()
        {
            var (registry, attacker, target) = Combatants(); var random = new TestRandomSource(); random.EnqueueResults(15, 15, 3);   // advantage rolls two d20s
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-test-dagger", "Dagger", TargetRelation.Enemy);
            var rule = new RecordingRiderRule(null);
            Attack(1, 6).Apply(Context(registry, attacker, target, random, new CombatLog(), rule, AdvantageState.Advantage, ability));
            Assert.AreEqual(1, rule.Asked); Assert.AreEqual(AdvantageState.Advantage, rule.Last.Advantage); Assert.AreSame(ability, rule.Last.Ability);
            Assert.AreEqual(attacker, rule.Last.Attacker); Assert.AreEqual(target, rule.Last.Target); Assert.IsFalse(rule.Last.IsCritical);
            Assert.AreEqual(40 - 3, Hp(registry, target), "no rider offered: the weapon alone");
        }

        [Test]
        public void WithNoRuleNothingChanges()
        {
            var (registry, attacker, target) = Combatants(); var random = new TestRandomSource(); random.EnqueueResults(15, 3);
            Attack(1, 6).Apply(Context(registry, attacker, target, random, new CombatLog(), rule: null));
            Assert.AreEqual(40 - 3, Hp(registry, target));
        }
    }
}
