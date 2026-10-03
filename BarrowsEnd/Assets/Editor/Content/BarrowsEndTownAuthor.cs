using System.Collections.Generic;
using System.IO;
using BarrowsEnd.Gate;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Editor.Location;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.Inventory;
using UnityEditor;
using UnityEngine;
using static BarrowsEnd.Editor.LocationAuthoringKit;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 6.0: the real Barrow's End town -- a town square surrounded by named buildings, per
    /// Barrows-End-Town-and-Caverns-Layout.png, replacing Town_Slice as the campaign's actual starting location.
    /// Building footprints sit on a uniform grid (systematic, not hand-placed coordinate by coordinate) so
    /// nothing overlaps; each is a real walled room with one Door tile satisfying <c>LocationBaker</c>'s
    /// bake-safety rules. This task only shells the map out -- which NPC actually occupies which building (10
    /// named NPCs from the handoff doc, 9 buildings since Elida the innkeeper and Grumble the cook share the
    /// Restful Hearth) is explicitly Task 6.1's job; the one placeholder talker/vendor this task itself proves
    /// end to end are its own throwaway fixtures, not any of the 10.
    /// </summary>
    public static class BarrowsEndTownAuthor
    {
        public const string Path = BarrowsEndLocationAuthor.Folder + "/Town.unity";
        public const string StartDestination = "start";
        public const string FromCavernsDestination = "from-caverns";

        private const int Width = 70, Depth = 50;

        // Tiles by sheet, column, row -- same vocabulary/convention as BarrowsEndLocationAuthor.
        private static readonly (string sheet, int col, int row) Grass = ("town", 5, 0), Dirt = ("town", 6, 0), Stone = ("town", 7, 0),
            WoodFloor = ("town", 0, 25), WallTile = ("town", 21, 17), DoorTile = ("town", 32, 1), TreeA = ("town", 13, 9), TreeB = ("town", 15, 9),
            Fence = ("town", 48, 23), MarketStall = ("town", 10, 0), Tent = ("town", 45, 10), Barrel = ("town", 23, 0);

        // The 9 walled buildings this task shells out, named for their eventual (Task 6.1) occupant -- kept as a
        // comment-level reservation, not yet wired to any real NpcDefinition/dialogue/store.
        // Row A (south of the square): Mayor's Hall, Shrine of Silas, The Iron Anvil (Barnaby), Restful Hearth
        // Inn & Tavern (Elida/Grumble), Fendrick's Apothecary.
        // Row C (north of the square, beyond the caverns road): Marjorie's General Store, Guard Barracks (Kael),
        // Lyra's Arcanist Study, Silvan's Fletcher.
        // Plus two open (unwalled) features near the square: Market Tents (Elara's recruit spot) and Old Man
        // Jenkins' bench -- matching the reference image's own "features of the Town Square" grouping.

        [MenuItem("Barrows End/Content/Author Town")]
        public static void Author() => Author(overwrite: false);

        [MenuItem("Barrows End/Content/Reset Town (OVERWRITES hand edits)")]
        public static void Reset()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Reset the Town?",
                    "This regenerates Town.unity from the code layout and DISCARDS anything painted or placed by hand in it.", "Reset", "Cancel")) return;
            Author(overwrite: true);
        }

        public static void Author(bool overwrite)
        {
            BarrowsEndTileAuthor.Author();
            Directory.CreateDirectory(BarrowsEndLocationAuthor.Folder);
            AssetDatabase.Refresh();

            if (!overwrite && File.Exists(Path))
            {
                Debug.Log("[BarrowsEnd] Town already exists: left untouched (use 'Reset Town' to regenerate).");
                return;
            }

            var l = new Layout(Width, Depth);
            l.FillGround(Grass, 0, 0, Width - 1, Depth - 1);

            const int squareX0 = 28, squareY0 = 20, squareX1 = 39, squareY1 = 29;
            l.FillGround(Stone, squareX0, squareY0, squareX1, squareY1);

            const int roadX0 = 32, roadX1 = 35;
            l.FillGround(Dirt, roadX0, squareY1 + 1, roadX1, Depth - 5); // north road, square edge up to the gate

            // Palisade, with a gap in the north wall for the caverns road.
            l.FillObjects(Fence, 6, 3, 64, 3);
            l.FillObjects(Fence, 6, Depth - 4, roadX0 - 1, Depth - 4);
            l.FillObjects(Fence, roadX1 + 1, Depth - 4, 64, Depth - 4);
            l.FillObjects(Fence, 3, 3, 3, Depth - 4);
            l.FillObjects(Fence, 67, 3, 67, Depth - 4);

            // Row A (south of the square): 5 buildings, 12-cell column spacing (8 wide, 4-cell gutters).
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 6, 9, 13, 16, Cell(9, 16));   // Mayor's Hall
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 18, 9, 25, 16, Cell(21, 16)); // Shrine of Silas
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 30, 9, 37, 16, Cell(33, 16)); // The Iron Anvil (Barnaby)
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 42, 9, 49, 16, Cell(45, 16)); // Restful Hearth Inn & Tavern (Elida/Grumble)
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 54, 9, 61, 16, Cell(57, 16)); // Fendrick's Apothecary

            // Row C (north of the square, beyond the road): 4 buildings, skipping the road's own column.
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 6, 33, 13, 40, Cell(9, 33));   // Marjorie's General Store
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 18, 33, 25, 40, Cell(21, 33)); // Guard Barracks (Kael)
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 42, 33, 49, 40, Cell(45, 33)); // Lyra's Arcanist Study
            CarveRoom(l, WallTile, WoodFloor, DoorTile, 54, 33, 61, 40, Cell(57, 33)); // Silvan's Fletcher

            // Market tents, west of the road just north of the square -- open ground, not walled.
            l.FillObjects(MarketStall, 22, 31, 23, 32);
            l.FillObjects(Tent, 24, 31, 27, 32);

            // Old Man Jenkins' bench, inside the square itself.
            l.Objects[30, 24] = Barrel; // a simple seat placeholder -- swap for a real bench prop once one's confirmed in the sheet

            // Tree border, matching the demo's own convention.
            l.FillObjects(TreeA, 0, 0, 0, Depth - 1);
            l.FillObjects(TreeB, Width - 1, 0, Width - 1, Depth - 1);
            l.FillObjects(TreeA, 1, Depth - 1, Width / 2 - 1, Depth - 1);
            l.FillObjects(TreeB, Width / 2, Depth - 1, Width - 2, Depth - 1);
            l.FillObjects(TreeB, 1, 0, Width / 2 - 1, 0);
            l.FillObjects(TreeA, Width / 2, 0, Width - 2, 0);

            var scene = NewLocationScene("Town", l, out var root, out var grid);
            AddDestination(root, StartDestination, Cell(33, 22), grid);
            AddDestination(root, FromCavernsDestination, Cell(33, Depth - 6), grid);
            AddTravelPoint(root, "TravelToCaverns", Cell(33, Depth - 4), Cell(33, Depth - 5),
                BarrowsEndCavernsAuthor.UpperPath, BarrowsEndCavernsAuthor.UpperFromTownDestination, ArchSprite());

            var content = root.AddComponent<LocationContent>();
            AuthorProofFixtures(root, content);
            AuthorPalisadeEncounterTrigger(root);
            BarrowsEndAct1NpcAuthor.Author(root, content);

            BakeAndSave(scene, root, Path);
            Debug.Log($"[BarrowsEnd] Town authored: {Path}");
        }

        /// <summary>Task 6.0's own end-to-end proof, per the plan's T6.0-D5: one placeholder talkable NPC and
        /// one placeholder vendor NPC, explicitly NOT any of the 10 real named NPCs (Task 6.1's job) -- proves
        /// the whole TalkInteractionAdapter/RequestLongRestAction/RequestSaveAction/StoreContentPoint chain works
        /// against a REAL scene before the real cast gets built on top of it.</summary>
        // Duplicated (not referenced) from BarrowsEndUiAuthor.GalleryPanelSettingsPath -- BarrowsEnd.Gate.Editor
        // (which owns that class) already references BarrowsEnd.Content.Editor (which owns this class), so the
        // reverse reference would cycle. Same "duplicate the path string across the boundary" convention
        // ShellTests.cs already uses for BarrowsEndLocationAuthor's own scene paths.
        private const string GalleryPanelSettingsPath = "Assets/Content/Ui/gallery-panel-settings.asset";

        private static void AuthorProofFixtures(GameObject root, LocationContent content)
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(GalleryPanelSettingsPath)
                ?? throw new System.InvalidOperationException($"{GalleryPanelSettingsPath} is missing -- author the UI content first.");

            // "Town Crier" -- a placeholder talker proving RequestLongRestAction/RequestSaveAction against a
            // real ConversationController, not just a unit test fixture. Placed near the "start" spawn point
            // (33,22) with almost no horizontal (X) offset, offset only south in Z -- found live (via two
            // throwaway diagnostic runs, measuring the real screen-space scale: 48px/world-unit, spawn dead
            // centre on screen at boot) that the HUD's own top-left/top-right regions leave only a ~120px-wide
            // safe column (screen X roughly 260-380) between them; anything beyond about +-1.25 world units of
            // horizontal offset from spawn lands inside a HUD region and the click is silently suppressed as
            // "over UI" -- nothing to do with the interaction mechanics themselves. A vertical (Z) offset stays
            // screen-X-safe regardless of magnitude, so both fixtures differ mainly in Z.
            var (_, talkerAdapter) = AddTalkPoint(root, "ProofFixture_TownCrier", Cell(33, 20), Cell(33, 21), ArchSprite());
            SetField(talkerAdapter, "panelSettings", panelSettings);
            SetField(talkerAdapter, "dialogue", BuildTalkerDialogue());

            // "Peddler" -- a placeholder vendor proving a real StoreContentPoint registration (session-
            // persistent, save/load-safe) rather than a store the dialogue owned on its own (Opus review B3).
            // Same screen-safe placement reasoning as the Town Crier above (small X offset only).
            var (_, vendorAdapter) = AddTalkPoint(root, "ProofFixture_Peddler", Cell(32, 20), Cell(32, 21), ArchSprite());
            SetField(vendorAdapter, "panelSettings", panelSettings);
            SetField(vendorAdapter, "dialogue", BuildVendorDialogue());

            var helm = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{BarrowsEndGearAuthor.GearDir}/item-iron-helm.asset")
                ?? throw new System.InvalidOperationException("item-iron-helm is missing -- author gear first.");

            // LocationContent's own lists are private SerializeFields -- set via SerializedObject.
            var so = new SerializedObject(content);
            var storesProp = so.FindProperty("stores");
            storesProp.arraySize = 1;
            var storeElement = storesProp.GetArrayElementAtIndex(0);
            storeElement.FindPropertyRelative("StoreId").stringValue = "store-proof-fixture-peddler";
            storeElement.FindPropertyRelative("Adapter").objectReferenceValue = vendorAdapter;
            var listingsProp = storeElement.FindPropertyRelative("InitialListings");
            listingsProp.arraySize = 1;
            var listingElement = listingsProp.GetArrayElementAtIndex(0);
            listingElement.FindPropertyRelative("Item").objectReferenceValue = helm;
            listingElement.FindPropertyRelative("Quantity").intValue = 5;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Task 6.1 (T6.1-D6, fact 15's placement fix): sits well inside the north road corridor
        /// (x in [32,35], matching the palisade gap itself) but clearly offset from BOTH the TravelToCaverns
        /// point (Cell(33,Depth-4)) and the FromCavernsDestination arrival point (Cell(33,Depth-6)) -- a party
        /// walking the road to the caverns passes through it before ever reaching either travel point, and a
        /// party arriving FROM the caverns lands well past it, never re-triggering it on return (its own
        /// persisted "already fired" Timeline flag is the real one-shot guard; the placement just avoids the
        /// worse failure mode of firing on travel-point approach itself).</summary>
        private static void AuthorPalisadeEncounterTrigger(GameObject root)
        {
            var go = new GameObject("PalisadeBreachTrigger");
            go.transform.SetParent(root.transform, false);
            go.transform.position = Centre(Cell(33, 41));
            var collider = go.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 2.5f;
            go.AddComponent<EncounterTrigger>();
        }

        private static DialogueDefinition BuildTalkerDialogue()
        {
            SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

            // Rest/save fire FIRST (auto-settling silently, no line to advance through) so both take effect the
            // instant the conversation opens -- the greeting afterward is flavor a live player reads, not a gate
            // the mechanical effects wait behind.
            var rest = new ActionNode();
            SetField(rest, "nodeId", Id("rest"));
            SetField(rest, "action", new RequestLongRestAction());
            SetField(rest, "nextNodeId", Id("save"));

            var save = new ActionNode();
            SetField(save, "nodeId", Id("save"));
            SetField(save, "action", new RequestSaveAction());
            SetField(save, "nextNodeId", Id("greeting"));

            var greeting = new LineNode();
            SetField(greeting, "nodeId", Id("greeting"));
            SetField(greeting, "speakerName", "Town Crier");
            SetField(greeting, "text", "There now -- rested and saved. Safe travels.");
            SetField(greeting, "nextNodeId", Id("end"));

            var end = new EndNode();
            SetField(end, "nodeId", Id("end"));

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-proof-fixture-town-crier");
            SetField(dialogue, "startNodeId", Id("rest"));
            SetField(dialogue, "nodes", new List<DialogueNode> { rest, save, greeting, end });

            var path = $"{BarrowsEndGearAuthor.NpcDir}/dlg-proof-fixture-town-crier.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(dialogue, path);
            return dialogue;
        }

        private static DialogueDefinition BuildVendorDialogue()
        {
            SerializableGuid Id(string seed) => SerializableGuid.FromString(seed);

            var openStore = new ActionNode();
            SetField(openStore, "nodeId", Id("open-store"));
            SetField(openStore, "action", new OpenStoreAction());
            SetField(openStore, "nextNodeId", Id("end"));

            var end = new EndNode();
            SetField(end, "nodeId", Id("end"));

            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            SetField(dialogue, "id", "dlg-proof-fixture-peddler");
            SetField(dialogue, "startNodeId", Id("open-store"));
            SetField(dialogue, "nodes", new List<DialogueNode> { openStore, end });

            var path = $"{BarrowsEndGearAuthor.NpcDir}/dlg-proof-fixture-peddler.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(dialogue, path);
            return dialogue;
        }
    }
}
