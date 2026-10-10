using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VikingRaid.Game;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Task 3: builds the whole game scene from <see cref="WorldLayout"/> in one run, from nothing (re-running replaces the scene and its generated assets; a fixed seed makes
    /// the content identical, though file ids change). Stages: ground + outer scenery + anchors + NavMesh (this file), forest, camp, lighting/FX (other partial files).
    /// Menu: Viking Raid/Task 3/Author World. Batch: -executeMethod VikingRaid.EditorTools.WorldAuthor.Run
    /// </summary>
    public static partial class WorldAuthor
    {
        public const string ScenePath = "Assets/Scenes/VikingRaid.unity";
        public const string GeneratedDir = "Assets/Scenes/VikingRaid";
        public const string MaterialsDir = GeneratedDir + "/Materials";

        private static Transform worldRoot, groundRoot, sceneryRoot, forestRoot, campRoot, fxRoot, lightRoot, anchorsRoot;

        [MenuItem("Viking Raid/Task 3/Author World")]
        public static void Run()
        {
            VikingRaidProjectSetup.EnsureLayers();
            EditorSettings.asyncShaderCompilation = false;

            if (AssetDatabase.IsValidFolder(GeneratedDir)) AssetDatabase.DeleteAsset(GeneratedDir);
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), GeneratedDir, "Materials"));
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            worldRoot = new GameObject("World").transform;
            groundRoot = Child(worldRoot, "Ground"); sceneryRoot = Child(worldRoot, "Scenery"); forestRoot = Child(worldRoot, "Forest"); campRoot = Child(worldRoot, "Camp");
            fxRoot = new GameObject("FX").transform; lightRoot = new GameObject("Lighting").transform; anchorsRoot = new GameObject("Anchors").transform;

            WorldGeometry.Reset();
            BuildGround();
            BuildScenery();
            BuildAnchors();
            BuildForest();
            BuildCamp();
            BuildLighting();
            AddCamera();
            BuildGame();

            BakeNavMesh();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[WorldAuthor] built {ScenePath}: road {WorldLayout.RoadLength:F1} m, {forestRoot.childCount} forest objects, {campRoot.childCount} camp roots");
        }

        // ------------------------------------------------------------------ helpers
        private static Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }

        private static Material Mat(string name, Color color, float smoothness = 0f)
        {
            var path = $"{MaterialsDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SaveMesh(Mesh mesh, string name)
        {
            mesh.name = name;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{GeneratedDir}/{name}.asset");
        }

        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, int layer, bool collider)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        /// <summary>A mesh whose triangles all face up, whatever order the caller listed them in.</summary>
        private static void AddQuad(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = v.Count;
            v.AddRange(new[] { a, b, c, d });
            foreach (var p in new[] { a, b, c, d }) uv.Add(new Vector2(p.x, p.z) * 0.12f);
            AddTriangle(t, v, start, start + 1, start + 2); AddTriangle(t, v, start + 1, start + 3, start + 2);
        }

        private static void AddTriangle(List<int> t, List<Vector3> v, int i0, int i1, int i2)
        {
            if (Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]).y < 0f) { t.Add(i0); t.Add(i2); t.Add(i1); } else { t.Add(i0); t.Add(i1); t.Add(i2); }
        }

        private static Mesh Build(List<Vector3> v, List<Vector2> uv, List<int> t)
        {
            var mesh = new Mesh { indexFormat = v.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        // ------------------------------------------------------------------ ground (walkable: layer Ground, collider, NavMesh source)
        private static void BuildGround()
        {
            var trampled = Mat("Ground_Trampled", new Color(0.24f, 0.28f, 0.15f));
            var dirt = Mat("Ground_Dirt", new Color(0.34f, 0.25f, 0.16f));
            var campMud = Mat("Ground_CampMud", new Color(0.25f, 0.20f, 0.14f));

            foreach (var leg in WorldLayout.Legs)
            {
                var isArena = WorldLayout.Arenas.Any(a => WorldLayout.Legs[a.LegIndex] == leg);
                var isCamp = leg.Name is "gatehouse" or "e4";
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                RibbonInto(v, uv, t, leg, leg.HalfWidth, 0f, overrun: isArena ? ArenaGroundOverrun : 0f);
                var material = isCamp ? campMud : isArena ? trampled : dirt;
                MeshObject($"Ground_{leg.Name}", groundRoot, Mesh(Build(v, uv, t), $"Ground_{leg.Name}"), material, WorldLayers.Ground, collider: true);

                if (isArena) // the road runs on through a clearing as a darker worn band (visual only)
                {
                    var bv = new List<Vector3>(); var buv = new List<Vector2>(); var bt = new List<int>();
                    RibbonInto(bv, buv, bt, leg, 2.2f, 0.03f);
                    MeshObject($"RoadBand_{leg.Name}", groundRoot, Mesh(Build(bv, buv, bt), $"RoadBand_{leg.Name}"), dirt, 0, collider: false);
                }
            }

            // the start clearing: a disc where the party begins (its road is the first leg)
            var dv = new List<Vector3>(); var duv = new List<Vector2>(); var dt = new List<int>();
            const int segments = 40; var r = WorldLayout.StartClearingRadius;
            for (var i = 0; i < segments; i++)
            {
                var a0 = i * Mathf.PI * 2f / segments; var a1 = (i + 1) * Mathf.PI * 2f / segments;
                var p0 = new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r); var p1 = new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                var start = dv.Count; dv.AddRange(new[] { Vector3.zero, p0, p1 }); foreach (var p in new[] { Vector3.zero, p0, p1 }) duv.Add(new Vector2(p.x, p.z) * 0.12f);
                AddTriangle(dt, dv, start, start + 1, start + 2);
            }
            MeshObject("Ground_start", groundRoot, Mesh(Build(dv, duv, dt), "Ground_start"), trampled, WorldLayers.Ground, collider: true);
        }

        private static Mesh Mesh(Mesh mesh, string name) { SaveMesh(mesh, name); return mesh; }

        /// <summary>Arena ground runs this far past BOTH ends of its rectangle: the NavMesh is eroded by the agent radius, so ground that stops exactly at the rectangle edge leaves the
        /// outermost 0.5 m unwalkable (the Task 3 review: "arena ground = the rectangle + 1 m"). Straight legs only.</summary>
        private const float ArenaGroundOverrun = 1.5f;

        private static void RibbonInto(List<Vector3> v, List<Vector2> uv, List<int> t, RoadLeg leg, float halfWidth, float y, float overrun = 0f)
        {
            var steps = Mathf.Max(1, Mathf.CeilToInt((leg.Length + 2f * overrun) / (Mathf.Abs(leg.Turn) < 1e-6f ? 4f : 2f)));
            Vector3 prevLeft = default, prevRight = default;
            for (var i = 0; i <= steps; i++)
            {
                var d = -overrun + (leg.Length + 2f * overrun) * i / steps;
                var centre = leg.PointAt(d) + Vector3.up * y;
                var forward = leg.Direction(leg.HeadingAt(d));
                var right = new Vector3(forward.z, 0, -forward.x);
                var left = centre - right * halfWidth; var rightPoint = centre + right * halfWidth;
                if (i > 0) AddQuad(v, uv, t, prevLeft, prevRight, left, rightPoint);
                prevLeft = left; prevRight = rightPoint;
            }
        }

        // ------------------------------------------------------------------ outer scenery: rolling, non-walkable, but clickable (layer Scenery)
        private static void BuildScenery()
        {
            WorldGeometry.Bounds(out var min, out var max);
            const float step = 4f, margin = 110f;
            var x0 = Mathf.Floor((min.x - margin) / step) * step; var z0 = Mathf.Floor((min.z - margin) / step) * step;
            var nx = Mathf.CeilToInt((max.x + margin - x0) / step); var nz = Mathf.CeilToInt((max.z + margin - z0) / step);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (var iz = 0; iz <= nz; iz++)
                for (var ix = 0; ix <= nx; ix++)
                {
                    var p = new Vector3(x0 + ix * step, 0, z0 + iz * step);
                    p.y = WorldGeometry.SceneryHeight(p);
                    v.Add(p); uv.Add(new Vector2(p.x, p.z) * 0.06f);
                }

            for (var iz = 0; iz < nz; iz++)
                for (var ix = 0; ix < nx; ix++)
                {
                    var i0 = iz * (nx + 1) + ix; var i1 = i0 + 1; var i2 = i0 + nx + 1; var i3 = i2 + 1;
                    AddTriangle(t, v, i0, i2, i1); AddTriangle(t, v, i1, i2, i3);
                }

            var floor = Mat("Scenery_ForestFloor", new Color(0.14f, 0.19f, 0.10f));
            MeshObject("Scenery_Hills", sceneryRoot, Mesh(Build(v, uv, t), "Scenery_Hills"), floor, WorldLayers.Scenery, collider: true);

            // far ground so there is never a void to the horizon (fog hides the join)
            var far = GameObject.CreatePrimitive(PrimitiveType.Plane);
            far.name = "FarGround"; far.transform.SetParent(sceneryRoot, false);
            far.transform.position = new Vector3((min.x + max.x) / 2f, -0.8f, (min.z + max.z) / 2f); far.transform.localScale = new Vector3(300f, 1f, 300f);
            Object.DestroyImmediate(far.GetComponent<MeshCollider>());
            far.GetComponent<MeshRenderer>().sharedMaterial = Mat("Scenery_Far", new Color(0.08f, 0.11f, 0.07f));
            far.layer = 0;
        }

        // ------------------------------------------------------------------ anchors + trigger volumes
        private static void BuildAnchors()
        {
            var anchors = anchorsRoot.gameObject.AddComponent<WorldAnchors>();
            Transform Point(Transform parent, string name, Vector3 position) { var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = position; return t; }
            Transform[] Points(Transform parent, string prefix, Vector3[] positions) => positions.Select((p, i) => Point(parent, $"{prefix}_{i}", p)).ToArray();

            anchors.startSlots = Points(Child(anchorsRoot, "Start"), "Slot", WorldLayout.StartSlots);
            var list = new List<WorldAnchors.EncounterAnchors>();
            foreach (var arena in WorldLayout.Arenas)
            {
                var root = Child(anchorsRoot, arena.Id.ToUpperInvariant());
                var triggerSize = arena.TriggerSize(WorldLayout.WalkableHalfWidthAtTrigger(arena));
                var triggerObject = new GameObject("Trigger") { layer = WorldLayers.Trigger };
                triggerObject.transform.SetParent(root, false);
                var tf = arena.TriggerForward;
                triggerObject.transform.SetPositionAndRotation(arena.TriggerCenter + Vector3.up * triggerSize.y / 2f, Quaternion.Euler(0f, Mathf.Atan2(-tf.z, tf.x) * Mathf.Rad2Deg, 0f));
                var box = triggerObject.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = triggerSize;

                var target = Point(root, "CameraTarget", arena.CameraTarget);
                list.Add(new WorldAnchors.EncounterAnchors
                {
                    id = arena.Id, displayName = arena.Name, goblinUnitIds = arena.GoblinUnitIds,
                    partyStage = Points(Child(root, "PartyStage"), "Hero", arena.PartyStage),
                    goblinSpawns = arena.GoblinSpawns.Select((p, i) => Point(Child(root, "GoblinSpawns"), $"{arena.GoblinUnitIds[i]}", p)).ToArray(),
                    checkpointSlots = Points(Child(root, "Checkpoint"), "Slot", arena.CheckpointSlots),
                    trigger = box, cameraTarget = target, roadYawDegrees = arena.YawDegrees,
                });
            }

            anchors.encounters = list.ToArray();
            var last = WorldLayout.Arenas[3];
            anchors.victoryCamera = Point(anchorsRoot, "VictoryCamera", last.ToWorld(WorldLayout.ArenaLength, 0f) + Vector3.up * 3f);
        }

        // ------------------------------------------------------------------ camera (a temporary framing aid; Task 4's bootstrap owns the real rig)
        private static void AddCamera()
        {
            var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            mainCamera = camera;
            camera.fieldOfView = 50f; camera.nearClipPlane = 0.3f; camera.farClipPlane = 600f;
            camera.transform.SetPositionAndRotation(new Vector3(-22f, 12f, 0f), Quaternion.Euler(28f, 90f, 0f));
            camera.gameObject.AddComponent<AudioListener>();
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;   // the world has a global post volume: the Task 4 camera rig must enable this too
        }

        // ------------------------------------------------------------------ NavMesh
        private static void BakeNavMesh()
        {
            WorldGeometry.Bounds(out var min, out var max);
            var surface = worldRoot.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3((min.x + max.x) / 2f, 0f, (min.z + max.z) / 2f) - worldRoot.position;
            surface.size = new Vector3(max.x - min.x + 80f, 40f, max.z - min.z + 80f);
            surface.layerMask = WorldLayers.NavMesh;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.defaultArea = 0;
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            AssetDatabase.CreateAsset(surface.navMeshData, $"{GeneratedDir}/NavMesh.asset");
            EditorUtility.SetDirty(surface);
        }

        private static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    /// <summary>Shared geometry queries for the authoring script: distance to the walkable corridor and the rolling height of the scenery outside it.</summary>
    public static class WorldGeometry
    {
        private static List<(Vector3 p, float hw)> samples;

        public static void Reset()
        {
            samples = new List<(Vector3, float)> { (Vector3.zero, WorldLayout.StartClearingRadius) };
            for (var s = 0f; s <= WorldLayout.RoadLength; s += 1f) samples.Add((WorldLayout.PointAt(s), WorldLayout.HalfWidthAt(s)));
        }

        public static void Bounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(samples.Min(s => s.p.x), 0, samples.Min(s => s.p.z)); max = new Vector3(samples.Max(s => s.p.x), 0, samples.Max(s => s.p.z));
        }

        /// <summary>Metres from the walkable ground's edge (0 inside it). Ribbon-accurate; arenas are rectangles, so near their corners this is slightly generous.</summary>
        public static float EdgeDistance(Vector3 p) => InCamp(p, 0f) ? 0f : WorldLayout.EdgeDistance(p);

        public static bool InCamp(Vector3 p, float extra) => WorldLayout.InCamp(p, extra);

        public static float SceneryHeight(Vector3 p)
        {
            var edge = EdgeDistance(p);
            if (edge <= 0f) return -0.03f;
            var h = Mathf.Clamp01((edge - 5f) / 45f); h = h * h * (3f - 2f * h);
            var big = Mathf.PerlinNoise(p.x * 0.018f + 40f, p.z * 0.018f + 11f);
            var small = Mathf.PerlinNoise(p.x * 0.09f, p.z * 0.09f);
            return -0.03f + h * (2.2f + 7f * big) + small * 0.7f * h;
        }
    }
}
