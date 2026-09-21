using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Presentation.Creator;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Phase 4 Tasks 4.1-4.2b: the toolkit's <see cref="CharacterCreatorModel"/> fed the campaign's REAL content (the two creator JSON files mapped by
    /// <see cref="CreatorContentLoader"/>) against the real ruleset. Proves the mapping (Skin, Hair Colour x Hair Style, Class with its armor + weapon kit), that
    /// every option previews its own sprite without a report, and that every class builds a complete character.
    /// </summary>
    public class CreatorModelContentTests
    {
        private const string PicksPath = "Assets/Content/Creator/CreatorPicks.json";
        private const string ClassTablePath = "Assets/Content/Creator/CreatorClassTable.json";
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Clothes = new("layer-clothes");
        private static readonly DefinitionId WeaponLayer = new("layer-weapon");

        private GameSession session;
        private GameObject hostObject;
        private readonly List<string> reports = new();
        private CreatorPicks picks;
        private CreatorClassTable table;

        [SetUp]
        public void SetUp()
        {
            var provider = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>("Assets/Content/Ruleset/BarrowsEndRuleset.asset");
            Assert.IsNotNull(provider, "the ruleset asset is authored");
            hostObject = new GameObject("CreatorModelContentTestsHost");
            session = hostObject.AddComponent<GameSessionHost>().StartSession(provider, new DiceService(new SeededRandomSource(7)), Array.Empty<Type>());
            picks = CreatorPicks.Parse(File.ReadAllText(PicksPath));
            table = CreatorClassTable.Parse(File.ReadAllText(ClassTablePath));
            reports.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
        }

        private CharacterCreatorModel NewModel() =>
            new(session.RulesDatabase, CreatorContentLoader.Build(File.ReadAllText(PicksPath), File.ReadAllText(ClassTablePath)), reports.Add);

        private static Sprite LayerSprite(CharacterCreatorModel model, DefinitionId slot)
        {
            Assert.IsTrue(model.Preview.TryGetSlotIndex(slot, out var i));
            return model.Preview.Layers[i].Sprite;
        }

        private Sprite CatalogSprite(string layerSlot, string optionId)
        {
            Assert.IsTrue(new SpriteCatalogSource(session.RulesDatabase).TryGetOption(new DefinitionId(layerSlot), new DefinitionId(optionId), out var option), optionId);
            return option.Sprite;
        }

        private string ItemName(string itemId) => picks.armors.Concat(picks.weapons).First(p => p.item == itemId).name;
        private string ItemOption(string itemId) => picks.armors.Concat(picks.weapons).First(p => p.item == itemId).option;

        [Test]
        public void TheLoaderMapsTheRealFilesOntoRowsOfTheExpectedSizes()
        {
            var model = NewModel();

            CollectionAssert.AreEqual(new[] { "layer-body", "hair-color", "layer-hair", "class" }, model.Rows.Select(r => r.Key).ToArray());
            Assert.AreEqual(4, model.RowFor("layer-body").Count, "skin: column 1 only");
            Assert.AreEqual(5, model.RowFor("hair-color").Count, "brown, ginger, blond, ash, silver");
            Assert.AreEqual(16, model.RowFor("layer-hair").Count, "twelve head styles and four beards");
            Assert.AreEqual(5, model.ClassRow.Count);
            Assert.IsNull(model.RowFor("weapon"), "the class fixes the weapon");
            Assert.IsNull(model.RowFor("layer-clothes"), "the class fixes the outfit");
            CollectionAssert.AreEquivalent(new[] { "Barbarian", "Cleric", "Fighter", "Rogue", "Wizard" }, model.ClassRow.Options.Select(o => o.Name).ToArray());
            Assert.IsEmpty(reports, string.Join("|", reports));

            var swatches = model.RowFor("hair-color").Options;
            Assert.IsTrue(swatches.All(o => o.HasSwatch));
            for (var i = 0; i < swatches.Count; i++)
                Assert.AreEqual(CreatorContentLoader.ParseSwatch(picks.hairColors[i].swatch), swatches[i].Swatch, picks.hairColors[i].id);
            Assert.AreEqual("Fair", model.RowFor("layer-body").Options[0].Name);
        }

        [Test]
        public void EveryClassesKitIsItsArmorThenItsWeaponNamedFromThePicks()
        {
            var model = NewModel();
            foreach (var entry in table.classes)
            {
                model.ClassRow.Select(new DefinitionId(entry.classId));
                CollectionAssert.AreEqual(entry.startingItems, model.StartingItems.Select(o => o.Id.Value).ToArray(), entry.classId);
                CollectionAssert.AreEqual(entry.startingItems.Select(ItemName).ToArray(), model.StartingItems.Select(o => o.Name).ToArray(), $"{entry.classId}: the kit is named from the picks");
            }
        }

        [Test]
        public void EveryHairColourAndEveryStylePreviewsItsOwnCatalogSpriteWithoutAReport()
        {
            var model = NewModel();
            var colour = model.RowFor("hair-color");
            var style = model.RowFor("layer-hair");

            for (var c = 0; c < colour.Count; c++)
            {
                colour.SelectIndex(c);
                for (var s = 0; s < style.Count; s++)
                {
                    style.SelectIndex(s);
                    var expectedOption = picks.hairColors[c].options[s];
                    Assert.AreEqual(expectedOption, style.Selected.Id.Value, $"{picks.hairColors[c].id} style {s}");
                    Assert.AreSame(CatalogSprite("layer-hair", expectedOption), LayerSprite(model, Hair), expectedOption);
                }
            }

            Assert.IsEmpty(reports, string.Join("|", reports));
        }

        [Test]
        public void ChangingTheColourOnTheRealHairKeepsTheStyleAndItsName()
        {
            var model = NewModel();
            var colour = model.RowFor("hair-color");
            var style = model.RowFor("layer-hair");
            style.SelectIndex(6);
            var name = style.Selected.Name;

            for (var i = 0; i < colour.Count; i++)
            {
                colour.SelectIndex(i);
                Assert.AreEqual(6, style.Index, picks.hairColors[i].id);
                Assert.AreEqual(name, style.Selected.Name, "the same style in every colour");
                Assert.AreEqual(picks.hairColors[i].options[6], style.Selected.Id.Value);
            }
        }

        [Test]
        public void EveryClassPreviewsItsArmorAndWeaponSpritesOverEverySkin()
        {
            var model = NewModel();
            var skins = model.RowFor("layer-body");

            foreach (var entry in table.classes)
            {
                model.ClassRow.Select(new DefinitionId(entry.classId));
                var armorSprite = CatalogSprite("layer-clothes", ItemOption(entry.startingItems[0]));
                var weaponSprite = CatalogSprite("layer-weapon", ItemOption(entry.startingItems[1]));
                for (var s = 0; s < skins.Count; s++)
                {
                    skins.SelectIndex(s);
                    Assert.IsNotNull(LayerSprite(model, Body), $"{entry.classId}/skin {s}: body");
                    Assert.AreSame(armorSprite, LayerSprite(model, Clothes), $"{entry.classId}/skin {s}: the class armor is the clothes layer");
                    Assert.AreSame(weaponSprite, LayerSprite(model, WeaponLayer), $"{entry.classId}/skin {s}: the class weapon");
                    Assert.IsNotNull(LayerSprite(model, Hair));
                }
            }

            Assert.IsEmpty(reports, string.Join("|", reports));
        }

        [Test]
        public void EveryClassBuildsACompleteCharacterCarryingTheChosenLookAndItsKit()
        {
            var model = NewModel();
            var service = new CharacterBuildService(session.RulesDatabase);
            model.RowFor("hair-color").SelectIndex(3);
            model.RowFor("layer-hair").SelectIndex(13); // a beard
            model.RowFor("layer-body").SelectIndex(2);

            for (var c = 0; c < model.ClassRow.Count; c++)
            {
                model.ClassRow.SelectIndex(c);
                var result = model.Build();

                Assert.IsTrue(result.Plan.IsComplete, model.ClassRow.Selected.Name);
                Assert.AreEqual(model.ClassRow.Selected.Id, result.ClassId);
                CollectionAssert.AreEqual(model.StartingItems.Select(o => o.Id).ToArray(), result.StartingItemIds.ToArray(), "the kit travels in the result: armor, then weapon");

                var character = service.Build(result.Plan, RuntimeEntityId.NewId());
                Assert.AreEqual(model.RowFor("layer-hair").Selected.Id, character.Appearance[Hair], "hair");
                Assert.AreEqual(model.RowFor("layer-body").Selected.Id, character.Appearance[Body], "skin");
                Assert.AreEqual(2, character.Appearance.Count, "skin and hair only: the outfit and the weapon are equipment, not appearance");
                Assert.AreEqual(15 + 1, character.Stats.GetScore(result.Plan.Class.PrimaryStatIds[0]), $"{model.ClassRow.Selected.Name}: the class's primary stat is first in its score order (15) plus Human's +1");
            }
        }

        [Test]
        public void RandomizingAnySeedYieldsABuildableCharacterAndAFullyDressedArmedPreviewWithoutAReport()
        {
            var model = NewModel();
            for (var seed = 0; seed < 60; seed++)
            {
                model.Randomize(seed);
                Assert.DoesNotThrow(() => model.Build(), $"seed {seed} ({model.ClassRow.Selected.Name})");
                Assert.IsNotNull(LayerSprite(model, Body));
                Assert.IsNotNull(LayerSprite(model, Hair));
                Assert.IsNotNull(LayerSprite(model, Clothes), $"seed {seed}: every class wears its armor");
                Assert.IsNotNull(LayerSprite(model, WeaponLayer), $"seed {seed}: every class carries its weapon");
            }

            Assert.IsEmpty(reports, string.Join("|", reports));
        }
    }
}
