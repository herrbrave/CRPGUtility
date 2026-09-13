using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Gameplay.Interactions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>
    /// Task 3.1 (Production Foundation): covers PlayerCommandRouter's full documented priority chain
    /// (captured UI/modal -&gt; targeting -&gt; interactable/selectable -&gt; ground) plus click/drag-threshold and
    /// shift-toggle behavior -- all against the router directly with simple fakes, per the design's own goal
    /// of keeping this decision logic testable without a live Input System device or a real UI. Physics-based
    /// resolution (click hit-testing) does need a real scene/frame, hence [UnityTest] throughout for
    /// consistency even where a case doesn't strictly require it.
    /// </summary>
    public class PointerCommandTests
    {
        private const float DragThreshold = 10f;

        // Confirmed live, twice: a click-on-selectable test passed in isolation but failed as part of the full
        // suite even after moving every test in this file to one shared far-away origin -- adjacent tests IN
        // THIS SAME FILE at that identical shared origin still clashed (most likely Physics.DestroyImmediate's
        // broadphase removal not being guaranteed to have fully settled by the very next test's single yielded
        // frame). Giving every test its OWN origin, spaced well apart, removes the shared-coordinate hazard
        // entirely rather than needing to reason about exactly how many frames a destroy needs to propagate.
        private static int originCounter;
        private Vector3 Origin { get; set; }

        private readonly List<GameObject> spawned = new();
        private readonly List<GameSession> sessions = new();

        [SetUp]
        public void SetUp()
        {
            Origin = new Vector3(5000f + originCounter * 500f, 0f, 5000f);
            originCounter++;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            foreach (var session in sessions) session.Dispose();
            sessions.Clear();
        }

        private sealed class FakePointerCaptureProvider : IPointerCaptureProvider
        {
            public bool CaptureAll;
            public bool IsPointerOverUI(Vector2 screenPosition) => CaptureAll;
        }

        private sealed class FakeSelectableSource : ISelectableSource
        {
            public readonly List<SelectableEntity> Entities = new();
            public IEnumerable<SelectableEntity> GetSelectables() => Entities;
        }

        private UnityEngine.Camera CreateTopDownCamera(GameObject owner)
        {
            var camera = owner.AddComponent<UnityEngine.Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 20f;
            owner.transform.position = Origin + new Vector3(0f, 10f, 0f);
            owner.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return camera;
        }

        private GameObject CreateGroundPlane()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ground";
            go.transform.position = Origin + new Vector3(0f, -0.5f, 0f);
            go.transform.localScale = new Vector3(100f, 1f, 100f);
            spawned.Add(go);
            return go;
        }

        /// <param name="localPosition">Relative to <see cref="Origin"/>.</param>
        private (GameObject go, RuntimeEntityId id) CreateBoundSelectable(Vector3 localPosition)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.position = Origin + localPosition;

            var agent = go.AddComponent<CharacterAgent>();
            var session = new GameSession(new RulesDatabase(), new DiceService(new SeededRandomSource(1)));
            sessions.Add(session);
            var id = RuntimeEntityId.NewId();
            agent.Bind(id, session);

            spawned.Add(go);
            return (go, id);
        }

        private PlayerCommandRouter CreateRouter(ISelectableSource selectableSource, IPointerCaptureProvider captureProvider) =>
            new(selectableSource, captureProvider, ~0, DragThreshold);

        /// <param name="localPoint">Relative to <see cref="Origin"/>.</param>
        private Ray RayFromAbove(Vector3 localPoint)
        {
            var worldPoint = Origin + localPoint;
            return new Ray(new Vector3(worldPoint.x, worldPoint.y + 20f, worldPoint.z), Vector3.down);
        }

        // A single `yield return null` (one rendered frame) is NOT a reliable guarantee that a just-created
        // Collider is actually queryable via Physics.Raycast yet -- confirmed live: click-resolution tests
        // failed intermittently (different ones each run) purely because of this, never the drag-box tests
        // (which never touch real Physics). FixedUpdate is what actually steps the physics world, and a single
        // rendered frame isn't guaranteed to contain one; Physics.SyncTransforms() forces an immediate sync as
        // a second safety net rather than trusting frame timing alone.
        private static IEnumerator WaitForColliders()
        {
            yield return null;
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
        }

        // --- Baseline positive paths ---

        [UnityTest]
        public IEnumerator AClickOnASelectableRaisesSelectWithReplaceMode()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);
            CreateGroundPlane();
            var (_, targetId) = CreateBoundSelectable(Vector3.zero);
            yield return WaitForColliders();

            var router = CreateRouter(new FakeSelectableSource(), new FakePointerCaptureProvider());
            SelectCommand? received = null;
            router.Select += cmd => received = cmd;

            var screenPos = camera.WorldToScreenPoint(Origin);
            var ray = RayFromAbove(Vector3.zero);
            router.HandlePointerDown(screenPos, ray);
            router.HandlePointerUp(screenPos, ray, camera, shiftHeld: false);

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(SelectionMode.Replace, received.Value.Mode);
            CollectionAssert.AreEqual(new[] { targetId }, received.Value.Characters);
        }

        [UnityTest]
        public IEnumerator AClickOnBareGroundRaisesMoveAtTheHitPoint()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);
            CreateGroundPlane();
            yield return WaitForColliders();

            var router = CreateRouter(new FakeSelectableSource(), new FakePointerCaptureProvider());
            MoveCommand? received = null;
            router.Move += cmd => received = cmd;

            var localPoint = new Vector3(5f, 0f, 5f);
            var worldPoint = Origin + localPoint;
            var screenPos = camera.WorldToScreenPoint(worldPoint);
            var ray = RayFromAbove(localPoint);
            router.HandlePointerDown(screenPos, ray);
            router.HandlePointerUp(screenPos, ray, camera, shiftHeld: false);

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(worldPoint.x, received.Value.Destination.x, 0.01f);
            Assert.AreEqual(worldPoint.z, received.Value.Destination.z, 0.01f);
        }

        [UnityTest]
        public IEnumerator CancelRaisesCancel()
        {
            var router = CreateRouter(new FakeSelectableSource(), new FakePointerCaptureProvider());
            var raised = false;
            router.Cancel += () => raised = true;

            router.HandleCancel();

            Assert.IsTrue(raised);
            yield break;
        }

        // --- Required acceptance cases ---

        [UnityTest]
        public IEnumerator AGestureThatStartsOverUiNeverRaisesMoveEvenIfReleasedOverTheWorld()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);
            CreateGroundPlane();
            yield return WaitForColliders();

            var capture = new FakePointerCaptureProvider { CaptureAll = true };
            var router = CreateRouter(new FakeSelectableSource(), capture);
            var moveRaised = false;
            var selectRaised = false;
            router.Move += _ => moveRaised = true;
            router.Select += _ => selectRaised = true;

            var downLocal = new Vector3(1f, 0f, 1f);
            var downScreenPos = camera.WorldToScreenPoint(Origin + downLocal);
            router.HandlePointerDown(downScreenPos, RayFromAbove(downLocal)); // starts "over UI"

            // Release-through: capture flips false before release, but the gesture already started suppressed.
            capture.CaptureAll = false;
            var upLocal = new Vector3(5f, 0f, 5f);
            var upScreenPos = camera.WorldToScreenPoint(Origin + upLocal);
            router.HandlePointerUp(upScreenPos, RayFromAbove(upLocal), camera, shiftHeld: false);

            Assert.IsFalse(moveRaised);
            Assert.IsFalse(selectRaised);
        }

        [UnityTest]
        public IEnumerator ShiftHeldClickOnASelectableRaisesSelectWithToggleMode()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);
            CreateGroundPlane();
            CreateBoundSelectable(Vector3.zero);
            yield return WaitForColliders();

            var router = CreateRouter(new FakeSelectableSource(), new FakePointerCaptureProvider());
            SelectCommand? received = null;
            router.Select += cmd => received = cmd;

            var screenPos = camera.WorldToScreenPoint(Origin);
            var ray = RayFromAbove(Vector3.zero);
            router.HandlePointerDown(screenPos, ray);
            router.HandlePointerUp(screenPos, ray, camera, shiftHeld: true);

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(SelectionMode.Toggle, received.Value.Mode);
        }

        [UnityTest]
        public IEnumerator DragBoxSelectsExactlyTheSelectablesInsideTheRectAndExcludesOneOutsideIt()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);

            var source = new FakeSelectableSource();
            var inside = RuntimeEntityId.NewId();
            var outside = RuntimeEntityId.NewId();
            source.Entities.Add(new SelectableEntity(inside, Origin + new Vector3(1f, 0f, 1f)));
            source.Entities.Add(new SelectableEntity(outside, Origin + new Vector3(15f, 0f, 15f)));

            var router = CreateRouter(source, new FakePointerCaptureProvider());
            SelectCommand? received = null;
            router.Select += cmd => received = cmd;

            var downScreenPos = camera.WorldToScreenPoint(Origin + new Vector3(-3f, 0f, -3f));
            router.HandlePointerDown(downScreenPos, RayFromAbove(new Vector3(-3f, 0f, -3f)));

            var midScreenPos = camera.WorldToScreenPoint(Origin + new Vector3(5f, 0f, 5f));
            router.HandlePointerMove(midScreenPos); // exceeds the drag threshold -- this becomes a box-select

            router.HandlePointerUp(midScreenPos, RayFromAbove(new Vector3(5f, 0f, 5f)), camera, shiftHeld: false);

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual(SelectionMode.Replace, received.Value.Mode);
            CollectionAssert.AreEqual(new[] { inside }, received.Value.Characters);
            yield break;
        }

        [UnityTest]
        public IEnumerator ASelectableRemovedFromTheSourceMidGestureIsSilentlyExcluded()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);

            var source = new FakeSelectableSource();
            var survivor = RuntimeEntityId.NewId();
            var destroyedEntity = new SelectableEntity(RuntimeEntityId.NewId(), Origin + new Vector3(2f, 0f, 2f));
            source.Entities.Add(new SelectableEntity(survivor, Origin + new Vector3(1f, 0f, 1f)));
            source.Entities.Add(destroyedEntity);

            var router = CreateRouter(source, new FakePointerCaptureProvider());
            SelectCommand? received = null;
            router.Select += cmd => received = cmd;

            var downScreenPos = camera.WorldToScreenPoint(Origin + new Vector3(-3f, 0f, -3f));
            router.HandlePointerDown(downScreenPos, RayFromAbove(new Vector3(-3f, 0f, -3f)));

            var dragScreenPos = camera.WorldToScreenPoint(Origin + new Vector3(5f, 0f, 5f));
            router.HandlePointerMove(dragScreenPos);

            // Simulates the selectable having been destroyed between the drag starting and it resolving --
            // production SceneSelectableSource would already have filtered it out; the router must handle
            // whatever the source currently reports without throwing.
            source.Entities.Remove(destroyedEntity);

            var dragRay = RayFromAbove(new Vector3(5f, 0f, 5f));
            Assert.DoesNotThrow(() => router.HandlePointerUp(dragScreenPos, dragRay, camera, shiftHeld: false));

            Assert.IsTrue(received.HasValue);
            CollectionAssert.AreEqual(new[] { survivor }, received.Value.Characters);
            yield break;
        }

        [UnityTest]
        public IEnumerator AClickIsSuppressedWhileAModalScopeIsActiveAndResolvesNormallyOnceItEnds()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);
            CreateGroundPlane();
            yield return WaitForColliders();

            var capture = new FakePointerCaptureProvider();
            var router = CreateRouter(new FakeSelectableSource(), capture);
            var moveCount = 0;
            router.Move += _ => moveCount++;

            var localPoint = new Vector3(5f, 0f, 5f);
            var screenPos = camera.WorldToScreenPoint(Origin + localPoint);
            var ray = RayFromAbove(localPoint);

            capture.CaptureAll = true; // stands in for UIToolkitPointerCapture.BeginModal() being active
            router.HandlePointerDown(screenPos, ray);
            router.HandlePointerUp(screenPos, ray, camera, shiftHeld: false);
            Assert.AreEqual(0, moveCount);

            capture.CaptureAll = false; // modal scope ended
            router.HandlePointerDown(screenPos, ray);
            router.HandlePointerUp(screenPos, ray, camera, shiftHeld: false);
            Assert.AreEqual(1, moveCount);
        }

        // Task 3.3: a gesture that starts clean over the world but ends after a modal opened MID-gesture (e.g.
        // a synchronous interaction execute triggered by a different click) must not leak a Move through just
        // because the down-time check didn't see the modal coming -- HandlePointerUp must re-check UI/modal
        // state at release time too, not only rely on the down-time snapshot.
        [UnityTest]
        public IEnumerator AModalThatOpensMidGestureSuppressesTheReleaseEvenThoughTheGestureStartedClean()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);
            CreateGroundPlane();
            yield return WaitForColliders();

            var capture = new FakePointerCaptureProvider();
            var router = CreateRouter(new FakeSelectableSource(), capture);
            var moveCount = 0;
            router.Move += _ => moveCount++;

            var localPoint = new Vector3(5f, 0f, 5f);
            var screenPos = camera.WorldToScreenPoint(Origin + localPoint);
            var ray = RayFromAbove(localPoint);

            capture.CaptureAll = false; // clean at press time
            router.HandlePointerDown(screenPos, ray);

            capture.CaptureAll = true; // a modal opens between press and release
            router.HandlePointerUp(screenPos, ray, camera, shiftHeld: false);

            Assert.AreEqual(0, moveCount);
        }

        // --- SceneSelectableSource itself, at the production level ---

        [UnityTest]
        public IEnumerator SceneSelectableSourceExcludesADestroyedCharacterAgent()
        {
            // Doesn't assert an exact total count -- CharacterAgent.AllLive is process-global static state, so
            // other tests' agents may legitimately still be registered depending on suite run order. Asserts
            // only what this test itself controls: the surviving agent IS present, the destroyed one is NOT.
            var (_, survivingId) = CreateBoundSelectable(new Vector3(1f, 0f, 1f));
            var (toDestroy, destroyedId) = CreateBoundSelectable(new Vector3(2f, 0f, 2f));
            yield return null;

            Object.DestroyImmediate(toDestroy);
            spawned.Remove(toDestroy);
            yield return null;

            var source = new SceneSelectableSource();
            var ids = source.GetSelectables().Select(s => s.Id).ToList();

            CollectionAssert.Contains(ids, survivingId);
            CollectionAssert.DoesNotContain(ids, destroyedId);
        }

        // --- Task 3.3: interactable/selectable precedence ---

        private sealed class FakeInteractionAdapter : MonoBehaviour, IInteractionAdapter
        {
            public DefinitionId ActionId { get; } = new("fake");
            public bool IsAvailable(RuntimeEntityId performer) => true;
            public IInteractionExecution BeginExecute(RuntimeEntityId performer, System.Action<InteractionExecutionResult> onFinished) => null;
        }

        [UnityTest]
        public IEnumerator AClickOnAnObjectThatIsBothSelectableAndInteractableResolvesAsSelectNeverInteract()
        {
            var cameraGo = new GameObject("Camera");
            spawned.Add(cameraGo);
            var camera = CreateTopDownCamera(cameraGo);
            CreateGroundPlane();

            var (go, targetId) = CreateBoundSelectable(Vector3.zero);
            go.SetActive(false);
            var anchorGo = new GameObject("Anchor") { transform = { position = go.transform.position } };
            spawned.Add(anchorGo);
            go.AddComponent<FakeInteractionAdapter>();
            var interactable = go.AddComponent<WorldInteractable>();
            interactable.Configure(anchorGo.transform, 1.5f);
            go.SetActive(true);
            yield return WaitForColliders();

            var router = CreateRouter(new FakeSelectableSource(), new FakePointerCaptureProvider());
            SelectCommand? selectReceived = null;
            var interactRaised = false;
            router.Select += cmd => selectReceived = cmd;
            router.Interact += _ => interactRaised = true;

            var screenPos = camera.WorldToScreenPoint(Origin);
            var ray = RayFromAbove(Vector3.zero);
            router.HandlePointerDown(screenPos, ray);
            router.HandlePointerUp(screenPos, ray, camera, shiftHeld: false);

            Assert.IsTrue(selectReceived.HasValue);
            Assert.AreEqual(SelectionMode.Replace, selectReceived.Value.Mode);
            CollectionAssert.AreEqual(new[] { targetId }, selectReceived.Value.Characters);
            Assert.IsFalse(interactRaised);

            selectReceived = null;
            router.HandlePointerDown(screenPos, ray);
            router.HandlePointerUp(screenPos, ray, camera, shiftHeld: true);

            Assert.IsTrue(selectReceived.HasValue);
            Assert.AreEqual(SelectionMode.Toggle, selectReceived.Value.Mode);
            Assert.IsFalse(interactRaised);
        }
    }
}
