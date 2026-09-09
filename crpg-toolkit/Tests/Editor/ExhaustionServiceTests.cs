using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class ExhaustionServiceTests
    {
        private static readonly DefinitionId Exhaustion = new("condition-exhaustion");

        [Test]
        public void IncreaseExhaustionStartsAtTheGivenAmount()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());

            ExhaustionService.IncreaseExhaustion(character, Exhaustion);

            Assert.AreEqual(1, ExhaustionService.GetLevel(character, Exhaustion));
        }

        [Test]
        public void IncreaseExhaustionStacksOnTheSameConditionInstance()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());

            ExhaustionService.IncreaseExhaustion(character, Exhaustion);
            ExhaustionService.IncreaseExhaustion(character, Exhaustion);

            Assert.AreEqual(2, ExhaustionService.GetLevel(character, Exhaustion));
            Assert.AreEqual(1, character.Conditions.Count); // one instance, stacked -- not two separate conditions
        }

        [Test]
        public void IncreaseExhaustionCapsAtSixAndReportsDeath()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());

            var died = ExhaustionService.IncreaseExhaustion(character, Exhaustion, amount: 10);

            Assert.IsTrue(died);
            Assert.AreEqual(6, ExhaustionService.GetLevel(character, Exhaustion));
        }

        [Test]
        public void IncreaseExhaustionBelowSixDoesNotReportDeath()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());

            var died = ExhaustionService.IncreaseExhaustion(character, Exhaustion, amount: 3);

            Assert.IsFalse(died);
        }

        [Test]
        public void DecreaseExhaustionReducesTheStack()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            ExhaustionService.IncreaseExhaustion(character, Exhaustion, amount: 3);

            ExhaustionService.DecreaseExhaustion(character, Exhaustion);

            Assert.AreEqual(2, ExhaustionService.GetLevel(character, Exhaustion));
        }

        [Test]
        public void DecreaseExhaustionToZeroRemovesTheCondition()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            ExhaustionService.IncreaseExhaustion(character, Exhaustion, amount: 1);

            ExhaustionService.DecreaseExhaustion(character, Exhaustion);

            Assert.AreEqual(0, ExhaustionService.GetLevel(character, Exhaustion));
            Assert.AreEqual(0, character.Conditions.Count);
        }

        [Test]
        public void GetLevelIsZeroWhenNeverExhausted()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());

            Assert.AreEqual(0, ExhaustionService.GetLevel(character, Exhaustion));
        }
    }
}
