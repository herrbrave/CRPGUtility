using System.Linq;
using CRPG.Editor.Validation;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CRPG.Tests.EditMode
{
    /// <summary>2D campaign Task 3.2: SemanticTile, TileSemantic and SemanticTileValidator (pure asset logic, no scene).</summary>
    public class SemanticTileTests
    {
        private readonly System.Collections.Generic.List<Object> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private Sprite Sprite()
        {
            var texture = new Texture2D(4, 4);
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            made.Add(texture);
            made.Add(sprite);
            return sprite;
        }

        private GameObject Prefab(bool renderer = true, bool sorter = true)
        {
            var go = new GameObject("Prefab");
            if (renderer) go.AddComponent<SpriteRenderer>();
            if (renderer && sorter) go.AddComponent<StaticSpriteSorter>();
            made.Add(go);
            return go;
        }

        private SemanticTile NewTile(TileSemantic semantic, GameObject prefab = null, Sprite sprite = null)
        {
            var tile = ScriptableObject.CreateInstance<SemanticTile>();
            tile.Configure(semantic, sprite ?? Sprite(), prefab);
            made.Add(tile);
            return tile;
        }

        // ---------------------------------------------------------------------------------------------
        // Semantics
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void ANewTileIsUnclassifiedBecauseThereIsNoDefaultMeaning()
        {
            var tile = ScriptableObject.CreateInstance<SemanticTile>();
            made.Add(tile);
            Assert.AreEqual(TileSemantic.Unclassified, tile.Semantic);
            Assert.IsFalse(tile.IsClassified);
            Assert.AreEqual(0, (int)TileSemantic.Unclassified, "the zero value is the invalid one");
        }

        [TestCase(TileSemantic.Ground, false, false, false)]
        [TestCase(TileSemantic.Blocked, true, false, false)]
        [TestCase(TileSemantic.Wall, true, true, true)]
        [TestCase(TileSemantic.Prop, true, false, true)]
        [TestCase(TileSemantic.Decor, false, false, true)]
        [TestCase(TileSemantic.Door, false, false, true)]
        [TestCase(TileSemantic.Unclassified, false, false, false)]
        public void EachSemanticHasTheDocumentedMovementSightAndRenderingBehaviour(TileSemantic semantic, bool blocksMovement, bool blocksSight, bool sorted)
        {
            Assert.AreEqual(blocksMovement, semantic.BlocksMovement(), "blocks movement");
            Assert.AreEqual(blocksSight, semantic.BlocksSight(), "blocks sight");
            Assert.AreEqual(sorted, semantic.IsSortedObject(), "sorted object");
        }

        [Test]
        public void ConfigureKeepsTheSpriteSemanticPrefabAndFlagsConsistent()
        {
            var prefab = Prefab();
            var sprite = Sprite();

            var wall = NewTile(TileSemantic.Wall, prefab, sprite);
            Assert.AreSame(sprite, wall.sprite);
            Assert.AreSame(prefab, wall.gameObject);
            Assert.IsTrue((wall.flags & TileFlags.InstantiateGameObjectRuntimeOnly) != 0, "a sorted object is only instantiated at runtime, never while painting");
            Assert.AreEqual(Tile.ColliderType.None, wall.colliderType);

            var ground = NewTile(TileSemantic.Ground, prefab, sprite);
            Assert.IsNull(ground.gameObject, "a flat tile never keeps a prefab, even if one is passed");
            Assert.IsTrue((ground.flags & TileFlags.InstantiateGameObjectRuntimeOnly) == 0);
        }

        // ---------------------------------------------------------------------------------------------
        // Tile data and instance start-up
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void InEditModeTheTileDataKeepsTheSpriteSoPaintingShowsIt()
        {
            var tile = NewTile(TileSemantic.Prop, Prefab());
            var data = new TileData();
            tile.GetTileData(Vector3Int.zero, null, ref data);
            Assert.AreSame(tile.sprite, data.sprite, "painting in the editor shows the sprite (the prefab is not instantiated there)");
        }

        [Test]
        public void StartUpGivesTheInstantiatedObjectThisTilesSprite()
        {
            var tile = NewTile(TileSemantic.Prop, Prefab());
            var instance = Prefab();

            tile.StartUp(Vector3Int.zero, null, instance);

            Assert.AreSame(tile.sprite, instance.GetComponent<SpriteRenderer>().sprite);
        }

        [Test]
        public void StartUpFindsAChildRendererAndToleratesANullObjectOrNoRenderer()
        {
            var tile = NewTile(TileSemantic.Prop, Prefab());

            var parent = new GameObject("Parent");
            made.Add(parent);
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform, false);
            var childRenderer = child.AddComponent<SpriteRenderer>();
            tile.StartUp(Vector3Int.zero, null, parent);
            Assert.AreSame(tile.sprite, childRenderer.sprite);

            Assert.DoesNotThrow(() => tile.StartUp(Vector3Int.zero, null, null));
            Assert.DoesNotThrow(() => tile.StartUp(Vector3Int.zero, null, Prefab(renderer: false)));
        }

        // ---------------------------------------------------------------------------------------------
        // Validator
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void AProperlyBuiltTileOfEverySemanticValidatesClean()
        {
            foreach (var semantic in new[] { TileSemantic.Ground, TileSemantic.Blocked, TileSemantic.Wall, TileSemantic.Prop, TileSemantic.Decor, TileSemantic.Door })
            {
                var tile = NewTile(semantic, Prefab());
                Assert.IsEmpty(SemanticTileValidator.Validate(tile, "x").Select(r => r.Message).ToList(), semantic.ToString());
            }

            Assert.IsEmpty(SemanticTileValidator.Validate(null, "x"));
        }

        [Test]
        public void AnUnclassifiedTileIsAnError()
        {
            var tile = ScriptableObject.CreateInstance<SemanticTile>();
            made.Add(tile);
            tile.sprite = Sprite();
            var results = SemanticTileValidator.Validate(tile, "x");
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == "semantic"));
        }

        [Test]
        public void ATileWithoutASpriteIsAnError()
        {
            var tile = NewTile(TileSemantic.Ground);
            tile.sprite = null;
            Assert.IsTrue(SemanticTileValidator.Validate(tile, "x").Any(r => r.Severity == ValidationSeverity.Error && r.Field == "sprite"));
        }

        [Test]
        public void ASortedObjectTileNeedsAPrefabWithASpriteRendererAndWarnsWithoutASorter()
        {
            var noPrefab = NewTile(TileSemantic.Prop);
            Assert.IsTrue(SemanticTileValidator.Validate(noPrefab, "x").Any(r => r.Severity == ValidationSeverity.Error && r.Field == "gameObject"));

            var noRenderer = NewTile(TileSemantic.Prop, Prefab(renderer: false));
            Assert.IsTrue(SemanticTileValidator.Validate(noRenderer, "x").Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("SpriteRenderer")));

            var noSorter = NewTile(TileSemantic.Wall, Prefab(sorter: false));
            var results = SemanticTileValidator.Validate(noSorter, "x");
            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("StaticSpriteSorter")));
            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void AFlatTileWithAPrefabAndASortedTileInstantiatedWhilePaintingAreWarnings()
        {
            var ground = NewTile(TileSemantic.Ground);
            ground.gameObject = Prefab();
            Assert.IsTrue(SemanticTileValidator.Validate(ground, "x").Any(r => r.Severity == ValidationSeverity.Warning && r.Field == "gameObject"));

            var prop = NewTile(TileSemantic.Prop, Prefab());
            prop.flags = TileFlags.LockColor; // someone cleared the runtime-only flag
            Assert.IsTrue(SemanticTileValidator.Validate(prop, "x").Any(r => r.Severity == ValidationSeverity.Warning && r.Field == "flags"));
        }
    }
}
