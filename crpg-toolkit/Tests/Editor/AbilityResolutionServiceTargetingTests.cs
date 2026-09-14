using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 4.2: relation/range/LOS validation and provider degradation semantics -- kept separate
    /// from AbilityResolutionServiceTests.cs since these all need the new optional Spatial provider fakes,
    /// which the bulk of that file's tests deliberately don't wire up at all.</summary>
    public class AbilityResolutionServiceTargetingTests
    {
        private static AbilityDefinition CreateAbility(
            TargetRelation relation = TargetRelation.Enemy, float range = 5f, bool requireLineOfSight = true)
        {
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(ability, "id", "ability-targeting-test");
            TestDefinitionFactory.SetPrivateField(ability, "costs", new List<AbilityCost>());
            TestDefinitionFactory.SetPrivateField(ability, "effects", new List<AbilityEffect>());

            var targeting = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(targeting, "minTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "maxTargets", 1);
            TestDefinitionFactory.SetPrivateField(targeting, "relation", relation);
            TestDefinitionFactory.SetPrivateField(targeting, "range", range);
            TestDefinitionFactory.SetPrivateField(targeting, "requireLineOfSight", requireLineOfSight);
            TestDefinitionFactory.SetPrivateField(ability, "targeting", targeting);

            return ability;
        }

        private static (CharacterRegistry Registry, RuntimeEntityId Caster, RuntimeEntityId Target) CreateCasterAndTarget()
        {
            var registry = new CharacterRegistry();
            var caster = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(caster);
            var target = new CharacterState(RuntimeEntityId.NewId());
            registry.Register(target);
            return (registry, caster.Id, target.Id);
        }

        // --- Relation ---

        [Test]
        public void ValidateRejectsAnEnemyOnlyAbilityAimedAtAnAlly()
        {
            var (registry, caster, target) = CreateCasterAndTarget();
            var factions = new FakeFactionProvider();
            factions.Set(caster, new DefinitionId("faction-heroes"));
            factions.Set(target, new DefinitionId("faction-heroes")); // same faction -- an ally

            var ability = CreateAbility(relation: TargetRelation.Enemy);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), factionProvider: factions);

            var result = service.Validate(caster, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.WrongRelation, result.Reason);
        }

        [Test]
        public void ValidateRejectsAnAllyOnlyAbilityAimedAtAnEnemy()
        {
            var (registry, caster, target) = CreateCasterAndTarget();
            var factions = new FakeFactionProvider();
            factions.Set(caster, new DefinitionId("faction-heroes"));
            factions.Set(target, new DefinitionId("faction-goblins")); // different faction -- an enemy

            var ability = CreateAbility(relation: TargetRelation.Ally);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), factionProvider: factions);

            var result = service.Validate(caster, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.WrongRelation, result.Reason);
        }

        [Test]
        public void ValidateAcceptsAnEnemyOnlyAbilityAimedAtARealEnemy()
        {
            var (registry, caster, target) = CreateCasterAndTarget();
            var factions = new FakeFactionProvider();
            factions.Set(caster, new DefinitionId("faction-heroes"));
            factions.Set(target, new DefinitionId("faction-goblins"));

            var ability = CreateAbility(relation: TargetRelation.Enemy);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), factionProvider: factions);

            Assert.IsTrue(service.Validate(caster, ability, new[] { target }).IsValid);
        }

        // --- Range ---

        [Test]
        public void ValidateRejectsATargetBeyondRange()
        {
            var (registry, caster, target) = CreateCasterAndTarget();
            var positions = new FakePositionProvider();
            positions.Set(caster, Vector3.zero);
            positions.Set(target, new Vector3(100f, 0f, 0f)); // far beyond the default 5-unit range

            var ability = CreateAbility(range: 5f);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), positions: positions);

            var result = service.Validate(caster, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.OutOfRange, result.Reason);
        }

        [Test]
        public void ValidateAcceptsATargetWithinRange()
        {
            var (registry, caster, target) = CreateCasterAndTarget();
            var positions = new FakePositionProvider();
            positions.Set(caster, Vector3.zero);
            positions.Set(target, new Vector3(3f, 0f, 0f));

            var ability = CreateAbility(range: 5f);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), positions: positions);

            Assert.IsTrue(service.Validate(caster, ability, new[] { target }).IsValid);
        }

        // --- Line of sight ---

        [Test]
        public void ValidateRejectsATargetWithNoLineOfSight()
        {
            var (registry, caster, target) = CreateCasterAndTarget();
            var positions = new FakePositionProvider();
            positions.Set(caster, Vector3.zero);
            positions.Set(target, new Vector3(3f, 0f, 0f));
            var los = new FakeLineOfSightProvider { AlwaysBlocked = true };

            var ability = CreateAbility(requireLineOfSight: true);
            var service = new AbilityResolutionService(
                registry, new DiceService(new SeededRandomSource(1)), positions: positions, lineOfSightProvider: los);

            var result = service.Validate(caster, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.NoLineOfSight, result.Reason);
        }

        // --- Provider degradation: not wired at all vs. wired-but-can't-resolve ---

        [Test]
        public void ValidateSkipsRelationRangeAndLosChecksEntirelyWhenNoProvidersAreWired()
        {
            var (registry, caster, target) = CreateCasterAndTarget();
            // Enemy relation, tight range, LOS required -- but NO factionProvider/positions/lineOfSightProvider
            // wired at all. An otherwise-valid ability must still validate Ok(): unwired is a deliberate skip,
            // not an accidental pass.
            var ability = CreateAbility(relation: TargetRelation.Enemy, range: 0.001f, requireLineOfSight: true);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)));

            Assert.IsTrue(service.Validate(caster, ability, new[] { target }).IsValid);
        }

        [Test]
        public void ValidateRejectsWhenFactionProviderIsWiredButCannotResolveTheTargetsFaction()
        {
            var (registry, caster, target) = CreateCasterAndTarget();
            var factions = new FakeFactionProvider();
            factions.Set(caster, new DefinitionId("faction-heroes"));
            // target's faction deliberately never set -- TryGetFaction returns false for it.

            var ability = CreateAbility(relation: TargetRelation.Enemy);
            var service = new AbilityResolutionService(registry, new DiceService(new SeededRandomSource(1)), factionProvider: factions);

            var result = service.Validate(caster, ability, new[] { target });

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(AbilityRejectionReason.WrongRelation, result.Reason); // never a silent pass
        }
    }
}
