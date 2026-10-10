using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>One straight stretch or circular arc of the road centreline. Heading is an angle in the XZ plane (0 = +X, counter-clockwise from above = a left turn).</summary>
    public sealed class RoadLeg
    {
        public string Name;
        public Vector3 Start;
        public float StartHeading, Length, Turn, StartS, HalfWidth;

        public float EndS => StartS + Length;
        public float HeadingAt(float distance) => StartHeading + Turn * (distance / Length);
        public Vector3 Direction(float heading) => new(Mathf.Cos(heading), 0f, Mathf.Sin(heading));

        public Vector3 PointAt(float distance)
        {
            if (Mathf.Abs(Turn) < 1e-6f) return Start + Direction(StartHeading) * distance;
            var curvature = Turn / Length;
            var h = HeadingAt(distance);
            return Start + new Vector3((Mathf.Sin(h) - Mathf.Sin(StartHeading)) / curvature, 0f, (Mathf.Cos(StartHeading) - Mathf.Cos(h)) / curvature);
        }
    }

    /// <summary>The playable rectangle of one fight, in the arena's own frame: origin = the middle of its west edge ON the road, +X along the road, +Z to the road's RIGHT.</summary>
    public sealed class ArenaLayout
    {
        public string Id, Name;
        public string[] GoblinUnitIds;
        public int LegIndex;
        public float WestS;
        public Vector3 Origin;   // world position of the west edge midpoint
        public Vector3 Forward;  // unit, along the road
        public Vector3 Right;    // unit, to the road's right
        public float Length = WorldLayout.ArenaLength, Width = WorldLayout.ArenaWidth;

        public Vector3 ToWorld(float x, float z) => Origin + Forward * x + Right * z;
        public Vector2 ToLocal(Vector3 world) { var d = world - Origin; return new Vector2(Vector3.Dot(d, Forward), Vector3.Dot(d, Right)); }
        public bool Contains(Vector3 world, float margin = 0f) { var l = ToLocal(world); return l.x >= -margin && l.x <= Length + margin && Mathf.Abs(l.y) <= Width / 2f + margin; }
        public float YawDegrees => Mathf.Atan2(Forward.x, Forward.z) * Mathf.Rad2Deg;

        public Vector3[] PartyStage => Enumerable.Range(0, WorldLayout.PartySize).Select(i => { var o = WorldLayout.PartyOffset(i); return ToWorld(WorldLayout.PartyLineX + o.x, o.y); }).ToArray();
        public Vector3[] GoblinSpawns => Enumerable.Range(0, GoblinUnitIds.Length).Select(i => { var o = WorldLayout.GoblinOffset(i, GoblinUnitIds.Length); return ToWorld(WorldLayout.PartyLineX + o.x, o.y); }).ToArray();

        // The approach to an arena is on the (possibly curved) road, NOT on the arena's straight axis extended backwards: 18 m back along a 55-70 m radius arc the
        // road is 2-3 m off that axis, outside the walkable strip. So the trigger and checkpoint stand on the real centreline, oriented to its tangent there.
        public float TriggerS => WestS + WorldLayout.TriggerLocalX;
        public float CheckpointS => WestS + WorldLayout.CheckpointLocalX;
        public Vector3 TriggerCenter => WorldLayout.PointAt(TriggerS);
        public Vector3 TriggerForward => WorldLayout.TangentAt(TriggerS);
        public Vector3 TriggerSize(float walkableHalfWidth) => new(WorldLayout.TriggerDepth, 4f, walkableHalfWidth * 2f + WorldLayout.TriggerWidthMargin); // x along the road, z across
        public Vector3 Checkpoint => WorldLayout.PointAt(CheckpointS);
        public Vector3[] CheckpointSlots
        {
            get
            {
                var t = WorldLayout.TangentAt(CheckpointS);
                var right = new Vector3(t.z, 0f, -t.x);
                var center = Checkpoint;
                return Enumerable.Range(0, WorldLayout.PartySize).Select(i => center - t * ((i % 2) * 1.2f) + right * ((i - 1.5f) * 0.8f)).ToArray();
            }
        }
        public Vector3 CameraTarget => ToWorld(WorldLayout.PartyLineX + WorldLayout.GoblinDistance / 2f + 0.5f, 0f);   // between the party line and the goblins: both sides stay in frame
    }

    /// <summary>
    /// THE layout of the world: one table, read by the authoring script, the world tests and the balance simulation, so a number cannot drift between them.
    /// The road is built from straight legs and circular arcs (so every station distance is real arc length, not a typed number); each arena is a straight leg
    /// section with its own local frame. Everything is flat (y = 0). Task 3 design, Section 3 and 9.
    /// </summary>
    public static class WorldLayout
    {
        // ---- the encounter geometry the Task 2 balance simulation was run on: party abreast, goblins ~20 m ahead in a loose column
        public const int PartySize = 4;
        public const float ArenaLength = 44f, ArenaWidth = 22f;
        public const float PartyLineX = 8f;            // party line from the arena's west edge: leaves 14 m of open ground behind the goblins
        public const float PartySpacing = 2f, GoblinDistance = 20f, GoblinColumnOffset = 2f, GoblinPitch = 2.5f;
        public const float TriggerLocalX = -9f, TriggerDepth = 2f, TriggerWidthMargin = 2f;
        public const float CheckpointLocalX = -18f;    // >= 8 m west of the trigger's west face
        public const float YardLength = 58f, YardWidth = 34f, GatehousePassage = 12f, GateOpening = 7.6f;

        /// <summary>Local (x along the line of advance, z across) offset of hero i from the party line -- exactly the simulation's formation.</summary>
        public static Vector2 PartyOffset(int index) => new(0f, -3f + PartySpacing * index);

        /// <summary>Local offset of goblin i of n from the party line -- exactly the simulation's formation (20 m ahead, alternate goblins 2 m further, 2.5 m pitch).</summary>
        public static Vector2 GoblinOffset(int index, int count) => new(GoblinDistance + (index % 2) * GoblinColumnOffset, (index - (count - 1) / 2f) * GoblinPitch);

        public static readonly string[][] EncounterGoblins =
        {
            new[] { "goblin-grunt-a", "goblin-grunt-b", "goblin-grunt-c" },
            new[] { "goblin-warrior-a", "goblin-warrior-b", "goblin-archer-m" },
            new[] { "goblin-warrior-a", "goblin-warrior-b", "goblin-archer-m", "goblin-archer-f", "goblin-shaman" },
            new[] { "goblin-troll", "goblin-king", "goblin-shaman" },
        };

        public static readonly string[] EncounterNames = { "E1 Woods ambush", "E2 Warband", "E3 The gate", "E4 The warlord's yard" };

        public static IReadOnlyList<RoadLeg> Legs { get; }
        public static IReadOnlyList<ArenaLayout> Arenas { get; }
        public static float RoadLength => Legs[Legs.Count - 1].EndS;
        public static float StartClearingRadius => 14f;

        static WorldLayout()
        {
            var legs = new List<RoadLeg>();
            var position = Vector3.zero;
            var heading = 0f;
            var s = 0f;

            void Straight(string name, float length, float halfWidth) => Add(name, length, 0f, halfWidth);
            void Arc(string name, float radius, float degrees, float halfWidth) => Add(name, radius * Mathf.Abs(degrees) * Mathf.Deg2Rad, degrees * Mathf.Deg2Rad, halfWidth);
            void Add(string name, float length, float turn, float halfWidth)
            {
                var leg = new RoadLeg { Name = name, Start = position, StartHeading = heading, Length = length, Turn = turn, StartS = s, HalfWidth = halfWidth };
                legs.Add(leg);
                position = leg.PointAt(length);
                heading = leg.HeadingAt(length);
                s += length;
            }

            Straight("woods-a1", 30f, 3.5f);
            Arc("woods-a2", 70f, 30f, 3.5f);
            Straight("woods-a3", 14f, 3.5f);
            Straight("e1", ArenaLength, 14f);               // 3
            Straight("woods-b1", 12f, 2.5f);                // the squeeze: a 5 m road
            Arc("woods-b2", 55f, -45f, 2.5f);
            Straight("e2", ArenaLength, 14f);               // 6
            Arc("woods-c1", 70f, 25f, 3.5f);
            Straight("woods-c2", 6f, 3.5f);
            Straight("e3", ArenaLength, 17f);               // 9: the forecourt, the palisade closes its east end
            Straight("gatehouse", GatehousePassage, 4.5f);  // 10: the gate passage between forecourt and yard (E4's trigger lives here)
            Straight("e4", YardLength, 17f);                // 11: the yard; the arena is its western 44 m, the HQ stands in the eastern 14 m

            Legs = legs;

            var arenaLegs = new[] { 3, 6, 9, 11 };
            var arenas = new List<ArenaLayout>();
            for (var i = 0; i < arenaLegs.Length; i++)
            {
                var leg = legs[arenaLegs[i]];
                var forward = leg.Direction(leg.StartHeading);
                arenas.Add(new ArenaLayout
                {
                    Id = $"e{i + 1}", Name = EncounterNames[i], GoblinUnitIds = EncounterGoblins[i], LegIndex = arenaLegs[i], WestS = leg.StartS,
                    Origin = leg.Start, Forward = forward, Right = new Vector3(forward.z, 0f, -forward.x),
                });
            }

            Arenas = arenas;
        }

        public static RoadLeg LegAt(float s) => Legs.First(l => s <= l.EndS + 1e-4f);

        public static Vector3 PointAt(float s)
        {
            s = Mathf.Clamp(s, 0f, RoadLength);
            var leg = LegAt(s);
            return leg.PointAt(s - leg.StartS);
        }

        public static Vector3 TangentAt(float s)
        {
            s = Mathf.Clamp(s, 0f, RoadLength);
            var leg = LegAt(s);
            return leg.Direction(leg.HeadingAt(s - leg.StartS));
        }

        public static float HalfWidthAt(float s) => LegAt(Mathf.Clamp(s, 0f, RoadLength)).HalfWidth;

        /// <summary>The camp: forecourt, gatehouse and yard, plus a 21 m half-width for walls, towers and tents. Trees never stand here; the ground outside the walkable parts is flat.</summary>
        public static bool InCamp(Vector3 p, float extra)
        {
            var forecourt = Arenas[2];
            var l = forecourt.ToLocal(p);
            return l.x >= -4f - extra && l.x <= ArenaLength + GatehousePassage + YardLength + 4f + extra && Mathf.Abs(l.y) <= 21f + extra;
        }

        private static List<(Vector3 p, float hw)> corridorSamples;

        /// <summary>Metres from the edge of the walkable ground (0 inside it): the road ribbons (1 m samples, each with its leg's half width) plus the start disc.
        /// Slightly generous at rectangle corners. The world tests use it to assert nothing walkable exists outside the corridor.</summary>
        public static float EdgeDistance(Vector3 p)
        {
            if (corridorSamples == null)
            {
                var list = new List<(Vector3, float)> { (Vector3.zero, StartClearingRadius) };
                for (var s = 0f; s <= RoadLength; s += 1f) list.Add((PointAt(s), HalfWidthAt(s)));
                corridorSamples = list;
            }

            var best = float.MaxValue;
            foreach (var (c, hw) in corridorSamples) { var d = Mathf.Sqrt((p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z)) - hw; if (d < best) best = d; }
            return Mathf.Max(0f, best);
        }

        /// <summary>Walkable half width of the road where an arena's trigger stands (the road leg just west of the arena).</summary>
        public static float WalkableHalfWidthAtTrigger(ArenaLayout arena) => HalfWidthAt(arena.WestS + TriggerLocalX) - 0.5f;

        /// <summary>The start clearing: a disc centred on the origin; the party's first checkpoint (slots abreast across the road, 4 m behind the origin).</summary>
        public static Vector3[] StartSlots => Enumerable.Range(0, PartySize).Select(i => new Vector3(-4f - (i % 2) * 1.2f, 0f, (i - 1.5f) * 1.0f)).ToArray();
    }
}
