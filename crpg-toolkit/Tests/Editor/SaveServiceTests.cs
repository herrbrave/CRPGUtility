using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Quests;
using CRPG.Rules;
using CRPG.Saving;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SaveServiceTests
    {
        private static DefinitionId StrId => new("stat-strength");
        private static DefinitionId HpId => new("resource-hp");
        private static DefinitionId ProneId => new("condition-prone");
        private static DefinitionId FeatureId => new("feature-second-wind");
        private static DefinitionId ProficiencyId => new("skill-athletics");
        private static DefinitionId SwordId => new("item-longsword");
        private static DefinitionId SlotId => new("slot-mainhand");

        private static (PartyState party, CharacterRegistry characters, PartyInventoryState inventory, Timeline timeline, QuestLog log, RuntimeEntityId fighterId)
            BuildLiveState()
        {
            var fighterId = RuntimeEntityId.NewId();
            var fighter = new CharacterState(fighterId);
            fighter.Stats.SetScore(StrId, 16);
            fighter.AddResource(new ResourceState(HpId, max: 28, current: 19));
            fighter.ApplyCondition(new ConditionInstance(ProneId, RuntimeEntityId.NewId(), fighterId, remainingDuration: 2, stackCount: 1));
            fighter.GrantFeature(FeatureId);
            fighter.GrantProficiency(ProficiencyId);

            var characters = new CharacterRegistry();
            characters.Register(fighter);

            var party = new PartyState(new[] { fighterId });
            party.SetGroup(fighterId, "Vanguard");

            var inventory = new PartyInventoryState();
            var sword = new ItemInstance(RuntimeEntityId.NewId(), SwordId, quantity: 1, charges: null);
            sword.SetCustomState("enchantment", "+1");
            inventory.AddItem(sword, ItemLocation.Equipped(fighterId, SlotId));
            var potion = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-potion"), quantity: 3);
            inventory.AddItem(potion);

            var timeline = new Timeline();
            timeline.Record("met-grimjaw", TimelineValue.Flag);
            timeline.Record("guild-reputation", TimelineValue.OfNumber(5), amount: 5);
            timeline.Record("coronation", TimelineValue.Flag);

            var rulesDatabase = new RulesDatabase();
            var quest = TestDefinitionFactory.CreateQuestDefinition(
                "quest-elder", "Speak With the Elder",
                TestDefinitionFactory.CreateQuestObjective("talk", "Talk to the Elder", QuestObjectiveType.TalkTo, "met-grimjaw", 1));
            rulesDatabase.Register(quest);
            var log = new QuestLog();
            var questService = new QuestService(log, timeline);
            questService.StartQuest(quest);
            questService.RefreshProgress(quest);

            return (party, characters, inventory, timeline, log, fighterId);
        }

        [Test]
        public void RestoringCapturedPartyStateReproducesMembershipSelectionAndGroups()
        {
            var (party, characters, inventory, timeline, log, fighterId) = BuildLiveState();
            var data = SaveService.Capture(party, characters, inventory, timeline, log);

            var newParty = new PartyState();
            SaveService.Restore(data, newParty, new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog());

            Assert.IsTrue(newParty.IsMember(fighterId));
            Assert.IsTrue(newParty.IsSelected(fighterId));
            Assert.AreEqual(fighterId, newParty.ActiveCharacter);
            Assert.AreEqual("Vanguard", newParty.GetGroup(fighterId));
        }

        [Test]
        public void RestoringCapturedCharacterReproducesStatsResourcesConditionsFeaturesAndProficiencies()
        {
            var (party, characters, inventory, timeline, log, fighterId) = BuildLiveState();
            var data = SaveService.Capture(party, characters, inventory, timeline, log);

            var newCharacters = new CharacterRegistry();
            SaveService.Restore(data, new PartyState(), newCharacters, new PartyInventoryState(), new Timeline(), new QuestLog());

            Assert.IsTrue(newCharacters.TryGetCharacter(fighterId, out var restored));
            Assert.AreEqual(16, restored.Stats.GetScore(StrId));
            Assert.IsTrue(restored.TryGetResource(HpId, out var hp));
            Assert.AreEqual(28, hp.Max);
            Assert.AreEqual(19, hp.Current);
            Assert.AreEqual(1, restored.Conditions.Count);
            Assert.AreEqual(2, restored.Conditions[0].RemainingDuration);
            Assert.IsTrue(restored.HasFeature(FeatureId));
            Assert.IsTrue(restored.HasProficiency(ProficiencyId));
        }

        [Test]
        public void RestoringCapturedInventoryReproducesSharedAndEquippedItemsWithCustomState()
        {
            var (party, characters, inventory, timeline, log, fighterId) = BuildLiveState();
            var data = SaveService.Capture(party, characters, inventory, timeline, log);

            var newInventory = new PartyInventoryState();
            SaveService.Restore(data, new PartyState(), new CharacterRegistry(), newInventory, new Timeline(), new QuestLog());

            var sword = newInventory.AllItems.Single(i => i.DefinitionId == SwordId);
            Assert.IsTrue(newInventory.GetLocation(sword.InstanceId).IsEquipped);
            Assert.AreEqual(fighterId, newInventory.GetLocation(sword.InstanceId).Character);
            Assert.IsTrue(sword.TryGetCustomState("enchantment", out var enchantment));
            Assert.AreEqual("+1", enchantment);

            var potion = newInventory.AllItems.Single(i => i.DefinitionId == new DefinitionId("item-potion"));
            Assert.AreEqual(3, potion.Quantity);
            Assert.IsFalse(newInventory.GetLocation(potion.InstanceId).IsEquipped);
        }

        [Test]
        public void RestoringCapturedTimelineReproducesAllQuerySurfaceResults()
        {
            var (party, characters, inventory, timeline, log, _) = BuildLiveState();
            var data = SaveService.Capture(party, characters, inventory, timeline, log);

            var newTimeline = new Timeline();
            SaveService.Restore(data, new PartyState(), new CharacterRegistry(), new PartyInventoryState(), newTimeline, new QuestLog());

            Assert.IsTrue(newTimeline.HasOccurred("met-grimjaw"));
            Assert.AreEqual(5, newTimeline.Sum("guild-reputation"));
            Assert.AreEqual(1, newTimeline.CountOccurrences("met-grimjaw"));
            Assert.IsTrue(newTimeline.OccurredBefore("met-grimjaw", "coronation"));
        }

        [Test]
        public void RestoringCapturedQuestsReproducesStatusAndObjectiveProgress()
        {
            var (party, characters, inventory, timeline, log, _) = BuildLiveState();
            var data = SaveService.Capture(party, characters, inventory, timeline, log);

            var newLog = new QuestLog();
            SaveService.Restore(data, new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), newLog);

            Assert.IsTrue(newLog.TryGetQuest(new DefinitionId("quest-elder"), out var quest));
            Assert.AreEqual(QuestStatus.Completed, quest.Status);
            Assert.IsTrue(quest.TryGetObjective("talk", out var objective));
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void ToJsonThenFromJsonRoundTripsThroughTextWithoutLosingData()
        {
            var (party, characters, inventory, timeline, log, fighterId) = BuildLiveState();
            var data = SaveService.Capture(party, characters, inventory, timeline, log);

            var json = SaveService.ToJson(data);
            var reloaded = SaveService.FromJson(json);

            var newCharacters = new CharacterRegistry();
            var newTimeline = new Timeline();
            SaveService.Restore(reloaded, new PartyState(), newCharacters, new PartyInventoryState(), newTimeline, new QuestLog());

            Assert.IsTrue(newCharacters.TryGetCharacter(fighterId, out var restored));
            Assert.AreEqual(16, restored.Stats.GetScore(StrId));
            Assert.AreEqual(5, newTimeline.Sum("guild-reputation"));
        }

        [Test]
        public void CapturedHeaderCarriesTheCurrentFormatVersion()
        {
            var (party, characters, inventory, timeline, log, _) = BuildLiveState();

            var data = SaveService.Capture(party, characters, inventory, timeline, log);

            Assert.AreEqual(SaveData.CurrentFormatVersion, data.Header.FormatVersion);
            Assert.IsNotEmpty(data.Header.SavedAtUtc);
        }
    }
}
