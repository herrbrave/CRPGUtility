using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Project-local copies of Synty prefabs with the colliders the GAME wants instead of the ones the pack shipped (the Task 3 review: Synty pines carry a 15 m whole-tree
    /// capsule, group prefabs a non-convex MeshCollider, every building a MeshCollider that would seal passages and give roofs a walkable top). Each copy is a plain prefab
    /// asset under Assets/World/Prefabs that still references the pack's meshes and materials by GUID; the scene then instantiates these, so there are no per-instance overrides.
    /// </summary>
    public static partial class WorldAuthor
    {
        public const string VariantsDir = "Assets/World/Prefabs";
        private static readonly Dictionary<string, GameObject> variants = new();

        public enum Solid
        {
            /// <summary>Decoration: no collider, layer Default.</summary>
            None,
            /// <summary>A thin 0.8 m x 4 m capsule at the trunk (trees): blocks sight and movement near the road, nothing else.</summary>
            Trunk,
            /// <summary>One box around the renderers (rocks, tents, walls, towers, big props).</summary>
            Box,
            /// <summary>A box shrunk to the middle half (barrels, torches, braziers: you can stand next to them).</summary>
            SmallBox,
        }

        private static void ResetVariants()
        {
            variants.Clear();
            if (AssetDatabase.IsValidFolder(VariantsDir)) AssetDatabase.DeleteAsset(VariantsDir);
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), VariantsDir));
            AssetDatabase.Refresh();
        }

        public static GameObject Variant(string sourcePath, Solid solid)
        {
            var key = $"{sourcePath}|{solid}";
            if (variants.TryGetValue(key, out var cached)) return cached;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new FileNotFoundException($"missing Synty prefab {sourcePath}");
            var clone = Object.Instantiate(source);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.transform.localScale = Vector3.one;
            try
            {
                foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
                WorldLayers.SetRecursive(clone, solid == Solid.None ? 0 : WorldLayers.Obstacle);

                var bounds = RendererBounds(clone);
                switch (solid)
                {
                    case Solid.Trunk:
                        var capsule = clone.AddComponent<CapsuleCollider>(); capsule.direction = 1; capsule.radius = 0.4f; capsule.height = 4f; capsule.center = new Vector3(0f, 2f, 0f);
                        break;
                    case Solid.Box:
                        var box = clone.AddComponent<BoxCollider>(); box.center = bounds.center; box.size = bounds.size;
                        break;
                    case Solid.SmallBox:
                        var small = clone.AddComponent<BoxCollider>(); small.center = bounds.center; small.size = new Vector3(bounds.size.x * 0.6f, bounds.size.y, bounds.size.z * 0.6f);
                        break;
                }

                // a flat top (a roof, a wall, a rock) is otherwise a legal walking surface: the NavMesh would get islands up there
                if (solid is Solid.Box or Solid.SmallBox) BlockNavigation(clone);

                var path = $"{VariantsDir}/{Path.GetFileNameWithoutExtension(sourcePath)}_{solid}_{Mathf.Abs(sourcePath.GetHashCode()) % 9973}.prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(clone, path);
                variants[key] = saved;
                return saved;
            }
            finally { Object.DestroyImmediate(clone); }
        }

        /// <summary>Combined renderer bounds in the object's own (root-local) space; the object must sit at identity.</summary>
        public static Bounds RendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r is not ParticleSystemRenderer && r.enabled).ToList();
            if (renderers.Count == 0) return new Bounds(Vector3.up * 0.5f, Vector3.one);
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Places an instance of a variant. Flags it static so batching and occlusion can use it.</summary>
        public static GameObject Place(GameObject variant, Transform parent, Vector3 position, float yawDegrees, float scale = 1f, float tiltX = 0f, float tiltZ = 0f)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(variant, parent);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(tiltX, yawDegrees, tiltZ));
            go.transform.localScale = Vector3.one * scale;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        /// <summary>Marks an obstacle's whole hierarchy Not Walkable for the NavMesh, so roofs, wall tops and rock tops never become islands (a flat top is otherwise a legal walking surface).</summary>
        public static void BlockNavigation(GameObject go)
        {
            var modifier = go.GetComponent<Unity.AI.Navigation.NavMeshModifier>() ?? go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            modifier.overrideArea = true; modifier.area = 1; modifier.applyToChildren = true;
        }

        /// <summary>Yaw (degrees about +Y) that turns a model's local +X onto the given ground direction.</summary>
        public static float YawForLocalX(Vector3 direction) => Mathf.Atan2(-direction.z, direction.x) * Mathf.Rad2Deg;

        /// <summary>Yaw that turns a model's local +Z (its front) onto the given ground direction.</summary>
        public static float YawForLocalZ(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
    }
}
