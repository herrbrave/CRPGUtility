using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class EncumbranceServiceTests
    {
        private static readonly DefinitionId Strength = new("stat-strength");
        private static readonly DefinitionId Encumbered = new("condition-encumbered");
        private static readonly DefinitionId HeavilyEncumbered = new("condition-heavily-encumbered");

        [Test]
        public void CarryingCapacityIsStrengthTimesFifteen()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 14);

            Assert.AreEqual(210f, EncumbranceService.ComputeCarryingCapacity(character, Strength));
        }

        [Test]
        public void TierIsUnencumberedBelowFiveTimesStrength()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 10); // thresholds: 50 / 100

            Assert.AreEqual(EncumbranceTier.Unencumbered, EncumbranceService.ComputeTier(character, Strength, 50f));
        }

        [Test]
        public void TierIsEncumberedAboveFiveTimesStrength()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 10);

            Assert.AreEqual(EncumbranceTier.Encumbered, EncumbranceService.ComputeTier(character, Strength, 51f));
        }

        [Test]
        public void TierIsHeavilyEncumberedAboveTenTimesStrength()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 10);

            Assert.AreEqual(EncumbranceTier.HeavilyEncumbered, EncumbranceService.ComputeTier(character, Strength, 101f));
        }

        [Test]
        public void RefreshAppliesTheEncumberedConditionAtTheEncumberedTier()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 10);

            EncumbranceService.Refresh(character, Strength, totalWeight: 51f, Encumbered, HeavilyEncumbered);

            Assert.IsTrue(character.HasCondition(Encumbered));
            Assert.IsFalse(character.HasCondition(HeavilyEncumbered));
        }

        [Test]
        public void RefreshSwapsToHeavilyEncumberedAsWeightIncreases()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 10);
            EncumbranceService.Refresh(character, Strength, totalWeight: 51f, Encumbered, HeavilyEncumbered);

            EncumbranceService.Refresh(character, Strength, totalWeight: 101f, Encumbered, HeavilyEncumbered);

            Assert.IsFalse(character.HasCondition(Encumbered));
            Assert.IsTrue(character.HasCondition(HeavilyEncumbered));
        }

        [Test]
        public void RefreshRemovesBothConditionsOnceUnencumberedAgain()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            character.Stats.SetScore(Strength, 10);
            EncumbranceService.Refresh(character, Strength, totalWeight: 101f, Encumbered, HeavilyEncumbered);

            EncumbranceService.Refresh(character, Strength, totalWeight: 10f, Encumbered, HeavilyEncumbered);

            Assert.IsFalse(character.HasCondition(Encumbered));
            Assert.IsFalse(character.HasCondition(HeavilyEncumbered));
        }
    }
}
