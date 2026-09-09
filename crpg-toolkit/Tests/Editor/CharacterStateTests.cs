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
    }
}
