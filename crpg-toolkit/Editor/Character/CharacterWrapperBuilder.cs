using System;
using System.IO;
using System.Reflection;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CRPG.Editor.Character
{
    /// <summary>
    /// Task 2.5 (Production Foundation): builds/updates a CharacterRoot/VisualRoot/Model wrapper prefab from a
    /// <see cref="CharacterSetupSpec"/>.
    /// <para>Phase 2 correction pass (R1): an UPDATE to an existing wrapper now genuinely edits it in place --
    /// <see cref="PrefabUtility.LoadPrefabContents"/> loads the real existing hierarchy, only the tool-owned
    /// components (capsule/rigidbody/agent/motor/facing/agent-binding/equipment/animator) and the Model child
    /// specifically are added-or-patched, and the result is saved back over itself. Everything else the
    /// consumer added -- extra components on CharacterRoot, extra children under VisualRoot, anything at all
    /// -- survives untouched, including every subobject reference into it. The OLD "always build a completely
    /// fresh, unrelated hierarchy and save over the existing asset" approach only preserved the asset's own
    /// GUID; it silently discarded everything else, which is unacceptable for a general-purpose, consumer-
    /// facing tool (as opposed to BaseHumanMaleCharacterAuthor.cs's narrower one-character internal script,
    /// which still rebuilds fully and remains as-is). Building a brand NEW wrapper (no existing asset at the
    /// target path) is unaffected -- there is nothing to preserve yet.</para>
    /// <para>"Undo" is an explicit confirmation prompt in CharacterSetupWindow before overwriting an existing
    /// wrapper, not Ctrl+Z -- an honest interpretation, not a claim of native Undo-stack integration that
    /// doesn't actually exist for an AssetDatabase-level prefab write.</para>
    /// </summary>
    public static class CharacterWrapperBuilder
    {
        public static GameObject BuildOrUpdate(CharacterSetupSpec spec, string prefabPath)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (string.IsNullOrEmpty(prefabPath)) throw new ArgumentException("prefabPath must not be empty.", nameof(prefabPath));

            var existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return existingAsset != null ? UpdateExisting(spec, prefabPath) : CreateNew(spec, prefabPath);
        }

        private static GameObject CreateNew(CharacterSetupSpec spec, string prefabPath)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            try
            {
                UpdateOrAddCharacterRootComponents(root, spec);
                UpdateOrCreateVisualRootAndModel(root, spec);

                EnsureFolder(Path.GetDirectoryName(prefabPath)?.Replace('\\', '/'));
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject UpdateExisting(CharacterSetupSpec spec, string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                UpdateOrAddCharacterRootComponents(root, spec);
                UpdateOrCreateVisualRootAndModel(root, spec);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Adds-or-patches ONLY the tool-owned gameplay components. Never removes anything -- any component a
        // consumer added beyond this set (a selection marker, an interaction anchor, ...) is left completely
        // untouched, on both the fresh-build and update-existing paths.
        private static void UpdateOrAddCharacterRootComponents(GameObject root, CharacterSetupSpec spec)
        {
            // NOT `GetComponent<T>() ?? AddComponent<T>()`: Unity's C#/native marshalling can return a
            // reference for "no component found" that fails C#'s true-null check the `??` operator relies on,
            // so `??` incorrectly keeps that unusable reference instead of falling through to AddComponent --
            // confirmed empirically (a live MissingComponentException) while writing this correction pass, not
            // assumed. An explicit `== null` check uses Unity's own overloaded equality operator instead, which
            // handles this correctly; that's the idiomatic get-or-add pattern in Unity C#.
            var capsule = GetOrAddComponent<CapsuleCollider>(root);
            capsule.height = spec.CapsuleHeight;
            capsule.radius = spec.CapsuleRadius;
            capsule.center = new Vector3(0f, spec.CapsuleCenterY, 0f);

            // A NavMeshAgent moves the transform directly and adds no Rigidbody of its own, so without a
            // kinematic/no-gravity one this character would never raise OnTriggerEnter/Exit for any
            // dialogue/teleport trigger zone -- same trio BaseHumanMaleCharacterAuthor/OakhavenContentAuthor
            // already establish for every character.
            var rigidbody = GetOrAddComponent<Rigidbody>(root);
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            var agent = GetOrAddComponent<NavMeshAgent>(root);
            agent.radius = spec.AgentRadius;
            agent.height = spec.AgentHeight;
            agent.speed = spec.AgentSpeed;
            agent.acceleration = spec.AgentAcceleration;
            agent.angularSpeed = spec.AgentAngularSpeed;
            agent.stoppingDistance = spec.AgentStoppingDistance;

            GetOrAddComponent<NavMeshCharacterMotor>(root);
            GetOrAddComponent<FacingController>(root);
            GetOrAddComponent<CharacterAgent>(root);
            GetOrAddComponent<EquipmentVisualBinder>(root);

            var animator = GetOrAddComponent<CharacterPlayableAnimator>(root);
            SetField(animator, "profile", spec.AnimationProfile);
            SetField(animator, "idleCueId", spec.IdleCueId);
            SetField(animator, "movingCueId", spec.MovingCueId);
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        // Finds-or-creates VisualRoot (preserving any OTHER children a consumer added under it), and replaces
        // ONLY the "Model" child specifically -- Model is deliberately, always fully swappable (matching
        // CharacterView.SetModel's own established "destroy old, instantiate new" contract), never merged.
        private static void UpdateOrCreateVisualRootAndModel(GameObject root, CharacterSetupSpec spec)
        {
            var visualRootTransform = root.transform.Find("VisualRoot");
            GameObject visualRoot;
            if (visualRootTransform == null)
            {
                visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(root.transform, false);
            }
            else
            {
                visualRoot = visualRootTransform.gameObject;
            }

            var existingModelTransform = visualRoot.transform.Find("Model");
            if (existingModelTransform != null) UnityEngine.Object.DestroyImmediate(existingModelTransform.gameObject);

            if (spec.ModelPrefab != null)
            {
                // CharacterView.Awake() runs the instant it's added to (or already present on) this
                // already-active GameObject and auto-binds whatever child it finds -- Model must already be
                // parented first. Its transient Awake-time RefreshBoneMap call may use rigDefinition's OLD
                // (pre-update) value if CharacterView already existed; harmless, since SetField below
                // overwrites it with the correct value before this GameObject is ever saved.
                var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(spec.ModelPrefab);
                modelInstance.name = "Model";
                modelInstance.transform.SetParent(visualRoot.transform, false);
            }

            var view = visualRoot.GetComponent<CharacterView>();
            if (view == null) view = visualRoot.AddComponent<CharacterView>();
            SetField(view, "rigDefinition", spec.RigDefinition);
        }

        private static void SetField(object target, string fieldName, object value) => Field(target, fieldName).SetValue(target, value);

        private static FieldInfo Field(object target, string fieldName)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                type = type.BaseType;
            }

            throw new Exception($"Field '{fieldName}' not found on {target.GetType().Name} or its base types.");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath)) return;

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
