using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>A roll-free look at an attack: the chance to hit (attack bonus against armour class, natural 1 always misses, the critical threshold always hits, advantage and disadvantage) and the damage dice, from the same rules the resolution uses.</summary>
    public class AbilityPreviewTests
    {
        private sealed class FixedAttack : IAttackBonusRule
        {
            public int Bonus, Threshold = 20; public AdvantageState Advantage;
            public int ComputeAttackBonus(RuntimeEntityId attacker, DefinitionId governingStatId) => Bonus;
            public int ComputeCriticalThreshold(RuntimeEntityId attacker) => Threshold;
            public AdvantageState ComputeAdvantage(RuntimeEntityId attacker, RuntimeEntityId target) => Advantage;
            public int ComputeAttackCount(RuntimeEntityId attacker) => 1;
        }

        private sealed class FixedArmor : IArmorClassRule { public int Ac; public int ComputeArmorClass(RuntimeEntityId target) => Ac; }

        private static AbilityDefinition Attack(DiceExpression? damage)
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-test-strike", "Strike", TargetRelation.Enemy);
            AbilityEffect onHit = null;
            if (damage.HasValue)
            {
                var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp-test", "HP");
                var dmg = new DamageEffect();
                TestDefinitionFactory.SetPrivateField(dmg, "hitPointResource", hp); TestDefinitionFactory.SetPrivateField(dmg, "amount", damage.Value);
                onHit = dmg;
            }

            var roll = new AttackRollEffect();
            TestDefinitionFactory.SetPrivateField(roll, "onHit", onHit);
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect> { roll });
            return ability;
        }

        private static AbilityPreviewResult Preview(AbilityDefinition ability, int bonus, int ac, int threshold = 20, AdvantageState advantage = AdvantageState.Normal) =>
            AbilityPreview.Compute(ability, RuntimeEntityId.NewId(), RuntimeEntityId.NewId(), new CharacterRegistry(), new FixedAttack { Bonus = bonus, Threshold = threshold, Advantage = advantage }, new FixedArmor { Ac = ac });

        [Test]
        public void AnOrdinaryAttackHitsOnTheNumbersThatBeatArmourClass()
        {
            var r = Preview(Attack(new DiceExpression(1, 6, 1)), bonus: 5, ac: 15);   // needs a natural 10 or better: 10..20 = 11 of 20
            Assert.IsTrue(r.HasAttackRoll); Assert.AreEqual(5, r.AttackBonus); Assert.AreEqual(15, r.ArmorClass);
            Assert.AreEqual(0.55f, r.HitChance, 0.0001f);
        }

        [Test]
        public void ANaturalTwentyAlwaysHitsAndANaturalOneAlwaysMisses()
        {
            Assert.AreEqual(0.05f, Preview(Attack(null), bonus: 0, ac: 30).HitChance, 0.0001f, "only the natural 20");
            Assert.AreEqual(0.95f, Preview(Attack(null), bonus: 20, ac: 5).HitChance, 0.0001f, "everything but the natural 1");
        }

        [Test]
        public void AnImprovedCriticalThresholdAlwaysHitsFromThatNumber() =>
            Assert.AreEqual(0.10f, Preview(Attack(null), bonus: 0, ac: 30, threshold: 19).HitChance, 0.0001f);

        [Test]
        public void AdvantageAndDisadvantageChangeTheChanceTheWayTwoDiceDo()
        {
            Assert.AreEqual(1f - 0.45f * 0.45f, Preview(Attack(null), 5, 15, advantage: AdvantageState.Advantage).HitChance, 0.0001f);
            Assert.AreEqual(0.55f * 0.55f, Preview(Attack(null), 5, 15, advantage: AdvantageState.Disadvantage).HitChance, 0.0001f);
            Assert.AreEqual(AdvantageState.Advantage, Preview(Attack(null), 5, 15, advantage: AdvantageState.Advantage).Advantage);
        }

        [Test]
        public void TheDamageDiceAndTheirAverageAreReported()
        {
            var r = Preview(Attack(new DiceExpression(2, 6, 3)), 5, 15);
            Assert.IsTrue(r.HasDamage); Assert.AreEqual("2d6+3", r.DamageText); Assert.AreEqual(10f, r.AverageDamage, 0.0001f);
            Assert.AreEqual("1d8", Preview(Attack(new DiceExpression(1, 8)), 5, 15).DamageText);
            Assert.AreEqual("1d4-1", Preview(Attack(new DiceExpression(1, 4, -1)), 5, 15).DamageText);
        }

        [Test]
        public void AnAbilityWithoutAnAttackRollHasNoChanceToShow()
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-test-wave", "Wave", TargetRelation.Enemy);
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect>());
            var r = Preview(ability, 5, 15);
            Assert.IsFalse(r.HasAttackRoll); Assert.IsFalse(r.HasDamage);
        }

        [Test]
        public void ADirectDamageEffectIsReportedWithoutAChance()
        {
            var ability = TestDefinitionFactory.CreateAbilityDefinition("ability-test-burn", "Burn", TargetRelation.Enemy);
            var dmg = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(dmg, "hitPointResource", TestDefinitionFactory.CreateResourceDefinition("resource-hp-test", "HP")); TestDefinitionFactory.SetPrivateField(dmg, "amount", new DiceExpression(3, 4));
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect> { dmg });
            var r = Preview(ability, 5, 15);
            Assert.IsFalse(r.HasAttackRoll); Assert.IsTrue(r.HasDamage); Assert.AreEqual("3d4", r.DamageText);
        }
    }
}
