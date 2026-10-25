using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UCamera = UnityEngine.Camera;

namespace CRPG.Tests.PlayMode
{
    /// <summary>The router's optional world filter (<see cref="IPointerWorldFilter"/>): a click passes through a collider the filter declines and lands on what is below, a drag box skips a character the filter declines, and with no filter nothing changes.</summary>
    public class PointerWorldFilterTests
    {
        private sealed class NoCapture : IPointerCaptureProvider { public bool IsPointerOverUI(Vector2 screenPosition) => false; }

        private sealed class Selectables : ISelectableSource
        {
            public readonly List<SelectableEntity> Entities = new();
            public IEnumerable<SelectableEntity> GetSelectables() => Entities;
        }

        private sealed class Filter : IPointerWorldFilter
        {
            public readonly HashSet<Collider> Hidden = new(); public readonly HashSet<RuntimeEntityId> Unselectable = new(); public int PickChecks;
            public bool IsPickable(Collider collider) { PickChecks++; return !Hidden.Contains(collider); }
            public bool IsSelectable(RuntimeEntityId id, Vector3 worldPosition) => !Unselectable.Contains(id);
        }

        private readonly List<Object> created = new();
        private BoxCollider ground, upper;
        private PlayerCommandRouter router; private Selectables selectables;
        private readonly List<MoveCommand> moves = new(); private readonly List<SelectCommand> selects = new();

        [TearDown] public void TearDown() { foreach (var o in created) if (o != null) Object.DestroyImmediate(o); created.Clear(); }

        private IEnumerator Build()
        {
            ground = Box("Ground floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));   // surface at y = 0
            upper = Box("Upper floor", new Vector3(0f, 2.75f, 0f), new Vector3(20f, 0.5f, 20f));   // surface at y = 3, directly above
            yield return new WaitForFixedUpdate(); Physics.SyncTransforms();
            selectables = new Selectables();
            router = new PlayerCommandRouter(selectables, new NoCapture(), ~0);
            router.Move += m => moves.Add(m); router.Select += s => selects.Add(s);
        }

        private BoxCollider Box(string name, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name); created.Add(go); go.transform.position = centre;
            var box = go.AddComponent<BoxCollider>(); box.size = size; return box;
        }

        private void Click(Vector2 screen, Ray ray) { router.HandlePointerDown(screen, ray); router.HandlePointerUp(screen, ray, null, false); }
        private static Ray Down(float x, float z) => new Ray(new Vector3(x, 10f, z), Vector3.down);

        [UnityTest]
        public IEnumerator WithoutAFilterTheNearestSurfaceTakesTheClick()
        {
            yield return Build();
            Click(Vector2.zero, Down(1f, 1f));
            Assert.AreEqual(1, moves.Count); Assert.AreEqual(3f, moves[0].Destination.y, 0.01f, "the upper floor is the nearest surface");
        }

        [UnityTest]
        public IEnumerator AClickPassesThroughACollider_TheFilterDeclines_AndLandsOnWhatIsBelow()
        {
            yield return Build();
            var filter = new Filter(); filter.Hidden.Add(upper); router.WorldFilter = filter;
            Click(Vector2.zero, Down(1f, 1f));
            Assert.AreEqual(1, moves.Count); Assert.AreEqual(0f, moves[0].Destination.y, 0.01f, "the hidden upper floor is not there for the pointer");
            Assert.AreEqual(1f, moves[0].Destination.x, 0.01f); Assert.GreaterOrEqual(filter.PickChecks, 2, "the filter saw the upper floor first, then accepted the ground");
        }

        [UnityTest]
        public IEnumerator ShowingTheUpperFloorAgainRestoresItAsTheTarget_AndAFilterThatDeclinesEverythingMakesAClickDoNothing()
        {
            yield return Build();
            var filter = new Filter(); router.WorldFilter = filter;
            Click(Vector2.zero, Down(2f, 2f)); Assert.AreEqual(3f, moves.Last().Destination.y, 0.01f, "a filter that declines nothing changes nothing");
            filter.Hidden.Add(upper); filter.Hidden.Add(ground); moves.Clear();
            Click(Vector2.zero, Down(2f, 2f)); Assert.AreEqual(0, moves.Count, "nothing pickable under the pointer: no order");
        }

        [UnityTest]
        public IEnumerator ADragBoxSkipsACharacterTheFilterDeclines()
        {
            yield return Build();
            var cameraObject = new GameObject("Camera"); created.Add(cameraObject); var camera = cameraObject.AddComponent<UCamera>();
            camera.transform.position = new Vector3(0f, 20f, 0f); camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var here = RuntimeEntityId.NewId(); var onAnotherFloor = RuntimeEntityId.NewId();
            selectables.Entities.Add(new SelectableEntity(here, new Vector3(-1f, 0f, 0f))); selectables.Entities.Add(new SelectableEntity(onAnotherFloor, new Vector3(1f, 3f, 0f)));
            var filter = new Filter(); filter.Unselectable.Add(onAnotherFloor); router.WorldFilter = filter;
            var from = new Vector2(0f, 0f); var to = new Vector2(Screen.width, Screen.height);
            router.HandlePointerDown(from, Down(0f, 0f)); router.HandlePointerMove(to); router.HandlePointerUp(to, Down(0f, 0f), camera, false);
            Assert.AreEqual(1, selects.Count); CollectionAssert.AreEqual(new[] { here }, selects[0].Characters, "only the character on the shown floor");
            selects.Clear(); filter.Unselectable.Clear();
            router.HandlePointerDown(from, Down(0f, 0f)); router.HandlePointerMove(to); router.HandlePointerUp(to, Down(0f, 0f), camera, false);
            Assert.AreEqual(2, selects[0].Characters.Count, "with nothing declined, both are caught");
        }
    }
}
