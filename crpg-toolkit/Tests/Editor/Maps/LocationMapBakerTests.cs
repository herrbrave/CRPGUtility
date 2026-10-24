using System;
using System.Collections.Generic;
using System.IO;
using CRPG.Editor;
using CRPG.Maps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UCamera = UnityEngine.Camera;

namespace CRPG.Tests.EditMode
{
    /// <summary>The map baker: the framing maths, a real render checked against known world positions (north up, east right, one pixel per fixed number of metres), the parchment pass and the guide grid, and the written assets.</summary>
    public class LocationMapBakerTests
    {
        private const string Folder = "Assets/__MapBakerTest";
        private static readonly Rect Wide = new Rect(-100f, -50f, 200f, 100f);
        private readonly List<UnityEngine.Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            created.Clear();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        // ----- framing ---------------------------------------------------------------------------------------

        [Test]
        public void PixelSizeFollowsTheRectanglesAspectAndRefusesWhatItCannotMatch()
        {
            Assert.AreEqual(new Vector2Int(400, 200), LocationMapBaker.PixelSize(Wide, 400));
            Assert.AreEqual(new Vector2Int(200, 400), LocationMapBaker.PixelSize(new Rect(0, 0, 50, 100), 400));
            Assert.AreEqual(new Vector2Int(2048, 1780), LocationMapBaker.PixelSize(new Rect(-253.4f, -220.25f, 506.8f, 440.5f), 2048), "the town's 506.8 x 440.5 m");
            Assert.Throws<ArgumentException>(() => LocationMapBaker.PixelSize(new Rect(0, 0, 1000, 1f), 64), "a sliver cannot be matched to the aspect tolerance");
            Assert.Throws<ArgumentException>(() => LocationMapBaker.PixelSize(new Rect(0, 0, 0f, 10f), 400));
        }

        [Test]
        public void TheCameraStandsOverTheCentreLookingDownWithNorthUpAndEastRight()
        {
            var go = new GameObject("cam"); created.Add(go);
            var camera = go.AddComponent<UCamera>();
            LocationMapBaker.FrameCamera(camera, Wide, 300f, 900f);
            Assert.IsTrue(camera.orthographic); Assert.AreEqual(50f, camera.orthographicSize, 1e-4f); Assert.AreEqual(2f, camera.aspect, 1e-4f);
            Assert.AreEqual(new Vector3(0f, 300f, 0f), go.transform.position);
            Assert.That(Vector3.Dot(go.transform.forward, Vector3.down), Is.EqualTo(1f).Within(1e-4f), "looks straight down");
            Assert.That(Vector3.Dot(go.transform.up, Vector3.forward), Is.EqualTo(1f).Within(1e-4f), "world +Z is the top of the image");
            Assert.That(Vector3.Dot(go.transform.right, Vector3.right), Is.EqualTo(1f).Within(1e-4f), "world +X is the right of the image");
            Assert.AreEqual(900f, camera.farClipPlane);
        }

        // ----- a real render ---------------------------------------------------------------------------------

        private GameObject Marker(Vector3 position, Color colour)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); created.Add(cube);
            cube.transform.position = position; cube.transform.localScale = new Vector3(20f, 5f, 20f);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader); created.Add(material);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour); if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        [Test]
        public void ARenderPutsWorldPositionsWhereTheDefinitionsBoundsSay()
        {
            // a red block at (+60, -30) and a green one at (-60, +30), both far from the middle
            Marker(new Vector3(60f, 2f, -30f), Color.red); Marker(new Vector3(-60f, 2f, 30f), Color.green);
            var texture = LocationMapBaker.Render(new MapBakeOptions { WorldBoundsXZ = Wide, LongSidePixels = 400, AntiAliasing = 1 }); created.Add(texture);
            Assert.AreEqual(400, texture.width); Assert.AreEqual(200, texture.height);

            Color At(float worldX, float worldZ) => texture.GetPixel(Mathf.RoundToInt((worldX - Wide.xMin) / Wide.width * texture.width), Mathf.RoundToInt((worldZ - Wide.yMin) / Wide.height * texture.height));
            var red = At(60f, -30f); var green = At(-60f, 30f); var empty = At(0f, 0f);
            Assert.IsTrue(red.r > 0.6f && red.g < 0.3f && red.b < 0.3f, "the red block is east and south: right of the middle, below it (texture row 0 is the south edge): " + red);
            Assert.IsTrue(green.g > 0.6f && green.r < 0.3f && green.b < 0.3f, "the green block is west and north: " + green);
            Assert.That(empty.r + empty.g + empty.b, Is.LessThan(0.9f), "bare ground is the background colour");
            // and they are not mirrored
            var mirroredRed = At(-60f, 30f);
            Assert.IsFalse(mirroredRed.r > 0.6f && mirroredRed.g < 0.3f, "north/east are not swapped");
        }

        [Test]
        public void TheCameraCallbackRunsBeforeTheRender_AndTheCullingMaskHidesALayer()
        {
            var block = Marker(new Vector3(0f, 2f, 0f), Color.red); block.layer = 31;
            var called = false;
            var texture = LocationMapBaker.Render(new MapBakeOptions { WorldBoundsXZ = Wide, LongSidePixels = 200, AntiAliasing = 1, CullingMask = ~(1 << 31), ConfigureCamera = c => called = c != null }); created.Add(texture);
            Assert.IsTrue(called);
            var middle = texture.GetPixel(texture.width / 2, texture.height / 2);
            Assert.IsFalse(middle.r > 0.6f && middle.g < 0.3f, "the hidden layer is not drawn");
        }

        // ----- the parchment pass and the guide grid ---------------------------------------------------------

        private static Color32[] Flat(int w, int h, byte v) { var p = new Color32[w * h]; for (var i = 0; i < p.Length; i++) p[i] = new Color32(v, v, v, 255); return p; }

        [Test]
        public void FlatGroundGetsTheParchmentToneAndNoInk()
        {
            var style = new MapStyle { KeepColour = 0f };
            var result = MapStylise.Parchment(Flat(8, 8, 128), 8, 8, style);
            var centre = result[4 * 8 + 4];
            var expected = Color.Lerp(style.Shadow, style.Light, 0.5f);
            Assert.AreEqual(expected.r * 255f, centre.r, 2f); Assert.AreEqual(expected.g * 255f, centre.g, 2f); Assert.AreEqual(expected.b * 255f, centre.b, 2f);
            Assert.AreEqual(result[0].r, result[63].r, "flat ground is flat to the corners (no phantom border ink)");
            Assert.AreEqual(255, centre.a);
        }

        [Test]
        public void ABrightnessEdgeIsInkedAndTurningTheInkOffRemovesIt()
        {
            var w = 16; var h = 8; var pixels = Flat(w, h, 40);
            for (var y = 0; y < h; y++) for (var x = 8; x < w; x++) pixels[y * w + x] = new Color32(220, 220, 220, 255);   // a hard vertical edge
            var inked = MapStylise.Parchment(pixels, w, h, new MapStyle { KeepColour = 0f, EdgeStrength = 1f });
            var plain = MapStylise.Parchment(pixels, w, h, new MapStyle { KeepColour = 0f, EdgeStrength = 0f });
            Assert.Less(inked[4 * w + 7].r, plain[4 * w + 7].r, "the pixels at the edge are darker with ink");
            Assert.AreEqual(plain[4 * w + 2].r, inked[4 * w + 2].r, "away from the edge the ink changes nothing");
        }

        [Test]
        public void TheKeepColourShareLetsTheRendersHueThrough()
        {
            var pixels = new Color32[16]; for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(200, 40, 40, 255);
            var mono = MapStylise.Parchment(pixels, 4, 4, new MapStyle { KeepColour = 0f, EdgeStrength = 0f })[5];
            var kept = MapStylise.Parchment(pixels, 4, 4, new MapStyle { KeepColour = 1f, EdgeStrength = 0f })[5];
            Assert.Greater(kept.r - kept.g, mono.r - mono.g, "more of the red survives");
        }

        [Test]
        public void TheGuideGridDrawsLinesEveryNMetresOnTheRectanglesOwnAxes()
        {
            var texture = new Texture2D(400, 200, TextureFormat.RGBA32, false, false); created.Add(texture);
            var fill = new Color32[400 * 200]; for (var i = 0; i < fill.Length; i++) fill[i] = new Color32(0, 0, 0, 255); texture.SetPixels32(fill); texture.Apply();
            var guide = MapStylise.WithGrid(texture, Wide, 50f); created.Add(guide);
            Assert.Greater(guide.GetPixel(100, 10).r, 0.1f, "x = -50 m is a line");
            Assert.Greater(guide.GetPixel(300, 10).r, 0.1f, "x = +50 m is a line");
            Assert.AreEqual(0f, guide.GetPixel(101, 10).r, 1e-4f, "between lines is untouched");
            Assert.Greater(guide.GetPixel(10, 100).r, 0.1f, "z = 0 is a line");
            Assert.AreEqual(0f, guide.GetPixel(10, 99).r, 1e-4f);
        }

        // ----- the written assets ----------------------------------------------------------------------------

        [Test]
        public void BakeWritesTheStyledImageTheGuideAndAValidDefinitionWithTheExactBounds()
        {
            Marker(new Vector3(60f, 2f, -30f), Color.red);
            var definition = LocationMapBaker.Bake(new MapBakeOptions { WorldBoundsXZ = Wide, LongSidePixels = 256, AntiAliasing = 1 }, Folder, "test-town", "test-town", "Test Town", "Assets/Scenes/Test.unity", new MapStyle());
            Assert.IsNotNull(definition); Assert.AreEqual("test-town", definition.LocationId); Assert.AreEqual(Wide, definition.TextureWorldBoundsXZ); Assert.AreEqual("Assets/Scenes/Test.unity", definition.CaptureSource);
            var problems = new List<string>(); Assert.IsTrue(definition.Validate(problems), string.Join("\n", problems));
            Assert.AreEqual(256, definition.Background.width); Assert.AreEqual(128, definition.Background.height);
            Assert.IsTrue(File.Exists(Path.GetFullPath(Folder + "/test-town_guide.png")), "the guide image is written beside it");
            var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/test-town.png");
            Assert.IsFalse(importer.mipmapEnabled); Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode); Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression); Assert.IsTrue(importer.sRGBTexture);
        }

        [Test]
        public void RebakingUpdatesTheSameDefinitionAssetInPlace()
        {
            var options = new MapBakeOptions { WorldBoundsXZ = Wide, LongSidePixels = 128, AntiAliasing = 1 };
            var first = LocationMapBaker.Bake(options, Folder, "again", "again", "First", "a", null);
            var guid = AssetDatabase.AssetPathToGUID(Folder + "/again.asset");
            var second = LocationMapBaker.Bake(options, Folder, "again", "again", "Second", "b", null);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(Folder + "/again.asset"), "same asset, same guid: references to it survive a rebake");
            Assert.AreEqual("Second", second.Title);
        }

        [Test]
        public void AnInvalidDefinitionIsNeverWritten()
        {
            var texture = new Texture2D(64, 64); created.Add(texture);
            Assert.Throws<InvalidOperationException>(() => LocationMapBaker.WriteDefinition(Folder + "/bad.asset", "bad", "Bad", texture, Wide, "x"), "a square image over a 2:1 rectangle");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<LocationMapDefinition>(Folder + "/bad.asset"));
        }
    }
}
