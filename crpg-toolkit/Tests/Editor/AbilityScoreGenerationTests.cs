using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules.SRD5;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class AbilityScoreGenerationTests
    {
        private static List<StatDefinition> CreateSixStats() => new()
        {
            TestDefinitionFactory.CreateStatDefinition("stat-str", "Strength"),
            TestDefinitionFactory.CreateStatDefinition("stat-dex", "Dexterity"),
            TestDefinitionFactory.CreateStatDefinition("stat-con", "Constitution"),
            TestDefinitionFactory.CreateStatDefinition("stat-int", "Intelligence"),
            TestDefinitionFactory.CreateStatDefinition("stat-wis", "Wisdom"),
            TestDefinitionFactory.CreateStatDefinition("stat-cha", "Charisma"),
        };

        [Test]
        public void StandardArrayAssignsValuesInStatOrder()
        {
            var stats = CreateSixStats();
            var method = new StandardArrayGenerationMethod();

            var block = method.Generate(stats);

            Assert.AreEqual(15, block.GetScore(stats[0].Id));
            Assert.AreEqual(14, block.GetScore(stats[1].Id));
            Assert.AreEqual(8, block.GetScore(stats[5].Id));
        }

        [Test]
        public void RandomRollsDropsTheLowestOfFourD6()
        {
            var stats = new List<StatDefinition> { TestDefinitionFactory.CreateStatDefinition("stat-str", "Strength") };
            var random = new TestRandomSource();
            random.EnqueueResults(6, 5, 2, 4); // sorted: 2,4,5,6 -> drop 2 -> 4+5+6=15
            var method = new RandomRollsGenerationMethod(random);

            var block = method.Generate(stats);

            Assert.AreEqual(15, block.GetScore(stats[0].Id));
        }

        [Test]
        public void ManualEntryCopiesGivenScoresVerbatim()
        {
            var stat = TestDefinitionFactory.CreateStatDefinition("stat-str", "Strength");
            var method = new ManualEntryGenerationMethod(new Dictionary<DefinitionId, int> { [stat.Id] = 18 });

            var block = method.Generate(new List<StatDefinition> { stat });

            Assert.AreEqual(18, block.GetScore(stat.Id));
        }

        [Test]
        public void PointBuyAcceptsAValidAllocationWithinBudget()
        {
            var stats = CreateSixStats();
            var desired = new Dictionary<DefinitionId, int>
            {
                [stats[0].Id] = 15, [stats[1].Id] = 14, [stats[2].Id] = 13,
                [stats[3].Id] = 12, [stats[4].Id] = 10, [stats[5].Id] = 8,
            }; // cost: 9+7+5+4+2+0 = 27

            var method = new PointBuyGenerationMethod(desired);
            var block = method.Generate(stats);

            Assert.AreEqual(15, block.GetScore(stats[0].Id));
        }

        [Test]
        public void PointBuyThrowsWhenAllocationExceedsBudget()
        {
            var stats = CreateSixStats();
            var desired = new Dictionary<DefinitionId, int>
            {
                [stats[0].Id] = 15, [stats[1].Id] = 15, [stats[2].Id] = 15,
                [stats[3].Id] = 8, [stats[4].Id] = 8, [stats[5].Id] = 8,
            }; // cost: 9+9+9 = 27, plus three more 0s = 27 -- bump one to force overflow

            desired[stats[3].Id] = 9; // +1 cost -> 28, over budget

            var method = new PointBuyGenerationMethod(desired);

            Assert.Throws<InvalidOperationException>(() => method.Generate(stats));
        }
    }
}
