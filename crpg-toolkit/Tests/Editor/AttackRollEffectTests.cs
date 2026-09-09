using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class AttackRollEffectTests
    {
        private static readonly DefinitionId Strength = new("stat-strength");
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private sealed class FixedAttackBonusRule : IAttackBonusRule
        {
            private readonly int bonus;
            private readonly int criticalThreshold;
            private readonly AdvantageState advantage;
            private readonly int attackCount;
            public FixedAttackBonusRule(int bonus, int criticalThreshold = 20, AdvantageState advantage = AdvantageState.Normal, int attackCount = 1)
            {
                this.bonus = bonus;
                this.criticalThreshold = criticalThreshold;
                this.advantage = advantage;
                this.attackCount = attackCount;
            }
            public DefinitionId LastGoverningStatId { get; private set; }
            public int ComputeAttackBonus(RuntimeEntityId attacker, DefinitionId governingStatId) { LastGoverningStatId = governingStatId; return bonus; }
            public int ComputeCriticalThreshold(RuntimeEntityId attacker) => criticalThreshold;
            public AdvantageState ComputeAdvantage(RuntimeEntityId attacker, RuntimeEntityId target) => advantage;
            public int ComputeAttackCount(RuntimeEntityId attacker) => attackCount;
        }

        private sealed class FixedArmorClassRule : IArmorClassRule
        {
            private readonly int armorClass;
            public FixedArmorClassRule(int armorClass) => this.armorClass = armorClass;
            public int ComputeArmorClass(RuntimeEntityId target) => armorClass;
        }

        private static AttackRollEffect CreateEffect(AbilityEffect onHit)
        {
            var effect = new AttackRollEffect();
            TestDefinitionFactory.SetPrivateField(effect, "governingStatId", Strength);
            TestDefinitionFactory.SetPrivateField(effect, "onHit", onHit);
            return effect;
        }

        private static DamageEffect CreateDamageEffect(int count, int sides, int modifier)
        {
            var effect = new DamageEffect();
            var hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            TestDefinitionFactory.SetPrivateField(effect, "hitPointResource", hp);
            TestDefinitionFactory.SetPrivateField(effect, "amount", new DiceExpression(count, sides, modifier));
            return effect;
        }

        private static (CharacterRegistry Registry, RuntimeEntityId Attacker, RuntimeEntityId Target) SetUpCombatants()
        {
            var registry = new CharacterRegistry();
            var attacker = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(attacker);

            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(HitPoints, max: 20));
            registry.Register(target);

            return (registry, attacker.Id, target.Id);
        }

        [Test]
        public void HitsAndAppliesOnHitEffectWhenTotalMeetsOrBeatsArmorClass()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var random = new TestRandomSource();
            random.EnqueueResults(15, 3); // attack d20, damage 1d6
            var dice = new DiceService(random);
            var combatLog = new CombatLog();

            var effect = CreateEffect(CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, combatLog, attackBonusRule: new FixedAttackBonusRule(2), armorClassRule: new FixedArmorClassRule(15));
            // Total = 15 + 2 = 17 >= AC 15 -> hit.

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(17, hp.Current); // 20 - 3 damage
        }

        [Test]
        public void MissesAndSkipsOnHitEffectWhenTotalIsBelowArmorClass()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var random = new TestRandomSource();
            random.EnqueueResult(5); // attack d20 only -- damage should never be rolled
            var dice = new DiceService(random);

            var effect = CreateEffect(CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, new CombatLog(), attackBonusRule: new FixedAttackBonusRule(2), armorClassRule: new FixedArmorClassRule(15));
            // Total = 5 + 2 = 7 < AC 15 -> miss.

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(20, hp.Current); // untouched
        }

        [Test]
        public void NaturalOneAlwaysMissesEvenIfBonusWouldClearArmorClass()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var random = new TestRandomSource();
            random.EnqueueResult(1); // natural 1
            var dice = new DiceService(random);

            var effect = CreateEffect(CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, new CombatLog(), attackBonusRule: new FixedAttackBonusRule(20), armorClassRule: new FixedArmorClassRule(5));
            // Total = 1 + 20 = 21 >= AC 5, but natural 1 still misses.

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(20, hp.Current);
        }

        [Test]
        public void NaturalTwentyAlwaysHitsAndDoublesDamageDice()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var random = new TestRandomSource();
            random.EnqueueResults(20, 3, 3); // natural 20, then 2d6 damage (doubled from 1d6)
            var dice = new DiceService(random);

            var effect = CreateEffect(CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, new CombatLog(), attackBonusRule: new FixedAttackBonusRule(0), armorClassRule: new FixedArmorClassRule(50));
            // Total = 20 + 0 = 20 < AC 50, but natural 20 always crits/hits.

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(14, hp.Current); // 20 - (3 + 3) doubled-dice damage
        }

        [Test]
        public void ExtraAttackRollsAndResolvesEachAttackSeparately()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var random = new TestRandomSource();
            random.EnqueueResults(15, 3, 15, 4); // two full attack passes, each hits AC 15, each rolls its own 1d6
            var dice = new DiceService(random);

            var effect = CreateEffect(CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, new CombatLog(),
                attackBonusRule: new FixedAttackBonusRule(0, attackCount: 2), armorClassRule: new FixedArmorClassRule(15));

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(13, hp.Current); // 20 - 3 - 4 -- two separate damage instances, not one doubled roll
        }

        [Test]
        public void ALoweredCriticalThresholdCritsBelowTwenty()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var random = new TestRandomSource();
            random.EnqueueResults(19, 4, 4); // natural 19 crits with a threshold of 19
            var dice = new DiceService(random);

            var effect = CreateEffect(CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, new CombatLog(),
                attackBonusRule: new FixedAttackBonusRule(0, criticalThreshold: 19), armorClassRule: new FixedArmorClassRule(50));

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(12, hp.Current); // 20 - (4 + 4)
        }

        [Test]
        public void DisadvantageRollsTwiceAndKeepsTheWorseResult()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var random = new TestRandomSource();
            random.EnqueueResults(18, 5); // primary 18, secondary 5 -- disadvantage keeps the lower (5)
            var dice = new DiceService(random);

            var effect = CreateEffect(CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, new CombatLog(),
                attackBonusRule: new FixedAttackBonusRule(2, advantage: AdvantageState.Disadvantage), armorClassRule: new FixedArmorClassRule(10));
            // Kept roll must be 5 (the worse one): total = 5 + 2 = 7 < AC 10 -> miss, even though 18 + 2 = 20 would've hit.

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(20, hp.Current); // untouched -- proves the worse die was actually used, not index 0
        }

        [Test]
        public void DoesNothingWhenOnHitIsNotSet()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var random = new TestRandomSource();
            random.EnqueueResult(15);
            var dice = new DiceService(random);

            var effect = CreateEffect(null);
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, new CombatLog(), attackBonusRule: new FixedAttackBonusRule(2), armorClassRule: new FixedArmorClassRule(5));

            Assert.DoesNotThrow(() => effect.Apply(context));
        }

        [Test]
        public void FallsBackToCastersSpellcastingStatWhenGoverningStatIsNotSet()
        {
            var intelligence = new DefinitionId("stat-intelligence");
            var (registry, attacker, target) = SetUpCombatants();
            registry.TryGetCharacter(attacker, out var attackerCharacter);
            attackerCharacter.SpellcastingStatId = intelligence;

            var random = new TestRandomSource();
            random.EnqueueResult(15);
            var dice = new DiceService(random);

            var effect = new AttackRollEffect();
            TestDefinitionFactory.SetPrivateField(effect, "governingStatId", DefinitionId.None);
            TestDefinitionFactory.SetPrivateField(effect, "onHit", CreateDamageEffect(1, 6, 0));

            var attackBonusRule = new FixedAttackBonusRule(0);
            var context = new AbilityExecutionContext(
                attacker, new[] { target }, registry, dice, new CombatLog(), attackBonusRule: attackBonusRule, armorClassRule: new FixedArmorClassRule(50));

            effect.Apply(context);

            Assert.AreEqual(intelligence, attackBonusRule.LastGoverningStatId);
        }

        [Test]
        public void DoesNothingWhenAttackRollRulesAreNotWiredUp()
        {
            var (registry, attacker, target) = SetUpCombatants();
            var dice = new DiceService(new TestRandomSource()); // never rolled

            var effect = CreateEffect(CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(attacker, new[] { target }, registry, dice, new CombatLog());

            Assert.DoesNotThrow(() => effect.Apply(context));
        }
    }
}
