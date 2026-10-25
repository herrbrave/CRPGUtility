using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// An ability that fires several projectiles (Magic Missile's three darts) spreads them over the creatures the caster chose: one target takes every dart, two targets take two and one, three take one each. Every dart is its own roll. Without the option an ability behaves as before: one effect run
    /// per chosen creature. The caller's list stays duplicate-free (that validation is unchanged); only the cast's own list of strikes is repeated.
    /// </summary>
    public class SpreadProjectilesTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private static AbilityDefinition Ability(int maxTargets, bool spread)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-darts");
            TestDefinitionFactory.SetPrivateField(ability, "costs", new List<AbilityCost>());
            var damage = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(damage, "hitPointResource", TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points"));
            TestDefinitionFactory.SetPrivateField(damage, "amount", new DiceExpression(1, 4, 1));
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect> { damage });
            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", maxTargets);
            TestDefinitionFactory.SetPrivateField(targeting, "relation", TargetRelation.Enemy);
            TestDefinitionFactory.SetPrivateField(targeting, "range", 50f);
            TestDefinitionFactory.SetPrivateField(targeting, "requireLineOfSight", false);
            TestDefinitionFactory.SetPrivateField(targeting, "spreadProjectiles", spread);
            TestDefinitionFactory.SetPrivateField(ability, "targeting", targeting);
            return ability;
        }

        private static (AbilityResolutionService Service, CharacterRegistry Registry, RuntimeEntityId Caster, List<RuntimeEntityId> Targets) SetUp(int targetCount, params int[] dartRolls)
        {
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId()); registry.Register(caster);
            var targets = new List<RuntimeEntityId>();
            for (var i = 0; i < targetCount; i++)
            {
                var target = new CharacterState(RuntimeEntityId.NewId()); target.AddResource(new ResourceState(HitPoints, max: 30)); registry.Register(target); targets.Add(target.Id);
            }

            var random = new TestRandomSource(); foreach (var roll in dartRolls) random.EnqueueResult(roll);
            return (new AbilityResolutionService(registry, new DiceService(random)), registry, caster.Id, targets);
        }

        private static int Hp(CharacterRegistry registry, RuntimeEntityId id) { registry.TryGetCharacter(id, out var character); character.TryGetResource(HitPoints, out var hp); return hp.Current; }

        [Test]
        public void OneTargetTakesEveryDart_EachRolledOnItsOwn()
        {
            var (service, registry, caster, targets) = SetUp(1, 1, 2, 4);   // d4 + 1: 2, 3 and 5
            var result = service.Execute(caster, Ability(maxTargets: 3, spread: true), targets);
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(30 - (2 + 3 + 5), Hp(registry, targets[0]), "three darts, three rolls");
        }

        [Test]
        public void TwoTargetsTakeTwoDartsAndOne()
        {
            var (service, registry, caster, targets) = SetUp(2, 3, 3, 3);   // each dart 4
            Assert.IsTrue(service.Execute(caster, Ability(3, true), targets).Succeeded);
            Assert.AreEqual(30 - 8, Hp(registry, targets[0]), "the first chosen creature takes the extra dart");
            Assert.AreEqual(30 - 4, Hp(registry, targets[1]));
        }

        [Test]
        public void ThreeTargetsTakeOneEach()
        {
            var (service, registry, caster, targets) = SetUp(3, 3, 3, 3);
            Assert.IsTrue(service.Execute(caster, Ability(3, true), targets).Succeeded);
            Assert.IsTrue(targets.All(t => Hp(registry, t) == 26));
        }

        [Test]
        public void WithoutTheOptionAnAbilityIsOneStrikePerChosenCreature_AsBefore()
        {
            var (service, registry, caster, targets) = SetUp(1, 3, 3, 3);
            Assert.IsTrue(service.Execute(caster, Ability(3, false), targets).Succeeded);
            Assert.AreEqual(26, Hp(registry, targets[0]), "a single dart only");
        }

        [Test]
        public void TheCallersListMustStillBeFreeOfDuplicates()
        {
            var (service, _, caster, targets) = SetUp(1, 3, 3, 3);
            var result = service.Validate(caster, Ability(3, true), new[] { targets[0], targets[0] });
            Assert.IsFalse(result.IsValid); Assert.AreEqual(AbilityRejectionReason.DuplicateTarget, result.Reason);
        }
    }
}
