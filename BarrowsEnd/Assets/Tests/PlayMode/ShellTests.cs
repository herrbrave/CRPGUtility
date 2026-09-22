using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BarrowsEnd.Shell;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions.Adapters;
using CRPG.Gameplay.Scenes;
using CRPG.InputSystem;
using CRPG.Navigation;
using CRPG.Navigation.Grid;
using CRPG.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// Task 3.4: the campaign shell end to end, driven through the REAL click stack (the router InputSystemCommandSource builds, fed a camera ray exactly as
    /// OnClick does): boot into the town, walk, open the house door, travel to the cave and back through a real travel point, and save in the cave then load
    /// from the town (the cross-scene load). Grid, camera bounds, formation placement and every rebuilt consumer are checked at each step.
    /// </summary>
    public class ShellTests
    {
        private const string CorePath = "Assets/Scenes/Core.unity";
        private const string TownPath = "Assets/Scenes/Locations/Town_Slice.unity";
        private const string CavePath = "Assets/Scenes/Locations/Cave_Slice.unity";

        private ShellBootstrap shell;
        private string savePath;

        [UnitySetUp]
        public IEnumerator Boot()
        {
            savePath = Path.Combine(Path.GetTempPath(), $"barrowsend-shell-test-{Guid.NewGuid():N}.json");
            ShellBootstrap.StartModeOverride = ShellStartMode.AuthoredParty; // these tests boot straight into the town: set BEFORE Core loads, cleared in Cleanup
            yield return SceneManager.LoadSceneAsync(CorePath, LoadSceneMode.Single);
            shell = UnityEngine.Object.FindFirstObjectByType<ShellBootstrap>();
            Assert.IsNotNull(shell);
            shell.SavePath = savePath;
            yield return WaitUntil(() => shell.IsReady, 20f, "the shell finished booting");
            yield return Seconds(0.2f);
        }

        [TearDown]
        public void Cleanup()
        {
            ShellBootstrap.StartModeOverride = null;
            if (File.Exists(savePath)) File.Delete(savePath);
            Time.timeScale = 1f;
        }

        private static IEnumerator Seconds(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Mathf.Min(Time.deltaTime, 0.1f);
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < seconds)
            {
                elapsed += Mathf.Min(Time.deltaTime, 0.1f);
                yield return null;
            }

            Assert.IsTrue(condition(), $"timed out waiting for: {what}");
        }

        private GameSession Session => shell.Services.Session;
        private RuntimeEntityId Member(int i) => Session.Party.Members[i];
        private CharacterAgent Agent(RuntimeEntityId id) => CharacterAgent.AllLive.First(a => a != null && a.EntityId == id);
        private static Vector3 Centre(int x, int z) => new(x + 0.5f, 0f, z + 0.5f);

        private void Click(Vector3 world)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<InputSystemCommandSource>();
            var router = typeof(InputSystemCommandSource).GetProperty("Router", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
            var routerType = router.GetType();
            var cam = Camera.main;
            var screen = (Vector2)cam.WorldToScreenPoint(world);
            var ray = cam.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            routerType.GetMethod("HandlePointerDown").Invoke(router, new object[] { screen, ray });
            routerType.GetMethod("HandlePointerUp").Invoke(router, new object[] { screen, ray, cam, false });
        }

        private static WalkabilityGrid ActiveGrid(ShellBootstrap shell) => shell.Pathfinding.ActiveGrid;

        private Rect CameraBounds() => WalkabilityGridProvider.FindInScene(SceneManager.GetActiveScene()) is { } provider
            ? new Rect(provider.Grid.Origin.x, provider.Grid.Origin.z, provider.Grid.Width, provider.Grid.Depth)
            : default;

        private void AssertPartyStandsOnWalkableDistinctCellsInTheActiveGrid()
        {
            var grid = ActiveGrid(shell);
            var cells = Session.Party.Members.Select(m => grid.ToGrid(Agent(m).transform.position)).ToList();
            foreach (var cell in cells) Assert.IsTrue(grid.IsWalkable(cell), $"a member stands on a blocked cell {cell.X},{cell.Z}");
            Assert.AreEqual(cells.Count, cells.Distinct().Count(), "no two members share a cell");
        }

        private void AssertCameraInsideTheLocation()
        {
            var bounds = CameraBounds();
            var cam = Camera.main;
            var half = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
            var focus = shell.CameraRig.Rig.FocusPoint;
            // The visible rectangle stays inside the map, or the map is smaller than the view and the camera centres on it.
            if (bounds.width > 2f * half.x) Assert.That(focus.x, Is.InRange(bounds.xMin + half.x - 0.1f, bounds.xMax - half.x + 0.1f), "camera x inside the map");
            else Assert.AreEqual(bounds.center.x, focus.x, 0.1f, "centred on a map narrower than the view");
            if (bounds.height > 2f * half.y) Assert.That(focus.z, Is.InRange(bounds.yMin + half.y - 0.1f, bounds.yMax - half.y + 0.1f), "camera z inside the map");
            else Assert.AreEqual(bounds.center.y, focus.z, 0.1f, "centred on a map shallower than the view");
        }

        /// <summary>Pushes the camera far past the map corner and checks it stops at the ACTIVE location's edge (not a stale one from the previous map).</summary>
        private IEnumerator AssertCameraClampsToTheActiveLocation()
        {
            var bounds = CameraBounds();
            var rig = shell.CameraRig.Rig;
            var before = rig.FocusPoint;
            rig.SnapTo(new Vector3(bounds.xMax + 500f, 0f, bounds.yMax + 500f));
            yield return null;
            var half = rig.HalfExtents;
            Assert.AreEqual(bounds.xMax - half.x, rig.FocusPoint.x, 0.2f, "the far x edge is THIS map's edge");
            Assert.AreEqual(bounds.yMax - half.y, rig.FocusPoint.z, 0.2f, "the far z edge is THIS map's edge");
            rig.SnapTo(before);
        }

        // ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BootsIntoTheTownWithTheWholePartyPlacedInFormationAndTheTownsGridAndBoundsActive()
        {
            Assert.AreEqual(4, Session.Party.Members.Count);
            Assert.AreEqual(RuntimeEntityId.FromString("ba000000000000000000000000000001"), Member(0), "fixed party ids, so a save loads after a restart");
            Assert.AreEqual(RuntimeEntityId.FromString("ba000000000000000000000000000004"), Member(3));
            var core = shell.gameObject.scene;
            foreach (var member in Session.Party.Members)
                Assert.AreEqual(core, Agent(member).gameObject.scene, "party members live in the persistent Core scene");

            Assert.AreEqual(TownPath, SceneManager.GetActiveScene().path);
            Assert.IsTrue(SceneManager.GetSceneByPath(TownPath).isLoaded);
            var townGrid = WalkabilityGridProvider.FindInScene(SceneManager.GetSceneByPath(TownPath)).Grid;
            Assert.AreSame(townGrid, ActiveGrid(shell), "the pathfinder was pointed at the town's grid");

            AssertPartyStandsOnWalkableDistinctCellsInTheActiveGrid();
            var start = FindDestination(TownPath, "start");
            foreach (var member in Session.Party.Members)
                Assert.Less(Vector3.Distance(Agent(member).transform.position, start.Anchor.position), 4f, "formed up around the start anchor");
            AssertCameraInsideTheLocation();
            yield return AssertCameraClampsToTheActiveLocation();

            var cam = Camera.main;
            Assert.IsTrue(cam.orthographic);
            Assert.AreEqual(90f, cam.transform.eulerAngles.x, 0.01f);
            Assert.AreEqual(Session.Party.Members[0], Session.Party.ActiveCharacter);
        }

        private static SceneTravelDestination FindDestination(string scenePath, string id) =>
            UnityEngine.Object.FindObjectsByType<SceneTravelDestination>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(d => d.gameObject.scene.path == scenePath && d.DestinationId == id);

        [UnityTest]
        public IEnumerator ClickingGroundWalksTheSelectedMemberThereAndTheCameraFollows()
        {
            var agent = Agent(Member(0));
            var target = Centre(14, 8);

            Click(target);
            yield return WaitUntil(() => Vector3.Distance(agent.transform.position, target) < 0.6f, 20f, "the member reached the clicked cell");

            yield return Seconds(1.5f); // let the smoothed camera settle
            Assert.Less(Vector2.Distance(new Vector2(shell.CameraRig.Rig.FocusPoint.x, shell.CameraRig.Rig.FocusPoint.z), new Vector2(target.x, target.z)), 1.5f, "the camera followed");
            AssertCameraInsideTheLocation();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheHouseDoorBlocksUntilClickedOpenThenTheMemberWalksIn()
        {
            var agent = Agent(Member(0));
            var inside = Centre(6, 13);
            var door = UnityEngine.Object.FindObjectsByType<UseDoorInteractionAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();

            Click(inside);
            yield return Seconds(12f);
            Assert.Less(agent.transform.position.z, 11f, "closed door: the wall keeps the member outside the house");
            Assert.IsFalse(door.IsOpen);

            Click(door.transform.position);
            yield return WaitUntil(() => door.IsOpen, 25f, "the member walked to the door and opened it");

            Click(inside);
            yield return WaitUntil(() => Vector3.Distance(agent.transform.position, inside) < 0.7f, 25f, "the member walked into the house");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClickingTheTravelPointTakesThePartyToTheCaveAndBackWithGridBoundsAndFormationSwapped()
        {
            yield return TravelTo(CavePath, Centre(14, 19));

            var caveGrid = WalkabilityGridProvider.FindInScene(SceneManager.GetSceneByPath(CavePath)).Grid;
            Assert.AreSame(caveGrid, ActiveGrid(shell), "the pathfinder now uses the cave's grid");
            Assert.IsFalse(SceneManager.GetSceneByPath(TownPath).isLoaded, "the town was unloaded");
            foreach (var member in Session.Party.Members)
                Assert.AreEqual(shell.gameObject.scene, Agent(member).gameObject.scene, "the party stayed in the Core scene through the travel");

            AssertPartyStandsOnWalkableDistinctCellsInTheActiveGrid();
            var arrival = FindDestination(CavePath, "from-town");
            foreach (var member in Session.Party.Members)
                Assert.Less(Vector3.Distance(Agent(member).transform.position, arrival.Anchor.position), 4f, "arrived around the cave's from-town anchor");
            yield return Seconds(1f);
            AssertCameraInsideTheLocation();
            yield return AssertCameraClampsToTheActiveLocation();

            // and back
            yield return TravelTo(TownPath, Centre(12, 0));
            Assert.AreSame(WalkabilityGridProvider.FindInScene(SceneManager.GetSceneByPath(TownPath)).Grid, ActiveGrid(shell));
            Assert.IsFalse(SceneManager.GetSceneByPath(CavePath).isLoaded);
            AssertPartyStandsOnWalkableDistinctCellsInTheActiveGrid();
            var back = FindDestination(TownPath, "from-cave");
            foreach (var member in Session.Party.Members)
                Assert.Less(Vector3.Distance(Agent(member).transform.position, back.Anchor.position), 4f);
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator TravelTo(string scenePath, Vector3 travelPoint)
        {
            Click(travelPoint);
            yield return WaitUntil(() => SceneManager.GetActiveScene().path == scenePath && !shell.Gate.IsBusy, 60f, $"travel to {scenePath}");
            yield return Seconds(0.5f);
        }

        [UnityTest]
        public IEnumerator SavingInTheCaveThenLoadingFromTheTownRestoresTheCaveAndReboundsEverything()
        {
            yield return TravelTo(CavePath, Centre(14, 19));
            var walker = Agent(Member(0));
            var target = Centre(14, 8);
            Click(target);
            yield return WaitUntil(() => Vector3.Distance(walker.transform.position, target) < 0.7f, 30f, "the member walked into the cave");
            var savedPositions = Session.Party.Members.Select(m => Agent(m).transform.position).ToList();
            var sessionBefore = Session;
            Assert.IsTrue(shell.SaveNow());

            yield return TravelTo(TownPath, Centre(12, 0));
            Assert.AreEqual(TownPath, SceneManager.GetActiveScene().path);

            LoadOutcome? outcome = null;
            shell.LoadNow(o => outcome = o);
            yield return WaitUntil(() => outcome.HasValue, 60f, "the load finished");
            yield return Seconds(0.5f);

            Assert.AreEqual(LoadOutcome.Success, outcome, shell.LastMessage);
            Assert.AreEqual(CavePath, SceneManager.GetActiveScene().path, "a cross-scene load put us back in the cave");
            Assert.IsFalse(SceneManager.GetSceneByPath(TownPath).isLoaded);
            Assert.AreNotSame(sessionBefore, Session, "a brand-new session");
            Assert.AreEqual(2, shell.Services.RebindCount, "the session consumers were rebuilt exactly once");
            Assert.AreEqual(4, CharacterAgent.AllLive.Count(a => a != null), "four members, none duplicated");
            Assert.AreSame(WalkabilityGridProvider.FindInScene(SceneManager.GetSceneByPath(CavePath)).Grid, ActiveGrid(shell), "the cave's grid is active again");

            for (var i = 0; i < savedPositions.Count; i++)
                Assert.Less(Vector3.Distance(Agent(Member(i)).transform.position, savedPositions[i]), 0.1f, $"member {i} is back where it was saved");
            AssertPartyStandsOnWalkableDistinctCellsInTheActiveGrid();
            AssertCameraInsideTheLocation();
            yield return AssertCameraClampsToTheActiveLocation();

            // The rebuilt click stack works against the new session and the new scene.
            var next = Agent(Member(0));
            var goal = Centre(14, 10);
            Click(goal);
            yield return WaitUntil(() => Vector3.Distance(next.transform.position, goal) < 0.7f, 30f, "movement works after the cross-scene load");

            // And travel still works after a load (a fresh travel coordinator was built).
            yield return TravelTo(TownPath, Centre(12, 0));
            Assert.AreEqual(TownPath, SceneManager.GetActiveScene().path);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
