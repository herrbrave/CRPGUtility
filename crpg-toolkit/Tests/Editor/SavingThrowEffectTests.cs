using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SavingThrowEffectTests
    {
        private static readonly DefinitionId Dexterity = new("stat-dexterity");
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private sealed class FixedSavingThrowRule : ISavingThrowRule
        {
            private readonly int bonus;
            private readonly AdvantageState advantage;
            public FixedSavingThrowRule(int bonus, AdvantageState advantage = AdvantageState.Normal) { this.bonus = bonus; this.advantage = advantage; }
            public int ComputeSavingThrowBonus(RuntimeEntityId character, DefinitionId savingStatId) => bonus;
            public AdvantageState ComputeAdvantage(RuntimeEntityId character, DefinitionId savingStatId) => advantage;
            public bool AutoFails(RuntimeEntityId character, DefinitionId savingStatId) => false;
            public int ComputeSpellSaveDC(RuntimeEntityId caster, DefinitionId spellcastingStatId) => 0;
        }

        private sealed class AlwaysAutoFailSavingThrowRule : ISavingThrowRule
        {
            public int ComputeSavingThrowBonus(RuntimeEntityId character, DefinitionId savingStatId) => 0;
            public AdvantageState ComputeAdvantage(RuntimeEntityId character, DefinitionId savingStatId) => AdvantageState.Normal;
            public bool AutoFails(RuntimeEntityId character, DefinitionId savingStatId) => true;
            public int ComputeSpellSaveDC(RuntimeEntityId caster, DefinitionId spellcastingStatId) => 0;
        }

        private sealed class FixedSpellSaveDCRule : ISavingThrowRule
        {
            private readonly int savingThrowBonus;
            private readonly int spellSaveDC;
            public FixedSpellSaveDCRule(int savingThrowBonus, int spellSaveDC) { this.savingThrowBonus = savingThrowBonus; this.spellSaveDC = spellSaveDC; }
            public DefinitionId LastSpellcastingStatId { get; private set; }
            public int ComputeSavingThrowBonus(RuntimeEntityId character, DefinitionId savingStatId) => savingThrowBonus;
            public AdvantageState ComputeAdvantage(RuntimeEntityId character, DefinitionId savingStatId) => AdvantageState.Normal;
            public bool AutoFails(RuntimeEntityId character, DefinitionId savingStatId) => false;
            public int ComputeSpellSaveDC(RuntimeEntityId caster, DefinitionId spellcastingStatId) { LastSpellcastingStatId = spellcastingStatId; return spellSaveDC; }
        }

        private static SavingThrowEffect CreateEffect(int difficultyClass, AbilityEffect onFailure, AbilityEffect onSuccess = null)
        {
            var effect = new SavingThrowEffect();
            TestDefinitionFactory.SetPrivateField(effect, "savingStatId", Dexterity);
            TestDefinitionFactory.SetPrivateField(effect, "difficultyClass", difficultyClass);
            TestDefinitionFactory.SetPrivateField(effect, "onFailure", onFailure);
            TestDefinitionFactory.SetPrivateField(effect, "onSuccess", onSuccess);
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

        private static (CharacterRegistry Registry, RuntimeEntityId Source, RuntimeEntityId Target) SetUpTarget()
        {
            var registry = new CharacterRegistry();
            var source = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(source);

            var target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(HitPoints, max: 20));
            registry.Register(target);

            return (registry, source.Id, target.Id);
        }

        [Test]
        public void FailedSaveAppliesOnFailureEffect()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResults(5, 4); // save d20, then 1d6 damage
            var dice = new DiceService(random);

            var effect = CreateEffect(difficultyClass: 15, onFailure: CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), savingThrowRule: new FixedSavingThrowRule(2));
            // Total = 5 + 2 = 7 < DC 15 -> failure.

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(16, hp.Current); // 20 - 4
        }

        [Test]
        public void SucceededSaveSkipsOnFailureEffect()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResult(15); // save d20 only -- damage should never be rolled
            var dice = new DiceService(random);

            var effect = CreateEffect(difficultyClass: 15, onFailure: CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), savingThrowRule: new FixedSavingThrowRule(2));
            // Total = 15 + 2 = 17 >= DC 15 -> success.

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(20, hp.Current); // untouched
        }

        [Test]
        public void SucceededSaveAppliesOnSuccessEffectWhenSet()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResults(15, 2); // save d20, then half-damage 1d6
            var dice = new DiceService(random);

            var effect = CreateEffect(difficultyClass: 15, onFailure: CreateDamageEffect(2, 6, 0), onSuccess: CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), savingThrowRule: new FixedSavingThrowRule(2));

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(18, hp.Current); // 20 - 2 (the onSuccess half-damage roll, not onFailure's)
        }

        [Test]
        public void DisadvantageRollsTwiceAndKeepsTheWorseResult()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResults(18, 5, 3); // primary 18, secondary 5 (disadvantage keeps the lower), then 1d6 damage
            var dice = new DiceService(random);

            var effect = CreateEffect(difficultyClass: 10, onFailure: CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), savingThrowRule: new FixedSavingThrowRule(2, AdvantageState.Disadvantage));
            // Kept roll must be 5 (the worse one): total = 5 + 2 = 7 < DC 10 -> failure, even though 18 + 2 = 20 would've succeeded.

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.Less(hp.Current, 20); // damage was applied -- proves the worse die decided the outcome
        }

        [Test]
        public void UsesCasterSpellSaveDCWhenFlagIsSet()
        {
            var intelligence = new DefinitionId("stat-intelligence");
            var (registry, source, target) = SetUpTarget();
            registry.TryGetCharacter(source, out var caster);
            caster.SpellcastingStatId = intelligence;

            var random = new TestRandomSource();
            random.EnqueueResults(10, 4); // save d20, then 1d6 damage
            var dice = new DiceService(random);

            var effect = new SavingThrowEffect();
            TestDefinitionFactory.SetPrivateField(effect, "savingStatId", Dexterity);
            TestDefinitionFactory.SetPrivateField(effect, "useCasterSpellSaveDC", true);
            TestDefinitionFactory.SetPrivateField(effect, "onFailure", CreateDamageEffect(1, 6, 0));

            var spellSaveDCRule = new FixedSpellSaveDCRule(savingThrowBonus: 2, spellSaveDC: 15);
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), savingThrowRule: spellSaveDCRule);
            // Total = 10 + 2 = 12 < the rule's DC of 15 -> failure.

            effect.Apply(context);

            Assert.AreEqual(intelligence, spellSaveDCRule.LastSpellcastingStatId);
            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(16, hp.Current); // 20 - 4
        }

        [Test]
        public void AutoFailSkipsTheRollAndAppliesOnFailureEffect()
        {
            var (registry, source, target) = SetUpTarget();
            var random = new TestRandomSource();
            random.EnqueueResult(4); // only the damage die -- no save d20 should be rolled
            var dice = new DiceService(random);

            var effect = CreateEffect(difficultyClass: 15, onFailure: CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), savingThrowRule: new AlwaysAutoFailSavingThrowRule());

            effect.Apply(context);

            registry.TryGetCharacter(target, out var targetCharacter);
            targetCharacter.TryGetResource(HitPoints, out var hp);
            Assert.AreEqual(16, hp.Current); // 20 - 4 -- treated as a failure without ever rolling the d20
        }

        [Test]
        public void DoesNothingWhenSavingThrowRuleIsNotWiredUp()
        {
            var (registry, source, target) = SetUpTarget();
            var dice = new DiceService(new TestRandomSource()); // never rolled

            var effect = CreateEffect(difficultyClass: 10, onFailure: CreateDamageEffect(1, 6, 0));
            var context = new AbilityExecutionContext(source, new[] { target }, registry, dice, new CombatLog());

            Assert.DoesNotThrow(() => effect.Apply(context));
        }

        [Test]
        public void DoesNothingWhenNeitherEffectIsSet()
        {
            var (registry, source, target) = SetUpTarget();
            var dice = new DiceService(new TestRandomSource());

            var effect = CreateEffect(difficultyClass: 10, onFailure: null, onSuccess: null);
            var context = new AbilityExecutionContext(
                source, new[] { target }, registry, dice, new CombatLog(), savingThrowRule: new FixedSavingThrowRule(2));

            Assert.DoesNotThrow(() => effect.Apply(context));
        }
    }
}
