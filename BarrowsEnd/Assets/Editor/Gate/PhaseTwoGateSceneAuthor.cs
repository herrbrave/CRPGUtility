using System.IO;
using System.Linq;
using BarrowsEnd.Gate;
using CRPG.Core;
using CRPG.Gameplay.Interactions;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.InputSystem;
using CRPG.Navigation.Grid;
using CRPG.Presentation.Sprites;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// Task 2.7b: authors the Phase 2 gate scene from <see cref="GateLayout"/> -- never hand-edited, safe to re-run. Ground and props are
    /// individual flat SpriteRenderers (Tilemaps are Phase 3) drawn from the copied Kenney sheet; a real 3D ground collider lets the
    /// click router raycast; the door is a UseDoorInteractionAdapter + GridWalkabilityBlocker on ONE GameObject with an explicit
    /// interaction anchor (WorldInteractable logs an error without one).
    /// </summary>
    public static class PhaseTwoGateSceneAuthor
    {
        public const string ScenePath = "Assets/Scenes/Gate/PhaseTwoGate.unity";
        public const string PanelSettingsPath = "Assets/Content/Gate/gate-panel-settings.asset";
        public const string LookSpikeScenePath = "Assets/Scenes/Spike/LookSpike.unity";

        [MenuItem("Barrows End/Gate/Build Phase Two Gate")]
        public static void Build()
        {
            EnsureFolder("Assets/Scenes/Gate");
            EnsureFolder("Assets/Content/Gate");
            BarrowsEndGearAuthor.Author(); // the ruleset asset the bootstrap needs, up to date

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Everything below is loaded AFTER NewScene (assets loaded before it can turn into fake-null references).
            var sprites = AssetDatabase.LoadAllAssetsAtPath(GateLayout.SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
            Sprite S(Vector2Int c) => sprites.TryGetValue($"{GateLayout.SheetName}_{c.x}_{c.y}", out var sprite)
                ? sprite
                : throw new System.InvalidOperationException($"Sprite {GateLayout.SheetName}_{c.x}_{c.y} is not sliced.");

            var ruleset = AssetDatabase.LoadAssetAtPath<BarrowsEndRulesetProvider>(BarrowsEndGearAuthor.RulesetPath);
            var partyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrowsEndCharacterAuthor.SpriteCharacterPath);

            var cameraGo = BuildCamera(out var gateCamera);
            var ground = BuildGround(S);
            var props = BuildProps(S);
            var (gridProvider, door) = BuildGridAndDoor(ground, S);
            var inputSource = BuildInput(cameraGo.GetComponent<Camera>());
            var panelDoc = BuildPanelDocument();

            var bootstrapGo = new GameObject("GateBootstrap");
            var bootstrap = bootstrapGo.AddComponent<GateBootstrap>();
            var bso = new SerializedObject(bootstrap);
            bso.FindProperty("ruleset").objectReferenceValue = ruleset;
            bso.FindProperty("partyPrefab").objectReferenceValue = partyPrefab;
            bso.FindProperty("gridProvider").objectReferenceValue = gridProvider;
            bso.FindProperty("inputSource").objectReferenceValue = inputSource;
            bso.FindProperty("gateCamera").objectReferenceValue = gateCamera;
            bso.FindProperty("doorObject").objectReferenceValue = door;
            bso.FindProperty("skeletonCell").vector2IntValue = GateLayout.SkeletonCell;
            var members = GateLayout.DefaultMembers();
            var membersProp = bso.FindProperty("members");
            membersProp.arraySize = members.Length;
            for (var i = 0; i < members.Length; i++)
            {
                var element = membersProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("displayName").stringValue = members[i].displayName;
                element.FindPropertyRelative("spawnId").stringValue = members[i].spawnId;
                element.FindPropertyRelative("cell").vector2IntValue = members[i].cell;
                element.FindPropertyRelative("maxHp").intValue = members[i].maxHp;
                SetStrings(element.FindPropertyRelative("appearance"), members[i].appearance);
                SetStrings(element.FindPropertyRelative("gear"), members[i].gear);
            }

            bso.ApplyModifiedPropertiesWithoutUndo();

            var panel = panelDoc.gameObject.AddComponent<GatePanel>();
            var pso = new SerializedObject(panel);
            pso.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            pso.FindProperty("gateCamera").objectReferenceValue = gateCamera;
            pso.ApplyModifiedPropertiesWithoutUndo();

            // No EventSystem: Unity 6 UI Toolkit runtime panels take pointer input straight from the Input System package; a click on the panel
            // is kept from becoming a world click by UIToolkitPointerCapture (registered by GatePanel).

            Physics.SyncTransforms(); // auto-sync is off in this project: make the authored colliders visible to raycasts immediately
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(LookSpikeScenePath, true),
            };
            Debug.Log($"[BarrowsEnd] Gate scene built: {ScenePath} ({props} props, door at {GateLayout.DoorCell}).");
        }

        [MenuItem("Barrows End/Gate/Open Phase Two Gate")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        private static void SetStrings(SerializedProperty array, string[] values)
        {
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        private static GameObject BuildCamera(out GateCamera gateCamera)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.07f, 0.1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            gateCamera = go.AddComponent<GateCamera>();
            go.transform.SetPositionAndRotation(new Vector3(6f, 20f, 9f), Quaternion.Euler(90f, 0f, 0f));
            return go;
        }

        private static Vector3 CellCentre(Vector2Int cell) =>
            GateLayout.Origin + new Vector3((cell.x + 0.5f) * GateLayout.CellSize, 0f, (cell.y + 0.5f) * GateLayout.CellSize);

        private static SpriteRenderer FlatSprite(string name, Transform parent, Sprite sprite, Vector3 position, string layer, int order)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>A prop sprite on the World layer, y-sorted against the characters by its own cell (the prop lane of the sorting contract).</summary>
        private static SpriteRenderer PropSprite(string name, Transform parent, Sprite sprite, Vector2Int cell)
        {
            var sr = FlatSprite(name, parent, sprite, CellCentre(cell) + new Vector3(0f, 0.02f, 0f), SpriteSortingContract.WorldLayer, 0);
            sr.gameObject.AddComponent<SpriteDepthSorter>(); // anchor = its own transform
            return sr;
        }

        private static Transform BuildGround(System.Func<Vector2Int, Sprite> S)
        {
            var root = new GameObject("Ground").transform;
            for (var x = 0; x < GateLayout.Width; x++)
            for (var z = 0; z < GateLayout.Depth; z++)
                FlatSprite($"Tile {x},{z}", root, S(GateLayout.GroundSprite(x, z)), CellCentre(new Vector2Int(x, z)), SpriteSortingContract.GroundLayer, 0);

            // The click router raycasts PHYSICS: a sprite has no collider, so the ground needs a real one.
            var collider = new GameObject("GroundCollider");
            collider.transform.SetParent(root, false);
            var box = collider.AddComponent<BoxCollider>();
            box.size = new Vector3(GateLayout.Width * GateLayout.CellSize, 0.1f, GateLayout.Depth * GateLayout.CellSize);
            collider.transform.position = GateLayout.Origin + new Vector3(GateLayout.Width * 0.5f, -0.05f, GateLayout.Depth * 0.5f) * GateLayout.CellSize;
            return root;
        }

        private static int BuildProps(System.Func<Vector2Int, Sprite> S)
        {
            var root = new GameObject("Props").transform;
            var count = 0;
            foreach (var cell in GateLayout.WallCells())
            {
                PropSprite($"Wall {cell.x}", root, S(GateLayout.WallTile), cell);
                count++;
            }

            foreach (var prop in GateLayout.SolidProps)
            {
                PropSprite(prop.Name, root, S(prop.Sprite), prop.Cell);
                count++;
            }

            return count;
        }

        private static (WalkabilityGridProvider provider, GameObject door) BuildGridAndDoor(Transform ground, System.Func<Vector2Int, Sprite> S)
        {
            var gridGo = new GameObject("WalkabilityGrid");
            var provider = gridGo.AddComponent<WalkabilityGridProvider>();
            provider.Configure(GateLayout.Origin, GateLayout.CellSize, GateLayout.Width, GateLayout.Depth,
                GateLayout.BlockedCells().Select(c => new GridCoordinate(c.x, c.y)));

            var door = new GameObject("Door");
            door.transform.position = CellCentre(GateLayout.DoorCell);
            var box = door.AddComponent<BoxCollider>();
            box.size = new Vector3(0.9f, 1f, 0.9f);
            box.center = new Vector3(0f, 0.5f, 0f);

            var closed = PropSprite("Closed", door.transform, S(GateLayout.DoorClosed), GateLayout.DoorCell);
            var open = PropSprite("Open", door.transform, S(GateLayout.DoorOpen), GateLayout.DoorCell);
            open.gameObject.SetActive(false);

            var blocker = door.AddComponent<GridWalkabilityBlocker>();
            blocker.Configure(provider, new[] { new GridCoordinate(GateLayout.DoorCell.x, GateLayout.DoorCell.y) }, startBlocking: true);

            var anchor = new GameObject("InteractionAnchor").transform;
            anchor.SetParent(door.transform, false);
            anchor.position = CellCentre(GateLayout.DoorAnchorCell);

            var interactable = door.AddComponent<WorldInteractable>(); // RequireComponent of the adapter would add it anyway
            var adapter = door.AddComponent<UseDoorInteractionAdapter>();
            var aso = new SerializedObject(adapter);
            aso.FindProperty("closedVisual").objectReferenceValue = closed.gameObject;
            aso.FindProperty("openVisual").objectReferenceValue = open.gameObject;
            aso.ApplyModifiedPropertiesWithoutUndo();
            interactable.Configure(anchor, 1.5f);
            return (provider, door);
        }

        private static InputSystemCommandSource BuildInput(Camera camera)
        {
            var go = new GameObject("Input");
            var source = go.AddComponent<InputSystemCommandSource>();
            var so = new SerializedObject(source);
            so.FindProperty("raycastCamera").objectReferenceValue = camera;
            so.ApplyModifiedPropertiesWithoutUndo();
            return source;
        }

        private static UIDocument BuildPanelDocument()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
                AssetDatabase.SaveAssets();
                settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            }

            var go = new GameObject("GatePanel");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            return doc;
        }

        private static void EnsureFolder(string path)
        {
            if (Directory.Exists(path)) return;
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
    }
}
