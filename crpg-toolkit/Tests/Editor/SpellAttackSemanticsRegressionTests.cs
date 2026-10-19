using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public sealed class SpellAttackSemanticsRegressionTests
    {
        private sealed class RecordingEffect : AbilityEffect
        {
            public readonly List<AbilityExecutionContext> Calls = new List<AbilityExecutionContext>();
            public override void Apply(AbilityExecutionContext context)
            {
                Calls.Add(context);
                foreach (RuntimeEntityId target in context.Targets) context.OwnedTargets.Add(target);
            }
        }

        private sealed class FixedAttackRule : IAttackBonusRule
        {
            public DefinitionId LastStat;
            public int MartialPolicyCalls;
            public int ComputeAttackBonus(RuntimeEntityId attacker, DefinitionId stat) { LastStat = stat; return 0; }
            public int ComputeCriticalThreshold(RuntimeEntityId attacker) { MartialPolicyCalls++; return 19; }
            public int ComputeAttackCount(RuntimeEntityId attacker) { MartialPolicyCalls++; return 3; }
            public AdvantageState ComputeAdvantage(RuntimeEntityId attacker, RuntimeEntityId target) => AdvantageState.Normal;
        }

        private sealed class FixedArmor : IArmorClassRule
        {
            private readonly int value;
            public FixedArmor(int value) => this.value = value;
            public int ComputeArmorClass(RuntimeEntityId target) => value;
        }

        private sealed class FixedDice : IDiceService
        {
            private readonly int natural;
            private readonly bool discardedTwenty;
            public int Calls;
            public FixedDice(int natural, bool discardedTwenty = false) { this.natural = natural; this.discardedTwenty = discardedTwenty; }
            public RollResult Roll(RollRequest request)
            {
                Calls++;
                DieRollResult[] dice = discardedTwenty
                    ? new[] { new DieRollResult(20, 20, false), new DieRollResult(20, natural, true) }
                    : new[] { new DieRollResult(20, natural, true) };
                return new RollResult(request, dice, 0);
            }
        }

        private static void Set(object owner, string fieldName, object value)
        {
            FieldInfo field = owner.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(owner, value);
        }

        private static (AttackRollEffect Effect, AbilityExecutionContext Context, RecordingEffect Hits, FixedDice Dice, FixedAttackRule Rule) Setup(
            int natural, int armor, bool martial = false, bool discardedTwenty = false)
        {
            CharacterRegistry registry = new CharacterRegistry();
            CharacterState caster = new CharacterState(RuntimeEntityId.NewId());
            caster.SpellcastingStatId = new DefinitionId("stat-charisma");
            registry.Register(caster);
            RuntimeEntityId target = RuntimeEntityId.NewId();
            FixedDice dice = new FixedDice(natural, discardedTwenty);
            FixedAttackRule rule = new FixedAttackRule();
            RecordingEffect hits = new RecordingEffect();
            AttackRollEffect effect = new AttackRollEffect();
            Set(effect, "onHit", hits);
            if (martial) Set(effect, "governingStatId", new DefinitionId("stat-strength"));
            AbilityExecutionContext context = new AbilityExecutionContext(caster.Id, new[] { target }, registry, dice, null,
                attackBonusRule: rule, armorClassRule: new FixedArmor(armor), upcastLevels: 2, ownerId: EffectOwnerId.NewOwner());
            return (effect, context, hits, dice, rule);
        }

        [Test]
        public void SpellRollDoesNotInheritExtraAttack()
        {
            var test = Setup(12, 10);
            test.Effect.Apply(test.Context);
            Assert.AreEqual(1, test.Dice.Calls, "SPELL_INHERITS_EXTRA_ATTACK");
            Assert.AreEqual(1, test.Hits.Calls.Count);
        }

        [Test]
        public void SpellNineteenDoesNotAutomaticallyHit()
        {
            var test = Setup(19, 50);
            test.Effect.Apply(test.Context);
            Assert.AreEqual(0, test.Hits.Calls.Count, "SPELL_INHERITS_WEAPON_CRITICAL_THRESHOLD");
        }

        [Test]
        public void SpellNineteenThatMeetsArmorIsNotCritical()
        {
            var test = Setup(19, 10);
            test.Effect.Apply(test.Context);
            Assert.IsFalse(test.Hits.Calls[0].CriticalTargets.Any(), "SPELL_INHERITS_WEAPON_CRITICAL_THRESHOLD");
        }

        [Test]
        public void SpellDoesNotConsultMartialCountOrThresholdPolicies()
        {
            var test = Setup(1, 10);
            test.Effect.Apply(test.Context);
            Assert.AreEqual(0, test.Rule.MartialPolicyCalls, "SPELL_CONSULTS_MARTIAL_POLICY");
        }

        [Test]
        public void NaturalTwentyStillHitsAndCritsForSpells()
        {
            var test = Setup(20, 99);
            test.Effect.Apply(test.Context);
            CollectionAssert.AreEquivalent(test.Context.Targets, test.Hits.Calls[0].CriticalTargets);
        }

        [Test]
        public void ExplicitMartialAttackKeepsExtraAttackAndImprovedCritical()
        {
            var test = Setup(19, 99, martial: true);
            test.Effect.Apply(test.Context);
            Assert.AreEqual(3, test.Dice.Calls);
            Assert.AreEqual(3, test.Hits.Calls.Count);
            Assert.IsTrue(test.Hits.Calls.All(c => c.CriticalTargets.Contains(test.Context.Targets[0])));
        }

        [Test]
        public void SpellKeepsCasterStatOwnerUpcastAndSharedRecipients()
        {
            var test = Setup(20, 10);
            test.Effect.Apply(test.Context);
            AbilityExecutionContext hit = test.Hits.Calls[0];
            Assert.AreEqual(new DefinitionId("stat-charisma"), test.Rule.LastStat);
            Assert.AreEqual(test.Context.OwnerId, hit.OwnerId);
            Assert.AreEqual(test.Context.UpcastLevels, hit.UpcastLevels);
            Assert.AreSame(test.Context.OwnedTargets, hit.OwnedTargets);
            Assert.IsTrue(test.Context.OwnedTargets.Contains(test.Context.Targets[0]));
        }

        [Test]
        public void SpellUsesKeptDieRatherThanDiscardedTwenty()
        {
            var test = Setup(19, 99, discardedTwenty: true);
            test.Effect.Apply(test.Context);
            Assert.AreEqual(0, test.Hits.Calls.Count, "SPELL_INHERITS_WEAPON_CRITICAL_THRESHOLD");
        }
    }
}
