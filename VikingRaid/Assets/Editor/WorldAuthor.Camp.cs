using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Stage C: the goblin war camp and the dressing of every clearing. Rules that come from the Task 2 balance and the Task 3 review: nothing solid inside an arena rectangle
    /// (the simulation assumed open ground), the gate stays passable (doors swung open, only the two posts get colliders), and every piece is placed by its MEASURED footprint,
    /// not a guessed one. Camp geometry (forecourt frame F = the E3 arena, yard frame Y = the E4 arena; x along the road, z to the right):
    ///   F x 0..44 open forecourt | F x 47 gate line (towers + palisade) | passage | Y x -1.2 yard west wall | Y x 0..44 open arena | Y x 45..58 fire, HQ | Y x 58.8 east wall.
    /// </summary>
    public static partial class WorldAuthor
    {
        private const float GateLineX = 47f, YardWallX = -1.2f, WallStep = 4.4f, YardHalf = 17.6f;

        private static System.Random campRng;

        private static void BuildCamp()
        {
            campRng = new System.Random(Seed + 1);
            var forecourt = WorldLayout.Arenas[2]; var yard = WorldLayout.Arenas[3];
            var structures = Child(campRoot, "Structures"); var walls = Child(campRoot, "Palisade"); var props = Child(campRoot, "Props"); var dressing = Child(campRoot, "Dressing");

            BuildGate(forecourt, structures);
            BuildPalisade(forecourt, yard, walls);
            BuildYard(yard, structures, props);
            BuildForecourtDressing(forecourt, props);
            BuildApproachDressing(dressing);
            BuildStartClearing(dressing);
            foreach (var arena in WorldLayout.Arenas.Take(2)) DressWoodsArena(arena, dressing);

            Debug.Log($"[WorldAuthor] camp: {walls.childCount} palisade pieces, {structures.childCount} structures, {props.childCount} props, {dressing.childCount} dressing pieces");
        }

        private static float R(float a, float b) => a + (float)campRng.NextDouble() * (b - a);
        private static T Pick<T>(IList<T> list) => list[campRng.Next(list.Count)];

        // ------------------------------------------------------------------ the gate
        private static void BuildGate(ArenaLayout f, Transform parent)
        {
            var pos = f.ToWorld(GateLineX, 0f);
            var gate = Place(Variant(WorldAssets.Gate, Solid.None), parent, pos, YawForLocalZ(f.Forward));
            gate.name = "Gate";

            // the prefab ships with its doors CLOSED across the opening: swing each about its hinge so the passage is clear (7.6 m), never solid
            foreach (var door in gate.GetComponentsInChildren<Transform>().Where(t => t.name.Contains("_Door_")))
            {
                var renderer = door.GetComponentInChildren<Renderer>();
                var hinge = gate.transform.InverseTransformPoint(door.position).x;
                var middle = renderer != null ? gate.transform.InverseTransformPoint(renderer.bounds.center).x : hinge;
                var extendsInward = Mathf.Abs(middle) < Mathf.Abs(hinge);
                var angle = (hinge > 0f ? 90f : -90f) * (extendsInward ? 1f : -1f);
                door.localRotation = Quaternion.Euler(0f, angle, 0f);
            }

            // two posts; the opening between them has no collider at all (and nothing above agent height either)
            foreach (var side in new[] { -1f, 1f })
            {
                var post = new GameObject(side < 0 ? "GatePost_L" : "GatePost_R") { layer = WorldLayers.Obstacle };
                post.transform.SetParent(gate.transform, false);
                post.transform.localPosition = new Vector3(side * 4.55f, 6f, 0.3f);
                var box = post.AddComponent<BoxCollider>(); box.size = new Vector3(1.3f, 12f, 2.7f);
                BlockNavigation(post);
            }

            // towers flank the gate, outside the E3 rectangle (x >= 44.7)
            foreach (var side in new[] { -1f, 1f })
            {
                var tower = Place(Variant(WorldAssets.TowerBig, Solid.Box), parent, f.ToWorld(GateLineX, side * 8.1f), YawForLocalZ(f.Forward) + (side < 0 ? 0f : 180f));
                tower.name = side < 0 ? "Tower_L" : "Tower_R";
            }
        }

        // ------------------------------------------------------------------ palisade: gate line + passage + yard walls
        private static void BuildPalisade(ArenaLayout f, ArenaLayout y, Transform parent)
        {
            // gate line, either side of the gate+towers
            foreach (var side in new[] { -1f, 1f }) Wall(f.ToWorld(GateLineX, side * 10.6f), f.ToWorld(GateLineX, side * 20.5f), parent);
            // passage walls from the gate line to the yard's west wall
            foreach (var side in new[] { -1f, 1f }) Wall(f.ToWorld(GateLineX + 1.8f, side * 4.9f), y.ToWorld(YardWallX, side * 4.9f), parent);
            // yard west wall either side of the passage, north and south walls, east wall behind the HQ
            foreach (var side in new[] { -1f, 1f })
            {
                Wall(y.ToWorld(YardWallX, side * 5.3f), y.ToWorld(YardWallX, side * YardHalf), parent);
                Wall(y.ToWorld(YardWallX, side * YardHalf), y.ToWorld(WorldLayout.YardLength + 0.8f, side * YardHalf), parent);
            }

            Wall(y.ToWorld(WorldLayout.YardLength + 0.8f, -YardHalf), y.ToWorld(WorldLayout.YardLength + 0.8f, YardHalf), parent);
        }

        private static void Wall(Vector3 a, Vector3 b, Transform parent)
        {
            var length = Vector3.Distance(a, b);
            var count = Mathf.Max(1, Mathf.CeilToInt(length / WallStep));
            var step = length / count; var dir = (b - a).normalized;
            for (var i = 0; i < count; i++)
            {
                var source = campRng.NextDouble() < 0.22 ? Pick(WorldAssets.WallTall) : Pick(WorldAssets.WallSegments);
                var centre = a + dir * (step * (i + 0.5f));
                Place(Variant(source, Solid.Box), parent, centre, YawForLocalX(dir), step / 4.5f);
                if (i % 3 == 0) Place(Variant(Pick(WorldAssets.Stakes), Solid.SmallBox), parent, a + dir * (step * i), R(0f, 360f), R(0.9f, 1.2f));
            }
        }

        // ------------------------------------------------------------------ the yard: tents along the side strips, boss fire, HQ, torches
        private static void BuildYard(ArenaLayout y, Transform structures, Transform props)
        {
            // HQ at half scale (the prefab is 40 x 24 x 23 m), its front toward the party, centred on the east zone
            var hq = Place(Variant(WorldAssets.Headquarters, Solid.Box), structures, y.ToWorld(52.2f, 0f), YawForLocalZ(-y.Forward), 0.5f);
            hq.name = "Headquarters";
            Recenter(hq, y.ToWorld(52.2f, 0f));

            // tents in both side strips (outside the arena rectangle |z| <= 11), each sized and pushed out by its measured footprint
            foreach (var side in new[] { -1f, 1f })
            {
                var x = 3f;
                while (x < 40f)
                {
                    var source = Pick(WorldAssets.Tents);
                    var tent = Place(Variant(source, Solid.Box), structures, y.ToWorld(x, side * 14.3f), YawForLocalZ(-y.Right * side));
                    var (xMin, xMax, zMin, zMax) = LocalExtent(tent, y);
                    var halfDepth = (zMax - zMin) / 2f; var halfLength = (xMax - xMin) / 2f;
                    var z = 11.9f + halfDepth;
                    if (z + halfDepth > 16.9f || x + halfLength * 2f > 42f) { Object.DestroyImmediate(tent); x += 2f; continue; }
                    Recenter(tent, y.ToWorld(x + halfLength, side * z));
                    x += halfLength * 2f + R(1.5f, 3.5f);
                }
            }

            // the warlord's fire, east of where the bosses stand and outside the arena rectangle
            Fire(y.ToWorld(45.2f, 0f), WorldAssets.CampFire, props, big: true, lightRange: 22f, lightIntensity: 7f);
            foreach (var side in new[] { -1f, 1f })
            {
                foreach (var x in new[] { 8f, 22f, 36f }) Fire(y.ToWorld(x, side * 12.4f), WorldAssets.Brazier, props, big: false, lightRange: 11f, lightIntensity: 3.2f);
                foreach (var x in new[] { 2f, 20f, 40f }) PoleTorch(y.ToWorld(x, side * 16.2f), props);
                foreach (var x in new[] { 12f, 28f }) Cluster(y, x, side, structures, WorldAssets.Racks);
                Cluster(y, 17f, side, structures, WorldAssets.Crates);
            }
        }

        private static void Cluster(ArenaLayout y, float x, float side, Transform parent, string[] options)
        {
            var prop = Place(Variant(Pick(options), Solid.SmallBox), parent, y.ToWorld(x, side * 15.5f), YawForLocalZ(-y.Right * side) + R(-15f, 15f));
            var (_, _, zMin, zMax) = LocalExtent(prop, y);
            if (Mathf.Min(Mathf.Abs(zMin), Mathf.Abs(zMax)) < 11.6f || Mathf.Max(Mathf.Abs(zMin), Mathf.Abs(zMax)) > 17f) Object.DestroyImmediate(prop);
        }

        // ------------------------------------------------------------------ forecourt (E3) and the approach (woods C)
        private static void BuildForecourtDressing(ArenaLayout f, Transform props)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                foreach (var x in new[] { 6f, 16f, 26f, 36f })
                {
                    var spikes = Place(Variant(Pick(WorldAssets.Spikes), Solid.SmallBox), props, f.ToWorld(x + R(-1.5f, 1.5f), side * 14f), YawForLocalZ(f.Forward) + R(-20f, 20f));
                    var (_, _, zMin, zMax) = LocalExtent(spikes, f);
                    if (Mathf.Min(Mathf.Abs(zMin), Mathf.Abs(zMax)) < 11.6f) Object.DestroyImmediate(spikes);
                }

                // the gate's grisly welcome: trophies beside the gate line, on the forecourt side, outside the arena rectangle
                foreach (var z in new[] { 12.6f, 15.4f })
                {
                    var trophy = Place(Variant(Pick(WorldAssets.Trophies), Solid.SmallBox), props, f.ToWorld(45.4f, side * z), YawForLocalZ(f.Forward) + 180f + R(-20f, 20f));
                    var (xMin, _, _, _) = LocalExtent(trophy, f);
                    if (xMin < 44.4f) Object.DestroyImmediate(trophy);
                }

                foreach (var x in new[] { 10f, 24f, 38f }) PoleTorch(f.ToWorld(x, side * 14.8f), props);
                PoleTorch(f.ToWorld(GateLineX - 1.4f, side * 6.8f), props);
            }
        }

        private static void BuildApproachDressing(Transform parent)
        {
            // from the end of E2 on, goblin work appears along the verge: bones, spikes, trophies (decoration only)
            var from = WorldLayout.Arenas[1].WestS + WorldLayout.ArenaLength + 6f;
            var to = WorldLayout.Arenas[2].WestS - 10f;
            for (var s = from; s < to; s += R(5f, 9f))
            {
                var pos = WorldLayout.PointAt(s); var t = WorldLayout.TangentAt(s); var right = new Vector3(t.z, 0, -t.x);
                foreach (var side in new[] { -1f, 1f })
                {
                    if (campRng.NextDouble() < 0.3) continue;
                    var p = pos + right * side * (WorldLayout.HalfWidthAt(s) + R(1.2f, 3f));
                    p.y = WorldGeometry.SceneryHeight(p);
                    var source = campRng.NextDouble() < 0.5 ? Pick(WorldAssets.Spikes) : campRng.NextDouble() < 0.5 ? Pick(WorldAssets.Bones) : Pick(WorldAssets.Trophies);
                    Place(Variant(source, Solid.None), parent, p, R(0f, 360f));
                }
            }

            // pole torches line the last stretch to the gate so the camp announces itself
            for (var s = to - 24f; s < to; s += 8f)
            {
                var pos = WorldLayout.PointAt(s); var t = WorldLayout.TangentAt(s); var right = new Vector3(t.z, 0, -t.x);
                foreach (var side in new[] { -1f, 1f }) PoleTorch(pos + right * side * (WorldLayout.HalfWidthAt(s) + 0.9f), parent);
            }
        }

        private static void BuildStartClearing(Transform parent)
        {
            Fire(new Vector3(-9f, 0f, 7f), WorldAssets.CampFire, parent, big: false, lightRange: 14f, lightIntensity: 4f);
            for (var i = 0; i < 4; i++)
            {
                var a = Mathf.Deg2Rad * (200f + i * 40f);
                Place(Variant(Pick(WorldAssets.Logs), Solid.None), parent, new Vector3(-9f + Mathf.Cos(a) * 2.4f, 0f, 7f + Mathf.Sin(a) * 2.4f), -a * Mathf.Rad2Deg + 90f);
            }

            for (var i = 0; i < 7; i++)
            {
                var a = Mathf.Deg2Rad * (R(100f, 260f)); var r = R(10.5f, 12.8f);
                Place(Variant(Pick(WorldAssets.Boulders), Solid.Box), parent, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), R(0f, 360f), R(0.6f, 1f));
            }
        }

        // ------------------------------------------------------------------ the woods clearings: boulders, stumps and logs at the edges, never inside the rectangle
        private static void DressWoodsArena(ArenaLayout a, Transform parent)
        {
            for (var i = 0; i < 12; i++)
            {
                var side = i % 2 == 0 ? -1f : 1f;
                var x = R(3f, a.Length - 3f);
                var roll = campRng.NextDouble();
                var source = roll < 0.45 ? Pick(WorldAssets.Boulders) : roll < 0.7 ? Pick(WorldAssets.Logs) : roll < 0.85 ? WorldAssets.DeadStump : Pick(WorldAssets.SmallRocks);
                var solid = source == WorldAssets.DeadStump ? Solid.SmallBox : Solid.Box;
                var item = Place(Variant(source, solid), parent, a.ToWorld(x, side * 13f), YawForLocalX(a.Forward) + R(-25f, 25f), R(0.7f, 1.2f));
                var (_, _, zMin, zMax) = LocalExtent(item, a);
                // outside the open rectangle (|z| <= 11) with a metre to spare, and on the ground (the clearing is 28 m wide)
                if (Mathf.Min(Mathf.Abs(zMin), Mathf.Abs(zMax)) < 11.8f || zMin * zMax < 0f || Mathf.Max(Mathf.Abs(zMin), Mathf.Abs(zMax)) > 14.2f) Object.DestroyImmediate(item);
            }
        }

        // ------------------------------------------------------------------ fire, light, torches
        private static void Fire(Vector3 position, string propPath, Transform parent, bool big, float lightRange, float lightIntensity)
        {
            var prop = Place(Variant(propPath, Solid.SmallBox), parent, position, R(0f, 360f));
            prop.name = $"Fire_{prop.name}";
            AddFlame(position + Vector3.up * 0.5f, big, lightRange, lightIntensity, big);
        }

        private static void PoleTorch(Vector3 position, Transform parent)
        {
            var torch = Place(Variant(WorldAssets.PoleTorch, Solid.SmallBox), parent, position, R(0f, 360f));
            torch.name = "PoleTorch";
            AddFlame(position + Vector3.up * 6.05f, false, 12f, 3.4f, false);
        }

        private static void AddFlame(Vector3 position, bool big, float range, float intensity, bool shadows)
        {
            var fx = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(big ? WorldAssets.FireBig : WorldAssets.FireSmall), fxRoot);
            fx.transform.position = position;
            if (big)
            {
                var embers = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(WorldAssets.Embers), fxRoot);
                embers.transform.position = position + Vector3.up * 0.4f;
            }

            var lightObject = new GameObject("FireLight"); lightObject.transform.SetParent(lightRoot, false); lightObject.transform.position = position + Vector3.up * 0.6f;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.55f, 0.22f); light.range = range; light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        // ------------------------------------------------------------------ footprints
        /// <summary>The object's footprint in an arena's frame, from its collider box if it has one (exact), else its renderer bounds (the world AABB: slightly generous).</summary>
        private static (float xMin, float xMax, float zMin, float zMax) LocalExtent(GameObject go, ArenaLayout arena)
        {
            var points = new List<Vector3>();
            var box = go.GetComponent<BoxCollider>();
            if (box != null)
                for (var i = 0; i < 8; i++)
                    points.Add(box.transform.TransformPoint(box.center + Vector3.Scale(box.size / 2f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            else
                foreach (var r in go.GetComponentsInChildren<Renderer>().Where(r => r is not ParticleSystemRenderer))
                    for (var i = 0; i < 8; i++)
                        points.Add(r.bounds.center + Vector3.Scale(r.bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));

            var local = points.Select(arena.ToLocal).ToList();
            return (local.Min(p => p.x), local.Max(p => p.x), local.Min(p => p.y), local.Max(p => p.y));
        }

        private static Vector3 CentreOf(GameObject go)
        {
            var box = go.GetComponent<BoxCollider>();
            if (box != null) return box.transform.TransformPoint(box.center);
            var b = go.GetComponentsInChildren<Renderer>().Where(r => r is not ParticleSystemRenderer).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            return b.center;
        }

        /// <summary>Moves the object horizontally so its footprint's centre sits on the target (prefab pivots are rarely at the middle).</summary>
        private static void Recenter(GameObject go, Vector3 target)
        {
            var delta = target - CentreOf(go); delta.y = 0f;
            go.transform.position += delta;
        }
    }
}
