using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using UnityEngine;

namespace VikingRaid.Appearance
{
    /// <summary>Marks an object the applier created for a slot (a mesh, an attachment, or a part's own dynamic bone chain), so the slot can be replaced as a unit and the creator can find it.</summary>
    public sealed class ModelAppearancePart : MonoBehaviour
    {
        public string SlotId;
        public string OptionId;
    }

    /// <summary>
    /// Dresses a master-rig character from (slot, option) choices and a <see cref="ModelAppearanceCatalog"/>. Pure Unity (no session), so the runtime binder, the editor preview and the tests share
    /// one implementation. The rules:
    /// <list type="bullet">
    /// <item><b>Total and idempotent.</b> Applying the same choices twice, or applying a different set after the first, gives exactly what the second set describes. An unknown slot id is ignored; an unknown
    /// option id is logged and degrades to the slot's default (REQUIRED slots, so a character is never invisible) or to nothing (optional slots). It never throws for content problems.</item>
    /// <item><b>Nothing is destroyed that Refresh may need.</b> The master's bodies, hair and furs are only deactivated. Objects the applier created are removed when their slot changes (unparented and
    /// renamed first, so a second Apply in the same frame, before a deferred Destroy lands, does not see them). A slot that already shows exactly the chosen option is left alone (no re-clone).</item>
    /// <item><b>Local poses only.</b> An attachment gets the local pose stored in the catalog under its bone; the applier never keeps a world pose (the character is animated by the time this runs).</item>
    /// <item>Clones lose their colliders and Animators (a cape or hood prefab carries an Animator with an avatar, and <c>ModelYawCorrection</c> takes the first one it finds) and take the root's layer.</item>
    /// <item><b>Part sets (Sidekick).</b> Every part is a skinned mesh whose bones are bound to the master's skeleton BY NAME, except the part's own <i>dynamic chain</i> bones (<c>hair_dyn_01_01_c</c>, ...):
    /// their names are generic (shared by many parts, and the master prefab itself may carry some), so they are never looked up; each part brings its own chain, which is moved under the master bone that
    /// was its parent in the part's hierarchy with its WORLD pose kept (= the bind pose, even where the master's own bone rests elsewhere) and marked as a part, so it is removed with the part and never seen by
    /// the next Apply. A weighted bone the master rests differently from the part's bind pose (outfit sets move their attachment sockets by up to 37 cm) gets a part-owned bind bone under it, at the bind pose.</item>
    /// </list>
    /// </summary>
    public static class ModelAppearanceApplier
    {
        /// <summary>A bone name that belongs to one part's own dynamic chain (never matched by name across parts).</summary>
        public static bool IsDynamic(string boneName) => boneName != null && boneName.Contains("_dyn_");

        /// <param name="root">The character: the object that holds the master model somewhere below it (CharacterRoot, or the master instance itself).</param>
        /// <param name="immediate">Edit mode (the preview, the tests): remove replaced parts with DestroyImmediate instead of the deferred Destroy.</param>
        public static void Apply(GameObject root, ModelAppearanceCatalog catalog, IReadOnlyDictionary<DefinitionId, DefinitionId> choices, bool immediate = false)
        {
            if (root == null || catalog == null) return;

            ModelAppearanceOption palette = null, build = null;
            var tints = new List<ModelAppearanceOption>();
            foreach (var slot in catalog.Slots)
            {
                var option = Resolve(slot, choices);
                ToggleMasterChildren(root, slot, option);

                var existing = PartsOf(root, slot.id);
                var keep = option != null && IsAttachKind(option.kind) && existing.Count > 0 && existing.All(p => p.OptionId == option.id);
                if (!keep)
                {
                    RemoveParts(existing, immediate);
                    if (option != null)
                        switch (option.kind)
                        {
                            case ModelAppearanceKind.AttachStatic:
                            case ModelAppearanceKind.AttachOwnRig: AttachWhole(root, slot, option); break;
                            case ModelAppearanceKind.AttachRigged: AttachRigged(root, slot, option); break;
                            case ModelAppearanceKind.AttachParts:
                            case ModelAppearanceKind.AttachPartsOwnMaterial: AttachPartSet(root, catalog, slot, option); break;
                        }
                }

                if (option == null) continue;
                if (option.kind == ModelAppearanceKind.Palette) palette = option;
                else if (option.kind == ModelAppearanceKind.BlendShapes) build = option;
                else if (option.kind == ModelAppearanceKind.ColourCells) tints.Add(option);
            }

            // runs on every Apply whatever the slots kept, so changing only a colour row repaints
            if (palette != null) ApplyPalette(root, catalog, ModelAppearanceColours.Resolve(palette.material, tints));
            ApplyBlendShapes(root, catalog, build);
            ApplyBuildOffsets(root, catalog, build);
        }

        private static bool IsAttachKind(ModelAppearanceKind kind) =>
            kind == ModelAppearanceKind.AttachStatic || kind == ModelAppearanceKind.AttachOwnRig || kind == ModelAppearanceKind.AttachRigged || kind == ModelAppearanceKind.AttachParts || kind == ModelAppearanceKind.AttachPartsOwnMaterial;

        /// <summary>The option a slot shows for these choices (null means "None").</summary>
        public static ModelAppearanceOption Resolve(ModelAppearanceSlot slot, IReadOnlyDictionary<DefinitionId, DefinitionId> choices)
        {
            string wanted = null;
            if (choices != null && choices.TryGetValue(new DefinitionId(slot.id), out var chosen)) wanted = chosen.Value;

            var option = slot.Find(wanted);
            if (option != null) return option;

            if (!string.IsNullOrEmpty(wanted)) Debug.LogWarning($"[ModelAppearance] slot '{slot.id}' has no option '{wanted}'" + (slot.required ? "; using its default." : "; showing nothing."));
            return slot.required ? slot.Find(slot.defaultOptionId) ?? slot.options.FirstOrDefault() : null;
        }

        // ------------------------------------------------------------------ master children
        private static void ToggleMasterChildren(GameObject root, ModelAppearanceSlot slot, ModelAppearanceOption chosen)
        {
            var toggles = slot.options.Where(o => o.kind == ModelAppearanceKind.ToggleChild).ToList();
            if (toggles.Count == 0) return;

            var byName = MasterMeshes(root).GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());
            foreach (var option in toggles)
            {
                if (byName.TryGetValue(option.childName, out var smr)) smr.gameObject.SetActive(chosen != null && chosen.id == option.id);
                else if (chosen != null && chosen.id == option.id) Debug.LogWarning($"[ModelAppearance] the master has no child '{option.childName}' (slot '{slot.id}', option '{option.id}').");
            }
        }

        /// <summary>The master's own skinned meshes (not parts this applier created).</summary>
        private static IEnumerable<SkinnedMeshRenderer> MasterMeshes(GameObject root) =>
            root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) == null);

        private static Transform FindBone(GameObject root, string boneName) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == boneName && t.GetComponentInParent<ModelAppearancePart>(true) == null && t.GetComponent<SkinnedMeshRenderer>() == null);

        /// <summary>The master's skeleton by name: not parts' own objects, not dynamic-chain bones.</summary>
        private static Dictionary<string, Transform> MasterBones(GameObject root) =>
            root.GetComponentsInChildren<Transform>(true).Where(t => !IsDynamic(t.name) && t.GetComponentInParent<ModelAppearancePart>(true) == null)
                .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

        /// <summary>Where a part's renderers are parented: the transform that carries the master's Animator (the model root).</summary>
        private static Transform FindRigRoot(GameObject root)
        {
            var animator = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.GetComponentInParent<ModelAppearancePart>(true) == null);
            return animator != null ? animator.transform : root.transform;
        }

        // ------------------------------------------------------------------ attachments
        private static List<ModelAppearancePart> PartsOf(GameObject root, string slotId) =>
            root.GetComponentsInChildren<ModelAppearancePart>(true).Where(p => p.SlotId == slotId).ToList();

        private static void RemoveParts(List<ModelAppearancePart> parts, bool immediate)
        {
            foreach (var part in parts)
            {
                if (part == null) continue;   // a chain inside an earlier removed part
                var go = part.gameObject;
                go.name = "Appearance_removed";
                go.SetActive(false);
                go.transform.SetParent(null, false);
                if (immediate) Object.DestroyImmediate(go); else Object.Destroy(go);
            }
        }

        private static GameObject Clone(GameObject prefab, int layer)
        {
            var clone = Object.Instantiate(prefab);
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in clone.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            return clone;
        }

        private static ModelAppearancePart Mark(GameObject go, ModelAppearanceSlot slot, ModelAppearanceOption option, bool rename = true)
        {
            if (rename) go.name = "Appearance_" + slot.id;
            var part = go.AddComponent<ModelAppearancePart>();
            part.SlotId = slot.id; part.OptionId = option.id;
            return part;
        }

        private static void AttachWhole(GameObject root, ModelAppearanceSlot slot, ModelAppearanceOption option)
        {
            if (option.prefab == null) { Debug.LogWarning($"[ModelAppearance] option '{option.id}' has no prefab."); return; }
            var bone = FindBone(root, option.bone);
            if (bone == null) { Debug.LogWarning($"[ModelAppearance] the rig has no bone '{option.bone}' for option '{option.id}'."); return; }

            var clone = Clone(option.prefab, root.layer);
            clone.transform.SetParent(bone, false);
            clone.transform.localPosition = option.localPosition;
            clone.transform.localRotation = Quaternion.Euler(option.localEuler);
            clone.transform.localScale = option.localScale == Vector3.zero ? Vector3.one : option.localScale;
            Mark(clone, slot, option);
        }

        /// <summary>Takes ONE renderer out of a prefab that is a whole character and re-skins it onto the master skeleton. Bones are remapped by ORDER (index i stays bone i: the bind poses depend on it), and
        /// the whole binding is rejected if any bone is missing on the master (a half-bound mesh is worse than none).</summary>
        private static void AttachRigged(GameObject root, ModelAppearanceSlot slot, ModelAppearanceOption option)
        {
            if (option.prefab == null) { Debug.LogWarning($"[ModelAppearance] option '{option.id}' has no prefab."); return; }
            var master = MasterMeshes(root).FirstOrDefault();
            if (master == null) { Debug.LogWarning("[ModelAppearance] the character has no master mesh to attach to."); return; }

            var bonesByName = root.GetComponentsInChildren<Transform>(true).Where(t => t.GetComponentInParent<ModelAppearancePart>(true) == null)
                .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var clone = Clone(option.prefab, root.layer);
            var wanted = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => string.IsNullOrEmpty(option.childName) ? s.gameObject.activeInHierarchy : s.name == option.childName).ToList();
            if (wanted.Count == 0) Debug.LogWarning($"[ModelAppearance] option '{option.id}': the prefab has no renderer '{option.childName}'.");

            foreach (var smr in wanted)
            {
                var source = smr.bones;
                var mapped = new Transform[source.Length];
                var missing = false;
                for (var i = 0; i < source.Length; i++)
                {
                    if (source[i] == null) continue;
                    if (bonesByName.TryGetValue(source[i].name, out var target)) mapped[i] = target; else missing = true;
                }

                if (missing) { Debug.LogWarning($"[ModelAppearance] option '{option.id}': the master skeleton lacks a bone this mesh needs; not attached."); continue; }

                smr.bones = mapped;
                smr.rootBone = smr.rootBone != null && bonesByName.TryGetValue(smr.rootBone.name, out var rootBone) ? rootBone : master.rootBone;
                smr.transform.SetParent(master.transform.parent, false);
                smr.transform.localPosition = Vector3.zero; smr.transform.localRotation = Quaternion.identity; smr.transform.localScale = Vector3.one;
                smr.gameObject.SetActive(true);
                Mark(smr.gameObject, slot, option);
            }

            Object.DestroyImmediate(clone);   // what is left: the other bodies, a second skeleton
        }

        /// <summary>Binds every renderer of every part prefab of the set to the master skeleton by bone name (see the class remarks for the dynamic chains and the bind bones).</summary>
        private static void AttachPartSet(GameObject root, ModelAppearanceCatalog catalog, ModelAppearanceSlot slot, ModelAppearanceOption option)
        {
            if (option.parts == null || option.parts.Count == 0) { Debug.LogWarning($"[ModelAppearance] option '{option.id}' has no parts."); return; }
            var rig = FindRigRoot(root);
            var bones = MasterBones(root);
            var rest = new RestPose(catalog.MasterPrefab, rig, bones);
            foreach (var prefab in option.parts)
            {
                if (prefab == null) { Debug.LogWarning($"[ModelAppearance] option '{option.id}' has an empty part."); continue; }
                var clone = Clone(prefab, root.layer);
                // the clone stands where the rig stands, so the world poses of its own bones are rig-consistent when a chain is moved with its world pose kept
                clone.transform.SetPositionAndRotation(rig.position, rig.rotation);
                foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList())
                {
                    BindPart(smr, rig, bones, rest, slot, option);
                    // a refitted Polygon piece keeps its own atlas material (the palette step never repaints this kind)
                    if (option.kind == ModelAppearanceKind.AttachPartsOwnMaterial && option.material != null && smr.sharedMesh != null)
                        smr.sharedMaterials = Enumerable.Repeat(option.material, Mathf.Max(1, smr.sharedMesh.subMeshCount)).ToArray();
                }
                Object.DestroyImmediate(clone);   // the rest of the part's own skeleton
            }
        }

        /// <summary>A weighted bone whose rest pose on the master differs from the part's bind pose by more than this gets a bind bone (metres; rotation matrix element).</summary>
        private const float BindTolerance = 0.004f, BindRotationTolerance = 0.02f;

        private static void BindPart(SkinnedMeshRenderer smr, Transform rig, Dictionary<string, Transform> bones, RestPose rest, ModelAppearanceSlot slot, ModelAppearanceOption option)
        {
            var source = smr.bones;
            var bindposes = smr.sharedMesh.bindposes;
            var weighted = new HashSet<int>(smr.sharedMesh.GetAllBoneWeights().Where(w => w.weight > 0.001f).Select(w => w.boneIndex));

            // the renderer first: at rest a vertex stays where bone-to-world times bind pose puts it, which must equal the renderer's own matrix
            smr.transform.SetParent(rig, false);
            smr.transform.localPosition = Vector3.zero; smr.transform.localRotation = Quaternion.identity; smr.transform.localScale = Vector3.one;
            var rendererToWorld = smr.transform.localToWorldMatrix;

            var mapped = new Transform[source.Length];
            var chains = new Dictionary<Transform, string>();   // the top-most part-own transform of each chain -> the master bone it hangs under
            var bindBones = new List<GameObject>();
            for (var i = 0; i < source.Length; i++)
            {
                var bone = source[i];
                if (bone == null) continue;
                if (!IsDynamic(bone.name) && bones.TryGetValue(bone.name, out var target))
                {
                    var restWorld = rest.World(target);
                    if (!weighted.Contains(i) || RestMatchesBind(restWorld, bindposes[i], rendererToWorld)) mapped[i] = target;
                    else
                    {
                        // the master's rest pose of this bone differs from the one the part was authored against (outfit sets move their attachment sockets): a bind bone under the
                        // master bone sits at exactly the bind pose and follows the master bone's animation from there
                        var bind = new GameObject(target.name + "_bind");
                        bind.layer = rig.gameObject.layer;
                        bind.transform.SetParent(target, false);
                        // the pose is expressed relative to the master bone's REST pose, so it is right whatever pose the skeleton is in right now
                        var local = restWorld.inverse * (rendererToWorld * bindposes[i].inverse);
                        bind.transform.localPosition = local.GetPosition(); bind.transform.localRotation = local.rotation; bind.transform.localScale = Vector3.one;
                        mapped[i] = bind.transform;
                        bindBones.Add(bind);
                    }

                    continue;
                }

                // the part's own bone (a dynamic chain, or any bone the master lacks): climb to the first ancestor the master has
                Transform top = bone, anchor = null;
                for (var t = bone; t != null; t = t.parent)
                {
                    if (!IsDynamic(t.name) && bones.ContainsKey(t.name)) { anchor = t; break; }
                    top = t;
                }

                if (anchor == null)
                {
                    foreach (var made in bindBones) Object.DestroyImmediate(made);
                    smr.transform.SetParent(null, false);
                    Debug.LogWarning($"[ModelAppearance] option '{option.id}': bone '{bone.name}' of '{smr.name}' has no ancestor on the master skeleton; not attached.");
                    return;
                }

                mapped[i] = bone;
                chains[top] = anchor.name;
            }

            foreach (var made in bindBones) Mark(made, slot, option, rename: false);
            foreach (var chain in chains)
            {
                var authored = chain.Key.localToWorldMatrix;   // the clone stands at the rig's pose and at rest: this is the part's bind pose
                var anchorRest = rest.World(bones[chain.Value]);
                chain.Key.SetParent(bones[chain.Value], false);
                var chainLocal = anchorRest.inverse * authored;   // relative to the anchor's REST pose, so right whatever pose the skeleton is in right now
                chain.Key.localPosition = chainLocal.GetPosition(); chain.Key.localRotation = chainLocal.rotation; chain.Key.localScale = chainLocal.lossyScale;
                Mark(chain.Key.gameObject, slot, option, rename: false);
            }

            smr.bones = mapped;
            smr.rootBone = smr.rootBone != null && !IsDynamic(smr.rootBone.name) && bones.TryGetValue(smr.rootBone.name, out var rootBone) ? rootBone : (bones.TryGetValue("root", out var r) ? r : rig);
            smr.updateWhenOffscreen = true;   // blend shapes are not in the mesh bounds
            smr.gameObject.SetActive(true);
            Mark(smr.gameObject, slot, option);
        }

        /// <summary>True when the master bone's rest pose already reproduces the part's bind pose (so the bone can be used as it is).</summary>
        private static bool RestMatchesBind(Matrix4x4 boneRestWorld, Matrix4x4 bindpose, Matrix4x4 rendererToWorld)
        {
            var skin = boneRestWorld * bindpose;
            if (new Vector3(skin.m03 - rendererToWorld.m03, skin.m13 - rendererToWorld.m13, skin.m23 - rendererToWorld.m23).magnitude > BindTolerance) return false;
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    if (Mathf.Abs(skin[row, column] - rendererToWorld[row, column]) > BindRotationTolerance) return false;
            return true;
        }

        /// <summary>The master skeleton's REST pose, read from the master PREFAB ASSET (never from the live transforms, which an Animator or a sampled clip may have posed): bone name to its matrix relative to
        /// the master root, placed in the world by the live rig's current matrix. Falls back to the live pose only for a bone the asset lacks.</summary>
        private sealed class RestPose
        {
            private static readonly Dictionary<GameObject, Dictionary<string, Matrix4x4>> Cache = new();
            private readonly Dictionary<string, Matrix4x4> relative;
            private readonly Matrix4x4 rigToWorld;

            public RestPose(GameObject masterPrefab, Transform rig, Dictionary<string, Transform> bones)
            {
                rigToWorld = rig.localToWorldMatrix;
                if (masterPrefab == null) { relative = new Dictionary<string, Matrix4x4>(); return; }
                if (!Cache.TryGetValue(masterPrefab, out relative) || relative == null)
                {
                    var asset = masterPrefab.transform;
                    var animator = masterPrefab.GetComponentInChildren<Animator>(true);
                    var origin = animator != null ? animator.transform : asset;   // the rig root, as FindRigRoot picks it on the live character
                    relative = new Dictionary<string, Matrix4x4>();
                    foreach (var t in masterPrefab.GetComponentsInChildren<Transform>(true))
                        if (!relative.ContainsKey(t.name)) relative[t.name] = origin.worldToLocalMatrix * t.localToWorldMatrix;
                    Cache[masterPrefab] = relative;
                }
            }

            public Matrix4x4 World(Transform bone) => relative.TryGetValue(bone.name, out var matrix) ? rigToWorld * matrix : bone.localToWorldMatrix;
        }

        // ------------------------------------------------------------------ palette and blend shapes
        private static void ApplyPalette(GameObject root, ModelAppearanceCatalog catalog, Material palette)
        {
            if (palette == null) return;
            if (catalog.PaletteScope == ModelPaletteScope.AppearanceParts)
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var part = renderer.GetComponentInParent<ModelAppearancePart>(true);
                    if (part == null) continue;
                    // only PART SETS (Sidekick meshes whose own materials are placeholders) take the colour map; a rigid Polygon piece (a helmet, a cape) keeps its own material. Decided from the catalog at paint
                    // time, not from a stored flag, so a baked prefab keeps working.
                    var option = catalog.FindSlot(part.SlotId)?.Find(part.OptionId);
                    if (option == null || option.kind != ModelAppearanceKind.AttachParts) continue;
                    renderer.sharedMaterials = Enumerable.Repeat(palette, renderer.sharedMaterials.Length).ToArray();
                }

                return;
            }

            var family = catalog.PaletteFamily;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == palette || !family.Contains(materials[i])) continue;
                    materials[i] = palette; changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }
        }

        /// <summary>Moves every rigid piece that carries build offsets to where the chosen build puts it: <c>localPosition = option.localPosition + sum(weight / 100 * offset)</c>. Runs on every Apply, so a piece kept by the "slot already shows it" rule is repositioned too and a build back to the default puts it home.</summary>
        private static void ApplyBuildOffsets(GameObject root, ModelAppearanceCatalog catalog, ModelAppearanceOption build)
        {
            foreach (var part in root.GetComponentsInChildren<ModelAppearancePart>(true))
            {
                var option = catalog.FindSlot(part.SlotId)?.Find(part.OptionId);
                if (option == null || option.buildOffsets == null || option.buildOffsets.Count == 0) continue;
                if (option.kind != ModelAppearanceKind.AttachStatic && option.kind != ModelAppearanceKind.AttachOwnRig) continue;
                if (part.transform.parent == null || part.name != "Appearance_" + part.SlotId) continue;   // the piece's root, not a nested mark
                var offset = Vector3.zero;
                if (build?.blendShapes != null)
                    foreach (var weight in build.blendShapes)
                        foreach (var entry in option.buildOffsets)
                            if (entry.shape == weight.shape) offset += entry.offset * (weight.weight / 100f);
                part.transform.localPosition = option.localPosition + offset;
            }
        }

        /// <summary>The index of a blend shape by the name after its region prefix (<c>TORSBlends.defaultBuff</c> matches <c>defaultBuff</c>), or -1.</summary>
        public static int BlendShapeIndex(Mesh mesh, string shape)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                if (name == shape || name.EndsWith("." + shape)) return i;
            }

            return -1;
        }

        /// <summary>Resets every blend shape the catalog names to 0 on every skinned renderer, then sets the chosen build's weights, each only where the mesh has the shape.</summary>
        private static void ApplyBlendShapes(GameObject root, ModelAppearanceCatalog catalog, ModelAppearanceOption chosen)
        {
            var named = catalog.Slots.SelectMany(s => s.options).Where(o => o.kind == ModelAppearanceKind.BlendShapes && o.blendShapes != null)
                .SelectMany(o => o.blendShapes).Select(b => b.shape).Distinct().ToList();
            if (named.Count == 0) return;

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                foreach (var shape in named)
                {
                    var index = BlendShapeIndex(mesh, shape);
                    if (index >= 0) smr.SetBlendShapeWeight(index, 0f);
                }

                if (chosen?.blendShapes == null) continue;
                foreach (var weight in chosen.blendShapes)
                {
                    var index = BlendShapeIndex(mesh, weight.shape);
                    if (index >= 0) smr.SetBlendShapeWeight(index, weight.weight);
                }
            }
        }
    }
}
