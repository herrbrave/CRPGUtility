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
    /// 2D campaign Task 4.1: <see cref="CharacterCreatorModel"/> against a small synthetic ruleset (two-to-three options per row, four
    /// classes: a Fighter and a Wizard with level-1 choices, a Cleric whose weapon hides hair, and a weaponless Monk).
    /// </summary>
    public class CharacterCreatorModelTests
    {
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Clothes = new("layer-clothes");
        private static readonly DefinitionId WeaponLayer = new("layer-weapon");
        private static readonly DefinitionId MainHand = new("slot-mainhand");
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
            RegisterCatalog("catalog-hair", Hair, "hair-1", "hair-2", "hair-3");
            RegisterCatalog("catalog-clothes", Clothes, "clothes-a", "clothes-b");

            RegisterItem("item-sword", WeaponProfile("visual-sword", "sword", 5));
            RegisterItem("item-axe", WeaponProfile("visual-axe", "axe", 5));
            RegisterItem("item-staff", null); // no profile: draws nothing
            RegisterItem("item-crown", WeaponProfile("visual-crown", "crown", 9, hides: Hair)); // a weapon whose profile hides hair

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

        private EquipmentVisualProfile WeaponProfile(string id, string spriteName, int priority, DefinitionId hides = default)
        {
            var profile = ScriptableObject.CreateInstance<EquipmentVisualProfile>();
            TestDefinitionFactory.SetPrivateField(profile, "id", id);
            TestDefinitionFactory.SetPrivateField(profile, "kind", EquipmentVisualKind.Sprite);
            TestDefinitionFactory.SetPrivateField(profile, "spritePriority", priority);
            TestDefinitionFactory.SetPrivateField(profile, "spriteLayers", new List<SpriteLayerEntry> { new(WeaponLayer, NewSprite(spriteName), Color.white) });
            TestDefinitionFactory.SetPrivateField(profile, "hiddenLayerSlots", hides.IsValid ? new List<DefinitionId> { hides } : new List<DefinitionId>());
            return Register(profile);
        }

        private void RegisterItem(string id, EquipmentVisualProfile profile)
        {
            var item = TestDefinitionFactory.CreateItemDefinition(id, id, equipmentSlotId: MainHand);
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
            new[] { Opt("item-sword", "Sword"), Opt("item-axe", "Axe") }, Ids(StatIds),
            defaults ?? new[] { new CreatorChoiceSpec("choose-style", Ids("feature-style-a")) });

        private static CreatorClassSpec Wizard(IEnumerable<CreatorChoiceSpec> defaults = null) => new(Id("class-wizard"),
            new[] { Opt("item-staff", "Staff"), Opt("item-sword", "Sword") }, Ids(StatIds.Reverse().ToArray()),
            defaults ?? new[] { new CreatorChoiceSpec("choose-cantrips", Ids("spell-a", "spell-b")) });

        private static CreatorClassSpec Cleric() => new(Id("class-cleric"), new[] { Opt("item-axe", "Axe"), Opt("item-crown", "Crown") }, Ids(StatIds), null);
        private static CreatorClassSpec Monk() => new(Id("class-monk"), null, Ids(StatIds), null);

        private static IEnumerable<CreatorAppearanceRowSpec> AppearanceRows() => new[]
        {
            new CreatorAppearanceRowSpec(Body, "Skin", Opts("body-a", "body-b")),
            new CreatorAppearanceRowSpec(Hair, "Hair", Opts("hair-1", "hair-2", "hair-3")),
            new CreatorAppearanceRowSpec(Clothes, "Outfit", Opts("clothes-a", "clothes-b")),
        };

        private static CreatorContent Content(IEnumerable<CreatorClassSpec> classes = null, string race = "race-human") =>
            new(Id(race), Id("bg-soldier"), 1, Array6, AppearanceRows(), classes ?? new[] { Fighter(), Wizard(), Cleric(), Monk() });

        private CharacterCreatorModel NewModel(CreatorContent content = null)
        {
            // The report callback goes through the constructor: problems found while constructing (an unknown class) would be lost otherwise.
            return new CharacterCreatorModel(rules, content ?? Content(), reports.Add);
        }

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
        public void RowsAppearInWireframeOrderAppearanceThenWeaponThenClass()
        {
            var model = NewModel();

            CollectionAssert.AreEqual(new[] { "layer-body", "layer-hair", "layer-clothes", "weapon", "class" }, model.Rows.Select(r => r.Key).ToArray());
            CollectionAssert.AreEqual(new[] { "Skin", "Hair", "Outfit", "Weapon", "Class" }, model.Rows.Select(r => r.Title).ToArray());
            Assert.AreSame(model.WeaponRow, model.RowFor("weapon"));
            Assert.AreSame(model.ClassRow, model.RowFor("class"));
            Assert.IsNull(model.RowFor("nope"));
            CollectionAssert.AreEqual(new[] { "Fighter", "Wizard", "Cleric", "Monk" }, model.ClassRow.Options.Select(o => o.Name).ToArray(), "class names come from the ruleset");
            CollectionAssert.AreEqual(new[] { "Sword", "Axe" }, model.WeaponRow.Options.Select(o => o.Name).ToArray(), "the first class's weapons");
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
        // Preview
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void ThePreviewStartsAssembledFromTheFirstOptionOfEveryRow()
        {
            var model = NewModel();

            Assert.AreEqual(SpriteLayerCompositor.DefaultSlots().Count, model.Preview.SlotCount, "the world's slot set");
            Assert.AreSame(sprites["body-a"], PreviewSprite(model, Body));
            Assert.AreSame(sprites["hair-1"], PreviewSprite(model, Hair));
            Assert.AreSame(sprites["clothes-a"], PreviewSprite(model, Clothes));
            Assert.AreSame(sprites["sword"], PreviewSprite(model, WeaponLayer), "the first class's first weapon, worn as equipment");
        }

        [Test]
        public void ChangingARowChangesThePreviewAndBumpsItsVersionButReselectingDoesNot()
        {
            var model = NewModel();
            var hairRow = model.RowFor("layer-hair");
            var versionBefore = model.Preview.Version;

            hairRow.Advance(1);

            Assert.AreSame(sprites["hair-2"], PreviewSprite(model, Hair));
            Assert.Greater(model.Preview.Version, versionBefore);

            var versionAfter = model.Preview.Version;
            hairRow.Select(Id("hair-2"));
            model.Rows.First(r => r.Key == "layer-body").SelectIndex(0);
            Assert.AreEqual(versionAfter, model.Preview.Version, "no real change, no version bump");
        }

        [Test]
        public void ChangingTheWeaponSwapsTheWeaponLayer()
        {
            var model = NewModel();

            model.WeaponRow.Advance(1);

            Assert.AreSame(sprites["axe"], PreviewSprite(model, WeaponLayer));
        }

        [Test]
        public void AClassChangeRebuildsTheWeaponRowAndKeepsAStillOfferedWeapon()
        {
            var model = NewModel();
            model.WeaponRow.Select(Id("item-axe")); // Fighter: axe

            model.ClassRow.Select(Id("class-cleric")); // Cleric offers {axe, crown}: axe is still offered
            Assert.AreEqual("item-axe", model.WeaponRow.Selected.Id.Value, "kept");
            Assert.AreEqual(0, model.WeaponRow.Index, "at its new position");

            model.ClassRow.Select(Id("class-wizard")); // Wizard offers {staff, sword}: axe is gone
            Assert.AreEqual("item-staff", model.WeaponRow.Selected.Id.Value, "falls back to the first");
        }

        [Test]
        public void AClassWithNoStarterWeaponLeavesThePreviewUnarmedAndTheWeaponComesBack()
        {
            var model = NewModel();
            Assert.IsNotNull(PreviewSprite(model, WeaponLayer));

            model.ClassRow.Select(Id("class-monk"));
            Assert.AreEqual(0, model.WeaponRow.Count);
            Assert.IsNull(PreviewSprite(model, WeaponLayer), "the remembered equipment key is cleared");

            model.ClassRow.Select(Id("class-fighter"));
            Assert.AreSame(sprites["sword"], PreviewSprite(model, WeaponLayer));
        }

        [Test]
        public void AWeaponWithNoSpriteProfileDrawsNothing()
        {
            var model = NewModel();
            model.ClassRow.Select(Id("class-wizard")); // keeps the Sword (the Wizard offers it too)
            Assert.IsNotNull(PreviewSprite(model, WeaponLayer));

            model.WeaponRow.Select(Id("item-staff")); // the staff has no profile
            Assert.AreEqual("item-staff", model.WeaponRow.Selected.Id.Value);
            Assert.IsNull(PreviewSprite(model, WeaponLayer));
        }

        [Test]
        public void AWeaponProfileThatHidesHairHidesItInThePreviewJustAsInTheWorld()
        {
            var model = NewModel();
            Assert.IsTrue(PreviewVisible(model, Hair));

            model.ClassRow.Select(Id("class-cleric"));
            model.WeaponRow.Select(Id("item-crown"));

            Assert.IsFalse(PreviewVisible(model, Hair), "equipment hide beats appearance");
            model.WeaponRow.Select(Id("item-axe"));
            Assert.IsTrue(PreviewVisible(model, Hair), "and the hair returns when the item is swapped out");
        }

        [Test]
        public void TheModelReportsEveryChangeOnceThroughItsChangedEvent()
        {
            var model = NewModel();
            var changes = 0;
            model.Changed += () => changes++;

            model.RowFor("layer-hair").Advance(1);
            Assert.AreEqual(1, changes);

            model.ClassRow.Advance(1); // also rebuilds the weapon row: still ONE notification
            Assert.AreEqual(2, changes);

            model.RowFor("layer-hair").SelectIndex(model.RowFor("layer-hair").Index);
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
        public void RandomizeLeavesTheWeaponInsideTheChosenClassesList()
        {
            var model = NewModel();
            for (var seed = 0; seed < 40; seed++)
            {
                model.Randomize(seed);
                var spec = new[] { Fighter(), Wizard(), Cleric(), Monk() }.Single(c => c.ClassId == model.ClassRow.Selected.Id);
                if (spec.Weapons.Count == 0) Assert.AreEqual(0, model.WeaponRow.Count);
                else CollectionAssert.Contains(spec.Weapons.Select(w => w.Id).ToArray(), model.WeaponRow.Selected.Id, $"seed {seed}");
                Assert.AreSame(PreviewSprite(model, Body), sprites[model.RowFor("layer-body").Selected.Id.Value], "the preview follows the randomized selection");
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
        public void BuildCarriesRaceClassBackgroundLevelAppearanceAndTheWeapon()
        {
            var model = NewModel();
            model.RowFor("layer-hair").Advance(2);
            model.RowFor("layer-body").Advance(1);
            model.WeaponRow.Select(Id("item-axe"));

            var result = model.Build();

            Assert.AreEqual("race-human", result.Plan.Race.Id.Value);
            Assert.AreEqual("class-fighter", result.Plan.Class.Id.Value);
            Assert.AreEqual("bg-soldier", result.Plan.Background.Id.Value);
            Assert.AreEqual(1, result.Plan.Level);
            Assert.AreEqual("class-fighter", result.ClassId.Value);
            Assert.AreEqual("item-axe", result.WeaponItemId.Value);
            Assert.AreEqual("hair-3", result.Plan.Appearance[Hair].Value);
            Assert.AreEqual("body-b", result.Plan.Appearance[Body].Value);
            Assert.AreEqual("clothes-a", result.Plan.Appearance[Clothes].Value);
            Assert.IsFalse(result.Plan.Appearance.ContainsKey(WeaponLayer), "the weapon is equipment, not appearance");
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
            CollectionAssert.AreEqual(new[] { "choose-cantrips" }, second.Plan.ResolvedChoices.Select(r => r.Requirement.Id).ToArray(),
                "only the Wizard's requirements");
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
