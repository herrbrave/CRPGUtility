using System;
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
    /// Builds the Sidekick HUMAN appearance catalog (design <c>Docs/Plans/VikingRaid-HumanAppearance.md</c>, Section 8): a clean-shaven Sidekick human kitbashed from Starter outfits (knight, civilian), our Blender
    /// tunic, and rigid Polygon Viking / War Camp / Generic pieces (hair, beards, helmets, capes, pouches). The master is the skeleton of <c>HumanSpecies_01</c> with its combined mesh and its own dynamic chains
    /// removed; a missing source file throws with the full list. Ids for Polygon pieces deliberately equal the Viking catalog's (<c>headwear-viking-helmet-horns-02</c>), so switching an NPC between the Viking
    /// and Human families keeps its hair, headwear, cloak and gear. Derived geometry (the Blender tunic) lives in the IGNORED <c>Assets/Appearance/SidekickHumans/</c>; the master and the catalog are not derived
    /// meshes and live in <c>Assets/Appearance/Human/</c> so committed prefabs and NPC assets never dangle.
    /// </summary>
    public static class HumanCatalogAuthor
    {
        public const string Folder = "Assets/Appearance/Human";
        public const string MasterPath = Folder + "/HumanMaster.prefab";
        public const string CatalogPath = Folder + "/HumanAppearanceCatalog.asset";
        public const string HeroProfilePath = "Assets/Characters/Animation/HeroAnimationProfile.asset";

        private const string Root = "Assets/Synty/SidekickCharacters";
        private const string Humans = Root + "/Resources/Meshes/Species/Humans";
        private const string Starter = Root + "/Resources/Meshes/Outfits/Starter";
        private const string Tunic = "Assets/Appearance/SidekickHumans/Outfits/VikingTunic";
        private const string SkeletonSource = Root + "/Characters/HumanSpecies/HumanSpecies_01/HumanSpecies_01.prefab";
        private const string VikingAttach = "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/Chr_Attach";
        private const string WarCampAttach = "Assets/Synty/PolygonGoblinWarCamp/Prefabs/Characters/Chr_Attach";
        private const string GenericAttach = "Assets/Synty/PolygonGeneric/Prefabs/Characters/Attachments";

        /// <summary>Sidekick human height / Polygon height (1.759 / 1.88), measured in the spike: the uniform scale of every Polygon piece.</summary>
        public const float PolygonScale = 0.935f;

        public const string TabFace = "Face", TabOutfit = "Outfit", TabGear = "Gear", TabColour = "Colour";
        public const int SpeciesCount = 10;
        private static readonly string[] BodyRegions = { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" };

        [MenuItem("Viking Raid/Appearance/Build Human Catalog")]
        public static void Run() => Build();

        public static ModelAppearanceCatalog LoadOrBuild() => AssetDatabase.LoadAssetAtPath<ModelAppearanceCatalog>(CatalogPath) ?? Build();

        public static ModelAppearanceCatalog Build()
        {
            var problems = new List<string>();
            Directory.CreateDirectory(Folder);
            PolygonPieceFitter.ResetBody();
            var master = BuildMaster();
            var context = new Context(master, problems);

            var slots = new List<ModelAppearanceSlot>
            {
                Face(context),
                Hair(context),
                FacialHair(context),
                Outfit(context),
                Shoulders(context),
                Headwear(context),
                Faceware(context),
                Cloak(context),
                Gear(context),
                Look(context),
                BuildRow(),
                SkinRow(),
                HairColourRow(),   // appended after build: the randomizer draws per row in order, so a row inserted earlier would reshuffle build for the same seed
            };

            foreach (var slot in slots)
            {
                foreach (var duplicate in slot.options.GroupBy(o => o.id).Where(g => g.Count() > 1)) problems.Add($"slot '{slot.id}' has the option id '{duplicate.Key}' {duplicate.Count()} times");
                if (slot.options.Any(o => o.kind == ModelAppearanceKind.AttachRigged)) problems.Add($"slot '{slot.id}': a rigged option cannot be bound on a skeleton-only master");
            }

            if (problems.Count > 0) throw new InvalidOperationException("HumanCatalogAuthor: " + string.Join("; ", problems.Take(40)) + (problems.Count > 40 ? $" ... ({problems.Count} problems)" : ""));

            var catalog = AssetDatabase.LoadAssetAtPath<ModelAppearanceCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ModelAppearanceCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetContent(slots, new List<Material>(), master);
            catalog.SetFamily("Human", ModelPaletteScope.AppearanceParts, TabFace, "head", IdleClip());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HumanCatalogAuthor] {slots.Count} rows, {slots.Sum(s => s.options.Count)} options -> {CatalogPath}");
            return catalog;
        }

        // ------------------------------------------------------------------ master
        /// <summary>The skeleton of HumanSpecies_01 with its combined mesh and dynamic chains removed (the same recipe as the goblin master).</summary>
        public static GameObject BuildMaster()
        {
            Directory.CreateDirectory(Folder);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonSource) ?? throw new InvalidOperationException(SkeletonSource + " is missing");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList()) if (smr != null) UnityEngine.Object.DestroyImmediate(smr.gameObject);
                foreach (var meshRenderer in instance.GetComponentsInChildren<MeshRenderer>(true).ToList()) if (meshRenderer != null) UnityEngine.Object.DestroyImmediate(meshRenderer.gameObject);
                foreach (var dynamic in instance.GetComponentsInChildren<Transform>(true).Where(t => ModelAppearanceApplier.IsDynamic(t.name)).ToList()) if (dynamic != null) UnityEngine.Object.DestroyImmediate(dynamic.gameObject);
                instance.name = "HumanMaster";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                return PrefabUtility.SaveAsPrefabAsset(instance, MasterPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static AnimationClip IdleClip()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(HeroProfilePath);
            return profile != null && profile.TryGetCue(new DefinitionId("anim-idle"), out var cue) ? cue.Clip : null;
        }

        // ------------------------------------------------------------------ rows
        private static string Base(string region) => $"{Humans}/SK_HUMN_BASE_01_{region}_HU01.fbx";
        private static string Species(int n, string region) => $"{Humans}/SK_HUMN_BASE_{n:00}_{region}_HU01.fbx";

        private static ModelAppearanceSlot Optional(string id, string title, string tab) => new() { id = id, title = title, tab = tab, required = false };

        private static ModelAppearanceSlot Face(Context c)
        {
            var slot = new ModelAppearanceSlot { id = "face", title = "Face", tab = TabFace, required = true, defaultOptionId = "face-human-01" };
            for (var n = 1; n <= SpeciesCount; n++)
                slot.options.Add(c.PartSet($"face-human-{n:00}", $"Face {n:00}", Base("01HEAD"), Base("05EYEL"), Base("06EYER"), Base("37TONG"),
                    Species(n, "03EBRL"), Species(n, "04EBRR"), Species(n, "07EARL"), Species(n, "08EARR"), Species(n, "35NOSE"), Species(n, "36TETH")));
            return slot;
        }

        private static ModelAppearanceSlot Hair(Context c)
        {
            var slot = Optional("hair", "Hair", TabFace);
            for (var n = 1; n <= SpeciesCount; n++) slot.options.Add(c.PartSet($"hair-human-{n:00}", $"Human {n:00}", Species(n, "02HAIR")));
            foreach (var child in new[] { "Hair_01", "Hair_01_Alt", "Hair_02", "Hair_03", "Hair_03_alt", "Hair_04", "Hair_04_Alt", "Hair_05" }) slot.options.Add(c.Fitted("hair", "SM_Chr_Attach_" + child, PolygonPieceFitter.Anchor.Head, PolygonScale));
            foreach (var n in new[] { "06", "07", "08", "09" }) slot.options.Add(c.Polygon("hair", $"{VikingAttach}/SM_Chr_Attach_Hair_{n}.prefab", "viking", "Viking"));
            for (var n = 1; n <= 7; n++) slot.options.Add(c.Polygon("hair", $"{WarCampAttach}/SM_Chr_Attach_Hair_{n:00}.prefab", "war camp", "War Camp"));
            foreach (var n in new[] { "04", "05", "06", "07", "08", "09", "09_alt", "10", "11" }) slot.options.Add(c.Polygon("hair", $"{GenericAttach}/SM_Gen_Chr_Attach_Hair_{n}.prefab", "generic", "Generic"));
            slot.options.Add(c.Polygon("hair", $"{GenericAttach}/SM_Gen_Chr_Attach_Bun_01.prefab", "generic", "Generic"));
            slot.options.Add(c.Polygon("hair", $"{GenericAttach}/SM_Gen_Chr_Attach_Ponytail_01.prefab", "generic", "Generic"));
            return slot;
        }

        /// <summary>Sidekick facial hair and the Polygon beards share ONE row: a beard is one thing.</summary>
        private static ModelAppearanceSlot FacialHair(Context c)
        {
            var slot = Optional("facialhair", "Facial hair", TabFace);
            for (var n = 1; n <= SpeciesCount; n++) slot.options.Add(c.PartSet($"facialhair-human-{n:00}", $"Human {n:00}", Species(n, "09FCHR")));
            for (var n = 1; n <= 5; n++) slot.options.Add(c.Polygon("facialhair", $"{VikingAttach}/SM_Chr_Attach_Beard_{n:00}.prefab", "viking", "Viking"));
            for (var n = 1; n <= 2; n++) slot.options.Add(c.Polygon("facialhair", $"{WarCampAttach}/SM_Chr_Attach_Beard_{n:00}.prefab", "war camp", "War Camp"));
            for (var n = 1; n <= 2; n++) slot.options.Add(c.Polygon("facialhair", $"{GenericAttach}/SM_Gen_Chr_Attach_Beard_{n:00}.prefab", "generic", "Generic"));
            slot.options.Add(c.Polygon("facialhair", $"{GenericAttach}/SM_Gen_Chr_Attach_Chops_01.prefab", "generic", "Generic"));
            slot.options.Add(c.Polygon("facialhair", $"{GenericAttach}/SM_Gen_Chr_Attach_Moustache_01.prefab", "generic", "Generic"));
            return slot;
        }

        /// <summary>ONE whole-body row: every region from the same outfit, so seams stay closed (the Starter outfits layer their garments; our tunic is inflated from the body).</summary>
        private static ModelAppearanceSlot Outfit(Context c)
        {
            var slot = new ModelAppearanceSlot { id = "outfit", title = "Outfit", tab = TabOutfit, required = true, defaultOptionId = "outfit-bare" };
            slot.options.Add(c.PartSet("outfit-bare", "Bare", BodyRegions.Select(Base).ToArray()));
            slot.options.Add(c.PartSet("outfit-tunic", "Viking Tunic", BodyRegions.Select(r => IsTunicRegion(r) ? $"{Tunic}/SK_VKNG_TUNC_01_{r}_HU01.fbx" : Base(r)).ToArray()));
            slot.options.Add(c.PartSet("outfit-civilian", "Civilian", BodyRegions.Select(r => $"{Starter}/SK_SCFI_CIVL_09_{r}_HU01.fbx").ToArray()));
            slot.options.Add(c.PartSet("outfit-knight", "Knight", BodyRegions.Select(r => $"{Starter}/SK_FANT_KNGT_17_{r}_HU01.fbx").ToArray()));
            return slot;
        }

        private static bool IsTunicRegion(string region) => region != "15HNDL" && region != "16HNDR";

        private static ModelAppearanceSlot Shoulders(Context c)
        {
            var slot = Optional("shoulders", "Shoulders", TabOutfit);
            slot.options.Add(c.PartSet("shoulders-knight", "Knight", $"{Starter}/SK_FANT_KNGT_17_29ASHL_HU01.fbx", $"{Starter}/SK_FANT_KNGT_17_30ASHR_HU01.fbx"));
            slot.options.Add(c.PartSet("shoulders-civilian", "Civilian", $"{Starter}/SK_SCFI_CIVL_09_29ASHL_HU01.fbx", $"{Starter}/SK_SCFI_CIVL_09_30ASHR_HU01.fbx"));
            for (var n = 1; n <= 4; n++) slot.options.Add(c.Fitted("shoulders", $"SM_Chr_Attach_Fur_{n:00}", PolygonPieceFitter.Anchor.Shoulders, 1f));   // shoulder widths: Polygon 0.191, Sidekick 0.194
            slot.options.Add(c.Polygon("shoulders", $"{WarCampAttach}/SM_Chr_Attach_Collar_01.prefab", "war camp", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Headwear(Context c)
        {
            var slot = Optional("headwear", "Headwear", TabGear);
            foreach (var n in new[] { "01", "02", "03", "04" }) slot.options.Add(c.Polygon("headwear", $"{VikingAttach}/SM_Chr_Attach_Helmet_{n}.prefab", "viking", "Viking"));
            foreach (var n in new[] { "01", "02", "03" }) slot.options.Add(c.Polygon("headwear", $"{VikingAttach}/SM_Chr_Attach_Helmet_Horns_{n}.prefab", "viking", "Viking"));
            slot.options.Add(c.Polygon("headwear", $"{VikingAttach}/SM_Chr_Attach_Helmet_Wings_01.prefab", "viking", "Viking"));
            foreach (var child in new[] { "Wolf_01", "Bear_01" }) slot.options.Add(c.Fitted("headwear", "SM_Chr_Attach_" + child, PolygonPieceFitter.Anchor.Head, PolygonScale));
            foreach (var n in new[] { "01", "01_Alt", "02", "03" }) slot.options.Add(c.Polygon("headwear", $"{VikingAttach}/SM_Chr_Attach_Crown_{n}.prefab", "viking", "Viking"));
            for (var n = 1; n <= 7; n++) slot.options.Add(c.Polygon("headwear", $"{WarCampAttach}/SM_Chr_Attach_Hat_{n:00}.prefab", "war camp", "War Camp"));
            for (var n = 1; n <= 2; n++) slot.options.Add(c.Polygon("headwear", $"{WarCampAttach}/SM_Chr_Attach_Helmet_{n:00}.prefab", "war camp", "War Camp"));
            for (var n = 1; n <= 5; n++) slot.options.Add(c.Polygon("headwear", $"{WarCampAttach}/SM_Chr_Attach_Crown_{n:00}.prefab", "war camp", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Faceware(Context c)
        {
            var slot = Optional("faceware", "Face wear", TabGear);
            slot.options.Add(c.Polygon("faceware", $"{VikingAttach}/SM_Chr_Attach_Eyepatch_01.prefab", "viking", "Viking"));
            slot.options.Add(c.Polygon("faceware", $"{WarCampAttach}/SM_Chr_Attach_Eye_Patch_01.prefab", "war camp", "War Camp"));
            slot.options.Add(c.Polygon("faceware", $"{WarCampAttach}/SM_Chr_Attach_Goggles_01.prefab", "war camp", "War Camp"));
            slot.options.Add(c.Polygon("faceware", $"{WarCampAttach}/SM_Chr_Attach_Mask_01.prefab", "war camp", "War Camp"));
            slot.options.Add(c.Polygon("faceware", $"{WarCampAttach}/SM_Chr_Attach_Mask_02.prefab", "war camp", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Cloak(Context c)
        {
            var slot = Optional("cloak", "Cloak", TabGear);
            foreach (var n in new[] { "01", "02" }) slot.options.Add(c.Polygon("cloak", $"{VikingAttach}/SM_Chr_Attach_Cape_{n}.prefab", "viking", "Viking"));
            for (var n = 1; n <= 5; n++) slot.options.Add(c.Polygon("cloak", $"{WarCampAttach}/SM_Chr_Attach_Cape_{n:00}.prefab", "war camp", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Gear(Context c)
        {
            var slot = Optional("gear", "Gear", TabGear);
            foreach (var n in new[] { "01", "02" }) slot.options.Add(c.Polygon("gear", $"{VikingAttach}/SM_Chr_Attach_Pouch_{n}.prefab", "viking", "Viking"));
            for (var n = 1; n <= 5; n++) slot.options.Add(c.Polygon("gear", $"{WarCampAttach}/SM_Chr_Attach_Bags_{n:00}.prefab", "war camp", "War Camp"));
            for (var n = 1; n <= 2; n++) slot.options.Add(c.Polygon("gear", $"{WarCampAttach}/SM_Chr_Attach_Backpack_{n:00}.prefab", "war camp", "War Camp"));
            for (var n = 1; n <= 2; n++) slot.options.Add(c.Polygon("gear", $"{WarCampAttach}/SM_Chr_Attach_Bottle_{n:00}.prefab", "war camp", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Look(Context c)
        {
            var slot = new ModelAppearanceSlot { id = "look", title = "Look", tab = TabColour, required = true, defaultOptionId = "look-starter-1" };
            for (var n = 1; n <= 4; n++)
            {
                var path = $"{Root}/Characters/Starter/Starter_0{n}/Materials/Starter_0{n}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) c.Problems.Add("missing " + path);
                slot.options.Add(new ModelAppearanceOption { id = $"look-starter-{n}", title = $"Starter {n}", kind = ModelAppearanceKind.Palette, material = material });
            }

            return slot;
        }

        private static ModelAppearanceSlot BuildRow()
        {
            ModelAppearanceOption Option(string id, string title, params (string shape, float weight)[] weights) => new()
            {
                id = id, title = title, kind = ModelAppearanceKind.BlendShapes,
                blendShapes = weights.Select(w => new ModelAppearanceBlendShape { shape = w.shape, weight = w.weight }).ToList(),
            };

            var slot = new ModelAppearanceSlot { id = "build", title = "Build", tab = TabColour, required = true, defaultOptionId = "build-default" };
            slot.options.Add(Option("build-default", "Default"));
            slot.options.Add(Option("build-buff", "Buff", ("defaultBuff", 100f)));
            slot.options.Add(Option("build-heavy", "Heavy", ("defaultHeavy", 100f)));
            slot.options.Add(Option("build-skinny", "Skinny", ("defaultSkinny", 100f)));
            return slot;
        }

        // ------------------------------------------------------------------ skin and hair colour
        // The cells of the 32x32 Sidekick colour map (x right, y up from the bottom row), measured by PaletteCellProbe (VikingRaid_Logs/spikes/palette_cells.txt). The underwear cells (0,18) (0,19) (1,19), the lips,
        // tongue, eyes and teeth are NOT skin and keep following the look.
        private static readonly (int x, int y)[] SkinMainCells = { (2, 4), (4, 4), (2, 6), (4, 6), (2, 8), (4, 8) };
        private static readonly (int x, int y)[] SkinLightCells = { (0, 10), (0, 11), (2, 10), (4, 10), (2, 2) };   // head, ears, nose
        private static readonly (int x, int y)[] HairCells = { (12, 8), (14, 8), (12, 10), (14, 10) };           // hair, facial hair, both brows
        private static readonly (int x, int y) HairHighlightCell = (12, 2);

        /// <summary>The offset Starter_01 puts between its lighter skin cells and its main skin cells (#D5A57B vs #BB8B67): an ADDITIVE offset keeps the shading without the clipping and hue shift a per-channel ratio gives pale tones.</summary>
        private static readonly (int r, int g, int b) SkinLightOffset = (26, 26, 20);

        private static Color32 Rgb(int hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);
        private static byte Clamp(int v) => (byte)Mathf.Clamp(v, 0, 255);

        private static ModelAppearanceSlot ColourRow(string id, string title) =>
            new() { id = id, title = title, tab = TabColour, required = false, noneTitle = "From look" };

        private static ModelAppearanceSlot SkinRow()
        {
            var slot = ColourRow("skin", "Skin");
            foreach (var (name, hex) in new[] { ("pale", 0xE8C3A8), ("fair", 0xD9A988), ("light", 0xCC9873), ("tan", 0xBB8B67), ("bronze", 0xA47552), ("brown", 0x85583A), ("dark", 0x62402D), ("deep", 0x43291F) })
            {
                var main = Rgb(hex);
                var light = new Color32(Clamp(main.r + SkinLightOffset.r), Clamp(main.g + SkinLightOffset.g), Clamp(main.b + SkinLightOffset.b), 255);
                var option = new ModelAppearanceOption { id = "skin-" + name, title = char.ToUpperInvariant(name[0]) + name.Substring(1), kind = ModelAppearanceKind.ColourCells, swatch = main };
                option.cells.AddRange(SkinMainCells.Select(c => new ModelAppearanceCell { x = c.x, y = c.y, colour = main }));
                option.cells.AddRange(SkinLightCells.Select(c => new ModelAppearanceCell { x = c.x, y = c.y, colour = light }));
                slot.options.Add(option);
            }

            return slot;
        }

        private static ModelAppearanceSlot HairColourRow()
        {
            var slot = ColourRow("haircolour", "Hair colour");
            foreach (var (id, title, baseHex, highlightHex) in new[]
            {
                ("black", "Black", 0x151515, 0x4B4640), ("dark-brown", "Dark brown", 0x2A1D14, 0x7A5A3C), ("brown", "Brown", 0x352A1F, 0x987451), ("auburn", "Auburn", 0x5B2A18, 0xA8603A),
                ("ginger", "Ginger", 0x8E3F1C, 0xDB8446), ("blond", "Blond", 0xB8923E, 0xE8CF8A), ("grey", "Grey", 0x77777A, 0xC4C4C8), ("white", "White", 0xD9D9D9, 0xF8F8F8),
            })
            {
                var option = new ModelAppearanceOption { id = "haircolour-" + id, title = title, kind = ModelAppearanceKind.ColourCells, swatch = Rgb(baseHex) };
                option.cells.AddRange(HairCells.Select(c => new ModelAppearanceCell { x = c.x, y = c.y, colour = Rgb(baseHex) }));
                option.cells.Add(new ModelAppearanceCell { x = HairHighlightCell.x, y = HairHighlightCell.y, colour = Rgb(highlightHex) });
                slot.options.Add(option);
            }

            return slot;
        }

        // ------------------------------------------------------------------ shared state
        private sealed class Context
        {
            public readonly List<string> Problems;
            private readonly Dictionary<string, Quaternion> restRotation = new();

            public Context(GameObject master, List<string> problems)
            {
                Problems = problems;
                foreach (var bone in new[] { "head", "spine_03", "pelvis" })
                {
                    var t = master.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == bone);
                    if (t == null) problems.Add("the human master has no bone " + bone); else restRotation[bone] = t.rotation;   // world rotation of the bone in the master ASSET: its REST pose
                }
            }

            public ModelAppearanceOption PartSet(string id, string title, params string[] paths)
            {
                var option = new ModelAppearanceOption { id = id, title = title, kind = ModelAppearanceKind.AttachParts };
                foreach (var path in paths)
                {
                    var part = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (part == null) Problems.Add("missing " + path);
                    else if (part.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) Problems.Add(path + " imports without a SkinnedMeshRenderer (check the import settings)");
                    else option.parts.Add(part);
                }

                return option;
            }

            /// <summary>A SKINNED Viking piece (hair, pelt, fur) refitted to the Sidekick skeleton by <see cref="PolygonPieceFitter"/>: its own Polygon material, never repainted. The id is the Viking catalog's.</summary>
            public ModelAppearanceOption Fitted(string slot, string sourceChild, PolygonPieceFitter.Anchor anchor, float scale)
            {
                var rest = sourceChild.Replace("SM_Chr_Attach_", "");
                var id = Kebab(slot, "viking ", rest);
                var option = new ModelAppearanceOption { id = id, title = "Viking " + rest.Replace('_', ' '), kind = ModelAppearanceKind.AttachPartsOwnMaterial };
                var prefab = PolygonPieceFitter.Fit(sourceChild, id, anchor, scale, out var material, Problems);
                if (prefab != null) { option.parts.Add(prefab); option.material = material; }
                return option;
            }

            /// <summary>A rigid Polygon piece mounted by bone name: upright for the bone's REST rotation (read from the master asset, never the live pose), local position zero, one uniform scale.</summary>
            public ModelAppearanceOption Polygon(string slot, string path, string pack, string packTitle)
            {
                var file = Path.GetFileNameWithoutExtension(path);
                var rest = file.Replace("SM_Gen_Chr_Attach_", "").Replace("SM_Chr_Attach_", "");
                var bone = BoneFor(rest);
                var option = new ModelAppearanceOption { id = Kebab(slot, pack + " ", rest), title = packTitle + " " + rest.Replace('_', ' '), prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path), bone = bone, localScale = Vector3.one * PolygonScale };
                if (option.prefab == null) { Problems.Add("missing " + path); return option; }

                var skinned = option.prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skinned.Length == 0) option.kind = ModelAppearanceKind.AttachStatic;
                else if (rest.Contains("Cape")) option.kind = ModelAppearanceKind.AttachOwnRig;
                else Problems.Add($"{path}: a skinned piece that is not a cape (a rigged piece cannot be bound on this master)");

                if (restRotation.TryGetValue(bone, out var q)) { option.localPosition = Vector3.zero; option.localEuler = Quaternion.Inverse(q).eulerAngles; }
                if (bone != "head") AddBuildOffsets(option, bone);
                return option;
            }

            /// <summary>A blend shape moves vertices, never bones, so a rigid piece on a bone has to be moved by hand. Per build the offset starts as the body's own delta at the closest point of the bare body to the
            /// piece's centre, then is pushed out along the face the deepest vertex sank through until the piece sits no deeper than it does at the default build (at most eight steps, 30 cm). Stored in the bone's LOCAL
            /// axes (the piece is mounted upright: bone rotation * local position = world offset).</summary>
            private void AddBuildOffsets(ModelAppearanceOption option, string bone)
            {
                var surface = PolygonPieceFitter.Body(out var problem);
                if (surface == null) { Problems.Add("cannot compute build offsets: " + problem); return; }
                if (!restRotation.TryGetValue(bone, out var q)) return;
                var master = AssetDatabase.LoadAssetAtPath<GameObject>(MasterPath);
                var boneTransform = master.GetComponentsInChildren<Transform>(true).First(t => t.name == bone);
                var rest = PieceClearance.StaticVertices(option.prefab, boneTransform.position, option.localScale.x, Vector3.zero);
                if (rest.Length == 0) return;
                var centre = (rest.Aggregate(Vector3.Min) + rest.Aggregate(Vector3.Max)) / 2f;
                var hit = surface.Closest(centre);
                var atDefault = PieceClearance.Penetration(surface, rest);
                foreach (var shape in BodySurface.BuildShapes)
                {
                    var deformed = surface.Deformed(new Dictionary<string, float> { [shape] = 100f });
                    var world = surface.DeltaAt(hit, shape);
                    for (var step = 0; step < 8; step++)
                    {
                        var depth = PieceClearance.Deepest(deformed, PieceClearance.StaticVertices(option.prefab, boneTransform.position, option.localScale.x, world), out var outward);
                        var excess = depth - atDefault - 0.005f;
                        if (excess <= 0f) break;
                        world += outward * excess;
                    }

                    world = Vector3.ClampMagnitude(world, 0.3f);
                    option.buildOffsets.Add(new ModelAppearanceBuildOffset { shape = shape, offset = Quaternion.Inverse(q) * world });
                }
            }

            private static string BoneFor(string name)
            {
                var n = name.ToLowerInvariant();
                if (n.Contains("cape") || n.Contains("backpack") || n.Contains("collar")) return "spine_03";
                if (n.Contains("pouch") || n.Contains("bags") || n.Contains("bottle")) return "pelvis";
                return "head";
            }

            private static string Kebab(string slot, string prefix, string rest) => (slot + "-" + prefix + rest).ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        }
    }
}
