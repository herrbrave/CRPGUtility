using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>
    /// Builds <c>ModelAppearanceCatalog.asset</c> from the Synty pack folders: the eight rows on three tabs settled by the spike (design Section 11). Re-running rebuilds the asset in place (same
    /// GUID, so the base prefab keeps its reference). Ids are derived from the mesh names and never change once an NPC uses them; the toggle names come from the MASTER's own children.
    /// <list type="bullet">
    /// <item>A static prefab is mounted at local position zero with rotation = the inverse of the bone's rest world rotation, computed here from the master and stored as plain data (runtime sets local transforms only).</item>
    /// <item>A prefab carrying its own skeleton (capes) is mounted whole; a prefab that is a whole character on the master's skeleton (hoods, shawl) is extracted by renderer and re-skinned.</item>
    /// </list>
    /// A missing pack asset throws with the full list, rather than silently producing a smaller catalog.
    /// </summary>
    public static class ModelAppearanceCatalogAuthor
    {
        public const string CatalogPath = "Assets/Appearance/ModelAppearanceCatalog.asset";
        public const string MasterPath = "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/SM_Chr_Warrior_Male_01.prefab";
        private const string Viking = "Assets/Synty/PolygonVikingRealm/Prefabs/Characters/Chr_Attach";
        private const string WarCamp = "Assets/Synty/PolygonGoblinWarCamp/Prefabs/Characters/Chr_Attach";
        private const string Generic = "Assets/Synty/PolygonGeneric/Prefabs/Characters/Attachments";
        private const string Alts = "Assets/Synty/PolygonVikingRealm/Materials/Alts";

        public const string TabBody = "Body", TabHead = "Head", TabWear = "Wear";
        public const string DefaultBody = "body-warrior-male-01", DefaultLook = "look-01-a";

        [MenuItem("Viking Raid/Appearance/Build Catalog")]
        public static void Run() => Build();

        public static ModelAppearanceCatalog LoadOrBuild() => AssetDatabase.LoadAssetAtPath<ModelAppearanceCatalog>(CatalogPath) ?? Build();

        public static ModelAppearanceCatalog Build()
        {
            var master = AssetDatabase.LoadAssetAtPath<GameObject>(MasterPath) ?? throw new InvalidOperationException(MasterPath + " is missing");
            var problems = new List<string>();
            var context = new Context(master, problems);

            var slots = new List<ModelAppearanceSlot>
            {
                Body(context),
                Look(context),
                Hair(context),
                Headwear(context),
                Face(context),
                Cloak(context),
                Shoulders(context),
                Gear(context),
            };

            foreach (var slot in slots)
                foreach (var duplicate in slot.options.GroupBy(o => o.id).Where(g => g.Count() > 1)) problems.Add($"slot '{slot.id}' has the option id '{duplicate.Key}' {duplicate.Count()} times");

            if (problems.Count > 0) throw new InvalidOperationException("ModelAppearanceCatalogAuthor: " + string.Join("; ", problems));

            var catalog = AssetDatabase.LoadAssetAtPath<ModelAppearanceCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
                catalog = ScriptableObject.CreateInstance<ModelAppearanceCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetContent(slots, PaletteFamily(master, context), master);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ModelAppearanceCatalogAuthor] {slots.Count} rows, {slots.Sum(s => s.options.Count)} options -> {CatalogPath}");
            return catalog;
        }

        // ------------------------------------------------------------------ rows
        private static ModelAppearanceSlot Body(Context c)
        {
            var slot = new ModelAppearanceSlot { id = "body", title = "Body", tab = TabBody, required = true, defaultOptionId = DefaultBody };
            foreach (var kind in new[] { "Warrior", "Peasant", "Leader" })
                foreach (var sex in new[] { "Male", "Female" })
                    foreach (var child in c.MasterChildren.Where(n => n.StartsWith($"SM_Chr_{kind}_{sex}_")).OrderBy(n => n))
                        slot.options.Add(c.Toggle("body", child, "SM_Chr_"));
            if (slot.Find(DefaultBody) == null) c.Problems.Add("the master has no default body " + DefaultBody);
            return slot;
        }

        private static ModelAppearanceSlot Look(Context c)
        {
            var slot = new ModelAppearanceSlot { id = "look", title = "Look", tab = TabBody, required = true, defaultOptionId = DefaultLook };
            foreach (var material in c.Palettes)
            {
                var name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(material));   // PolygonVikingRealm_01_A
                var tail = name.Substring(name.IndexOf('_') + 1).ToLowerInvariant();                  // 01_a
                slot.options.Add(new ModelAppearanceOption { id = "look-" + tail.Replace('_', '-'), title = "Look " + tail.Replace('_', ' ').ToUpperInvariant(), kind = ModelAppearanceKind.Palette, material = material });
            }

            if (slot.Find(DefaultLook) == null) c.Problems.Add("no default look " + DefaultLook);
            return slot;
        }

        private static ModelAppearanceSlot Hair(Context c)
        {
            var slot = Optional("hair", "Hair", TabHead);
            foreach (var child in c.MasterChildren.Where(n => n.StartsWith("SM_Chr_Attach_Hair_")).OrderBy(n => n)) slot.options.Add(c.Toggle("hair", child, "SM_Chr_Attach_", "Viking "));
            foreach (var n in new[] { "06", "07", "08", "09" }) slot.options.Add(c.Attach("hair", $"{Viking}/SM_Chr_Attach_Hair_{n}.prefab", "Viking"));
            foreach (var n in Enumerable.Range(1, 7)) slot.options.Add(c.Attach("hair", $"{WarCamp}/SM_Chr_Attach_Hair_{n:00}.prefab", "War Camp"));
            foreach (var n in new[] { "04", "05", "06", "07", "08", "09", "09_alt", "10", "11" }) slot.options.Add(c.Attach("hair", $"{Generic}/SM_Gen_Chr_Attach_Hair_{n}.prefab", "Generic"));
            slot.options.Add(c.Attach("hair", $"{Generic}/SM_Gen_Chr_Attach_Bun_01.prefab", "Generic"));
            slot.options.Add(c.Attach("hair", $"{Generic}/SM_Gen_Chr_Attach_Ponytail_01.prefab", "Generic"));
            return slot;
        }

        private static ModelAppearanceSlot Headwear(Context c)
        {
            var slot = Optional("headwear", "Headwear", TabHead);
            foreach (var n in new[] { "01", "02", "03", "04" }) slot.options.Add(c.Attach("headwear", $"{Viking}/SM_Chr_Attach_Helmet_{n}.prefab", "Viking"));
            foreach (var n in new[] { "01", "02", "03" }) slot.options.Add(c.Attach("headwear", $"{Viking}/SM_Chr_Attach_Helmet_Horns_{n}.prefab", "Viking"));
            slot.options.Add(c.Attach("headwear", $"{Viking}/SM_Chr_Attach_Helmet_Wings_01.prefab", "Viking"));
            foreach (var n in new[] { "01", "01_Alt", "02", "03" }) slot.options.Add(c.Attach("headwear", $"{Viking}/SM_Chr_Attach_Crown_{n}.prefab", "Viking"));
            slot.options.Add(c.Toggle("headwear", "SM_Chr_Attach_Wolf_01", "SM_Chr_Attach_", "Viking "));
            slot.options.Add(c.Toggle("headwear", "SM_Chr_Attach_Bear_01", "SM_Chr_Attach_", "Viking "));
            foreach (var n in Enumerable.Range(1, 7)) slot.options.Add(c.Attach("headwear", $"{WarCamp}/SM_Chr_Attach_Hat_{n:00}.prefab", "War Camp"));
            foreach (var n in Enumerable.Range(1, 2)) slot.options.Add(c.Attach("headwear", $"{WarCamp}/SM_Chr_Attach_Helmet_{n:00}.prefab", "War Camp"));
            foreach (var n in Enumerable.Range(1, 5)) slot.options.Add(c.Attach("headwear", $"{WarCamp}/SM_Chr_Attach_Crown_{n:00}.prefab", "War Camp"));
            foreach (var n in Enumerable.Range(1, 3)) slot.options.Add(c.Attach("headwear", $"{WarCamp}/SM_Chr_Attach_Hood_{n:00}_Rigged.prefab", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Face(Context c)
        {
            var slot = Optional("face", "Face", TabHead);
            slot.options.Add(c.Attach("face", $"{Viking}/SM_Chr_Attach_Eyepatch_01.prefab", "Viking"));
            slot.options.Add(c.Attach("face", $"{WarCamp}/SM_Chr_Attach_Eye_Patch_01.prefab", "War Camp"));
            slot.options.Add(c.Attach("face", $"{WarCamp}/SM_Chr_Attach_Goggles_01.prefab", "War Camp"));
            slot.options.Add(c.Attach("face", $"{WarCamp}/SM_Chr_Attach_Mask_01.prefab", "War Camp"));
            slot.options.Add(c.Attach("face", $"{WarCamp}/SM_Chr_Attach_Mask_02.prefab", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Cloak(Context c)
        {
            var slot = Optional("cloak", "Cloak", TabWear);
            foreach (var n in new[] { "01", "02" }) slot.options.Add(c.Attach("cloak", $"{Viking}/SM_Chr_Attach_Cape_{n}.prefab", "Viking"));
            foreach (var n in Enumerable.Range(1, 5)) slot.options.Add(c.Attach("cloak", $"{WarCamp}/SM_Chr_Attach_Cape_{n:00}.prefab", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Shoulders(Context c)
        {
            var slot = Optional("shoulders", "Shoulders", TabWear);
            foreach (var child in c.MasterChildren.Where(n => n.StartsWith("SM_Chr_Attach_Fur_")).OrderBy(n => n)) slot.options.Add(c.Toggle("shoulders", child, "SM_Chr_Attach_", "Viking "));
            slot.options.Add(c.Attach("shoulders", $"{WarCamp}/SM_Chr_Attach_Shawl_01_Rigged.prefab", "War Camp"));
            slot.options.Add(c.Attach("shoulders", $"{WarCamp}/SM_Chr_Attach_Collar_01.prefab", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Gear(Context c)
        {
            var slot = Optional("gear", "Gear", TabWear);
            foreach (var n in new[] { "01", "02" }) slot.options.Add(c.Attach("gear", $"{Viking}/SM_Chr_Attach_Pouch_{n}.prefab", "Viking"));
            foreach (var n in Enumerable.Range(1, 5)) slot.options.Add(c.Attach("gear", $"{WarCamp}/SM_Chr_Attach_Bags_{n:00}.prefab", "War Camp"));
            foreach (var n in Enumerable.Range(1, 2)) slot.options.Add(c.Attach("gear", $"{WarCamp}/SM_Chr_Attach_Backpack_{n:00}.prefab", "War Camp"));
            foreach (var n in Enumerable.Range(1, 2)) slot.options.Add(c.Attach("gear", $"{WarCamp}/SM_Chr_Attach_Bottle_{n:00}.prefab", "War Camp"));
            return slot;
        }

        private static ModelAppearanceSlot Optional(string id, string title, string tab) => new() { id = id, title = title, tab = tab, required = false };

        /// <summary>Every Viking-pack material a character can carry: the 15 palettes, the master's own, and what the Viking attachments use. The Palette option replaces any of them (War Camp and Generic parts keep theirs).</summary>
        private static List<Material> PaletteFamily(GameObject master, Context c)
        {
            var family = new List<Material>(c.Palettes);
            void Add(Material m) { if (m != null && !family.Contains(m) && AssetDatabase.GetAssetPath(m).StartsWith("Assets/Synty/PolygonVikingRealm/")) family.Add(m); }
            foreach (var r in master.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) Add(m);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Viking }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) Add(m);
            }

            return family;
        }

        // ------------------------------------------------------------------ shared state
        private sealed class Context
        {
            public readonly List<string> Problems;
            public readonly List<string> MasterChildren;
            public readonly List<Material> Palettes;
            private readonly HashSet<string> masterBoneNames;
            private readonly Dictionary<string, Vector3> boneEuler = new();

            public Context(GameObject master, List<string> problems)
            {
                Problems = problems;
                MasterChildren = master.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.name).Distinct().ToList();
                masterBoneNames = new HashSet<string>(master.GetComponentsInChildren<Transform>(true).Select(t => t.name));
                Palettes = AssetDatabase.FindAssets("t:Material", new[] { Alts }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !p.Contains("/Emissive/") && !p.Contains("/Snow/")).OrderBy(p => p).Select(AssetDatabase.LoadAssetAtPath<Material>).ToList();
                if (Palettes.Count != 15) problems.Add($"expected 15 palette materials in {Alts}, found {Palettes.Count}");

                // bone rest rotations, with the master upright at the origin
                var contents = PrefabUtility.LoadPrefabContents(MasterPath);
                try
                {
                    contents.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    contents.transform.localScale = Vector3.one;
                    foreach (var bone in new[] { "Head", "Spine_03", "Hips" })
                    {
                        var t = contents.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == bone);
                        if (t == null) problems.Add("the master has no bone " + bone); else boneEuler[bone] = Quaternion.Inverse(t.rotation).eulerAngles;
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }

            public ModelAppearanceOption Toggle(string slot, string child, string stripPrefix, string titlePrefix = "")
            {
                if (!MasterChildren.Contains(child)) Problems.Add($"the master has no child '{child}'");
                var rest = child.StartsWith(stripPrefix) ? child.Substring(stripPrefix.Length) : child;
                return new ModelAppearanceOption { id = Kebab(slot, titlePrefix, rest), title = titlePrefix + Words(rest), kind = ModelAppearanceKind.ToggleChild, childName = child };
            }

            public ModelAppearanceOption Attach(string slot, string path, string pack)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var file = Path.GetFileNameWithoutExtension(path);
                var rest = file.Replace("SM_Gen_Chr_Attach_", "").Replace("SM_Chr_Attach_", "").Replace("_Rigged", "");
                var option = new ModelAppearanceOption { id = Kebab(slot, pack + " ", rest), title = pack + " " + Words(rest), prefab = prefab, bone = MountBone(rest) };
                if (prefab == null) { Problems.Add("missing " + path); return option; }

                var skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skinned.Length == 0) option.kind = ModelAppearanceKind.AttachStatic;
                else if (skinned.All(s => s.bones.All(b => b == null || masterBoneNames.Contains(b.name))))
                {
                    option.kind = ModelAppearanceKind.AttachRigged;
                    var active = skinned.Where(s => s.gameObject.activeSelf).ToList();
                    if (active.Count != 1) Problems.Add($"{path}: expected exactly one active renderer, found {active.Count}");
                    else option.childName = active[0].name;
                }
                else option.kind = ModelAppearanceKind.AttachOwnRig;

                if (option.kind != ModelAppearanceKind.AttachRigged)
                {
                    if (!boneEuler.TryGetValue(option.bone, out var euler)) { Problems.Add($"{path}: no rest pose for bone {option.bone}"); }
                    else { option.localPosition = Vector3.zero; option.localEuler = euler; }
                }

                return option;
            }

            private static string MountBone(string name)
            {
                var n = name.ToLowerInvariant();
                if (n.Contains("cape") || n.Contains("backpack") || n.Contains("shawl") || n.Contains("collar")) return "Spine_03";
                if (n.Contains("pouch") || n.Contains("bags") || n.Contains("bottle")) return "Hips";
                return "Head";
            }

            private static string Kebab(string slot, string prefix, string rest) => (slot + "-" + prefix + rest).ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            private static string Words(string rest) => rest.Replace('_', ' ');
        }
    }
}
