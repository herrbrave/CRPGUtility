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
    /// Builds the Sidekick GOBLIN appearance catalog (12 rows on 4 tabs, design <c>Docs/Plans/VikingRaid-GoblinAppearance.md</c>) and its skeleton-only master prefab. The Sidekick prefabs are one combined
    /// mesh each, so the master is the species-01 prefab with its combined mesh AND its own dynamic-chain bones removed (those names are generic: they would be matched by the wrong parts); every visible piece
    /// is a part set attached by the applier. A missing part file throws with the full list (a catalog silently smaller is worse than none). Re-running updates the assets in place (GUIDs survive).
    /// </summary>
    public static class GoblinCatalogAuthor
    {
        public const string Folder = "Assets/Appearance/Goblin";
        public const string MasterPath = Folder + "/GoblinMaster.prefab";
        public const string CatalogPath = Folder + "/GoblinAppearanceCatalog.asset";
        public const string GoblinProfilePath = "Assets/Characters/Animation/GoblinAnimationProfile.asset";

        private const string Root = "Assets/Synty/SidekickCharacters";
        private const string Species = Root + "/Resources/Meshes/Species/Goblins";
        private const string Fighters = Root + "/Resources/Meshes/Outfits/GoblinFighters";
        private const string SkeletonSource = Root + "/Characters/GoblinSpecies/GoblinSpecies_01/GoblinSpecies_01.prefab";

        public const string TabFace = "Face", TabOutfit = "Outfit", TabGear = "Gear", TabColour = "Colour";
        public const int SpeciesCount = 10, SetCount = 10;

        [MenuItem("Viking Raid/Appearance/Build Goblin Catalog")]
        public static void Run() => Build();

        public static ModelAppearanceCatalog LoadOrBuild() => AssetDatabase.LoadAssetAtPath<ModelAppearanceCatalog>(CatalogPath) ?? Build();

        public static ModelAppearanceCatalog Build()
        {
            var problems = new List<string>();
            Directory.CreateDirectory(Folder);
            var master = BuildMaster();

            var slots = new List<ModelAppearanceSlot>
            {
                Face(problems), Hair("hair", "Hair", "02HAIR", problems), Hair("facialhair", "Facial hair", "09FCHR", problems),
                OutfitRow("torso", "Torso", TabOutfit, true, problems, "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR"),
                OutfitRow("legs", "Legs", TabOutfit, true, problems, "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR"),
                OutfitRow("helm", "Helm", TabOutfit, false, problems, "22AHED", "23AFAC"),
                OutfitRow("shoulders", "Shoulders", TabGear, false, problems, "29ASHL", "30ASHR"),
                OutfitRow("back", "Back", TabGear, false, problems, "24ABAC"),
                OutfitRow("belt", "Belt", TabGear, false, problems, "25AHPF", "26AHPB", "27AHPL", "28AHPR"),
                OutfitRow("joints", "Joints", TabGear, false, problems, "31AEBL", "32AEBR", "33AKNL", "34AKNR"),
                Look(problems),
                BuildRow(),
            };

            foreach (var slot in slots)
                foreach (var duplicate in slot.options.GroupBy(o => o.id).Where(g => g.Count() > 1)) problems.Add($"slot '{slot.id}' has the option id '{duplicate.Key}' {duplicate.Count()} times");
            if (problems.Count > 0) throw new InvalidOperationException("GoblinCatalogAuthor: " + string.Join("; ", problems.Take(40)) + (problems.Count > 40 ? $" ... ({problems.Count} problems)" : ""));

            var catalog = AssetDatabase.LoadAssetAtPath<ModelAppearanceCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ModelAppearanceCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetContent(slots, new List<Material>(), master);
            catalog.SetFamily("Goblin", ModelPaletteScope.AppearanceParts, TabFace, "head", IdleClip());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GoblinCatalogAuthor] {slots.Count} rows, {slots.Sum(s => s.options.Count)} options -> {CatalogPath}");
            return catalog;
        }

        // ------------------------------------------------------------------ master
        private static GameObject BuildMaster()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonSource) ?? throw new InvalidOperationException(SkeletonSource + " is missing");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList()) if (smr != null) UnityEngine.Object.DestroyImmediate(smr.gameObject);
                foreach (var meshRenderer in instance.GetComponentsInChildren<MeshRenderer>(true).ToList()) if (meshRenderer != null) UnityEngine.Object.DestroyImmediate(meshRenderer.gameObject);
                foreach (var dynamic in instance.GetComponentsInChildren<Transform>(true).Where(t => ModelAppearanceApplier.IsDynamic(t.name)).ToList()) if (dynamic != null) UnityEngine.Object.DestroyImmediate(dynamic.gameObject);
                instance.name = "GoblinMaster";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var master = PrefabUtility.SaveAsPrefabAsset(instance, MasterPath);
                return master;
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static AnimationClip IdleClip()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(GoblinProfilePath);
            return profile != null && profile.TryGetCue(new DefinitionId("anim-idle"), out var cue) ? cue.Clip : null;
        }

        // ------------------------------------------------------------------ rows
        private static string SpeciesPart(int n, string region) => $"{Species}/SK_GOBL_BASE_{n:00}_{region}_GO01.fbx";
        private static string SetPart(int n, string region) => $"{Fighters}/SK_GOBL_FIGT_{n:00}_{region}_GO01.fbx";

        private static ModelAppearanceOption PartSet(string id, string title, List<string> problems, params string[] paths)
        {
            var option = new ModelAppearanceOption { id = id, title = title, kind = ModelAppearanceKind.AttachParts };
            foreach (var path in paths)
            {
                var part = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (part == null) problems.Add("missing " + path);
                else option.parts.Add(part);
            }

            return option;
        }

        private static ModelAppearanceSlot Face(List<string> problems)
        {
            var slot = new ModelAppearanceSlot { id = "face", title = "Face", tab = TabFace, required = true, defaultOptionId = "face-goblin-01" };
            for (var n = 1; n <= SpeciesCount; n++)
                slot.options.Add(PartSet($"face-goblin-{n:00}", $"Goblin {n:00}", problems,
                    SpeciesPart(1, "01HEAD"), SpeciesPart(1, "05EYEL"), SpeciesPart(1, "06EYER"), SpeciesPart(1, "37TONG"),
                    SpeciesPart(n, "03EBRL"), SpeciesPart(n, "04EBRR"), SpeciesPart(n, "07EARL"), SpeciesPart(n, "08EARR"), SpeciesPart(n, "35NOSE"), SpeciesPart(n, "36TETH")));
            return slot;
        }

        /// <summary>Hair and facial hair: the ten species pieces, then the ten fighter-set pieces (None first, added by the creator model).</summary>
        private static ModelAppearanceSlot Hair(string id, string title, string region, List<string> problems)
        {
            var slot = new ModelAppearanceSlot { id = id, title = title, tab = TabFace, required = false };
            for (var n = 1; n <= SpeciesCount; n++) slot.options.Add(PartSet($"{id}-goblin-{n:00}", $"Goblin {n:00}", problems, SpeciesPart(n, region)));
            for (var n = 1; n <= SetCount; n++) slot.options.Add(PartSet($"{id}-set-{n:00}", $"Fighter {n:00}", problems, SetPart(n, region)));
            return slot;
        }

        /// <summary>An outfit row: the fighter sets 01..10 (and, for a required row, Bare first = the base body's own parts).</summary>
        private static ModelAppearanceSlot OutfitRow(string id, string title, string tab, bool required, List<string> problems, params string[] regions)
        {
            var slot = new ModelAppearanceSlot { id = id, title = title, tab = tab, required = required, defaultOptionId = required ? $"{id}-bare" : null };
            if (required) slot.options.Add(PartSet($"{id}-bare", "Bare", problems, regions.Select(r => SpeciesPart(1, r)).ToArray()));
            for (var n = 1; n <= SetCount; n++) slot.options.Add(PartSet($"{id}-set-{n:00}", $"Set {n:00}", problems, regions.Select(r => SetPart(n, r)).ToArray()));
            return slot;
        }

        private static ModelAppearanceSlot Look(List<string> problems)
        {
            var slot = new ModelAppearanceSlot { id = "look", title = "Look", tab = TabColour, required = true, defaultOptionId = "look-fighter-1" };
            foreach (var (family, folder, label) in new[] { ("GoblinFighter", "GoblinFighters", "Fighter"), ("GoblinSpecies", "GoblinSpecies", "Species") }.Select(t => (t.Item1, t.Item2, t.Item3)))
                for (var n = 1; n <= 5; n++)
                {
                    var path = $"{Root}/Characters/{folder}/{family}_{n:00}/Materials/{family}_{n:00}.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) problems.Add("missing " + path);
                    slot.options.Add(new ModelAppearanceOption { id = $"look-{label.ToLowerInvariant()}-{n}", title = $"{label} {n}", kind = ModelAppearanceKind.Palette, material = material });
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
            slot.options.Add(Option("build-feminine", "Feminine", ("masculineFeminine", 100f)));
            return slot;
        }
    }
}
