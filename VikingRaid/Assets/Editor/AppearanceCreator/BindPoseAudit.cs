using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Diagnostic: for every part of a catalog, how far does each WEIGHTED bone of the master's rest pose sit from the part's bind pose (boneToWorld * bindpose vs the renderer's matrix, translation in metres)? Writes <c>VikingRaid_Logs/spikes/bindpose_audit_{family}.txt</c>.</summary>
    public static class BindPoseAudit
    {
        public static void RunGoblin() => Run(GoblinCatalogAuthor.LoadOrBuild());
        public static void RunViking() => Run(ModelAppearanceCatalogAuthor.LoadOrBuild());

        public static void Run(ModelAppearanceCatalog catalog)
        {
            var root = Object.Instantiate(catalog.MasterPrefab);
            var worst = new Dictionary<string, (float error, string part, int count)>();
            var report = new StringBuilder();
            try
            {
                foreach (var slot in catalog.Slots.Where(s => s.options.Any(o => o.kind == ModelAppearanceKind.AttachParts || o.kind == ModelAppearanceKind.AttachPartsOwnMaterial)))
                    foreach (var option in slot.options)
                    {
                        ModelAppearanceApplier.Apply(root, catalog, new Dictionary<CRPG.Core.DefinitionId, CRPG.Core.DefinitionId> { [new CRPG.Core.DefinitionId(slot.id)] = new CRPG.Core.DefinitionId(option.id) }, immediate: true);
                        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.GetComponentInParent<ModelAppearancePart>(true) != null && s.GetComponentInParent<ModelAppearancePart>(true).SlotId == slot.id))
                        {
                            var bindposes = smr.sharedMesh.bindposes;
                            var weighted = new HashSet<int>(smr.sharedMesh.GetAllBoneWeights().Where(w => w.weight > 0.001f).Select(w => w.boneIndex));
                            foreach (var i in weighted)
                            {
                                var bone = smr.bones[i]; if (bone == null) continue;
                                var skin = bone.localToWorldMatrix * bindposes[i];
                                var renderer = smr.transform.localToWorldMatrix;
                                var error = new Vector3(skin.m03 - renderer.m03, skin.m13 - renderer.m13, skin.m23 - renderer.m23).magnitude;
                                var rotation = 0f; for (var e = 0; e < 3; e++) for (var f = 0; f < 3; f++) rotation = Mathf.Max(rotation, Mathf.Abs(skin[e, f] - renderer[e, f]));
                                var key = bone.name;
                                if (!worst.TryGetValue(key, out var w) || error > w.error) worst[key] = (error, smr.sharedMesh.name, (w.count) + 1); else worst[key] = (w.error, w.part, w.count + 1);
                                if (rotation > 0.05f) report.AppendLine($"  ROTATION {rotation:F3} {bone.name} in {smr.sharedMesh.name}");
                            }
                        }
                    }

                report.Insert(0, string.Join("\n", worst.OrderByDescending(p => p.Value.error).Take(400).Select(p => $"{p.Value.error * 100f,7:F2} cm  {p.Key,-28} worst in {p.Value.part} ({p.Value.count} bindings)")) + "\n");
            }
            finally { Object.DestroyImmediate(root); }

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            File.WriteAllText(Path.Combine(outDir, $"bindpose_audit_{catalog.Family.ToLowerInvariant()}.txt"), report.ToString());
            Debug.Log("[BindPoseAudit] done");
        }
    }
}
