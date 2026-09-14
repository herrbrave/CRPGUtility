using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class CharacterStateTests
    {
        [Test]
        public void StatBlockStoresAndRetrievesScores()
        {
            var strength = new DefinitionId("stat-strength");
            var stats = new StatBlock();

            stats.SetScore(strength, 16);

            Assert.AreEqual(16, stats.GetScore(strength));
            Assert.IsTrue(stats.HasScore(strength));
        }

        [Test]
        public void UnsetStatScoreDefaultsToZero()
        {
            var stats = new StatBlock();

            Assert.AreEqual(0, stats.GetScore(new DefinitionId("stat-wisdom")));
        }

        [Test]
        public void ResourceStateSpendAndRestoreClampToBounds()
        {
            var resource = new ResourceState(new DefinitionId("resource-hp"), max: 10, current: 4);

            var restored = resource.Restore(100);
            Assert.AreEqual(6, restored);
            Assert.AreEqual(10, resource.Current);

            var spent = resource.Spend(100);
            Assert.AreEqual(10, spent);
            Assert.AreEqual(0, resource.Current);
        }

        [Test]
        public void CharacterStateTracksConditionsAndProficiencies()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            var poisoned = new DefinitionId("condition-poisoned");
            var stealth = new DefinitionId("skill-stealth");

            character.ApplyCondition(new ConditionInstance(poisoned, source: RuntimeEntityId.NewId(), target: character.Id));
            character.GrantProficiency(stealth);

            Assert.IsTrue(character.HasCondition(poisoned));
            Assert.IsTrue(character.HasProficiency(stealth));
            Assert.AreEqual(1, character.Conditions.Count);
        }

        [Test]
        public void RemovingAConditionStopsItFromBeingActive()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            var prone = new DefinitionId("condition-prone");
            var condition = new ConditionInstance(prone, source: RuntimeEntityId.NewId(), target: character.Id);

            character.ApplyCondition(condition);
            character.RemoveCondition(condition);

            Assert.IsFalse(character.HasCondition(prone));
        }

        [Test]
        public void TwoSourcesApplyingTheSameConditionCoexistIndependently()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            var poisoned = new DefinitionId("condition-poisoned");
            var ownerA = EffectOwnerId.NewOwner();
            var ownerB = EffectOwnerId.NewOwner();

            character.ApplyCondition(new ConditionInstance(poisoned, RuntimeEntityId.NewId(), character.Id, ownerId: ownerA));
            character.ApplyCondition(new ConditionInstance(poisoned, RuntimeEntityId.NewId(), character.Id, ownerId: ownerB));

            Assert.IsTrue(character.HasCondition(poisoned));
            Assert.AreEqual(2, character.Conditions.Count);
        }

        [Test]
        public void RemoveConditionsWithOwnerLeavesAnUnrelatedSourcesInstanceIntact()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            var poisoned = new DefinitionId("condition-poisoned");
            var ownerA = EffectOwnerId.NewOwner();
            var ownerB = EffectOwnerId.NewOwner();
            character.ApplyCondition(new ConditionInstance(poisoned, RuntimeEntityId.NewId(), character.Id, ownerId: ownerA));
            character.ApplyCondition(new ConditionInstance(poisoned, RuntimeEntityId.NewId(), character.Id, ownerId: ownerB));

            var removed = character.RemoveConditionsWithOwner(ownerA);

            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual(1, character.Conditions.Count);
            Assert.AreEqual(ownerB, character.Conditions[0].OwnerId);
        }

        [Test]
        public void RemoveConditionsWithOwnerNeverMatchesAnOwnerlessCondition()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.ApplyCondition(new ConditionInstance(new DefinitionId("condition-prone"), RuntimeEntityId.NewId(), character.Id));

            var removed = character.RemoveConditionsWithOwner(EffectOwnerId.None);

            Assert.AreEqual(0, removed.Count);
            Assert.AreEqual(1, character.Conditions.Count);
        }

        [Test]
        public void RemoveConditionsByPredicateRemovesEveryMatchAndReturnsThem()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            var poisoned = new DefinitionId("condition-poisoned");
            var prone = new DefinitionId("condition-prone");
            character.ApplyCondition(new ConditionInstance(poisoned, RuntimeEntityId.NewId(), character.Id));
            character.ApplyCondition(new ConditionInstance(poisoned, RuntimeEntityId.NewId(), character.Id));
            character.ApplyCondition(new ConditionInstance(prone, RuntimeEntityId.NewId(), character.Id));

            var removed = character.RemoveConditions(c => c.ConditionId == poisoned);

            Assert.AreEqual(2, removed.Count);
            Assert.AreEqual(1, character.Conditions.Count);
            Assert.IsTrue(character.HasCondition(prone));
            Assert.IsFalse(character.HasCondition(poisoned));
        }

        [Test]
        public void SetConcentrationEffectAndClearConcentrationStayConsistent()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            var abilityId = new DefinitionId("ability-bless");
            var ownerId = EffectOwnerId.NewOwner();
            var targetId = RuntimeEntityId.NewId();

            character.SetConcentrationEffect(abilityId, ownerId, new[] { targetId });

            Assert.AreEqual(abilityId, character.ConcentratingOnAbilityId);
            Assert.AreEqual(ownerId, character.ConcentrationEffectOwnerId);
            CollectionAssert.AreEqual(new[] { targetId }, character.ConcentrationAffectedTargets);

            character.ClearConcentration();

            Assert.AreEqual(DefinitionId.None, character.ConcentratingOnAbilityId);
            Assert.AreEqual(EffectOwnerId.None, character.ConcentrationEffectOwnerId);
            Assert.AreEqual(0, character.ConcentrationAffectedTargets.Count);
        }
    }
}
