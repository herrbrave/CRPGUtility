using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Camera;
using CRPG.Exploration;
using CRPG.Gameplay.Scenes;
using CRPG.InputSystem;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VikingRaid.Game;
using VikingRaid.Village;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Sets up the village scene for the CRPG toolkit. Copies the Synty Viking Realm <c>Demo</c> scene to <c>Assets/Scenes/VikingVillage.unity</c> (the original is never touched) and
    /// configures the copy; every step is idempotent, so the menu item can be run again after the scene has been edited and will not move an anchor or an NPC that is already there.
    /// <list type="number">
    /// <item><b>Geometry classification.</b> The demo is a modular kit (about 1,000 ground tiles, 24 buildings of 65-300 pieces, 460 props, 300 trees, 860 rocks) with almost no
    /// physics, so: ground tiles, roads, docks and piers go on the <c>Ground</c> layer (walkable, clickable); buildings, props, fences, boats, rocks and stone walls go on the
    /// <c>Obstacle</c> layer (they carve the NavMesh; their tops are forced Not Walkable by a <see cref="NavMeshModifier"/>); pines get a trunk-sized capsule (the canopy must not
    /// block); foliage, grass, water, clouds and the background are left alone. A piece with no collider gets a mesh collider (LOD0 only).</item>
    /// <item><b>NavMesh</b>, baked from physics colliders on those two layers and saved next to the scene.</item>
    /// <item><b>The toolkit rig</b>: the demo's own camera is moved under an <see cref="IsometricCameraRig"/> (keeping its post-processing and clip settings) with a
    /// <see cref="PartyCameraFollowBehaviour"/>; an <see cref="InputSystemCommandSource"/> with the Viking Raid hittable mask; a <see cref="LocationContent"/> holding the NPC spawn
    /// points (three demo villagers on first run, anchors under <c>CRPG/NPCs</c>); a <c>PartyStart</c> anchor on the largest connected piece of walkable ground; and the
    /// <see cref="VillageBootstrap"/> that ties them together.</item>
    /// </list>
    /// </summary>
    public static class VillageSceneAuthor
    {
        public const string SourceScenePath = "Assets/Synty/PolygonVikingRealm/Scenes/Demo.unity";
        public const string ScenePath = "Assets/Scenes/VikingVillage.unity";
        public const string GeneratedDir = "Assets/Scenes/VikingVillage";
        public const string CameraSettingsPath = "Assets/Village/VillageCamera.asset";
        private const string RootName = "CRPG";

        [MenuItem("Viking Raid/Village/1. Author Village Scene")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RunUnattended();
        }

        /// <summary>Throws the village scene away and builds it again from the original demo. Everything done to the copy by hand (moved anchors, added NPCs, painted props) is LOST,
        /// which is why it asks first.</summary>
        [MenuItem("Viking Raid/Village/Rebuild Scene From The Demo (discards all edits)")]
        public static void Rebuild()
        {
            if (!EditorUtility.DisplayDialog("Rebuild the village scene?", $"This deletes {ScenePath} and everything you have changed in it, and builds it again from the Synty demo scene.", "Rebuild", "Cancel")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RebuildUnattended();
        }

        public static void RebuildUnattended()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // the scene being deleted must not be open
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(GeneratedDir);
            RunUnattended();
        }

        /// <summary>The same without the save prompt (batch mode).</summary>
        public static void RunUnattended()
        {
            CharacterRosterAuthor.RunVillagers();
            VillageContentAuthor.Run();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null) throw new InvalidOperationException($"{SourceScenePath} is missing: import the Viking Realm package.");
                if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath)) throw new InvalidOperationException($"could not copy {SourceScenePath} to {ScenePath}");
                Debug.Log($"[VillageSceneAuthor] copied {SourceScenePath} -> {ScenePath}");
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var report = new List<string>();
            Configure(scene, report);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[VillageSceneAuthor] done:\n  " + string.Join("\n  ", report));
        }

        // ------------------------------------------------------------------ configure
        private static void Configure(Scene scene, List<string> report)
        {
            foreach (var (layer, name) in new[] { (WorldLayers.Ground, "Ground"), (WorldLayers.Obstacle, "Obstacle"), (WorldLayers.Character, "Character"), (WorldLayers.Scenery, "Scenery") })
                if (LayerMask.LayerToName(layer) != name) throw new InvalidOperationException($"layer {layer} should be named '{name}' (run the project setup: 'Viking Raid/Task 0/Project Setup').");

            var root = FindOrCreateRoot(scene, RootName);
            ClassifyGeometry(scene, report);
            var playable = PlayableBounds(scene, report);
            BuildNavMesh(scene, root.transform, report);
            var camera = BuildCameraRig(scene, root.transform);
            var start = EnsurePartyStart(scene, root.transform, playable, report);
            var location = EnsureLocation(root.transform, start, playable, report);
            WireGame(root.transform, camera.rig, camera.follow, location, start);
        }

        // ------------------------------------------------------------------ geometry
        private static void ClassifyGeometry(Scene scene, List<string> report)
        {
            var terrain = FindPath(scene, "Environment/Terrain");
            if (terrain == null) throw new InvalidOperationException("Environment/Terrain not found: is this the Viking Realm demo scene?");

            var added = 0; var ground = 0; var obstacles = 0;
            foreach (Transform piece in terrain)
            {
                var isGround = piece.name.Contains("Ground") || piece.name.Contains("Road");
                added += EnsureColliders(piece, isGround ? WorldLayers.Ground : WorldLayers.Obstacle);
                if (isGround) ground++; else { obstacles++; Modifier(piece.gameObject, notWalkable: true); }
            }

            report.Add($"Terrain: {ground} ground pieces (Ground layer), {obstacles} wall/pillar pieces (Obstacle, not walkable)");

            var village = FindPath(scene, "Village");
            if (village != null)
            {
                Modifier(village.gameObject, notWalkable: true);   // roofs, crates and boat decks are not floors
                foreach (var groupName in new[] { "Buildings", "Structure", "Props", "Vehicles" })
                {
                    var group = village.Find(groupName);
                    if (group == null) continue;
                    var pieces = 0;
                    foreach (Transform piece in group)
                    {
                        var walkway = piece.name.Contains("Dock") || piece.name.Contains("Pier");
                        added += EnsureColliders(piece, walkway ? WorldLayers.Ground : WorldLayers.Obstacle);
                        if (walkway) Modifier(piece.gameObject, notWalkable: false);   // overrides the parent's Not Walkable: docks and piers can be walked on
                        pieces++;
                    }

                    report.Add($"Village/{groupName}: {pieces} pieces");
                }
            }

            var rocks = FindPath(scene, "Environment/Rock");
            if (rocks != null)
            {
                var big = 0;
                foreach (Transform rock in rocks)
                {
                    if (rock.name.Contains("Pebble")) continue;   // pebbles are decoration, not obstacles
                    added += EnsureColliders(rock, WorldLayers.Obstacle);
                    big++;
                }

                Modifier(rocks.gameObject, notWalkable: true);
                report.Add($"Environment/Rock: {big} rocks as obstacles (pebbles left alone)");
            }

            var trees = FindPath(scene, "Environment/Trees");
            if (trees != null)
            {
                var trunks = 0;
                foreach (Transform tree in trees)
                {
                    if (!tree.name.Contains("Tree")) continue;   // logs and stumps are walkable decoration
                    if (AddTrunkCollider(tree)) trunks++;
                    foreach (var c in tree.GetComponentsInChildren<Collider>(true)) SetLayer(c.gameObject, WorldLayers.Obstacle);
                }

                Modifier(trees.gameObject, notWalkable: true);
                report.Add($"Environment/Trees: {trunks} trunk capsules added");
            }

            Physics.SyncTransforms();
            report.Add($"colliders added in total: {added}");
        }

        /// <summary>Gives every LOD0 mesh of the piece a collider if it has none, and puts every collider of the piece on <paramref name="layer"/>. Returns the number of colliders added.</summary>
        private static int EnsureColliders(Transform piece, int layer)
        {
            var added = 0;
            foreach (var filter in piece.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !IsLod0(filter) || filter.TryGetComponent<Collider>(out _)) continue;
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                added++;
            }

            foreach (var collider in piece.GetComponentsInChildren<Collider>(true)) SetLayer(collider.gameObject, layer);
            return added;
        }

        private static bool IsLod0(MeshFilter filter)
        {
            var lodGroup = filter.GetComponentInParent<LODGroup>();
            if (lodGroup == null) return true;
            var lods = lodGroup.GetLODs();
            return lods.Length > 0 && filter.TryGetComponent<Renderer>(out var renderer) && lods[0].renderers.Contains(renderer);
        }

        private static bool AddTrunkCollider(Transform tree)
        {
            if (tree.GetComponentInChildren<Collider>(true) != null) return false;
            var renderers = tree.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            var scale = Mathf.Max(0.01f, tree.lossyScale.x);
            var worldRadius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * 0.1f, 0.3f, 0.7f);
            var worldHeight = Mathf.Min(bounds.size.y, 4f);
            var capsule = tree.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = worldRadius / scale;
            capsule.height = worldHeight / scale;
            capsule.center = tree.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y + worldHeight / 2f, bounds.center.z));
            return true;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            if (go.layer == layer) return;
            go.layer = layer;
            PrefabUtility.RecordPrefabInstancePropertyModifications(go);   // a layer change on a prefab instance is an override: without this it is not saved with the scene
        }

        private static void Modifier(GameObject go, bool notWalkable)
        {
            var modifier = go.GetComponent<NavMeshModifier>() ?? go.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = notWalkable ? NavMesh.GetAreaFromName("Not Walkable") : 0;
            modifier.applyToChildren = true;
        }

        /// <summary>The area the village actually occupies: every renderer under the Village root (buildings, props, fences, boats), plus a margin. The ground group is NOT used: it includes
        /// enormous background planes (the demo's ground reaches 2.6 km out), which would make the camera bounds and every search meaningless.</summary>
        private static Bounds PlayableBounds(Scene scene, List<string> report)
        {
            var village = FindPath(scene, "Village") ?? throw new InvalidOperationException("Village group not found.");
            var renderers = village.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            bounds.Expand(new Vector3(40f, 0f, 40f));   // Expand adds twice the amount to the size: 20 m of margin each side
            report.Add($"playable area (XZ): x {bounds.min.x:F0}..{bounds.max.x:F0}, z {bounds.min.z:F0}..{bounds.max.z:F0}");
            return bounds;
        }

        /// <summary>The ground surface under (x, z): a ray from high above against the Ground layer only (roofs and canopies are other layers).</summary>
        private static bool GroundPoint(float x, float z, out Vector3 point)
        {
            if (Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out var hit, 800f, 1 << WorldLayers.Ground)) { point = hit.point; return true; }
            point = default;
            return false;
        }

        /// <summary>The nearest walkable point to (x, z), found by spiralling outward: on the ground surface, on the NavMesh, with at least <paramref name="minClearance"/> metres to the nearest
        /// edge, and (when <paramref name="reachFrom"/> is given) connected to it.</summary>
        private static bool FindWalkable(Vector3 ideal, float searchRadius, float minClearance, Vector3? reachFrom, out Vector3 result)
        {
            var path = new NavMeshPath();
            for (var radius = 0f; radius <= searchRadius; radius += 1.5f)
            {
                var steps = radius < 0.1f ? 1 : Mathf.Max(8, Mathf.CeilToInt(radius * 2f));
                for (var i = 0; i < steps; i++)
                {
                    var angle = i * Mathf.PI * 2f / steps;
                    var x = ideal.x + Mathf.Cos(angle) * radius; var z = ideal.z + Mathf.Sin(angle) * radius;
                    if (!GroundPoint(x, z, out var ground) || !NavMesh.SamplePosition(ground, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                    if (minClearance > 0f && NavMesh.FindClosestEdge(hit.position, out var edge, NavMesh.AllAreas) && edge.distance < minClearance) continue;
                    if (reachFrom.HasValue && !(NavMesh.CalculatePath(reachFrom.Value, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)) continue;
                    result = hit.position;
                    return true;
                }
            }

            result = default;
            return false;
        }

        // ------------------------------------------------------------------ navmesh
        private static void BuildNavMesh(Scene scene, Transform root, List<string> report)
        {
            var host = FindOrCreateChild(root, "NavMesh");
            var surface = host.GetComponent<NavMeshSurface>() ?? host.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = WorldLayers.NavMesh;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.defaultArea = 0;

            var path = $"{GeneratedDir}/NavMesh.asset";
            EnsureFolder(GeneratedDir);
            surface.RemoveData();
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null) AssetDatabase.DeleteAsset(path);
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            EditorUtility.SetDirty(surface);

            var triangulation = NavMesh.CalculateTriangulation();
            report.Add($"NavMesh: {triangulation.indices.Length / 3} triangles, {triangulation.vertices.Length} vertices, saved to {path}");
        }

        // ------------------------------------------------------------------ camera
        private static (IsometricCameraRig rig, PartyCameraFollowBehaviour follow, UnityEngine.Camera camera) BuildCameraRig(Scene scene, Transform root)
        {
            var settings = LoadOrCreateCameraSettings();
            var existing = UnityEngine.Object.FindFirstObjectByType<IsometricCameraRig>();
            if (existing != null) return (existing, existing.GetComponent<PartyCameraFollowBehaviour>(), existing.GetComponentInChildren<UnityEngine.Camera>());

            var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnityEngine.Camera>(true)).FirstOrDefault(c => c.CompareTag("MainCamera"))
                         ?? scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnityEngine.Camera>(true)).FirstOrDefault();
            if (camera == null)
            {
                camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<UnityEngine.Camera>();
                camera.gameObject.AddComponent<AudioListener>();
            }

            var rigRoot = new GameObject("CameraRig");
            rigRoot.transform.SetParent(root, false);
            rigRoot.SetActive(false);   // the rig's fields must be in place before its Awake applies them
            var yaw = new GameObject("YawPivot").transform; yaw.SetParent(rigRoot.transform, false);
            var pitch = new GameObject("PitchPivot").transform; pitch.SetParent(yaw, false);
            camera.transform.SetParent(pitch, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;

            var rig = rigRoot.AddComponent<IsometricCameraRig>();
            SetObject(rig, "yawPivot", yaw); SetObject(rig, "pitchPivot", pitch); SetObject(rig, "cameraTransform", camera.transform); SetObject(rig, "settings", settings);
            var follow = rigRoot.AddComponent<PartyCameraFollowBehaviour>();
            SetObject(follow, "cameraRig", rig); SetObject(follow, "settings", settings);
            rigRoot.SetActive(true);
            return (rig, follow, camera);
        }

        private static CRPGCameraSettings LoadOrCreateCameraSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CRPGCameraSettings>(CameraSettingsPath);
            var created = settings == null;
            if (created) settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();

            // a village is walked at human scale: close in, steeper than a map view, rotatable
            Set(settings, "panSpeed", 22f); Set(settings, "rotationSpeed", 90f); Set(settings, "zoomSpeed", 6f);
            Set(settings, "minimumZoom", 8f); Set(settings, "maximumZoom", 70f); Set(settings, "defaultZoom", 32f);   // this village is hilly: a low camera ends up behind a hill
            Set(settings, "minimumPitch", 30f); Set(settings, "maximumPitch", 80f); Set(settings, "defaultPitch", 55f);
            Set(settings, "snapRotationDegrees", 45f); Set(settings, "followSelection", true); Set(settings, "followSmoothingSpeed", 6f); Set(settings, "enableEdgeScrolling", false);

            if (created) { EnsureFolder("Assets/Village"); AssetDatabase.CreateAsset(settings, CameraSettingsPath); }
            else EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<CRPGCameraSettings>(CameraSettingsPath);
        }

        // ------------------------------------------------------------------ anchors and content
        private static Transform EnsurePartyStart(Scene scene, Transform root, Bounds playable, List<string> report)
        {
            var existing = root.Find("PartyStart");
            if (existing != null) return existing;

            var centre = playable.center;
            var buildings = FindPath(scene, "Village/Buildings");
            if (buildings != null)
            {
                var renderers = buildings.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0) { var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); centre = b.center; }
            }

            var anchor = new GameObject("PartyStart").transform;
            anchor.SetParent(root, false);
            var chosen = ChooseOpenGround(centre, playable, report);
            anchor.position = chosen;
            anchor.rotation = Quaternion.LookRotation(FlatDirection(chosen, centre, Vector3.forward));
            return anchor;
        }

        /// <summary>The walkable point nearest the village centre that has room around it and can reach the most of the walkable ground (so the party never starts on an island).
        /// Clearance is relaxed in steps (3, 2, 1 m): the village is dense.</summary>
        private static Vector3 ChooseOpenGround(Vector3 centre, Bounds playable, List<string> report)
        {
            var targets = new List<Vector3>();
            for (var x = playable.min.x; x <= playable.max.x; x += 25f)
                for (var z = playable.min.z; z <= playable.max.z; z += 25f)
                    if (GroundPoint(x, z, out var g) && NavMesh.SamplePosition(g, out var t, 1.5f, NavMesh.AllAreas)) targets.Add(t.position);

            var path = new NavMeshPath();
            foreach (var clearance in new[] { 3f, 2f, 1f, 0f })
            {
                var candidates = new List<(Vector3 position, int reach, float distance)>();
                for (var dx = -70f; dx <= 70f; dx += 4f)
                    for (var dz = -70f; dz <= 70f; dz += 4f)
                    {
                        if (!GroundPoint(centre.x + dx, centre.z + dz, out var ground) || !NavMesh.SamplePosition(ground, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                        if (clearance > 0f && NavMesh.FindClosestEdge(hit.position, out var edge, NavMesh.AllAreas) && edge.distance < clearance) continue;

                        var reach = 0;
                        foreach (var target in targets) if (NavMesh.CalculatePath(hit.position, target, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) reach++;
                        candidates.Add((hit.position, reach, Vector3.Distance(Flat(hit.position), Flat(centre))));
                    }

                if (candidates.Count == 0) continue;

                // connected to the main walkable ground (within 10% of the best), and then as near the centre of the village as possible: "the most reach" alone picks a far corner
                var most = candidates.Max(c => c.reach);
                var pick = candidates.Where(c => c.reach >= most * 0.9f).OrderBy(c => c.distance).First();
                report.Add($"PartyStart: {pick.position} ({clearance:F0} m clearance, reaches {pick.reach} of {targets.Count} sample points, {pick.distance:F0} m from the village centre)");
                return pick.position;
            }

            report.Add("WARNING: no walkable ground found near the village centre; PartyStart is at the centre: move it by hand.");
            return centre;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static LocationContent EnsureLocation(Transform root, Transform start, Bounds ground, List<string> report)
        {
            var host = FindOrCreateChild(root, "Location");
            var location = host.GetComponent<LocationContent>() ?? host.AddComponent<LocationContent>();
            var anchors = FindOrCreateChild(root, "NPCs").transform;
            var serialized = new SerializedObject(location);
            var points = serialized.FindProperty("spawnPoints");

            // demo villagers only on the first run: after that the spawn points belong to whoever is dressing the scene
            if (points.arraySize == 0 && anchors.childCount == 0)
            {
                var demo = new[] { ("village-elder", "npc-village-elder", -50f), ("village-hild", "npc-village-hild", 0f), ("village-orm", "npc-village-orm", 50f) };
                foreach (var (spawnId, npcId, angle) in demo)
                {
                    var npc = VillageContentAuthor.Load(npcId);
                    var ideal = start.position + Quaternion.AngleAxis(angle, Vector3.up) * start.forward * 9f;
                    var position = FindWalkable(ideal, 10f, 1f, start.position, out var found) ? found : ideal;
                    if (position == ideal) report.Add($"WARNING: no reachable open ground near the ideal spot of {spawnId}; its anchor is at {ideal}: move it by hand.");
                    var anchor = new GameObject($"Anchor_{spawnId}").transform;
                    anchor.SetParent(anchors, false);
                    anchor.position = position;
                    anchor.rotation = Quaternion.LookRotation(FlatDirection(position, start.position, -start.forward));

                    points.InsertArrayElementAtIndex(points.arraySize);
                    var element = points.GetArrayElementAtIndex(points.arraySize - 1);
                    element.FindPropertyRelative("SpawnId").stringValue = spawnId;
                    element.FindPropertyRelative("Definition").objectReferenceValue = npc;
                    element.FindPropertyRelative("Anchor").objectReferenceValue = anchor;
                }

                report.Add($"Location: {demo.Length} demo NPC spawn points (anchors under CRPG/NPCs)");
            }

            if (!serialized.FindProperty("hasCameraBounds").boolValue)
            {
                var r = Rect.MinMaxRect(ground.min.x, ground.min.z, ground.max.x, ground.max.z);
                serialized.FindProperty("hasCameraBounds").boolValue = true;
                serialized.FindProperty("cameraBoundsXZ").rectValue = r;
                report.Add($"camera bounds (XZ): {r}");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return location;
        }

        private static void WireGame(Transform root, IsometricCameraRig rig, PartyCameraFollowBehaviour follow, LocationContent location, Transform start)
        {
            var camera = rig.GetComponentInChildren<UnityEngine.Camera>();
            var inputHost = FindOrCreateChild(root, "Input");
            var input = inputHost.GetComponent<InputSystemCommandSource>() ?? inputHost.AddComponent<InputSystemCommandSource>();
            SetObject(input, "raycastCamera", camera);
            SetObject(input, "cameraRig", rig);
            var serializedInput = new SerializedObject(input);
            serializedInput.FindProperty("hittableMask").intValue = WorldLayers.Hittable;   // not "everything": the router captures it once, so a wrong mask swallows clicks
            serializedInput.ApplyModifiedPropertiesWithoutUndo();

            var gameHost = FindOrCreateChild(root, "Bootstrap");
            var bootstrap = gameHost.GetComponent<VillageBootstrap>() ?? gameHost.AddComponent<VillageBootstrap>();
            SetObject(bootstrap, "ruleset", AssetDatabase.LoadAssetAtPath<VikingRaidRulesetProvider>(WorldAuthor.RulesetPath));
            SetObject(bootstrap, "inputSource", input);
            SetObject(bootstrap, "location", location);
            SetObject(bootstrap, "partyStart", start);
            SetObject(bootstrap, "cameraRig", rig);
            SetObject(bootstrap, "cameraFollow", follow);
        }

        // ------------------------------------------------------------------ helpers
        private static GameObject FindOrCreateRoot(Scene scene, string name)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (existing != null) return existing;
            var created = new GameObject(name);
            SceneManager.MoveGameObjectToScene(created, scene);
            return created;
        }

        private static GameObject FindOrCreateChild(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.gameObject;
            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created;
        }

        private static Transform FindPath(Scene scene, string path)
        {
            var parts = path.Split('/');
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]);
            if (root == null) return null;
            return parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts.Skip(1)));
        }

        private static Vector3 FlatDirection(Vector3 from, Vector3 to, Vector3 fallback)
        {
            var d = to - from; d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : fallback;
        }

        private static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name} has no serialized field '{field}'.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(object target, string name, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new Exception($"Field '{name}' not found on {target.GetType().Name}.");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
