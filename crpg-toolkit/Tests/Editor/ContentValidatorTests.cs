using CRPG.Core;
using CRPG.Rules;
using CRPG.Saving;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 3.5: "JSON parse success is not save validity" -- ContentValidator is checked before
    /// SessionLoadCoordinator ever builds a live GameSession.</summary>
    public class ContentValidatorTests
    {
        private static SaveData EmptySave() => new();

        [Test]
        public void AnEmptySaveIsValid()
        {
            var rulesDatabase = new RulesDatabase();
            var result = ContentValidator.Validate(EmptySave(), rulesDatabase);
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void AStatIdThatDoesNotResolveIsRejected()
        {
            var data = EmptySave();
            data.Characters.Add(new CharacterSaveData { Id = "char-1", Stats = { new StatScoreSaveEntry { StatId = "stat-nonexistent", Score = 10 } } });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void AResolvableStatIdIsAccepted()
        {
            var rulesDatabase = new RulesDatabase();
            rulesDatabase.Register(TestDefinitionFactory.CreateStatDefinition("stat-strength", "Strength"));
            var data = EmptySave();
            data.Characters.Add(new CharacterSaveData { Id = "char-1", Stats = { new StatScoreSaveEntry { StatId = "stat-strength", Score = 10 } } });

            var result = ContentValidator.Validate(data, rulesDatabase);

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void DefinitionIdNoneIsAcceptedForAnOptionalReference()
        {
            var data = EmptySave();
            data.Characters.Add(new CharacterSaveData { Id = "char-1", SpellcastingStatId = "", ConcentratingOnAbilityId = "" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void ANonEmptySpellcastingStatIdThatDoesNotResolveIsRejected()
        {
            var data = EmptySave();
            data.Characters.Add(new CharacterSaveData { Id = "char-1", SpellcastingStatId = "stat-nonexistent" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void AnUnresolvableItemDefinitionIdIsRejected()
        {
            var data = EmptySave();
            data.InventoryItems.Add(new ItemSaveData { InstanceId = "item-1", DefinitionId = "item-nonexistent" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void AnUnresolvableQuestIdIsRejected()
        {
            var data = EmptySave();
            data.Quests.Add(new QuestSaveData { QuestId = "quest-nonexistent" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void AnUnresolvableNpcDefinitionIdOnAWorldSpawnIsRejected()
        {
            var data = EmptySave();
            data.WorldSpawns.Add(new WorldSpawnSaveEntry { SpawnId = "spawn-1", CharacterId = "char-1", NpcDefinitionId = "npc-nonexistent" });
            data.Characters.Add(new CharacterSaveData { Id = "char-1" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void AnEmptyNpcDefinitionIdOnAWorldSpawnIsAcceptedPartyMemberCase()
        {
            var data = EmptySave();
            data.Characters.Add(new CharacterSaveData { Id = "char-1" });
            data.WorldSpawns.Add(new WorldSpawnSaveEntry { SpawnId = "char-1", CharacterId = "char-1", NpcDefinitionId = "" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void DuplicateCharacterIdsWithinTheSaveAreRejected()
        {
            var data = EmptySave();
            data.Characters.Add(new CharacterSaveData { Id = "char-1" });
            data.Characters.Add(new CharacterSaveData { Id = "char-1" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void DuplicateQuestIdsWithinTheSaveAreRejected()
        {
            var data = EmptySave();
            data.Quests.Add(new QuestSaveData { QuestId = "quest-1" });
            data.Quests.Add(new QuestSaveData { QuestId = "quest-1" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void DuplicateWorldSpawnIdsWithinTheSaveAreRejected()
        {
            var data = EmptySave();
            data.Characters.Add(new CharacterSaveData { Id = "char-1" });
            data.Characters.Add(new CharacterSaveData { Id = "char-2" });
            data.WorldSpawns.Add(new WorldSpawnSaveEntry { SpawnId = "dup", CharacterId = "char-1" });
            data.WorldSpawns.Add(new WorldSpawnSaveEntry { SpawnId = "dup", CharacterId = "char-2" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void ADanglingEquippedCharacterIdIsRejected()
        {
            var rulesDatabase = new RulesDatabase();
            rulesDatabase.Register(TestDefinitionFactory.CreateItemDefinition("item-sword", "Sword"));
            var data = EmptySave();
            data.InventoryItems.Add(new ItemSaveData { InstanceId = "item-1", DefinitionId = "item-sword", EquippedCharacterId = "char-does-not-exist" });

            var result = ContentValidator.Validate(data, rulesDatabase);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void ADanglingWorldSpawnCharacterIdIsRejected()
        {
            var data = EmptySave();
            data.WorldSpawns.Add(new WorldSpawnSaveEntry { SpawnId = "spawn-1", CharacterId = "char-does-not-exist" });

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void AnEquippedCharacterIdThatIsPresentInTheSaveIsAccepted()
        {
            var rulesDatabase = new RulesDatabase();
            rulesDatabase.Register(TestDefinitionFactory.CreateItemDefinition("item-sword", "Sword"));
            var data = EmptySave();
            data.Characters.Add(new CharacterSaveData { Id = "char-1" });
            data.InventoryItems.Add(new ItemSaveData { InstanceId = "item-1", DefinitionId = "item-sword", EquippedCharacterId = "char-1" });

            var result = ContentValidator.Validate(data, rulesDatabase);

            Assert.IsTrue(result.IsValid);
        }
    }
}
