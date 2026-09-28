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
        private static DefinitionId WisId => new("stat-wisdom");
        private static DefinitionId ConcentrationAbilityId => new("ability-bless");
        private static DefinitionId KnownAbilityId => new("ability-firebolt");
        private static DefinitionId PreparedAbilityId => new("ability-cure-wounds");

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

            // Fields CaptureCharacters/RestoreCharacters do not round-trip yet -- see
            // Docs/Design/State-Persistence-Matrix.md. Set here so the gap is visible against the same live
            // fixture every other SaveServiceTests case already shares, not a bespoke one-off setup.
            fighter.Level = 5;
            fighter.SpellcastingStatId = WisId;
            fighter.SetConcentrationEffect(ConcentrationAbilityId, EffectOwnerId.NewOwner(), new[] { fighterId });
            fighter.LearnAbility(KnownAbilityId);
            fighter.PrepareSpells(new[] { PreparedAbilityId });
            fighter.DeathSaves = new DeathSaveState { Successes = 1, Failures = 2 };

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
        public void RestoringCapturedQuestsReproducesActivationCursorAndRewardClaimed()
        {
            var (party, characters, inventory, timeline, log, _) = BuildLiveState();
            var data = SaveService.Capture(party, characters, inventory, timeline, log);

            var newLog = new QuestLog();
            SaveService.Restore(data, new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), newLog);

            Assert.IsTrue(newLog.TryGetQuest(new DefinitionId("quest-elder"), out var quest));
            Assert.IsTrue(quest.RewardClaimed, "BuildLiveState's quest completes via RefreshProgress, which claims the reward step even with no authored Rewards.");
            Assert.IsTrue(quest.TryGetObjective("talk", out var objective));
            Assert.AreEqual(3, objective.ActivationCursor, "BuildLiveState records 3 Timeline events (met-grimjaw, guild-reputation, coronation) before starting the quest.");
        }

        [Test]
        public void CapturingWithAdditionalCharacterIdsPersistsANonPartyCharacter()
        {
            var fighterId = RuntimeEntityId.NewId();
            var characters = new CharacterRegistry();
            characters.Register(new CharacterState(fighterId));
            var party = new PartyState(new[] { fighterId });

            var npcId = RuntimeEntityId.NewId();
            var npc = new CharacterState(npcId) { Level = 3 };
            characters.Register(npc);

            var data = SaveService.Capture(party, characters, new PartyInventoryState(), new Timeline(), new QuestLog(), additionalCharacterIds: new[] { npcId });

            Assert.AreEqual(2, data.Characters.Count, "Capture without additionalCharacterIds would only ever see the party member.");

            var newCharacters = new CharacterRegistry();
            SaveService.Restore(data, new PartyState(), newCharacters, new PartyInventoryState(), new Timeline(), new QuestLog());

            Assert.IsTrue(newCharacters.TryGetCharacter(npcId, out var restoredNpc));
            Assert.AreEqual(3, restoredNpc.Level);
        }

        [Test]
        public void StoreStateRoundTripsThroughCaptureAndRestore()
        {
            var store = new StoreInventoryState(buyPriceMultiplier: 1.5f, sellPriceMultiplier: 0.4f);
            store.AddListing(new DefinitionId("item-potion"), 5);
            var stores = new System.Collections.Generic.Dictionary<string, StoreInventoryState> { ["griff-store"] = store };

            var data = SaveService.Capture(new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog(), stores: stores);

            Assert.AreEqual(1, data.Stores.Count);
            Assert.AreEqual("griff-store", data.Stores[0].StoreId);
            Assert.AreEqual(1.5f, data.Stores[0].BuyPriceMultiplier);

            var newStore = new StoreInventoryState();
            SaveService.RestoreStores(data, new System.Collections.Generic.Dictionary<string, StoreInventoryState> { ["griff-store"] = newStore });

            Assert.AreEqual(1.5f, newStore.BuyPriceMultiplier);
            Assert.AreEqual(0.4f, newStore.SellPriceMultiplier);
            Assert.IsTrue(newStore.TryGetListing(new DefinitionId("item-potion"), out var listing));
            Assert.AreEqual(5, listing.Quantity);
        }

        // Task 6.0: found live -- a real game's composition root re-preps its scene's ILocationContent on load
        // (LocationInitializer.RebindStoresAndLoot), which always calls GetOrCreateStore(seedIfNew:...) against
        // the brand-new session's own empty stores dictionary; a same StoreId is therefore always "new" to it
        // and always gets re-seeded from InitialListings BEFORE RestoreStores ever runs. Without RestoreStores
        // clearing first, TryGetListing kept returning the stale reseeded quantity, not the restored one --
        // confirmed via a real depleted-stock-survives-a-load test before this was traced to its root cause here.
        [Test]
        public void RestoreStoresReplacesAnAlreadySeededStoresListingsRatherThanLayeringOnTopOfThem()
        {
            var store = new StoreInventoryState();
            store.AddListing(new DefinitionId("item-potion"), 3); // depleted from an initial seed of 5, then saved
            var stores = new System.Collections.Generic.Dictionary<string, StoreInventoryState> { ["griff-store"] = store };
            var data = SaveService.Capture(new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog(), stores: stores);

            // Simulates GetOrCreateStore(seedIfNew:...) already having re-seeded a "new" instance before restore runs.
            var freshlySeededStore = new StoreInventoryState();
            freshlySeededStore.AddListing(new DefinitionId("item-potion"), 5);

            SaveService.RestoreStores(data, new System.Collections.Generic.Dictionary<string, StoreInventoryState> { ["griff-store"] = freshlySeededStore });

            Assert.IsTrue(freshlySeededStore.TryGetListing(new DefinitionId("item-potion"), out var listing));
            Assert.AreEqual(3, listing.Quantity, "the SAVED (depleted) quantity, not the freshly re-seeded one");
            Assert.AreEqual(1, freshlySeededStore.Listings.Count, "no duplicate listing left behind for the same item");
        }

        [Test]
        public void LootContainerStateRoundTripsThroughCaptureAndRestore()
        {
            var container = new LootContainerState();
            var item = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-gem"), quantity: 2);
            container.AddItem(item);
            var lootContainers = new System.Collections.Generic.Dictionary<string, LootContainerState> { ["chest-1"] = container };

            var data = SaveService.Capture(new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog(), lootContainers: lootContainers);

            Assert.AreEqual(1, data.LootContainers.Count);
            Assert.AreEqual(1, data.LootContainers[0].Items.Count);

            var newContainer = new LootContainerState();
            SaveService.RestoreLootContainers(data, new System.Collections.Generic.Dictionary<string, LootContainerState> { ["chest-1"] = newContainer });

            Assert.AreEqual(1, newContainer.Items.Count);
            Assert.AreEqual(2, newContainer.Items[0].Quantity);
        }

        // Task 6.0: same class of bug as the store one above -- GetOrCreateLootContainer(seedIfNew:...) always
        // re-seeds InitialItems into a "new" (to this session) container before restore runs.
        [Test]
        public void RestoreLootContainersReplacesAnAlreadySeededContainersItemsRatherThanLayeringOnTopOfThem()
        {
            var container = new LootContainerState();
            var savedItem = new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-gem"), quantity: 1); // looted down from 2, then saved
            container.AddItem(savedItem);
            var lootContainers = new System.Collections.Generic.Dictionary<string, LootContainerState> { ["chest-1"] = container };
            var data = SaveService.Capture(new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog(), lootContainers: lootContainers);

            // Simulates GetOrCreateLootContainer(seedIfNew:...) already having re-seeded a "new" instance before restore runs.
            var freshlySeededContainer = new LootContainerState();
            freshlySeededContainer.AddItem(new ItemInstance(RuntimeEntityId.NewId(), new DefinitionId("item-gem"), quantity: 2));

            SaveService.RestoreLootContainers(data, new System.Collections.Generic.Dictionary<string, LootContainerState> { ["chest-1"] = freshlySeededContainer });

            Assert.AreEqual(1, freshlySeededContainer.Items.Count, "no leftover freshly-seeded item alongside the restored one");
            Assert.AreEqual(1, freshlySeededContainer.Items[0].Quantity, "the SAVED (looted-down) quantity, not the freshly re-seeded one");
        }

        [Test]
        public void WorldSpawnSnapshotsRoundTripIncludingADefeatedEntry()
        {
            var alive = new WorldSpawnSnapshot("spawn-goblin", RuntimeEntityId.NewId().ToString(), "npc-goblin", new UnityEngine.Vector3(1, 2, 3), 90f, defeated: false);
            var defeated = new WorldSpawnSnapshot("spawn-slain", "", "npc-slain-goblin", UnityEngine.Vector3.zero, 0f, defeated: true);

            var data = SaveService.Capture(new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog(), worldSpawns: new[] { alive, defeated });

            Assert.AreEqual(2, data.WorldSpawns.Count);
            var aliveEntry = data.WorldSpawns.Single(e => e.SpawnId == "spawn-goblin");
            Assert.IsFalse(aliveEntry.Defeated);
            Assert.AreEqual(1f, aliveEntry.PosX);
            Assert.AreEqual(90f, aliveEntry.FacingYaw);

            var defeatedEntry = data.WorldSpawns.Single(e => e.SpawnId == "spawn-slain");
            Assert.IsTrue(defeatedEntry.Defeated);
        }

        [Test]
        public void SaveHeaderCapturesScenePathRulesetIdAndContentCompatibilityId()
        {
            var data = SaveService.Capture(new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog(),
                scenePath: "Assets/Scenes/InteractionLab.unity", rulesetId: "srd5-2014", contentCompatibilityId: "content-v1");

            Assert.AreEqual("Assets/Scenes/InteractionLab.unity", data.Header.ScenePath);
            Assert.AreEqual("srd5-2014", data.Header.RulesetId);
            Assert.AreEqual("content-v1", data.Header.ContentCompatibilityId);
            Assert.AreEqual(SaveData.CurrentFormatVersion, data.Header.FormatVersion);
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

        // --- Task 0.2 (Production Foundation): the six tests below document the gap in
        // Docs/Design/State-Persistence-Matrix.md and are EXPECTED TO FAIL until Phase 1 migration adds the
        // six missing CharacterState fields to CharacterSaveData/CaptureCharacters/RestoreCharacters. One
        // field per test (rather than one bundled test) so a single run reports exactly which fields are
        // still missing instead of stopping at the first failure. Do not "fix" by weakening an assertion --
        // fix by extending the save DTO and capture/restore code, then each test should turn green unchanged.
        private static CharacterState RestoreFighter((PartyState party, CharacterRegistry characters, PartyInventoryState inventory, Timeline timeline, QuestLog log, RuntimeEntityId fighterId) live)
        {
            var data = SaveService.Capture(live.party, live.characters, live.inventory, live.timeline, live.log);
            var newCharacters = new CharacterRegistry();
            SaveService.Restore(data, new PartyState(), newCharacters, new PartyInventoryState(), new Timeline(), new QuestLog());
            Assert.IsTrue(newCharacters.TryGetCharacter(live.fighterId, out var restored));
            return restored;
        }

        [Test]
        public void RestoringCapturedCharacterReproducesLevel()
        {
            var restored = RestoreFighter(BuildLiveState());
            Assert.AreEqual(5, restored.Level, "Level does not round-trip -- restored characters revert to the default (1).");
        }

        [Test]
        public void RestoringCapturedCharacterReproducesDeathSaves()
        {
            var restored = RestoreFighter(BuildLiveState());
            Assert.IsNotNull(restored.DeathSaves, "DeathSaves does not round-trip -- a dying character loses its death-save progress on load.");
        }

        [Test]
        public void RestoringCapturedCharacterReproducesSpellcastingStatId()
        {
            var restored = RestoreFighter(BuildLiveState());
            Assert.AreEqual(WisId, restored.SpellcastingStatId, "SpellcastingStatId does not round-trip -- reverts to DefinitionId.None.");
        }

        [Test]
        public void RestoringCapturedCharacterReproducesConcentratingOnAbilityId()
        {
            var restored = RestoreFighter(BuildLiveState());
            Assert.AreEqual(ConcentrationAbilityId, restored.ConcentratingOnAbilityId, "ConcentratingOnAbilityId does not round-trip.");
        }

        [Test]
        public void RestoringCapturedCharacterReproducesConcentrationOwnershipData()
        {
            var live = BuildLiveState();
            live.characters.TryGetCharacter(live.fighterId, out var original);

            var restored = RestoreFighter(live);

            Assert.AreEqual(original.ConcentrationEffectOwnerId, restored.ConcentrationEffectOwnerId,
                "ConcentrationEffectOwnerId does not round-trip -- a later ConcentrationEffectCleanup.Break after reload would have nothing to sweep.");
            CollectionAssert.AreEqual(original.ConcentrationAffectedTargets, restored.ConcentrationAffectedTargets,
                "ConcentrationAffectedTargets does not round-trip.");
        }

        [Test]
        public void RestoringCapturedConditionReproducesOwnershipAndBoundary()
        {
            var restored = RestoreFighter(BuildLiveState());
            var condition = restored.Conditions.Single(c => c.ConditionId == ProneId);

            // BuildLiveState's Prone instance is ownerless/boundary-None by construction (predates Task 4.4) --
            // this proves the new fields round-trip as the correct default, not that they're silently dropped.
            Assert.AreEqual(EffectOwnerId.None, condition.OwnerId);
            Assert.AreEqual(DurationBoundary.None, condition.Boundary);
        }

        [Test]
        public void SaveLoadThenBreakConcentrationRemovesOnlyThatCastsConditionAcrossAReload()
        {
            var casterId = RuntimeEntityId.NewId();
            var targetId = RuntimeEntityId.NewId();
            var caster = new CharacterState(casterId);
            var target = new CharacterState(targetId);

            var ownerId = EffectOwnerId.NewOwner();
            var ownedConditionId = new DefinitionId("condition-slowed");
            var unrelatedConditionId = new DefinitionId("condition-frightened");
            target.ApplyCondition(new ConditionInstance(ownedConditionId, casterId, targetId, ownerId: ownerId));
            target.ApplyCondition(new ConditionInstance(unrelatedConditionId, RuntimeEntityId.NewId(), targetId));
            caster.SetConcentrationEffect(ConcentrationAbilityId, ownerId, new[] { targetId });

            var characters = new CharacterRegistry();
            characters.Register(caster);
            characters.Register(target);
            var party = new PartyState(new[] { casterId });

            // Real save -> real reload, exactly like a genuine session restart: a fresh CharacterRegistry, not
            // the live one, and every value reconstructed purely from SaveData.
            var data = SaveService.Capture(party, characters, new PartyInventoryState(), new Timeline(), new QuestLog(), additionalCharacterIds: new[] { targetId });
            var restoredCharacters = new CharacterRegistry();
            SaveService.Restore(data, new PartyState(), restoredCharacters, new PartyInventoryState(), new Timeline(), new QuestLog());

            restoredCharacters.TryGetCharacter(casterId, out var restoredCaster);
            restoredCharacters.TryGetCharacter(targetId, out var restoredTarget);

            ConcentrationEffectCleanup.Break(casterId, restoredCaster, restoredCharacters, null);

            Assert.IsFalse(restoredTarget.HasCondition(ownedConditionId), "Ownership must survive save/load as a real relationship, not just the flag.");
            Assert.IsTrue(restoredTarget.HasCondition(unrelatedConditionId), "An unrelated condition on the same target must still survive the sweep.");
        }

        [Test]
        public void RestoringCapturedCharacterReproducesKnownAbilities()
        {
            var restored = RestoreFighter(BuildLiveState());
            Assert.IsTrue(restored.KnowsAbility(KnownAbilityId), "KnownAbilities does not round-trip -- a caster forgets every known spell/ability.");
        }

        [Test]
        public void RestoringCapturedCharacterReproducesPreparedAbilities()
        {
            var restored = RestoreFighter(BuildLiveState());
            Assert.IsTrue(restored.HasPrepared(PreparedAbilityId), "PreparedAbilities does not round-trip -- a prepared caster's daily selection is lost.");
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
