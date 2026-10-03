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

        // Task 6.2: encounter keys. Both Upper patrols record UpperClearedProgressKey so one Kill objective (targetCount 2)
        // can count them together; the Middle barricade has its own.
        public const string UpperClearedProgressKey = "act2-upper-patrol-cleared";
        public const string MiddleClearedProgressKey = "act2-middle-barricade-cleared";
        public const string GoblinNpcId = "npc-goblin";

        // Task 6.3 (the Deep level).
        public const string DeepLeverKey = "deep-lever-pulled";
        public const string ThrallClearedProgressKey = "act3-thrall-cleared";
        public const string NecromancerDefeatedKey = "necromancer-defeated";

        private const int UpperWidth = 34, UpperDepth = 26;
        private const int MiddleWidth = 36, MiddleDepth = 28;
        private const int DeepWidth = 34, DeepDepth = 32;

        // Tiles by sheet, column, row -- same vocabulary/convention as BarrowsEndLocationAuthor.
        private static readonly (string sheet, int col, int row) CaveFloor = ("cave", 8, 2), CaveDirt = ("cave", 8, 5), CaveWall = ("cave", 9, 0),
            Rock = ("cave", 0, 0), RockB = ("cave", 1, 0), TallColumn = ("cave", 3, 0), CaveWater = ("cave", 0, 10), Mushrooms = ("cave", 0, 3),
            Bones = ("cave", 0, 2), MineRail = ("cave", 10, 16), IronBars = ("cave", 0, 7);

        private static Sprite MushroomSprite() => Tile(Mushrooms).sprite;

        [MenuItem("Barrows End/Content/Author Caverns")]
        public static void Author() => Author(overwrite: false);

        /// <summary>Regenerates ONLY Cavern_Deep (Task 6.3 work should not touch the shipped Upper/Middle levels).</summary>
        [MenuItem("Barrows End/Content/Reset Deep Cavern (OVERWRITES hand edits)")]
        public static void ResetDeep()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Reset the Deep cavern?",
                    "This regenerates Cavern_Deep from the code layout and DISCARDS anything painted or placed by hand in it.", "Reset", "Cancel")) return;
            BarrowsEndTileAuthor.Author();
            Directory.CreateDirectory(BarrowsEndLocationAuthor.Folder);
            AssetDatabase.Refresh();
            AuthorDeep();
            Debug.Log("[BarrowsEnd] Deep cavern authored.");
        }

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
            // Grumble's giant mushrooms (side quest): two on this level, one in the water chamber.
            AddHarvestPoint(root, "MushroomsWest", Cell(7, 12), Cell(7, 11), MushroomSprite(), "upper-west");
            AddHarvestPoint(root, "MushroomsEast", Cell(UpperWidth - 8, 20), Cell(UpperWidth - 8, 19), MushroomSprite(), "upper-east");
            AddQuestStartTrigger(root, "DescentQuestStart", Cell(gateX, 5), BarrowsEndGearAuthor.DescentQuestId);
            // Two goblin patrols on the mine-rail strip: the party walks the track north and must cross both.
            AddEncounterTrigger(root, "GoblinPatrolSouth", Cell(gateX, 10), "encounter-upper-south-triggered", "encounter-upper-south-won", UpperClearedProgressKey, GoblinNpcId, 2);
            AddEncounterTrigger(root, "GoblinPatrolNorth", Cell(gateX, 17), "encounter-upper-north-triggered", "encounter-upper-north-won", UpperClearedProgressKey, GoblinNpcId, 2);
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

            // The goblins' barricade across the road to the Deep gate: a wall of rock with one gap, so the party funnels through it.
            for (var x = gateX - 5; x <= gateX + 5; x++) if (x != gateX) l.Objects[x, MiddleDepth - 4] = x % 2 == 0 ? Rock : RockB;

            l.Objects[3, 4] = TallColumn; l.Objects[MiddleWidth - 4, 4] = TallColumn;
            l.Objects[3, MiddleDepth - 5] = Rock; l.Objects[MiddleWidth - 4, MiddleDepth - 5] = RockB;

            var scene = NewLocationScene("CavernMiddle", l, out var root, out var grid);
            AddHarvestPoint(root, "MushroomsShore", Cell(8, 6), Cell(8, 5), MushroomSprite(), "middle-shore");
            AddEncounterTrigger(root, "GoblinBarricade", Cell(gateX, MiddleDepth - 7), "encounter-middle-barricade-triggered", "encounter-middle-barricade-won", MiddleClearedProgressKey, GoblinNpcId, 3);
            AddDestination(root, MiddleFromUpperDestination, Cell(gateX, 3), grid);
            AddDestination(root, MiddleFromDeepDestination, Cell(gateX, MiddleDepth - 4), grid);
            AddTravelPoint(root, "TravelToUpper", Cell(gateX, 0), Cell(gateX, 1), UpperPath, UpperFromMiddleDestination, ArchSprite());
            AddTravelPoint(root, "TravelToDeep", Cell(gateX, MiddleDepth - 1), Cell(gateX, MiddleDepth - 2), DeepPath, DeepFromMiddleDestination, ArchSprite());
            return BakeAndSave(scene, root, MiddlePath);
        }

        private const string GalleryPanelSettingsPath = "Assets/Content/Ui/gallery-panel-settings.asset";

        private static UnityEngine.UIElements.PanelSettings LoadGalleryPanelSettings() =>
            AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(GalleryPanelSettingsPath)
            ?? throw new System.InvalidOperationException($"{GalleryPanelSettingsPath} is missing -- author the UI content first.");

        /// <summary>The campaign's ending, a short narrator conversation played when the Deep Dark quest completes. Saved as an asset
        /// (re-created, so re-authoring the Deep level re-bakes the scene reference with it).</summary>
        private static CRPG.Dialogue.DialogueDefinition BuildEpilogueDialogue()
        {
            CRPG.Core.SerializableGuid Id(string seed) => CRPG.Core.SerializableGuid.FromString(seed);
            var lines = new[]
            {
                "The necromancer falls, and with a long dry rattle the thralls crumble back into dust.",
                "Silence settles over the Deep Dark -- the old silence, not the hungry one. Far above, Barrow's End will sleep tonight.",
                "Your squad climbs toward the light. The Gathering Storm has broken, and the town still stands.",
                "THE END. Thank you for playing Barrow's End. You are free to keep exploring.",
            };

            var nodes = new System.Collections.Generic.List<CRPG.Dialogue.DialogueNode>();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = new CRPG.Dialogue.LineNode();
                SetField(line, "nodeId", Id($"epilogue-{i}"));
                SetField(line, "speakerName", "Narrator");
                SetField(line, "text", lines[i]);
                SetField(line, "nextNodeId", Id(i + 1 < lines.Length ? $"epilogue-{i + 1}" : "epilogue-end"));
                nodes.Add(line);
            }

            var end = new CRPG.Dialogue.EndNode();
            SetField(end, "nodeId", Id("epilogue-end"));
            nodes.Add(end);

            var dialogue = ScriptableObject.CreateInstance<CRPG.Dialogue.DialogueDefinition>();
            SetField(dialogue, "id", "dlg-epilogue");
            SetField(dialogue, "startNodeId", Id("epilogue-0"));
            SetField(dialogue, "nodes", nodes);
            var path = $"{BarrowsEndGearAuthor.NpcDir}/dlg-epilogue.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(dialogue, path);
            return dialogue;
        }

        /// <summary>The Deep Dark (Task 6.3). South to north: an entry hall (arrival at y 3), a first thrall patrol, a river with a
        /// bridge, a north bank with a lever and a second patrol, a portcullis row, and the necromancer's arena.
        /// Placement rules from the plan's review: every encounter trigger sits at least 2x its scatter from the portcullis
        /// cells (a spawn sample has no connectivity check and must never land in the sealed arena); the arena trigger sits at
        /// least radius+1 inside the arena (volumes pass through walls and bars); the arrival point is outside every patrol volume;
        /// and no volume sits within radius+1 of Middle's arch approach (18.5, 26.5) -- the Middle and Deep scenes share world
        /// coordinates while the party travels.</summary>
        public static BakeReport AuthorDeep()
        {
            const int gateX = 17;
            var l = new Layout(DeepWidth, DeepDepth);
            l.FillGround(CaveFloor, 0, 0, DeepWidth - 1, DeepDepth - 1);
            l.FillGround(CaveDirt, gateX - 4, 2, gateX + 4, DeepDepth - 3);
            l.FillGround(CaveWater, 1, 10, DeepWidth - 2, 11);                // the underground river...
            l.FillGround(CaveDirt, gateX - 1, 10, gateX + 1, 11);             // ...with a three-wide bridge

            l.FillObjects(CaveWall, 0, 0, DeepWidth - 1, 0);
            l.FillObjects(CaveWall, 0, DeepDepth - 1, DeepWidth - 1, DeepDepth - 1);
            l.FillObjects(CaveWall, 0, 0, 0, DeepDepth - 1);
            l.FillObjects(CaveWall, DeepWidth - 1, 0, DeepWidth - 1, DeepDepth - 1);
            l.Objects[gateX, 0] = null;                                      // the only way out: back up to Cavern_Middle

            // The arena: walled on three sides, its south wall (row 18) pierced by a three-cell doorway the portcullis fills.
            const int gateRow = 18;
            l.FillObjects(CaveWall, 10, gateRow, 24, gateRow);
            l.FillObjects(CaveWall, 10, gateRow, 10, DeepDepth - 1);
            l.FillObjects(CaveWall, 24, gateRow, 24, DeepDepth - 1);
            for (var x = gateX - 1; x <= gateX + 1; x++) l.Objects[x, gateRow] = null; // plain floor in the bake: the bake sees the arena as reachable

            l.Objects[4, 4] = Bones; l.Objects[5, 5] = Bones; l.Objects[DeepWidth - 5, 4] = Rock;
            l.Objects[7, 13] = Bones; l.Objects[27, 14] = Bones; l.Objects[12, 27] = Bones; l.Objects[22, 28] = Bones; // the ritual's remains

            var scene = NewLocationScene("CavernDeep", l, out var root, out var grid);
            AddDestination(root, DeepFromMiddleDestination, Cell(gateX, 3), grid);
            AddTravelPoint(root, "TravelToMiddle", Cell(gateX, 0), Cell(gateX, 1), MiddlePath, MiddleFromDeepDestination, ArchSprite());

            // Quest + patrols + lever + gate + boss. Keys are shared with the tests and the Deep Dark quest.
            AddEpilogue(root, "Epilogue", Cell(gateX, 3), BarrowsEndGearAuthor.DeepDarkQuestId, BuildEpilogueDialogue(), LoadGalleryPanelSettings());
            AddQuestStartTrigger(root, "DeepDarkQuestStart", Cell(gateX, 3), BarrowsEndGearAuthor.DeepDarkQuestId);
            AddEncounterTrigger(root, "ThrallPatrolSouth", Cell(gateX, 7), "encounter-deep-south-triggered", "encounter-deep-south-won", ThrallClearedProgressKey,
                BarrowsEndGearAuthor.SkeletonThrallNpcId, 2, spawnScatter: 2f);
            AddEncounterTrigger(root, "ThrallPatrolNorth", Cell(gateX, 14), "encounter-deep-north-triggered", "encounter-deep-north-won", ThrallClearedProgressKey,
                BarrowsEndGearAuthor.SkeletonThrallNpcId, 2, spawnScatter: 1.5f);
            // The lever stands beside the corridor to the gate (visible on the way, a bright tint so it reads as "use me"), and a hint
            // fires when the party reaches the sealed portcullis -- the first live playthrough never found the lever when it was a
            // plain column far to the west with nothing pointing at it.
            AddLeverPoint(root, "DeepLever", Cell(13, 17), Cell(14, 17), Tile(TallColumn).sprite, DeepLeverKey, new Color(1f, 0.82f, 0.25f, 1f));
            AddHintTrigger(root, "SealedGateHint", Cell(gateX, 16), "The portcullis is sealed. There must be a lever nearby -- look for something glowing gold beside the road.", DeepLeverKey);
            AddPortcullis(root, "Portcullis", new[] { Cell(gateX - 1, gateRow), Cell(gateX, gateRow), Cell(gateX + 1, gateRow) }, Tile(IronBars).sprite, DeepLeverKey);
            AddEncounterTrigger(root, "NecromancerArena", Cell(gateX, 22), "encounter-deep-boss-triggered", NecromancerDefeatedKey, "act3-boss-progress",
                BarrowsEndGearAuthor.NecromancerNpcId, 1, secondaryNpcId: BarrowsEndGearAuthor.SkeletonThrallNpcId, secondaryCount: 2,
                spawnCell: Cell(gateX, 28), requiredKey: DeepLeverKey, spawnScatter: 1.5f);
            return BakeAndSave(scene, root, DeepPath);
        }
    }
}
