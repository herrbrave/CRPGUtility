using System.Collections;
using System.IO;
using System.Linq;
using CRPG.Gameplay.Ambient;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using VikingRaid.Village;

namespace VikingRaid.Tests
{
    /// <summary>The ambient routines on the real village (design <c>Docs/Plans/Toolkit-NpcRoutines.md</c>): Orm works the dock, Hild and the elder wander. Time budgets are accumulated simulated seconds.
    /// <see cref="CaptureTheDockWorker"/> writes <c>VikingRaid_Logs/spikes/routine_*.png</c> so a human can judge the carry against the walk and the pick-up.</summary>
    public class VillageRoutineTests : VillageTestBase
    {
        private const string Sack = "Prop_Sack";

        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));

        private NpcRoutineRunner RunnerOf(string spawnId) => Village.Npcs[spawnId].GetComponent<NpcRoutineRunner>();
        private static bool Carrying(Component npc) => npc.GetComponentsInChildren<Transform>(true).Any(t => t.name == Sack);

        private CRPG.Gameplay.Scenes.NpcSpawnPoint PointOf(string spawnId) => Village.Location.SpawnPoints.First(p => p.SpawnId == spawnId);

        [UnityTest]
        public IEnumerator EveryAuthoredRoutineGetsARunner_AndStandersGetNone()
        {
            foreach (var point in Village.Location.SpawnPoints)
            {
                var runner = Village.Npcs[point.SpawnId].GetComponent<NpcRoutineRunner>();
                if (point.Routine.IsActive) Assert.IsNotNull(runner, $"{point.SpawnId} has a {point.Routine.Kind} routine but no runner");
                else Assert.IsNull(runner, $"{point.SpawnId} stands but has a runner");
            }

            Assert.IsNotNull(RunnerOf("village-orm"));
            yield return null;
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator TheVillagersLiveTheirRoutinesOnTheRealNavMesh()
        {
            Time.timeScale = 4f;   // a full dock loop takes about a minute at normal speed; BaseTearDown restores 1
            var orm = Village.Npcs["village-orm"];
            var hild = Village.Npcs["village-hild"];
            var elder = Village.Npcs["village-elder"];
            var ormStops = PointOf("village-orm").Routine.Stops.Select(s => s.Point.position).ToArray();
            var hildCentre = PointOf("village-hild").Anchor.position;
            var hildRadius = PointOf("village-hild").Routine.AreaRadius;
            var elderCentre = PointOf("village-elder").Anchor.position;
            var elderRadius = PointOf("village-elder").Routine.AreaRadius;

            Assert.IsFalse(Carrying(orm), "nothing in the hands at the start");
            var reached = new bool[ormStops.Length];
            var sawSack = false;
            var sackGoneAfterwards = false;
            float hildTravelled = 0f, elderTravelled = 0f, hildMax = 0f, elderMax = 0f;
            Vector3 lastHild = hild.transform.position, lastElder = elder.transform.position;

            var elapsed = 0f;
            while (elapsed < 120f && !(reached.All(r => r) && sackGoneAfterwards && hildTravelled > 3f && elderTravelled > 1f))
            {
                elapsed += Time.deltaTime;
                yield return null;

                for (var i = 0; i < ormStops.Length; i++) reached[i] |= Vector3.Distance(Flat(orm.transform.position), Flat(ormStops[i])) < 1.2f;
                var carrying = Carrying(orm);
                if (carrying) sawSack = true;
                else if (sawSack) sackGoneAfterwards = true;

                hildTravelled += Vector3.Distance(hild.transform.position, lastHild); lastHild = hild.transform.position;
                elderTravelled += Vector3.Distance(elder.transform.position, lastElder); lastElder = elder.transform.position;
                hildMax = Mathf.Max(hildMax, Vector3.Distance(Flat(hild.transform.position), Flat(hildCentre)));
                elderMax = Mathf.Max(elderMax, Vector3.Distance(Flat(elder.transform.position), Flat(elderCentre)));
            }

            Assert.IsTrue(reached.All(r => r), $"Orm never reached every dock stop ({string.Join(",", reached)})");
            Assert.IsTrue(sawSack, "Orm never carried the sack");
            Assert.IsTrue(sackGoneAfterwards, "Orm never put the sack down");
            Assert.Greater(hildTravelled, 3f, "Hild never wandered");
            Assert.LessOrEqual(hildMax, hildRadius + 1.5f, "Hild left her area");
            Assert.Greater(elderTravelled, 1f, "the elder never moved");
            Assert.LessOrEqual(elderMax, elderRadius + 1.5f, "the elder left his area");
            foreach (var npc in new[] { orm, hild, elder }) Assert.IsTrue(OnNavMesh(npc.transform.position, 1.5f), $"{npc.name} ended off the NavMesh");
            AssertNoErrorsLogged();
        }

        [UnityTest]
        public IEnumerator ARoutineNpcStopsForTheParty_WhenTheyWalkUpToTalkToIt()
        {
            var hild = Village.Npcs["village-hild"];
            var owner = new object();

            yield return Simulate(2f);
            Village.Session.Ambient.Suspend(hild.EntityId, owner);
            yield return Simulate(1.5f);   // let her come to rest
            var parked = hild.transform.position;
            yield return Simulate(8f);
            Assert.Less(Vector3.Distance(parked, hild.transform.position), 0.1f, "she kept wandering while held");

            Village.Session.Ambient.Resume(hild.EntityId, owner);
            var moved = false;
            var elapsed = 0f;
            while (elapsed < 40f && !moved)
            {
                elapsed += Time.deltaTime;
                yield return null;
                moved = Vector3.Distance(parked, hild.transform.position) > 1f;
            }

            Assert.IsTrue(moved, "she never resumed");
            AssertNoErrorsLogged();
        }

        private static IEnumerator Simulate(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds) { elapsed += Time.deltaTime; yield return null; }
        }

        // ---------------------------------------------------------------- the visual gate

        private IEnumerator Shoot(Transform subject, string name, Vector3 offset)
        {
            var go = new GameObject("RoutineCam");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(Cam);
            camera.depth = Cam.depth + 10;
            var extra = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var main = Cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (main != null) { extra.renderPostProcessing = main.renderPostProcessing; extra.antialiasing = main.antialiasing; }
            camera.fieldOfView = 35f; camera.nearClipPlane = 0.1f; camera.farClipPlane = 200f;
            var focus = subject.position + Vector3.up * 1.1f;
            go.transform.position = focus + subject.TransformDirection(offset);
            go.transform.LookAt(focus);

            var rt = new RenderTexture(640, 640, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = rt;
            var fog = RenderSettings.fog; RenderSettings.fog = false;
            for (var i = 0; i < 3; i++) yield return null;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, $"routine_{name}.png"), tex.EncodeToPNG());
            RenderSettings.fog = fog;
            camera.targetTexture = null;
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator CaptureTheDockWorker()
        {
            Time.timeScale = 2f;
            var orm = Village.Npcs["village-orm"];
            var machine = RunnerOf("village-orm").Machine;
            var shots = new System.Collections.Generic.HashSet<string>();
            var carryingSince = -1f;
            var elapsed = 0f;
            var lastPhase = machine.Current;
            var pickupStart = -1f;

            while (elapsed < 120f && shots.Count < 5)
            {
                elapsed += Time.deltaTime;
                yield return null;

                var phase = machine.Current;
                var carrying = Carrying(orm);
                if (phase == AmbientRoutineMachine.Phase.Acting && lastPhase != AmbientRoutineMachine.Phase.Acting && !carrying) pickupStart = elapsed;
                lastPhase = phase;

                if (phase == AmbientRoutineMachine.Phase.Acting && !carrying && pickupStart >= 0f && elapsed - pickupStart > 0.25f && shots.Add("pickup_bend"))
                    yield return Shoot(orm.transform, "pickup_bend", new Vector3(2.2f, 0.3f, 2.4f));
                if (carrying && carryingSince < 0f) carryingSince = elapsed;
                if (carrying && phase == AmbientRoutineMachine.Phase.Moving && elapsed - carryingSince > 1.0f)
                {
                    if (shots.Add("carry_walk_a")) yield return Shoot(orm.transform, "carry_walk_a", new Vector3(2.4f, 0.3f, 2.2f));
                    else if (elapsed - carryingSince > 1.6f && shots.Add("carry_walk_b")) yield return Shoot(orm.transform, "carry_walk_b", new Vector3(-2.4f, 0.3f, 2.2f));
                    else if (elapsed - carryingSince > 2.2f && shots.Add("carry_walk_front")) yield return Shoot(orm.transform, "carry_walk_front", new Vector3(0f, 0.4f, 3.2f));
                }

                if (!carrying) carryingSince = -1f;
                if (carrying && phase == AmbientRoutineMachine.Phase.Acting && shots.Add("drop")) yield return Shoot(orm.transform, "drop", new Vector3(2.2f, 0.3f, 2.4f));
            }

            Assert.GreaterOrEqual(shots.Count, 4, $"captured only: {string.Join(",", shots)}");
            AssertNoErrorsLogged();
        }
    }
}
