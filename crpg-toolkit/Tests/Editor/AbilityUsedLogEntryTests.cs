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
    /// A cast says what it was before it does anything: <see cref="AbilityUsedLogEntry"/> is logged once per execution, ahead of the rolls and damage, naming the ability, the caster and every strike (a spread ability lists a creature once per projectile). A presentation reads it to start the
    /// cast effect and launch one projectile per strike, then reads the rolls that follow to know what hit.
    /// </summary>
    public class AbilityUsedLogEntryTests
    {
        private static readonly DefinitionId HitPoints = new("resource-hp");

        private static AbilityDefinition Ability(int maxTargets, bool spread)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-used-log-test"); TestDefinitionFactory.SetPrivateField(ability, "costs", new List<AbilityCost>());
            var damage = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(damage, "hitPointResource", TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points"));
            TestDefinitionFactory.SetPrivateField(damage, "amount", new DiceExpression(1, 4, 1));
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect> { damage });
            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1); TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", maxTargets); TestDefinitionFactory.SetPrivateField(targeting, "relation", TargetRelation.Enemy);
            TestDefinitionFactory.SetPrivateField(targeting, "range", 50f); TestDefinitionFactory.SetPrivateField(targeting, "requireLineOfSight", false); TestDefinitionFactory.SetPrivateField(targeting, "spreadProjectiles", spread);
            TestDefinitionFactory.SetPrivateField(ability, "targeting", targeting);
            return ability;
        }

        private static (AbilityResolutionService Service, CombatLog Log, RuntimeEntityId Caster, RuntimeEntityId Target, AbilityDefinition Ability) Setup(int maxTargets, bool spread)
        {
            var registry = new CharacterRegistry(); var caster = new CharacterState(RuntimeEntityId.NewId()); registry.Register(caster);
            var target = new CharacterState(RuntimeEntityId.NewId()); target.AddResource(new ResourceState(HitPoints, max: 40)); registry.Register(target);
            var log = new CombatLog(); var random = new TestRandomSource(); random.EnqueueResults(1, 1, 1, 1);
            return (new AbilityResolutionService(registry, new DiceService(random), log), log, caster.Id, target.Id, Ability(maxTargets, spread));
        }

        [Test]
        public void ACastIsLoggedOnceBeforeItsRollsAndDamage()
        {
            var (service, log, caster, target, ability) = Setup(1, false);
            Assert.IsTrue(service.Execute(caster, ability, new[] { target }).Succeeded);
            var used = log.Entries.OfType<AbilityUsedLogEntry>().Single();
            Assert.AreEqual("ability-used-log-test", used.AbilityId); Assert.AreEqual(caster, used.Source); CollectionAssert.AreEqual(new[] { target }, used.Targets);
            Assert.AreEqual(0, log.Entries.ToList().IndexOf(used), "first in the log, before the damage roll");
        }

        [Test]
        public void ASpreadAbilityListsTheCreatureOncePerProjectile()
        {
            var (service, log, caster, target, ability) = Setup(3, true);
            Assert.IsTrue(service.Execute(caster, ability, new[] { target }).Succeeded);
            CollectionAssert.AreEqual(new[] { target, target, target }, log.Entries.OfType<AbilityUsedLogEntry>().Single().Targets, "three darts at the one target");
        }

        [Test]
        public void ARejectedCastLogsNothing()
        {
            var (service, log, caster, _, ability) = Setup(1, false);
            Assert.IsFalse(service.Execute(caster, ability, new RuntimeEntityId[0]).Succeeded);
            Assert.IsEmpty(log.Entries.OfType<AbilityUsedLogEntry>());
        }
    }
}
