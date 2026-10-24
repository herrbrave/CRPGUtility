using UnityEngine;

namespace CRPG.Editor
{
    /// <summary>How the parchment pass looks. Plain numbers, so a game can tune its map without touching the code.</summary>
    public sealed class MapStyle
    {
        /// <summary>The tone the darkest and the lightest ground take.</summary>
        public Color Shadow = new Color(0.22f, 0.16f, 0.10f, 1f), Light = new Color(0.97f, 0.90f, 0.72f, 1f);

        /// <summary>0 = a pure parchment monochrome, 1 = the render's own colours: how much of the original hue survives.</summary>
        [Range(0f, 1f)] public float KeepColour = 0.55f;

        /// <summary>How hard the outlines are inked (0 = none): the darkening at a brightness edge.</summary>
        [Range(0f, 2f)] public float EdgeStrength = 0.9f;
        public Color Ink = new Color(0.12f, 0.08f, 0.05f, 1f);

        /// <summary>Contrast around the middle grey before toning (1 = unchanged).</summary>
        public float Contrast = 1.3f;
    }

    /// <summary>
    /// The stylisation of a baked map and the guide image's grid, as pure pixel passes over a texture (no GPU, no scene): a warm parchment tone from the render's brightness with a share of its colour, and inked edges from a Sobel filter on brightness so streets and walls keep crisp
    /// outlines at the minimap's magnification. Nothing is done at play time.
    /// </summary>
    public static class MapStylise
    {
        public static Texture2D Parchment(Texture2D source, MapStyle style)
        {
            var pixels = source.GetPixels32();
            var result = Parchment(pixels, source.width, source.height, style);
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false) { name = source.name + " (parchment)" };
            texture.SetPixels32(result); texture.Apply(false, false);
            return texture;
        }

        /// <summary>The pass over a pixel array (row 0 at the bottom, as Texture2D keeps them).</summary>
        public static Color32[] Parchment(Color32[] pixels, int width, int height, MapStyle style)
        {
            var luminance = new float[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) luminance[i] = (0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b) / 255f;
            var result = new Color32[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    var l = Mathf.Clamp01((luminance[i] - 0.5f) * style.Contrast + 0.5f);
                    var toned = Color.Lerp(style.Shadow, style.Light, l);
                    var original = new Color(pixels[i].r / 255f, pixels[i].g / 255f, pixels[i].b / 255f, 1f);
                    var colour = Color.Lerp(toned, original, style.KeepColour);
                    if (style.EdgeStrength > 0f) colour = Color.Lerp(colour, style.Ink, Mathf.Clamp01(Edge(luminance, width, height, x, y) * style.EdgeStrength));
                    result[i] = new Color32((byte)Mathf.RoundToInt(colour.r * 255f), (byte)Mathf.RoundToInt(colour.g * 255f), (byte)Mathf.RoundToInt(colour.b * 255f), 255);
                }
            }

            return result;
        }

        /// <summary>The Sobel gradient magnitude of the brightness at a pixel (0 on flat ground), the clamp-to-edge border.</summary>
        public static float Edge(float[] luminance, int width, int height, int x, int y)
        {
            float L(int px, int py) => luminance[Mathf.Clamp(py, 0, height - 1) * width + Mathf.Clamp(px, 0, width - 1)];
            var gx = -L(x - 1, y - 1) - 2f * L(x - 1, y) - L(x - 1, y + 1) + L(x + 1, y - 1) + 2f * L(x + 1, y) + L(x + 1, y + 1);
            var gy = -L(x - 1, y - 1) - 2f * L(x, y - 1) - L(x + 1, y - 1) + L(x - 1, y + 1) + 2f * L(x, y + 1) + L(x + 1, y + 1);
            return Mathf.Sqrt(gx * gx + gy * gy);
        }

        /// <summary>A copy with thin lines every <paramref name="metres"/> metres of the world rectangle (the guide image: a painter's reference at the exact rectangle; the lines sit on multiples of the grid from the rectangle's own corner so they can be counted).</summary>
        public static Texture2D WithGrid(Texture2D source, Rect worldBoundsXZ, float metres)
        {
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false) { name = source.name + " (guide)" };
            copy.SetPixels32(source.GetPixels32());
            if (metres > 0f)
            {
                var pixelsPerMetre = source.width / worldBoundsXZ.width;
                var line = new Color32(255, 255, 255, 255); var major = new Color32(255, 220, 90, 255);
                for (var i = 1; i * metres < worldBoundsXZ.width; i++) Column(copy, Mathf.RoundToInt(i * metres * pixelsPerMetre), i % 4 == 0 ? major : line);
                for (var j = 1; j * metres < worldBoundsXZ.height; j++) Row(copy, Mathf.RoundToInt(j * metres * pixelsPerMetre), j % 4 == 0 ? major : line);
            }

            copy.Apply(false, false);
            return copy;
        }

        private static void Column(Texture2D t, int x, Color32 c) { if (x < 0 || x >= t.width) return; for (var y = 0; y < t.height; y++) t.SetPixel(x, y, Color.Lerp(t.GetPixel(x, y), (Color)c, 0.45f)); }
        private static void Row(Texture2D t, int y, Color32 c) { if (y < 0 || y >= t.height) return; for (var x = 0; x < t.width; x++) t.SetPixel(x, y, Color.Lerp(t.GetPixel(x, y), (Color)c, 0.45f)); }
    }
}
