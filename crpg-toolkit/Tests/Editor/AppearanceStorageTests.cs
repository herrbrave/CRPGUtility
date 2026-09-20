using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Quests;
using CRPG.Rules;
using CRPG.Saving;
using CRPG.Saving.Migrations;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 2.4 (stage A): appearance on CharacterState, the save round trip and the v2 -> v3 migration, NPC
    /// and build-plan sources of appearance.</summary>
    public class AppearanceStorageTests
    {
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Clothes = new("layer-clothes");
        private static readonly DefinitionId HairOption = new("hair-kenney-19-0");
        private static readonly DefinitionId BodyOption = new("body-kenney-0-0");
        private static readonly DefinitionId ClothesOption = new("clothes-kenney-6-0");

        private string tempDir;
        private readonly List<Object> made = new();

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "crpg-appearance-tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }

        private static (PartyState party, CharacterRegistry characters, RuntimeEntityId id) LiveCharacterWithAppearance()
        {
            var id = RuntimeEntityId.NewId();
            var character = new CharacterState(id);
            character.SetAppearance(Hair, HairOption);
            character.SetAppearance(Body, BodyOption);
            character.SetAppearance(Clothes, ClothesOption);
            var characters = new CharacterRegistry();
            characters.Register(character);
            var party = new PartyState();
            party.AddMember(id);
            return (party, characters, id);
        }

        private static SaveData Capture(PartyState party, CharacterRegistry characters) =>
            SaveService.Capture(party, characters, new PartyInventoryState(), new Timeline(), new QuestLog());

        // ---------------------------------------------------------------------------------------------
        // CharacterState
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void AppearanceStartsEmptyAndSetReplacesClearsAndRemoves()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());
            Assert.AreEqual(0, character.Appearance.Count);

            character.SetAppearance(Hair, HairOption);
            character.SetAppearance(Hair, new DefinitionId("hair-kenney-20-0"));
            Assert.AreEqual(new DefinitionId("hair-kenney-20-0"), character.Appearance[Hair], "set replaces");

            character.SetAppearance(Body, BodyOption);
            character.ClearAppearance(Hair);
            Assert.IsFalse(character.Appearance.ContainsKey(Hair));
            Assert.IsTrue(character.Appearance.ContainsKey(Body));

            character.ClearAllAppearance();
            Assert.AreEqual(0, character.Appearance.Count);
        }

        [Test]
        public void AnInvalidSlotOrOptionIsIgnoredWithoutThrowing()
        {
            var character = new CharacterState(RuntimeEntityId.NewId());

            Assert.DoesNotThrow(() =>
            {
                character.SetAppearance(default, HairOption);
                character.SetAppearance(Hair, default);
            });
            Assert.AreEqual(0, character.Appearance.Count);

            character.SetAppearance(Hair, HairOption);
            character.SetAppearance(Hair, default);
            Assert.AreEqual(HairOption, character.Appearance[Hair], "an invalid option never removes; ClearAppearance is the only remover");
        }

        // ---------------------------------------------------------------------------------------------
        // Save round trip
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void AppearanceSurvivesCaptureJsonAndRestore()
        {
            var (party, characters, id) = LiveCharacterWithAppearance();

            var json = SaveService.ToJson(Capture(party, characters));
            var loaded = SaveService.FromJson(json);
            var newCharacters = new CharacterRegistry();
            SaveService.Restore(loaded, new PartyState(), newCharacters, new PartyInventoryState(), new Timeline(), new QuestLog());

            Assert.IsTrue(newCharacters.TryGetCharacter(id, out var restored));
            Assert.AreEqual(3, restored.Appearance.Count);
            Assert.AreEqual(HairOption, restored.Appearance[Hair]);
            Assert.AreEqual(BodyOption, restored.Appearance[Body]);
            Assert.AreEqual(ClothesOption, restored.Appearance[Clothes]);
        }

        [Test]
        public void CapturedAppearanceEntriesAreInSlotOrder()
        {
            var (party, characters, _) = LiveCharacterWithAppearance();

            var entries = Capture(party, characters).Characters[0].Appearance;

            CollectionAssert.AreEqual(new[] { "layer-body", "layer-clothes", "layer-hair" }, entries.Select(e => e.Slot).ToArray());
        }

        [Test]
        public void EmptyAndGarbledAppearanceEntriesAreSkippedOnRestore()
        {
            var (party, characters, id) = LiveCharacterWithAppearance();
            var data = Capture(party, characters);
            data.Characters[0].Appearance.Add(new AppearanceSaveEntry { Slot = "", Option = "x" });
            data.Characters[0].Appearance.Add(new AppearanceSaveEntry { Slot = "layer-feet", Option = null });
            data.Characters[0].Appearance.Add(null);

            var newCharacters = new CharacterRegistry();
            Assert.DoesNotThrow(() => SaveService.Restore(data, new PartyState(), newCharacters, new PartyInventoryState(), new Timeline(), new QuestLog()));

            newCharacters.TryGetCharacter(id, out var restored);
            Assert.AreEqual(3, restored.Appearance.Count);
        }

        [Test]
        public void ARealV2DocumentLackingTheAppearanceFieldRestoresWithAnEmptyMap()
        {
            // Hand-written JSON: a ToJson-produced fixture always emits every field, which would prove nothing.
            const string v2 = "{\"Header\":{\"FormatVersion\":2,\"SavedAtUtc\":\"x\"},\"Characters\":[{\"Id\":\"00000000000000000000000000000001\",\"Level\":4,\"KnownAbilities\":[],\"PreparedAbilities\":[]}]}";
            var data = SaveService.FromJson(v2);

            Assert.IsNotNull(data.Characters[0].Appearance, "the field initializer survives an absent JSON field");
            Assert.AreEqual(0, data.Characters[0].Appearance.Count);
        }

        [Test]
        public void AppearanceSurvivesTheFullSaveFileStorePath()
        {
            var (party, characters, id) = LiveCharacterWithAppearance();
            var path = Path.Combine(tempDir, "save.json");
            var store = new SaveFileStore();

            Assert.IsTrue(store.Save(path, SaveService.ToJson(Capture(party, characters))).Success);
            var result = store.Load(path);

            Assert.AreEqual(SaveLoadOutcome.Success, result.Outcome);
            Assert.AreEqual(3, result.Data.Header.FormatVersion);
            var newCharacters = new CharacterRegistry();
            SaveService.Restore(result.Data, new PartyState(), newCharacters, new PartyInventoryState(), new Timeline(), new QuestLog());
            newCharacters.TryGetCharacter(id, out var restored);
            Assert.AreEqual(HairOption, restored.Appearance[Hair]);
        }

        [Test]
        public void AV2FileMigratesToV3ThroughSaveFileStoreAndLoadsWithAnEmptyAppearance()
        {
            const string v2 = "{\"Header\":{\"FormatVersion\":2,\"SavedAtUtc\":\"x\"},\"Characters\":[{\"Id\":\"00000000000000000000000000000001\",\"Level\":4}]}";
            var path = Path.Combine(tempDir, "v2.json");
            File.WriteAllText(path, v2);

            var result = new SaveFileStore().Load(path);

            Assert.AreEqual(SaveLoadOutcome.Success, result.Outcome);
            Assert.AreEqual(3, result.Data.Header.FormatVersion);
            Assert.AreEqual(4, result.Data.Characters[0].Level);
            Assert.AreEqual(0, result.Data.Characters[0].Appearance.Count);
        }

        [Test]
        public void ContentValidationStillAcceptsASaveWhoseAppearanceOptionsResolveToNothing()
        {
            // Pins the decision: an option id that no longer resolves degrades at the view (empty layer + report); it must
            // never make ContentValidator refuse the whole save.
            var (party, characters, _) = LiveCharacterWithAppearance();
            var data = Capture(party, characters);

            var result = ContentValidator.Validate(data, new RulesDatabase());

            Assert.IsEmpty(result.Errors.Where(e => e.Contains("layer-") || e.Contains("hair-") || e.Contains("body-") || e.Contains("ppearance")));
        }

        // ---------------------------------------------------------------------------------------------
        // V2ToV3Migration
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void TheMigrationOnlyBumpsTheVersionAndPreservesEveryFieldOfAPopulatedDocument()
        {
            var v2 = new SaveData { Header = { FormatVersion = 2, SavedAtUtc = "2026-09-19", ScenePath = "Assets/x.unity", RulesetId = "srd5", ContentCompatibilityId = "c1" } };
            v2.Party.ActiveCharacterId = "00000000000000000000000000000001";
            v2.Characters.Add(new CharacterSaveData { Id = "00000000000000000000000000000001", Level = 3, KnownAbilities = { "ability-a" }, Features = { "feature-b" } });
            v2.InventoryItems.Add(new ItemSaveData { InstanceId = "00000000000000000000000000000002", DefinitionId = "item-c" });
            v2.Quests.Add(new QuestSaveData { QuestId = "quest-d" });
            v2.TimelineEvents.Add(new TimelineEventSaveData());

            var migrated = SaveService.FromJson(new V2ToV3Migration().Migrate(SaveService.ToJson(v2)));

            Assert.AreEqual(3, migrated.Header.FormatVersion);
            Assert.AreEqual("2026-09-19", migrated.Header.SavedAtUtc);
            Assert.AreEqual("Assets/x.unity", migrated.Header.ScenePath);
            Assert.AreEqual("srd5", migrated.Header.RulesetId);
            Assert.AreEqual("c1", migrated.Header.ContentCompatibilityId);
            Assert.AreEqual(v2.Party.ActiveCharacterId, migrated.Party.ActiveCharacterId);
            Assert.AreEqual(1, migrated.Characters.Count);
            Assert.AreEqual(3, migrated.Characters[0].Level, "the level is NOT touched (unlike v1 -> v2)");
            CollectionAssert.AreEqual(new[] { "ability-a" }, migrated.Characters[0].KnownAbilities);
            CollectionAssert.AreEqual(new[] { "feature-b" }, migrated.Characters[0].Features);
            Assert.AreEqual(0, migrated.Characters[0].Appearance.Count);
            Assert.AreEqual("item-c", migrated.InventoryItems[0].DefinitionId);
            Assert.AreEqual("quest-d", migrated.Quests[0].QuestId);
            Assert.AreEqual(1, migrated.TimelineEvents.Count);
        }

        [Test]
        public void TheMigrationDeclaresItselfAsFromVersionTwo() => Assert.AreEqual(2, new V2ToV3Migration().FromVersion);

        [Test]
        public void TheRunnerWalksAV1DocumentAllTheWayToTheCurrentVersion()
        {
            const string v1 = "{\"Header\":{\"FormatVersion\":1,\"SavedAtUtc\":\"x\"},\"Characters\":[{\"Id\":\"00000000000000000000000000000001\"}]}";

            var migrated = SaveService.FromJson(SaveMigrationRunner.Apply(v1, new ISaveMigration[] { new V1ToV2Migration(), new V2ToV3Migration() }));

            Assert.AreEqual(SaveData.CurrentFormatVersion, migrated.Header.FormatVersion);
            Assert.AreEqual(1, migrated.Characters[0].Level, "the v1 -> v2 level fixup still runs on the way");
        }

        [Test]
        public void TheStoresDefaultMigrationsReachTheCurrentVersionAndACurrentDocumentIsLeftAlone()
        {
            var path = Path.Combine(tempDir, "current.json");
            var current = new SaveData { Header = { FormatVersion = SaveData.CurrentFormatVersion } };
            current.Characters.Add(new CharacterSaveData { Id = "char-1", Level = 7 });
            File.WriteAllText(path, SaveService.ToJson(current));

            var result = new SaveFileStore().Load(path);

            Assert.AreEqual(SaveLoadOutcome.Success, result.Outcome);
            Assert.AreEqual(7, result.Data.Characters[0].Level);
        }

        [Test]
        public void ANewerVersionIsRefusedAsUnsupportedAndVersionZeroWithNoChainIsNoMigrationPath()
        {
            var newer = Path.Combine(tempDir, "newer.json");
            File.WriteAllText(newer, SaveService.ToJson(new SaveData { Header = { FormatVersion = SaveData.CurrentFormatVersion + 1 } }));
            Assert.AreEqual(SaveLoadOutcome.UnsupportedNewerVersion, new SaveFileStore().Load(newer).Outcome);

            var stuck = Path.Combine(tempDir, "stuck.json");
            File.WriteAllText(stuck, "{\"Header\":{\"FormatVersion\":0,\"SavedAtUtc\":\"x\"}}");
            Assert.AreEqual(SaveLoadOutcome.NoMigrationPath, new SaveFileStore().Load(stuck).Outcome);
        }

        // ---------------------------------------------------------------------------------------------
        // Sources of appearance: NPCs and build plans
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void NpcSpawnCopiesTheAuthoredAppearanceAndAnNpcWithNoneHasAnEmptyMap()
        {
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            made.Add(definition);
            Assert.IsNotNull(definition.Appearance);
            Assert.AreEqual(0, definition.Appearance.Count);

            var bare = NpcSpawnService.Spawn(definition, new RulesDatabase(), RuntimeEntityId.NewId());
            Assert.AreEqual(0, bare.Appearance.Count);

            SetField(definition, "appearance", new List<AppearanceEntry>
            {
                new(Hair, HairOption),
                new(Body, BodyOption),
                null,
                new(default, ClothesOption),
            });
            var spawned = NpcSpawnService.Spawn(definition, new RulesDatabase(), RuntimeEntityId.NewId());

            Assert.AreEqual(2, spawned.Appearance.Count, "null and invalid entries are skipped");
            Assert.AreEqual(HairOption, spawned.Appearance[Hair]);
        }

        [Test]
        public void BuildCopiesThePlansAppearanceAndAPlanWithNoneBuildsAnEmptyNonNullMap()
        {
            var service = new CharacterBuildService(new RulesDatabase());

            var bare = service.Build(new CharacterBuildPlan(), RuntimeEntityId.NewId());
            Assert.IsNotNull(bare.Appearance);
            Assert.AreEqual(0, bare.Appearance.Count);

            var plan = new CharacterBuildPlan();
            plan.SetAppearance(Hair, HairOption);
            plan.SetAppearance(Body, BodyOption);
            plan.SetAppearance(default, ClothesOption); // ignored
            plan.ClearAppearance(Body);
            var built = service.Build(plan, RuntimeEntityId.NewId());

            Assert.AreEqual(1, built.Appearance.Count);
            Assert.AreEqual(HairOption, built.Appearance[Hair]);
        }

        [Test]
        public void EverySerializedFieldOnNpcDefinitionIsDrawnByTheCustomInspector()
        {
            // The custom inspector lists property names explicitly, so a new NpcDefinition field is invisible until added there.
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            made.Add(definition);
            var editor = UnityEditor.Editor.CreateEditor(definition);
            made.Add(editor);

            var root = editor.CreateInspectorGUI();
            var drawn = root.Query<PropertyField>().ToList().Select(f => f.bindingPath).ToHashSet();

            var declared = typeof(NpcDefinition).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(f => f.Name);
            foreach (var field in declared)
                Assert.IsTrue(drawn.Contains(field), $"NpcDefinition.{field} is not drawn by NpcDefinitionEditor");
        }
    }
}
