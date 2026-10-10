using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The design table (GameDesign) against the REAL engine: each combatant is spawned through NpcSpawnService and its Armor Class, hit points, level,
    /// attack bonuses and attack count are computed by the shipped SRD5 rules. A drift in either the authoring or the engine math fails here -- including the
    /// silent kind (a missing ability score reads as -5; an unresolvable feature adds no armor).</summary>
    public class DesignNumbersTests
    {
        private ContentFixture content;
        private CharacterRegistry chars;

        [SetUp]
        public void SetUp()
        {
            content = ContentFixture.Load();
            chars = new CharacterRegistry();
        }

        [Test]
        public void EveryUnitSpawnsWithItsDesignedLevelHitPointsAndTurnResources()
        {
            foreach (var unit in GameDesign.Units)
            {
                var c = content.Spawn(chars, unit.Id);
                Assert.AreEqual(unit.Level, c.Level, unit.Id);
                Assert.IsTrue(c.TryGetResource(new DefinitionId(GameDesign.Hp), out var hp));
                Assert.AreEqual(unit.Hp, hp.Max, $"{unit.Id} max hp"); Assert.AreEqual(unit.Hp, hp.Current, $"{unit.Id} starts at full hp");
                foreach (var id in new[] { GameDesign.Action, GameDesign.BonusAction, GameDesign.Reaction, GameDesign.Movement })
                {
                    Assert.IsTrue(c.TryGetResource(new DefinitionId(id), out var r), $"{unit.Id} has no {id}");
                    Assert.AreEqual(r.Max, r.Current, $"{unit.Id} {id} starts full");
                }
            }
        }

        [Test]
        public void ArmorClassComputedByTheRealRuleMatchesTheDesign()
        {
            var armor = content.ArmorRule(chars);
            foreach (var unit in GameDesign.Units)
            {
                var c = content.Spawn(chars, unit.Id);
                Assert.AreEqual(unit.ExpectedAc, armor.ComputeArmorClass(c.Id), $"{unit.Id} AC (10 + Dex {unit.DexModifier} + armor feature {unit.ArmorBonus})");
            }
        }

        [Test]
        public void EveryAttackBonusComputedByTheRealRuleMatchesTheDesign()
        {
            var attack = content.AttackRule(chars);
            foreach (var unit in GameDesign.Units)
            {
                var c = content.Spawn(chars, unit.Id);
                foreach (var abilityId in unit.Abilities.Where(a => a != GameDesign.SecondWindId))
                {
                    var spec = GameDesign.Ability(abilityId);
                    if (spec.Kind is AbilityKind.HealAlly or AbilityKind.SelfBuff) continue;
                    var statId = spec.Kind == AbilityKind.SpellAttack ? c.SpellcastingStatId : new DefinitionId(spec.StatId);
                    Assert.IsTrue(statId.IsValid, $"{unit.Id}/{abilityId}: no governing stat");
                    Assert.AreEqual(unit.ExpectedAttackBonus(spec), attack.ComputeAttackBonus(c.Id, statId), $"{unit.Id} {abilityId} to-hit");
                }
            }
        }

        [Test]
        public void AttackCountIsOneExceptTwoForTheTrollAndTheKing()
        {
            var attack = content.AttackRule(chars);
            foreach (var unit in GameDesign.Units)
                Assert.AreEqual(unit.Multiattack ? 2 : 1, attack.ComputeAttackCount(content.Spawn(chars, unit.Id).Id), unit.Id);
        }

        [Test]
        public void AuthoredAbilityAssetsMatchTheDesignDiceRangeAndGoverningStat()
        {
            foreach (var spec in GameDesign.Abilities)
            {
                var ability = content.Resolve<AbilityDefinition>(spec.Id);
                Assert.AreEqual(spec.Name, ability.DisplayName);
                Assert.AreEqual(spec.Range, ability.Targeting.Range, 1e-4f, $"{spec.Id} range");
                Assert.AreEqual(spec.Cue ?? "", ability.AnimationCueId.Value ?? "", $"{spec.Id} cue");

                switch (spec.Kind)
                {
                    case AbilityKind.MeleeAttack:
                    case AbilityKind.RangedAttack:
                    case AbilityKind.SpellAttack:
                    {
                        var attack = ability.Effects.OfType<AttackRollEffect>().Single();
                        var stat = ContentFixture.Field<DefinitionId>(attack, "governingStatId");
                        if (spec.Kind == AbilityKind.SpellAttack) Assert.IsFalse(stat.IsValid, $"{spec.Id}: a spell attack leaves the stat None (the caster's SpellcastingStatId)");
                        else Assert.AreEqual(spec.StatId, stat.Value, $"{spec.Id} governing stat");
                        var damage = ContentFixture.Field<AbilityEffect>(attack, "onHit");
                        AssertDice(spec, ContentFixture.Field<DiceExpression>(damage, "amount"));
                        Assert.AreEqual(TargetRelation.Enemy, ability.Targeting.Relation);
                        break;
                    }
                    case AbilityKind.HealAlly:
                        AssertDice(spec, ContentFixture.Field<DiceExpression>(ability.Effects.OfType<HealEffect>().Single(), "amount"));
                        Assert.AreEqual(TargetRelation.Ally, ability.Targeting.Relation);
                        break;
                    case AbilityKind.SelfBuff:
                        Assert.AreEqual(TargetMode.Self, ability.Targeting.Mode, "the combat UI only auto-casts Self-mode abilities");
                        Assert.AreEqual(TargetRelation.Self, ability.Targeting.Relation);
                        break;
                }
            }
        }

        private static void AssertDice(AbilitySpec spec, DiceExpression dice)
        {
            Assert.AreEqual(spec.Count, dice.Count, $"{spec.Id} dice count");
            Assert.AreEqual(spec.Sides, dice.Sides, $"{spec.Id} sides");
            Assert.AreEqual(spec.Modifier, dice.Modifier, $"{spec.Id} flat modifier");
        }

        [Test]
        public void TheShippedSecondWindIsReusedAndNeedsOurNpcToOwnItsUsePool()
        {
            var ability = content.Resolve<AbilityDefinition>(GameDesign.SecondWindId);
            CollectionAssert.AreEquivalent(new[] { GameDesign.BonusAction, GameDesign.SecondWindUses }, ability.Costs.Select(c => c.Resource.Id.Value).ToList());
            var bjorn = content.Spawn(chars, "bjorn");
            Assert.IsTrue(bjorn.TryGetResource(new DefinitionId(GameDesign.SecondWindUses), out var uses));
            Assert.AreEqual(1, uses.Max);
        }
    }
}
