using System.Collections;
using System.IO;
using System.Linq;
using CRPG.Core;
using CRPG.Presentation.Creator;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 4.1 spike (findings recorded in Docs/Plans/2D-Campaign-Phase4-CharacterCreator.md): does a stack of UI Toolkit Images render the REAL sliced
    /// Kenney character sprites pixel-exactly at an integer scale? The panel renders into a RenderTexture; the read-back is compared, pixel by pixel,
    /// against the same layers alpha-composited on the CPU from the SOURCE PNG (the imported sprites are not readable, so the file is loaded directly,
    /// as BarrowsEndSetup does). Any bilinear sampling, atlas re-filtering or off-by-one mapping shows up as a mismatch. Needs graphics: run it without -nographics.
    /// </summary>
    public class LayeredPreviewSpikeTests
    {
        private const string CharSheet = "Assets/Art/Source/roguelikeChar_transparent.png";
        private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";
        private const int Scale = 4;
        private const int Cell = 16, Pitch = 17;
        private static readonly Color Background = new(0.2f, 0.25f, 0.3f, 1f);

        private GameObject root;
        private RenderTexture rt;
        private PanelSettings panelSettings;

        [TearDown]
        public void Cleanup()
        {
            if (root != null) Object.Destroy(root);
            if (panelSettings != null) panelSettings.targetTexture = null;
            if (rt != null) rt.Release();
            if (panelSettings != null) Object.Destroy(panelSettings);
        }

        private static Sprite Load(int col, int row)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAllAssetsAtPath(CharSheet).OfType<Sprite>().First(s => s.name == $"roguelikeChar_transparent_{col}_{row}");
#else
            return null;
#endif
        }

        private static Texture2D LoadSourcePng()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(CharSheet)), "the source PNG loads");
            return tex;
        }

        /// <summary>The 16x16 cell's pixels, top row first (image space), straight from the source PNG.</summary>
        private static Color32[,] SourceCell(Texture2D sheet, int col, int row)
        {
            var cell = new Color32[Cell, Cell];
            var yTop = row * Pitch;
            for (var y = 0; y < Cell; y++)
            for (var x = 0; x < Cell; x++)
                cell[y, x] = sheet.GetPixel(col * Pitch + x, sheet.height - 1 - (yTop + y));
            return cell;
        }

        [UnityTest]
        public IEnumerator ALayeredPreviewRendersTheRealSpritesPixelExactAtAnIntegerScale()
        {
            const int Size = 128;
            var sheet = LoadSourcePng();
            var layers = new[] { (slot: "layer-body", col: 1, row: 1), (slot: "layer-clothes", col: 10, row: 0), (slot: "layer-hair", col: 20, row: 4) };

            // The expected image: the layers alpha-composited in draw order over the panel's clear colour, at 4x nearest-neighbour.
            var expectedCells = layers.Select(l => SourceCell(sheet, l.col, l.row)).ToArray();
            foreach (var cell in expectedCells)
                foreach (var p in cell)
                    Assert.IsTrue(p.a == 0 || p.a == 255, "the spike compares exact pixels, so the art must be fully opaque or fully transparent");

            var composite = new Color32[Cell, Cell];
            var background = (Color32)Background;
            for (var y = 0; y < Cell; y++)
            for (var x = 0; x < Cell; x++)
            {
                var pixel = background;
                foreach (var cell in expectedCells)
                    if (cell[y, x].a == 255) pixel = cell[y, x];
                composite[y, x] = pixel;
            }

            root = new GameObject("PreviewSpike");
            rt = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1f;
            panelSettings.targetTexture = rt;
            panelSettings.clearColor = true;
            panelSettings.colorClearValue = Background;
#if UNITY_EDITOR
            panelSettings.themeStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            Assert.IsNotNull(panelSettings.themeStyleSheet, "the runtime theme is assigned explicitly (the themeless fallback is editor-only)");
#endif
            var atlas = panelSettings.dynamicAtlasSettings;
            atlas.activeFilters = DynamicAtlasFilters.Readability | DynamicAtlasFilters.Size | DynamicAtlasFilters.Format | DynamicAtlasFilters.ColorSpace | DynamicAtlasFilters.FilterMode;
            panelSettings.dynamicAtlasSettings = atlas;

            var document = root.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;

            var state = new SpriteLayerState(SpriteLayerCompositor.DefaultSlots());
            foreach (var l in layers) state.SetAppearance(new DefinitionId(l.slot), new SpriteOption(Load(l.col, l.row), Color.white));

            var preview = new LayeredSpritePreview { Scale = Scale };
            preview.style.position = Position.Absolute;
            preview.style.left = 0;
            preview.style.top = 0;
            document.rootVisualElement.Add(preview);
            preview.Bind(state);

            for (var i = 0; i < 6; i++) yield return null; // WaitForEndOfFrame is not evoked in batch mode; the panel renders into the RenderTexture each frame

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            read.Apply();
            RenderTexture.active = previous;

            // Settled by the first run of this spike: the panel's top-left is the RenderTexture's top-left (no flip). The flipped count is
            // still logged so a future Unity change that flips it is visible at a glance.
            var mismatchesTopDown = CountMismatches(read, composite, flipped: false);
            var mismatchesBottomUp = CountMismatches(read, composite, flipped: true);
            Debug.Log($"[PreviewSpike] mismatched pixels of {Cell * Scale * Cell * Scale}: panel-top-at-texture-top={mismatchesTopDown}, panel-top-at-texture-bottom={mismatchesBottomUp}");

            var dir = Path.Combine(System.Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), "sp");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "creator-preview-spike.png"), read.EncodeToPNG());
            Object.Destroy(read);
            Object.Destroy(sheet);

            Assert.AreEqual(0, mismatchesTopDown,
                "the read-back must equal the CPU composite of the source PNG pixel for pixel (any blur, atlas re-filtering or misalignment would show as mismatches)");
        }

        /// <summary>Compares the top-left (Cell*Scale)^2 region of the read-back with the composite scaled 4x nearest. Tolerance 2/255 per channel.</summary>
        private static int CountMismatches(Texture2D read, Color32[,] composite, bool flipped)
        {
            var size = Cell * Scale;
            var bad = 0;
            for (var uiY = 0; uiY < size; uiY++)
            for (var uiX = 0; uiX < size; uiX++)
            {
                var expected = composite[uiY / Scale, uiX / Scale];
                var texY = flipped ? uiY : read.height - 1 - uiY;
                var actual = (Color32)read.GetPixel(uiX, texY);
                if (Mathf.Abs(actual.r - expected.r) > 2 || Mathf.Abs(actual.g - expected.g) > 2 || Mathf.Abs(actual.b - expected.b) > 2) bad++;
            }

            return bad;
        }
    }
}
