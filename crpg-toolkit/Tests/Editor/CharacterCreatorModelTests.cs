using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation.Creator;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>
    /// 2D campaign Task 4.2b: <see cref="CharacterCreatorModel"/> against a small synthetic ruleset. Rows are Skin, Hair Colour (a filter row with swatches),
    /// Hair Style (one style per colour position, same shape in every colour) and Class; the class fixes a starting kit (armor + weapon) that the preview wears
    /// as equipment. Classes: a Fighter and a Wizard with level-1 choices, a Cleric whose kit includes a cowl that hides hair, and a kit-less Monk.
    /// </summary>
    public class CharacterCreatorModelTests
    {
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Clothes = new("layer-clothes");
        private static readonly DefinitionId WeaponLayer = new("layer-weapon");
        private static readonly DefinitionId MainHand = new("slot-mainhand");
        private static readonly DefinitionId ArmorSlot = new("slot-armor");
        private static readonly DefinitionId HeadSlot = new("slot-head");
        private static readonly string[] StatIds = { "stat-strength", "stat-dexterity", "stat-constitution", "stat-intelligence", "stat-wisdom", "stat-charisma" };
        private static readonly int[] Array6 = { 15, 14, 13, 12, 10, 8 };

        private readonly List<UnityEngine.Object> made = new();
        private readonly List<string> reports = new();
        private readonly Dictionary<string, Sprite> sprites = new();
        private RulesDatabase rules;

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase();
            reports.Clear();
            sprites.Clear();

            RegisterCatalog("catalog-body", Body, "body-a", "body-b");
            RegisterCatalog("catalog-hair", Hair, "hair-b1", "hair-b2", "hair-b3", "hair-r1", "hair-r2", "hair-r3"); // brown then ginger, three shapes each
            RegisterCatalog("catalog-clothes", Clothes, "clothes-a");

            RegisterItem("item-leather", ArmorSlot, ClothesProfile("visual-leather", "leather", 10));
            RegisterItem("item-robe", ArmorSlot, ClothesProfile("visual-robe", "robe", 10));
            RegisterItem("item-sword", MainHand, WeaponProfile("visual-sword", "sword", 5));
            RegisterItem("item-staff", MainHand, null); // no profile: draws nothing
            RegisterItem("item-cowl", HeadSlot, WeaponProfile("visual-cowl", "cowl", 9, hides: Hair)); // a kit item whose profile hides hair

            var styleA = Feature("feature-style-a");
            var styleB = Feature("feature-style-b", TestDefinitionFactory.CreateChoiceRequirement(
                "choose-style-b-extra", "Style B extra", ChoiceType.SelectSpell, 1, new DefinitionId("spell-x"), new DefinitionId("spell-y")));
            var fighterFeature = Feature("feature-fighter", TestDefinitionFactory.CreateChoiceRequirement(
                "choose-style", "Fighting Style", ChoiceType.SelectFeature, 1, styleA.Id, styleB.Id));
            var wizardFeature = Feature("feature-wizard", TestDefinitionFactory.CreateChoiceRequirement(
                "choose-cantrips", "Cantrips", ChoiceType.SelectSpell, 2, new DefinitionId("spell-a"), new DefinitionId("spell-b"), new DefinitionId("spell-c")));
            var plainFeature = Feature("feature-plain");

            RegisterClass("class-fighter", "Fighter", fighterFeature);
            RegisterClass("class-wizard", "Wizard", wizardFeature);
            RegisterClass("class-cleric", "Cleric", plainFeature);
            RegisterClass("class-monk", "Monk", plainFeature);

            Register(TestDefinitionFactory.CreateRaceDefinition("race-human", "Human"));
            Register(TestDefinitionFactory.CreateBackgroundDefinition("bg-soldier", "Soldier"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
        }

        // ---------------------------------------------------------------------------------------------
        // Synthetic ruleset builders
        // ---------------------------------------------------------------------------------------------

        private T Register<T>(T definition) where T : DefinitionAsset
        {
            made.Add(definition);
            rules.Register(definition);
            return definition;
        }

        private Sprite NewSprite(string name)
        {
            var texture = new Texture2D(4, 4) { name = name };
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            made.Add(texture);
            made.Add(sprite);
            sprites[name] = sprite;
            return sprite;
        }

        private void RegisterCatalog(string id, DefinitionId slot, params string[] optionIds)
        {
            var catalog = ScriptableObject.CreateInstance<SpriteLayerCatalog>();
            TestDefinitionFactory.SetPrivateField(catalog, "id", id);
            TestDefinitionFactory.SetPrivateField(catalog, "layerSlot", slot);
            TestDefinitionFactory.SetPrivateField(catalog, "displayName", slot.Value);
            TestDefinitionFactory.SetPrivateField(catalog, "options",
                optionIds.Select(o => new SpriteOptionEntry(new DefinitionId(o), o, NewSprite(o), Color.white)).ToList());
            Register(catalog);
        }

        private EquipmentVisualProfile Profile(string id, DefinitionId layer, string spriteName, int priority, DefinitionId hides)
        {
            var profile = ScriptableObject.CreateInstance<EquipmentVisualProfile>();
            TestDefinitionFactory.SetPrivateField(profile, "id", id);
            TestDefinitionFactory.SetPrivateField(profile, "kind", EquipmentVisualKind.Sprite);
            TestDefinitionFactory.SetPrivateField(profile, "spritePriority", priority);
            TestDefinitionFactory.SetPrivateField(profile, "spriteLayers", new List<SpriteLayerEntry> { new(layer, NewSprite(spriteName), Color.white) });
            TestDefinitionFactory.SetPrivateField(profile, "hiddenLayerSlots", hides.IsValid ? new List<DefinitionId> { hides } : new List<DefinitionId>());
            return Register(profile);
        }

        private EquipmentVisualProfile WeaponProfile(string id, string spriteName, int priority, DefinitionId hides = default) => Profile(id, WeaponLayer, spriteName, priority, hides);
        private EquipmentVisualProfile ClothesProfile(string id, string spriteName, int priority) => Profile(id, Clothes, spriteName, priority, default);

        private void RegisterItem(string id, DefinitionId slot, EquipmentVisualProfile profile)
        {
            var item = TestDefinitionFactory.CreateItemDefinition(id, id, equipmentSlotId: slot);
            if (profile != null) TestDefinitionFactory.SetPrivateField(item, "equipmentVisualProfileId", profile.Id);
            Register(item);
        }

        private FeatureDefinition Feature(string id, params ChoiceRequirement[] choices)
        {
            var feature = TestDefinitionFactory.CreateFeatureDefinition(id, id);
            if (choices.Length > 0) TestDefinitionFactory.SetChoices(feature, choices);
            return Register(feature);
        }

        private void RegisterClass(string id, string name, FeatureDefinition level1) =>
            Register(TestDefinitionFactory.CreateClassDefinition(id, name, new DiceExpression(1, 8, 0), TestDefinitionFactory.CreateClassLevelGrant(1, level1.Id)));

        private static DefinitionId Id(string id) => new(id);
        private static CreatorOption Opt(string id, string name = null) => new(Id(id), name ?? id);
        private static CreatorOption[] Opts(params string[] ids) => ids.Select(i => Opt(i)).ToArray();
        private static DefinitionId[] Ids(params string[] ids) => ids.Select(Id).ToArray();

        private static CreatorClassSpec Fighter(IEnumerable<CreatorChoiceSpec> defaults = null) => new(Id("class-fighter"),
            new[] { Opt("item-leather", "Leather"), Opt("item-sword", "Sword") }, Ids(StatIds),
            defaults ?? new[] { new CreatorChoiceSpec("choose-style", Ids("feature-style-a")) });

        private static CreatorClassSpec Wizard(IEnumerable<CreatorChoiceSpec> defaults = null) => new(Id("class-wizard"),
            new[] { Opt("item-robe", "Robe"), Opt("item-staff", "Staff") }, Ids(StatIds.Reverse().ToArray()),
            defaults ?? new[] { new CreatorChoiceSpec("choose-cantrips", Ids("spell-a", "spell-b")) });

        private static CreatorClassSpec Cleric() => new(Id("class-cleric"), new[] { Opt("item-leather", "Leather"), Opt("item-cowl", "Cowl") }, Ids(StatIds), null);
        private static CreatorClassSpec Monk() => new(Id("class-monk"), null, Ids(StatIds), null);

        private static CreatorFilter Brown() => new("brown", "Brown", new Color32(161, 125, 82, 255), Ids("hair-b1", "hair-b2", "hair-b3"));
        private static CreatorFilter Ginger() => new("ginger", "Ginger", new Color32(192, 117, 28, 255), Ids("hair-r1", "hair-r2", "hair-r3"));

        private static CreatorFilteredAppearanceRowSpec HairSpec(params CreatorFilter[] filters) =>
            new(Hair, "hair-color", "Hair Colour", filters.Length > 0 ? filters : new[] { Brown(), Ginger() }, "Hair Style", new[] { "Short", "Long", "Braid" });

        private static IEnumerable<CreatorAppearanceRowSpec> AppearanceRows(CreatorAppearanceRowSpec hair = null) => new CreatorAppearanceRowSpec[]
        {
            new(Body, "Skin", Opts("body-a", "body-b")),
            hair ?? HairSpec(),
        };

        private static CreatorContent Content(IEnumerable<CreatorClassSpec> classes = null, string race = "race-human", CreatorAppearanceRowSpec hair = null) =>
            new(Id(race), Id("bg-soldier"), 1, Array6, AppearanceRows(hair), classes ?? new[] { Fighter(), Wizard(), Cleric(), Monk() });

        private CharacterCreatorModel NewModel(CreatorContent content = null) =>
            // The report callback goes through the constructor: problems found while constructing (an unknown class, a bad filter) would be lost otherwise.
            new(rules, content ?? Content(), reports.Add);

        private Sprite PreviewSprite(CharacterCreatorModel model, DefinitionId slot)
        {
            Assert.IsTrue(model.Preview.TryGetSlotIndex(slot, out var i), slot.Value);
            return model.Preview.Layers[i].Sprite; // read the value NOW: Layers is one array refreshed in place, so keep the sprite, not the array
        }

        private bool PreviewVisible(CharacterCreatorModel model, DefinitionId slot)
        {
            Assert.IsTrue(model.Preview.TryGetSlotIndex(slot, out var i));
            return model.Preview.Layers[i].Visible;
        }

        // ---------------------------------------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void RowsAppearInOrderSkinHairColourHairStyleClass()
        {
            var model = NewModel();

            CollectionAssert.AreEqual(new[] { "layer-body", "hair-color", "layer-hair", "class" }, model.Rows.Select(r => r.Key).ToArray());
            CollectionAssert.AreEqual(new[] { "Skin", "Hair Colour", "Hair Style", "Class" }, model.Rows.Select(r => r.Title).ToArray());
            Assert.AreSame(model.ClassRow, model.RowFor("class"));
            Assert.IsNull(model.RowFor("weapon"), "the class fixes the weapon: there is no Weapon row");
            Assert.IsNull(model.RowFor("nope"));
            CollectionAssert.AreEqual(new[] { "Fighter", "Wizard", "Cleric", "Monk" }, model.ClassRow.Options.Select(o => o.Name).ToArray(), "class names come from the ruleset");
            CollectionAssert.AreEqual(new[] { "Brown", "Ginger" }, model.RowFor("hair-color").Options.Select(o => o.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "Short", "Long", "Braid" }, model.RowFor("layer-hair").Options.Select(o => o.Name).ToArray());
        }

        [Test]
        public void AContentClassTheRulesetDoesNotKnowIsSkippedAndReported()
        {
            var ghost = new CreatorClassSpec(Id("class-ghost"), Opts("item-sword"), Ids(StatIds), null);
            var model = NewModel(Content(new[] { ghost, Fighter() }));

            CollectionAssert.AreEqual(new[] { "Fighter" }, model.ClassRow.Options.Select(o => o.Name).ToArray());
            Assert.IsTrue(reports.Any(r => r.Contains("class-ghost")), string.Join("|", reports));
        }

        // ---------------------------------------------------------------------------------------------
        // Hair colour filter
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void TheColourRowCarriesASwatchPerColourAndTheStyleRowNone()
        {
            var model = NewModel();
            var colours = model.RowFor("hair-color").Options;

            Assert.IsTrue(colours.All(o => o.HasSwatch));
            Assert.AreEqual(new Color32(161, 125, 82, 255), colours[0].Swatch);
            Assert.AreEqual(new Color32(192, 117, 28, 255), colours[1].Swatch);
            Assert.IsFalse(model.RowFor("layer-hair").Options.Any(o => o.HasSwatch));
            Assert.IsFalse(model.RowFor("layer-body").Options.Any(o => o.HasSwatch));
        }

        [Test]
        public void ChangingTheColourSwapsTheStyleOptionsKeepsTheStyleAndRepaintsThePreview()
        {
            var model = NewModel();
            var style = model.RowFor("layer-hair");
            style.Select(Id("hair-b3")); // the third shape, brown
            Assert.AreEqual(2, style.Index);
            Assert.AreSame(sprites["hair-b3"], PreviewSprite(model, Hair));

            model.RowFor("hair-color").Advance(1); // ginger

            Assert.AreEqual(2, style.Index, "the same style position");
            Assert.AreEqual("Braid", style.Selected.Name, "the same style name");
            Assert.AreEqual("hair-r3", style.Selected.Id.Value, "in the new colour");
            CollectionAssert.AreEqual(new[] { "hair-r1", "hair-r2", "hair-r3" }, style.Options.Select(o => o.Id.Value).ToArray());
            Assert.AreSame(sprites["hair-r3"], PreviewSprite(model, Hair));
        }

        [Test]
        public void TheAppearanceCarriesTheStyleOptionAndNoEntryForTheColourRow()
        {
            var model = NewModel();
            model.RowFor("hair-color").Advance(1);
            model.RowFor("layer-hair").Advance(1);

            var plan = model.Build().Plan;

            Assert.AreEqual("hair-r2", plan.Appearance[Hair].Value);
            Assert.AreEqual(2, plan.Appearance.Count, "skin and hair only: the colour row has no layer slot, and the class armor is equipment, not appearance");
            Assert.IsFalse(plan.Appearance.ContainsKey(Clothes));
        }

        [Test]
        public void AFilterWhoseOptionCountDiffersIsReportedAndSkipped()
        {
            var skewed = new CreatorFilter("blond", "Blond", new Color32(226, 212, 168, 255), Ids("hair-r1", "hair-r2")); // two shapes, not three
            var model = NewModel(Content(hair: HairSpec(Brown(), skewed, Ginger())));

            CollectionAssert.AreEqual(new[] { "Brown", "Ginger" }, model.RowFor("hair-color").Options.Select(o => o.Name).ToArray());
            Assert.IsTrue(reports.Any(r => r.Contains("blond")), string.Join("|", reports));
        }

        [Test]
        public void AFilteredSpecWithNoUsableFilterContributesNoRowsAndIsReported()
        {
            var model = NewModel(Content(hair: new CreatorFilteredAppearanceRowSpec(Hair, "hair-color", "Hair Colour", new CreatorFilter[0], "Hair Style", new string[0])));

            CollectionAssert.AreEqual(new[] { "layer-body", "class" }, model.Rows.Select(r => r.Key).ToArray());
            Assert.IsTrue(reports.Any(r => r.Contains("hair-color")), string.Join("|", reports));
            Assert.IsFalse(model.Build().Plan.Appearance.ContainsKey(Hair), "no hair entry");
        }

        // ---------------------------------------------------------------------------------------------
        // Preview and the class kit
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void ThePreviewStartsAssembledFromTheFirstOptionsAndTheFirstClassesKit()
        {
            var model = NewModel();

            Assert.AreEqual(SpriteLayerCompositor.DefaultSlots().Count, model.Preview.SlotCount, "the world's slot set");
            Assert.AreSame(sprites["body-a"], PreviewSprite(model, Body));
            Assert.AreSame(sprites["hair-b1"], PreviewSprite(model, Hair));
            Assert.AreSame(sprites["leather"], PreviewSprite(model, Clothes), "the class armor, worn as equipment on the clothes layer");
            Assert.AreSame(sprites["sword"], PreviewSprite(model, WeaponLayer), "and its weapon");
        }

        [Test]
        public void ChangingARowChangesThePreviewAndBumpsItsVersionButReselectingDoesNot()
        {
            var model = NewModel();
            var body = model.RowFor("layer-body");
            var versionBefore = model.Preview.Version;

            body.Advance(1);

            Assert.AreSame(sprites["body-b"], PreviewSprite(model, Body));
            Assert.Greater(model.Preview.Version, versionBefore);

            var versionAfter = model.Preview.Version;
            body.Select(Id("body-b"));
            model.RowFor("layer-hair").SelectIndex(0);
            Assert.AreEqual(versionAfter, model.Preview.Version, "no real change, no version bump");
        }

        [Test]
        public void TheStartingItemsAreExposedInKitOrderAndFollowTheClass()
        {
            var model = NewModel();

            CollectionAssert.AreEqual(new[] { "Leather", "Sword" }, model.StartingItems.Select(o => o.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "item-leather", "item-sword" }, model.StartingItems.Select(o => o.Id.Value).ToArray());

            model.ClassRow.Select(Id("class-wizard"));
            CollectionAssert.AreEqual(new[] { "item-robe", "item-staff" }, model.StartingItems.Select(o => o.Id.Value).ToArray());

            model.ClassRow.Select(Id("class-monk"));
            Assert.IsEmpty(model.StartingItems);
        }

        [Test]
        public void AClassChangeReplacesTheWholeKitInThePreview()
        {
            var model = NewModel();

            model.ClassRow.Select(Id("class-wizard"));

            Assert.AreSame(sprites["robe"], PreviewSprite(model, Clothes));
            Assert.IsNull(PreviewSprite(model, WeaponLayer), "the wizard's staff has no sprite profile, so nothing is drawn in the weapon layer");
        }

        [Test]
        public void AClassWithNoKitLeavesThePreviewUnclothedAndUnarmedAndTheKitComesBack()
        {
            var model = NewModel();
            Assert.IsNotNull(PreviewSprite(model, Clothes));
            Assert.IsNotNull(PreviewSprite(model, WeaponLayer));

            model.ClassRow.Select(Id("class-monk"));
            Assert.IsNull(PreviewSprite(model, Clothes), "every remembered kit key is cleared");
            Assert.IsNull(PreviewSprite(model, WeaponLayer));

            model.ClassRow.Select(Id("class-fighter"));
            Assert.AreSame(sprites["leather"], PreviewSprite(model, Clothes));
            Assert.AreSame(sprites["sword"], PreviewSprite(model, WeaponLayer));
        }

        [Test]
        public void AKitKeyTheNewClassDoesNotUseIsClearedWhileASharedOneIsKept()
        {
            var model = NewModel();
            model.ClassRow.Select(Id("class-cleric")); // leather (armor slot) + cowl (head slot; hides hair)
            Assert.IsFalse(PreviewVisible(model, Hair), "the cowl hides hair");

            model.ClassRow.Select(Id("class-fighter")); // leather + sword: the head key must go, the armor key stays
            Assert.IsTrue(PreviewVisible(model, Hair), "the cowl's hide is gone");
            Assert.AreSame(sprites["leather"], PreviewSprite(model, Clothes));
        }

        [Test]
        public void AKitItemMissingFromTheRulesetIsReportedAndSkipped()
        {
            var broken = new CreatorClassSpec(Id("class-fighter"), new[] { Opt("item-leather"), Opt("item-nope") }, Ids(StatIds), null);
            var model = NewModel(Content(new[] { broken }));

            Assert.AreSame(sprites["leather"], PreviewSprite(model, Clothes), "the rest of the kit still shows");
            Assert.IsTrue(reports.Any(r => r.Contains("item-nope")), string.Join("|", reports));
        }

        [Test]
        public void TheModelReportsEveryChangeOnceThroughItsChangedEvent()
        {
            var model = NewModel();
            var changes = 0;
            model.Changed += () => changes++;

            model.RowFor("hair-color").Advance(1); // also rebuilds the style options: still ONE notification
            Assert.AreEqual(1, changes);

            model.ClassRow.Advance(1); // also swaps the kit
            Assert.AreEqual(2, changes);

            model.RowFor("layer-body").SelectIndex(model.RowFor("layer-body").Index);
            Assert.AreEqual(2, changes, "a no-op is silent");
        }

        // ---------------------------------------------------------------------------------------------
        // Randomize
        // ---------------------------------------------------------------------------------------------

        private static int[] Indices(CharacterCreatorModel model) => model.Rows.Select(r => r.Index).ToArray();

        [Test]
        public void RandomizeIsDeterministicPerSeedAndVariesAcrossSeedsInEveryRow()
        {
            var a = NewModel();
            var b = NewModel();
            a.Randomize(1234);
            b.Randomize(1234);
            CollectionAssert.AreEqual(Indices(a), Indices(b), "same seed, same selections");

            var seen = a.Rows.ToDictionary(r => r.Key, _ => new HashSet<int>());
            var model = NewModel();
            for (var seed = 0; seed < 60; seed++)
            {
                model.Randomize(seed);
                foreach (var row in model.Rows) seen[row.Key].Add(row.Index);
            }

            foreach (var row in model.Rows.Where(r => r.Count > 1))
                Assert.Greater(seen[row.Key].Count, 1, $"{row.Key} varies across seeds");
        }

        [Test]
        public void RandomizeKeepsTheStyleOptionsInTheChosenColourAndTheKitInTheChosenClass()
        {
            var model = NewModel();
            var brown = Brown().Options.Select(o => o.Value).ToArray();
            var ginger = Ginger().Options.Select(o => o.Value).ToArray();
            var kits = new Dictionary<string, string[]>
            {
                ["class-fighter"] = new[] { "item-leather", "item-sword" }, ["class-wizard"] = new[] { "item-robe", "item-staff" },
                ["class-cleric"] = new[] { "item-leather", "item-cowl" }, ["class-monk"] = new string[0],
            };

            for (var seed = 0; seed < 50; seed++)
            {
                model.Randomize(seed);
                var expectedColour = model.RowFor("hair-color").Selected.Name == "Brown" ? brown : ginger;
                CollectionAssert.AreEqual(expectedColour, model.RowFor("layer-hair").Options.Select(o => o.Id.Value).ToArray(), $"seed {seed}: the style options belong to the chosen colour");
                Assert.AreSame(sprites[model.RowFor("layer-hair").Selected.Id.Value], PreviewSprite(model, Hair), $"seed {seed}: the preview follows");
                CollectionAssert.AreEqual(kits[model.ClassRow.Selected.Id.Value], model.StartingItems.Select(o => o.Id.Value).ToArray(), $"seed {seed}: the kit follows the class");
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void BuildAssignsTheStandardArrayInTheClassScoreOrder()
        {
            var model = NewModel();
            model.ClassRow.Select(Id("class-wizard")); // reversed order: charisma first

            var plan = model.Build().Plan;

            var reversed = StatIds.Reverse().ToArray();
            for (var i = 0; i < 6; i++) Assert.AreEqual(Array6[i], plan.AbilityScores.GetScore(Id(reversed[i])), reversed[i]);
        }

        [Test]
        public void BuildCarriesRaceClassBackgroundLevelAppearanceAndTheKit()
        {
            var model = NewModel();
            model.RowFor("hair-color").Advance(1);
            model.RowFor("layer-hair").Advance(2);
            model.RowFor("layer-body").Advance(1);

            var result = model.Build();

            Assert.AreEqual("race-human", result.Plan.Race.Id.Value);
            Assert.AreEqual("class-fighter", result.Plan.Class.Id.Value);
            Assert.AreEqual("bg-soldier", result.Plan.Background.Id.Value);
            Assert.AreEqual(1, result.Plan.Level);
            Assert.AreEqual("class-fighter", result.ClassId.Value);
            CollectionAssert.AreEqual(new[] { "item-leather", "item-sword" }, result.StartingItemIds.Select(i => i.Value).ToArray(), "armor first, then weapon");
            Assert.AreEqual("hair-r3", result.Plan.Appearance[Hair].Value);
            Assert.AreEqual("body-b", result.Plan.Appearance[Body].Value);
        }

        [Test]
        public void ABuildForAClassWithNoKitHasAnEmptyStartingItemList()
        {
            var model = NewModel();
            model.ClassRow.Select(Id("class-monk"));

            Assert.IsEmpty(model.Build().StartingItemIds);
        }

        [Test]
        public void BuildResolvesTheLevelOneChoicesFromTheClassDefaultsSoTheCharacterBuilds()
        {
            var model = NewModel();
            var service = new CharacterBuildService(rules);

            var fighter = service.Build(model.Build().Plan, RuntimeEntityId.NewId());
            Assert.IsTrue(fighter.Features.Contains(Id("feature-style-a")), "the defaulted fighting style is granted");

            model.ClassRow.Select(Id("class-wizard"));
            var wizardResult = model.Build();
            Assert.IsTrue(wizardResult.Plan.IsComplete);
            var wizard = service.Build(wizardResult.Plan, RuntimeEntityId.NewId());
            Assert.IsTrue(wizard.KnownAbilities.Contains(Id("spell-a")) && wizard.KnownAbilities.Contains(Id("spell-b")), "cantrips are learned");
        }

        [Test]
        public void EachBuildStartsFromAFreshPlanSoSwitchingClassNeverLeaksTheOldClassesChoices()
        {
            var model = NewModel();
            var service = new CharacterBuildService(rules);

            var first = model.Build(); // Fighter: resolves choose-style, granting feature-style-a
            model.ClassRow.Select(Id("class-wizard"));
            var second = model.Build();

            Assert.AreNotSame(first.Plan, second.Plan);
            CollectionAssert.AreEqual(new[] { "choose-cantrips" }, second.Plan.ResolvedChoices.Select(r => r.Requirement.Id).ToArray(), "only the Wizard's requirements");
            var wizard = service.Build(second.Plan, RuntimeEntityId.NewId());
            Assert.IsFalse(wizard.Features.Contains(Id("feature-style-a")), "the Fighter's Fighting Style must not survive on the Wizard");
            Assert.IsFalse(wizard.Features.Contains(Id("feature-fighter")));

            // And building the same class twice in a row must surface and resolve its choices both times (a reused plan would surface none the second time).
            model.ClassRow.Select(Id("class-fighter"));
            var third = model.Build();
            var fourth = model.Build();
            CollectionAssert.AreEqual(new[] { "choose-style" }, fourth.Plan.ResolvedChoices.Select(r => r.Requirement.Id).ToArray());
            Assert.AreEqual(third.Plan.ResolvedChoices.Count, fourth.Plan.ResolvedChoices.Count);
        }

        [Test]
        public void BuildAnswersAChoiceThatAnEarlierSelectionSurfacesFromTheSameDefaults()
        {
            // Selecting Fighting Style B grants a feature that carries its OWN choice: only visible after a further refresh.
            var fighter = Fighter(new[]
            {
                new CreatorChoiceSpec("choose-style", Ids("feature-style-b")),
                new CreatorChoiceSpec("choose-style-b-extra", Ids("spell-x")),
            });
            var model = NewModel(Content(new[] { fighter }));

            var result = model.Build();

            Assert.IsTrue(result.Plan.IsComplete);
            CollectionAssert.AreEquivalent(new[] { "choose-style", "choose-style-b-extra" }, result.Plan.ResolvedChoices.Select(r => r.Requirement.Id).ToArray());
            var character = new CharacterBuildService(rules).Build(result.Plan, RuntimeEntityId.NewId());
            Assert.IsTrue(character.KnownAbilities.Contains(Id("spell-x")));
        }

        [Test]
        public void BuildThrowsWhenAnEarlierSelectionSurfacesAChoiceWithNoDefault()
        {
            var fighter = Fighter(new[] { new CreatorChoiceSpec("choose-style", Ids("feature-style-b")) });
            var model = NewModel(Content(new[] { fighter }));

            var e = Assert.Throws<InvalidOperationException>(() => model.Build());
            StringAssert.Contains("choose-style-b-extra", e.Message);
        }

        [Test]
        public void BuildThrowsWhenARequirementHasNoDefault()
        {
            var model = NewModel(Content(new[] { Fighter(defaults: new CreatorChoiceSpec[0]) }));

            var e = Assert.Throws<InvalidOperationException>(() => model.Build());
            StringAssert.Contains("choose-style", e.Message);
        }

        [Test]
        public void BuildThrowsWhenADefaultHasTheWrongNumberOfOptions()
        {
            var model = NewModel(Content(new[] { Wizard(new[] { new CreatorChoiceSpec("choose-cantrips", Ids("spell-a")) }) }));

            var e = Assert.Throws<InvalidOperationException>(() => model.Build());
            StringAssert.Contains("choose-cantrips", e.Message);
        }

        [Test]
        public void BuildThrowsWhenADefaultIsNotAnOptionOfTheRequirement()
        {
            // ResolveChoice checks only the COUNT, so the model must check membership itself.
            var model = NewModel(Content(new[] { Wizard(new[] { new CreatorChoiceSpec("choose-cantrips", Ids("spell-a", "spell-zzz")) }) }));

            var e = Assert.Throws<InvalidOperationException>(() => model.Build());
            StringAssert.Contains("spell-zzz", e.Message);
        }

        [Test]
        public void BuildThrowsWhenTheRaceOrBackgroundDoesNotResolve()
        {
            var e = Assert.Throws<InvalidOperationException>(() => NewModel(Content(race: "race-ghost")).Build());
            StringAssert.Contains("race-ghost", e.Message);
        }

        [Test]
        public void BuildThrowsWhenTheScoreOrderAndTheArrayDisagreeInLength()
        {
            var short5 = new CreatorClassSpec(Id("class-cleric"), null, Ids(StatIds.Take(5).ToArray()), null);
            var model = NewModel(Content(new[] { short5 }));

            var e = Assert.Throws<InvalidOperationException>(() => model.Build());
            StringAssert.Contains("class-cleric", e.Message);
        }

        [Test]
        public void BuildOnAModelWithNoClassesThrows()
        {
            var model = NewModel(Content(new CreatorClassSpec[0]));

            Assert.AreEqual(0, model.ClassRow.Count);
            Assert.Throws<InvalidOperationException>(() => model.Build());
        }
    }
}
