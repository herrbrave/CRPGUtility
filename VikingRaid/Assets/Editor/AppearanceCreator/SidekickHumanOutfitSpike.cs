using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>
    /// Step 2 go/no-go render: a Sidekick human dressed through the REAL applier (part sets, bind bones, rest poses from the master asset) with the Blender-authored Viking tunic, in the four Starter colour maps,
    /// then with Polygon Viking accessories (hair, helmet, beard, cape) mounted on top, front / side / back, idle-posed. Builds a skeleton-only human master and an in-memory catalog (no asset is created
    /// except the master prefab). Writes <c>appearance_human_viking_*.png</c>. Needs graphics; run without -quit (it exits itself).
    /// </summary>
    public static class SidekickHumanOutfitSpike
    {
        private const string Humans = "Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans";
        private const string Tunic = "Assets/Appearance/SidekickHumans/Outfits/VikingTunic";
                private const string SkeletonSource = "Assets/Synty/SidekickCharacters/Characters/HumanSpecies/HumanSpecies_01/HumanSpecies_01.prefab";
        private const string HeroProfile = "Assets/Characters/Animation/HeroAnimationProfile.asset";

        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new System.InvalidOperationException(path + " is missing");
        private static string Base(string region) => $"{Humans}/SK_HUMN_BASE_01_{region}_HU01.fbx";

        [MenuItem("Viking Raid/Appearance/Spike: Viking Tunic On Sidekick Human")]
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

        private static GameObject BuildMaster() => HumanCatalogAuthor.BuildMaster();

        private static ModelAppearanceOption Parts(string id, params string[] paths)
        {
            var option = new ModelAppearanceOption { id = id, title = id, kind = ModelAppearanceKind.AttachParts };
            option.parts.AddRange(paths.Select(Load));
            return option;
        }

        private static ModelAppearanceCatalog BuildCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<ModelAppearanceCatalog>();
            var face = new ModelAppearanceSlot { id = "face", title = "Face", tab = "Face", required = true, defaultOptionId = "face-3" };
            face.options.Add(Parts("face-3", Base("01HEAD"), Base("05EYEL"), Base("06EYER"), Base("37TONG"),
                $"{Humans}/SK_HUMN_BASE_03_03EBRL_HU01.fbx", $"{Humans}/SK_HUMN_BASE_03_04EBRR_HU01.fbx", $"{Humans}/SK_HUMN_BASE_03_07EARL_HU01.fbx", $"{Humans}/SK_HUMN_BASE_03_08EARR_HU01.fbx",
                $"{Humans}/SK_HUMN_BASE_03_35NOSE_HU01.fbx", $"{Humans}/SK_HUMN_BASE_03_36TETH_HU01.fbx"));
            var torso = new ModelAppearanceSlot { id = "torso", title = "Torso", tab = "Outfit", required = true, defaultOptionId = "torso-bare" };
            torso.options.Add(Parts("torso-bare", new[] { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR" }.Select(Base).ToArray()));
            torso.options.Add(Parts("torso-tunic", new[] { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR" }.Select(r => $"{Tunic}/SK_VKNG_TUNC_01_{r}_HU01.fbx").Concat(new[] { Base("15HNDL"), Base("16HNDR") }).ToArray()));
            var legs = new ModelAppearanceSlot { id = "legs", title = "Legs", tab = "Outfit", required = true, defaultOptionId = "legs-bare" };
            legs.options.Add(Parts("legs-bare", new[] { "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" }.Select(Base).ToArray()));
            legs.options.Add(Parts("legs-trousers", new[] { "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" }.Select(r => $"{Tunic}/SK_VKNG_TUNC_01_{r}_HU01.fbx").ToArray()));
            var look = new ModelAppearanceSlot { id = "look", title = "Look", tab = "Colour", required = true, defaultOptionId = "look-starter-1" };
            for (var n = 1; n <= 4; n++)
                look.options.Add(new ModelAppearanceOption { id = $"look-starter-{n}", title = $"Starter {n}", kind = ModelAppearanceKind.Palette,
                    material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Synty/SidekickCharacters/Characters/Starter/Starter_0{n}/Materials/Starter_0{n}.mat") });
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(HeroProfile);
            catalog.SetContent(new List<ModelAppearanceSlot> { face, torso, legs, look }, new List<Material>(), BuildMaster());
            catalog.SetFamily("Human", ModelPaletteScope.AppearanceParts, "Face", "head", profile != null && profile.TryGetCue(new DefinitionId("anim-idle"), out var cue) ? cue.Clip : null);
            return catalog;
        }

        /// <summary>Mounts a Polygon piece upright for the bone's REST pose (read from the master PREFAB ASSET, like the applier's RestPose): the live bone may be posed (the idle clip turns the human, tilts the head), and a rotation taken from it would bake that pose into the piece.</summary>
        private static GameObject Mount(GameObject human, ModelAppearanceCatalog catalog, string path, string bone, float scale)
        {
            var restRotation = catalog.MasterPrefab.GetComponentsInChildren<Transform>(true).First(t => t.name == bone).rotation;
            var anchor = human.GetComponentsInChildren<Transform>(true).First(t => t.name == bone);
            var clone = Object.Instantiate(Load(path));
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in clone.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            clone.transform.SetParent(anchor, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.Inverse(restRotation);
            clone.transform.localScale = Vector3.one * scale;
            return clone;
        }

        private static IEnumerator<object> Routine()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            var catalog = BuildCatalog();
            using var preview = new ModelAppearancePreview(catalog);
            try
            {
                IEnumerable<object> Ticks(int n) { for (var i = 0; i < n; i++) yield return null; }
                DefinitionId Id(string v) => new DefinitionId(v);
                var rect = new Rect(0, 0, 360, 520);
                void Save(string name) => SaveTexture(preview.Render(rect), Path.Combine(outDir, name), rect);

                // 1: the four colour maps, front, idle
                var sheet = new Texture2D(4 * 260, 380, TextureFormat.RGBA32, false);
                for (var n = 1; n <= 4; n++)
                {
                    preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [Id("torso")] = Id("torso-tunic"), [Id("legs")] = Id("legs-trousers"), [Id("look")] = Id($"look-starter-{n}") });
                    foreach (var t in Ticks(4)) yield return t;
                    if (n == 1) { preview.Render(rect); preview.Render(rect); foreach (var t in Ticks(2)) yield return t; }
                    var small = new Rect(0, 0, 260, 380);
                    sheet.SetPixels32((n - 1) * 260, 0, 260, 380, ReadPixels(preview.Render(small), small));
                }

                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "appearance_human_viking_colours.png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);

                // 2: bare vs tunic, with Viking accessories on the tunic one, front / side / back
                preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [Id("look")] = Id("look-starter-1") });
                foreach (var t in Ticks(4)) yield return t;
                Save("appearance_human_viking_bare.png");

                preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [Id("torso")] = Id("torso-tunic"), [Id("legs")] = Id("legs-trousers"), [Id("look")] = Id("look-starter-1") });
                foreach (var t in Ticks(4)) yield return t;
                Save("appearance_human_viking_tunic_front.png");
                preview.Yaw = 90f; foreach (var t in Ticks(2)) yield return t; Save("appearance_human_viking_tunic_side.png");
                preview.Yaw = 180f; foreach (var t in Ticks(2)) yield return t; Save("appearance_human_viking_tunic_back.png");
                preview.Yaw = 0f;

                const float scale = 0.935f;
                var accessories = new[]
                {
                    Mount(preview.Model, catalog, "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/Chr_Attach/SM_Chr_Attach_Helmet_Horns_02.prefab", "head", scale),
                    Mount(preview.Model, catalog, "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/Chr_Attach/SM_Chr_Attach_Beard_03.prefab", "head", scale),
                    Mount(preview.Model, catalog, "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/Chr_Attach/SM_Chr_Attach_Cape_01.prefab", "spine_03", scale),
                    Mount(preview.Model, catalog, "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/Chr_Attach/SM_Chr_Attach_Pouch_01.prefab", "pelvis", scale),
                };
                foreach (var t in Ticks(3)) yield return t;
                Save("appearance_human_viking_dressed_front.png");
                preview.Yaw = 180f; foreach (var t in Ticks(2)) yield return t; Save("appearance_human_viking_dressed_back.png");
                preview.Yaw = 35f; preview.FocusHead(true); foreach (var t in Ticks(2)) yield return t; Save("appearance_human_viking_dressed_head.png");
                foreach (var a in accessories) Object.DestroyImmediate(a);
                Debug.Log("[SidekickHumanOutfitSpike] done");
            }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        private static Color32[] ReadPixels(Texture texture, Rect rect)
        {
            var rt = RenderTexture.GetTemporary((int)rect.width, (int)rect.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, rt);
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var cell = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            cell.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            var pixels = cell.GetPixels32();
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(cell);
            return pixels;
        }

        private static void SaveTexture(Texture texture, string path, Rect rect)
        {
            var pixels = ReadPixels(texture, rect);
            var png = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
            png.SetPixels32(pixels); png.Apply();
            File.WriteAllBytes(path, png.EncodeToPNG());
            Object.DestroyImmediate(png);
        }
    }
}
