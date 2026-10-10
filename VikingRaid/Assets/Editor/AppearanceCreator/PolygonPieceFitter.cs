using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>
    /// Turns a SKINNED Polygon Viking piece (a hair, a pelt, a fur: children of the Viking master prefab, weighted to the Polygon rig) into a skinned PART of the Sidekick human master skeleton
    /// (design <c>Docs/Plans/VikingRaid-HumanAppearance.md</c> Section 12). The Sidekick skeleton is NOT the Polygon skeleton scaled: its spine is about 20 cm lower relative to the neck, so blending per-bone
    /// translations would shear a piece into the body. Instead the piece is placed RIGIDLY about ONE anchor (the Head for hairs and pelts, exactly where a static Polygon piece sits under the
    /// <c>head</c> bone; the shoulder midpoint for furs) and only its WEIGHTS are remapped by bone name, so it still follows the spine with its authored share when the character animates. Rigid placement
    /// and a uniform scale keep the normals and tangents exact. The bind poses are the master's own REST pose (read from the master prefab ASSET, whose root is at identity), so <c>BindPart</c> finds no
    /// mismatch and creates no bind bones. Output (ignored by git: derived Synty geometry): a Mesh asset and a part prefab (the master hierarchy plus one skinned renderer) under <see cref="Folder"/>;
    /// existing files are overwritten IN PLACE so their GUIDs survive a rebuild.
    /// </summary>
    public static class PolygonPieceFitter
    {
        public const string Folder = "Assets/Appearance/SidekickHumans/Pieces";

        public enum Anchor { Head, Shoulders }

        private static readonly Dictionary<string, string> BoneMap = new()
        {
            ["Root"] = "root", ["Hips"] = "pelvis", ["Spine_01"] = "spine_01", ["Spine_02"] = "spine_02", ["Spine_03"] = "spine_03", ["Neck"] = "neck_01", ["Head"] = "head", ["Jaw"] = "jaw",
            ["Eyes"] = "head", ["Eyebrows"] = "head",
            ["Clavicle_L"] = "clavicle_l", ["Shoulder_L"] = "upperarm_l", ["Elbow_L"] = "lowerarm_l", ["Hand_L"] = "hand_l",
            ["Clavicle_R"] = "clavicle_r", ["Shoulder_R"] = "upperarm_r", ["Elbow_R"] = "lowerarm_r", ["Hand_R"] = "hand_r",
            ["UpperLeg_L"] = "thigh_l", ["LowerLeg_L"] = "calf_l", ["Ankle_L"] = "foot_l", ["Ball_L"] = "ball_l",
            ["UpperLeg_R"] = "thigh_r", ["LowerLeg_R"] = "calf_r", ["Ankle_R"] = "foot_r", ["Ball_R"] = "ball_r",
        };

        /// <summary>The Sidekick bone a Polygon bone's weight goes to: its own mapped name, else that of the nearest mapped ancestor (a finger goes to the hand), else null.</summary>
        public static string TargetBone(Transform polygonBone)
        {
            for (var t = polygonBone; t != null; t = t.parent)
                if (BoneMap.TryGetValue(t.name, out var target)) return target;
            return null;
        }

        private static BodySurface body;

        /// <summary>The bare body the fit samples (loaded once; call <see cref="ResetBody"/> when the Synty meshes may have been reimported).</summary>
        public static BodySurface Body(out string problem)
        {
            problem = null;
            return body ??= BodySurface.Load(out problem);
        }

        public static void ResetBody() => body = null;

        /// <summary>Faded fit distance: a vertex this close to the body (metres) follows the body's build fully; beyond <see cref="FitFar"/> it does not follow at all.</summary>
        public const float FitNear = 0.05f, FitFar = 0.15f;

        /// <summary>How fully a vertex this far from the body surface (metres) follows the body's build: 1 up to <see cref="FitNear"/>, 0 from <see cref="FitFar"/>, linear between.</summary>
        public static float Fade(float distance) => Mathf.Clamp01(1f - (distance - FitNear) / (FitFar - FitNear));

        /// <summary>A vertex weighted this much to the head follows the HEAD's own build delta (a wolf skull must not shear with the neck).</summary>
        public const float HeadDominant = 0.6f;

        /// <summary>A vertex may sink this much (metres) deeper than at the default build before it is pushed out, and a push is at most <see cref="MaxPush"/>.</summary>
        public const float SinkTolerance = 0.003f, MaxPush = 0.06f;

        /// <summary>The rigid mount formula: a source point about the source anchor lands about the target anchor, uniformly scaled.</summary>
        public static Vector3 Place(Vector3 point, Vector3 sourceAnchor, Vector3 targetAnchor, float scale) => targetAnchor + scale * (point - sourceAnchor);

        /// <summary>Fits the child <paramref name="sourceChild"/> of the Polygon master and returns its part prefab (and the piece's own material), or null with the reason added to <paramref name="problems"/>.</summary>
        public static GameObject Fit(string sourceChild, string outputName, Anchor anchor, float scale, out Material material, List<string> problems)
        {
            material = null;
            var polygon = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAppearanceCatalogAuthor.MasterPath);
            var human = AssetDatabase.LoadAssetAtPath<GameObject>(HumanCatalogAuthor.MasterPath);
            if (polygon == null || human == null) { problems.Add("the Polygon master or the human master is missing"); return null; }

            var source = polygon.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == sourceChild);   // case-sensitive: Hair_03_alt is lower case, the others _Alt
            if (source == null || source.sharedMesh == null) { problems.Add($"the Polygon master has no skinned piece '{sourceChild}'"); return null; }

            var mesh = source.sharedMesh;
            Vector3[] vertices;
            BoneWeight[] weights;
            try { vertices = mesh.vertices; weights = mesh.boneWeights; }
            catch (Exception e) { problems.Add($"cannot read {sourceChild}: {e.Message}"); return null; }
            if (vertices.Length == 0 || weights.Length != vertices.Length) { problems.Add($"{sourceChild}: no vertices or no bone weights (readable: {mesh.isReadable})"); return null; }

            var humanBones = human.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var sourceBones = source.bones;
            var bindposes = mesh.bindposes;
            var toWorld = source.transform.localToWorldMatrix;

            Vector3 SourceRest(string name)
            {
                var i = Array.FindIndex(sourceBones, b => b != null && b.name == name);
                if (i < 0) throw new InvalidOperationException($"{sourceChild} is not skinned to '{name}'");
                return (toWorld * bindposes[i].inverse).GetPosition();
            }

            Vector3 sourceAnchor, targetAnchor;
            try
            {
                if (anchor == Anchor.Head) { sourceAnchor = SourceRest("Head"); targetAnchor = humanBones["head"].position; }
                else
                {
                    sourceAnchor = (SourceRest("Shoulder_L") + SourceRest("Shoulder_R")) / 2f;
                    targetAnchor = (humanBones["upperarm_l"].position + humanBones["upperarm_r"].position) / 2f;
                }
            }
            catch (InvalidOperationException e) { problems.Add(e.Message); return null; }

            // geometry: rigid placement about the anchor, in the rig frame (the human master's root is at identity)
            var placed = new Vector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) placed[i] = Place(toWorld.MultiplyPoint3x4(vertices[i]), sourceAnchor, targetAnchor, scale);

            // weights: remapped by name, merged, the four largest kept and normalised
            var targetNames = new List<string>();
            int IndexOf(string name) { var i = targetNames.IndexOf(name); if (i < 0) { targetNames.Add(name); i = targetNames.Count - 1; } return i; }
            var remapped = new BoneWeight[weights.Length];
            for (var v = 0; v < weights.Length; v++)
            {
                var sums = new Dictionary<string, float>();
                void Add(int bone, float w)
                {
                    if (w <= 0f || bone < 0 || bone >= sourceBones.Length || sourceBones[bone] == null) return;
                    var target = TargetBone(sourceBones[bone]) ?? "head";
                    sums[target] = sums.TryGetValue(target, out var s) ? s + w : w;
                }

                var bw = weights[v];
                Add(bw.boneIndex0, bw.weight0); Add(bw.boneIndex1, bw.weight1); Add(bw.boneIndex2, bw.weight2); Add(bw.boneIndex3, bw.weight3);
                var top = sums.OrderByDescending(p => p.Value).Take(4).ToList();
                if (top.Count == 0) top.Add(new KeyValuePair<string, float>("head", 1f));
                var total = top.Sum(p => p.Value);
                var result = new BoneWeight();
                for (var k = 0; k < top.Count; k++)
                {
                    var index = IndexOf(top[k].Key); var w = top[k].Value / total;
                    switch (k)
                    {
                        case 0: result.boneIndex0 = index; result.weight0 = w; break;
                        case 1: result.boneIndex1 = index; result.weight1 = w; break;
                        case 2: result.boneIndex2 = index; result.weight2 = w; break;
                        default: result.boneIndex3 = index; result.weight3 = w; break;
                    }
                }

                remapped[v] = result;
            }

            var missing = targetNames.Where(n => !humanBones.ContainsKey(n)).ToList();
            if (missing.Count > 0) { problems.Add($"{sourceChild}: the human master has no bone(s) {string.Join(", ", missing)}"); return null; }

            Directory.CreateDirectory(Folder);
            // fill the EXISTING asset in place when there is one (its GUID survives a rebuild); CopySerialized onto a cleared mesh leaves it EMPTY, so the data is set through the Mesh API
            var meshPath = $"{Folder}/{outputName}.asset";
            var made = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var isNew = made == null;
            if (isNew) made = new Mesh(); else made.Clear();
            made.name = outputName; made.indexFormat = mesh.indexFormat;
            made.SetVertices(placed);
            made.SetNormals(mesh.normals);   // a rigid move with a uniform scale leaves the normals and tangents exact
            if (mesh.tangents.Length == vertices.Length) made.SetTangents(mesh.tangents);
            for (var channel = 0; channel < 4; channel++)
            {
                var uvs = new List<Vector2>();
                mesh.GetUVs(channel, uvs);
                if (uvs.Count == vertices.Length) made.SetUVs(channel, uvs);
            }

            if (mesh.colors32.Length == vertices.Length) made.colors32 = mesh.colors32;
            made.subMeshCount = mesh.subMeshCount;
            for (var s = 0; s < mesh.subMeshCount; s++) made.SetTriangles(mesh.GetTriangles(s), s);
            made.boneWeights = remapped;
            made.ClearBlendShapes();
            var surface = Body(out var bodyProblem);
            if (surface == null) { problems.Add("cannot fit to the builds: " + bodyProblem); return null; }
            AddBuildShapes(made, placed, remapped, targetNames.IndexOf("head"), surface);
            made.bindposes = targetNames.Select(n => humanBones[n].worldToLocalMatrix).ToArray();   // the master's REST pose: the renderer sits at the rig's identity
            made.RecalculateBounds();
            if (isNew) AssetDatabase.CreateAsset(made, meshPath); else EditorUtility.SetDirty(made);

            material = source.sharedMaterial;
            return SavePrefab(human, made, targetNames, material, $"{Folder}/{outputName}.prefab", outputName);
        }

        /// <summary>One blend shape per body build (same names, so the build row drives them): the delta at a vertex is the body's own delta at the closest point of the bare body's TRIANGLES (barycentric), faded
        /// to zero between <see cref="FitNear"/> and <see cref="FitFar"/> from the surface; a head-dominant vertex takes the mean head delta instead.</summary>
        private static void AddBuildShapes(Mesh target, Vector3[] placed, BoneWeight[] weights, int headIndex, BodySurface surface)
        {
            var hits = placed.Select(surface.Closest).ToArray();
            var headWeight = weights.Select(w => (w.boneIndex0 == headIndex ? w.weight0 : 0f) + (w.boneIndex1 == headIndex ? w.weight1 : 0f) + (w.boneIndex2 == headIndex ? w.weight2 : 0f) + (w.boneIndex3 == headIndex ? w.weight3 : 0f)).ToArray();
            foreach (var shape in BodySurface.BuildShapes)
            {
                var head = surface.MeanHeadDelta(shape);
                var deltas = new Vector3[placed.Length];
                for (var i = 0; i < placed.Length; i++)
                {
                    if (headIndex >= 0 && headWeight[i] >= HeadDominant) { deltas[i] = head; continue; }
                    deltas[i] = Fade(hits[i].Distance) * surface.DeltaAt(hits[i], shape);
                }

                // push-out passes: a vertex that would sink deeper into the DEFORMED body than it sits in the default body is pushed back out along the face it sank through (never in, never far)
                var deformed = surface.Deformed(new Dictionary<string, float> { [shape] = 100f });
                for (var pass = 0; pass < 2; pass++)
                    for (var i = 0; i < placed.Length; i++)
                    {
                        if (headIndex >= 0 && headWeight[i] >= HeadDominant) continue;
                        var hit = deformed.Closest(placed[i] + deltas[i]);
                        var before = -Mathf.Min(hits[i].Signed, 0f);   // how deep it sits at the default build (0 when outside)
                        var now = -Mathf.Min(hit.Signed, 0f);
                        var excess = now - before - SinkTolerance;
                        if (excess > 0f) deltas[i] += deformed.Normal(hit.Triangle) * Mathf.Min(excess, MaxPush);
                    }

                target.AddBlendShapeFrame(shape, 100f, deltas, null, null);
            }
        }

        private static GameObject SavePrefab(GameObject human, Mesh mesh, List<string> boneNames, Material material, string path, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(human);
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = name;
                var byName = instance.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                var holder = new GameObject(name);
                holder.transform.SetParent(instance.transform, false);
                var smr = holder.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = boneNames.Select(n => byName[n]).ToArray();
                smr.rootBone = byName.TryGetValue("pelvis", out var pelvis) ? pelvis : instance.transform;
                smr.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, mesh.subMeshCount)).ToArray();
                smr.updateWhenOffscreen = true;
                return PrefabUtility.SaveAsPrefabAsset(instance, path);   // overwrites in place: the GUID survives
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
    }
}
