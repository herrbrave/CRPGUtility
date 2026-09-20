using System.Collections.Generic;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>
    /// The single source of truth for the Phase 2 gate map: the scene author places tiles and props from it, configures the walkability grid
    /// from it, and the content tests assert against it -- so a prop and the cell it blocks can never drift apart.
    /// Sprites are named cells of the copied Kenney sheet <c>roguelikeSheet_transparent</c> (col, row on its 17px pitch).
    /// </summary>
    public static class GateLayout
    {
        public const int Width = 24;
        public const int Depth = 16;
        public const float CellSize = 1f;
        public static readonly Vector3 Origin = new(0f, 0f, 0f);

        public const string SheetPath = "Assets/Art/Source/roguelikeSheet_transparent.png";
        public const string SheetName = "roguelikeSheet_transparent";

        /// <summary>The horizontal wall splits the map; the door is the only way through.</summary>
        public const int WallRow = 11;
        public static readonly Vector2Int DoorCell = new(12, WallRow);
        /// <summary>Where a character stands to use the door: the walkable cell just south of it.</summary>
        public static readonly Vector2Int DoorAnchorCell = new(12, WallRow - 1);

        public static readonly Vector2Int Grass = new(5, 0);
        public static readonly Vector2Int Dirt = new(6, 0);
        public static readonly Vector2Int Stone = new(7, 0);
        public static readonly Vector2Int WallTile = new(23, 12);
        public static readonly Vector2Int DoorClosed = new(32, 1);
        public static readonly Vector2Int DoorOpen = new(36, 1);

        public readonly struct Prop
        {
            public Prop(string name, Vector2Int sprite, Vector2Int cell)
            {
                Name = name;
                Sprite = sprite;
                Cell = cell;
            }

            public string Name { get; }
            public Vector2Int Sprite { get; }
            public Vector2Int Cell { get; }
        }

        /// <summary>Solid props (trees, barrels, the chest): each blocks its own cell.</summary>
        public static readonly Prop[] SolidProps =
        {
            new("Tree A", new Vector2Int(13, 9), new Vector2Int(3, 3)),
            new("Tree B", new Vector2Int(15, 9), new Vector2Int(15, 4)),
            new("Tree C", new Vector2Int(16, 9), new Vector2Int(19, 8)),
            new("Tree D", new Vector2Int(13, 9), new Vector2Int(2, 7)),
            new("Barrel A", new Vector2Int(23, 0), new Vector2Int(8, 6)),
            new("Barrel B", new Vector2Int(24, 0), new Vector2Int(9, 6)),
            new("Barrel C", new Vector2Int(23, 0), new Vector2Int(8, 7)),
            new("Chest", new Vector2Int(37, 9), new Vector2Int(17, 13)),
            new("Tree E", new Vector2Int(15, 9), new Vector2Int(4, 13)),
            new("Tree F", new Vector2Int(16, 9), new Vector2Int(20, 14)),
        };

        /// <summary>Every cell a wall tile occupies (the whole wall row except the door cell).</summary>
        public static IEnumerable<Vector2Int> WallCells()
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = new Vector2Int(x, WallRow);
                if (cell != DoorCell) yield return cell;
            }
        }

        /// <summary>Cells blocked from the start of the game: walls and solid props. The door cell is NOT here: a GridWalkabilityBlocker owns it.</summary>
        public static List<Vector2Int> BlockedCells()
        {
            var cells = new List<Vector2Int>(WallCells());
            foreach (var prop in SolidProps) cells.Add(prop.Cell);
            return cells;
        }

        /// <summary>Row of dirt path running west-east south of the wall; north of the wall is a stone-floored room.</summary>
        public const int PathRow = 8;

        public static Vector2Int GroundSprite(int x, int z) => z > WallRow ? Stone : z == PathRow ? Dirt : Grass;

        public static readonly Vector2Int SkeletonCell = new(14, 4);

        public static GateMemberSpec[] DefaultMembers() => new[]
        {
            new GateMemberSpec
            {
                displayName = "Aldric", spawnId = "hero-1", cell = new Vector2Int(4, 9), maxHp = 24,
                appearance = new[] { "layer-body=body-kenney-0-0", "layer-hair=hair-kenney-19-0", "layer-clothes=clothes-kenney-6-0" },
                gear = new[] { "item-iron-helm", "item-short-sword", "item-round-shield" },
            },
            new GateMemberSpec
            {
                displayName = "Brenna", spawnId = "hero-2", cell = new Vector2Int(6, 9), maxHp = 18,
                appearance = new[] { "layer-body=body-kenney-1-0", "layer-hair=hair-kenney-20-0", "layer-clothes=clothes-kenney-8-0" },
                gear = new[] { "item-short-sword" },
            },
            new GateMemberSpec
            {
                displayName = "Corvin", spawnId = "hero-3", cell = new Vector2Int(5, 10), maxHp = 20,
                appearance = new[] { "layer-body=body-kenney-0-1", "layer-hair=hair-kenney-21-1", "layer-clothes=clothes-kenney-10-0" },
                gear = new[] { "item-tunic" },
            },
            new GateMemberSpec
            {
                displayName = "Dessa", spawnId = "hero-4", cell = new Vector2Int(7, 10), maxHp = 16,
                appearance = new[] { "layer-body=body-kenney-1-1", "layer-hair=hair-kenney-22-1", "layer-clothes=clothes-kenney-12-0" },
                gear = new string[0],
            },
        };
    }
}
