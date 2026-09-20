using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Dialogue;
using CRPG.Presentation.Equipment;
using CRPG.Presentation.Sprites;
using UnityEditor;
using UnityEngine;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// Task 5.3: a project asset-validation entrypoint -- deliberately NOT a claim of unifying every validator
    /// this package has. Runs the checks that are genuinely bulk-scannable across a whole project:
    /// <see cref="DialogueValidator"/> (existing, previously had zero production callers),
    /// <see cref="RulesetRegistrationValidator"/>, and <see cref="CompatibilityValidator"/> (this project's
    /// actual Unity/dependency versions against what this package's own package.json declares -- not per-asset,
    /// runs once). <see cref="CharacterRigValidator"/> stays reachable
    /// only through Character Setup's own Validate button (it validates one in-progress
    /// <c>CharacterSetupSpec</c>, not a saved asset -- there is nothing to bulk-scan). Save-content validation
    /// (<c>CRPG.Saving.ContentValidator</c>) already runs automatically at load time
    /// (<c>SessionLoadCoordinator</c>) and has no author-time equivalent to run here. Capability-requirements,
    /// outfit-compatibility, and scene-entry-point validation (also named in Docs/Design/Production-Foundation.md
    /// sec.8) are not built at all yet -- explicitly out of this task's scope, not silently implied to exist.
    /// </summary>
    public sealed class PackageValidatorWindow : EditorWindow
    {
        private readonly List<ValidationResult> results = new();
        private Vector2 resultsScroll;
        private bool hasRun;

        [MenuItem("CRPG Toolkit/Validate Project Assets")]
        public static void Open() => GetWindow<PackageValidatorWindow>("Package Validator");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Runs against every DialogueDefinition, every IRulesetDefinition asset and every EquipmentVisualProfile " +
                "in the project, plus " +
                "one project-wide check:\n" +
                "- Dialogue graphs: duplicate/dangling node ids, unreachable nodes, inescapable auto-cycles.\n" +
                "- Ruleset providers: does Register() complete without throwing (duplicate ids, construction failures).\n" +
                "- Equipment visual profiles (sprite side): unknown/duplicate/empty layer slots, missing sprites, a layer " +
                "both painted and hidden, transparent tints, and fields authored for the wrong kind.\n" +
                "- Sprite layer catalogs: missing layer slot, options with no id or sprite, duplicate option ids, and two catalogs on one slot.\n" +
                "- Compatibility: this project's actual Unity version and dependency package versions against " +
                "what this package's own package.json declares (see COMPATIBILITY.md).\n\n" +
                "Does NOT cover character rig/socket validation (use Character Setup's own Validate button) or " +
                "save-content validation (runs automatically at load time). Capability requirements, outfit " +
                "compatibility, and scene entry points have no validator at all yet.",
                MessageType.None);

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Supported Checks")) RunChecks();

            DrawResults();
        }

        private void RunChecks()
        {
            results.Clear();
            hasRun = true;

            var dialoguesChecked = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:DialogueDefinition"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                try
                {
                    var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(path);
                    if (dialogue == null) continue;
                    dialoguesChecked++;
                    results.AddRange(DialogueValidator.Validate(dialogue));
                }
                catch (System.Exception ex)
                {
                    // One broken asset must not abort checking the rest of the project.
                    results.Add(new ValidationResult(ValidationSeverity.Error, path, "Load",
                        $"Could not validate this dialogue: {ex.Message}", "Inspect the asset directly in the Inspector."));
                }
            }

            var providersChecked = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                try
                {
                    if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) is not IRulesetDefinition provider) continue;
                    providersChecked++;
                    results.AddRange(RulesetRegistrationValidator.Validate(provider, path));
                }
                catch (System.Exception ex)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, path, "Register",
                        $"Could not validate this ruleset provider: {ex.Message}", "Inspect the asset directly in the Inspector."));
                }
            }

            var spriteProfilesChecked = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:EquipmentVisualProfile"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                try
                {
                    var profile = AssetDatabase.LoadAssetAtPath<EquipmentVisualProfile>(path);
                    if (profile == null) continue;
                    spriteProfilesChecked++;
                    results.AddRange(SpriteEquipmentProfileValidator.Validate(profile, path));
                }
                catch (System.Exception ex)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, path, "Load",
                        $"Could not validate this equipment visual profile: {ex.Message}", "Inspect the asset directly in the Inspector."));
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:CRPGCameraSettings"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var cameraSettings = AssetDatabase.LoadAssetAtPath<CRPG.Camera.CRPGCameraSettings>(path);
                if (cameraSettings != null) results.AddRange(CameraSettingsValidator.Validate(cameraSettings, path));
            }

            foreach (var guid in AssetDatabase.FindAssets("t:SemanticTile"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tile = AssetDatabase.LoadAssetAtPath<CRPG.Navigation.Grid.Tilemaps.SemanticTile>(path);
                if (tile != null) results.AddRange(SemanticTileValidator.Validate(tile, path));
            }

            var catalogs = new List<(SpriteLayerCatalog catalog, string assetPath)>();
            foreach (var guid in AssetDatabase.FindAssets("t:SpriteLayerCatalog"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                try
                {
                    var catalog = AssetDatabase.LoadAssetAtPath<SpriteLayerCatalog>(path);
                    if (catalog == null) continue;
                    catalogs.Add((catalog, path));
                    results.AddRange(SpriteLayerCatalogValidator.Validate(catalog, path));
                }
                catch (System.Exception ex)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, path, "Load",
                        $"Could not validate this sprite catalog: {ex.Message}", "Inspect the asset directly in the Inspector."));
                }
            }

            results.AddRange(SpriteLayerCatalogValidator.ValidateSet(catalogs));

            results.AddRange(CompatibilityValidator.Validate());

            Debug.Log($"[PackageValidatorWindow] Checked {dialoguesChecked} dialogue asset(s), {providersChecked} ruleset provider asset(s), {spriteProfilesChecked} equipment visual profile(s) (sprite fields), and this project's Unity/dependency compatibility -- {results.Count} result(s).");
        }

        private void DrawResults()
        {
            if (!hasRun) return;

            EditorGUILayout.Space();
            if (results.Count == 0)
            {
                EditorGUILayout.LabelField("No issues found.", EditorStyles.boldLabel);
                return;
            }

            var errorCount = results.Count(r => r.Severity == ValidationSeverity.Error);
            var warningCount = results.Count - errorCount;
            EditorGUILayout.LabelField($"{errorCount} error(s), {warningCount} warning(s)", EditorStyles.boldLabel);

            resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll);
            foreach (var result in results)
            {
                var type = result.Severity == ValidationSeverity.Error ? MessageType.Error : MessageType.Warning;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox($"[{result.Field}] {result.Message}\nFix: {result.Fix}\nAsset: {result.AssetPath}", type);
                if (GUILayout.Button("Ping", GUILayout.Width(48), GUILayout.Height(38)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(result.AssetPath);
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
