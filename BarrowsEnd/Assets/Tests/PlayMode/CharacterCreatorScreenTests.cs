using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Presentation.Creator;
using CRPG.Presentation.Sprites;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Tasks 4.2-4.2b: the creator screen against the campaign's REAL content (real ruleset, real sliced sprites, the real creator JSON) on a live
    /// UIDocument, clicked through <see cref="UiClick.Pointer"/> (synthetic pointer events at the element's centre: the spike showed a real mouse does
    /// not reach a panel in a batch run, this does, and it exercises Clickable's own hit-test). Needs graphics (the capture test): run without -nographics.
    /// Rows are Skin, Hair Colour (with a swatch), Hair Style and Class; the class fixes the armor and weapon shown in the preview and the "Gear:" label.
    /// </summary>
    public class CharacterCreatorScreenTests
    {
        private const string PicksPath = "Assets/Content/Creator/CreatorPicks.json";
        private const string TablePath = "Assets/Content/Creator/CreatorClassTable.json";
        private const string RulesetPath = "Assets/Content/Ruleset/BarrowsEndRuleset.asset";
        private const string PanelSettingsPath = "Assets/Content/Creator/creator-panel-settings.asset";
        private const string CharSheet = "Assets/Art/Source/roguelikeChar_transparent.png";
        private const string ArmorSheet = "Assets/Art/Source/barrowsend_armor_transparent.png";
        private static readonly DefinitionId Hair = new("layer-hair");

        private static GameObject sessionHost;
        private static GameSession session;

        private GameObject host;
        private PanelSettings settings;
        private RenderTexture rt;
        private CharacterCreatorScreen screen;
        private CharacterCreatorModel model;
        private UIDocument document;
        private readonly List<CreatorResult> confirmed = new();
        private int cancelled;
        private readonly List<string> reports = new();
        private CreatorPicks picks;
        private CreatorClassTable table;

        [OneTimeSetUp]
        public void StartSession()
        {
#if UNITY_EDITOR
            var provider = UnityEditor.AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(RulesetPath);
            Assert.IsNotNull(provider, "the ruleset asset is authored");
            sessionHost = new GameObject("CreatorScreenSessionHost");
            session = sessionHost.AddComponent<GameSessionHost>().StartSession(provider, new DiceService(new SeededRandomSource(7)), Array.Empty<Type>());
#endif
            picks = CreatorPicks.Parse(File.ReadAllText(PicksPath));
            table = CreatorClassTable.Parse(File.ReadAllText(TablePath));
        }

        [OneTimeTearDown]
        public void EndSession()
        {
            session?.Dispose();
            if (sessionHost != null) UnityEngine.Object.DestroyImmediate(sessionHost);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (screen != null) screen.enabled = false; // unregisters from the static pointer capture
            if (document != null) UIToolkitPointerCapture.Unregister(document);
            if (host != null) UnityEngine.Object.Destroy(host);
            if (settings != null) settings.targetTexture = null;
            if (rt != null) rt.Release();
            if (settings != null) UnityEngine.Object.Destroy(settings);
            screen = null;
            model = null;
            document = null;
            confirmed.Clear();
            reports.Clear();
            cancelled = 0;
            yield return null;
        }

        // ---------------------------------------------------------------------------------------------
        // Fixture
        // ---------------------------------------------------------------------------------------------

        private static CreatorContent RealContent() => CreatorContentLoader.Build(File.ReadAllText(PicksPath), File.ReadAllText(TablePath));

        // A batch-mode PlayMode run has a 640x480 game view, narrower than the layout, so by default the panel renders into a 1280x720 RenderTexture (clicks are in panel
        // coordinates, so they behave exactly as on a real display). Only the pointer-capture tests use the screen-targeted panel: they convert real screen coordinates.
        private void Create(CreatorContent content = null, bool withCancel = false, Func<int> seed = null, bool renderToTexture = true, bool usePlaceholderStyle = true)
        {
#if UNITY_EDITOR
            var authored = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(authored, "the creator panel settings are authored ('Barrows End/Content/Author Creator Preview Scene')");
            settings = UnityEngine.Object.Instantiate(authored); // a COPY: assigning a RenderTexture to the committed asset would persist
            if (renderToTexture)
            {
                rt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                rt.Create();
                settings.targetTexture = rt;
            }
#endif
            host = new GameObject("CreatorScreenTest");
            document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            screen = host.AddComponent<CharacterCreatorScreen>();
            screen.UsePlaceholderStyle = usePlaceholderStyle; // Task 5.3: must be set before Initialize (which builds the layout)
            model = new CharacterCreatorModel(session.RulesDatabase, content ?? RealContent(), reports.Add);
            screen.Initialize(model, result => confirmed.Add(result), withCancel ? () => cancelled++ : null, seed);
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        private VisualElement Root => document.rootVisualElement;
        private Button Btn(string name) => Root.Q<Button>(name);
        private Label Lbl(string name) => Root.Q<Label>(name);

        private void Click(string name)
        {
            var button = Btn(name);
            Assert.IsNotNull(button, $"a button named '{name}'");
            UiClick.Pointer(button);
        }

        private Sprite SpriteOf(string slot)
        {
            Assert.IsTrue(model.Preview.TryGetSlotIndex(new DefinitionId(slot), out var i));
            return screen.Preview.LayerImages[i].sprite;
        }

        private Sprite CatalogSprite(string layerSlot, string optionId)
        {
            Assert.IsTrue(new SpriteCatalogSource(session.RulesDatabase).TryGetOption(new DefinitionId(layerSlot), new DefinitionId(optionId), out var o), optionId);
            return o.Sprite;
        }

        private string OptionOf(string itemId) => picks.armors.Concat(picks.weapons).First(p => p.item == itemId).option;
        private string NameOf(string itemId) => picks.armors.Concat(picks.weapons).First(p => p.item == itemId).name;
        private CreatorClassEntry Entry(string classId) => table.classes.First(c => c.classId == classId);
        private string KitLabel(string classId) => "Gear: " + string.Join(", ", Entry(classId).startingItems.Select(NameOf));

        // ---------------------------------------------------------------------------------------------
        // Structure
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheScreenBuildsTheContractElementsAndBindsThePreviewAtAnIntegralPosition()
        {
            Create();
            yield return Frames(3);

            var screenElement = Root.Q("creator-screen");
            Assert.IsNotNull(screenElement);
            // The screen fills the whole panel (its background covers everything). NB the document root is itself pickable everywhere, so pointer capture cannot tell
            // whether creator-screen fills the panel; the layout bounds can.
            Assert.AreEqual(Root.worldBound.width, screenElement.worldBound.width, 0.01f, "creator-screen spans the panel width");
            Assert.AreEqual(Root.worldBound.height, screenElement.worldBound.height, 0.01f, "creator-screen spans the panel height");
            Assert.Greater(screenElement.worldBound.height, 400f, "and that height is real, not a collapsed auto size");
            Assert.IsNotNull(Root.Q("creator-preview-panel"));
            Assert.IsNotNull(Root.Q("creator-options-panel"));
            foreach (var key in new[] { "layer-body", "hair-color", "layer-hair", "class" })
            {
                Assert.IsNotNull(Root.Q("row-" + key), "row-" + key);
                Assert.IsNotNull(Btn($"row-{key}-prev"), $"row-{key}-prev");
                Assert.IsNotNull(Btn($"row-{key}-next"), $"row-{key}-next");
                Assert.IsNotNull(Lbl($"row-{key}-value"), $"row-{key}-value");
                Assert.IsNotNull(Lbl($"row-{key}-position"), $"row-{key}-position");
            }

            foreach (var gone in new[] { "layer-clothes", "weapon" })
                Assert.IsNull(Root.Q("row-" + gone), $"the class fixes the {gone}: no row");
            Assert.IsNull(Btn("row-layer-hair-page-next"), "no page buttons: no row is longer than 16 options");
            Assert.IsNotNull(Root.Q("row-hair-color-swatch"), "the colour row draws a swatch");
            Assert.IsNull(Root.Q("row-layer-hair-swatch"), "and the style row does not");
            Assert.IsNotNull(Lbl("creator-kit-label"));
            Assert.IsNotNull(Btn("random-button"));
            Assert.IsNotNull(Btn("confirm-button"));
            Assert.IsNull(Btn("cancel-button"), "no Cancel without a callback");
            Assert.IsNotNull(Root.Q("error-label"));

            Assert.AreEqual(model.Preview.SlotCount, screen.Preview.LayerImages.Count, "the preview is bound to the model's state");
            var position = screen.Preview.worldBound.position;
            Assert.AreEqual(Mathf.Round(position.x), position.x, 0.0001f, "an integral x, so 12x blocks never straddle texels");
            Assert.AreEqual(Mathf.Round(position.y), position.y, 0.0001f);
            Assert.AreEqual(16 * 12, screen.Preview.worldBound.width, 0.0001f, "the default preview scale is 12");
        }

        // ---------------------------------------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ClickingNextAdvancesTheStyleRowUpdatesItsLabelsAndTheHairLayer()
        {
            Create();
            yield return Frames(3);
            Assert.AreEqual($"Hair Style: {picks.hairStyleNames[0]}", Lbl("row-layer-hair-value").text);
            Assert.AreEqual("1 / 16", Lbl("row-layer-hair-position").text);

            Click("row-layer-hair-next");
            yield return Frames(1);

            var style = model.RowFor("layer-hair");
            Assert.AreEqual(1, style.Index);
            Assert.AreEqual($"Hair Style: {picks.hairStyleNames[1]}", Lbl("row-layer-hair-value").text);
            Assert.AreEqual("2 / 16", Lbl("row-layer-hair-position").text);
            Assert.AreSame(CatalogSprite("layer-hair", picks.hairColors[0].options[1]), SpriteOf("layer-hair"), "the preview image shows the new style's catalog sprite");
        }

        [UnityTest]
        public IEnumerator PreviousAtTheFirstOptionWrapsToTheLastAndNextWrapsBack()
        {
            Create();
            yield return Frames(3);

            Click("row-layer-hair-prev");
            yield return Frames(1);
            Assert.AreEqual(15, model.RowFor("layer-hair").Index);
            Assert.AreEqual("16 / 16", Lbl("row-layer-hair-position").text);
            StringAssert.StartsWith("Hair Style: Beard", Lbl("row-layer-hair-value").text, "the last styles are the beards");

            Click("row-layer-hair-next");
            yield return Frames(1);
            Assert.AreEqual(0, model.RowFor("layer-hair").Index);
        }

        [UnityTest]
        public IEnumerator ClickingTheColourChangesTheSwatchAndTheHairColourKeepingTheStyle()
        {
            Create();
            yield return Frames(3);
            var swatch = Root.Q("row-hair-color-swatch");
            Assert.AreEqual(CreatorContentLoader.ParseSwatch(picks.hairColors[0].swatch), (Color32)swatch.resolvedStyle.backgroundColor, "the first colour's swatch");
            Assert.AreEqual($"Hair Colour: {picks.hairColors[0].name}", Lbl("row-hair-color-value").text);
            Assert.AreEqual($"1 / {picks.hairColors.Length}", Lbl("row-hair-color-position").text);

            Click("row-layer-hair-next");
            Click("row-layer-hair-next");
            Click("row-hair-color-next");
            yield return Frames(2);

            Assert.AreEqual(1, model.RowFor("hair-color").Index);
            Assert.AreEqual(CreatorContentLoader.ParseSwatch(picks.hairColors[1].swatch), (Color32)swatch.resolvedStyle.backgroundColor, "the swatch follows the colour");
            Assert.AreEqual($"Hair Colour: {picks.hairColors[1].name}", Lbl("row-hair-color-value").text);
            Assert.AreEqual(2, model.RowFor("layer-hair").Index, "the style position is kept");
            Assert.AreEqual($"Hair Style: {picks.hairStyleNames[2]}", Lbl("row-layer-hair-value").text);
            Assert.AreSame(CatalogSprite("layer-hair", picks.hairColors[1].options[2]), SpriteOf("layer-hair"), "the same shape, in the new colour");
        }

        [UnityTest]
        public IEnumerator AClassChangeRewritesTheGearLabelAndTheArmorAndWeaponLayers()
        {
            Create();
            yield return Frames(3);
            Assert.AreEqual("Class: Barbarian", Lbl("row-class-value").text);
            Assert.AreEqual(KitLabel("class-barbarian"), Lbl("creator-kit-label").text);
            Assert.AreSame(CatalogSprite("layer-clothes", OptionOf(Entry("class-barbarian").startingItems[0])), SpriteOf("layer-clothes"), "the barbarian's armor is the clothes layer");

            Click("row-class-next"); // Cleric
            yield return Frames(1);

            Assert.AreEqual("Class: Cleric", Lbl("row-class-value").text);
            Assert.AreEqual(KitLabel("class-cleric"), Lbl("creator-kit-label").text);
            Assert.AreSame(CatalogSprite("layer-clothes", OptionOf(Entry("class-cleric").startingItems[0])), SpriteOf("layer-clothes"));
            Assert.AreSame(CatalogSprite("layer-weapon", OptionOf(Entry("class-cleric").startingItems[1])), SpriteOf("layer-weapon"));
        }

        [UnityTest]
        public IEnumerator AClassWithNoKitShowsGearNoneAndAnUnclothedUnarmedPreview()
        {
            var real = RealContent();
            var cleric = real.Classes.First(c => c.ClassId.Value == "class-cleric");
            var bare = new CreatorClassSpec(cleric.ClassId, new CreatorOption[0], cleric.ScoreOrder, cleric.ChoiceDefaults);
            var content = new CreatorContent(real.Race, real.Background, real.Level, real.StandardArray, real.AppearanceRows, new[] { bare });
            Create(content);
            yield return Frames(3);

            Assert.AreEqual("Gear: none", Lbl("creator-kit-label").text);
            Assert.IsNull(SpriteOf("layer-clothes"));
            Assert.IsNull(SpriteOf("layer-weapon"));
        }

        [UnityTest]
        public IEnumerator RandomizeUsesTheInjectedSeedAndSelectsWhatTheModelWouldForThatSeed()
        {
            Create(seed: () => 77);
            yield return Frames(3);
            var expected = new CharacterCreatorModel(session.RulesDatabase, RealContent());
            expected.Randomize(77);

            Click("random-button");
            yield return Frames(1);

            CollectionAssert.AreEqual(expected.Rows.Select(r => r.Index).ToArray(), model.Rows.Select(r => r.Index).ToArray());
            Assert.AreEqual($"Class: {expected.ClassRow.Selected.Name}", Lbl("row-class-value").text, "the labels follow");
            Assert.AreEqual($"Hair Colour: {expected.RowFor("hair-color").Selected.Name}", Lbl("row-hair-color-value").text);
            Assert.AreSame(CatalogSprite("layer-hair", expected.RowFor("layer-hair").Selected.Id.Value), SpriteOf("layer-hair"));
            Assert.AreEqual("Gear: " + string.Join(", ", expected.StartingItems.Select(i => i.Name)), Lbl("creator-kit-label").text);
        }

        // ---------------------------------------------------------------------------------------------
        // Confirm / Cancel
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ConfirmBuildsOnceHandsOverTheCompletePlanAndDisablesEverythingUntilShownAgain()
        {
            Create();
            yield return Frames(3);
            Click("row-hair-color-next");
            Click("row-layer-hair-next");
            Click("row-class-next");
            yield return Frames(1);

            Click("confirm-button");
            yield return Frames(1);

            Assert.AreEqual(1, confirmed.Count);
            var result = confirmed[0];
            Assert.IsTrue(result.Plan.IsComplete);
            Assert.AreEqual(model.ClassRow.Selected.Id, result.ClassId);
            CollectionAssert.AreEqual(Entry("class-cleric").startingItems, result.StartingItemIds.Select(i => i.Value).ToArray(), "the cleric's armor and weapon travel in the result");
            Assert.AreEqual(model.RowFor("layer-hair").Selected.Id, result.Plan.Appearance[Hair]);
            Assert.AreEqual(picks.hairColors[1].options[1], result.Plan.Appearance[Hair].Value);
            Assert.IsTrue(screen.IsFinished);
            foreach (var button in Root.Query<Button>().ToList()) Assert.IsFalse(button.enabledSelf, button.name);

            var hairIndex = model.RowFor("layer-hair").Index;
            Click("confirm-button"); // one-shot: a second click must not build or hand over again
            Click("row-layer-hair-next"); // and a disabled row button does nothing
            yield return Frames(1);
            Assert.AreEqual(1, confirmed.Count);
            Assert.AreEqual(hairIndex, model.RowFor("layer-hair").Index);

            screen.Show(); // re-arms
            yield return Frames(1);
            Assert.IsFalse(screen.IsFinished);
            Click("confirm-button");
            yield return Frames(1);
            Assert.AreEqual(2, confirmed.Count);
        }

        [UnityTest]
        public IEnumerator ConfirmShowsTheErrorAndHandsOverNothingWhenTheClassCannotBuildThenTheNextChangeClearsIt()
        {
            var real = RealContent();
            var fighter = real.Classes.First(c => c.ClassId.Value == "class-fighter");
            var broken = new CreatorClassSpec(fighter.ClassId, fighter.StartingItems, fighter.ScoreOrder, new CreatorChoiceSpec[0]); // no default for the fighting style
            var content = new CreatorContent(real.Race, real.Background, real.Level, real.StandardArray, real.AppearanceRows, new[] { broken });
            Create(content);
            yield return Frames(3);
            var error = Root.Q<Label>("error-label");
            Assert.AreEqual(DisplayStyle.None, error.resolvedStyle.display, "no error at first");

            Click("confirm-button");
            yield return Frames(1);

            Assert.AreEqual(0, confirmed.Count);
            StringAssert.Contains("choose-fighting-style", error.text);
            Assert.AreEqual(DisplayStyle.Flex, error.resolvedStyle.display);
            Assert.IsTrue(Btn("confirm-button").enabledSelf, "still interactive");
            Assert.IsFalse(screen.IsFinished);

            Click("row-layer-hair-next");
            yield return Frames(1);
            Assert.AreEqual(DisplayStyle.None, error.resolvedStyle.display, "the next change clears the error");
        }

        [UnityTest]
        public IEnumerator CancelExistsOnlyWithACallbackAndFiresOnce()
        {
            Create(withCancel: true);
            yield return Frames(3);
            Assert.IsNotNull(Btn("cancel-button"));

            Click("cancel-button");
            Click("cancel-button");
            yield return Frames(1);

            Assert.AreEqual(1, cancelled);
            Assert.AreEqual(0, confirmed.Count);
        }

        // ---------------------------------------------------------------------------------------------
        // Pointer capture, Show/Hide, enable/disable
        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheScreenIsAModalCaptureSurfaceThatHideReleasesAndShowRestores()
        {
            Create(renderToTexture: false);
            yield return Frames(3);
            var capture = new UIToolkitPointerCapture();
            var emptyCorner = new Vector2(2f, Screen.height - 2f); // Input System style: bottom-left origin
            var confirmCenter = Btn("confirm-button").worldBound.center;
            var overConfirm = new Vector2(confirmCenter.x, Screen.height - confirmCenter.y);

            Assert.IsTrue(capture.IsPointerOverUI(overConfirm), "over the confirm button");
            Assert.IsTrue(capture.IsPointerOverUI(emptyCorner), "creator-screen fills the panel, so even an empty corner is captured");

            screen.Hide();
            yield return Frames(1);
            Assert.IsFalse(capture.IsPointerOverUI(emptyCorner), "Hide releases the world");
            Assert.IsFalse(capture.IsPointerOverUI(overConfirm));

            // Hide hides the root AND unregisters; either alone would pass the checks above (a display:none root cannot be picked). Show the root again behind
            // Hide's back: the world must STILL be released, which only the unregistration can guarantee.
            Root.style.display = DisplayStyle.Flex;
            yield return Frames(2);
            Assert.IsFalse(capture.IsPointerOverUI(emptyCorner), "Hide also drops the pointer-capture registration");
            Root.style.display = DisplayStyle.None;
            yield return Frames(1);

            screen.Show();
            yield return Frames(2);
            Assert.IsTrue(capture.IsPointerOverUI(emptyCorner), "Show captures again");

            screen.enabled = false;
            yield return Frames(1);
            Assert.IsFalse(capture.IsPointerOverUI(emptyCorner), "a disabled screen is unregistered");
        }

        [UnityTest]
        public IEnumerator ADisableEnableCycleRebuildsTheLayoutAndItStillWorks()
        {
            Create();
            yield return Frames(3);
            Click("row-layer-hair-next");
            yield return Frames(1);

            screen.enabled = false;
            yield return Frames(1);
            screen.enabled = true;
            yield return Frames(3);

            Assert.IsNotNull(Btn("confirm-button"), "the layout was rebuilt from stored state");
            Assert.AreEqual("2 / 16", Lbl("row-layer-hair-position").text, "and reflects the model as it was");
            Click("row-layer-hair-next");
            yield return Frames(1);
            Assert.AreEqual("3 / 16", Lbl("row-layer-hair-position").text, "one subscription, not two");
            Click("confirm-button");
            yield return Frames(1);
            Assert.AreEqual(1, confirmed.Count);
        }

        [UnityTest]
        public IEnumerator ReinitializingWithANewModelDetachesTheOldOne()
        {
            Create();
            yield return Frames(3);
            var oldModel = model;
            model = new CharacterCreatorModel(session.RulesDatabase, RealContent());
            screen.Initialize(model, result => confirmed.Add(result));
            yield return Frames(3);

            // A stale subscription would clear the error label (OnModelChanged) when the OLD model changes: plant a marker in it first.
            var error = Root.Q<Label>("error-label");
            error.text = "marker";
            error.style.display = DisplayStyle.Flex;
            oldModel.RowFor("layer-hair").Advance(5);
            yield return Frames(1);
            Assert.AreEqual("marker", error.text, "the old model no longer reaches the screen");
            Assert.AreEqual("1 / 16", Lbl("row-layer-hair-position").text);
            Click("row-layer-hair-next");
            yield return Frames(1);
            Assert.AreEqual("2 / 16", Lbl("row-layer-hair-position").text);
        }

        // ---------------------------------------------------------------------------------------------
        // Rendering
        // ---------------------------------------------------------------------------------------------

        private static Texture2D LoadPng(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), path);
            return texture;
        }

        [UnityTest]
        public IEnumerator ThePreviewOnTheRenderedScreenIsPixelExactWithTheClassArmorAndTheWholeScreenIsCaptured()
        {
            Create();
            yield return Frames(3);
            Click("row-hair-color-next");
            Click("row-layer-hair-next");
            Click("row-class-next"); // Cleric in its white tabard
            yield return Frames(6);

            AssertPixelExactPreview("creator-screen.png");
        }

        // Task 5.3 (Opus review, should-fix): no test exercised a flag-off (real-skin) creator at all before this
        // -- every other test in this fixture uses the default UsePlaceholderStyle=true. Re-runs the exact same
        // pixel-exactness check with the wood panel behind the preview instead of the placeholder's own flat
        // colour, and confirms the row arrows carry no inline size/text once the flag is off (the Task 5.3 review's
        // own blocker: RowButton used to set both unconditionally, which would have stretched be-arrow's 22x21 art
        // to a non-integer scale with a stray glyph drawn over it).
        [UnityTest]
        public IEnumerator WithTheFlagOffTheRowArrowsHaveNoInlineSizeAndThePreviewStaysPixelExactBehindTheWoodPanel()
        {
            Create(usePlaceholderStyle: false);
            yield return Frames(3);

            var prev = Btn("row-class-prev");
            Assert.AreEqual(string.Empty, prev.text, "the background image is the glyph, not a text character");
            // Confirmed empirically (not assumed): a Button property that was never assigned inline reads as
            // StyleKeyword.Null in this Unity version, not .Undefined.
            Assert.AreEqual(StyleKeyword.Null, prev.style.width.keyword, "no inline width override -- be-arrow's own 22px must be free to apply");
            Assert.AreEqual(StyleKeyword.Null, prev.style.height.keyword, "no inline height override -- be-arrow's own 21px must be free to apply");
            Assert.IsTrue(prev.ClassListContains("be-arrow"));
            Assert.IsTrue(prev.ClassListContains("be-part--arrow-left"));

            Click("row-hair-color-next");
            Click("row-layer-hair-next");
            Click("row-class-next");
            yield return Frames(6);

            AssertPixelExactPreview("creator-screen-skinned.png");
        }

        private void AssertPixelExactPreview(string debugFileName)
        {
            var kenney = LoadPng(CharSheet);
            var armorSheet = LoadPng(ArmorSheet);
            var layers = new List<EffectiveLayer>();
            model.Preview.CopyVisibleTo(layers);

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            read.Apply();
            RenderTexture.active = previous;

            var dir = Path.Combine(Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), "sp");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, debugFileName), read.EncodeToPNG());

            var origin = screen.Preview.worldBound.position;
            const int scale = 12;
            var checkedCells = 0;
            var mismatches = 0;
            for (var cy = 0; cy < 16; cy++)
            for (var cx = 0; cx < 16; cx++)
            {
                Color32? expected = null; // the top-most visible layer with an opaque pixel here
                foreach (var layer in layers)
                {
                    var parts = layer.Sprite.name.Split('_'); // <sheet>_transparent_<col>_<row>
                    var col = int.Parse(parts[parts.Length - 2]);
                    var row = int.Parse(parts[parts.Length - 1]);
                    var sheet = layer.Sprite.name.StartsWith("barrowsend_armor") ? armorSheet : kenney; // the class armors are generated sprites on their own sheet
                    Color32 pixel = sheet.GetPixel(col * 17 + cx, sheet.height - 1 - (row * 17 + cy));
                    if (pixel.a == 255) expected = pixel;
                }

                if (expected == null) continue; // transparent: the panel background shows, not asserted
                checkedCells++;
                for (var dy = 0; dy < scale; dy++)
                for (var dx = 0; dx < scale; dx++)
                {
                    var uiX = (int)origin.x + cx * scale + dx;
                    var uiY = (int)origin.y + cy * scale + dy;
                    var actual = (Color32)read.GetPixel(uiX, read.height - 1 - uiY);
                    if (Mathf.Abs(actual.r - expected.Value.r) > 2 || Mathf.Abs(actual.g - expected.Value.g) > 2 || Mathf.Abs(actual.b - expected.Value.b) > 2) mismatches++;
                }
            }

            UnityEngine.Object.Destroy(kenney);
            UnityEngine.Object.Destroy(armorSheet);
            UnityEngine.Object.Destroy(read);
            Assert.Greater(checkedCells, 80, "the dressed character covers a good part of the cell");
            Assert.AreEqual(0, mismatches, "every opaque preview pixel on the rendered screen equals the source art at 12x (no blur, no offset), armor and trousers included");
        }
    }
}
