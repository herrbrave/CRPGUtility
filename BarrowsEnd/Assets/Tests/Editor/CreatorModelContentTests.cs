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
    /// Phase 4 Task 4.1: the toolkit's <see cref="CharacterCreatorModel"/> fed the campaign's REAL content (the two creator JSON files mapped by
    /// <see cref="CreatorContentLoader"/>) against the real ruleset. Proves the mapping, that every option of every row previews without a
    /// report, and that every class builds a complete character.
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

        [SetUp]
        public void SetUp()
        {
            var provider = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>("Assets/Content/Ruleset/BarrowsEndRuleset.asset");
            Assert.IsNotNull(provider, "the ruleset asset is authored");
            hostObject = new GameObject("CreatorModelContentTestsHost");
            session = hostObject.AddComponent<GameSessionHost>().StartSession(provider, new DiceService(new SeededRandomSource(7)), Array.Empty<Type>());
            picks = CreatorPicks.Parse(File.ReadAllText(PicksPath));
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

        [Test]
        public void TheLoaderMapsTheRealFilesOntoRowsOfTheExpectedSizes()
        {
            var model = NewModel();

            CollectionAssert.AreEqual(new[] { "layer-body", "layer-hair", "layer-clothes", "weapon", "class" }, model.Rows.Select(r => r.Key).ToArray());
            Assert.AreEqual(4, model.RowFor("layer-body").Count, "skin: column 1 only");
            Assert.AreEqual(80, model.RowFor("layer-hair").Count, "every hair option, beards included");
            Assert.AreEqual(120, model.RowFor("layer-clothes").Count, "every outfit");
            Assert.AreEqual(5, model.ClassRow.Count);
            CollectionAssert.AreEquivalent(new[] { "Barbarian", "Cleric", "Fighter", "Rogue", "Wizard" }, model.ClassRow.Options.Select(o => o.Name).ToArray());
            Assert.IsEmpty(reports, string.Join("|", reports));

            model.ClassRow.Select(new DefinitionId("class-fighter"));
            CollectionAssert.AreEquivalent(new[] { "Iron Sword", "Iron Axe" }, model.WeaponRow.Options.Select(o => o.Name).ToArray());
            CollectionAssert.AreEquivalent(new[] { "item-weapon-44-6", "item-weapon-51-4" }, model.WeaponRow.Options.Select(o => o.Id.Value).ToArray(), "a weapon option's id is its ITEM id");
            Assert.AreEqual("Fair", model.RowFor("layer-body").Options[0].Name);
        }

        [Test]
        public void EveryOptionOfEveryAppearanceRowPreviewsItsOwnSpriteWithoutAReport()
        {
            var model = NewModel();
            var source = new SpriteCatalogSource(session.RulesDatabase);

            foreach (var row in model.Rows.Where(r => r.Key.StartsWith("layer-")))
            {
                var slot = new DefinitionId(row.Key);
                for (var i = 0; i < row.Count; i++)
                {
                    row.SelectIndex(i);
                    Assert.IsTrue(source.TryGetOption(slot, row.Selected.Id, out var expected), row.Selected.Id.Value);
                    Assert.AreSame(expected.Sprite, LayerSprite(model, slot), $"{row.Key} option {i} ({row.Selected.Id.Value}) shows its own sprite");
                }
            }

            Assert.IsEmpty(reports, string.Join("|", reports));
        }

        [Test]
        public void EveryClassAndEveryOfItsWeaponsPreviewsAWeaponSpriteOverEverySkin()
        {
            var model = NewModel();
            var skins = model.RowFor("layer-body");

            for (var c = 0; c < model.ClassRow.Count; c++)
            {
                model.ClassRow.SelectIndex(c);
                Assert.Greater(model.WeaponRow.Count, 0, $"{model.ClassRow.Selected.Name} offers a starter weapon");
                for (var w = 0; w < model.WeaponRow.Count; w++)
                {
                    model.WeaponRow.SelectIndex(w);
                    var weapon = model.WeaponRow.Selected.Id.Value;
                    for (var s = 0; s < skins.Count; s++)
                    {
                        skins.SelectIndex(s);
                        Assert.IsNotNull(LayerSprite(model, Body), $"{model.ClassRow.Selected.Name}/{weapon}/skin {s}: body");
                        Assert.IsNotNull(LayerSprite(model, WeaponLayer), $"{model.ClassRow.Selected.Name}/{weapon}/skin {s}: weapon");
                        Assert.IsNotNull(LayerSprite(model, Hair));
                        Assert.IsNotNull(LayerSprite(model, Clothes));
                    }
                }
            }

            Assert.IsEmpty(reports, string.Join("|", reports));
        }

        [Test]
        public void AWeaponPreviewsTheSpriteOfItsPick()
        {
            var model = NewModel();
            model.ClassRow.Select(new DefinitionId("class-fighter"));
            var source = new SpriteCatalogSource(session.RulesDatabase);

            foreach (var pick in picks.weapons.Where(p => model.WeaponRow.Options.Any(o => o.Id.Value == p.item)))
            {
                model.WeaponRow.Select(new DefinitionId(pick.item));
                Assert.IsTrue(source.TryGetOption(WeaponLayer, new DefinitionId(pick.option), out var expected));
                Assert.AreSame(expected.Sprite, LayerSprite(model, WeaponLayer), pick.item);
            }
        }

        [Test]
        public void EveryClassBuildsACompleteCharacterCarryingTheChosenLookAndWeapon()
        {
            var model = NewModel();
            var service = new CharacterBuildService(session.RulesDatabase);
            model.RowFor("layer-hair").SelectIndex(17);
            model.RowFor("layer-clothes").SelectIndex(63);
            model.RowFor("layer-body").SelectIndex(2);

            for (var c = 0; c < model.ClassRow.Count; c++)
            {
                model.ClassRow.SelectIndex(c);
                var result = model.Build();

                Assert.IsTrue(result.Plan.IsComplete, model.ClassRow.Selected.Name);
                Assert.AreEqual(model.ClassRow.Selected.Id, result.ClassId);
                Assert.AreEqual(model.WeaponRow.Selected.Id, result.WeaponItemId);

                var character = service.Build(result.Plan, RuntimeEntityId.NewId());
                Assert.AreEqual(model.RowFor("layer-hair").Selected.Id, character.Appearance[Hair], "hair");
                Assert.AreEqual(model.RowFor("layer-clothes").Selected.Id, character.Appearance[Clothes], "outfit");
                Assert.AreEqual(model.RowFor("layer-body").Selected.Id, character.Appearance[Body], "skin");
                Assert.IsFalse(character.Appearance.ContainsKey(WeaponLayer), "the weapon is equipment");
                Assert.AreEqual(15 + 1, character.Stats.GetScore(result.Plan.Class.PrimaryStatIds[0]), $"{model.ClassRow.Selected.Name}: the class's primary stat is first in its score order (15) plus Human's +1");
            }
        }

        [Test]
        public void RandomizingAnySeedYieldsABuildableCharacterAndAPreviewWithoutAReport()
        {
            var model = NewModel();
            for (var seed = 0; seed < 60; seed++)
            {
                model.Randomize(seed);
                Assert.DoesNotThrow(() => model.Build(), $"seed {seed} ({model.ClassRow.Selected.Name})");
                Assert.IsNotNull(LayerSprite(model, Body));
                Assert.IsNotNull(LayerSprite(model, WeaponLayer), $"seed {seed}: every class offers a starter weapon");
            }

            Assert.IsEmpty(reports, string.Join("|", reports));
        }
    }
}
