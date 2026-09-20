using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CRPG.Core;
using CRPG.Editor.Validation;
using CRPG.Presentation.Sprites;
using CRPG.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 2.4 (stage A/B logic): SpriteLayerState.ApplyAppearance, SpriteLayerCatalog, SpriteCatalogSource and
    /// the catalog validator.</summary>
    public class SpriteAppearanceLogicTests
    {
        private static readonly DefinitionId Body = new("layer-body");
        private static readonly DefinitionId Hair = new("layer-hair");
        private static readonly DefinitionId Helmet = new("layer-helmet");
        private static readonly DefinitionId Weapon = new("layer-weapon");
        private static readonly DefinitionId Head = new("slot-head");

        private readonly List<Object> made = new();
        private readonly List<string> reports = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            reports.Clear();
        }

        private sealed class DictSource : ISpriteOptionSource
        {
            public readonly Dictionary<(DefinitionId, DefinitionId), SpriteOption> Options = new();
            public bool TryGetOption(DefinitionId slot, DefinitionId option, out SpriteOption result) => Options.TryGetValue((slot, option), out result);
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

        private Sprite NewSprite(string name = "s")
        {
            var texture = new Texture2D(4, 4) { name = name };
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            made.Add(texture);
            made.Add(sprite);
            return sprite;
        }

        private SpriteLayerState NewState()
        {
            var state = new SpriteLayerState(new[] { Body, Hair, Helmet, Weapon });
            state.Report = reports.Add;
            return state;
        }

        private static Dictionary<DefinitionId, DefinitionId> Map(params (DefinitionId slot, string option)[] pairs) =>
            pairs.ToDictionary(p => p.slot, p => new DefinitionId(p.option));

        private static EffectiveLayer LayerOf(SpriteLayerState state, DefinitionId slot) => state.Layers.First(l => l.Slot == slot);

        // ---------------------------------------------------------------------------------------------
        // ApplyAppearance
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void KnownOptionsAreAppliedWithTheirTints()
        {
            var state = NewState();
            var source = new DictSource();
            var body = NewSprite("body");
            var hair = NewSprite("hair");
            source.Options[(Body, new DefinitionId("body-1"))] = new SpriteOption(body, Color.white);
            source.Options[(Hair, new DefinitionId("hair-1"))] = new SpriteOption(hair, Color.red);

            state.ApplyAppearance(Map((Body, "body-1"), (Hair, "hair-1")), source);

            Assert.AreSame(body, LayerOf(state, Body).Sprite);
            Assert.AreSame(hair, LayerOf(state, Hair).Sprite);
            Assert.AreEqual(Color.red, LayerOf(state, Hair).Tint, "the option's tint is honoured");
            Assert.IsEmpty(reports);
        }

        [Test]
        public void ReplaceSemanticsClearSlotsAbsentFromTheMapAndASmallerMapRemovesTheRest()
        {
            var state = NewState();
            var source = new DictSource();
            source.Options[(Body, new DefinitionId("body-1"))] = new SpriteOption(NewSprite("body"), Color.white);
            source.Options[(Hair, new DefinitionId("hair-1"))] = new SpriteOption(NewSprite("hair"), Color.white);
            state.ApplyAppearance(Map((Body, "body-1"), (Hair, "hair-1")), source);
            Assert.IsTrue(LayerOf(state, Hair).Visible);

            state.ApplyAppearance(Map((Body, "body-1")), source);

            Assert.IsTrue(LayerOf(state, Body).Visible);
            Assert.IsFalse(LayerOf(state, Hair).Visible, "a slot absent from the new map is cleared, not left stale");
        }

        [Test]
        public void AnUnknownOptionLeavesItsSlotEmptyAndReportsWhileTheRestApply()
        {
            var state = NewState();
            var source = new DictSource();
            var body = NewSprite("body");
            source.Options[(Body, new DefinitionId("body-1"))] = new SpriteOption(body, Color.white);
            state.SetAppearance(Hair, new SpriteOption(NewSprite("old-hair"), Color.white)); // stale, must not survive

            state.ApplyAppearance(Map((Body, "body-1"), (Hair, "hair-gone")), source);

            Assert.AreSame(body, LayerOf(state, Body).Sprite);
            Assert.IsFalse(LayerOf(state, Hair).Visible);
            Assert.AreEqual(1, reports.Count);
            StringAssert.Contains("hair-gone", reports[0]);
        }

        [Test]
        public void AnUnknownSlotIsReportedAndSkipped()
        {
            var state = NewState();
            var source = new DictSource();
            source.Options[(Body, new DefinitionId("body-1"))] = new SpriteOption(NewSprite(), Color.white);

            state.ApplyAppearance(Map((Body, "body-1"), (new DefinitionId("layer-wings"), "wings-1")), source);

            Assert.IsTrue(LayerOf(state, Body).Visible);
            Assert.AreEqual(1, reports.Count);
            StringAssert.Contains("layer-wings", reports[0]);
        }

        [Test]
        public void ANullMapClearsEverythingAndANullSourceReportsOnceAndTreatsEveryOptionAsUnknown()
        {
            var state = NewState();
            state.SetAppearance(Body, new SpriteOption(NewSprite(), Color.white));

            state.ApplyAppearance(null, new DictSource());
            Assert.IsFalse(state.Layers.Any(l => l.Visible));

            state.SetAppearance(Body, new SpriteOption(NewSprite(), Color.white));
            state.ApplyAppearance(Map((Body, "a"), (Hair, "b")), null);

            Assert.IsFalse(state.Layers.Any(l => l.Visible));
            Assert.AreEqual(1, reports.Count, "one report for the missing source, not one per option");
        }

        [Test]
        public void TheVersionBumpsOnlyWhenTheAppearanceReallyChanges()
        {
            var state = NewState();
            var source = new DictSource();
            source.Options[(Body, new DefinitionId("body-1"))] = new SpriteOption(NewSprite("a"), Color.white);
            source.Options[(Body, new DefinitionId("body-2"))] = new SpriteOption(NewSprite("b"), Color.white);
            var map = Map((Body, "body-1"));

            state.ApplyAppearance(map, source);
            var v1 = state.Version;
            Assert.Greater(v1, 0);

            state.ApplyAppearance(map, source);
            Assert.AreEqual(v1, state.Version, "re-applying an identical map is free");

            state.ApplyAppearance(Map((Body, "body-2")), source);
            Assert.Greater(state.Version, v1);

            state.ApplyAppearance(null, source);
            var cleared = state.Version;
            state.ApplyAppearance(null, source);
            Assert.AreEqual(cleared, state.Version, "clearing an already-empty appearance is free too");
        }

        [Test]
        public void ReapplyingAppearanceLeavesEquipmentAndHidesAlone()
        {
            var state = NewState();
            var source = new DictSource();
            var hair = NewSprite("hair");
            var helm = NewSprite("helm");
            source.Options[(Hair, new DefinitionId("hair-1"))] = new SpriteOption(hair, Color.white);
            state.ApplyAppearance(Map((Hair, "hair-1")), source);
            state.SetEquipment(Head, 30, new[] { new LayerPaint(Helmet, new SpriteOption(helm, Color.white)) }, new[] { Hair });

            state.ApplyAppearance(Map((Hair, "hair-1")), source);
            state.ApplyAppearance(Map((Hair, "hair-1"), (Body, "missing")), source);

            Assert.IsTrue(LayerOf(state, Helmet).Visible, "the equipped helmet still wins its slot");
            Assert.IsFalse(LayerOf(state, Hair).Visible, "and still hides the hair");
            state.ClearEquipment(Head);
            Assert.AreSame(hair, LayerOf(state, Hair).Sprite, "hair returns when the helmet is removed");
        }

        // ---------------------------------------------------------------------------------------------
        // Catalog + source
        // ---------------------------------------------------------------------------------------------

        private SpriteLayerCatalog NewCatalog(string id, DefinitionId slot, params (string option, Sprite sprite)[] options)
        {
            var catalog = ScriptableObject.CreateInstance<SpriteLayerCatalog>();
            made.Add(catalog);
            SetField(catalog, "id", id);
            SetField(catalog, "layerSlot", slot);
            SetField(catalog, "displayName", slot.Value);
            SetField(catalog, "options", options.Select(o => new SpriteOptionEntry(new DefinitionId(o.option), o.option, o.sprite, Color.white)).ToList());
            return catalog;
        }

        [Test]
        public void ACatalogFindsItsOptionsByIdAndReturnsTheFirstOfADuplicate()
        {
            var first = NewSprite("first");
            var second = NewSprite("second");
            var catalog = NewCatalog("catalog-hair", Hair, ("hair-1", first), ("hair-1", second), ("hair-2", NewSprite()));

            Assert.IsTrue(catalog.TryGetOption(new DefinitionId("hair-1"), out var entry));
            Assert.AreSame(first, entry.sprite);
            Assert.IsFalse(catalog.TryGetOption(new DefinitionId("hair-9"), out _));
            Assert.AreEqual(Hair, catalog.LayerSlot);
        }

        [Test]
        public void TheSourceIndexesCatalogsBySlotAndScopesOptionIdsBySlot()
        {
            var rules = new RulesDatabase();
            var hair = NewSprite("hair");
            var helm = NewSprite("helm");
            rules.Register(NewCatalog("catalog-hair", Hair, ("opt-01", hair)));
            rules.Register(NewCatalog("catalog-helmet", Helmet, ("opt-01", helm))); // same option id, different slot
            var source = new SpriteCatalogSource(rules);

            Assert.IsTrue(source.TryGetOption(Hair, new DefinitionId("opt-01"), out var h));
            Assert.AreSame(hair, h.Sprite);
            Assert.IsTrue(source.TryGetOption(Helmet, new DefinitionId("opt-01"), out var m));
            Assert.AreSame(helm, m.Sprite);
            Assert.IsFalse(source.TryGetOption(Weapon, new DefinitionId("opt-01"), out _), "no catalog for that slot");
            Assert.IsFalse(source.TryGetOption(Hair, new DefinitionId("opt-99"), out _));
        }

        [Test]
        public void AnOptionWithNoSpriteDoesNotResolve()
        {
            var rules = new RulesDatabase();
            rules.Register(NewCatalog("catalog-hair", Hair, ("hair-broken", null)));

            Assert.IsFalse(new SpriteCatalogSource(rules).TryGetOption(Hair, new DefinitionId("hair-broken"), out _));
        }

        [Test]
        public void TwoCatalogsOnOneSlotTheSmallerIdWinsInEitherRegistrationOrderWithOneWarning()
        {
            foreach (var smallFirst in new[] { true, false })
            {
                var small = NewCatalog("catalog-a", Hair, ("opt", NewSprite("from-a")));
                var large = NewCatalog("catalog-b", Hair, ("opt", NewSprite("from-b")));
                var rules = new RulesDatabase();
                if (smallFirst) { rules.Register(small); rules.Register(large); }
                else { rules.Register(large); rules.Register(small); }

                LogAssert.Expect(LogType.Warning, new Regex("both claim layer slot 'layer-hair'"));
                var source = new SpriteCatalogSource(rules);

                Assert.IsTrue(source.TryGetOption(Hair, new DefinitionId("opt"), out var result));
                Assert.AreEqual("from-a", result.Sprite.name, $"smaller id wins (registered {(smallFirst ? "first" : "last")})");
            }
        }

        [Test]
        public void RebuildPicksUpCatalogsRegisteredAfterConstruction()
        {
            var rules = new RulesDatabase();
            var source = new SpriteCatalogSource(rules);
            Assert.IsFalse(source.TryGetOption(Hair, new DefinitionId("hair-1"), out _));

            rules.Register(NewCatalog("catalog-hair", Hair, ("hair-1", NewSprite())));
            Assert.IsFalse(source.TryGetOption(Hair, new DefinitionId("hair-1"), out _), "an index is a snapshot until rebuilt");
            source.Rebuild();

            Assert.IsTrue(source.TryGetOption(Hair, new DefinitionId("hair-1"), out _));
        }

        [Test]
        public void ANullRulesDatabaseYieldsAnEmptySource()
        {
            var source = new SpriteCatalogSource(null);
            Assert.IsFalse(source.TryGetOption(Hair, new DefinitionId("x"), out _));
        }

        // ---------------------------------------------------------------------------------------------
        // Validator
        // ---------------------------------------------------------------------------------------------

        private static List<ValidationResult> Run(SpriteLayerCatalog catalog) => SpriteLayerCatalogValidator.Validate(catalog, "Assets/c.asset");

        [Test]
        public void ACleanCatalogIsSilent() =>
            Assert.IsEmpty(Run(NewCatalog("catalog-hair", Hair, ("hair-1", NewSprite()), ("hair-2", NewSprite()))));

        [Test]
        public void AMissingLayerSlotIsAnErrorAndANonDefaultOneIsAWarning()
        {
            Assert.IsTrue(Run(NewCatalog("c1", default, ("a", NewSprite()))).Any(r => r.Severity == ValidationSeverity.Error && r.Field == "layerSlot"));
            Assert.IsTrue(Run(NewCatalog("c2", new DefinitionId("layer-cape"), ("a", NewSprite()))).Any(r => r.Severity == ValidationSeverity.Warning && r.Field == "layerSlot"));
        }

        [Test]
        public void OptionProblemsAreErrorsAndAnEmptyCatalogAndTransparentTintAreWarnings()
        {
            var noSprite = Run(NewCatalog("c1", Hair, ("hair-1", null)));
            Assert.IsTrue(noSprite.Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".sprite")));

            var noId = Run(NewCatalog("c2", Hair, ("", NewSprite())));
            Assert.IsTrue(noId.Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".optionId")));

            var duplicate = Run(NewCatalog("c3", Hair, ("hair-1", NewSprite()), ("hair-1", NewSprite())));
            Assert.AreEqual(1, duplicate.Count(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("more than once")));

            Assert.IsTrue(Run(NewCatalog("c4", Hair)).Any(r => r.Severity == ValidationSeverity.Warning && r.Field == "options"));

            var transparent = NewCatalog("c5", Hair, ("hair-1", NewSprite()));
            SetField(transparent, "options", new List<SpriteOptionEntry> { new(new DefinitionId("hair-1"), "x", NewSprite(), new Color(1f, 1f, 1f, 0f)) });
            Assert.IsTrue(Run(transparent).Any(r => r.Severity == ValidationSeverity.Warning && r.Field.EndsWith(".tint")));
        }

        [Test]
        public void TwoCatalogsClaimingOneSlotAreFlaggedOnBoth()
        {
            var a = NewCatalog("catalog-a", Hair, ("x", NewSprite()));
            var b = NewCatalog("catalog-b", Hair, ("y", NewSprite()));
            var other = NewCatalog("catalog-helmet", Helmet, ("z", NewSprite()));

            var results = SpriteLayerCatalogValidator.ValidateSet(new[] { (a, "a.asset"), (b, "b.asset"), (other, "h.asset") });

            Assert.AreEqual(2, results.Count);
            CollectionAssert.AreEquivalent(new[] { "a.asset", "b.asset" }, results.Select(r => r.AssetPath).ToArray());
            Assert.IsEmpty(SpriteLayerCatalogValidator.ValidateSet(new[] { (a, "a.asset"), (other, "h.asset") }));
        }

        [Test]
        public void ANullCatalogIsToleratedAndSilent() => Assert.IsEmpty(SpriteLayerCatalogValidator.Validate(null, "x"));
    }
}
