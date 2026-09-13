using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CRPG.Editor
{
    // Package-owned (not TestProject dogfood) -- this is the actual shipped default Input Actions asset
    // Task 3.1's design calls for, so it lives under Editor/ as a reproducible one-shot authoring script
    // rather than a hand-maintained asset, matching every other *Author.cs convention in this project.
    //
    // Point/Click/Cancel/ShiftModifier/Pan/Zoom/Rotate/Recenter are all consumed by InputSystemCommandSource
    // as of Task 3.2. AbilityTarget remains RESERVED -- present so the asset is genuinely the "default Input
    // Actions asset... for... ability targeting" the design doc calls for, but nothing reads it yet (no
    // ability-targeting system exists). Rework its binding freely once something does.
    public static class CRPGInputActionsAuthor
    {
        private const string AssetPath = "Packages/com.crpgtoolkit.core/Integrations/InputSystem/CRPGInputActions.inputactions";
        private const string WrapperClassName = "CRPGInputActions";
        private const string WrapperNamespace = "CRPG.InputSystem";

        [MenuItem("CRPG Toolkit/Input/Author Default Input Actions")]
        public static void Author()
        {
            AssetDatabase.DeleteAsset(AssetPath); // no-op if nothing's there yet; also cleans up a corrupt prior attempt

            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var exploration = asset.AddActionMap("Exploration");

            var point = exploration.AddAction("Point", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            point.AddBinding("<Mouse>/position");

            var click = exploration.AddAction("Click", InputActionType.Button);
            click.AddBinding("<Mouse>/leftButton");

            var cancel = exploration.AddAction("Cancel", InputActionType.Button);
            cancel.AddBinding("<Keyboard>/escape");

            var shiftModifier = exploration.AddAction("ShiftModifier", InputActionType.Button);
            shiftModifier.AddBinding("<Keyboard>/leftShift");
            shiftModifier.AddBinding("<Keyboard>/rightShift");

            // Reserved -- see class remarks.
            var abilityTarget = exploration.AddAction("AbilityTarget", InputActionType.Button);
            abilityTarget.AddBinding("<Mouse>/rightButton");

            var pan = exploration.AddAction("Pan", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            pan.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            var zoom = exploration.AddAction("Zoom", InputActionType.PassThrough, expectedControlLayout: "Axis");
            zoom.AddBinding("<Mouse>/scroll/y");

            // Task 3.2: redefined from the Task 3.1 placeholder (<Mouse>/middleButton, a plain Button that
            // cannot supply a signed value at all) to a real signed 1D axis -- matches
            // CameraRigState.Rotate(float direction, float deltaTime)'s existing continuous-while-held
            // signature, the same composite-binding pattern already used for Pan's WASD 2D-vector.
            var rotate = exploration.AddAction("Rotate", InputActionType.Value, expectedControlLayout: "Axis");
            rotate.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q")
                .With("Positive", "<Keyboard>/e");

            var recenter = exploration.AddAction("Recenter", InputActionType.Button);
            recenter.AddBinding("<Keyboard>/space");

            // .inputactions is a custom JSON-text asset format (ScriptedImporter), not a native serialized
            // ScriptableObject -- AssetDatabase.CreateAsset writes the WRONG on-disk format here (confirmed
            // live: InputActionImporter then fails with "JSON parse error: Invalid value" trying to read it).
            // Write the actual JSON text to the package's real resolved disk path instead, then import it.
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.crpgtoolkit.core/package.json");
            if (packageInfo == null)
            {
                Debug.LogError("[CRPGInputActionsAuthor] Could not resolve com.crpgtoolkit.core's package root -- aborting.");
                return;
            }

            const string packagePrefix = "Packages/com.crpgtoolkit.core/";
            var relativeWithinPackage = AssetPath.Substring(packagePrefix.Length);
            var fullPath = System.IO.Path.Combine(packageInfo.resolvedPath, relativeWithinPackage);
            System.IO.File.WriteAllText(fullPath, asset.ToJson());
            AssetDatabase.ImportAsset(AssetPath);

            var importer = AssetImporter.GetAtPath(AssetPath);
            var serialized = new SerializedObject(importer);
            serialized.FindProperty("m_GenerateWrapperCode").boolValue = true;
            serialized.FindProperty("m_WrapperClassName").stringValue = WrapperClassName;
            serialized.FindProperty("m_WrapperCodeNamespace").stringValue = WrapperNamespace;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();

            Debug.Log($"[CRPGInputActionsAuthor] Authored {AssetPath} with a generated {WrapperNamespace}.{WrapperClassName} wrapper class.");
        }
    }
}
