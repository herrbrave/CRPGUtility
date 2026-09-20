using System;
using System.IO;
using System.Reflection;
using CRPG.Gameplay;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
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
            if (existingAsset != null) RefuseConflictingUpdate(existingAsset, spec, prefabPath);
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
            if (spec.UsesTopDownCollider)
            {
                // 2D campaign: a top-down character's click target and footprint is ONE grid cell, not the humanoid capsule
                // (r = 0.3 would project a 0.6-unit disc into a 1-unit cell). The builder owns this BoxCollider the way it owns
                // the capsule on humanoid wrappers: an existing one is patched, never duplicated.
                var box = GetOrAddComponent<BoxCollider>(root);
                var footprint = spec.GridCellSize * spec.GridColliderFill;
                box.size = new Vector3(footprint, spec.GridColliderHeight, footprint);
                box.center = new Vector3(0f, spec.GridColliderHeight * 0.5f, 0f);
            }
            else
            {
                var capsule = GetOrAddComponent<CapsuleCollider>(root);
                capsule.height = spec.CapsuleHeight;
                capsule.radius = spec.CapsuleRadius;
                capsule.center = new Vector3(0f, spec.CapsuleCenterY, 0f);
            }

            // A NavMeshAgent moves the transform directly and adds no Rigidbody of its own, so without a
            // kinematic/no-gravity one this character would never raise OnTriggerEnter/Exit for any
            // dialogue/teleport trigger zone -- same trio BaseHumanMaleCharacterAuthor/OakhavenContentAuthor
            // already establish for every character.
            var rigidbody = GetOrAddComponent<Rigidbody>(root);
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            if (spec.Movement == MovementBackend.Grid)
            {
                var gridMotor = GetOrAddComponent<GridCharacterMotor>(root);
                gridMotor.Speed = spec.GridSpeed;
            }
            else
            {
                var agent = GetOrAddComponent<NavMeshAgent>(root);
                agent.radius = spec.AgentRadius;
                agent.height = spec.AgentHeight;
                agent.speed = spec.AgentSpeed;
                agent.acceleration = spec.AgentAcceleration;
                agent.angularSpeed = spec.AgentAngularSpeed;
                agent.stoppingDistance = spec.AgentStoppingDistance;

                GetOrAddComponent<NavMeshCharacterMotor>(root);
            }

            GetOrAddComponent<FacingController>(root);
            GetOrAddComponent<CharacterAgent>(root);

            if (spec.Presentation == PresentationKind.Sprite)
            {
                // On CharacterRoot, not inside the swappable Model: they re-resolve the compositor when a model swap destroys it,
                // which only works if they survive the swap.
                GetOrAddComponent<SpriteEquipmentBinder>(root);
                GetOrAddComponent<SpriteAppearanceBinder>(root);
            }
            else
            {
                GetOrAddComponent<EquipmentVisualBinder>(root);

                var animator = GetOrAddComponent<CharacterPlayableAnimator>(root);
                SetField(animator, "profile", spec.AnimationProfile);
                SetField(animator, "idleCueId", spec.IdleCueId);
                SetField(animator, "movingCueId", spec.MovingCueId);
            }
        }

        /// <summary>The builder never removes anything, so an update whose spec disagrees with what the existing wrapper already
        /// carries would leave two movers (or two presentation pipelines, or two collider kinds) on one character. It is
        /// refused up front, from the existing ASSET (no preview scene is opened, nothing is edited), so the file is untouched.</summary>
        private static void RefuseConflictingUpdate(GameObject existing, CharacterSetupSpec spec, string prefabPath)
        {
            string Has<T>() where T : Component => existing.GetComponentInChildren<T>(true) != null ? typeof(T).Name : null;

            var conflicts = new System.Collections.Generic.List<string>();
            if (spec.Movement == MovementBackend.Grid) { conflicts.Add(Has<NavMeshAgent>()); conflicts.Add(Has<NavMeshCharacterMotor>()); }
            else conflicts.Add(Has<GridCharacterMotor>());

            if (spec.Presentation == PresentationKind.Sprite) { conflicts.Add(Has<CharacterPlayableAnimator>()); conflicts.Add(Has<EquipmentVisualBinder>()); }
            else { conflicts.Add(Has<SpriteEquipmentBinder>()); conflicts.Add(Has<SpriteAppearanceBinder>()); }

            if (spec.UsesTopDownCollider && existing.GetComponent<CapsuleCollider>() != null) conflicts.Add(nameof(CapsuleCollider));

            conflicts.RemoveAll(string.IsNullOrEmpty);
            if (conflicts.Count == 0) return;

            throw new InvalidOperationException(
                $"'{prefabPath}' already has {string.Join(", ", conflicts)}, which conflicts with this spec " +
                $"({spec.Movement} movement, {spec.Presentation} presentation). The builder never removes components, so updating would leave two movers, " +
                "two presentation pipelines or two collider kinds on one character -- create a new wrapper instead.");
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

            GameObject modelInstance = null;
            if (spec.Presentation == PresentationKind.Sprite)
            {
                // The generated card. Its 0.02 world-Y lift lives on VisualRoot (CharacterView.SetModel resets a swapped-in
                // Model's local transform to zero, so a lift on the card itself would be lost on the first swap).
                modelInstance = SpriteCardBuilder.Build(spec);
                visualRoot.transform.localPosition = new Vector3(0f, spec.SpriteCardHeight, 0f);
            }
            else if (spec.ModelPrefab != null)
            {
                // CharacterView.Awake() runs the instant it's added to (or already present on) this
                // already-active GameObject and auto-binds whatever child it finds -- Model must already be
                // parented first. Its transient Awake-time RefreshBoneMap call may use rigDefinition's OLD
                // (pre-update) value if CharacterView already existed; harmless, since SetField below
                // overwrites it with the correct value before this GameObject is ever saved.
                modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(spec.ModelPrefab);
                modelInstance.name = "Model";
            }

            if (modelInstance != null)
            {
                modelInstance.transform.SetParent(visualRoot.transform, false);
                // CharacterView binds GetChild(0): consumer children under VisualRoot are preserved, so the Model must go FIRST
                // (SetParent appends last).
                modelInstance.transform.SetSiblingIndex(0);
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
