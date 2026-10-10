using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Core;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>
    /// Diagnostic for the creator's 3D preview: renders the preview several ways (default, a full outfit, the Head tab framing, orbited) with the same code the inspector uses, and writes
    /// <c>VikingRaid_Logs/spikes/appearance_preview_*.png</c>. Needs a graphics device (run WITHOUT -nographics). The first render of a Synty shader graph is a placeholder, so there is a warm-up.
    /// </summary>
    public static class AppearancePreviewProbe
    {
        [MenuItem("Viking Raid/Appearance/Probe: Render Preview Samples")]
        public static void Run() => RunUnattended();

        public static void RunUnattended()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var catalog = ModelAppearanceCatalogAuthor.LoadOrBuild();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            using var preview = new ModelAppearancePreview(catalog);
            try
            {
                var rect = new Rect(0, 0, 420, 420);
                var outfit = new Dictionary<DefinitionId, DefinitionId>
                {
                    [new DefinitionId("body")] = new DefinitionId("body-warrior-female-01"),
                    [new DefinitionId("look")] = new DefinitionId("look-03-b"),
                    [new DefinitionId("hair")] = new DefinitionId("hair-war-camp-hair-03"),
                    [new DefinitionId("headwear")] = new DefinitionId("headwear-viking-helmet-horns-02"),
                    [new DefinitionId("cloak")] = new DefinitionId("cloak-war-camp-cape-02"),
                    [new DefinitionId("shoulders")] = new DefinitionId("shoulders-viking-fur-02"),
                    [new DefinitionId("gear")] = new DefinitionId("gear-viking-pouch-01"),
                };

                preview.Apply(new Dictionary<DefinitionId, DefinitionId>());
                var m = preview.Model.transform;
                var hips = preview.Model.GetComponentsInChildren<Transform>(true).First(t => t.name == "Hips");
                Debug.Log($"[AppearancePreviewProbe] after idle pose: root pos {m.position} euler {m.eulerAngles}; hips forward {hips.forward} up {hips.up} right {hips.right}");
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_preview_warmup.png"), rect);   // discarded
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_preview_warmup2.png"), rect);

                Save(preview.Render(rect), Path.Combine(outDir, "appearance_preview_default.png"), rect);
                preview.Apply(outfit);
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_preview_outfit_front.png"), rect);
                preview.Yaw = 160f;
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_preview_outfit_back.png"), rect);
                preview.Yaw = 25f; preview.FocusHead(true);
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_preview_outfit_head.png"), rect);
                Debug.Log("[AppearancePreviewProbe] wrote appearance_preview_*.png to " + outDir);
            }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        [MenuItem("Viking Raid/Appearance/Probe: Option Contact Sheets")]
        public static void ContactSheets() => RunContactSheets();

        /// <summary>One sheet per row (<c>appearance_options_{row}.png</c>, 8 per line, in catalog order; the order is in <c>appearance_options_report.txt</c>): every option on the default Viking body in the idle pose, head-framed for the Head tab and full-length otherwise.</summary>
        public static void RunContactSheets() => RunTicked(ContactSheets(ModelAppearanceCatalogAuthor.LoadOrBuild(), "appearance_options_"));

        /// <summary>Runs a routine one step per EDITOR UPDATE TICK (a skinned mesh is only re-deformed by the editor loop, never inside one synchronous method), then exits the Editor. Run with graphics and WITHOUT -quit.</summary>
        /// <summary>The same sheets for the human family: <c>appearance_human_options_{row}.png</c>.</summary>
        public static void RunHumanContactSheets() => RunTicked(ContactSheets(HumanCatalogAuthor.LoadOrBuild(), "appearance_human_options_"));

        /// <summary>Skin tones and hair colours on a dressed human, head framed (<c>appearance_human_colour_{skin,hair,skin_look3}.png</c>): the first tile of each sheet is "From look". Run with graphics, WITHOUT -quit.</summary>
        public static void RunHumanColourSheets() => RunTicked(ColourSheets(HumanCatalogAuthor.LoadOrBuild()));

        private static IEnumerator<object> ColourSheets(ModelAppearanceCatalog catalog)
        {
            IEnumerable<object> Ticks(int n) { for (var i = 0; i < n; i++) yield return null; }
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            using var preview = new ModelAppearancePreview(catalog);
            try
            {
                const int tile = 240, columns = 9;
                var rect = new Rect(0, 0, tile, tile);
                preview.Apply(new Dictionary<DefinitionId, DefinitionId>());
                foreach (var tick in Ticks(4)) yield return tick;
                preview.Render(rect); preview.Render(rect);
                preview.FocusHead(true);
                var dressed = new[] { ("hair", "hair-human-03"), ("facialhair", "facialhair-human-02"), ("outfit", "outfit-civilian") };
                foreach (var (name, row, look) in new[] { ("skin", "skin", "look-starter-1"), ("hair", "haircolour", "look-starter-1"), ("skin_look3", "skin", "look-starter-3") })
                {
                    var slot = catalog.FindSlot(row);
                    var sheet = new Texture2D(columns * tile, tile, TextureFormat.RGBA32, false);
                    for (var i = 0; i <= slot.options.Count; i++)
                    {
                        var choices = dressed.ToDictionary(d => new DefinitionId(d.Item1), d => new DefinitionId(d.Item2));
                        choices[new DefinitionId("look")] = new DefinitionId(look);
                        if (i > 0) choices[new DefinitionId(row)] = new DefinitionId(slot.options[i - 1].id);
                        preview.Apply(choices);
                        foreach (var tick in Ticks(3)) yield return tick;
                        sheet.SetPixels32(i * tile, 0, tile, tile, Read(preview.Render(rect), rect));
                    }

                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"appearance_human_colour_{name}.png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                }

                Debug.Log("[AppearancePreviewProbe] wrote appearance_human_colour_*.png to " + outDir);
            }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        /// <summary>Fitted pieces on every build, front and back (<c>appearance_human_fit.png</c>: columns default, buff, heavy, skinny; rows front outfit set, back gear set). Run with graphics, WITHOUT -quit.</summary>
        public static void RunHumanFitSheet() => RunTicked(FitSheet(HumanCatalogAuthor.LoadOrBuild()));

        private static IEnumerator<object> FitSheet(ModelAppearanceCatalog catalog)
        {
            IEnumerable<object> Ticks(int n) { for (var i = 0; i < n; i++) yield return null; }
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            using var preview = new ModelAppearancePreview(catalog);
            try
            {
                const int tile = 330;
                var rect = new Rect(0, 0, tile, tile);
                preview.Apply(new Dictionary<DefinitionId, DefinitionId>());
                foreach (var tick in Ticks(4)) yield return tick;
                preview.Render(rect); preview.Render(rect);
                preview.FocusHead(false);
                var builds = new[] { "build-default", "build-buff", "build-heavy", "build-skinny" };
                var sets = new (float yaw, (string, string)[] items)[]
                {
                    (0f, new[] { ("outfit", "outfit-tunic"), ("hair", "hair-viking-hair-03"), ("headwear", "headwear-viking-wolf-01"), ("shoulders", "shoulders-viking-fur-04"), ("gear", "gear-war-camp-bags-01") }),
                    (155f, new[] { ("outfit", "outfit-civilian"), ("hair", "hair-viking-hair-01-alt"), ("cloak", "cloak-war-camp-cape-05"), ("gear", "gear-war-camp-backpack-01"), ("shoulders", "shoulders-viking-fur-01") }),
                };
                var sheet = new Texture2D(builds.Length * tile, sets.Length * tile, TextureFormat.RGBA32, false);
                for (var row = 0; row < sets.Length; row++)
                    for (var column = 0; column < builds.Length; column++)
                    {
                        var choices = sets[row].items.ToDictionary(i => new DefinitionId(i.Item1), i => new DefinitionId(i.Item2));
                        choices[new DefinitionId("build")] = new DefinitionId(builds[column]);
                        preview.Yaw = sets[row].yaw;
                        preview.Apply(choices);
                        foreach (var tick in Ticks(3)) yield return tick;
                        sheet.SetPixels32(column * tile, (sets.Length - 1 - row) * tile, tile, tile, Read(preview.Render(rect), rect));
                    }

                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "appearance_human_fit.png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                Debug.Log("[AppearancePreviewProbe] wrote appearance_human_fit.png");
            }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        private static void RunTicked(IEnumerator<object> routine)
        {
            void Step()
            {
                try { if (routine.MoveNext()) return; }
                catch (System.Exception e) { Debug.LogException(e); }
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
            }

            EditorApplication.update += Step;
        }

        /// <summary>The same sheets for the goblin family: <c>appearance_goblin_options_{row}.png</c>.</summary>
        public static void RunGoblinContactSheets() => RunTicked(ContactSheets(GoblinCatalogAuthor.LoadOrBuild(), "appearance_goblin_options_"));

        private static IEnumerator<object> ContactSheets(ModelAppearanceCatalog catalog, string prefix)
        {
            IEnumerable<object> Ticks(int n) { for (var i = 0; i < n; i++) yield return null; }
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            var report = new System.Text.StringBuilder();
            using var preview = new ModelAppearancePreview(catalog);
            try
            {
                const int tile = 220, columns = 8;
                var rect = new Rect(0, 0, tile, tile);
                preview.Apply(new Dictionary<DefinitionId, DefinitionId>());
                foreach (var tick in Ticks(4)) yield return tick;
                preview.Render(rect); preview.Render(rect);   // warm-up

                foreach (var slot in catalog.Slots)
                {
                    preview.FocusHead(slot.tab == catalog.HeadTab || slot.id == "helm" || slot.id == "headwear" || slot.id == "faceware");
                    preview.Yaw = slot.id == "cloak" || slot.id == "gear" || slot.id == "back" || slot.id == "belt" ? 155f : 0f;   // capes, packs and bags are judged from behind
                    var rows = Mathf.CeilToInt(slot.options.Count / (float)columns);
                    var sheet = new Texture2D(columns * tile, rows * tile, TextureFormat.RGBA32, false);
                    sheet.SetPixels32(Enumerable.Repeat(new Color32(30, 32, 38, 255), sheet.width * sheet.height).ToArray());
                    report.AppendLine($"== {slot.id}: {slot.options.Count} options, {columns} per line");
                    for (var i = 0; i < slot.options.Count; i++)
                    {
                        var option = slot.options[i];
                        preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [new DefinitionId(slot.id)] = new DefinitionId(option.id) });
                        foreach (var tick in Ticks(3)) yield return tick;
                        var pixels = Read(preview.Render(rect), rect);
                        sheet.SetPixels32((i % columns) * tile, (rows - 1 - i / columns) * tile, tile, tile, pixels);
                        report.AppendLine($"{i,3} r{i / columns}c{i % columns} {option.id} [{option.kind}]");
                    }

                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"{prefix}{slot.id}.png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                }

                File.WriteAllText(Path.Combine(outDir, prefix + "report.txt"), report.ToString());
                Debug.Log($"[AppearancePreviewProbe] wrote {prefix}*.png to {outDir}");
            }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        /// <summary>The goblin creator's preview, drawn the way the inspector draws it: across EDITOR UPDATE TICKS (a skinned mesh with blend shapes is only re-deformed by the editor loop, never inside one synchronous
        /// method): default, a full outfit from the front, the back and the head, and every build. <c>appearance_goblin_preview_*.png</c>. Run with graphics and WITHOUT -quit (it exits itself).</summary>
        public static void RunGoblinSamples()
        {
            var routine = GoblinSamplesRoutine();
            void Step()
            {
                try { if (routine.MoveNext()) return; }
                catch (System.Exception e) { Debug.LogException(e); }
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
            }

            EditorApplication.update += Step;
        }

        private static IEnumerator<object> GoblinSamplesRoutine()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var catalog = GoblinCatalogAuthor.LoadOrBuild();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            using var preview = new ModelAppearancePreview(catalog);
            try
            {
                var rect = new Rect(0, 0, 420, 420);
                DefinitionId Id(string v) => new DefinitionId(v);
                var outfit = new Dictionary<DefinitionId, DefinitionId>
                {
                    [Id("face")] = Id("face-goblin-03"), [Id("hair")] = Id("hair-goblin-08"), [Id("facialhair")] = Id("facialhair-goblin-06"),
                    [Id("torso")] = Id("torso-set-02"), [Id("legs")] = Id("legs-set-07"), [Id("helm")] = Id("helm-set-04"), [Id("shoulders")] = Id("shoulders-set-05"),
                    [Id("back")] = Id("back-set-03"), [Id("belt")] = Id("belt-set-06"), [Id("joints")] = Id("joints-set-08"), [Id("look")] = Id("look-fighter-3"),
                };

                IEnumerable<object> Ticks(int n) { for (var i = 0; i < n; i++) yield return null; }

                preview.Apply(new Dictionary<DefinitionId, DefinitionId>());
                foreach (var t in Ticks(4)) yield return t;
                preview.Render(rect); preview.Render(rect);   // shader warm-up
                foreach (var t in Ticks(2)) yield return t;
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_goblin_preview_default.png"), rect);

                preview.Apply(outfit);
                var armBone = preview.Model.GetComponentsInChildren<Transform>(true).First(x => x.name == "upperarm_l");
                var animatorOnModel = preview.Model.GetComponentInChildren<Animator>(true);
                for (var tick = 0; tick < 4; tick++) { Debug.Log($"[Diag] tick {tick}: upperarm_l {armBone.localEulerAngles} animator enabled={animatorOnModel.enabled} controller={(animatorOnModel.runtimeAnimatorController != null ? animatorOnModel.runtimeAnimatorController.name : "none")} isPlaying={Application.isPlaying}"); yield return null; }
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_goblin_preview_front.png"), rect);
                Debug.Log($"[Diag] after render: upperarm_l {armBone.localEulerAngles}");
                preview.Yaw = 155f;
                foreach (var t in Ticks(2)) yield return t;
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_goblin_preview_back.png"), rect);
                preview.Yaw = 25f; preview.FocusHead(true);
                foreach (var t in Ticks(2)) yield return t;
                Save(preview.Render(rect), Path.Combine(outDir, "appearance_goblin_preview_head.png"), rect);

                preview.Yaw = 0f; preview.FocusHead(false);
                var builds = catalog.FindSlot("build").options;
                var sheet = new Texture2D(builds.Count * 260, 260, TextureFormat.RGBA32, false);
                Color32[] firstPixels = null;
                for (var i = 0; i < builds.Count; i++)
                {
                    preview.Apply(new Dictionary<DefinitionId, DefinitionId>(outfit) { [Id("build")] = Id(builds[i].id) });
                    foreach (var t in Ticks(4)) yield return t;
                    var small = new Rect(0, 0, 260, 260);
                    var pixels = Read(preview.Render(small), small);
                    sheet.SetPixels32(i * 260, 0, 260, 260, pixels);
                    if (i == 0) firstPixels = pixels;
                    else Debug.Log($"[Diag] build {builds[i].id}: {Enumerable.Range(0, pixels.Length).Count(k => Mathf.Abs(pixels[k].r - firstPixels[k].r) + Mathf.Abs(pixels[k].g - firstPixels[k].g) > 12)} pixels differ from the default");
                }

                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "appearance_goblin_preview_builds.png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                Debug.Log("[AppearancePreviewProbe] wrote appearance_goblin_preview_*.png; builds: " + string.Join(", ", builds.Select(x => x.id)));
            }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        /// <summary>Control: Synty's own assembled goblin prefab through the same preview and idle sampling, across editor ticks. <c>appearance_goblin_control.png</c>.</summary>
        public static void RunGoblinControl()
        {
            var routine = ControlRoutine();
            void Step()
            {
                try { if (routine.MoveNext()) return; }
                catch (System.Exception e) { Debug.LogException(e); }
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
            }

            EditorApplication.update += Step;
        }

        private static IEnumerator<object> ControlRoutine()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            var catalog = GoblinCatalogAuthor.LoadOrBuild();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            var utility = new PreviewRenderUtility();
            try
            {
                utility.camera.fieldOfView = 28f; utility.camera.nearClipPlane = 0.03f; utility.camera.farClipPlane = 60f;
                utility.camera.clearFlags = CameraClearFlags.SolidColor; utility.camera.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
                utility.ambientColor = new Color(0.55f, 0.55f, 0.6f);
                utility.lights[0].intensity = 1.15f; utility.lights[0].transform.rotation = Quaternion.Euler(35f, 150f, 0f);
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/SidekickCharacters/Characters/GoblinFighters/GoblinFighter_01/GoblinFighter_01.prefab"));
                utility.AddSingleGO(model);
                catalog.PreviewIdleClip.SampleAnimation(model, 0.25f);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var arm = model.GetComponentsInChildren<Transform>(true).First(x => x.name == "upperarm_l");
                var hand = model.GetComponentsInChildren<Transform>(true).First(x => x.name == "hand_l");
                Debug.Log($"[Diag] control: upperarm_l {arm.localEulerAngles}, hand_l world {hand.position}");
                for (var i = 0; i < 4; i++) yield return null;
                var rect = new Rect(0, 0, 420, 420);
                for (var pass = 0; pass < 2; pass++)
                {
                    utility.BeginPreview(rect, GUIStyle.none);
                    var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                    var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                    utility.camera.transform.position = b.center + new Vector3(0f, 0.05f, 3.4f);
                    utility.camera.transform.rotation = Quaternion.LookRotation(b.center - utility.camera.transform.position, Vector3.up);
                    utility.Render(true);
                    var texture = utility.EndPreview();
                    if (pass == 1) Save(texture, Path.Combine(outDir, "appearance_goblin_control.png"), rect);
                    yield return null;
                }
            }
            finally { utility.Cleanup(); EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        /// <summary>Experiment: which nudge makes attached part renderers follow the sampled skeleton? Logs the pixel difference of each variant against the stale render.</summary>
        public static void RunGoblinStaleness()
        {
            var routine = StalenessRoutine();
            void Step()
            {
                try { if (routine.MoveNext()) return; }
                catch (System.Exception e) { Debug.LogException(e); }
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
            }

            EditorApplication.update += Step;
        }

        private static IEnumerator<object> StalenessRoutine()
        {
            var catalog = GoblinCatalogAuthor.LoadOrBuild();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            using var preview = new ModelAppearancePreview(catalog);
            DefinitionId Id(string v) => new DefinitionId(v);
            var rect = new Rect(0, 0, 260, 260);
            var smrs = new List<SkinnedMeshRenderer>();
            Color32[] baseline = null;
            int Diff(Color32[] p) => Enumerable.Range(0, p.Length).Count(k => Mathf.Abs(p[k].r - baseline[k].r) + Mathf.Abs(p[k].g - baseline[k].g) > 12);
            IEnumerable<object> Ticks(int n) { for (var i = 0; i < n; i++) yield return null; }
            Color32[] Shot() => Read(preview.Render(rect), rect);

            preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [Id("torso")] = Id("torso-set-02"), [Id("legs")] = Id("legs-set-07") });
            foreach (var t in Ticks(4)) yield return t;
            preview.Render(rect); preview.Render(rect);
            foreach (var t in Ticks(2)) yield return t;
            baseline = Shot();
            smrs = preview.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) != null).ToList();
            Debug.Log($"[Diag] staleness: {smrs.Count} part renderers");

            foreach (var s in smrs) s.enabled = false;
            yield return null;
            foreach (var s in smrs) s.enabled = true;
            foreach (var t in Ticks(3)) yield return t;
            Debug.Log($"[Diag] V1 toggle enabled: {Diff(Shot())} pixels differ");

            foreach (var s in smrs) s.bones = s.bones;
            foreach (var t in Ticks(3)) yield return t;
            Debug.Log($"[Diag] V2 reassign bones: {Diff(Shot())} pixels differ");

            foreach (var s in smrs) s.sharedMesh = s.sharedMesh;
            foreach (var t in Ticks(3)) yield return t;
            Debug.Log($"[Diag] V3 reassign mesh: {Diff(Shot())} pixels differ");

            foreach (var s in smrs) s.rootBone = s.rootBone;
            foreach (var t in Ticks(3)) yield return t;
            Debug.Log($"[Diag] V4 reassign root bone: {Diff(Shot())} pixels differ");

            // the same sampling again, as the creator does on every change
            catalog.PreviewIdleClip.SampleAnimation(preview.Model, 0.31f);
            preview.Model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var t in Ticks(3)) yield return t;
            var v5 = Shot();
            Debug.Log($"[Diag] V5 sample a different time: {Diff(v5)} pixels differ");
            var v5Texture = new Texture2D(260, 260, TextureFormat.RGBA32, false); v5Texture.SetPixels32(v5); v5Texture.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes")), "appearance_goblin_v5.png"), v5Texture.EncodeToPNG());
            var base0 = new Texture2D(260, 260, TextureFormat.RGBA32, false); base0.SetPixels32(baseline); base0.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes")), "appearance_goblin_v0.png"), base0.EncodeToPNG());

            // a baked mesh of one part against the bone it hangs on: does the CPU path see the pose?
            var hand = smrs.First(s => s.sharedMesh.name.Contains("15HNDL"));
            var baked = new Mesh(); hand.BakeMesh(baked);
            var centre = hand.transform.TransformPoint(baked.bounds.center);
            var handBone = preview.Model.GetComponentsInChildren<Transform>(true).First(x => x.name == "hand_l");
            Debug.Log($"[Diag] V6 baked left hand centre {centre} vs hand_l bone {handBone.position}; mesh bind-pose centre {hand.transform.TransformPoint(hand.sharedMesh.bounds.center)}");
            EditorSettings.asyncShaderCompilation = previousAsync;
        }

        /// <summary>Experiment: apply, render, then apply MORE and render: is the second dressing posed? Writes appearance_goblin_seq_{n}.png and logs the baked hand against the bone.</summary>
        public static void RunGoblinSequence()
        {
            var routine = SequenceRoutine();
            void Step()
            {
                try { if (routine.MoveNext()) return; }
                catch (System.Exception e) { Debug.LogException(e); }
                EditorApplication.update -= Step;
                EditorApplication.Exit(0);
            }

            EditorApplication.update += Step;
        }

        private static IEnumerator<object> SequenceRoutine()
        {
            var catalog = GoblinCatalogAuthor.LoadOrBuild();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            using var preview = new ModelAppearancePreview(catalog);
            DefinitionId Id(string v) => new DefinitionId(v);
            var rect = new Rect(0, 0, 260, 260);
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            IEnumerable<object> Ticks(int n) { for (var i = 0; i < n; i++) yield return null; }
            void Snap(int n, string what)
            {
                var pixels = Read(preview.Render(rect), rect);
                var texture = new Texture2D(260, 260, TextureFormat.RGBA32, false); texture.SetPixels32(pixels); texture.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"appearance_goblin_seq_{n}.png"), texture.EncodeToPNG());
                var hand = preview.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.sharedMesh != null && s.sharedMesh.name.Contains("15HNDL"));
                var bone = preview.Model.GetComponentsInChildren<Transform>(true).First(x => x.name == "hand_l");
                var baked = new Mesh(); hand.BakeMesh(baked);
                Debug.Log($"[Diag] seq {n} ({what}): baked hand {hand.transform.TransformPoint(baked.bounds.center)} bone {bone.position}");
            }

            preview.Apply(new Dictionary<DefinitionId, DefinitionId>());
            foreach (var t in Ticks(4)) yield return t;
            preview.Render(rect); preview.Render(rect);
            Snap(0, "bare default");

            preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [Id("torso")] = Id("torso-set-02"), [Id("legs")] = Id("legs-set-07") });
            foreach (var t in Ticks(4)) yield return t;
            Snap(1, "torso+legs after a bare render");

            preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [Id("torso")] = Id("torso-set-02"), [Id("legs")] = Id("legs-set-07"), [Id("helm")] = Id("helm-set-04"), [Id("shoulders")] = Id("shoulders-set-05"), [Id("belt")] = Id("belt-set-06") });
            foreach (var t in Ticks(4)) yield return t;
            Snap(2, "plus helm, shoulders, belt");
            EditorSettings.asyncShaderCompilation = previousAsync;
        }

        /// <summary>Numbers behind two visual questions: does the sampled idle clip move the goblin skeleton, and do the blend shapes displace the baked mesh? Logs to the Editor log.</summary>
        public static void RunGoblinDiagnostics()
        {
            var catalog = GoblinCatalogAuthor.LoadOrBuild();
            var root = Object.Instantiate(catalog.MasterPrefab);
            try
            {
                DefinitionId Id(string v) => new DefinitionId(v);
                ModelAppearanceApplier.Apply(root, catalog, new Dictionary<DefinitionId, DefinitionId> { [Id("torso")] = Id("torso-set-02") }, immediate: true);
                var animator = root.GetComponentInChildren<Animator>(true);
                var clip = catalog.PreviewIdleClip;
                var arm = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "upperarm_l");
                Debug.Log($"[Diag] animator on '{animator.name}' enabled={animator.enabled} avatar={(animator.avatar != null ? animator.avatar.name + " human=" + animator.avatar.isHuman + " valid=" + animator.avatar.isValid : "none")}; clip={(clip != null ? clip.name + " human=" + clip.isHumanMotion + " len=" + clip.length + " legacy=" + clip.legacy : "none")}");
                var before = arm.localEulerAngles;
                clip.SampleAnimation(root, 0.25f);
                Debug.Log($"[Diag] upperarm_l local euler before {before} after {arm.localEulerAngles}; root euler {root.transform.eulerAngles}");

                var torso = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name.StartsWith("Appearance_torso") && s.sharedMesh.name.Contains("10TORS"));
                var mesh = torso.sharedMesh;
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    var deltas = new Vector3[mesh.vertexCount]; var normals = new Vector3[mesh.vertexCount]; var tangents = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(i, mesh.GetBlendShapeFrameCount(i) - 1, deltas, normals, tangents);
                    Debug.Log($"[Diag] torso blend shape {name}: frames {mesh.GetBlendShapeFrameCount(i)} weight {mesh.GetBlendShapeFrameWeight(i, mesh.GetBlendShapeFrameCount(i) - 1)} max delta {deltas.Max(d => d.magnitude):F4} m");
                }

                var baked0 = new Mesh(); var baked1 = new Mesh();
                torso.SetBlendShapeWeight(ModelAppearanceApplier.BlendShapeIndex(mesh, "defaultBuff"), 0f); torso.BakeMesh(baked0);
                torso.SetBlendShapeWeight(ModelAppearanceApplier.BlendShapeIndex(mesh, "defaultBuff"), 100f); torso.BakeMesh(baked1);
                var v0 = baked0.vertices; var v1 = baked1.vertices;
                Debug.Log($"[Diag] baked torso, buff 0 -> 100: max vertex displacement {Enumerable.Range(0, v0.Length).Max(k => (v1[k] - v0[k]).magnitude):F4} m");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>Head close-ups of every body (front and three-quarter), <c>appearance_body_heads.png</c>: which bodies carry facial hair of their own.</summary>
        public static void RunBodyHeads()
        {
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(outDir);
            var catalog = ModelAppearanceCatalogAuthor.LoadOrBuild();
            var previousAsync = EditorSettings.asyncShaderCompilation;
            EditorSettings.asyncShaderCompilation = false;
            using var preview = new ModelAppearancePreview(catalog);
            try
            {
                const int tile = 260;
                var rect = new Rect(0, 0, tile, tile);
                var bodies = catalog.FindSlot("body").options;
                preview.FocusHead(true);
                preview.Apply(new Dictionary<DefinitionId, DefinitionId>());
                preview.Render(rect); preview.Render(rect);
                var sheet = new Texture2D(5 * tile, 4 * tile, TextureFormat.RGBA32, false);
                for (var i = 0; i < bodies.Count; i++)
                {
                    preview.Apply(new Dictionary<DefinitionId, DefinitionId> { [new DefinitionId("body")] = new DefinitionId(bodies[i].id) });
                    for (var view = 0; view < 2; view++)
                    {
                        preview.Yaw = view == 0 ? 0f : 35f;
                        sheet.SetPixels32((i % 5) * tile, (3 - (i / 5) * 2 - view) * tile, tile, tile, Read(preview.Render(rect), rect));
                    }
                }

                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "appearance_body_heads.png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                Debug.Log("[AppearancePreviewProbe] wrote appearance_body_heads.png; order: " + string.Join(", ", bodies.Select(b => b.id)));
            }
            finally { EditorSettings.asyncShaderCompilation = previousAsync; }
        }

        private static Color32[] Read(Texture texture, Rect rect)
        {
            var rt = RenderTexture.GetTemporary((int)rect.width, (int)rect.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var cell = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            cell.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            var pixels = cell.GetPixels32();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(cell);
            return pixels;
        }

        private static void Save(Texture texture, string path, Rect rect)
        {
            if (texture == null) { Debug.LogError("[AppearancePreviewProbe] the preview rendered no texture for " + path); return; }
            var rt = RenderTexture.GetTemporary((int)rect.width, (int)rect.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var png = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            png.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            png.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, png.EncodeToPNG());
            Object.DestroyImmediate(png);
        }
    }
}
