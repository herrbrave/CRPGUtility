using System.Collections;
using System.IO;
using System.Linq;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 3.0 spike (findings recorded in Docs/Plans/2D-Campaign-Phase3-CameraAndWorld.md): can a Tilemap lie on the XZ plane under the
    /// pitch-90 orthographic camera, does it render upright, and how do tilemap tiles sort against depth-sorted characters? Everything is built at
    /// runtime, so the test needs no authored assets. Captures go to %TEMP%/sp for a human to look at.
    /// </summary>
    public class TilemapSpikeTests
    {
        private const string Sheet = "Assets/Art/Source/roguelikeSheet_transparent.png";
        private const string CharSheet = "Assets/Art/Source/roguelikeChar_transparent.png";

        // Built far away on X (Z stays small: the sorting contract only distinguishes |Z| <= 256) so scenes left loaded by earlier tests, which occupy
        // the same small coordinates, can never render into these captures.
        private const float Ox = 5000f;

        private GameObject root;
        private Camera cam;
        private RenderTexture rt;

        [TearDown]
        public void Cleanup()
        {
            if (rt != null) { cam.targetTexture = null; rt.Release(); }
            if (root != null) Object.Destroy(root);
        }

        private static Sprite Load(string sheetPath, string prefix, int col, int row)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().First(s => s.name == $"{prefix}_{col}_{row}");
#else
            return null;
#endif
        }

        private static Tile MakeTile(Sprite sprite, GameObject prefab = null)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            if (prefab != null) tile.gameObject = prefab;
            return tile;
        }

        /// <summary>A Grid on the XZ plane with a Tilemap whose sprites face up, on the given sorting layer/order.</summary>
        private Tilemap MakeTilemap(string name, string layer, int order, TilemapRenderer.Mode mode, bool xzOrientation = true, bool swizzle = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var tilemap = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingLayerName = layer;
            renderer.sortingOrder = order;
            renderer.mode = mode;
            if (xzOrientation) tilemap.orientation = Tilemap.Orientation.XZ;
            return tilemap;
        }

        private void MakeRoot(bool swizzle = true)
        {
            root = new GameObject("TilemapSpike");
            root.transform.position = new Vector3(Ox, 0f, 0f);
            var grid = root.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 1f);
            if (swizzle) grid.cellSwizzle = GridLayout.CellSwizzle.XZY;
        }

        private void MakeCamera(int scale = 3, int height = 1080)
        {
            var camGo = new GameObject("SpikeCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(root.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.07f, 0.1f);
            rt = new RenderTexture(height * 16 / 9, height, 24);
            cam.targetTexture = rt;
            cam.orthographicSize = height / (2f * 16f * scale);
            camGo.transform.SetPositionAndRotation(new Vector3(Ox + 6f, 20f, 5f), Quaternion.Euler(90f, 0f, 0f));
        }

        private Color32 PixelAt(Vector3 world)
        {
            var screen = cam.WorldToScreenPoint(world);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(screen.x, screen.y, 1, 1), 0, 0);
            tex.Apply();
            var pixel = tex.GetPixel(0, 0);
            Object.Destroy(tex);
            RenderTexture.active = previous;
            return pixel;
        }

        private void Save(string name)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            var dir = Path.Combine(System.Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath(), "sp");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            Object.Destroy(tex);
            RenderTexture.active = previous;
        }

        private static bool IsGreenish(Color c) => c.g > c.r + 0.08f && c.g > c.b + 0.08f;

        // ----------------------------------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnXzTilemapWithBothSwizzleAndOrientationRendersFlatAndUpright()
        {
            MakeRoot();
            MakeCamera();
            var grass = MakeTile(Load(Sheet, "roguelikeSheet_transparent", 5, 0));
            var tree = MakeTile(Load(Sheet, "roguelikeSheet_transparent", 13, 9)); // crown at the top of the sprite, trunk at the bottom
            var ground = MakeTilemap("Ground", "Ground", 0, TilemapRenderer.Mode.Chunk);
            for (var x = 0; x < 12; x++)
            for (var z = 0; z < 8; z++)
                ground.SetTile(new Vector3Int(x, z, 0), grass);
            var props = MakeTilemap("Props", "World", 0, TilemapRenderer.Mode.Chunk);
            props.SetTile(new Vector3Int(6, 5, 0), tree);
            yield return null;
            yield return null;

            // Where the tile centre really is in the world (this is the answer to "what does XZY swizzle do to cell coordinates").
            var centre = ground.GetCellCenterWorld(new Vector3Int(3, 2, 0));
            Debug.Log($"[Spike] cell (3,2) centre in world = {centre}; tilemap.orientation={ground.orientation}, swizzle={ground.layoutGrid.cellSwizzle}");
            Assert.AreEqual(Ox + 3.5f, centre.x, 0.01f);
            Assert.AreEqual(2.5f, centre.z, 0.01f, "cell y maps to world Z with XZY swizzle");

            Assert.IsTrue(IsGreenish(PixelAt(centre)), "grass shows under the camera at the cell centre (the tilemap renders on XZ, facing up)");

            var treeCentre = props.GetCellCenterWorld(new Vector3Int(6, 5, 0));
            var top = PixelAt(treeCentre + new Vector3(0f, 0f, 0.3f));
            var bottom = PixelAt(treeCentre + new Vector3(0f, 0f, -0.42f));
            Debug.Log($"[Spike] tree top pixel {top}, bottom pixel {bottom}");
            Assert.IsTrue(IsGreenish(top), "the tree crown is toward +Z (screen up): the sprite is upright, not mirrored or rotated");
            Assert.Greater(bottom.r, bottom.g, "and the trunk (brown) is toward -Z");
            Save("spike_xz_flat.png");
        }

        [UnityTest]
        public IEnumerator SwizzleAloneWithoutOrientationDoesNotLieFlat()
        {
            MakeRoot(swizzle: true);
            MakeCamera();
            var grass = MakeTile(Load(Sheet, "roguelikeSheet_transparent", 5, 0));
            var ground = MakeTilemap("Ground", "Ground", 0, TilemapRenderer.Mode.Chunk, xzOrientation: false);
            for (var x = 0; x < 12; x++)
            for (var z = 0; z < 8; z++)
                ground.SetTile(new Vector3Int(x, z, 0), grass);
            yield return null;
            yield return null;

            var centre = ground.GetCellCenterWorld(new Vector3Int(3, 2, 0));
            var pixel = PixelAt(centre);
            Debug.Log($"[Spike] swizzle only: centre {centre}, pixel {pixel}, orientation {ground.orientation}");
            // The pitch-90 camera sees a sprite standing in the XY plane edge-on: expect background, not grass.
            Assert.IsFalse(IsGreenish(pixel), "with swizzle alone the sprites still stand upright in local XY (edge-on from above): both settings are required");
            Save("spike_swizzle_only.png");
        }

        [UnityTest]
        public IEnumerator AWholeTilemapHasOneSortingOrderSoItCannotInterleaveWithACharacterBehindAndInFrontOfAProp()
        {
            MakeRoot();
            MakeCamera();
            var grass = MakeTile(Load(Sheet, "roguelikeSheet_transparent", 5, 0));
            var tree = MakeTile(Load(Sheet, "roguelikeSheet_transparent", 13, 9));
            var ground = MakeTilemap("Ground", "Ground", 0, TilemapRenderer.Mode.Chunk);
            for (var x = 0; x < 12; x++)
            for (var z = 0; z < 8; z++)
                ground.SetTile(new Vector3Int(x, z, 0), grass);
            var props = MakeTilemap("PropsTilemap", "World", 0, TilemapRenderer.Mode.Individual);
            props.SetTile(new Vector3Int(4, 4, 0), tree);

            // Two characters (sprite + depth sorter): one in front of the tree (lower Z), one behind it (higher Z).
            var body = Load(CharSheet, "roguelikeChar_transparent", 0, 0);
            var front = MakeSortedSprite("Front", body, new Vector3(Ox + 4.5f, 0.02f, 3.5f));
            var behind = MakeSortedSprite("Behind", body, new Vector3(Ox + 4.5f, 0.02f, 5.5f));
            for (var i = 0; i < 4; i++) yield return null;

            var treeOrder = props.GetComponent<TilemapRenderer>().sortingOrder;
            Debug.Log($"[Spike] tilemap order {treeOrder}; front char order {front.sortingOrder}; behind char order {behind.sortingOrder}");
            var frontAbove = front.sortingOrder > treeOrder;
            var behindAbove = behind.sortingOrder > treeOrder;
            Assert.AreEqual(frontAbove, behindAbove,
                "both characters land on the SAME side of the tilemap's single order: a tilemap prop can be in front of both or behind both, never between them");
            Save("spike_tilemap_sorting.png");
        }

        [UnityTest]
        public IEnumerator ATileWithAGameObjectInstantiatesASortedPropThatDoesInterleave()
        {
            MakeRoot();
            MakeCamera();
            var grass = MakeTile(Load(Sheet, "roguelikeSheet_transparent", 5, 0));
            var ground = MakeTilemap("Ground", "Ground", 0, TilemapRenderer.Mode.Chunk);
            for (var x = 0; x < 12; x++)
            for (var z = 0; z < 8; z++)
                ground.SetTile(new Vector3Int(x, z, 0), grass);

            // The prop template the tile instantiates: a flat sprite on the World layer with the prop-lane depth sorter.
            // FINDING: the tile copies the template's ACTIVE state, so a template inactive in the scene yields inactive instances. A real prefab ASSET is
            // active, so this stand-in is active too, parked far outside the view and destroyed once the instances exist.
            var template = new GameObject("TreeTemplate");
            template.transform.position = new Vector3(Ox + 900f, 0f, 100f);
            var sr = template.AddComponent<SpriteRenderer>();
            sr.sprite = Load(Sheet, "roguelikeSheet_transparent", 13, 9);
            sr.sortingLayerName = SpriteSortingContract.WorldLayer;
            template.AddComponent<SpriteDepthSorter>();
            var tree = MakeTile(sr.sprite, template);
            tree.sprite = null; // the tile itself draws nothing: the instantiated object is the visual
            var props = MakeTilemap("Props", "World", 0, TilemapRenderer.Mode.Chunk);
            props.SetTile(new Vector3Int(4, 4, 0), tree);
            props.SetTile(new Vector3Int(8, 4, 0), tree);

            var body = Load(CharSheet, "roguelikeChar_transparent", 0, 0);
            var front = MakeSortedSprite("Front", body, new Vector3(Ox + 4.5f, 0.02f, 3.5f));
            var behind = MakeSortedSprite("Behind", body, new Vector3(Ox + 4.5f, 0.02f, 5.5f));
            for (var i = 0; i < 4; i++) yield return null;

            Object.Destroy(template);
            yield return null;
            var instance = props.GetInstantiatedObject(new Vector3Int(4, 4, 0));
            Assert.IsNotNull(instance, "the Tilemap instantiated the prop object for the painted cell in Play mode");
            Debug.Log($"[Spike] instance at {instance.transform.position}, euler {instance.transform.eulerAngles}, active {instance.activeSelf}");
            Assert.AreEqual(Ox + 4.5f, instance.transform.position.x, 0.01f);
            Assert.AreEqual(4.5f, instance.transform.position.z, 0.01f, "at the cell centre on the XZ plane");

            var treeRenderer = instance.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(treeRenderer);
            Assert.Greater(front.sortingOrder, treeRenderer.sortingOrder, "a character in front (lower Z) draws over the tree");
            Assert.Less(behind.sortingOrder, treeRenderer.sortingOrder, "a character behind (higher Z) draws under the tree");
            Save("spike_gameobject_props.png");
        }

        [UnityTest]
        public IEnumerator ATileTransformMatrixRotatesAPaintedTileWithinTheXzPlane()
        {
            MakeRoot();
            MakeCamera();
            var grass = MakeTile(Load(Sheet, "roguelikeSheet_transparent", 5, 0));
            var tree = MakeTile(Load(Sheet, "roguelikeSheet_transparent", 13, 9));
            var ground = MakeTilemap("Ground", "Ground", 0, TilemapRenderer.Mode.Chunk);
            for (var x = 0; x < 12; x++)
            for (var z = 0; z < 8; z++)
                ground.SetTile(new Vector3Int(x, z, 0), grass);
            var props = MakeTilemap("Props", "World", 0, TilemapRenderer.Mode.Chunk);
            var cell = new Vector3Int(6, 5, 0);
            props.SetTile(cell, tree);
            props.SetTransformMatrix(cell, Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f)));
            yield return null;
            yield return null;

            var centre = props.GetCellCenterWorld(cell);
            var trunkBefore = PixelAt(centre + new Vector3(0f, 0f, -0.42f));
            var trunkSides = new[] { PixelAt(centre + new Vector3(0.42f, 0f, 0f)), PixelAt(centre + new Vector3(-0.42f, 0f, 0f)) };
            Debug.Log($"[Spike] rotated tree: -Z {trunkBefore}, +X {trunkSides[0]}, -X {trunkSides[1]}");
            Assert.IsFalse(trunkBefore.r > trunkBefore.g + 0.05f * 255 && trunkBefore.b < 120, "the brown trunk is no longer at -Z");
            Assert.IsTrue(trunkSides.Any(c => c.r > c.g + 0.05f * 255 && c.b < 120), "the trunk moved to an X side: the matrix rotates the tile within the XZ plane");
            Save("spike_rotated_tile.png");
        }

        private SpriteRenderer MakeSortedSprite(string name, Sprite sprite, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = SpriteSortingContract.WorldLayer;
            go.AddComponent<SpriteDepthSorter>();
            return sr;
        }
    }
}
