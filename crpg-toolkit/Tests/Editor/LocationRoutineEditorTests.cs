using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Location;
using CRPG.Editor.Validation;
using CRPG.Gameplay.Ambient;
using CRPG.Gameplay.Scenes;
using CRPG.Navigation;
using CRPG.Presentation.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CRPG.Tests.EditMode
{
    /// <summary>The editor half of NPC routines: the validator rules, the placement helpers (a new spawn point must not inherit the previous NPC's routine; Add stop), the drawer's sections and the gizmo helpers.
    /// The interactive Scene-view drawing is checked by hand.</summary>
    public sealed class LocationRoutineEditorTests
    {
        private sealed class FakeNav : INavMeshService
        {
            public Func<Vector3, bool> Walkable = _ => true;
            public Func<Vector3, Vector3, bool> Reach = (_, _) => true;
            public bool TrySamplePosition(Vector3 point, out Vector3 sampledPosition, float maxDistance = 5f, int areaMask = NavMesh.AllAreas) { sampledPosition = point; return Walkable(point); }
            public bool TryCalculatePathLength(Vector3 from, Vector3 to, out float length, int areaMask = NavMesh.AllAreas) { length = 0f; return Reach(from, to); }
            public bool TryCalculatePath(Vector3 from, Vector3 to, out Vector3[] corners, int areaMask = NavMesh.AllAreas) { corners = Array.Empty<Vector3>(); return Reach(from, to); }
        }

        private readonly List<UnityEngine.Object> made = new();
        private LocationContent location;
        private GameObject visual;

        [SetUp]
        public void SetUp()
        {
            visual = new GameObject("visual");
            made.Add(visual);
            // A NavMesh-driven body, as a routine NPC needs.
            visual.AddComponent<NavMeshAgent>();
            visual.AddComponent<NavMeshCharacterMotor>();

            var root = new GameObject("CRPG");
            made.Add(root);
            var host = new GameObject("Location");
            host.transform.SetParent(root.transform, false);
            location = host.AddComponent<LocationContent>();
        }

        [TearDown]
        public void TearDown()
        {
            Selection.objects = Array.Empty<UnityEngine.Object>();
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
        }

        private NpcDefinition Npc(string id, GameObject visualPrefab = null)
        {
            var npc = ScriptableObject.CreateInstance<NpcDefinition>();
            made.Add(npc);
            var so = new SerializedObject(npc);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("visualPrefab").objectReferenceValue = visualPrefab != null ? visualPrefab : visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            return npc;
        }

        private NpcSpawnPoint AddNpc(string id, Vector3 position = default)
        {
            var spawnId = LocationPlacement.AddSpawn(location, Npc("npc-" + id), position, Quaternion.identity, id);
            var point = location.SpawnPoints.First(p => p.SpawnId == spawnId);
            var parent = point.Anchor.parent;
            if (parent != null && !made.Contains(parent.gameObject)) made.Add(parent.gameObject);
            return point;
        }

        private Transform Marker(string name, Vector3 position)
        {
            var go = new GameObject(name) { transform = { position = position } };
            made.Add(go);
            return go.transform;
        }

        private List<ValidationResult> Validate(bool deep = true, INavMeshService nav = null) =>
            LocationValidator.Validate(location, nav, deepChecks: deep);

        private static IEnumerable<ValidationResult> Routine(IEnumerable<ValidationResult> results) =>
            results.Where(r => r.Field.Contains(".Routine"));

        private CarriedPropDefinition Prop(bool withPrefab = true)
        {
            var prop = ScriptableObject.CreateInstance<CarriedPropDefinition>();
            made.Add(prop);
            if (withPrefab)
            {
                var prefab = new GameObject("crate");
                made.Add(prefab);
                prop.Configure(prefab, HumanBodyBones.RightHand, Vector3.zero, Vector3.zero);
            }

            return prop;
        }

        private WorkStop Stop(string name, WorkStopAction action = WorkStopAction.None, CarriedPropDefinition prop = null, Vector3 at = default) =>
            new() { Point = Marker(name, at), Action = action, Prop = prop };

        private static void SetLoop(NpcSpawnPoint point, params WorkStop[] stops)
        {
            point.Routine.Kind = NpcRoutineKind.WorkLoop;
            point.Routine.Stops = stops.ToList();
        }

        // ---------------------------------------------------------------- validator: wander

        [Test]
        public void Stand_NeverProducesRoutineFindings()
        {
            AddNpc("a");
            Assert.IsEmpty(Routine(Validate()).ToList());
        }

        [Test]
        public void Wander_ARadiusOfZeroIsAnError_AHalfMetreIsAWarning_AGoodOneIsClean()
        {
            var point = AddNpc("a");
            point.Routine.Kind = NpcRoutineKind.Wander;

            point.Routine.AreaRadius = 0f;
            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".AreaRadius")));

            point.Routine.AreaRadius = 0.5f;
            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Warning && r.Field.EndsWith(".AreaRadius")));

            point.Routine.AreaRadius = 5f;
            Assert.IsEmpty(Routine(Validate()).ToList());
        }

        [Test]
        public void Wander_ACentreOffTheWalkableSurface_IsAWarning_OnlyInTheDeepCheck()
        {
            var point = AddNpc("a");
            point.Routine.Kind = NpcRoutineKind.Wander;
            point.Routine.AreaCentre = Marker("centre", new Vector3(100f, 0f, 0f));
            var nav = new FakeNav { Walkable = p => p.x < 50f };

            Assert.IsTrue(Routine(Validate(deep: true, nav)).Any(r => r.Severity == ValidationSeverity.Warning && r.Field.EndsWith(".AreaCentre")));
            Assert.IsFalse(Routine(Validate(deep: false, nav)).Any(r => r.Field.EndsWith(".AreaCentre")), "the per-row dot must stay cheap");
        }

        [Test]
        public void Wander_ACentreInAnotherScene_IsAnError()
        {
            var point = AddNpc("a");
            point.Routine.Kind = NpcRoutineKind.Wander;
            var other = EditorSceneManager.NewPreviewScene();
            try
            {
                var centre = Marker("elsewhere", Vector3.zero);
                SceneManager.MoveGameObjectToScene(centre.gameObject, other);
                point.Routine.AreaCentre = centre;

                Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".AreaCentre")));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(other);
            }
        }

        // ---------------------------------------------------------------- validator: work loop

        [Test]
        public void WorkLoop_WithNoStops_IsAnError()
        {
            var point = AddNpc("a");
            point.Routine.Kind = NpcRoutineKind.WorkLoop;

            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".Stops")));
        }

        [Test]
        public void WorkLoop_AStopWithoutAPoint_IsAnError()
        {
            var point = AddNpc("a");
            SetLoop(point, new WorkStop { Point = null });

            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".Stops[0].Point")));
        }

        [Test]
        public void WorkLoop_AStopInAnotherScene_IsAnError()
        {
            var point = AddNpc("a");
            var other = EditorSceneManager.NewPreviewScene();
            try
            {
                var stop = Stop("far");
                SceneManager.MoveGameObjectToScene(stop.Point.gameObject, other);
                SetLoop(point, stop);

                Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".Stops[0].Point")));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(other);
            }
        }

        [Test]
        public void WorkLoop_APickUpNeedsAPropWithAPrefab()
        {
            var point = AddNpc("a");
            SetLoop(point, Stop("pile", WorkStopAction.PickUp, prop: null), Stop("bench", WorkStopAction.Drop));
            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".Stops[0].Prop")), "no prop");

            SetLoop(point, Stop("pile", WorkStopAction.PickUp, Prop(withPrefab: false)), Stop("bench", WorkStopAction.Drop));
            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Error && r.Field.EndsWith(".Stops[0].Prop")), "prop without prefab");

            SetLoop(point, Stop("pile", WorkStopAction.PickUp, Prop()), Stop("bench", WorkStopAction.Drop));
            Assert.IsEmpty(Routine(Validate()).ToList());
        }

        [Test]
        public void WorkLoop_PickUpTwiceInARow_IsAnError_AndAPickUpAloneIsToo()
        {
            var point = AddNpc("a");
            var prop = Prop();
            SetLoop(point, Stop("a", WorkStopAction.PickUp, prop), Stop("b", WorkStopAction.PickUp, prop), Stop("c", WorkStopAction.Drop));
            Assert.AreEqual(1, Routine(Validate()).Count(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("still carrying")), "reported once, at the second pick-up");

            SetLoop(point, Stop("only", WorkStopAction.PickUp, prop));
            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("still carrying")), "the loop comes round and picks up again without ever dropping");
        }

        [Test]
        public void WorkLoop_ADropFirstThenAPickUp_IsAValidLoop()
        {
            var point = AddNpc("a");
            SetLoop(point, Stop("bench", WorkStopAction.Drop), Stop("pile", WorkStopAction.PickUp, Prop()));

            Assert.IsEmpty(Routine(Validate()).ToList(), "the NPC arrives at the bench carrying what it picked up on the previous round");
        }

        [Test]
        public void WorkLoop_VisitingTheSameSpotTwiceInOneLoop_IsFine()
        {
            var point = AddNpc("a");
            var anvil = Stop("anvil", WorkStopAction.Work);
            SetLoop(point, anvil, Stop("well"), new WorkStop { Point = anvil.Point, Action = WorkStopAction.Work });

            Assert.IsFalse(Routine(Validate()).Any(r => r.Message.Contains("also a stop of row")), "only a stop shared with ANOTHER NPC is a problem");
        }

        [Test]
        public void WorkLoop_DropWithNothingCarried_IsAWarning()
        {
            var point = AddNpc("a");
            SetLoop(point, Stop("bench", WorkStopAction.Drop));

            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("carrying nothing")));
        }

        [Test]
        public void WorkLoop_APickUpDropPair_AndPlainWorkStops_AreClean()
        {
            var point = AddNpc("a");
            SetLoop(point, Stop("pile", WorkStopAction.PickUp, Prop()), Stop("bench", WorkStopAction.Drop), Stop("anvil", WorkStopAction.Work));

            Assert.IsEmpty(Routine(Validate()).ToList());
        }

        [Test]
        public void WorkLoop_ATransformSharedByTwoNpcs_IsAWarning()
        {
            AddNpc("a");
            AddNpc("b", new Vector3(5f, 0f, 0f));
            var shared = Stop("shared");
            SetLoop(location.SpawnPoints[0], shared);   // fetched after both adds: AddSpawn re-deserializes the list
            SetLoop(location.SpawnPoints[1], new WorkStop { Point = shared.Point });

            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("also a stop of row 0")));
        }

        [Test]
        public void WorkLoop_AStopOffTheSurfaceOrUnreachable_IsAWarning_OnlyInTheDeepCheck()
        {
            var point = AddNpc("a");
            SetLoop(point, Stop("island", at: new Vector3(100f, 0f, 0f)));
            var offSurface = new FakeNav { Walkable = p => p.x < 50f };
            Assert.IsTrue(Routine(Validate(true, offSurface)).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("walkable")));
            Assert.IsEmpty(Routine(Validate(false, offSurface)).ToList());

            var cutOff = new FakeNav { Reach = (from, to) => to.x < 50f };
            Assert.IsTrue(Routine(Validate(true, cutOff)).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("cannot be reached")));
        }

        [Test]
        public void Routine_OnABodyWithoutANavMeshMotor_IsAWarning_InTheDeepCheck()
        {
            var gridBody = new GameObject("grid-body");
            made.Add(gridBody);
            var point = AddNpc("a");
            AssignDefinition(point, Npc("npc-grid", gridBody));
            point.Routine.Kind = NpcRoutineKind.Wander;
            point.Routine.AreaRadius = 5f;

            Assert.IsTrue(Validate(true).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("NavMeshCharacterMotor")));
            Assert.IsFalse(Validate(false).Any(r => r.Message.Contains("NavMeshCharacterMotor")));
        }

        private void AssignDefinition(NpcSpawnPoint point, NpcDefinition definition)
        {
            var index = location.SpawnPoints.ToList().IndexOf(point);
            var so = new SerializedObject(location);
            so.FindProperty("spawnPoints").GetArrayElementAtIndex(index).FindPropertyRelative("Definition").objectReferenceValue = definition;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- validator: animation cues

        private CharacterAnimationProfile ProfileWith(string cue, bool loop, string stanceId = null)
        {
            var profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
            made.Add(profile);
            var so = new SerializedObject(profile);
            var cues = so.FindProperty("cues");
            cues.arraySize = 1;
            var entry = cues.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("cueId").FindPropertyRelative("value").stringValue = cue;
            entry.FindPropertyRelative("loop").boolValue = loop;
            if (stanceId != null)
            {
                var sets = so.FindProperty("stanceLocomotionSets");
                sets.arraySize = 1;
                sets.GetArrayElementAtIndex(0).FindPropertyRelative("stanceId").FindPropertyRelative("value").stringValue = stanceId;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private void GiveAnimator(CharacterAnimationProfile profile)
        {
            var animator = visual.AddComponent<CharacterPlayableAnimator>();
            var so = new SerializedObject(animator);
            so.FindProperty("profile").objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void Cue_NotInTheProfile_IsAWarning()
        {
            GiveAnimator(ProfileWith("other-cue", loop: false));
            var point = AddNpc("a");
            SetLoop(point, new WorkStop { Point = Marker("s", Vector3.zero), Action = WorkStopAction.Work, CueId = new DefinitionId("work-fix") });

            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("not in")));
        }

        [Test]
        public void Cue_ALoopingCueWithoutAStanceSet_IsAWarning_AndWithOneIsClean()
        {
            GiveAnimator(ProfileWith("work-fix", loop: true));
            var point = AddNpc("a");
            SetLoop(point, new WorkStop { Point = Marker("s", Vector3.zero), Action = WorkStopAction.Work, CueId = new DefinitionId("work-fix") });
            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("stance")));

            UnityEngine.Object.DestroyImmediate(visual.GetComponent<CharacterPlayableAnimator>());
            GiveAnimator(ProfileWith("work-fix", loop: true, stanceId: "work-fix"));
            Assert.IsEmpty(Routine(Validate()).ToList());
        }

        [Test]
        public void Cue_AOneShotThatExists_IsClean_AndAnAnimatorWithoutAProfileIsAWarning()
        {
            GiveAnimator(ProfileWith("pickup", loop: false));
            var point = AddNpc("a");
            SetLoop(point, new WorkStop { Point = Marker("s", Vector3.zero), Action = WorkStopAction.Work, CueId = new DefinitionId("pickup") });
            Assert.IsEmpty(Routine(Validate()).ToList());

            UnityEngine.Object.DestroyImmediate(visual.GetComponent<CharacterPlayableAnimator>());
            GiveAnimator(null);
            Assert.IsTrue(Routine(Validate()).Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains("no animation profile")));
        }

        // ---------------------------------------------------------------- placement

        [Test]
        public void AddSpawn_GivesTheNewNpcAFreshRoutine_NotACopyOfThePreviousOne()
        {
            var first = AddNpc("a");
            var pile = Stop("pile", WorkStopAction.PickUp, Prop());
            SetLoop(first, pile, Stop("bench", WorkStopAction.Drop));
            first.Routine.Seed = 42;
            first.Routine.AreaRadius = 11f;
            first.Routine.PauseMin = 9f;
            first.Routine.PauseMax = 19f;
            first.Routine.AreaCentre = Marker("centre", Vector3.zero);

            AddNpc("b", new Vector3(5f, 0f, 0f));
            var second = location.SpawnPoints[1];

            Assert.AreEqual(NpcRoutineKind.Stand, second.Routine.Kind);
            Assert.AreEqual(0, second.Routine.Stops.Count);
            Assert.IsNull(second.Routine.AreaCentre);
            Assert.AreEqual(0, second.Routine.Seed);
            Assert.AreEqual(6f, second.Routine.AreaRadius);
            Assert.AreEqual(2f, second.Routine.PauseMin);
            Assert.AreEqual(6f, second.Routine.PauseMax);
            Assert.AreEqual(NpcRoutineKind.WorkLoop, location.SpawnPoints[0].Routine.Kind, "and the first NPC is untouched");
            Assert.AreEqual(2, location.SpawnPoints[0].Routine.Stops.Count);
        }

        [Test]
        public void AddStop_CreatesASceneTransformBesideTheNpc_AndAppendsItToTheLoop_InOneUndoStep()
        {
            var point = AddNpc("smith", new Vector3(3f, 0f, 4f));
            point.Routine.Kind = NpcRoutineKind.WorkLoop;

            var stop = LocationPlacement.AddStop(location, 0);

            Assert.IsNotNull(stop);
            Assert.AreEqual("Stop_smith_1", stop.name);
            Assert.AreEqual(point.Anchor.parent, stop.parent, "beside the anchor, under the NPC root");
            Assert.AreEqual(2f, Vector3.Distance(point.Anchor.position, stop.position), 0.01f);
            Assert.AreEqual(1, point.Routine.Stops.Count);
            Assert.AreSame(stop, point.Routine.Stops[0].Point);
            Assert.AreEqual(WorkStopAction.None, point.Routine.Stops[0].Action);
            Assert.AreEqual(2f, point.Routine.Stops[0].Seconds);

            var second = LocationPlacement.AddStop(location, 0);
            Assert.AreEqual("Stop_smith_2", second.name);
            Assert.AreEqual(WorkStopAction.None, point.Routine.Stops[1].Action, "a new stop is not a copy of the previous one");

            Undo.PerformUndo();
            Assert.AreEqual(1, location.SpawnPoints[0].Routine.Stops.Count, "one undo removes the second stop");
            Assert.IsTrue(second == null, "and its transform");
        }

        [Test]
        public void AddStop_ANewStopIsNotACopyOfAPickUpStop()
        {
            var point = AddNpc("a");
            SetLoop(point, Stop("pile", WorkStopAction.PickUp, Prop()));
            point.Routine.Stops[0].Seconds = 9f;
            point.Routine.Stops[0].CueId = new DefinitionId("pickup");

            LocationPlacement.AddStop(location, 0);

            var added = location.SpawnPoints[0].Routine.Stops[1];
            Assert.AreEqual(WorkStopAction.None, added.Action);
            Assert.IsNull(added.Prop);
            Assert.AreEqual(2f, added.Seconds);
            Assert.IsFalse(added.CueId.IsValid);
        }

        [Test]
        public void AddStop_RefusesABadRowOrAnAnchorlessRow()
        {
            AddNpc("a");
            Assert.Throws<ArgumentOutOfRangeException>(() => LocationPlacement.AddStop(location, 5));
            Assert.Throws<ArgumentNullException>(() => LocationPlacement.AddStop(null, 0));

            var so = new SerializedObject(location);
            so.FindProperty("spawnPoints").GetArrayElementAtIndex(0).FindPropertyRelative("Anchor").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<InvalidOperationException>(() => LocationPlacement.AddStop(location, 0));
        }

        // ---------------------------------------------------------------- drawer + gizmo helpers

        [Test]
        public void Drawer_ShowsWanderFieldsForWander_StopsForWorkLoop_NeitherForStand()
        {
            Assert.AreEqual((false, false), NpcSpawnPointDrawer.RoutineSections(NpcRoutineKind.Stand));
            Assert.AreEqual((true, false), NpcSpawnPointDrawer.RoutineSections(NpcRoutineKind.Wander));
            Assert.AreEqual((false, true), NpcSpawnPointDrawer.RoutineSections(NpcRoutineKind.WorkLoop));
        }

        [Test]
        public void Drawer_BuildsTheRoutineSection_AndItsAddStopButtonAddsAStop()
        {
            var point = AddNpc("a");
            point.Routine.Kind = NpcRoutineKind.WorkLoop;
            var so = new SerializedObject(location);
            var element = new NpcSpawnPointDrawer().CreatePropertyGUI(so.FindProperty("spawnPoints").GetArrayElementAtIndex(0));

            Assert.IsNotNull(element.Q<VisualElement>("spawn-routine"));
            Assert.IsNotNull(element.Q("routine-kind"));
            Assert.IsNotNull(element.Q("routine-wander"));
            Assert.AreEqual(DisplayStyle.None, element.Q("routine-wander").style.display.value);
            Assert.AreEqual(DisplayStyle.Flex, element.Q("routine-work").style.display.value);
            Assert.IsNotNull(element.Q<Button>("routine-add-stop"));

            var stop = NpcSpawnPointDrawer.AddStop(so, "spawnPoints.Array.data[0]");
            Assert.IsNotNull(stop);
            Assert.AreEqual(1, location.SpawnPoints[0].Routine.Stops.Count);
        }

        [Test]
        public void Gizmos_RoutineColourIsStablePerNpc_AndDiffersBetweenNpcs()
        {
            Assert.AreEqual(LocationGizmos.RoutineColour("dock-worker"), LocationGizmos.RoutineColour("dock-worker"));
            Assert.AreNotEqual(LocationGizmos.RoutineColour("dock-worker"), LocationGizmos.RoutineColour("fisher"));
        }

        [Test]
        public void Gizmos_AnNpcsRoutineIsStrongWhenItsAnchorOrAStopOrItsCentreIsSelected()
        {
            var point = AddNpc("a");
            var stop = Stop("stop");
            var centre = Marker("centre", Vector3.zero);
            SetLoop(point, stop);
            point.Routine.AreaCentre = centre;
            var unrelated = Marker("unrelated", Vector3.zero);

            Selection.activeTransform = unrelated;
            Assert.IsFalse(LocationGizmos.IsRoutineSelected(point));

            Selection.activeTransform = point.Anchor;
            Assert.IsTrue(LocationGizmos.IsRoutineSelected(point));

            Selection.activeTransform = stop.Point;
            Assert.IsTrue(LocationGizmos.IsRoutineSelected(point));

            Selection.activeTransform = centre;
            Assert.IsTrue(LocationGizmos.IsRoutineSelected(point));

            var child = Marker("child", Vector3.zero);
            child.SetParent(stop.Point, false);
            Selection.activeTransform = child;
            Assert.IsTrue(LocationGizmos.IsRoutineSelected(point), "a child of a stop counts");
        }
    }
}
