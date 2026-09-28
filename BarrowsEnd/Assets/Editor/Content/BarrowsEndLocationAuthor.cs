using System.Collections.Generic;
using System.IO;
using CRPG.Editor.Location;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BarrowsEnd.Editor.LocationAuthoringKit;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 3.4: authors the two demo locations the Phase 3 shell travels between -- a town slice and a cave slice -- from code, using the REAL generated
    /// semantic tiles, then bakes each with <see cref="LocationBaker"/> (it throws if the bake is refused, so a broken map can never be saved). Each has
    /// arrival <c>SceneTravelDestination</c> anchors and one travel point leading to the other. These remain throwaway proof scenes proving camera, world
    /// authoring, travel and save/load end to end -- the real Town/Caverns (Task 6.0, code-authored per the user's own call, not hand-painted) live in
    /// <c>BarrowsEndTownAuthor</c>/<c>BarrowsEndCavernsAuthor</c>, built against the SAME shared pipeline, extracted here into <see cref="LocationAuthoringKit"/>
    /// so neither of those duplicates this class's own tile-authoring/travel-point/bake logic by hand.
    /// </summary>
    public static class BarrowsEndLocationAuthor
    {
        public const string Folder = "Assets/Scenes/Locations";
        public const string TownPath = Folder + "/Town_Slice.unity";
        public const string CavePath = Folder + "/Cave_Slice.unity";

        public const string TownStart = "start";
        public const string TownFromCave = "from-cave";
        public const string CaveFromTown = "from-town";

        public const int TownWidth = 30, TownDepth = 20;
        public const int CaveWidth = 24, CaveDepth = 16;

        // Tiles by sheet, column, row (see Docs/TileCuration/*.png).
        private static readonly (string sheet, int col, int row) Grass = ("town", 5, 0), Dirt = ("town", 6, 0), Stone = ("town", 7, 0), WoodFloor = ("town", 0, 25),
            Water = ("town", 0, 0), WallTile = ("town", 21, 17), DoorTile = ("town", 32, 1), TreeA = ("town", 13, 9), TreeB = ("town", 15, 9), Barrel = ("town", 23, 0),
            Fence = ("town", 48, 23), CaveFloor = ("cave", 8, 2), CaveDirt = ("cave", 8, 5), CaveWall = ("cave", 9, 0), Rock = ("cave", 0, 0), RockB = ("cave", 1, 0),
            CaveWater = ("cave", 0, 10), Mushrooms = ("cave", 0, 3), Bones = ("cave", 0, 2);

        /// <summary>Authors the demo locations that do not exist yet. An existing location scene is NEVER overwritten (it may hold hand-painted changes): use
        /// <see cref="ResetAll"/> to regenerate from the code layouts on purpose.</summary>
        [MenuItem("Barrows End/Content/Author Demo Locations")]
        public static void AuthorAll() => Author(overwrite: false);

        [MenuItem("Barrows End/Content/Reset Demo Locations (OVERWRITES hand edits)")]
        public static void ResetAll()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Reset demo locations?",
                    "This regenerates Town_Slice and Cave_Slice from the code layouts and DISCARDS anything painted or placed by hand in them.", "Reset", "Cancel")) return;
            Author(overwrite: true);
        }

        public static void Author(bool overwrite)
        {
            BarrowsEndTileAuthor.Author();
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();

            var authored = new List<string>();
            if (overwrite || !File.Exists(TownPath)) { AuthorTown(); authored.Add("town"); }
            if (overwrite || !File.Exists(CavePath)) { AuthorCave(); authored.Add("cave"); }
            Debug.Log(authored.Count == 0
                ? "[BarrowsEnd] Demo locations already exist: left untouched (use 'Reset Demo Locations' to regenerate)."
                : $"[BarrowsEnd] Demo locations authored: {string.Join(", ", authored)}.");
        }

        public static BakeReport AuthorTown()
        {
            var l = new Layout(TownWidth, TownDepth);
            l.FillGround(Grass, 0, 0, TownWidth - 1, TownDepth - 1);
            l.FillGround(Dirt, 13, 0, 15, TownDepth - 1);                 // the north-south road
            l.FillGround(Stone, 9, 5, 19, 9);                             // a paved square across it
            l.FillGround(Water, 22, 11, 26, 14);                          // a pond
            l.FillGround(WoodFloor, 4, 12, 9, 15);                        // a house floor

            l.FillObjects(TreeA, 0, 0, 0, TownDepth - 1);                 // tree border
            l.FillObjects(TreeB, TownWidth - 1, 0, TownWidth - 1, TownDepth - 1);
            l.FillObjects(TreeA, 1, TownDepth - 1, 12, TownDepth - 1);
            l.FillObjects(TreeB, 16, TownDepth - 1, TownWidth - 2, TownDepth - 1);
            l.FillObjects(TreeB, 1, 0, 12, 0);
            l.FillObjects(TreeA, 16, 0, TownWidth - 2, 0);
            l.FillObjects(WallTile, 3, 11, 10, 16);                       // the house: walls all round ...
            l.Objects[6, 11] = DoorTile;                                  // ... with a door in the south wall
            for (var x = 4; x <= 9; x++)
            for (var y = 12; y <= 15; y++)
                l.Objects[x, y] = null;                                   // ... and an empty interior
            l.FillObjects(Fence, 1, 3, 11, 3);                            // a palisade with a gap for the road
            l.FillObjects(Fence, 17, 3, TownWidth - 2, 3);
            l.Objects[12, 8] = Barrel;
            l.Objects[20, 6] = Barrel;
            l.Objects[24, 8] = TreeA;
            l.Objects[19, 16] = TreeB;
            l.Objects[5, 5] = TreeA;

            var scene = NewLocationScene("Town", l, out var root, out var grid);
            AddDestination(root, TownStart, Cell(14, 2), grid);
            AddDestination(root, TownFromCave, Cell(14, 16), grid);
            AddTravelPoint(root, "TravelToCave", Cell(14, TownDepth - 1), Cell(14, TownDepth - 2), CavePath, CaveFromTown, ArchSprite());
            return BakeAndSave(scene, root, TownPath);
        }

        public static BakeReport AuthorCave()
        {
            var l = new Layout(CaveWidth, CaveDepth);
            l.FillGround(CaveFloor, 0, 0, CaveWidth - 1, CaveDepth - 1);
            l.FillGround(CaveDirt, 9, 0, 14, 9);                          // a dirt track from the exit
            l.FillGround(CaveWater, 16, 8, 20, 11);                       // an underground pool

            l.FillObjects(CaveWall, 0, 0, CaveWidth - 1, 0);              // wall all round, with a gap for the exit
            l.FillObjects(CaveWall, 0, CaveDepth - 1, CaveWidth - 1, CaveDepth - 1);
            l.FillObjects(CaveWall, 0, 0, 0, CaveDepth - 1);
            l.FillObjects(CaveWall, CaveWidth - 1, 0, CaveWidth - 1, CaveDepth - 1);
            l.Objects[12, 0] = null;
            l.Objects[3, 4] = Rock; l.Objects[4, 4] = RockB; l.Objects[3, 5] = RockB;
            l.Objects[18, 3] = Rock; l.Objects[19, 3] = RockB;
            l.Objects[7, 11] = Rock; l.Objects[8, 12] = RockB;
            l.Objects[5, 9] = Mushrooms; l.Objects[10, 13] = Mushrooms; l.Objects[20, 5] = Bones;

            var scene = NewLocationScene("Cave", l, out var root, out var grid);
            AddDestination(root, CaveFromTown, Cell(12, 3), grid);
            AddTravelPoint(root, "TravelToTown", Cell(12, 0), Cell(12, 1), TownPath, TownFromCave, ArchSprite());
            return BakeAndSave(scene, root, CavePath);
        }
    }
}
