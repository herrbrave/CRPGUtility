using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Editor.Validation;
using CRPG.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CRPG.Editor.Character
{
    /// <summary>
    /// Task 2.5 (Production Foundation): the "Character Setup editor" Docs/Design/Production-Foundation.md
    /// sec.4.6 describes -- accepts a model prefab, rig family, animation profile, capsule/navigation
    /// settings and socket mapping, then Validates/Previews/creates-or-updates a wrapper prefab. A thin GUI
    /// shell: all real logic lives in <see cref="CharacterSetupSpec"/> (plain data),
    /// <see cref="CharacterRigValidator"/> (pure validation) and <see cref="CharacterWrapperBuilder"/> (prefab
    /// construction) -- all three testable without this window at all.
    /// <para>"Undo" here means an explicit confirmation prompt before overwriting an existing wrapper, not
    /// Ctrl+Z: <see cref="PrefabUtility.SaveAsPrefabAsset"/> is an AssetDatabase-level write, which Unity's
    /// scene-edit Undo stack does not cover -- an honest interpretation rather than claiming native Undo
    /// support that doesn't actually exist for this kind of operation.</para>
    /// </summary>
    public sealed class CharacterSetupWindow : EditorWindow
    {
        private const string CharacterLabScenePath = "Assets/Scenes/CharacterLab.unity";

        private readonly CharacterSetupSpec spec = new();
        private string prefabPath = "Assets/Resources/NPCs/NewCharacter.prefab";
        private string idleCueIdText = "anim-idle";
        private string movingCueIdText = "anim-moving";
        private string optionalCueIdsText = "anim-attack-melee, anim-death";
        private string requiredSocketIdsText = "";

        private List<ValidationResult> lastValidation = new();
        private Vector2 resultsScroll;

        [MenuItem("CRPG Toolkit/Characters/Character Setup")]
        public static void Open() => GetWindow<CharacterSetupWindow>("Character Setup");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Model & Rig", EditorStyles.boldLabel);
            spec.ModelPrefab = (GameObject)EditorGUILayout.ObjectField("Model Prefab", spec.ModelPrefab, typeof(GameObject), false);
            spec.RigDefinition = (CharacterRigDefinition)EditorGUILayout.ObjectField("Rig Family (optional)", spec.RigDefinition, typeof(CharacterRigDefinition), false);
            spec.AnimationProfile = (CRPG.Actors.CharacterAnimationProfile)EditorGUILayout.ObjectField(
                "Animation Profile", spec.AnimationProfile, typeof(CRPG.Actors.CharacterAnimationProfile), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animation Cue Ids", EditorStyles.boldLabel);
            idleCueIdText = EditorGUILayout.TextField("Idle Cue Id (mandatory)", idleCueIdText);
            movingCueIdText = EditorGUILayout.TextField("Moving Cue Id (mandatory)", movingCueIdText);
            optionalCueIdsText = EditorGUILayout.TextField("Optional Cue Ids (comma-separated)", optionalCueIdsText);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Socket Mapping", EditorStyles.boldLabel);
            requiredSocketIdsText = EditorGUILayout.TextField("Required Socket Ids (comma-separated)", requiredSocketIdsText);
            EditorGUILayout.HelpBox("A socket is just a Transform named exactly the socket id, anywhere in the model's hierarchy (Task 2.4's Rigid attachment convention).", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Capsule", EditorStyles.boldLabel);
            spec.CapsuleHeight = EditorGUILayout.FloatField("Height", spec.CapsuleHeight);
            spec.CapsuleRadius = EditorGUILayout.FloatField("Radius", spec.CapsuleRadius);
            spec.CapsuleCenterY = EditorGUILayout.FloatField("Center Y", spec.CapsuleCenterY);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("NavMesh Agent", EditorStyles.boldLabel);
            spec.AgentRadius = EditorGUILayout.FloatField("Radius", spec.AgentRadius);
            spec.AgentHeight = EditorGUILayout.FloatField("Height", spec.AgentHeight);
            spec.AgentSpeed = EditorGUILayout.FloatField("Speed", spec.AgentSpeed);
            spec.AgentAcceleration = EditorGUILayout.FloatField("Acceleration", spec.AgentAcceleration);
            spec.AgentAngularSpeed = EditorGUILayout.FloatField("Angular Speed", spec.AgentAngularSpeed);
            spec.AgentStoppingDistance = EditorGUILayout.FloatField("Stopping Distance", spec.AgentStoppingDistance);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Wrapper Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            prefabPath = EditorGUILayout.TextField("Prefab Path", prefabPath);
            if (GUILayout.Button("Browse...", GUILayout.Width(80))) BrowseForPath();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            var existing = string.IsNullOrEmpty(prefabPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate")) RunValidate();
            if (GUILayout.Button("Preview")) RunPreview();
            if (GUILayout.Button(existing != null ? "Update Wrapper" : "Create Wrapper")) RunBuild(existing != null);
            EditorGUILayout.EndHorizontal();

            DrawValidationResults();
        }

        private void SyncSpecFromText()
        {
            spec.IdleCueId = new DefinitionId(idleCueIdText?.Trim());
            spec.MovingCueId = new DefinitionId(movingCueIdText?.Trim());
            spec.OptionalCueIds = ParseIds(optionalCueIdsText);
            spec.RequiredSocketIds = ParseIds(requiredSocketIdsText);
        }

        private static List<DefinitionId> ParseIds(string commaSeparated)
        {
            if (string.IsNullOrWhiteSpace(commaSeparated)) return new List<DefinitionId>();
            return commaSeparated.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Select(s => new DefinitionId(s))
                .ToList();
        }

        private void RunValidate()
        {
            SyncSpecFromText();
            lastValidation = CharacterRigValidator.Validate(spec);
        }

        private void RunBuild(bool isUpdate)
        {
            SyncSpecFromText();
            lastValidation = CharacterRigValidator.Validate(spec);

            if (lastValidation.Any(r => r.Severity == ValidationSeverity.Error))
            {
                if (!EditorUtility.DisplayDialog("Validation Errors Present",
                        "This configuration has validation errors (see the list below). Build the wrapper anyway?",
                        "Build Anyway", "Cancel"))
                {
                    return;
                }
            }

            if (isUpdate && !EditorUtility.DisplayDialog("Update Existing Wrapper?",
                    $"'{prefabPath}' already exists and will be updated IN PLACE (its GUID -- and therefore every existing reference to it -- is preserved). " +
                    "This is an asset-level write; it cannot be undone with Ctrl+Z. Continue?",
                    "Update", "Cancel"))
            {
                return;
            }

            GameObject prefab;
            try
            {
                prefab = CharacterWrapperBuilder.BuildOrUpdate(spec, prefabPath);
            }
            catch (InvalidOperationException ex)
            {
                // The builder refuses an update that would leave two movers/presentation pipelines/collider kinds on one wrapper.
                EditorUtility.DisplayDialog("Cannot Update This Wrapper", ex.Message, "OK");
                return;
            }

            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[CharacterSetupWindow] {(isUpdate ? "Updated" : "Created")} wrapper at '{prefabPath}'.");
        }

        private void RunPreview()
        {
            SyncSpecFromText();

            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(CharacterLabScenePath))
            {
                EditorUtility.DisplayDialog("Character Lab Scene Not Found",
                    $"'{CharacterLabScenePath}' doesn't exist yet -- run 'CRPG Toolkit/Characters/Author Character Lab Scene' first.",
                    "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(CharacterLabScenePath);

            // Found by interface, never a direct type reference -- the package must not know about
            // TestProject-only content (CharacterLabHarness lives there, not in this package).
            var harness = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ICharacterPreviewAnchor>()
                .FirstOrDefault();
            if (harness == null)
            {
                EditorUtility.DisplayDialog("No Preview Anchor Found",
                    $"'{CharacterLabScenePath}' has nothing implementing ICharacterPreviewAnchor in it -- re-run 'Author Character Lab Scene' to rebuild it.", "OK");
                return;
            }

            // Kind-specific path: previewing one movement/presentation kind must never be refused because an earlier preview of
            // another kind occupies the same asset.
            var previewPath = $"Assets/CharacterLab/_PreviewWrapper_{spec.Movement}_{spec.Presentation}.prefab";
            GameObject previewPrefab;
            try
            {
                previewPrefab = CharacterWrapperBuilder.BuildOrUpdate(spec, previewPath);
            }
            catch (InvalidOperationException ex)
            {
                EditorUtility.DisplayDialog("Cannot Preview This Configuration", ex.Message, "OK");
                return;
            }
            harness.SpawnCharacter(previewPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[CharacterSetupWindow] Staged the current configuration in CharacterLab -- press Play to inspect idle/walk/turn/action/interruption/death/recovery/equip-unequip (see CharacterLabHarness's on-screen controls).");
        }

        private void BrowseForPath()
        {
            var directory = string.IsNullOrEmpty(prefabPath) ? "Assets" : System.IO.Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            var fileName = string.IsNullOrEmpty(prefabPath) ? "NewCharacter" : System.IO.Path.GetFileNameWithoutExtension(prefabPath);
            var chosen = EditorUtility.SaveFilePanelInProject("Wrapper Prefab Path", fileName, "prefab", "Choose where to save the wrapper prefab.", directory);
            if (!string.IsNullOrEmpty(chosen)) prefabPath = chosen;
        }

        private void DrawValidationResults()
        {
            if (lastValidation.Count == 0) return;

            EditorGUILayout.Space();
            var errorCount = lastValidation.Count(r => r.Severity == ValidationSeverity.Error);
            var warningCount = lastValidation.Count - errorCount;
            EditorGUILayout.LabelField($"Validation results: {errorCount} error(s), {warningCount} warning(s)", EditorStyles.boldLabel);

            resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll, GUILayout.Height(160));
            foreach (var result in lastValidation)
            {
                var type = result.Severity == ValidationSeverity.Error ? MessageType.Error : MessageType.Warning;
                EditorGUILayout.HelpBox($"[{result.Field}] {result.Message}\nFix: {result.Fix}\nAsset: {result.AssetPath}", type);
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
