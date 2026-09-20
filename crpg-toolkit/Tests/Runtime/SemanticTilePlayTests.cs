using System.Collections;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D campaign Task 3.2: SemanticTile on a REAL Tilemap on the XZ plane, and StaticSpriteSorter. Sorted-object tiles must become one flat, correctly
    /// ordered object each with the tile's sprite and nothing drawn twice; flat tiles stay on the tilemap; static objects sort once and go quiet.</summary>
    public class SemanticTilePlayTests
    {
        // Far away on X so scenes left loaded by other tests cannot interfere; Z stays small (the sorting contract clamps beyond 256).
        private const float Ox = 7000f;

        private readonly System.Collections.Generic.List<Object> made = new();
        private GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.Destroy(root);
            foreach (var o in made) if (o != null) Object.Destroy(o);
            made.Clear();
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

        /// <summary>The shared sorted-object prefab stand-in: active (a real prefab asset is), parked far out of view, destroyed once instances exist.</summary>
        private GameObject NewObjectTemplate()
        {
            var template = new GameObject("ObjectTemplate");
            template.transform.position = new Vector3(Ox + 900f, 0f, 100f);
            template.AddComponent<SpriteRenderer>();
            template.AddComponent<StaticSpriteSorter>();
            return template;
        }

        private SemanticTile NewTile(TileSemantic semantic, GameObject template, Sprite sprite)
        {
            var tile = ScriptableObject.CreateInstance<SemanticTile>();
            tile.Configure(semantic, sprite, template);
            made.Add(tile);
            return tile;
        }

        private Tilemap NewTilemap(string name)
        {
            if (root == null)
            {
                root = new GameObject("Root");
                root.transform.position = new Vector3(Ox, 0f, 0f);
                var grid = root.AddComponent<Grid>();
                grid.cellSwizzle = GridLayout.CellSwizzle.XZY;
            }

            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var tilemap = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();
            tilemap.orientation = Tilemap.Orientation.XZ;
            return tilemap;
        }

        [UnityTest]
        public IEnumerator ASortedObjectTileBecomesOneFlatObjectWithTheTilesSpriteAndTheTilemapDoesNotDrawItToo()
        {
            var template = NewObjectTemplate();
            var propSprite = NewSprite("tree");
            var prop = NewTile(TileSemantic.Prop, template, propSprite);
            var props = NewTilemap("Props");
            var cell = new Vector3Int(4, 5, 0);
            props.SetTile(cell, prop);
            yield return null;
            Object.Destroy(template);
            yield return null;
            yield return null;

            var instance = props.GetInstantiatedObject(cell);
            Assert.IsNotNull(instance, "the Tilemap instantiated the shared object at runtime");
            Assert.AreEqual(Ox + 4.5f, instance.transform.position.x, 0.01f);
            Assert.AreEqual(5.5f, instance.transform.position.z, 0.01f);
            Assert.AreEqual(90f, instance.transform.eulerAngles.x, 0.01f, "flat on the XZ plane");
            Assert.AreSame(propSprite, instance.GetComponent<SpriteRenderer>().sprite, "StartUp handed it this tile's sprite");
            Assert.IsNull(props.GetSprite(cell), "the tilemap itself draws nothing for a sorted-object tile while playing, so nothing is drawn twice");
        }

        [UnityTest]
        public IEnumerator AFlatTileStaysOnTheTilemapWithItsSpriteAndInstantiatesNothing()
        {
            var ground = NewTile(TileSemantic.Ground, NewObjectTemplate(), NewSprite("grass"));
            var tilemap = NewTilemap("Ground");
            var cell = new Vector3Int(2, 2, 0);
            tilemap.SetTile(cell, ground);
            yield return null;
            yield return null;

            Assert.AreSame(ground.sprite, tilemap.GetSprite(cell), "flat tiles are drawn by the tilemap renderer");
            Assert.IsNull(tilemap.GetInstantiatedObject(cell));
        }

        [UnityTest]
        public IEnumerator InstantiatedObjectsSortAgainstACharacterByZAndThenStopUpdating()
        {
            var template = NewObjectTemplate();
            var tree = NewTile(TileSemantic.Prop, template, NewSprite("tree"));
            var props = NewTilemap("Props");
            props.SetTile(new Vector3Int(4, 4, 0), tree);
            yield return null;
            Object.Destroy(template);

            // A character sprite with the moving sorter: one in front of the tree (lower Z), one behind it (higher Z).
            SpriteRenderer Character(string name, float z)
            {
                var go = new GameObject(name);
                go.transform.SetPositionAndRotation(new Vector3(Ox + 4.5f, 0.02f, z), Quaternion.Euler(90f, 0f, 0f));
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = NewSprite(name);
                sr.sortingLayerName = SpriteSortingContract.WorldLayer;
                go.AddComponent<SpriteDepthSorter>();
                made.Add(go);
                return sr;
            }

            var front = Character("Front", 3.5f);
            var behind = Character("Behind", 5.5f);
            yield return null;
            yield return null;

            var instance = props.GetInstantiatedObject(new Vector3Int(4, 4, 0));
            var treeRenderer = instance.GetComponent<SpriteRenderer>();
            Assert.AreEqual(SpriteSortingContract.OrderFor(SpriteSortingContract.BaseOrder(4.5f), SpriteSortingContract.PropBias), treeRenderer.sortingOrder, "the prop lane at its own Z");
            Assert.Greater(front.sortingOrder, treeRenderer.sortingOrder, "in front of the tree");
            Assert.Less(behind.sortingOrder, treeRenderer.sortingOrder, "behind the tree");
            Assert.AreEqual(SpriteSortingContract.WorldLayer, treeRenderer.sortingLayerName);

            var sorter = instance.GetComponent<StaticSpriteSorter>();
            Assert.IsFalse(sorter.enabled, "a static sorter switches itself off after sorting once");
            var orderBefore = treeRenderer.sortingOrder;
            instance.transform.position += new Vector3(0f, 0f, 3f);
            yield return null;
            yield return null;
            Assert.AreEqual(orderBefore, treeRenderer.sortingOrder, "and never re-sorts on its own");

            sorter.Resort();
            Assert.AreNotEqual(orderBefore, treeRenderer.sortingOrder, "unless asked to");
        }

        [UnityTest]
        public IEnumerator ASixtyByFortyMapOfPropsInstantiatesQuicklyAndEveryStaticSorterSwitchesItselfOff()
        {
            var template = NewObjectTemplate();
            var prop = NewTile(TileSemantic.Prop, template, NewSprite("tree"));
            var props = NewTilemap("Props");
            const int width = 60, depth = 40;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
                props.SetTile(new Vector3Int(x, z, 0), prop);

            var last = new Vector3Int(width - 1, depth - 1, 0);
            var frames = 0;
            while (props.GetInstantiatedObject(last) == null && frames < 600) { frames++; yield return null; }
            clock.Stop();
            Object.Destroy(template);
            Debug.Log($"[Perf] {width * depth} prop objects instantiated in {clock.ElapsedMilliseconds} ms over {frames} frames");

            Assert.IsNotNull(props.GetInstantiatedObject(last), "all 2400 props were instantiated");
            Assert.Less(clock.ElapsedMilliseconds, 8000, "a large map must not take seconds to bring up its props");

            yield return null;
            yield return null;
            var stillRunning = 0;
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
                if (props.GetInstantiatedObject(new Vector3Int(x, z, 0)).GetComponent<StaticSpriteSorter>().enabled) stillRunning++;
            Assert.AreEqual(0, stillRunning, "no per-frame sorter cost once the props have sorted");
        }

        [UnityTest]
        public IEnumerator EverySortedSemanticGetsAnObjectAndEveryFlatOneDoesNot()
        {
            var template = NewObjectTemplate();
            var tilemap = NewTilemap("Mixed");
            var semantics = new[] { TileSemantic.Ground, TileSemantic.Blocked, TileSemantic.Wall, TileSemantic.Prop, TileSemantic.Decor, TileSemantic.Door };
            for (var i = 0; i < semantics.Length; i++)
                tilemap.SetTile(new Vector3Int(i, 0, 0), NewTile(semantics[i], template, NewSprite(semantics[i].ToString())));
            yield return null;
            Object.Destroy(template);
            yield return null;
            yield return null;

            for (var i = 0; i < semantics.Length; i++)
            {
                var instance = tilemap.GetInstantiatedObject(new Vector3Int(i, 0, 0));
                Assert.AreEqual(semantics[i].IsSortedObject(), instance != null, semantics[i].ToString());
            }
        }

        [UnityTest]
        public IEnumerator AStaticSorterOutsideTheSortableRangeWarnsOnceAndStillSorts()
        {
            var go = new GameObject("FarProp");
            go.SetActive(false);
            go.AddComponent<SpriteRenderer>();
            go.transform.position = new Vector3(0f, 0f, 5000f);
            var sorter = go.AddComponent<StaticSpriteSorter>();
            go.SetActive(true);
            made.Add(go);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("outside"));
            yield return null;

            Assert.IsFalse(sorter.enabled);
            Assert.AreEqual(SpriteSortingContract.WorldLayer, go.GetComponent<SpriteRenderer>().sortingLayerName);
        }
    }
}
