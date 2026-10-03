using System.Linq;
using System.Reflection;
using BarrowsEnd.Editor;
using BarrowsEnd.Gate;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarrowsEnd.Tests
{
    /// <summary>Task 6.3: the placement rules from the plan's review, pinned against the authored <c>Cavern_Deep.unity</c>.
    /// Volumes pass through walls and bars, a spawn sample has no connectivity check, and the Middle and Deep scenes share world
    /// coordinates while the party travels -- so these distances are what keeps the arena sealed, the boss ranged, and the
    /// arrival quiet.</summary>
    public class DeepCavernContentTests
    {
        private Scene scene;

        [OneTimeSetUp]
        public void Open() => scene = EditorSceneManager.OpenScene(BarrowsEndCavernsAuthor.DeepPath, OpenSceneMode.Additive);

        [OneTimeTearDown]
        public void Close() => EditorSceneManager.CloseScene(scene, true);

        private static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private EncounterTrigger[] Encounters() =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<EncounterTrigger>(true)).ToArray();

        private static float Radius(Component c) => c.GetComponent<SphereCollider>().radius * Mathf.Max(c.transform.lossyScale.x, c.transform.lossyScale.z);

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        private PortcullisGate Gate() => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PortcullisGate>(true)).Single();

        [Test]
        public void TheDeepLevelHasTwoThrallPatrolsABossArenaALeverAndAPortcullisSharingOneLeverKey()
        {
            var encounters = Encounters();
            CollectionAssert.AreEquivalent(new[] { "ThrallPatrolSouth", "ThrallPatrolNorth", "NecromancerArena" }, encounters.Select(e => e.name).ToArray());
            Assert.AreEqual(BarrowsEndCavernsAuthor.DeepLeverKey, Gate().OpenKey);
            var lever = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LeverInteractionAdapter>(true)).Single();
            Assert.AreEqual(BarrowsEndCavernsAuthor.DeepLeverKey, lever.LeverKey);

            var arena = encounters.Single(e => e.name == "NecromancerArena");
            Assert.AreEqual(BarrowsEndCavernsAuthor.NecromancerDefeatedKey, arena.WonKey, "the quest's boss objective tracks the arena's win key");
            Assert.AreEqual(BarrowsEndCavernsAuthor.DeepLeverKey, Field<string>(arena, "requiredKey"), "the arena cannot fire before the gate is open");
            Assert.AreEqual("npc-necromancer", Field<string>(arena, "npcDefinitionId"));
            Assert.AreEqual("npc-skeleton-thrall", Field<string>(arena, "secondaryNpcDefinitionId"));
            Assert.AreEqual(2, Field<int>(arena, "secondaryCount"));
            Assert.IsNotNull(Field<Transform>(arena, "spawnAnchor"), "the boss spawns at the ritual circle, not on the party");

            foreach (var patrol in encounters.Where(e => e.name.StartsWith("ThrallPatrol")))
                Assert.AreEqual(BarrowsEndCavernsAuthor.ThrallClearedProgressKey, Field<string>(patrol, "progressKey"), $"{patrol.name} feeds the quest's shared thrall counter");
        }

        [Test]
        public void EveryTriggerSitsFarEnoughFromThePortcullisThatNoSpawnSampleCanLandInTheSealedArena()
        {
            var gate = Gate();
            var cells = gate.transform.Cast<Transform>().Select(t => t.position).ToArray();
            Assert.IsNotEmpty(cells);

            foreach (var trigger in Encounters())
            {
                var anchor = Field<Transform>(trigger, "spawnAnchor");
                var scatter = Field<float>(trigger, "spawnScatter");
                var spawnCentre = anchor != null ? anchor.position : trigger.transform.position;
                var south = trigger.name != "NecromancerArena"; // the two patrols spawn on the OUTER side of the gate
                foreach (var cell in cells)
                {
                    // A spawn sample can land up to 2x the scatter from its centre (see EncounterTrigger.Fire).
                    Assert.GreaterOrEqual(Flat(spawnCentre, cell), 2f * scatter, $"{trigger.name}'s spawn centre is at least 2x its scatter from gate cell {cell}");
                    if (south) Assert.Less(trigger.transform.position.z, cell.z, $"{trigger.name} is south of (outside) the gate");
                    else Assert.Greater(trigger.transform.position.z, cell.z, $"{trigger.name} is north of (inside) the gate");
                }
            }
        }

        [Test]
        public void TheArenaTriggerStartsInsideTheArenaAndTheBossSpawnsFarEnoughAwayToBeRanged()
        {
            var arena = Encounters().Single(e => e.name == "NecromancerArena");
            var anchor = Field<Transform>(arena, "spawnAnchor");
            foreach (var cell in Gate().transform.Cast<Transform>())
                Assert.GreaterOrEqual(Flat(arena.transform.position, cell.position), Radius(arena) + 1f, "volumes pass through bars: the arena sphere must not reach the gate row");

            Assert.GreaterOrEqual(Flat(anchor.position, arena.transform.position) - Radius(arena), 3f,
                "the boss spawns well beyond the zone's edge, so the party has to advance into bolt range");
        }

        [Test]
        public void NoDeepVolumeReachesTheArrivalPointOrMiddlesArchApproachWhichBothScenesShareWhileTheyTravel()
        {
            var arrival = new Vector3(17.5f, 0f, 3.5f);                       // Deep's "from-middle" destination
            var middleArchApproach = new Vector3(18.5f, 0f, 26.5f);           // where the party stands in Middle while Deep is loaded additively

            foreach (var trigger in Encounters())
            {
                Assert.Greater(Flat(trigger.transform.position, arrival), Radius(trigger), $"{trigger.name} must not fire on arrival");
                Assert.Greater(Flat(trigger.transform.position, middleArchApproach), Radius(trigger) + 1f, $"{trigger.name} must stay clear of Middle's arch approach");
            }
        }
    }
}
