using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VikingRaid.Game;
using VikingRaid.Village;

namespace VikingRaid.Tests
{
    /// <summary>Shared fixture for the village scene: loads the REAL authored scene with a REAL <see cref="PlayerCommandRouter"/> injected as the input (so clicks go through the hittable mask and
    /// the collider layers exactly as in play), and collects every logged error.</summary>
    public abstract class VillageTestBase
    {
        public const string ScenePath = "Assets/Scenes/VikingVillage.unity";

        private sealed class NoUiCapture : IPointerCaptureProvider { public bool IsPointerOverUI(Vector2 screenPosition) => false; }

        protected VillageBootstrap Village;
        protected Camera Cam;
        protected PlayerCommandRouter Router;
        private readonly List<string> loggedErrors = new();

        private void OnLog(string condition, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) loggedErrors.Add($"{type}: {condition}"); }
        protected void AssertNoErrorsLogged() => Assert.IsEmpty(loggedErrors, string.Join("\n", loggedErrors));

        [UnitySetUp]
        public IEnumerator BaseSetUp()
        {
            Time.timeScale = 1f;
            loggedErrors.Clear();
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            LogAssert.ignoreFailingMessages = true;
            VillageBootstrap.InputFactory = () => Router = new PlayerCommandRouter(new SceneSelectableSource(), new NoUiCapture(), WorldLayers.Hittable);

#if UNITY_EDITOR
            Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(ScenePath), $"{ScenePath} does not exist: run 'Viking Raid/Village/1. Author Village Scene'");
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            var until = Time.realtimeSinceStartup + 20f;
            while ((VillageBootstrap.Instance == null || VillageBootstrap.Instance.Session == null) && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsNotNull(VillageBootstrap.Instance?.Session, "the village never booted");
            Village = VillageBootstrap.Instance;
            Village.Seed = 12345;
            Cam = Camera.main;
            yield return null; yield return null;   // let the spawned agents bind and the camera settle
        }

        [TearDown]
        public void BaseTearDown()
        {
            VillageBootstrap.InputFactory = null;
            Time.timeScale = 1f;
            Application.logMessageReceived -= OnLog;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTearDown]
        public IEnumerator UnloadAfterTest() => SceneIsolation.UnloadAll();

        protected static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
        {
            var start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds) Assert.Fail($"timed out after {timeoutSeconds:F0}s waiting for {what}");
                yield return null;
            }
        }

        protected void Click(Vector3 worldPoint)
        {
            var screen = (Vector2)Cam.WorldToScreenPoint(worldPoint);
            var ray = Cam.ScreenPointToRay(screen);
            Router.HandlePointerDown(screen, ray);
            Router.HandlePointerUp(screen, ray, Cam, false);
        }

        protected bool OnScreen(Vector3 world)
        {
            var v = Cam.WorldToViewportPoint(world);
            return v.z > 0f && v.x > 0.15f && v.x < 0.85f && v.y > 0.15f && v.y < 0.85f;
        }

        /// <summary>True when a click at the point on screen would resolve to (about) that point: nothing on the hittable layers (a hill, a cliff) stands between the camera and it.</summary>
        protected bool ClickWouldLandOn(Vector3 world)
        {
            var ray = Cam.ScreenPointToRay(Cam.WorldToScreenPoint(world));
            return Physics.Raycast(ray, out var hit, 1000f, WorldLayers.Hittable, QueryTriggerInteraction.Ignore) && Vector3.Distance(Flat(hit.point), Flat(world)) < 1.5f;
        }

        protected static Vector3 Position(RuntimeEntityId id) => VillageBootstrap.FindAgent(id).transform.position;
        protected Vector3 PartyCentre() => Village.Party.Aggregate(Vector3.zero, (sum, id) => sum + Position(id)) / Village.Party.Count;
        protected static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        protected static bool OnNavMesh(Vector3 p, float tolerance = 1f) => NavMesh.SamplePosition(p, out var hit, tolerance, NavMesh.AllAreas) && Vector3.Distance(Flat(hit.position), Flat(p)) < tolerance;

        /// <summary>A walkable point 6-12 m from the party that the camera can see and the party can reach, so a click on it is a real order.</summary>
        protected Vector3 VisibleReachablePoint()
        {
            var from = PartyCentre();
            var path = new NavMeshPath();
            for (var distance = 8f; distance <= 14f; distance += 2f)
                for (var angle = 0; angle < 360; angle += 30)
                {
                    var wanted = from + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * distance;
                    if (!NavMesh.SamplePosition(wanted, out var hit, 2f, NavMesh.AllAreas) || !OnScreen(hit.position) || !ClickWouldLandOn(hit.position)) continue;
                    if (NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) return hit.position;
                }

            Assert.Fail("no visible, reachable point near the party: is the NavMesh baked and is the camera on the party?");
            return default;
        }
    }

    public class VillageSceneTests : VillageTestBase
    {
        [UnityTest]
        public IEnumerator TheVillageBootsWithTheFullPartyAndTheAuthoredNpcsAllStandingOnTheNavMesh()
        {
            Assert.AreEqual(4, Village.Party.Count, "the four heroes");
            Assert.AreEqual(4, Village.Session.Party.Members.Count);
            foreach (var id in Village.Party)
            {
                Assert.IsNotNull(VillageBootstrap.FindAgent(id), "a party member has no live agent");
                Assert.IsTrue(OnNavMesh(Position(id), 1.5f), $"a hero spawned off the NavMesh at {Position(id)}");
            }

            var points = Village.Location.SpawnPoints;
            Assert.Greater(points.Count, 0, "the location has no NPC spawn points: the author adds three demo villagers on its first run");
            Assert.AreEqual(points.Count, Village.Npcs.Count, "every spawn point must produce an NPC");
            foreach (var point in points)
            {
                Assert.IsNotNull(point.Definition, $"{point.SpawnId} has no NpcDefinition");
                Assert.IsNotNull(point.Anchor, $"{point.SpawnId} has no anchor");
                var agent = Village.Npcs[point.SpawnId];
                Assert.IsNotNull(agent);
                Assert.IsTrue(Village.Session.Characters.TryGetCharacter(agent.EntityId, out _), $"{point.SpawnId} is not a registered character");
                Assert.IsTrue(OnNavMesh(agent.transform.position, 1.5f), $"{point.SpawnId} stands off the NavMesh at {agent.transform.position}");
            }

            Assert.AreEqual(points.Select(p => p.SpawnId).Distinct().Count(), points.Count, "spawn ids must be unique");
            yield return null;
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ThePartyWalksWhereYouClickAndTheOrderIsFacedTowardTheTravelDirection()
        {
            Time.timeScale = 4f;
            var start = PartyCentre();
            var target = VisibleReachablePoint();
            Assert.AreEqual(Vector3.zero, Village.Exploration.LastOrderFacing, "no order yet");

            Click(target);
            yield return WaitUntil(() => Vector3.Distance(Flat(PartyCentre()), Flat(target)) < 3f, 40f, "the party to reach the clicked point");

            var wanted = Flat(target - start).normalized;
            Assert.Greater(Vector3.Dot(Village.Exploration.LastOrderFacing, wanted), 0.9f, "the order's facing must point from the party toward the click, not world forward");
            foreach (var id in Village.Party) Assert.IsTrue(OnNavMesh(Position(id), 1.5f), "a hero ended off the NavMesh");
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ClickingAVillagerDoesNotThrowAndLeavesThePartySelected()
        {
            var villager = Village.Npcs.Values.First();
            Assert.AreEqual(4, Village.Session.Party.Selected.Count);

            // a ray straight down onto the villager from above: the router selects the nearest CharacterAgent under it
            var ray = new Ray(villager.transform.position + Vector3.up * 5f, Vector3.down);
            var screen = (Vector2)Cam.WorldToScreenPoint(villager.transform.position + Vector3.up);
            Router.HandlePointerDown(screen, ray);
            Assert.DoesNotThrow(() => Router.HandlePointerUp(screen, ray, Cam, false));
            yield return null;

            Assert.AreEqual(4, Village.Session.Party.Selected.Count, "a click on a non-party character must not change the party's selection");
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator TheToolkitCameraStartsOnAndFollowsTheParty()
        {
            Time.timeScale = 4f;
            Assert.IsNotNull(Village.CameraRig, "the scene has no IsometricCameraRig");
            Assert.Less(Vector3.Distance(Flat(Village.CameraRig.Rig.FocusPoint), Flat(PartyCentre())), 3f, "the camera starts on the party");

            Click(VisibleReachablePoint());
            yield return WaitUntil(() => Vector3.Distance(Flat(PartyCentre()), Flat(Village.PartyStart.position)) > 4f, 30f, "the party to leave the start");
            var until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until && Vector3.Distance(Flat(Village.CameraRig.Rig.FocusPoint), Flat(PartyCentre())) > 3f) yield return null;
            Assert.Less(Vector3.Distance(Flat(Village.CameraRig.Rig.FocusPoint), Flat(PartyCentre())), 3f, "the camera follows the party as it walks");
        }

        [UnityTest]
        public IEnumerator TheWalkableGroundIsOneLargeConnectedAreaNotAScatteringOfIslands()
        {
            var ground = Village.Location.CameraBoundsXZ.Value;
            var from = Village.PartyStart.position;
            var path = new NavMeshPath();
            var samples = 0; var reachable = 0;
            for (var x = ground.xMin; x <= ground.xMax; x += 15f)
                for (var z = ground.yMin; z <= ground.yMax; z += 15f)
                {
                    if (!NavMesh.SamplePosition(new Vector3(x, from.y, z), out var hit, 6f, NavMesh.AllAreas)) continue;
                    samples++;
                    if (NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) reachable++;
                }

            Debug.Log($"[Village] {reachable} of {samples} walkable sample points are reachable from the party start");
            Assert.Greater(samples, 20, "almost no walkable ground was found: is the NavMesh baked?");
            Assert.Greater(reachable / (float)samples, 0.6f, $"only {reachable} of {samples} walkable points are reachable from the party start: the NavMesh is fragmented (gaps between ground tiles, or obstacles walling the start in)");
            yield return null;
        }
    }
}
