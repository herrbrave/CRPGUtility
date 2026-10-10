using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>
    /// The BAKED world against the layout table: the real scene is loaded in play mode and the real NavMesh is queried. Tolerances are deliberately tight (the review found
    /// loose ones that pass broken worlds): anchors must sit ON the mesh (horizontal error &lt; 5 cm) and clear of its eroded edge, every arena must be 100% open, a trigger
    /// must be wider than the walkable road, and a click anywhere over an arena must hit the ground.
    /// </summary>
    public class WorldSceneTests
    {
        private const string ScenePath = "Assets/Scenes/VikingRaid.unity";
        private WorldAnchors anchors;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return null;
            anchors = Object.FindFirstObjectByType<WorldAnchors>();
            Assert.IsNotNull(anchors, "no WorldAnchors in the scene -- run 'Viking Raid/Task 3/Author World'");
        }

        [UnityTearDown]
        public IEnumerator UnloadWorldAfterTest() => SceneIsolation.UnloadAll();

        private static bool OnMesh(Vector3 p, float tolerance, out Vector3 snapped)
        {
            if (!NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas)) { snapped = p; return false; }
            snapped = hit.position;
            return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(hit.position.x, hit.position.z)) <= tolerance;
        }

        private static float EdgeClearance(Vector3 p)
        {
            NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas);
            return NavMesh.FindClosestEdge(hit.position, out var edge, NavMesh.AllAreas) ? edge.distance : float.MaxValue;
        }

        private IEnumerable<(string what, Vector3 position)> EveryAnchor()
        {
            foreach (var t in anchors.startSlots) yield return ($"start {t.name}", t.position);
            foreach (var e in anchors.encounters)
            {
                foreach (var t in e.partyStage) yield return ($"{e.id} stage {t.name}", t.position);
                foreach (var t in e.goblinSpawns) yield return ($"{e.id} spawn {t.name}", t.position);
                foreach (var t in e.checkpointSlots) yield return ($"{e.id} checkpoint {t.name}", t.position);
            }
        }

        [UnityTest]
        public IEnumerator TheSceneHasOneNavMeshSurfaceFourEncountersAndTheNamedLayers()
        {
            Assert.AreEqual(1, Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(4, anchors.encounters.Length);
            CollectionAssert.AreEqual(new[] { 3, 3, 5, 3 }, anchors.encounters.Select(e => e.goblinSpawns.Length).ToArray());
            for (var i = 0; i < WorldLayers.Names.Length; i++) Assert.AreEqual(WorldLayers.Names[i], LayerMask.LayerToName(WorldLayers.Ground + i));
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryAnchorIsOnTheNavMeshAndClearOfItsEdge()
        {
            var failures = new List<string>();
            foreach (var (what, position) in EveryAnchor())
            {
                if (!OnMesh(position, 0.05f, out _)) { failures.Add($"{what} is not on the NavMesh ({position})"); continue; }
                var clearance = EdgeClearance(position);
                if (clearance < 0.55f) failures.Add($"{what} is only {clearance:F2} m from the mesh edge (the Troll needs 0.55)");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePartyCanWalkFromTheStartThroughEveryCheckpointAndStageToTheYard()
        {
            var waypoints = new List<(string, Vector3)> { ("start", anchors.startSlots[0].position) };
            foreach (var e in anchors.encounters)
            {
                waypoints.Add(($"{e.id} checkpoint", e.checkpointSlots[0].position));
                waypoints.Add(($"{e.id} stage", e.partyStage[0].position));
                waypoints.Add(($"{e.id} far goblin", e.goblinSpawns.Last().position));
            }

            var path = new NavMeshPath();
            for (var i = 1; i < waypoints.Count; i++)
            {
                var (_, from) = waypoints[i - 1]; var (name, to) = waypoints[i];
                Assert.IsTrue(NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path), $"no path to {name}");
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, $"path to {name} is {path.status}");
                Assert.Less(Vector3.Distance(path.corners.Last(), to), 0.1f, $"the path to {name} stops {Vector3.Distance(path.corners.Last(), to):F2} m short");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryArenaIsCompletelyOpenGround()
        {
            var failures = new List<string>();
            var path = new NavMeshPath();
            foreach (var arena in WorldLayout.Arenas)
            {
                // 1. every probe on the whole rectangle (a 1 m grid, inset 0.5 m) is on the mesh
                var probes = 0;
                for (var x = 0.5f; x <= arena.Length - 0.5f; x += 1f)
                    for (var z = -arena.Width / 2f + 0.5f; z <= arena.Width / 2f - 0.5f; z += 1f)
                    {
                        probes++;
                        if (!OnMesh(arena.ToWorld(x, z), 0.05f, out _)) failures.Add($"{arena.Id}: ({x:F1},{z:F1}) is not walkable");
                    }

                Assert.Greater(probes, 800);

                // 2. straight moves along rows and columns are unobstructed (the straight lines the simulation assumes)
                for (var z = -arena.Width / 2f + 1f; z <= arena.Width / 2f - 1f; z += 2f)
                    if (NavMesh.Raycast(arena.ToWorld(0.5f, z), arena.ToWorld(arena.Length - 0.5f, z), out var hit, NavMesh.AllAreas)) failures.Add($"{arena.Id}: row z={z:F1} is blocked at {hit.position}");
                for (var x = 1f; x <= arena.Length - 1f; x += 4f)
                    if (NavMesh.Raycast(arena.ToWorld(x, -arena.Width / 2f + 0.5f), arena.ToWorld(x, arena.Width / 2f - 0.5f), out var hit, NavMesh.AllAreas)) failures.Add($"{arena.Id}: column x={x:F1} is blocked at {hit.position}");

                // 3. no obstacle collider anywhere in the rectangle
                var centre = arena.ToWorld(arena.Length / 2f, 0f) + Vector3.up * 3f;
                var blockers = Physics.OverlapBox(centre, new Vector3(arena.Length / 2f, 3f, arena.Width / 2f), Quaternion.LookRotation(arena.Forward) * Quaternion.Euler(0, 90, 0), 1 << WorldLayers.Obstacle);
                if (blockers.Length > 0) failures.Add($"{arena.Id}: obstacle colliders inside the rectangle: {string.Join(", ", blockers.Select(b => b.name))}");
            }

            Assert.IsEmpty(failures.Take(25), string.Join("\n", failures.Take(25)) + (failures.Count > 25 ? $"\n... and {failures.Count - 25} more" : ""));
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachTriggerIsAnIsTriggerBoxOnTheTriggerLayerWiderThanTheWalkableRoadItSpans()
        {
            foreach (var e in anchors.encounters)
            {
                Assert.IsTrue(e.trigger.isTrigger); Assert.AreEqual(WorldLayers.Trigger, e.trigger.gameObject.layer);
                var centre = new Vector3(e.trigger.transform.position.x, 0f, e.trigger.transform.position.z);
                var across = new Vector3(e.trigger.transform.forward.x, 0, e.trigger.transform.forward.z).normalized; // the box's local Z is the across axis
                // walk outwards from the road centre until the mesh ends, both ways
                float Reach(float sign)
                {
                    var d = 0f;
                    while (d < 30f && NavMesh.SamplePosition(centre + across * sign * (d + 0.25f), out var hit, 0.1f, NavMesh.AllAreas) && Vector2.Distance(new Vector2(hit.position.x, hit.position.z), new Vector2(centre.x + across.x * sign * (d + 0.25f), centre.z + across.z * sign * (d + 0.25f))) < 0.1f) d += 0.25f;
                    return d;
                }

                var reach = Mathf.Max(Reach(1f), Reach(-1f));
                var boxHalfAcross = e.trigger.size.z / 2f;
                Assert.Greater(reach, 1.5f, $"{e.id}: no walkable road found at the trigger");
                Assert.GreaterOrEqual(boxHalfAcross, reach + 0.9f, $"{e.id}: the trigger ({boxHalfAcross:F1} m half width) can be walked around (walkable half width {reach:F1} m)");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePathFromACheckpointToItsStageCrossesOnlyItsOwnTrigger()
        {
            var path = new NavMeshPath();
            for (var k = 0; k < anchors.encounters.Length; k++)
            {
                var e = anchors.encounters[k];
                Assert.IsTrue(NavMesh.CalculatePath(e.checkpointSlots[0].position, e.partyStage[0].position, NavMesh.AllAreas, path));
                var crossed = new HashSet<int>();
                for (var i = 1; i < path.corners.Length; i++)
                {
                    var a = path.corners[i - 1]; var b = path.corners[i];
                    for (var f = 0f; f <= 1f; f += 0.5f / Mathf.Max(0.5f, Vector3.Distance(a, b)))
                    {
                        var p = Vector3.Lerp(a, b, f) + Vector3.up;
                        for (var j = 0; j < anchors.encounters.Length; j++) if (anchors.encounters[j].trigger.ClosestPoint(p) == p) crossed.Add(j);
                    }
                }

                CollectionAssert.AreEquivalent(new[] { k }, crossed.ToArray(), $"{e.id}: the walk from its checkpoint to its stage crosses triggers [{string.Join(",", crossed.Select(c => anchors.encounters[c].id))}]");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator NothingWalkableExistsOutsideTheCorridor()
        {
            var tri = NavMesh.CalculateTriangulation();
            Assert.Greater(tri.vertices.Length, 100);
            var bad = tri.vertices.Where(v => WorldLayout.EdgeDistance(v) > 0.1f || v.y > 0.3f || v.y < -0.2f).Take(5).ToList();
            Assert.IsEmpty(bad, $"NavMesh vertices outside the corridor or off the ground plane (islands on roofs/rocks?): {string.Join(" ", bad)}");

            // and 10 m beside the road (where that is genuinely outside the corridor: not inside a clearing or the start disc) there is simply no mesh
            var checks = 0;
            for (var s = 5f; s < WorldLayout.RoadLength; s += 30f)
            {
                var p = WorldLayout.PointAt(s); var t = WorldLayout.TangentAt(s); var right = new Vector3(t.z, 0, -t.x);
                foreach (var side in new[] { -1f, 1f })
                {
                    var off = p + right * side * (WorldLayout.HalfWidthAt(s) + 10f);
                    if (WorldLayout.EdgeDistance(off) < 4f) continue;
                    checks++;
                    Assert.IsFalse(NavMesh.SamplePosition(off, out _, 2f, NavMesh.AllAreas), $"walkable ground 10 m beside the corridor at s={s:F0}");
                }
            }

            Assert.GreaterOrEqual(checks, 8, "too few genuinely off-corridor probes to mean anything");

            yield return null;
        }


        [UnityTest]
        public IEnumerator EveryColliderIsOneWeAuthoredOnAProjectLayer()
        {
            // the Synty prefabs ship their own colliders (a 15 m whole-tree capsule, mesh colliders on every building): all stripped, so none may be left on Default
            var strays = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.gameObject.layer == 0).Take(8).Select(c => c.name).ToList();
            Assert.IsEmpty(strays, "colliders left on the Default layer: " + string.Join(", ", strays));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheForestIsDenseButNotSilly_TreesStayOutOfTheCorridorTheVergeAndTheCamp()
        {
            var forest = GameObject.Find("Forest/Pines");
            Assert.IsNotNull(forest);
            var trees = forest.GetComponentsInChildren<Transform>().Where(t => t.parent == forest.transform).ToList();
            Assert.That(trees.Count, Is.InRange(1200, 2600), "tree count");
            var tooClose = trees.Where(t => WorldLayout.EdgeDistance(t.position) < 3.4f).Take(5).Select(t => t.position).ToList();
            Assert.IsEmpty(tooClose, "trees inside the corridor or its 3.5 m verge: " + string.Join(" ", tooClose));
            var inCamp = trees.Where(t => WorldLayout.InCamp(t.position, 0f)).Take(5).Select(t => t.position).ToList();
            Assert.IsEmpty(inCamp, "trees inside the camp: " + string.Join(" ", inCamp));

            // trunk colliders only where they matter (sight round bends): within ~10 m of the road
            var capsules = forest.GetComponentsInChildren<CapsuleCollider>().ToList();
            Assert.Greater(capsules.Count, 150, "the near-road trees must have trunk colliders");
            Assert.IsTrue(capsules.All(c => c.gameObject.layer == WorldLayers.Obstacle));
            var far = capsules.Where(c => WorldLayout.EdgeDistance(c.transform.position) > 10.5f).Take(3).ToList();
            Assert.IsEmpty(far, "trunk colliders far from the road (wasted bake/physics work)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SightLinesCrossEveryArenaUnobstructedButTrunksBlockThem()
        {
            var los = new CRPG.Core.PhysicsLineOfSightProvider(WorldLayers.SightBlocker);
            foreach (var arena in WorldLayout.Arenas)
                foreach (var z in new[] { -8f, -3f, 0f, 3f, 8f })
                    Assert.IsTrue(los.HasLineOfSight(arena.ToWorld(0.5f, z) + Vector3.up * 1.4f, arena.ToWorld(arena.Length - 0.5f, z) + Vector3.up * 1.4f), $"{arena.Id}: eye-height sight line at z={z} is blocked");

            // the ground must never block sight (a bump would block every shot)
            var start = WorldLayout.PointAt(5f) + Vector3.up * 1.4f;
            Assert.IsTrue(los.HasLineOfSight(start, WorldLayout.PointAt(40f) + Vector3.up * 1.4f), "ground blocks sight along the road");

            // a trunk right next to the road blocks a line through it
            var trunk = Object.FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None).First(c => c.gameObject.layer == WorldLayers.Obstacle);
            var centre = trunk.transform.TransformPoint(trunk.center);
            Assert.IsFalse(los.HasLineOfSight(centre + Vector3.left * 3f, centre + Vector3.right * 3f), "a trunk does not block sight");
            yield return null;
        }


        [UnityTest]
        public IEnumerator TheLightingIsAuthoredADuskSkyASunFogThreeBakedProbesAndABoundedNumberOfLights()
        {
            Assert.IsNotNull(RenderSettings.skybox, "no skybox"); Assert.AreEqual("Skybox/Procedural", RenderSettings.skybox.shader.name, "the Synty sky materials are meshes, not skyboxes");
            Assert.IsNotNull(RenderSettings.sun); Assert.IsTrue(RenderSettings.fog); Assert.AreEqual(FogMode.ExponentialSquared, RenderSettings.fogMode);
            Assert.That(RenderSettings.sun.transform.eulerAngles.x, Is.InRange(20f, 40f), "a low sun makes tree shadows streak across every arena");
            var probes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
            Assert.AreEqual(3, probes.Length);
            foreach (var probe in probes) { Assert.AreEqual(UnityEngine.Rendering.ReflectionProbeMode.Custom, probe.mode); Assert.IsNotNull(probe.customBakedTexture, probe.name + " has no baked cubemap: metals would have nothing to reflect"); }
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            Assert.That(lights.Count(l => l.type == LightType.Point), Is.InRange(8, 40), "point lights (fires)");
            Assert.AreEqual(1, lights.Count(l => l.type == LightType.Directional));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheGateOpeningIsWideEnoughForTheTrollAndThePathToTheYardGoesThroughIt()
        {
            var f = WorldLayout.Arenas[2];
            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(anchors.encounters[2].partyStage[0].position, anchors.encounters[3].partyStage[0].position, NavMesh.AllAreas, path));
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);
            var crossed = false;
            for (var i = 1; i < path.corners.Length; i++)
            {
                var a = f.ToLocal(path.corners[i - 1]); var b = f.ToLocal(path.corners[i]);
                if ((a.x - 47f) * (b.x - 47f) > 0f) continue;
                var z = Mathf.Lerp(a.y, b.y, Mathf.InverseLerp(a.x, b.x, 47f));
                Assert.Less(Mathf.Abs(z), 3.8f, "the path crosses the gate line outside the gate opening");
                crossed = true;
            }

            Assert.IsTrue(crossed, "the path to the yard never crosses the gate line");

            // the walkable width at the gate line itself (the doors must be swung open, the posts must not eat the passage)
            var centre = f.ToWorld(47f, 0f);
            float Reach(float sign) { var d = 0f; while (d < 12f && NavMesh.SamplePosition(f.ToWorld(47f, sign * (d + 0.25f)), out var hit, 0.1f, NavMesh.AllAreas) && Vector2.Distance(new Vector2(hit.position.x, hit.position.z), new Vector2(f.ToWorld(47f, sign * (d + 0.25f)).x, f.ToWorld(47f, sign * (d + 0.25f)).z)) < 0.1f) d += 0.25f; return d; }
            var width = Reach(1f) + Reach(-1f);
            Assert.GreaterOrEqual(width, 6f, $"only {width:F1} m walkable through the gate: the Troll (1.1 m) and four heroes need room");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheTacticalCameraFramingSeesEveryStageAndSpawnPointAndNoTrunkBlocksTheView()
        {
            var camera = Camera.main;
            camera.orthographic = false; camera.fieldOfView = 48f; camera.aspect = 16f / 9f;
            for (var i = 0; i < 2; i++) // the two woods arenas, from the framing the captures use
            {
                var a = WorldLayout.Arenas[i]; var e = anchors.encounters[i];
                camera.transform.position = a.ToWorld(-9f, 5f) + Vector3.up * 11f; camera.transform.rotation = Quaternion.LookRotation(a.ToWorld(26f, 0f) - camera.transform.position);
                foreach (var t in e.partyStage.Concat(e.goblinSpawns))
                {
                    var v = camera.WorldToViewportPoint(t.position + Vector3.up);
                    Assert.IsTrue(v.z > 0f && v.x > 0.04f && v.x < 0.96f && v.y > 0.04f && v.y < 0.96f, $"{e.id}: {t.name} is outside the camera frame ({v})");
                    Assert.IsFalse(Physics.Linecast(camera.transform.position, t.position + Vector3.up, 1 << WorldLayers.Obstacle), $"{e.id}: a solid object stands between the camera and {t.name}");
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator AClickOverAnyArenaHitsTheGroundAndAClickInTheHillsHitsScenery()
        {
            foreach (var arena in WorldLayout.Arenas)
                for (var x = 1f; x < arena.Length; x += 3f)
                    for (var z = -arena.Width / 2f + 1f; z < arena.Width / 2f; z += 3f)
                    {
                        var origin = arena.ToWorld(x, z) + Vector3.up * 40f;
                        Assert.IsTrue(Physics.Raycast(origin, Vector3.down, out var hit, 80f, WorldLayers.Hittable, QueryTriggerInteraction.Ignore), $"{arena.Id}: a click at ({x},{z}) hits nothing");
                        // the game places goblins in the arenas: a click may legitimately land on a CHARACTER (that is how they are targeted); it must never land on a canopy, a trunk or a wall
                        var layer = hit.collider.gameObject.layer;
                        Assert.IsTrue(layer == WorldLayers.Ground || layer == WorldLayers.Character, $"{arena.Id}: a click at ({x},{z}) hits {hit.collider.name} (layer {layer}), not the ground or a character");
                        if (layer == WorldLayers.Ground) Assert.AreEqual(0f, hit.point.y, 0.05f);
                    }

            var far = WorldLayout.PointAt(60f) + new Vector3(0, 0, 40f);
            Assert.IsTrue(Physics.Raycast(far + Vector3.up * 60f, Vector3.down, out var scenery, 120f, WorldLayers.Hittable, QueryTriggerInteraction.Ignore), "a click 40 m off the road hits nothing");
            Assert.AreEqual(WorldLayers.Scenery, scenery.collider.gameObject.layer);
            yield return null;
        }
    }
}
