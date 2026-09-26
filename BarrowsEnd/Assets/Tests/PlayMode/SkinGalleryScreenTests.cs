using System.Collections;
using System.IO;
using System.Linq;
using BarrowsEnd.Shell;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 5.0, Steps 4/5: the gallery actually builds a real PartyView bound to a real PartyPresenter
    /// (not a mock), and every card resolves the skin (proving the re-skin contract end to end, not just that the
    /// parts render in isolation). Needs graphics: run without -nographics.</summary>
    public class SkinGalleryScreenTests
    {
        private GameObject root;
        private RenderTexture rt;
        private PanelSettings panelSettings;

        [TearDown]
        public void Cleanup()
        {
            if (root != null) Object.Destroy(root);
            if (panelSettings != null) panelSettings.targetTexture = null;
            if (rt != null) rt.Release();
        }

        [UnityTest]
        public IEnumerator TheGalleryBuildsARealPartyViewWithFourSkinnedCardsAndNoException()
        {
            const int Size = 900;
            root = new GameObject("GallerySmoke");
            rt = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1f;
            panelSettings.targetTexture = rt;
            panelSettings.clearColor = true;
            panelSettings.colorClearValue = new Color(0.09f, 0.1f, 0.12f, 1f);
#if UNITY_EDITOR
            panelSettings.themeStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/Skin/BarrowsEndTheme.tss");
            Assert.IsNotNull(panelSettings.themeStyleSheet);
#endif
            var atlas = panelSettings.dynamicAtlasSettings;
            atlas.activeFilters = DynamicAtlasFilters.Readability | DynamicAtlasFilters.Size | DynamicAtlasFilters.Format | DynamicAtlasFilters.ColorSpace | DynamicAtlasFilters.FilterMode;
            panelSettings.dynamicAtlasSettings = atlas;

            var document = root.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var screen = root.AddComponent<SkinGalleryScreen>();

            for (var i = 0; i < 6; i++) yield return null;

            var partyRoster = document.rootVisualElement.Q(name: "party-roster");
            Assert.IsNotNull(partyRoster, "the gallery's PartyView built and named its roster container");
            var cards = partyRoster.Children().Where(c => c.name.StartsWith("party-card-")).ToList();
            Assert.AreEqual(4, cards.Count, "all four fake party members got a card");

            var barsBuilt = cards.Count(c => c.Q(name: "hp-bar") != null);
            Assert.AreEqual(4, barsBuilt, "every card built an hp-bar child (flag off)");

            // Corvin (0/10 HP) must hide its fill (0% -- the S1/step-3 empty-bar rule), the rest must show one.
            var corvinCard = cards.First(c => c.Q<Label>(name: "party-card-name").text == "Corvin");
            var corvinFill = corvinCard.Q(name: "hp-bar__fill");
            Assert.AreEqual(DisplayStyle.None, corvinFill.resolvedStyle.display, "0 HP hides the fill rather than showing a 0-width sliced bar (bar caps can't fit under 18px)");

            var aldricCard = cards.First(c => c.Q<Label>(name: "party-card-name").text == "Aldric");
            var aldricFill = aldricCard.Q(name: "hp-bar__fill");
            Assert.AreEqual(DisplayStyle.Flex, aldricFill.resolvedStyle.display, "14/14 HP shows a full fill");

            var read = ReadRt(Size);
            var dir = Path.Combine(System.Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), "ui-skin-spike");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "gallery.png"), read.EncodeToPNG());

            // Gate review round 2 ("the health bars are extending outside of the character card"): a card's
            // hp-bar must never be wider than the card's own padded content area. Caught a real bug once already
            // (a card's declared 140px width was flex-shrinking to 88px inside a too-narrow demo container while
            // the bar stayed a fixed 120px) -- kept as a permanent regression guard, not just a one-off diagnostic.
            foreach (var card in cards)
            {
                var bar = card.Q(name: "hp-bar");
                if (bar == null) continue;
                var contentWidth = card.resolvedStyle.width - card.resolvedStyle.paddingLeft - card.resolvedStyle.paddingRight;
                Assert.LessOrEqual(bar.resolvedStyle.width, contentWidth, $"{card.Q<Label>(name: "party-card-name").text}'s hp-bar ({bar.resolvedStyle.width}px) must fit inside the card's content width ({contentWidth}px)");
            }

            // Gate review round 2 ("the text is even less legible in the disabled button"): a real WCAG-style
            // contrast-ratio check against an actually-rendered pixel, not an eyeball guess -- round 1's fix
            // (dropping this rule's own opacity) measured 1.05 and still failed live, because UI Toolkit's
            // built-in theme applies its own default opacity to any disabled control regardless of this rule.
            var disabled = document.rootVisualElement.Query<Button>(className: "be-button").ToList().First(b => !b.enabledSelf);
            var bgSample = SampleAt(read, new Vector2(disabled.worldBound.xMin + 12, disabled.worldBound.center.y)); // near the edge, clear of the centred label's own glyph pixels
            var contrast = ContrastRatio(bgSample, disabled.resolvedStyle.color);
            Assert.GreaterOrEqual(contrast, 4.5f, $"a disabled button's text must meet WCAG AA contrast (>=4.5:1) against its own background, not just look different from the enabled state (measured bg~{bgSample}, text={disabled.resolvedStyle.color}, contrast={contrast})");

            Object.Destroy(read);

            LogAssert.NoUnexpectedReceived();
        }

        private static Color SampleAt(Texture2D read, Vector2 panelPos)
        {
            var x = Mathf.Clamp(Mathf.RoundToInt(panelPos.x), 0, read.width - 1);
            var y = Mathf.Clamp(read.height - 1 - Mathf.RoundToInt(panelPos.y), 0, read.height - 1);
            return read.GetPixel(x, y);
        }

        /// <summary>WCAG-style relative luminance contrast ratio between two colours -- a real number, not an eyeball
        /// guess, so a proposed fix can be checked before another round-trip to the user.</summary>
        private static float ContrastRatio(Color bg, Color text)
        {
            float Rel(Color c)
            {
                float Lin(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
                return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
            }

            var l1 = Rel(bg) + 0.05f;
            var l2 = Rel(text) + 0.05f;
            return l1 > l2 ? l1 / l2 : l2 / l1;
        }

        private Texture2D ReadRt(int size)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            return read;
        }
    }
}
