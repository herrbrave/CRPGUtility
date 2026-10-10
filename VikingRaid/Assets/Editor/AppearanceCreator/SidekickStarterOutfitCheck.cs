using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>
    /// Quick check (Opus's suggestion): how do the Starter pack's own civilian and knight outfits look on the Sidekick human with the Starter colour maps THEY were authored for (an earlier look used a species map and
    /// came out red)? Rows = outfit (civilian 09, knight 17), columns = Starter maps 1-4, seen from the front, idle. <c>appearance_human_starter_outfits.png</c>. Needs graphics; run without -quit.
    /// </summary>
    public static class SidekickStarterOutfitCheck
    {
        private const string Humans = "Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans";
        private const string Outfits = "Assets/Synty/SidekickCharacters/Resources/Meshes/Outfits/Starter";
        private const string MasterPath = HumanCatalogAuthor.MasterPath;
        private static readonly string[] Upper = { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR" };
        private static readonly string[] Lower = { "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" };

        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        [MenuItem("Viking Raid/Appearance/Check: Starter Outfits With Their Own Colour Maps")]
        public static void Run()
        {
            var routine = Routine();
            void Step()
            {
                try { if (routine.MoveNext()) return; }
                catch (System.Exception e) { Debug.LogException(e); }
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
            }

            EditorApplication.update += Step;
        }

        private static ModelAppearanceOption Set(string id, string folder, string prefix, string[] regions, bool baseHands)
        {
            var option = new ModelAppearanceOption { id = id, title = id, kind = ModelAppearanceKind.AttachParts };
            foreach (var region in regions)
            {
                var path = $"{folder}/{prefix}_{region}_HU01.fbx";
                option.parts.Add(Load(File.Exists(path) ? path : $"{Humans}/SK_HUMN_BASE_01_{region}_HU01.fbx"));
            }

            return option;
        }

        private static IEnumerator<object> Routine()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            var catalog = ScriptableObject.CreateInstance<ModelAppearanceCatalog>();
            var face = new ModelAppearanceSlot { id = "face", title = "Face", tab = "Face", required = true, defaultOptionId = "face-3" };
            var faceOption = new ModelAppearanceOption { id = "face-3", title = "face-3", kind = ModelAppearanceKind.AttachParts };
            foreach (var p in new[] { "SK_HUMN_BASE_01_01HEAD_HU01", "SK_HUMN_BASE_01_05EYEL_HU01", "SK_HUMN_BASE_01_06EYER_HU01", "SK_HUMN_BASE_01_37TONG_HU01", "SK_HUMN_BASE_03_03EBRL_HU01", "SK_HUMN_BASE_03_04EBRR_HU01",
                "SK_HUMN_BASE_03_07EARL_HU01", "SK_HUMN_BASE_03_08EARR_HU01", "SK_HUMN_BASE_03_35NOSE_HU01", "SK_HUMN_BASE_03_36TETH_HU01", "SK_HUMN_BASE_03_02HAIR_HU01" })
                faceOption.parts.Add(Load($"{Humans}/{p}.fbx"));
            face.options.Add(faceOption);
            var torso = new ModelAppearanceSlot { id = "torso", title = "Torso", tab = "Outfit", required = true, defaultOptionId = "torso-civ" };
            torso.options.Add(Set("torso-civ", Outfits, "SK_SCFI_CIVL_09", Upper, true));
            torso.options.Add(Set("torso-knight", Outfits, "SK_FANT_KNGT_17", Upper, true));
            var legs = new ModelAppearanceSlot { id = "legs", title = "Legs", tab = "Outfit", required = true, defaultOptionId = "legs-civ" };
            legs.options.Add(Set("legs-civ", Outfits, "SK_SCFI_CIVL_09", Lower, true));
            legs.options.Add(Set("legs-knight", Outfits, "SK_FANT_KNGT_17", Lower, true));
            var look = new ModelAppearanceSlot { id = "look", title = "Look", tab = "Colour", required = true, defaultOptionId = "look-1" };
            for (var n = 1; n <= 4; n++)
                look.options.Add(new ModelAppearanceOption { id = $"look-{n}", title = $"{n}", kind = ModelAppearanceKind.Palette, material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Synty/SidekickCharacters/Characters/Starter/Starter_0{n}/Materials/Starter_0{n}.mat") });
            catalog.SetContent(new List<ModelAppearanceSlot> { face, torso, legs, look }, new List<Material>(), Load(MasterPath));
            catalog.SetFamily("Human", ModelPaletteScope.AppearanceParts, "Face", "head", null);

            using var preview = new ModelAppearancePreview(catalog);
            try
            {
                IEnumerable<object> Ticks(int n) { for (var i = 0; i < n; i++) yield return null; }
                DefinitionId Id(string v) => new DefinitionId(v);
                const int w = 260, h = 380;
                var sheet = new Texture2D(4 * w, 2 * h, TextureFormat.RGBA32, false);
                var warmed = false;
                for (var row = 0; row < 2; row++)
                    for (var map = 1; map <= 4; map++)
                    {
                        var set = row == 0 ? "civ" : "knight";
                        preview.Yaw = 180f;
                        preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [Id("torso")] = Id("torso-" + set), [Id("legs")] = Id("legs-" + set), [Id("look")] = Id("look-" + map) });
                        foreach (var t in Ticks(5)) yield return t;
                        var rect = new Rect(0, 0, w, h);
                        if (!warmed) { preview.Render(rect); preview.Render(rect); foreach (var t in Ticks(3)) yield return t; warmed = true; }
                        var texture = preview.Render(rect);
                        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        Graphics.Blit(texture, rt);
                        var previous = RenderTexture.active; RenderTexture.active = rt;
                        var cell = new Texture2D(w, h, TextureFormat.RGBA32, false);
                        cell.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        sheet.SetPixels32((map - 1) * w, (1 - row) * h, w, h, cell.GetPixels32());
                        RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(cell);
                    }

                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "appearance_human_starter_outfits.png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                Debug.Log("[SidekickStarterOutfitCheck] done");
            }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }
    }
}
