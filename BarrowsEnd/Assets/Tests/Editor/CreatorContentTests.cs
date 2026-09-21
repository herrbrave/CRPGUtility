using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarrowsEnd.Editor;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Phase 4 Task 4.0: the character creator's curated content (CreatorPicks.json, CreatorClassTable.json) and the starter weapons it
    /// references. Everything runs against a REAL session built from the campaign ruleset, so a ruleset change that invalidates a pick or a
    /// class default fails here rather than in a player's creator.
    /// </summary>
    public class CreatorContentTests
    {
        private const string PicksPath = "Assets/Content/Creator/CreatorPicks.json";
        private const string ClassTablePath = "Assets/Content/Creator/CreatorClassTable.json";
        private const string Srd5ContentDir = "Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content";
        private const string MainHand = "slot-mainhand";
        private static readonly string[] StatIds =
            { "stat-strength", "stat-dexterity", "stat-constitution", "stat-intelligence", "stat-wisdom", "stat-charisma" };
        private static readonly string[] ClassIds = { "class-barbarian", "class-cleric", "class-fighter", "class-rogue", "class-wizard" };

        private GameSession session;
        private GameObject hostObject;
        private CreatorPicks picks;
        private CreatorClassTable table;

        [SetUp]
        public void SetUp()
        {
            var provider = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            Assert.IsNotNull(provider, "the ruleset asset is authored (run 'Barrows End/Content/Author Gear And Ruleset')");
            hostObject = new GameObject("CreatorContentTestsHost");
            session = hostObject.AddComponent<GameSessionHost>().StartSession(provider, new DiceService(new SeededRandomSource(7)), Array.Empty<Type>());
            picks = CreatorPicks.Parse(ReadFile(PicksPath));
            table = CreatorClassTable.Parse(ReadFile(ClassTablePath));
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
        }

        private static string ReadFile(string assetPath)
        {
            Assert.IsTrue(File.Exists(assetPath), $"{assetPath} is missing");
            return File.ReadAllText(assetPath);
        }

        private static (string slot, string sheet, int col, int row) ParseOption(string optionId)
        {
            var parts = optionId.Split('-');
            Assert.AreEqual(4, parts.Length, $"'{optionId}' is <slot>-<sheet>-<col>-<row>");
            return (parts[0], parts[1], int.Parse(parts[2]), int.Parse(parts[3]));
        }

        private IEnumerable<(string row, CreatorPick pick, string catalogSlot, string layerSlot)> AllPicks() =>
            picks.skin.Select(p => ("skin", p, "body", "layer-body"))
                .Concat(picks.hair.Select(p => ("hair", p, "hair", "layer-hair")))
                .Concat(picks.outfit.Select(p => ("outfit", p, "clothes", "layer-clothes")))
                .Concat(picks.weapons.Select(p => ("weapons", p, "weapon", "layer-weapon")));

        // ---- 1. picks ----

        [Test]
        public void EveryCreatorRowHasOptionsAndEveryPickResolvesToASpriteInItsOwnCatalog()
        {
            Assert.IsNotEmpty(picks.skin, "skin");
            Assert.IsNotEmpty(picks.hair, "hair");
            Assert.IsNotEmpty(picks.outfit, "outfit");
            Assert.IsNotEmpty(picks.weapons, "weapons");

            var source = new SpriteCatalogSource(session.RulesDatabase);
            foreach (var (row, pick, catalogSlot, layerSlot) in AllPicks())
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(pick.name), $"{row} pick '{pick.option}' has a curated name");
                var (slot, sheet, _, _) = ParseOption(pick.option);
                Assert.AreEqual(catalogSlot, slot, $"{row} pick '{pick.option}' is in the {catalogSlot} catalog");
                Assert.AreEqual("kenney", sheet, $"{row} pick '{pick.option}' must not be a custom (monster) sheet option");
                Assert.IsTrue(source.TryGetOption(new DefinitionId(layerSlot), new DefinitionId(pick.option), out var resolved), $"{pick.option} resolves");
                Assert.IsNotNull(resolved.Sprite, $"{pick.option} has a sprite");
            }
        }

        [Test]
        public void PickIdsAreUniqueWithinEachRow()
        {
            foreach (var group in AllPicks().GroupBy(p => p.row))
            {
                var ids = group.Select(p => p.pick.option).ToList();
                CollectionAssert.AllItemsAreUnique(ids, $"{group.Key} option ids");
                CollectionAssert.AllItemsAreUnique(group.Select(p => p.pick.name).ToList(), $"{group.Key} names");
            }
        }

        // ---- 2. starter weapons ----

        [Test]
        public void EveryPickedWeaponIsAMainHandItemWhoseProfilePaintsOnlyTheWeaponLayer()
        {
            var rules = session.RulesDatabase;
            var source = new SpriteCatalogSource(rules);
            foreach (var pick in picks.weapons)
            {
                Assert.IsFalse(string.IsNullOrEmpty(pick.item), $"weapon pick '{pick.option}' names its item");
                Assert.IsTrue(rules.TryResolve<ItemDefinition>(new DefinitionId(pick.item), out var item), $"{pick.item} resolves");
                Assert.AreEqual(MainHand, item.EquipmentSlotId.Value, $"{pick.item} slot");
                Assert.IsTrue(rules.TryResolve<EquipmentVisualProfile>(item.EquipmentVisualProfileId, out var profile), $"{pick.item} has a visual profile");
                Assert.AreEqual(EquipmentVisualKind.Sprite, profile.Kind);
                Assert.AreEqual(1, profile.SpriteLayers.Count, $"{pick.item} paints one layer");
                Assert.AreEqual("layer-weapon", profile.SpriteLayers[0].layerSlot.Value, $"{pick.item} paints only the weapon layer (equipment beats appearance, so a wider profile would override the player's outfit/hair/skin)");
                Assert.IsEmpty(profile.HiddenLayerSlots, $"{pick.item} hides nothing");
                Assert.IsTrue(source.TryGetOption(new DefinitionId("layer-weapon"), new DefinitionId(pick.option), out var option));
                Assert.AreSame(option.Sprite, profile.SpriteLayers[0].sprite, $"{pick.item} paints the sprite of {pick.option}");
            }

            CollectionAssert.AllItemsAreUnique(picks.weapons.Select(p => p.item).ToList(), "one item per weapon pick");
        }

        [Test]
        public void NoCampaignDefinitionIdCollidesWithAnSrd5DefinitionId()
        {
            // RulesDatabase.Register throws on a duplicate id, so a collision would already fail SetUp; this names the cause and covers
            // the campaign items even if session start-up ever stops registering everything.
            var srd5Ids = AssetDatabase.FindAssets("t:DefinitionAsset", new[] { Srd5ContentDir })
                .Select(guid => AssetDatabase.LoadAssetAtPath<DefinitionAsset>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(a => a != null)
                .Select(a => a.Id.Value)
                .ToHashSet();
            Assert.Greater(srd5Ids.Count, 100, "the SRD5 content was found");

            var provider = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            foreach (var extra in provider.ExtraContent)
                Assert.IsFalse(srd5Ids.Contains(extra.Id.Value), $"campaign definition '{extra.Id.Value}' collides with an SRD5 id");
        }

        // ---- 3. class table ----

        [Test]
        public void TheTableCoversExactlyTheFiveClassesWithHumanAndAResolvableBackground()
        {
            Assert.AreEqual("race-human", table.race);
            Assert.AreEqual(1, table.level);
            CollectionAssert.AreEqual(new[] { 15, 14, 13, 12, 10, 8 }, table.standardArray, "the SRD standard array: the one source the creator reads");
            Assert.IsTrue(session.RulesDatabase.TryResolve<RaceDefinition>(new DefinitionId(table.race), out _));
            Assert.IsTrue(session.RulesDatabase.TryResolve<BackgroundDefinition>(new DefinitionId(table.background), out _), $"background '{table.background}'");
            CollectionAssert.AreEquivalent(ClassIds, table.classes.Select(c => c.classId).ToArray());
            Assert.IsFalse(table.race.Contains("elf"), "Elf stays excluded");
        }

        [Test]
        public void EveryClassOffersOnlyPickedWeaponsAndAPermutationOfTheSixStats()
        {
            var pickedItems = picks.weapons.Select(p => p.item).ToHashSet();
            foreach (var entry in table.classes)
            {
                Assert.IsNotEmpty(entry.weapons, $"{entry.classId} offers a weapon");
                foreach (var weapon in entry.weapons) Assert.IsTrue(pickedItems.Contains(weapon), $"{entry.classId} weapon '{weapon}' is a picked weapon");
                CollectionAssert.AllItemsAreUnique(entry.weapons, $"{entry.classId} weapons");
                CollectionAssert.AreEquivalent(StatIds, entry.scoreOrder, $"{entry.classId} scoreOrder is a permutation of the six stats");
            }

            // Every weapon pick is a real item (so it can be loot), but only the class table's subset is a STARTER: the rest are not offered.
            var starters = table.classes.SelectMany(c => c.weapons).Distinct().ToList();
            Assert.Less(starters.Count, pickedItems.Count, "starter weapons are a subset of the weapon picks");
        }

        [Test]
        public void EveryClassBuildsACompleteCharacterFromItsDefaultsWithLegalChoices()
        {
            var rules = session.RulesDatabase;
            var service = new CharacterBuildService(rules);

            foreach (var entry in table.classes)
            {
                var plan = new CharacterBuildPlan { Level = table.level };
                Assert.IsTrue(rules.TryResolve<RaceDefinition>(new DefinitionId(table.race), out var race));
                Assert.IsTrue(rules.TryResolve<ClassDefinition>(new DefinitionId(entry.classId), out var cls), entry.classId);
                Assert.IsTrue(rules.TryResolve<BackgroundDefinition>(new DefinitionId(table.background), out var background));
                plan.Race = race;
                plan.Class = cls;
                plan.Background = background;
                for (var i = 0; i < StatIds.Length; i++) plan.AbilityScores.SetScore(new DefinitionId(entry.scoreOrder[i]), table.standardArray[i]);

                service.RefreshPendingChoices(plan);
                var pending = plan.PendingChoices.ToList();
                Assert.IsNotEmpty(pending, $"{entry.classId} has level-1 choices (the table would otherwise be dead data)");

                // The table must cover exactly the requirements the class really surfaces.
                CollectionAssert.AreEquivalent(pending.Select(r => r.Id).ToArray(), entry.choiceDefaults.Select(d => d.requirementId).ToArray(), $"{entry.classId} choiceDefaults cover the pending requirements");

                foreach (var requirement in pending)
                {
                    var defaults = entry.choiceDefaults.Single(d => d.requirementId == requirement.Id);
                    // ResolveChoice checks only the COUNT, never membership, so legality is asserted here explicitly.
                    Assert.AreEqual(requirement.Count, defaults.options.Length, $"{requirement.Id} count");
                    CollectionAssert.AllItemsAreUnique(defaults.options, $"{requirement.Id} defaults");
                    foreach (var option in defaults.options)
                        Assert.IsTrue(requirement.Options.Any(o => o.Value == option), $"{requirement.Id}: '{option}' is a real option");
                    plan.ResolveChoice(requirement.Id, defaults.options.Select(o => new DefinitionId(o)).ToList());
                }

                // A resolved selection re-enters feature collection (a fighting style is a feature) and could surface more choices.
                service.RefreshPendingChoices(plan);
                Assert.IsTrue(plan.IsComplete, $"{entry.classId} is complete after its defaults: {string.Join(", ", plan.PendingChoices.Select(r => r.Id))}");

                var character = service.Build(plan, RuntimeEntityId.NewId());
                for (var i = 0; i < StatIds.Length; i++)
                    Assert.AreEqual(table.standardArray[i] + 1, character.Stats.GetScore(new DefinitionId(entry.scoreOrder[i])), $"{entry.classId} {entry.scoreOrder[i]} (array value + Human's +1)");
            }
        }

        [Test]
        public void RogueExpertiseOnlyCoversSkillsTheRogueIsProficientIn()
        {
            // The two option lists are independent (both offer all 11 skills), so a legal default could otherwise grant Expertise in an untrained skill.
            var rogue = table.classes.Single(c => c.classId == "class-rogue");
            var skills = rogue.choiceDefaults.Single(d => d.requirementId == "choose-rogue-skills").options;
            var expertise = rogue.choiceDefaults.Single(d => d.requirementId == "choose-expertise-skills").options;
            foreach (var skill in expertise) CollectionAssert.Contains(skills, skill, "expertise skill must be one the rogue is proficient in");
        }
    }
}
