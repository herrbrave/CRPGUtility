using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    /// <summary>The pure-data half of the world tests: the layout table itself. Every geometric promise the scene makes (open arenas, triggers that cannot be walked
    /// around, checkpoints on the road and clear of triggers, spawn points that reproduce the balance simulation's formation) is checked here in milliseconds, before a
    /// scene exists. The scene tests then check the BAKED world against the same table.</summary>
    public class WorldLayoutTests
    {
        /// <summary>An oriented rectangle on the ground plane (x along <see cref="Forward"/>, z across), for overlap and distance checks.</summary>
        private readonly struct Box
        {
            public readonly Vector2 Center, Forward, Right, Half;
            public Box(Vector3 center, Vector3 forward, float alongHalf, float acrossHalf)
            {
                Center = new Vector2(center.x, center.z);
                Forward = new Vector2(forward.x, forward.z).normalized;
                Right = new Vector2(Forward.y, -Forward.x);
                Half = new Vector2(alongHalf, acrossHalf);
            }

            public Vector2 Local(Vector2 p) { var d = p - Center; return new Vector2(Vector2.Dot(d, Forward), Vector2.Dot(d, Right)); }
            public float DistanceTo(Vector3 world)
            {
                var l = Local(new Vector2(world.x, world.z));
                var outside = new Vector2(Mathf.Max(Mathf.Abs(l.x) - Half.x, 0f), Mathf.Max(Mathf.Abs(l.y) - Half.y, 0f));
                return outside.magnitude;
            }

            public IEnumerable<Vector2> Corners() { foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) yield return Center + Forward * (sx * Half.x) + Right * (sz * Half.y); }

            public bool Overlaps(Box other)
            {
                foreach (var axis in new[] { Forward, Right, other.Forward, other.Right })
                {
                    float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                    foreach (var c in Corners()) { var p = Vector2.Dot(c, axis); aMin = Mathf.Min(aMin, p); aMax = Mathf.Max(aMax, p); }
                    foreach (var c in other.Corners()) { var p = Vector2.Dot(c, axis); bMin = Mathf.Min(bMin, p); bMax = Mathf.Max(bMax, p); }
                    if (aMax < bMin || bMax < aMin) return false;
                }
                return true;
            }
        }

        private static Box ArenaBox(ArenaLayout a) => new(a.ToWorld(a.Length / 2f, 0f), a.Forward, a.Length / 2f, a.Width / 2f);
        private static Box TriggerBox(ArenaLayout a)
        {
            var size = a.TriggerSize(WorldLayout.WalkableHalfWidthAtTrigger(a));
            return new Box(a.TriggerCenter, a.TriggerForward, size.x / 2f, size.z / 2f);
        }

        private static float DistanceToRoadCentreline(Vector3 p)
        {
            var best = float.MaxValue;
            for (var s = 0f; s <= WorldLayout.RoadLength; s += 0.5f) best = Mathf.Min(best, Vector3.Distance(new Vector3(p.x, 0, p.z), WorldLayout.PointAt(s)));
            return best;
        }

        [Test]
        public void TheRoadIsContinuousUnitSpeedAndItsTangentFollowsIt()
        {
            for (var s = 0f; s < WorldLayout.RoadLength - 0.5f; s += 0.5f)
            {
                var a = WorldLayout.PointAt(s); var b = WorldLayout.PointAt(s + 0.5f);
                Assert.AreEqual(0.5f, Vector3.Distance(a, b), 0.01f, $"arc length is not real at s={s}");
                Assert.AreEqual(1f, WorldLayout.TangentAt(s).magnitude, 1e-3f);
                Assert.Less(Vector3.Angle(WorldLayout.TangentAt(s + 0.25f), (b - a).normalized), 1.5f, $"tangent does not follow the road at s={s}");
                Assert.AreEqual(0f, a.y, 1e-6f, "the world is flat");
            }
        }

        [Test]
        public void TheRoadIsAboutThreeHundredAndFiftyMetresWithTheFourStationsInOrder()
        {
            Assert.That(WorldLayout.RoadLength, Is.InRange(330f, 380f));
            Assert.AreEqual(4, WorldLayout.Arenas.Count);
            for (var i = 1; i < WorldLayout.Arenas.Count; i++)
                Assert.Greater(WorldLayout.Arenas[i].WestS, WorldLayout.Arenas[i - 1].WestS + WorldLayout.ArenaLength + 8f, $"{WorldLayout.Arenas[i].Id} starts too close to the arena before it");
        }

        [Test]
        public void EveryArenaIsAtLeast44By22AndIsASingleStraightStretchOfRoad()
        {
            foreach (var a in WorldLayout.Arenas)
            {
                Assert.GreaterOrEqual(a.Length, 44f); Assert.GreaterOrEqual(a.Width, 22f);
                var leg = WorldLayout.Legs[a.LegIndex];
                Assert.AreEqual(0f, leg.Turn, 1e-6f, $"{a.Id} must sit on a straight leg: its rectangle is axis-aligned to the road");
                Assert.GreaterOrEqual(leg.Length, a.Length, $"{a.Id}'s leg is shorter than its arena");
                Assert.AreEqual(1f, Vector3.Dot(a.Forward, WorldLayout.TangentAt(a.WestS + 1f)), 1e-4f, $"{a.Id}'s axis is not the road's tangent");
                Assert.GreaterOrEqual(leg.HalfWidth * 2f, a.Width + 2f, $"{a.Id}'s ground must be wider than the rectangle by at least 1 m each side so the eroded NavMesh still covers it");
            }
        }

        [Test]
        public void ArenaRectanglesNeverOverlapEachOther()
        {
            var boxes = WorldLayout.Arenas.Select(ArenaBox).ToList();
            for (var i = 0; i < boxes.Count; i++) for (var j = i + 1; j < boxes.Count; j++) Assert.IsFalse(boxes[i].Overlaps(boxes[j]), $"arena {i + 1} overlaps arena {j + 1}");
        }

        [Test]
        public void EachTriggerIsInTheRoadEightToTenMetresBeforeItsArenaAndCannotBeWalkedAround()
        {
            var arenaBoxes = WorldLayout.Arenas.Select(ArenaBox).ToList();
            var triggerBoxes = WorldLayout.Arenas.Select(TriggerBox).ToList();
            for (var i = 0; i < WorldLayout.Arenas.Count; i++)
            {
                var a = WorldLayout.Arenas[i];
                Assert.That(a.WestS - a.TriggerS, Is.InRange(8f, 10f), $"{a.Id} trigger distance");
                var size = a.TriggerSize(WorldLayout.WalkableHalfWidthAtTrigger(a));
                Assert.GreaterOrEqual(size.z, 2f * WorldLayout.WalkableHalfWidthAtTrigger(a) + 2f, $"{a.Id}: a trigger narrower than the walkable road + 2 m can be walked around");
                var triggerLeg = WorldLayout.Legs.ToList().IndexOf(WorldLayout.LegAt(a.TriggerS));
                Assert.IsFalse(WorldLayout.Arenas.Any(x => x.LegIndex == triggerLeg), $"{a.Id}'s trigger stands on an arena's own leg, not on the approach road");
                foreach (var arena in arenaBoxes) Assert.IsFalse(triggerBoxes[i].Overlaps(arena), $"{a.Id}'s trigger overlaps an arena rectangle: it would fire mid-fight");
                for (var j = i + 1; j < triggerBoxes.Count; j++) Assert.IsFalse(triggerBoxes[i].Overlaps(triggerBoxes[j]), $"triggers {i + 1} and {j + 1} overlap");
            }
        }

        [Test]
        public void CheckpointsAreOnTheRoadWellBeforeTheirTriggerAndTheirSlotsAreClearOfEveryTrigger()
        {
            var triggerBoxes = WorldLayout.Arenas.Select(TriggerBox).ToList();
            foreach (var a in WorldLayout.Arenas)
            {
                Assert.GreaterOrEqual(a.TriggerS - WorldLayout.TriggerDepth / 2f - a.CheckpointS, 8f, $"{a.Id}: the checkpoint must be >= 8 m before the trigger's west face");
                var slots = a.CheckpointSlots;
                Assert.AreEqual(WorldLayout.PartySize, slots.Length);
                var walkable = WorldLayout.HalfWidthAt(a.CheckpointS) - 0.5f;
                foreach (var slot in slots)
                {
                    Assert.LessOrEqual(DistanceToRoadCentreline(slot), walkable - 0.3f, $"{a.Id}: a checkpoint slot is off the walkable road ({slot})");
                    foreach (var box in triggerBoxes) Assert.GreaterOrEqual(box.DistanceTo(slot), 3f, $"{a.Id}: a respawned party would start inside or beside a trigger");
                }
            }

            foreach (var slot in WorldLayout.StartSlots)
            {
                Assert.Less(new Vector2(slot.x, slot.z).magnitude, WorldLayout.StartClearingRadius - 2f, "start slot inside the clearing");
                foreach (var box in triggerBoxes) Assert.GreaterOrEqual(box.DistanceTo(slot), 3f);
            }
        }

        [Test]
        public void SpawnPointsReproduceTheBalanceSimulationsFormationExactly()
        {
            // the literals of the original simulation: party at x=0, z=-3,-1,1,3; goblin i at x=20+(i%2)*2, z=(i-(n-1)/2)*2.5
            for (var i = 0; i < 4; i++) Assert.AreEqual(new Vector2(0f, -3f + 2f * i), WorldLayout.PartyOffset(i));
            foreach (var n in new[] { 3, 5 })
                for (var i = 0; i < n; i++) Assert.AreEqual(new Vector2(20f + (i % 2) * 2f, (i - (n - 1) / 2f) * 2.5f), WorldLayout.GoblinOffset(i, n), $"n={n} i={i}");

            foreach (var a in WorldLayout.Arenas)
            {
                var party = a.PartyStage; var goblins = a.GoblinSpawns;
                for (var i = 0; i < party.Length; i++)
                {
                    var l = a.ToLocal(party[i]);
                    Assert.AreEqual(WorldLayout.PartyLineX, l.x, 1e-3f); Assert.AreEqual(-3f + 2f * i, l.y, 1e-3f);
                }

                for (var i = 0; i < goblins.Length; i++)
                {
                    var l = a.ToLocal(goblins[i]); var expected = WorldLayout.GoblinOffset(i, goblins.Length);
                    Assert.AreEqual(WorldLayout.PartyLineX + expected.x, l.x, 1e-3f, $"{a.Id} goblin {i} x"); Assert.AreEqual(expected.y, l.y, 1e-3f, $"{a.Id} goblin {i} z");
                }
            }
        }

        [Test]
        public void TheFourEncountersAreThreeThreeFiveThreeGoblinsOfRealGoblinUnits()
        {
            CollectionAssert.AreEqual(new[] { 3, 3, 5, 3 }, WorldLayout.Arenas.Select(a => a.GoblinUnitIds.Length).ToArray());
            var goblinIds = new HashSet<string>(GameDesign.Goblins.Select(u => u.Id));
            foreach (var a in WorldLayout.Arenas) foreach (var id in a.GoblinUnitIds) Assert.IsTrue(goblinIds.Contains(id), $"{a.Id}: '{id}' is not a goblin unit");
            Assert.IsTrue(WorldLayout.Arenas[3].GoblinUnitIds.Contains("goblin-troll") && WorldLayout.Arenas[3].GoblinUnitIds.Contains("goblin-king"), "E4 is the boss fight");
        }

        [Test]
        public void EveryoneStartsInOpenGroundWellInsideTheirArena()
        {
            foreach (var a in WorldLayout.Arenas)
            {
                foreach (var p in a.PartyStage.Concat(a.GoblinSpawns)) Assert.IsTrue(a.Contains(p, margin: -3f), $"{a.Id}: {p} is not at least 3 m inside the arena");
                Assert.IsTrue(a.Contains(a.CameraTarget, margin: -3f));
                var furthest = a.GoblinSpawns.Max(g => a.ToLocal(g).x);
                Assert.Greater(a.Length - furthest, 10f, $"{a.Id}: goblins start with less than 10 m of open ground behind them");
            }
        }

        [Test]
        public void TheYardIsFiftyEightByThirtyFourWithTheArenaInItsWesternFortyFourAndTheHqZoneToTheEast()
        {
            var yard = WorldLayout.Legs[WorldLayout.Arenas[3].LegIndex];
            Assert.AreEqual(WorldLayout.YardLength, yard.Length, 1e-3f);
            Assert.AreEqual(WorldLayout.YardWidth, yard.HalfWidth * 2f, 1e-3f);
            Assert.GreaterOrEqual(yard.Length - WorldLayout.Arenas[3].Length, 12f, "the HQ and boss fire need >= 12 m of yard east of the arena");
            Assert.GreaterOrEqual((WorldLayout.YardWidth - WorldLayout.Arenas[3].Width) / 2f, 6f, "tents need >= 6 m side strips outside the arena");
            Assert.GreaterOrEqual(WorldLayout.GateOpening, 6f, "the gate opening has to take the party and the Troll");
        }
    }
}
