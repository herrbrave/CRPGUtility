using System.IO;
using System.Linq;
using BarrowsEnd.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 4.2b: the five generated class armors (<c>barrowsend_armor_transparent.png</c>, made by <c>make_class_armor.py</c>: a Kenney top composited over generated
    /// trousers and boots painted on the body's own leg pixels). The sheet has no pants layer to draw on, so each armor is one complete outfit; these tests pin that
    /// the trousers really are there, that they never overwrite the top, and that the five looks differ. The Python script's own <c>--check</c> mode proves the PNG is
    /// what the script generates (Unity cannot run Python).
    /// </summary>
    public class ClassArmorArtTests
    {
        private const string KenneySheet = BarrowsEndCatalogAuthor.KenneyChar;
        private const string ArmorSheet = BarrowsEndCatalogAuthor.BarrowsEndArmor;

        // Class order: barbarian, cleric, fighter, rogue, wizard. Mirrors the TOPS table in make_class_armor.py.
        private static readonly (int col, int row)[] Tops = { (6, 4), (16, 1), (7, 4), (15, 5), (17, 2) };

        private Texture2D kenney;
        private Texture2D armor;

        [OneTimeSetUp]
        public void Load()
        {
            kenney = Read(KenneySheet);
            armor = Read(ArmorSheet);
        }

        [OneTimeTearDown]
        public void Unload()
        {
            if (kenney != null) Object.DestroyImmediate(kenney);
            if (armor != null) Object.DestroyImmediate(armor);
        }

        private static Texture2D Read(string path)
        {
            Assert.IsTrue(File.Exists(path), $"{path} is missing (run make_class_armor.py)");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), path);
            return texture;
        }

        private static Color32 Pixel(Texture2D sheet, int col, int row, int x, int y) => sheet.GetPixel(col * 17 + x, sheet.height - 1 - (row * 17 + y));

        [Test]
        public void TheSheetHasFiveSixteenPixelCellsSlicedAtSixteenPixelsPerUnit()
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(ArmorSheet).OfType<Sprite>().OrderBy(s => s.name).ToList();

            Assert.AreEqual(5, sprites.Count, "one sprite per class");
            for (var i = 0; i < 5; i++)
            {
                Assert.AreEqual($"barrowsend_armor_transparent_{i}_0", sprites[i].name);
                Assert.AreEqual(16f, sprites[i].rect.width);
                Assert.AreEqual(16f, sprites[i].rect.height);
                Assert.AreEqual(16f, sprites[i].pixelsPerUnit);
            }
        }

        [Test]
        public void EveryArmorCoversEveryLegPixelOfEveryBody()
        {
            // The body sprites draw bare legs on rows 11-15; the armor must paint trousers/boots over exactly those pixels so no class is left without pants.
            for (var skin = 0; skin < 4; skin++)
            for (var y = 11; y < 16; y++)
            for (var x = 0; x < 16; x++)
            {
                if (Pixel(kenney, 1, skin, x, y).a == 0) continue;
                for (var n = 0; n < 5; n++)
                    Assert.AreEqual(255, Pixel(armor, n, 0, x, y).a, $"armor {n}: leg pixel ({x},{y}) of skin {skin} is covered");
            }
        }

        [Test]
        public void WhereverTheKenneyTopIsOpaqueTheArmorIsExactlyTheTop()
        {
            for (var n = 0; n < 5; n++)
            {
                var (col, row) = Tops[n];
                var opaque = 0;
                for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                {
                    var top = Pixel(kenney, col, row, x, y);
                    if (top.a != 255) continue;
                    opaque++;
                    var actual = Pixel(armor, n, 0, x, y);
                    Assert.AreEqual((top.r, top.g, top.b, top.a), (actual.r, actual.g, actual.b, actual.a), $"armor {n}: pixel ({x},{y}) is the top's, never overwritten by the trousers");
                }

                Assert.Greater(opaque, 40, $"armor {n}: the top is a real garment");
            }
        }

        [Test]
        public void NoArmorPaintsAboveTheShouldersOrOutsideTheBody()
        {
            // The trousers are generated only on the body's leg pixels, so the armor's silhouette never exceeds the union of its top and the body's legs.
            for (var n = 0; n < 5; n++)
            {
                var (col, row) = Tops[n];
                for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                {
                    var inTop = Pixel(kenney, col, row, x, y).a > 0;
                    var inLegs = y >= 11 && Pixel(kenney, 1, 0, x, y).a > 0;
                    if (!inTop && !inLegs) Assert.AreEqual(0, Pixel(armor, n, 0, x, y).a, $"armor {n}: stray pixel at ({x},{y})");
                }
            }
        }

        [Test]
        public void TheFiveArmorsAreVisiblyDifferent()
        {
            var signatures = Enumerable.Range(0, 5)
                .Select(n => string.Join(",", Enumerable.Range(0, 256).Select(i => { var c = Pixel(armor, n, 0, i % 16, i / 16); return $"{c.r:x2}{c.g:x2}{c.b:x2}{c.a:x2}"; })))
                .ToList();

            CollectionAssert.AllItemsAreUnique(signatures, "five different looks, one per class");
        }
    }
}
