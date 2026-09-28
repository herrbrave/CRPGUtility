using System.IO;
using CRPG.Editor.Location;
using UnityEditor;
using UnityEngine;
using static BarrowsEnd.Editor.LocationAuthoringKit;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 6.0: the real Glimmering Caverns -- 3 levels (per Barrows-End-Town-and-Caverns-Layout.png: Upper
    /// mine tracks, a Middle water chamber, a Deep undead/boss level), replacing Cave_Slice as the campaign's
    /// actual dungeon. Two-way travel throughout (Town &lt;-&gt; Upper &lt;-&gt; Middle &lt;-&gt; Deep), same
    /// tile-authoring/travel-point/bake pipeline as <see cref="BarrowsEndTownAuthor"/> via
    /// <see cref="LocationAuthoringKit"/>. This task only shells the three levels out (walls, floor, water/rock
    /// decor, real two-way travel) -- goblin/undead encounters, the necromancer boss and the portcullis'
    /// actual mechanic are explicitly Task 6.2/6.3's job (a plain open gap marks where the Deep level's boss
    /// arena will go, not yet dressed).
    /// </summary>
    public static class BarrowsEndCavernsAuthor
    {
        public const string UpperPath = BarrowsEndLocationAuthor.Folder + "/Cavern_Upper.unity";
        public const string MiddlePath = BarrowsEndLocationAuthor.Folder + "/Cavern_Middle.unity";
        public const string DeepPath = BarrowsEndLocationAuthor.Folder + "/Cavern_Deep.unity";

        public const string UpperFromTownDestination = "from-town";
        public const string UpperFromMiddleDestination = "from-middle";
        public const string MiddleFromUpperDestination = "from-upper";
        public const string MiddleFromDeepDestination = "from-deep";
        public const string DeepFromMiddleDestination = "from-middle";

        private const int UpperWidth = 34, UpperDepth = 26;
        private const int MiddleWidth = 36, MiddleDepth = 28;
        private const int DeepWidth = 34, DeepDepth = 26;

        // Tiles by sheet, column, row -- same vocabulary/convention as BarrowsEndLocationAuthor.
        private static readonly (string sheet, int col, int row) CaveFloor = ("cave", 8, 2), CaveDirt = ("cave", 8, 5), CaveWall = ("cave", 9, 0),
            Rock = ("cave", 0, 0), RockB = ("cave", 1, 0), TallColumn = ("cave", 3, 0), CaveWater = ("cave", 0, 10), Mushrooms = ("cave", 0, 3),
            Bones = ("cave", 0, 2), MineRail = ("cave", 10, 16), IronBars = ("cave", 0, 7);

        [MenuItem("Barrows End/Content/Author Caverns")]
        public static void Author() => Author(overwrite: false);

        [MenuItem("Barrows End/Content/Reset Caverns (OVERWRITES hand edits)")]
        public static void Reset()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Reset the Caverns?",
                    "This regenerates all three Cavern levels from the code layouts and DISCARDS anything painted or placed by hand in them.", "Reset", "Cancel")) return;
            Author(overwrite: true);
        }

        public static void Author(bool overwrite)
        {
            BarrowsEndTileAuthor.Author();
            Directory.CreateDirectory(BarrowsEndLocationAuthor.Folder);
            AssetDatabase.Refresh();

            var authored = new System.Collections.Generic.List<string>();
            if (overwrite || !File.Exists(UpperPath)) { AuthorUpper(); authored.Add("upper"); }
            if (overwrite || !File.Exists(MiddlePath)) { AuthorMiddle(); authored.Add("middle"); }
            if (overwrite || !File.Exists(DeepPath)) { AuthorDeep(); authored.Add("deep"); }
            Debug.Log(authored.Count == 0
                ? "[BarrowsEnd] Caverns already exist: left untouched (use 'Reset Caverns' to regenerate)."
                : $"[BarrowsEnd] Caverns authored: {string.Join(", ", authored)}.");
        }

        public static BakeReport AuthorUpper()
        {
            const int gateX = 17;
            var l = new Layout(UpperWidth, UpperDepth);
            l.FillGround(CaveFloor, 0, 0, UpperWidth - 1, UpperDepth - 1);
            l.FillGround(CaveDirt, gateX - 4, 2, gateX + 4, UpperDepth - 3);   // a dirt track running the length of the level
            l.FillGround(MineRail, gateX - 1, 8, gateX + 1, 18);              // mining tracks (Task 6.2's goblin patrols walk this)

            l.FillObjects(CaveWall, 0, 0, UpperWidth - 1, 0);                 // wall all round, two gaps for travel
            l.FillObjects(CaveWall, 0, UpperDepth - 1, UpperWidth - 1, UpperDepth - 1);
            l.FillObjects(CaveWall, 0, 0, 0, UpperDepth - 1);
            l.FillObjects(CaveWall, UpperWidth - 1, 0, UpperWidth - 1, UpperDepth - 1);
            l.Objects[gateX, 0] = null;                                      // south gap: down from Town
            l.Objects[gateX, UpperDepth - 1] = null;                         // north gap: further down to Cavern_Middle

            l.Objects[4, 4] = Rock; l.Objects[5, 4] = RockB; l.Objects[4, 5] = RockB;
            l.Objects[UpperWidth - 5, 5] = Rock; l.Objects[UpperWidth - 6, 5] = RockB;
            l.Objects[6, UpperDepth - 6] = Mushrooms; l.Objects[UpperWidth - 7, UpperDepth - 7] = Bones;

            var scene = NewLocationScene("CavernUpper", l, out var root, out var grid);
            AddDestination(root, UpperFromTownDestination, Cell(gateX, 3), grid);
            AddDestination(root, UpperFromMiddleDestination, Cell(gateX, UpperDepth - 4), grid);
            AddTravelPoint(root, "TravelToTown", Cell(gateX, 0), Cell(gateX, 1), BarrowsEndTownAuthor.Path, BarrowsEndTownAuthor.FromCavernsDestination, ArchSprite());
            AddTravelPoint(root, "TravelToMiddle", Cell(gateX, UpperDepth - 1), Cell(gateX, UpperDepth - 2), MiddlePath, MiddleFromUpperDestination, ArchSprite());
            return BakeAndSave(scene, root, UpperPath);
        }

        public static BakeReport AuthorMiddle()
        {
            const int gateX = 18;
            var l = new Layout(MiddleWidth, MiddleDepth);
            l.FillGround(CaveFloor, 0, 0, MiddleWidth - 1, MiddleDepth - 1);
            l.FillGround(CaveDirt, gateX - 4, 2, gateX + 4, MiddleDepth - 3);
            var midY = MiddleDepth / 2;
            l.FillGround(CaveWater, gateX - 6, midY - 4, gateX + 6, midY + 4); // the central water chamber

            l.FillObjects(CaveWall, 0, 0, MiddleWidth - 1, 0);
            l.FillObjects(CaveWall, 0, MiddleDepth - 1, MiddleWidth - 1, MiddleDepth - 1);
            l.FillObjects(CaveWall, 0, 0, 0, MiddleDepth - 1);
            l.FillObjects(CaveWall, MiddleWidth - 1, 0, MiddleWidth - 1, MiddleDepth - 1);
            l.Objects[gateX, 0] = null;                                      // south gap: up to Cavern_Upper
            l.Objects[gateX, MiddleDepth - 1] = null;                        // north gap: down to Cavern_Deep

            l.Objects[3, 4] = TallColumn; l.Objects[MiddleWidth - 4, 4] = TallColumn;
            l.Objects[3, MiddleDepth - 5] = Rock; l.Objects[MiddleWidth - 4, MiddleDepth - 5] = RockB;

            var scene = NewLocationScene("CavernMiddle", l, out var root, out var grid);
            AddDestination(root, MiddleFromUpperDestination, Cell(gateX, 3), grid);
            AddDestination(root, MiddleFromDeepDestination, Cell(gateX, MiddleDepth - 4), grid);
            AddTravelPoint(root, "TravelToUpper", Cell(gateX, 0), Cell(gateX, 1), UpperPath, UpperFromMiddleDestination, ArchSprite());
            AddTravelPoint(root, "TravelToDeep", Cell(gateX, MiddleDepth - 1), Cell(gateX, MiddleDepth - 2), DeepPath, DeepFromMiddleDestination, ArchSprite());
            return BakeAndSave(scene, root, MiddlePath);
        }

        public static BakeReport AuthorDeep()
        {
            const int gateX = 17;
            var l = new Layout(DeepWidth, DeepDepth);
            l.FillGround(CaveFloor, 0, 0, DeepWidth - 1, DeepDepth - 1);
            l.FillGround(CaveDirt, gateX - 4, 2, gateX + 4, DeepDepth - 3);

            l.FillObjects(CaveWall, 0, 0, DeepWidth - 1, 0);
            l.FillObjects(CaveWall, 0, DeepDepth - 1, DeepWidth - 1, DeepDepth - 1);
            l.FillObjects(CaveWall, 0, 0, 0, DeepDepth - 1);
            l.FillObjects(CaveWall, DeepWidth - 1, 0, DeepWidth - 1, DeepDepth - 1);
            l.Objects[gateX, 0] = null;                                      // only gap: back up to Cavern_Middle -- a dead end otherwise

            l.Objects[4, 4] = Bones; l.Objects[5, 5] = Bones; l.Objects[DeepWidth - 5, 4] = Rock;
            l.Objects[gateX - 3, DeepDepth - 6] = IronBars; l.Objects[gateX + 3, DeepDepth - 6] = IronBars; // flanking the reserved boss-arena approach

            var scene = NewLocationScene("CavernDeep", l, out var root, out var grid);
            AddDestination(root, DeepFromMiddleDestination, Cell(gateX, 3), grid);
            AddTravelPoint(root, "TravelToMiddle", Cell(gateX, 0), Cell(gateX, 1), MiddlePath, MiddleFromDeepDestination, ArchSprite());
            return BakeAndSave(scene, root, DeepPath);
        }
    }
}
