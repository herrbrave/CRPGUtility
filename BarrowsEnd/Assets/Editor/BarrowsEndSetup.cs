using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarrowsEnd.Editor
{
    /// <summary>Project bootstrap: URP activation and Kenney 16px sheet slicing (2D campaign Task 2.0a).</summary>
    public static class BarrowsEndSetup
    {
        private const string SettingsDir = "Assets/Settings";
        private const string SourceDir = "Assets/Art/Source";
        private const int Cell = 16, Pitch = 17;

        [MenuItem("Barrows End/Setup/Activate URP")]
        public static void ActivateUrp()
        {
            Directory.CreateDirectory(SettingsDir);
            var rendererPath = SettingsDir + "/BarrowsEnd_Renderer.asset";
            var assetPath = SettingsDir + "/BarrowsEnd_URP.asset";
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, rendererPath);
            }
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(data);
                AssetDatabase.CreateAsset(urp, assetPath);
            }
            GraphicsSettings.defaultRenderPipeline = urp;
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[BarrowsEnd] URP activated: " + assetPath);
        }

        [MenuItem("Barrows End/Setup/Slice Kenney Sheets")]
        public static void SliceAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SourceDir }))
                SliceSheet(AssetDatabase.GUIDToAssetPath(guid));
        }

        /// <summary>Import at PPU 16 / point / uncompressed / Multiple, one sprite per non-empty 16x16 cell (1px gutter).</summary>
        public static int SliceSheet(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = Cell;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; // pixel art: exact quads, no tight-mesh outlines
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            var cols = (tex.width + 1) / Pitch;
            var rows = (tex.height + 1) / Pitch;
            var name = Path.GetFileNameWithoutExtension(path);
            var pixels = tex.GetPixels32();
            var rects = new List<SpriteRect>();
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var x = c * Pitch;
                var yTop = r * Pitch;
                var y = tex.height - yTop - Cell; // sprite rects are bottom-left origin
                if (IsEmpty(pixels, tex.width, x, y)) continue;
                rects.Add(new SpriteRect
                {
                    name = $"{name}_{c}_{r}",
                    spriteID = GUID.Generate(),
                    rect = new Rect(x, y, Cell, Cell),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                });
            }
            Object.DestroyImmediate(tex);

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            provider.SetSpriteRects(rects.ToArray());
            var nameFile = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameFile.SetNameFileIdPairs(rects.ConvertAll(s => new SpriteNameFileIdPair(s.name, s.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
            Debug.Log($"[BarrowsEnd] Sliced {path}: {rects.Count} sprites ({cols}x{rows} cells)");
            return rects.Count;
        }

        private static bool IsEmpty(Color32[] px, int w, int x, int y)
        {
            for (var j = 0; j < Cell; j++)
            for (var i = 0; i < Cell; i++)
                if (px[(y + j) * w + x + i].a != 0) return false;
            return true;
        }
    }
}
