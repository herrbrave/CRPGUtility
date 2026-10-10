using System.Linq;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Stage B: the forest. A jittered grid of pines either side of the road (dense at the verge, thinning outward, none inside the walkable corridor, the camp or a 3.5 m verge),
    /// each standing on the rolling scenery height; trunk capsules only within 10 m of the road (that is all they are for: sight lines round the bends); undergrowth along the
    /// verge; rock banks and a mountain horizon for depth. Seeded, so a rerun is identical.
    /// </summary>
    public static partial class WorldAuthor
    {
        private const int Seed = 20261005;
        private const float TreeSpacing = 3.9f, VergeMargin = 3.5f, SolidTrunkRange = 10f, MaxTreeRange = 75f;

        private static void BuildForest()
        {
            ResetVariants();
            var rng = new System.Random(Seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            var pines = Child(forestRoot, "Pines"); var undergrowth = Child(forestRoot, "Undergrowth"); var banks = Child(forestRoot, "RockBanks"); var horizon = Child(forestRoot, "Horizon");

            WorldGeometry.Bounds(out var min, out var max);
            var solidTrees = 0; var decorTrees = 0;
            for (var z = min.z - MaxTreeRange; z <= max.z + MaxTreeRange; z += TreeSpacing)
                for (var x = min.x - MaxTreeRange; x <= max.x + MaxTreeRange; x += TreeSpacing)
                {
                    var p = new Vector3(x + R(-1.3f, 1.3f), 0f, z + R(-1.3f, 1.3f));
                    var edge = WorldGeometry.EdgeDistance(p);
                    if (edge < VergeMargin || edge > MaxTreeRange || WorldLayout.InCamp(p, 3f)) continue;
                    if (rng.NextDouble() > Mathf.Lerp(0.92f, 0.10f, edge / MaxTreeRange)) continue;

                    var solid = edge <= SolidTrunkRange ? Solid.Trunk : Solid.None;
                    var roll = rng.NextDouble();
                    var source = roll < 0.70 ? WorldAssets.VikingPines[rng.Next(WorldAssets.VikingPines.Length)]
                               : roll < 0.90 ? WorldAssets.AlpinePines[rng.Next(2)]
                               : roll < 0.95 ? WorldAssets.AlpinePines[2]
                               : WorldAssets.DeadStump;
                    p.y = WorldGeometry.SceneryHeight(p);
                    var go = Place(Variant(source, source == WorldAssets.DeadStump ? Solid.None : solid), pines, p, R(0f, 360f), R(0.85f, 1.35f), R(-2.5f, 2.5f), R(-2.5f, 2.5f));
                    if (solid == Solid.Trunk) solidTrees++; else decorTrees++;
                    go.name = go.name.Replace("(Clone)", "");
                }

            // undergrowth along the verge: bushes, ferns, small rocks (decoration only)
            for (var s = 4f; s < WorldLayout.RoadLength; s += 2.2f)
            {
                var pos = WorldLayout.PointAt(s); var t = WorldLayout.TangentAt(s); var right = new Vector3(t.z, 0, -t.x);
                foreach (var side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() > 0.55) continue;
                    var p = pos + right * side * (WorldLayout.HalfWidthAt(s) + R(1.3f, 7f));
                    if (WorldLayout.InCamp(p, 1f) || WorldGeometry.EdgeDistance(p) < 1.2f) continue;
                    p.y = WorldGeometry.SceneryHeight(p);
                    var roll = rng.NextDouble();
                    var source = roll < 0.4 ? WorldAssets.Bushes[rng.Next(WorldAssets.Bushes.Length)] : roll < 0.75 ? WorldAssets.Ferns[rng.Next(WorldAssets.Ferns.Length)] : WorldAssets.SmallRocks[rng.Next(WorldAssets.SmallRocks.Length)];
                    Place(Variant(source, Solid.None), undergrowth, p, R(0f, 360f), R(0.8f, 1.5f));
                }
            }

            // rock banks 14-32 m off the road make it read as a sunken track; decoration only (they stand on the hills, away from the walkable ground)
            for (var s = 6f; s < WorldLayout.RoadLength; s += R(22f, 40f))
            {
                var pos = WorldLayout.PointAt(s); var t = WorldLayout.TangentAt(s); var right = new Vector3(t.z, 0, -t.x);
                foreach (var side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() > 0.6) continue;
                    var p = pos + right * side * (WorldLayout.HalfWidthAt(s) + R(18f, 38f));
                    if (WorldLayout.InCamp(p, 6f)) continue;
                    p.y = WorldGeometry.SceneryHeight(p) - 0.8f;
                    var facing = Quaternion.LookRotation(-right * side).eulerAngles.y + R(-25f, 25f);
                    Place(Variant(WorldAssets.Cliffs[rng.Next(WorldAssets.Cliffs.Length)], Solid.None), banks, p, facing, R(1.0f, 1.7f));
                }
            }

            // the horizon: mountain ranges on an ellipse around the whole world, far enough that fog does most of the work
            var centre = (min + max) / 2f;
            for (var i = 0; i < 26; i++)
            {
                var angle = i * Mathf.PI * 2f / 26f + R(-0.08f, 0.08f);
                var p = centre + new Vector3(Mathf.Cos(angle) * ((max.x - min.x) / 2f + R(190f, 240f)), 0f, Mathf.Sin(angle) * R(230f, 290f));
                p.y = -0.8f;
                Place(Variant(WorldAssets.Mountains[rng.Next(WorldAssets.Mountains.Length)], Solid.None), horizon, p, R(0f, 360f), R(8f, 13f));
            }

            Debug.Log($"[WorldAuthor] forest: {solidTrees} trees with trunk colliders (within {SolidTrunkRange} m of the road), {decorTrees} decorative; {undergrowth.childCount} undergrowth, {banks.childCount} rock banks, {horizon.childCount} mountains");
        }
    }
}
