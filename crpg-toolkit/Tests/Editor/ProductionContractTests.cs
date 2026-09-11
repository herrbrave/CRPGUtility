using System.Linq;
using System.Reflection;
using CRPG.Inventory;
using CRPG.Quests;
using CRPG.Saving;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// Task 0.2 (Production Foundation): contract/tripwire tests for gaps recorded in
    /// Docs/Design/State-Persistence-Matrix.md that don't fit as an extension of an existing test fixture --
    /// either because the API to exercise doesn't exist yet (Store/Loot have no SaveService parameter at all
    /// to call) or because the gap is about a field's absence on a definition type, not a round-trip.
    /// <para>These tests deliberately PASS today. Each documents "this doesn't exist yet" as a reflection
    /// check over the public shape of a type, so that the moment a future task adds the missing field, the
    /// assertion flips to failing -- forcing whoever lands that task to consciously delete/rewrite this test
    /// (and ideally replace it with a real round-trip test like SaveServiceTests') rather than silently
    /// leaving a stale tripwire behind. Do not "fix" a newly-failing test here by loosening the assertion --
    /// that failure means the gap it tracked was actually closed, which is the intended outcome.</para>
    /// </summary>
    public class ProductionContractTests
    {
        [Test]
        public void SaveDataHasNoStoreOrLootPersistenceYet()
        {
            var fieldNames = typeof(SaveData).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToList();

            Assert.IsFalse(fieldNames.Any(n => n.Contains("Store")),
                "SaveData now has a Store-related field -- StoreInventoryState persistence has been added. " +
                "Replace this tripwire with a real round-trip test (see SaveServiceTests' pattern) and remove this assertion.");
            Assert.IsFalse(fieldNames.Any(n => n.Contains("Loot")),
                "SaveData now has a Loot-related field -- LootContainerState persistence has been added. " +
                "Replace this tripwire with a real round-trip test (see SaveServiceTests' pattern) and remove this assertion.");
        }

        [Test]
        public void StoreInventoryStateAndLootContainerStateAreNeverReferencedFromSaveService()
        {
            var saveServiceMethods = typeof(SaveService).GetMethods(BindingFlags.Public | BindingFlags.Static);

            var referencesStoreOrLoot = saveServiceMethods.Any(m =>
                m.GetParameters().Any(p => p.ParameterType == typeof(StoreInventoryState) || p.ParameterType == typeof(LootContainerState)));

            Assert.IsFalse(referencesStoreOrLoot,
                "SaveService.Capture/Restore now takes a StoreInventoryState or LootContainerState parameter -- " +
                "world/store persistence has been wired up. Replace this tripwire with a real round-trip test and remove this assertion.");
        }

        [Test]
        public void QuestObjectiveStateHasNoActivationCursorOrRewardClaimFieldYet()
        {
            // Docs/Design/State-Persistence-Matrix.md: Task 3.4 needs a since-activation cursor and a
            // persisted reward-claim marker on the quest objective/quest state before its save-schema and
            // "reload cannot duplicate reward" acceptance criteria are even representable. Confirms today's
            // actual field set directly from the type rather than trusting the design doc's claim.
            var objectiveProperties = typeof(QuestObjectiveState).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
            CollectionAssert.AreEquivalent(new[] { "ObjectiveId", "CurrentCount", "IsComplete" }, objectiveProperties,
                "QuestObjectiveState's field set changed -- if an activation cursor and/or reward-claim marker were added, " +
                "replace this tripwire with a real save round-trip test for them and remove this assertion.");

            var questProperties = typeof(QuestState).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
            Assert.IsFalse(questProperties.Any(n => n.Contains("Claim") || n.Contains("Reward")),
                "QuestState now has a reward-claim-related member -- replace this tripwire with a real test covering " +
                "'reload cannot duplicate reward' and remove this assertion.");
        }
    }
}
