using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Presentation.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>
    /// How the roster is oriented and animated (the user: "click to move works well, but they don't seem to be facing the correct direction" and "their run animations are synced"). Measured
    /// on the running game: the borrowed jog and idle clips used to leave the whole body yawed 30 (jog) / 19 (goblin idle) degrees off the root, and every loop started at time zero so four
    /// heroes jogged in lockstep. Now: the hip line faces the direction of travel in every state, a stopped hero faces the way the party was sent, and run cycles are spread apart.
    /// </summary>
    public class GameFacingTests : GameTestBase
    {
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        /// <summary>The direction the body faces, from the hip line (signed yaw relative to the root's forward).</summary>
        private static float HipYaw(GameObject go)
        {
            var animator = go.GetComponentsInChildren<Animator>(true).First(a => a.avatar != null);
            var side = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            side.y = 0f;
            return Vector3.SignedAngle(Flat(go.transform.forward), Vector3.Cross(side.normalized, Vector3.up).normalized, Vector3.up);
        }

        private static Vector3 HipForward(GameObject go) => Quaternion.AngleAxis(HipYaw(go), Vector3.up) * Flat(go.transform.forward);

        private IEnumerator Frames(int count) { for (var i = 0; i < count; i++) yield return null; }

        [UnityTest]
        public IEnumerator EveryRosterPrefabsBodyFacesItsDirectionOfTravelWhileJoggingAndForwardWhenIdle()
        {
            var names = new[] { "Hero_Bjorn", "Hero_Ulf", "Hero_Sigrid", "Hero_Astrid", "Goblin_Grunt_A", "Goblin_Warrior_A", "Goblin_Archer_M", "Goblin_Shaman", "Goblin_Troll", "Goblin_King" };
            var origin = WorldLayout.PointAt(40f); var forward = WorldLayout.TangentAt(40f);
            var failures = new List<string>();
            for (var n = 0; n < names.Length; n++)
            {
                GameObject prefab = null;
#if UNITY_EDITOR
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Characters/Prefabs/{names[n]}.prefab");
#endif
                var spot = origin + new Vector3(forward.z, 0, -forward.x) * (n - 5) * 1.6f;
                var go = Object.Instantiate(prefab, spot, Quaternion.LookRotation(forward));
                yield return Frames(3);
                var agent = go.GetComponent<NavMeshAgent>();
                if (!agent.isOnNavMesh) agent.Warp(spot);

                yield return Frames(40);                                   // idle, with the correction eased in
                var idle = Enumerable.Range(0, 20).Select(_ => HipYaw(go)).ToList();

                agent.SetDestination(spot + forward * 14f);
                var moving = new List<float>();
                var until = Time.realtimeSinceStartup + 6f;
                for (var i = 0; Time.realtimeSinceStartup < until && i < 200; i++)
                {
                    if (i > 45 && agent.velocity.magnitude > 1.5f) moving.Add(HipYaw(go));   // up to speed, correction settled
                    yield return null;
                }

                if (moving.Count < 10) failures.Add($"{names[n]}: never got up to speed ({moving.Count} samples)");
                else if (Mathf.Abs(moving.Average()) > 7f) failures.Add($"{names[n]}: runs with its body {moving.Average():F0} degrees off its direction of travel");
                if (Mathf.Abs(idle.Average()) > 7f) failures.Add($"{names[n]}: stands with its body {idle.Average():F0} degrees off forward");
                Object.Destroy(go);
                yield return null;
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [UnityTest]
        public IEnumerator AfterAClickTheStoppedHeroesAllFaceTheWayThePartyWasSent()
        {
            Time.timeScale = 4f;
            var target = WorldLayout.PointAt(WorldLayout.Arenas[0].WestS - 40f);
            Click(target);
            yield return WaitUntil(() => Vector3.Distance(PartyCentre(), target) < 3.5f && Game.Heroes.All(h => VikingGame.FindAgent(h).Motor.Snapshot.Velocity.magnitude < 0.1f), 60f, "the party to arrive and stop");
            Time.timeScale = 1f;
            yield return Frames(120);                                     // the turn to face is eased (360 degrees a second)
            var sent = Game.LastOrderFacing;
            Assert.Greater(sent.magnitude, 0.5f);
            foreach (var hero in Game.Heroes)
            {
                var agent = VikingGame.FindAgent(hero);
                Assert.Less(Vector3.Angle(Flat(agent.transform.forward), sent), 12f, $"a stopped hero must face the way the party was sent (his root, {Vector3.Angle(Flat(agent.transform.forward), sent):F0} degrees off)");
                Assert.Less(Vector3.Angle(HipForward(agent.gameObject), sent), 25f, "...and so must his body");
            }
        }

        [UnityTest]
        public IEnumerator TheHeroesJogOutOfStepAndTheGoblinsDoNotBreatheInUnison()
        {
            Time.timeScale = 2f;
            Click(WorldLayout.PointAt(WorldLayout.Arenas[0].WestS - 40f));
            yield return WaitUntil(() => Game.Heroes.All(h => VikingGame.FindAgent(h).Motor.Snapshot.Velocity.magnitude > 3f), 20f, "everyone to be jogging");
            Time.timeScale = 1f;
            yield return Frames(30);

            var phases = Game.Heroes.Select(h => AnimationPhaseOffset.CurrentCyclePosition(VikingGame.FindAgent(h).GetComponentInChildren<CharacterPlayableAnimator>(true))).ToList();
            Assert.IsTrue(phases.All(p => p >= 0f), $"cycle positions unreadable ({string.Join(", ", phases)}): the toolkit animator no longer exposes its driver as AnimationPhaseOffset expects");
            float Gap(float a, float b) { var d = Mathf.Abs(a - b); return Mathf.Min(d, 1f - d); }
            for (var i = 0; i < 4; i++)
                for (var j = i + 1; j < 4; j++)
                    Assert.Greater(Gap(phases[i], phases[j]), 0.08f, $"heroes {i} and {j} are in step (cycle positions {phases[i]:F2} and {phases[j]:F2})");

            var goblins = Game.GoblinsOf(0).Select(g => AnimationPhaseOffset.CurrentCyclePosition(VikingGame.FindAgent(g.Id).GetComponentInChildren<CharacterPlayableAnimator>(true))).ToList();
            Assert.IsTrue(goblins.All(p => p >= 0f));
            for (var i = 0; i < goblins.Count; i++)
                for (var j = i + 1; j < goblins.Count; j++)
                    Assert.Greater(Gap(goblins[i], goblins[j]), 0.05f, $"idle goblins {i} and {j} breathe in unison ({goblins[i]:F2}, {goblins[j]:F2})");
        }
    }
}
