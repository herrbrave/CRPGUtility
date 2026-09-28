using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BarrowsEnd.Editor;
using CRPG.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 5.0's permanent EditMode content tests (Docs/Plans/2D-Campaign-Phase5-UIReskin.md, Tests 1-7).
    /// EditMode has no layout/style pass, so these check content/config/text, never resolvedStyle (the 4.1
    /// finding) -- pixel/state assertions live in the PlayMode spikes and SkinGalleryScreenTests instead.</summary>
    public class UiSkinContentTests
    {
        private const string SkinPath = "Assets/UI/Skin/barrowsend-skin.uss";
        private const string SlicesPath = UiSkinBuilder.SlicesUssPath;
        private const string SourceDir = UiSkinBuilder.SourceDir;

        // Test 1 -------------------------------------------------------------------------------------------

        [Test]
        public void EveryManifestPartExistsOnDiskAndEveryUrlInTheSkinResolvesToAPart()
        {
            var manifest = UiSkinBuilder.LoadManifest();
            foreach (var entry in manifest.parts)
                Assert.IsTrue(File.Exists($"{UiSkinBuilder.PartsDir}/{entry.name}.png"), $"part '{entry.name}' exists on disk");

            var urls = Regex.Matches(File.ReadAllText(SkinPath), "url\\(\"([^\"]+)\"\\)")
                .Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(File.ReadAllText(SlicesPath), "url\\(\"([^\"]+)\"\\)").Select(m => m.Groups[1].Value))
                .Where(u => u.StartsWith("/Assets/"))
                .Select(u => u.TrimStart('/'));

            foreach (var url in urls) Assert.IsTrue(File.Exists(url), $"{url} referenced by url() exists");
        }

        // Test 2 -------------------------------------------------------------------------------------------

        [Test]
        public void EveryPartPngIsImportedPointNoMipsUncompressedClampNonReadable()
        {
            var manifest = UiSkinBuilder.LoadManifest();
            foreach (var entry in manifest.parts)
            {
                var path = $"{UiSkinBuilder.PartsDir}/{entry.name}.png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(importer, $"{path} has a TextureImporter");
                Assert.AreEqual(TextureImporterType.Default, importer.textureType, path);
                Assert.AreEqual(FilterMode.Point, importer.filterMode, path);
                Assert.IsFalse(importer.mipmapEnabled, path);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression, path);
                Assert.IsTrue(importer.sRGBTexture, path);
                Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode, path);
                Assert.IsTrue(importer.alphaIsTransparency, path);
                Assert.IsFalse(importer.isReadable, path);
            }
        }

        [Test]
        public void NothingUnderTheUiSourceTreeIsSpriteTyped()
        {
            // D1a guard: Assets/Art/UI/Source/ must stay outside BarrowsEndSetup.SliceAll's scan root
            // (Assets/Art/Source/) so the Kenney UI pack copy is never force-converted to a 16px-grid sprite sheet.
            Assert.IsFalse(SourceDir.StartsWith("Assets/Art/Source"), "the UI source tree must not be nested under the slicer's scan root");
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SourceDir });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.AreNotEqual(TextureImporterType.Sprite, importer.textureType, $"{path} must not be Sprite-typed");
            }
        }

        // Test 3 -------------------------------------------------------------------------------------------

        [Test]
        public void BuildingTwiceProducesByteIdenticalOutput()
        {
            var before = UiSkinBuilder.LoadManifest().parts.ToDictionary(p => p.name, p => File.ReadAllBytes($"{UiSkinBuilder.PartsDir}/{p.name}.png"));
            UiSkinBuilder.BuildFromManifest();
            var after = UiSkinBuilder.LoadManifest().parts.ToDictionary(p => p.name, p => File.ReadAllBytes($"{UiSkinBuilder.PartsDir}/{p.name}.png"));
            foreach (var name in before.Keys) CollectionAssert.AreEqual(before[name], after[name], $"'{name}' is byte-identical after a no-op rebuild");
        }

        [Test]
        public void RefusesAMissingSourcePart()
        {
            var manifest = new SkinPartManifest { parts = new[] { new SkinPartEntry { name = "x", source = "does-not-exist.png", slice = new[] { 0, 0, 0, 0 } } } };
            Assert.Throws<System.InvalidOperationException>(() => UiSkinBuilder.BuildFromManifest(manifest));
        }

        [Test]
        public void RefusesADuplicatePartName()
        {
            var manifest = new SkinPartManifest
            {
                parts = new[]
                {
                    new SkinPartEntry { name = "dup", source = "panel_brown.png", slice = new[] { 0, 0, 0, 0 } },
                    new SkinPartEntry { name = "dup", source = "panel_beige.png", slice = new[] { 0, 0, 0, 0 } },
                },
            };
            Assert.Throws<System.InvalidOperationException>(() => UiSkinBuilder.BuildFromManifest(manifest));
        }

        [Test]
        public void RefusesASliceThatLeavesNoStretchableMiddle()
        {
            // panel_brown.png is 100x100; left+right = 100 leaves zero stretchable width.
            var manifest = new SkinPartManifest { parts = new[] { new SkinPartEntry { name = "x", source = "panel_brown.png", slice = new[] { 50, 0, 50, 0 } } } };
            Assert.Throws<System.InvalidOperationException>(() => UiSkinBuilder.BuildFromManifest(manifest));
        }

        // Test 4 -------------------------------------------------------------------------------------------

        [Test]
        public void SlicesLiveOnlyInTheGeneratedFragmentNeverHandWrittenExceptTheDocumentedPressedException()
        {
            var handWritten = File.ReadAllText(SkinPath);
            var sliceLines = Regex.Matches(handWritten, @"-unity-slice-(left|top|right|bottom)\s*:\s*\d+");
            // The one documented exception (file header + D3): a button's :active state repeats its already-
            // generated pressed part's slice because USS can't otherwise make :active win over the normal rule.
            var pressedExceptionCount = Regex.Matches(handWritten, @"be-part--button-(primary|secondary|square):active").Count * 4; // 4 slice properties each
            Assert.AreEqual(pressedExceptionCount, sliceLines.Count, "no hand-written -unity-slice-* outside the documented :active pressed-state exception");

            var generated = File.ReadAllText(SlicesPath);
            var manifest = UiSkinBuilder.LoadManifest();
            foreach (var entry in manifest.parts.Where(p => p.slice != null && p.slice.Any(s => s != 0)))
                Assert.IsTrue(generated.Contains($"-unity-slice-left: {entry.slice[0]}"), $"'{entry.name}' left slice matches the manifest");
        }

        // Test 5 -------------------------------------------------------------------------------------------

        [Test]
        public void TheTwoNewPanelSettingsCarryTheThemeConstantPixelSizeAllFiltersAndTheRightSortingOrder()
        {
            AssertPanelSettings(BarrowsEndUiAuthor.HudPanelSettingsPath, BarrowsEndUiAuthor.HudSortingOrder);
            AssertPanelSettings(BarrowsEndUiAuthor.GalleryPanelSettingsPath, BarrowsEndUiAuthor.GallerySortingOrder);
        }

        private static void AssertPanelSettings(string path, int expectedSortingOrder)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            Assert.IsNotNull(settings, path);
            Assert.IsNotNull(settings.themeStyleSheet, $"{path}: theme assigned");
            Assert.AreEqual(PanelScaleMode.ConstantPixelSize, settings.scaleMode, path);
            Assert.AreEqual(1f, settings.scale, path);
            Assert.AreEqual(expectedSortingOrder, settings.sortingOrder, path);
            Assert.AreEqual((DynamicAtlasFilters)UiPanelSettingsAuthor.AllAtlasFilters, settings.dynamicAtlasSettings.activeFilters, path);

            // Idempotent GUID: re-authoring must not create a new asset.
            var guidBefore = AssetDatabase.AssetPathToGUID(path);
            UiPanelSettingsAuthor.Author(path, expectedSortingOrder, settings.themeStyleSheet);
            Assert.AreEqual(guidBefore, AssetDatabase.AssetPathToGUID(path), $"{path}: GUID is stable across a re-author");
        }

        [Test]
        public void TheThemeImportsDefaultFirstAndSkinSecond()
        {
            var text = File.ReadAllText(BarrowsEndUiAuthor.ThemePath);
            var defaultIndex = text.IndexOf("unity-theme://default", System.StringComparison.Ordinal);
            var skinIndex = text.IndexOf("barrowsend-skin.uss", System.StringComparison.Ordinal);
            Assert.Greater(defaultIndex, -1, "imports the default theme");
            Assert.Greater(skinIndex, -1, "imports the skin");
            Assert.Less(defaultIndex, skinIndex, "default theme is imported BEFORE the skin (D2: the skin must win ties by import order)");
        }

        [Test]
        public void TheGatesOwnPanelSettingsStayUntouched()
        {
            // D6: 5.0 must not switch the SHIPPED gate panel settings over to the new theme/scale mode.
            // Renamed from "GateAndCreatorPanelSettingsAreUntouchedBy50" (Task 5.3): the creator's OWN panel
            // settings are no longer untouched -- Task 5.3 deliberately opted them into the skin theme (see
            // CreatorScreenAuthoringTests for that guarantee instead). This test only ever asserted the gate's
            // asset; only its stale name claimed otherwise.
            var gate = AssetDatabase.LoadAssetAtPath<PanelSettings>(PhaseTwoGateSceneAuthor.PanelSettingsPath);
            Assert.IsNotNull(gate);
            // Correction (verified directly against the asset, 2026-09-22): the shipped gate is ConstantPhysicalSize,
            // not ScaleWithScreenSize as both the Opus review and the plan's D6 first assumed -- this Unity
            // version's PanelScaleMode enum order is not {ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize}
            // as guessed; m_ScaleMode: 1 in the real .asset YAML maps to ConstantPhysicalSize. The design point (5.0
            // must not switch the shipped gate panel settings) is unaffected -- only the exact mode name was wrong.
            Assert.AreEqual(PanelScaleMode.ConstantPhysicalSize, gate.scaleMode, "the gate's panel settings must stay untouched (ConstantPhysicalSize) -- nothing in this phase opts it in");
        }

        // Test 6 -------------------------------------------------------------------------------------------

        [Test]
        public void EveryBePrimitiveHasARuleAndPartyViewsClassesAppearInTheSkin()
        {
            var skin = File.ReadAllText(SkinPath);
            var primitives = new[]
            {
                "be-screen", "be-panel", "be-button", "be-button--round", "be-icon", "be-arrow", "be-bar", "be-bar__fill",
                "be-slot", "be-slot--empty", "be-slot--selected", "be-label", "be-label--title", "be-label--muted", "be-label--danger",
                "be-scroll", "be-tooltip",
            };
            foreach (var primitive in primitives) Assert.IsTrue(skin.Contains($".{primitive}"), $"skin has a rule for .{primitive}");

            foreach (var state in new[] { ":hover", ":active", ":disabled", ":focus" })
                Assert.IsTrue(skin.Contains($"{state}"), $"skin has at least one rule using {state}");

            // PartyView's own emitted classes (built directly, not string-guessed) all appear in a selector.
            var partyClasses = new[] { "party-panel", "party-title", "party-roster", "party-card", "party-card--active", "party-card--selected", "party-card-name", "party-card-hp" };
            foreach (var cls in partyClasses) Assert.IsTrue(skin.Contains($".{cls}"), $"skin has a rule for PartyView's .{cls}");

            // Task 5.3: the creator/title screens' own re-skin work reuses the be-* primitives above directly
            // (already covered) rather than introducing new creator-*/shell-* component rules -- shell-title-label
            // is the one genuine exception (the title heading's larger size, since be-label--title's on-dark ink
            // is wrong for a label sitting inside a wood panel, not on the bare be-screen backdrop).
            Assert.IsTrue(skin.Contains(".shell-title-label"), "skin has a rule for the title heading's font size");
        }

        // Test 7 -------------------------------------------------------------------------------------------

        [Test]
        public void NoHexColourLiteralAppearsOutsideRoot()
        {
            var skin = File.ReadAllText(SkinPath);
            var rootEnd = skin.IndexOf("}", skin.IndexOf(":root", System.StringComparison.Ordinal), System.StringComparison.Ordinal);
            var afterRoot = skin.Substring(rootEnd);
            Assert.IsFalse(Regex.IsMatch(afterRoot, "#[0-9a-fA-F]{3,8}\\b"), "no hex colour literal outside :root (component rules use rgb()/var() only)");
        }
    }
}
