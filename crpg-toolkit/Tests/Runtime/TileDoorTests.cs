using System.Collections;
using System.Reflection;
using CRPG.Core;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Navigation.Grid;
using CRPG.Navigation.Grid.Tilemaps;
using CRPG.Presentation.Sprites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D campaign Task 3.3: a Door tile's prefab instantiated by a REAL Tilemap is wired at runtime by TileDoor -- it blocks its own cell while
    /// closed, opens through the real UseDoorInteractionAdapter, and parks its interaction anchor on a walkable neighbouring cell.</summary>
    public class TileDoorTests
    {
        private const float Ox = 8000f; // far from other tests' scenes; Z stays small

        private readonly System.Collections.Generic.List<Object> made = new();
        private GameObject root;
        private WalkabilityGridProvider provider;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.Destroy(root);
            foreach (var o in made) if (o != null) Object.Destroy(o);
            made.Clear();
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        /// <summary>The door prefab a real project authors: root with logic, a Visual child, an Anchor child. Built inactive so serialized values are in
        /// place before Awake, then activated (a prefab ASSET is active), parked far away; the tests destroy it once instances exist.</summary>
        private GameObject NewDoorTemplate()
        {
            var template = new GameObject("DoorTemplate");
            template.SetActive(false);
            template.transform.position = new Vector3(Ox + 900f, 0f, 100f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(template.transform, false);
            visual.AddComponent<SpriteRenderer>();
            visual.AddComponent<StaticSpriteSorter>();
            var anchor = new GameObject("Anchor").transform;
            anchor.SetParent(template.transform, false);

            template.AddComponent<BoxCollider>();
            var blocker = template.AddComponent<GridWalkabilityBlocker>();
            var interactable = template.AddComponent<WorldInteractable>();
            interactable.Configure(anchor, 1.5f);
            var adapter = template.AddComponent<UseDoorInteractionAdapter>();
            SetField(adapter, "closedVisual", visual);
            var door = template.AddComponent<TileDoor>();
            SetField(door, "blocker", blocker);
            SetField(door, "anchor", anchor);
            SetField(door, "interactable", interactable);
            SetField(door, "interactionRange", 2.5f); // differs from the 1.5 the prefab's interactable was built with

            template.SetActive(true);
            return template;
        }

        private Tilemap NewMap(int width, int depth, System.Func<int, int, bool> blockedCell = null)
        {
            var providerGo = new GameObject("Provider");
            providerGo.transform.position = Vector3.zero;
            provider = providerGo.AddComponent<WalkabilityGridProvider>();
            var blocked = new System.Collections.Generic.List<GridCoordinate>();
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
                if (blockedCell != null && blockedCell(x, z)) blocked.Add(new GridCoordinate(x, z));
            provider.Configure(new Vector3(Ox, 0f, 0f), 1f, width, depth, blocked);
            made.Add(providerGo);

            root = new GameObject("Root");
            root.transform.position = new Vector3(Ox, 0f, 0f);
            root.AddComponent<Grid>().cellSwizzle = GridLayout.CellSwizzle.XZY;
            var go = new GameObject("Doors");
            go.transform.SetParent(root.transform, false);
            var map = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();
            map.orientation = Tilemap.Orientation.XZ;
            return map;
        }

        private SemanticTile NewDoorTile(GameObject template)
        {
            var tile = ScriptableObject.CreateInstance<SemanticTile>();
            var texture = new Texture2D(4, 4);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            tile.Configure(TileSemantic.Door, sprite, template);
            made.Add(tile);
            made.Add(texture);
            made.Add(sprite);
            return tile;
        }

        private IEnumerator PaintDoor(Tilemap map, Vector3Int cell)
        {
            var template = NewDoorTemplate();
            map.SetTile(cell, NewDoorTile(template));
            yield return null;
            Object.Destroy(template);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ADoorTileInstantiatesADoorThatBlocksItsCellAndParksItsAnchorSouthOfIt()
        {
            var map = NewMap(10, 10);
            var cell = new Vector3Int(5, 5, 0);
            yield return PaintDoor(map, cell);

            var instance = map.GetInstantiatedObject(cell);
            Assert.IsNotNull(instance);
            var door = instance.GetComponent<TileDoor>();
            Assert.IsTrue(door.IsWired);
            Assert.AreEqual(new GridCoordinate(5, 5), door.Cell);
            Assert.IsFalse(provider.Grid.IsWalkable(new GridCoordinate(5, 5)), "closed: the door blocks its own cell");
            Assert.AreEqual(new GridCoordinate(5, 4), door.ApproachCell, "south of the door first");

            var interactable = instance.GetComponent<WorldInteractable>();
            Assert.IsTrue(interactable.IsConfigured);
            Assert.AreEqual(2.5f, interactable.InteractionRange, 1e-4f, "TileDoor applies its own interaction range");
            Assert.AreEqual(Ox + 5.5f, interactable.InteractionPoint.x, 0.01f);
            Assert.AreEqual(4.5f, interactable.InteractionPoint.z, 0.01f, "the anchor stands on the approach cell");
        }

        [UnityTest]
        public IEnumerator OpeningTheDoorFreesItsCellAndHidesItsVisualAndItCanBeClosedAgain()
        {
            var map = NewMap(10, 10);
            var cell = new Vector3Int(3, 3, 0);
            yield return PaintDoor(map, cell);
            var instance = map.GetInstantiatedObject(cell);
            var adapter = instance.GetComponent<UseDoorInteractionAdapter>();
            var visual = instance.transform.Find("Visual").gameObject;

            adapter.BeginExecute(RuntimeEntityId.NewId(), _ => { });
            Assert.IsTrue(adapter.IsOpen);
            Assert.IsTrue(provider.Grid.IsWalkable(new GridCoordinate(3, 3)), "open: passable");
            Assert.IsFalse(visual.activeSelf, "an open door draws nothing");

            adapter.BeginExecute(RuntimeEntityId.NewId(), _ => { });
            Assert.IsFalse(adapter.IsOpen);
            Assert.IsFalse(provider.Grid.IsWalkable(new GridCoordinate(3, 3)));
            Assert.IsTrue(visual.activeSelf);
        }

        [UnityTest]
        public IEnumerator TheInstanceGetsTheTilesSpriteAndTheApproachFallsBackWhenSouthIsBlocked()
        {
            // The south neighbour (5,4) is a wall: the anchor goes north.
            var map = NewMap(10, 10, (x, z) => x == 5 && z == 4);
            var cell = new Vector3Int(5, 5, 0);
            yield return PaintDoor(map, cell);

            var instance = map.GetInstantiatedObject(cell);
            Assert.IsNotNull(instance.GetComponentInChildren<SpriteRenderer>().sprite, "StartUp gave the visual the tile's sprite");
            Assert.AreEqual(new GridCoordinate(5, 6), instance.GetComponent<TileDoor>().ApproachCell);
        }

        [UnityTest]
        public IEnumerator ADoorWithNoWalkableNeighbourWarnsAndADoorWithNoGridLogsAnError()
        {
            var map = NewMap(10, 10, (x, z) => !(x == 5 && z == 5)); // everything else is blocked
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no walkable neighbouring cell"));
            yield return PaintDoor(map, new Vector3Int(5, 5, 0));
            Assert.IsNull(map.GetInstantiatedObject(new Vector3Int(5, 5, 0)).GetComponent<TileDoor>().ApproachCell);
            Object.Destroy(root);
            Object.Destroy(provider.gameObject);
            yield return null;

            // No provider at all in the scene.
            root = new GameObject("Root2");
            root.transform.position = new Vector3(Ox, 0f, 0f);
            root.AddComponent<Grid>().cellSwizzle = GridLayout.CellSwizzle.XZY;
            var go = new GameObject("Doors");
            go.transform.SetParent(root.transform, false);
            var map2 = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>();
            map2.orientation = Tilemap.Orientation.XZ;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no WalkabilityGridProvider"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no WalkabilityGridProvider")); // the parked template wires too
            yield return PaintDoor(map2, new Vector3Int(1, 1, 0));
        }
    }
}
