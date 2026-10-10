using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Measures how far a worn piece sinks into the bare body at a build (the fit's acceptance number, shared by the tests and the probe) and writes <c>VikingRaid_Logs/spikes/fit_clearance.txt</c>.</summary>
    public static class PieceClearance
    {
        public static readonly (string name, Dictionary<string, float> weights)[] Builds =
        {
            ("default", new Dictionary<string, float>()),
            ("buff", new Dictionary<string, float> { ["defaultBuff"] = 100f }),
            ("heavy", new Dictionary<string, float> { ["defaultHeavy"] = 100f }),
            ("skinny", new Dictionary<string, float> { ["defaultSkinny"] = 100f }),
        };

        /// <summary>The vertices of a fitted (skinned, blend-shaped) piece mesh at rest with the build's blend shape weights applied (rig frame).</summary>
        public static Vector3[] FittedVertices(Mesh mesh, IReadOnlyDictionary<string, float> weights)
        {
            var vertices = mesh.vertices;
            foreach (var pair in weights)
            {
                var index = ModelAppearanceApplier.BlendShapeIndex(mesh, pair.Key);
                if (index < 0) continue;
                var delta = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(index, mesh.GetBlendShapeFrameCount(index) - 1, delta, null, null);
                for (var i = 0; i < vertices.Length; i++) vertices[i] += delta[i] * (pair.Value / 100f);
            }

            return vertices;
        }

        /// <summary>The vertices of a rigid static piece worn at rest: upright (the author's mount rule), scaled, at the bone's rest position plus the build's per-build offset (given in WORLD axes here).</summary>
        public static Vector3[] StaticVertices(GameObject prefab, Vector3 boneRestPosition, float scale, Vector3 worldOffset)
        {
            var result = new List<Vector3>();
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;   // piece space
                result.AddRange(filter.sharedMesh.vertices.Select(v => boneRestPosition + scale * (Vector3)matrix.MultiplyPoint3x4(v) + worldOffset));
            }

            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))   // a cape: its mesh skinned at the rest pose of its own skeleton (the mesh bounds alone say nothing about where it hangs)
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var vertices = mesh.vertices; var weights = mesh.boneWeights; var bindposes = mesh.bindposes; var bones = smr.bones;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var w = weights.Length == vertices.Length ? weights[i] : default;
                    Vector3 Part(int bone, float weight) => weight <= 0f || bone >= bones.Length || bones[bone] == null ? Vector3.zero : weight * (bones[bone].localToWorldMatrix * bindposes[bone]).MultiplyPoint3x4(vertices[i]);
                    var skinned = Part(w.boneIndex0, w.weight0) + Part(w.boneIndex1, w.weight1) + Part(w.boneIndex2, w.weight2) + Part(w.boneIndex3, w.weight3);
                    if (w.weight0 + w.weight1 + w.weight2 + w.weight3 <= 0f) skinned = smr.transform.localToWorldMatrix.MultiplyPoint3x4(vertices[i]);
                    result.Add(boneRestPosition + scale * (Vector3)prefab.transform.worldToLocalMatrix.MultiplyPoint3x4(skinned) + worldOffset);
                }
            }

            return result.ToArray();
        }

        /// <summary>At most about 600 vertices, evenly strided (a bag has thousands; the measure needs the shape, not every vertex).</summary>
        private static IEnumerable<Vector3> Sample(IEnumerable<Vector3> vertices)
        {
            var all = vertices as IList<Vector3> ?? vertices.ToList();
            var stride = Mathf.Max(1, all.Count / 600);
            for (var i = 0; i < all.Count; i += stride) yield return all[i];
        }

        /// <summary>The deepest vertex inside the body: how deep (metres, 0 when none is inside) and the outward normal of the face it sank through.</summary>
        public static float Deepest(BodySurface body, IEnumerable<Vector3> vertices, out Vector3 outward)
        {
            var worst = 0f; outward = Vector3.zero;
            foreach (var v in Sample(vertices))
            {
                var hit = body.Closest(v);
                if (hit.Signed < 0f && -hit.Signed > worst) { worst = -hit.Signed; outward = body.Normal(hit.Triangle); }
            }

            return worst;
        }

        /// <summary>The deepest a vertex sits inside the (deformed) body, in metres (0 when none does).</summary>
        public static float Penetration(BodySurface body, IEnumerable<Vector3> vertices)
        {
            var worst = 0f;
            foreach (var v in Sample(vertices))
            {
                var hit = body.Closest(v);
                if (hit.Signed < 0f) worst = Mathf.Max(worst, -hit.Signed);
            }

            return worst;
        }

        public static void Probe()
        {
            var body = BodySurface.Load(out var problem);
            var report = new StringBuilder();
            if (body == null) { report.AppendLine(problem); }
            else
            {
                var catalog = HumanCatalogAuthor.LoadOrBuild();
                report.AppendLine("deepest vertex inside the bare body (cm) per build: default / buff / heavy / skinny");
                foreach (var slot in catalog.Slots)
                    foreach (var option in slot.options)
                    {
                        float[] depth;
                        if (option.kind == ModelAppearanceKind.AttachPartsOwnMaterial)
                        {
                            var mesh = option.parts[0].GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
                            depth = Builds.Select(b => 100f * Penetration(body.Deformed(b.weights), FittedVertices(mesh, b.weights))).ToArray();
                            report.AppendLine($"{option.id,-34} fitted shapes {mesh.blendShapeCount}  " + string.Join(" / ", depth.Select(d => $"{d:F1}")));
                        }
                        if ((option.kind == ModelAppearanceKind.AttachOwnRig || option.kind == ModelAppearanceKind.AttachStatic) && option.buildOffsets.Count > 0)
                            report.AppendLine($"   {option.id}: offsets (bone-local, cm) " + string.Join("  ", option.buildOffsets.Select(b => $"{b.shape} ({b.offset.x * 100f:F1},{b.offset.y * 100f:F1},{b.offset.z * 100f:F1})")));
                        if (option.kind == ModelAppearanceKind.AttachOwnRig && option.prefab != null)
                        {
                            var smr = option.prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                            report.AppendLine($"   cape {option.id}: smr {smr?.name} bounds centre {smr?.sharedMesh.bounds.center:F3} size {smr?.sharedMesh.bounds.size:F3} prefab scale {option.prefab.transform.localScale} smr local {smr?.transform.localPosition:F3} rot {smr?.transform.localRotation.eulerAngles:F0} scale {smr?.transform.localScale:F3}");
                        }

                        if (option.kind == ModelAppearanceKind.AttachStatic && option.prefab != null && option.bone != "head")
                        {
                            var master = AssetDatabase.LoadAssetAtPath<GameObject>(HumanCatalogAuthor.MasterPath);
                            var bone = master.GetComponentsInChildren<Transform>(true).First(t => t.name == option.bone);
                            var verts = StaticVertices(option.prefab, bone.position, option.localScale.x, Vector3.zero);
                            depth = Builds.Select(b => 100f * Penetration(body.Deformed(b.weights), verts)).ToArray();
                            report.AppendLine($"{option.id,-34} rigid on {option.bone,-9}  " + string.Join(" / ", depth.Select(d => $"{d:F1}")));
                        }
                    }
            }

            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes", "fit_clearance.txt")), report.ToString());
            Debug.Log("[PieceClearance] done");
        }
    }
}
