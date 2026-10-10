using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>
    /// The bare Sidekick human body as a triangle soup in the rig frame (every region part plus the head), with the per-vertex blend shape deltas of the body builds. Used to fit pieces to the builds
    /// (<see cref="PolygonPieceFitter"/>, the per-build offsets of rigid gear) and to MEASURE how far a piece sinks into the body (the tests). Closest-point queries are brute force over a few thousand
    /// triangles, which is plenty for an editor-time tool.
    /// </summary>
    public sealed class BodySurface
    {
        public static readonly string[] BuildShapes = { "defaultBuff", "defaultHeavy", "defaultSkinny" };
        private const string Humans = "Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans";
        private static readonly string[] Regions = { "01HEAD", "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" };

        public readonly Vector3[] Vertices;
        public readonly int[] Triangles;
        /// <summary>Per build shape: the delta of every vertex at weight 100.</summary>
        public readonly Dictionary<string, Vector3[]> Deltas;
        /// <summary>The vertices that belong to the head mesh (01HEAD).</summary>
        public readonly int HeadFirst, HeadCount;

        private BodySurface(Vector3[] vertices, int[] triangles, Dictionary<string, Vector3[]> deltas, int headFirst, int headCount)
        {
            Vertices = vertices; Triangles = triangles; Deltas = deltas; HeadFirst = headFirst; HeadCount = headCount;
        }

        /// <summary>The bare body from the Synty part meshes, or null with the reason in <paramref name="problem"/>.</summary>
        public static BodySurface Load(out string problem)
        {
            problem = null;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var deltas = BuildShapes.ToDictionary(s => s, _ => new List<Vector3>());
            int headFirst = 0, headCount = 0;
            foreach (var region in Regions)
            {
                var path = $"{Humans}/SK_HUMN_BASE_01_{region}_HU01.fbx";
                var smr = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr == null || smr.sharedMesh == null) { problem = "missing body part " + path; return null; }
                var mesh = smr.sharedMesh;
                var offset = vertices.Count;
                if (region == "01HEAD") { headFirst = offset; headCount = mesh.vertexCount; }
                var regionVertices = mesh.vertices;
                var regionTriangles = mesh.triangles;
                OrientOutward(regionVertices, regionTriangles);
                vertices.AddRange(regionVertices);
                triangles.AddRange(regionTriangles.Select(t => t + offset));
                foreach (var shape in BuildShapes)
                {
                    var index = ModelAppearanceApplier.BlendShapeIndex(mesh, shape);
                    var delta = new Vector3[mesh.vertexCount];
                    if (index >= 0 && mesh.blendShapeCount > 0) mesh.GetBlendShapeFrameVertices(index, mesh.GetBlendShapeFrameCount(index) - 1, delta, null, null);
                    deltas[shape].AddRange(delta);
                }
            }

            return new BodySurface(vertices.ToArray(), triangles.ToArray(), deltas.ToDictionary(p => p.Key, p => p.Value.ToArray()), headFirst, headCount);
        }

        /// <summary>The Synty part meshes do not agree on their winding. A region is turned so most of its triangles face away from its own centre (the regions are limbs, a torso, a head: roughly convex).</summary>
        private static void OrientOutward(Vector3[] vertices, int[] triangles)
        {
            var centre = Vector3.zero;
            foreach (var v in vertices) centre += v;
            centre /= vertices.Length;
            var score = 0f;
            for (var t = 0; t < triangles.Length; t += 3)
            {
                var a = vertices[triangles[t]]; var b = vertices[triangles[t + 1]]; var c = vertices[triangles[t + 2]];
                score += Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - centre);
            }

            if (score >= 0f) return;
            for (var t = 0; t < triangles.Length; t += 3) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
        }

        /// <summary>The same surface with every vertex moved by the weighted deltas (weights 0..100, like the blend shape weights).</summary>
        public BodySurface Deformed(IReadOnlyDictionary<string, float> weights)
        {
            var moved = (Vector3[])Vertices.Clone();
            foreach (var pair in weights)
                if (Deltas.TryGetValue(pair.Key, out var delta))
                    for (var i = 0; i < moved.Length; i++) moved[i] += delta[i] * (pair.Value / 100f);
            return new BodySurface(moved, Triangles, Deltas, HeadFirst, HeadCount);
        }

        public struct Hit
        {
            public float Distance;       // unsigned
            public float Signed;         // negative: the point is inside (behind the face)
            public Vector3 Point;
            public int Triangle;
            public Vector3 Barycentric;
        }

        // uniform grid over the triangles (cell 4 cm): a query walks cells outward in shells until no unvisited triangle can be closer than the best hit
        private const float Cell = 0.04f;
        private Dictionary<long, List<int>> grid;
        private Vector3 gridOrigin;
        private int gridSize;
        private int[] stamp;
        private int stampValue;

        private static long Key(int x, int y, int z) => ((long)x << 40) ^ ((long)y << 20) ^ (long)z;

        private void EnsureGrid()
        {
            if (grid != null) return;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = -min;
            foreach (var v in Vertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            gridOrigin = min;
            gridSize = Mathf.CeilToInt(Mathf.Max(max.x - min.x, max.y - min.y, max.z - min.z) / Cell) + 1;
            grid = new Dictionary<long, List<int>>();
            stamp = new int[Triangles.Length / 3];
            for (var t = 0; t < Triangles.Length; t += 3)
            {
                var a = Vertices[Triangles[t]]; var b = Vertices[Triangles[t + 1]]; var c = Vertices[Triangles[t + 2]];
                var lo = CellOf(Vector3.Min(a, Vector3.Min(b, c))); var hi = CellOf(Vector3.Max(a, Vector3.Max(b, c)));
                for (var x = lo.x; x <= hi.x; x++)
                    for (var y = lo.y; y <= hi.y; y++)
                        for (var z = lo.z; z <= hi.z; z++)
                        {
                            var key = Key(x, y, z);
                            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                            list.Add(t / 3);
                        }
            }
        }

        private Vector3Int CellOf(Vector3 p) => new(Mathf.FloorToInt((p.x - gridOrigin.x) / Cell), Mathf.FloorToInt((p.y - gridOrigin.y) / Cell), Mathf.FloorToInt((p.z - gridOrigin.z) / Cell));

        public Hit Closest(Vector3 p)
        {
            EnsureGrid();
            var best = new Hit { Distance = float.MaxValue, Triangle = -1 };
            stampValue++;
            var centre = CellOf(p);
            // the farthest cell that can hold a triangle, from the query cell
            var reach = Mathf.Max(Mathf.Abs(centre.x), Mathf.Abs(centre.x - gridSize), Mathf.Abs(centre.y), Mathf.Abs(centre.y - gridSize), Mathf.Abs(centre.z), Mathf.Abs(centre.z - gridSize));
            for (var k = 0; k <= reach; k++)
            {
                if (best.Triangle >= 0 && best.Distance <= (k - 1) * Cell) break;   // every triangle in shell k or beyond is at least (k - 1) cells away
                for (var x = centre.x - k; x <= centre.x + k; x++)
                    for (var y = centre.y - k; y <= centre.y + k; y++)
                        for (var z = centre.z - k; z <= centre.z + k; z++)
                        {
                            if (Mathf.Max(Mathf.Abs(x - centre.x), Mathf.Abs(y - centre.y), Mathf.Abs(z - centre.z)) != k) continue;
                            if (!grid.TryGetValue(Key(x, y, z), out var list)) continue;
                            foreach (var tri in list)
                            {
                                if (stamp[tri] == stampValue) continue;
                                stamp[tri] = stampValue;
                                var t = tri * 3;
                                var a = Vertices[Triangles[t]]; var b = Vertices[Triangles[t + 1]]; var c = Vertices[Triangles[t + 2]];
                                var q = ClosestOnTriangle(p, a, b, c, out var bary);
                                var d = Vector3.Distance(p, q);
                                if (d >= best.Distance) continue;
                                var normal = Vector3.Cross(b - a, c - a).normalized;
                                best = new Hit { Distance = d, Signed = Vector3.Dot(p - q, normal) >= 0f ? d : -d, Point = q, Triangle = tri, Barycentric = bary };
                            }
                        }
            }

            return best;
        }

        /// <summary>The same query by testing every triangle: the reference the grid-accelerated <see cref="Closest"/> is checked against.</summary>
        public Hit ClosestBruteForce(Vector3 p)
        {
            var best = new Hit { Distance = float.MaxValue, Triangle = -1 };
            for (var t = 0; t < Triangles.Length; t += 3)
            {
                var a = Vertices[Triangles[t]]; var b = Vertices[Triangles[t + 1]]; var c = Vertices[Triangles[t + 2]];
                var q = ClosestOnTriangle(p, a, b, c, out var bary);
                var d = Vector3.Distance(p, q);
                if (d < best.Distance) best = new Hit { Distance = d, Signed = d, Point = q, Triangle = t / 3, Barycentric = bary };
            }

            return best;
        }

        /// <summary>The outward (winding) normal of a triangle of this surface.</summary>
        public Vector3 Normal(int triangle)
        {
            var t = triangle * 3;
            var a = Vertices[Triangles[t]];
            return Vector3.Cross(Vertices[Triangles[t + 1]] - a, Vertices[Triangles[t + 2]] - a).normalized;
        }

        /// <summary>The delta of a build shape at a hit (barycentric over the triangle's vertices).</summary>
        public Vector3 DeltaAt(Hit hit, string shape)
        {
            var d = Deltas[shape]; var t = hit.Triangle * 3;
            return d[Triangles[t]] * hit.Barycentric.x + d[Triangles[t + 1]] * hit.Barycentric.y + d[Triangles[t + 2]] * hit.Barycentric.z;
        }

        public Vector3 MeanHeadDelta(string shape)
        {
            var d = Deltas[shape]; var sum = Vector3.zero;
            for (var i = 0; i < HeadCount; i++) sum += d[HeadFirst + i];
            return HeadCount == 0 ? Vector3.zero : sum / HeadCount;
        }

        /// <summary>Ericson, Real-Time Collision Detection 5.1.5.</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            var d1 = Vector3.Dot(ab, ap); var d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { bary = new Vector3(1, 0, 0); return a; }
            var bp = p - b; var d3 = Vector3.Dot(ab, bp); var d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { bary = new Vector3(0, 1, 0); return b; }
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { var v = d1 / (d1 - d3); bary = new Vector3(1 - v, v, 0); return a + v * ab; }
            var cp = p - c; var d5 = Vector3.Dot(ab, cp); var d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { bary = new Vector3(0, 0, 1); return c; }
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { var w = d2 / (d2 - d6); bary = new Vector3(1 - w, 0, w); return a + w * ac; }
            var va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) { var w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); bary = new Vector3(0, 1 - w, w); return b + w * (c - b); }
            var denom = 1f / (va + vb + vc); var vv = vb * denom; var ww = vc * denom;
            bary = new Vector3(1 - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }
    }
}
