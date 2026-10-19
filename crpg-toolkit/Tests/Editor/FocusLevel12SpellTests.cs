using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEditor;

namespace CRPG.Tests.EditMode
{
    public sealed class FocusLevel12SpellTests
    {
        private const string Content = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/";
        private static readonly DefinitionId HpId = new DefinitionId("resource-hp");

        private sealed class Dice : IDiceService
        {
            private readonly int natural;
            public int Attacks;
            public int DamageDice;
            public Dice(int natural) => this.natural = natural;
            public RollResult Roll(RollRequest request)
            {
                if (request.Purpose == RollPurpose.AttackRoll)
                {
                    Attacks++;
                    return new RollResult(request, new[] { new DieRollResult(20, natural, true) }, 0);
                }
                DamageDice += request.Dice.Count;
                DieRollResult[] results = Enumerable.Range(0, request.Dice.Count)
                    .Select(_ => new DieRollResult(request.Dice.Sides, 2, true)).ToArray();
                return new RollResult(request, results, 0);
            }
        }

        private sealed class Attack : IAttackBonusRule
        {
            public int ComputeAttackBonus(RuntimeEntityId attacker, DefinitionId stat) => 0;
            public int ComputeAttackCount(RuntimeEntityId attacker) => 3;
            public int ComputeCriticalThreshold(RuntimeEntityId attacker) => 19;
            public AdvantageState ComputeAdvantage(RuntimeEntityId attacker, RuntimeEntityId target) => AdvantageState.Normal;
        }
        private sealed class Armor : IArmorClassRule
        {
            private readonly int value;
            public Armor(int value) => this.value = value;
            public int ComputeArmorClass(RuntimeEntityId target) => value;
        }
        private sealed class Resistance : IDamageResistanceRule
        {
            public DefinitionId ReceivedType;
            public int ApplyResistance(RuntimeEntityId target, DefinitionId type, int amount) { ReceivedType = type; return amount; }
        }

        [TestCase(false, 12, 10, 3)]
        [TestCase(true, 12, 10, 4)]
        [TestCase(false, 20, 99, 6)]
        [TestCase(false, 19, 99, 0)]
        [TestCase(true, 20, 99, 8)]
        public void ImportedInflictWoundsExecutesThroughCostAttackDamagePipeline(bool upcast, int natural, int armor, int expectedDice)
        {
            AbilityDefinition ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(Content + "AbilityDefinition_InflictWounds.asset");
            ResourceDefinition slot1 = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(Content + "ResourceDefinition_SpellSlotLevel1.asset");
            ResourceDefinition slot2 = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(Content + "ResourceDefinition_SpellSlotLevel2.asset");
            Assert.IsNotNull(ability); Assert.IsNotNull(slot1); Assert.IsNotNull(slot2);
            CharacterRegistry registry = new CharacterRegistry();
            CharacterState caster = new CharacterState(RuntimeEntityId.NewId());
            caster.SpellcastingStatId = new DefinitionId("stat-wisdom");
            caster.LearnAbility(ability.Id);
            foreach (AbilityCost cost in ability.Costs) caster.AddResource(new ResourceState(cost.Resource.Id, cost.Amount));
            caster.AddResource(new ResourceState(slot2.Id, 1));
            registry.Register(caster);
            CharacterState target = new CharacterState(RuntimeEntityId.NewId());
            target.AddResource(new ResourceState(HpId, 100));
            registry.Register(target);
            Dice dice = new Dice(natural);
            Resistance resistance = new Resistance();
            AbilityResolutionService service = new AbilityResolutionService(registry, dice,
                attackBonusRule: new Attack(), armorClassRule: new Armor(armor), damageResistanceRule: resistance);
            var result = service.Execute(caster.Id, ability, new[] { target.Id }, upcastWithSlot: upcast ? slot2 : null);
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, dice.Attacks);
            Assert.AreEqual(expectedDice, dice.DamageDice);
            target.TryGetResource(HpId, out ResourceState hp);
            Assert.AreEqual(100 - expectedDice * 2, hp.Current);
            if (expectedDice > 0) Assert.AreEqual(new DefinitionId("damage-necrotic"), resistance.ReceivedType);
            caster.TryGetResource(slot1.Id, out ResourceState first);
            caster.TryGetResource(slot2.Id, out ResourceState second);
            Assert.AreEqual(upcast ? 1 : 0, first.Current);
            Assert.AreEqual(upcast ? 0 : 1, second.Current);
        }

        [Test]
        public void SlotMetadataAndRestPoliciesMatchTheirActualLevels()
        {
            for (int level = 1; level <= 2; level++)
            {
                ResourceDefinition slot = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(Content + "ResourceDefinition_SpellSlotLevel" + level + ".asset");
                Assert.AreEqual(level, slot.SpellSlotLevel);
                Assert.AreEqual(RestTrigger.LongRest, slot.RestTrigger);
            }
        }

        [Test]
        public void ImportedGraphUsesSpellCasterStatAndTypedDamage()
        {
            AbilityDefinition ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(Content + "AbilityDefinition_InflictWounds.asset");
            Assert.AreEqual("spell-inflict-wounds", ability.Id.Value);
            Assert.AreEqual(1, ability.SpellLevel);
            Assert.AreEqual(TargetRelation.Any, ability.Targeting.Relation);
            AttackRollEffect attack = (AttackRollEffect)ability.Effects.Single();
            Assert.IsFalse(attack.GoverningStatId.IsValid);
            Assert.AreEqual(new DiceExpression(3, 10), ((DamageEffect)attack.OnHit).Amount);
        }

        [Test]
        public void CanonicalProviderContainsNewSpellAndEveryDamageTypeWithoutDuplicateIds()
        {
            UnityEngine.Object provider = AssetDatabase.LoadMainAssetAtPath(Content + "SRD5RulesetProvider.asset");
            SerializedObject serialized = new SerializedObject(provider);
            SerializedProperty content = serialized.FindProperty("content");
            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < content.arraySize; i++)
            {
                DefinitionAsset definition = content.GetArrayElementAtIndex(i).objectReferenceValue as DefinitionAsset;
                Assert.IsNotNull(definition);
                Assert.IsTrue(ids.Add(definition.Id.Value), "Duplicate definition: " + definition.Id.Value);
            }
            Assert.IsTrue(ids.Contains("spell-inflict-wounds"));
            foreach (string type in new[] { "acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder" })
                Assert.IsTrue(ids.Contains("damage-" + type));
        }
    }
}
