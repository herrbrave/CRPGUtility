using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VikingRaid.EditorTools
{
    /// <summary>One-shot project bootstrap (Task 0): Linear colour space plus a fresh URP asset + 3D renderer,
    /// assigned as the default pipeline for every quality level. Re-runnable: reuses assets that already exist.
    /// A copied project skeleton (ConsumerGame) was rejected in the plan review because it carried a dangling URP
    /// asset GUID, Gamma colour space and missing engine modules -- this script creates the real thing instead.</summary>
    public static class VikingRaidProjectSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string RendererPath = SettingsFolder + "/VikingRaid_UniversalRenderer.asset";
        private const string PipelinePath = SettingsFolder + "/VikingRaid_URPAsset.asset";

        [MenuItem("Viking Raid/Setup Project (Linear + URP)")]
        public static void Run()
        {
            PlayerSettings.companyName = "CRPG Toolkit";
            PlayerSettings.productName = "Viking Raid";
            PlayerSettings.colorSpace = ColorSpace.Linear;

            if (!AssetDatabase.IsValidFolder(SettingsFolder)) AssetDatabase.CreateFolder("Assets", "Settings");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                AssetDatabase.SaveAssets();
                renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                AssetDatabase.SaveAssets();
                pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            }

            // Lighting the world needs more than the defaults the asset was created with (found in the Task 3 review): the default Forward renderer
            // allows 4 additional lights per object, so a welded ground mesh lit by a camp full of fires would flicker; there is 1 shadow cascade and no
            // soft shadows. Forward+ removes the per-object light cap.
            renderer.renderingMode = RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(renderer);
            pipeline.shadowCascadeCount = 3;
            pipeline.shadowDistance = 70f;
            pipeline.mainLightShadowmapResolution = 2048;
            var pipelineObject = new SerializedObject(pipeline);   // supportsSoftShadows has no public setter
            pipelineObject.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            EnsureLayers();

            GraphicsSettings.defaultRenderPipeline = pipeline;
            var originalLevel = QualitySettings.GetQualityLevel();
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(originalLevel, false);

            AssetDatabase.SaveAssets();
            Debug.Log($"[VikingRaidProjectSetup] colorSpace={PlayerSettings.colorSpace} pipeline={AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline)} renderer={AssetDatabase.GetAssetPath(renderer)} " +
                      $"mode={renderer.renderingMode} cascades={pipeline.shadowCascadeCount} shadowDistance={pipeline.shadowDistance} soft={pipeline.supportsSoftShadows}");
        }

        /// <summary>Names layers 8-12 (see <see cref="VikingRaid.Game.WorldLayers"/>). A layer already holding a different name is an error, not an overwrite.</summary>
        public static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (var i = 0; i < VikingRaid.Game.WorldLayers.Names.Length; i++)
            {
                var slot = layers.GetArrayElementAtIndex(VikingRaid.Game.WorldLayers.Ground + i);
                var wanted = VikingRaid.Game.WorldLayers.Names[i];
                if (slot.stringValue == wanted) continue;
                if (!string.IsNullOrEmpty(slot.stringValue)) throw new System.InvalidOperationException($"layer {VikingRaid.Game.WorldLayers.Ground + i} is already named '{slot.stringValue}', wanted '{wanted}'");
                slot.stringValue = wanted;
            }

            tagManager.ApplyModifiedProperties();
        }
    }
}
