using System.Collections;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D-Campaign-ExplorationUX-Followups.md task item 5: DragSelectionOverlay against a real
    /// UIDocument/PanelSettings (ConstantPixelSize, scale 1 -- this project's actual panel setup, finding S1)
    /// and a real PlayerCommandRouter constructed directly (matching PointerCommandTests.cs's own precedent,
    /// B6) -- no Input System device, no HudRoot MonoBehaviour lifecycle needed to exercise this.</summary>
    public class DragSelectionOverlayTests
    {
        private const float DragThreshold = 10f;
        private readonly List<GameObject> spawned = new();

        private sealed class FakePointerCaptureProvider : IPointerCaptureProvider
        {
            public bool CaptureAll;
            public bool IsPointerOverUI(Vector2 screenPosition) => CaptureAll;
        }

        private sealed class FakeSelectableSource : ISelectableSource
        {
            public IEnumerable<SelectableEntity> GetSelectables() => System.Array.Empty<SelectableEntity>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private UnityEngine.Camera CreateTopDownCamera()
        {
            var go = new GameObject("Camera");
            spawned.Add(go);
            var camera = go.AddComponent<UnityEngine.Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 20f;
            go.transform.position = new Vector3(0f, 10f, 0f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return camera;
        }

        private VisualElement CreateDocumentRoot()
        {
            var go = new GameObject("Hud");
            spawned.Add(go);
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1f;
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            return document.rootVisualElement;
        }

        [UnityTest]
        public IEnumerator ADragPastTheThresholdShowsTheOverlayAtAPositiveSizedBoxAndHidesItOnRelease()
        {
            var camera = CreateTopDownCamera();
            var documentRoot = CreateDocumentRoot();
            yield return null; // let the panel actually attach

            var router = new PlayerCommandRouter(new FakeSelectableSource(), new FakePointerCaptureProvider(), ~0, DragThreshold);
            var overlay = new DragSelectionOverlay(documentRoot, router);
            var box = documentRoot.Q(name: "drag-select-box");
            Assert.IsNotNull(box);
            Assert.AreEqual(DisplayStyle.None, box.style.display.value);

            var downScreenPos = camera.WorldToScreenPoint(new Vector3(-3f, 0f, -3f));
            var midScreenPos = camera.WorldToScreenPoint(new Vector3(5f, 0f, 5f));
            var downRay = new Ray(new Vector3(-3f, 20f, -3f), Vector3.down);
            var midRay = new Ray(new Vector3(5f, 20f, 5f), Vector3.down);

            router.HandlePointerDown(downScreenPos, downRay);
            Assert.AreEqual(DisplayStyle.None, box.style.display.value); // no movement yet

            router.HandlePointerMove(midScreenPos); // exceeds the drag threshold
            Assert.AreEqual(DisplayStyle.Flex, box.style.display.value);
            Assert.Greater(box.style.width.value.value, 0f);
            Assert.Greater(box.style.height.value.value, 0f);

            router.HandlePointerUp(midScreenPos, midRay, camera, shiftHeld: false);
            Assert.AreEqual(DisplayStyle.None, box.style.display.value);

            overlay.Dispose();
        }

        [UnityTest]
        public IEnumerator AModalThatBeginsMidDragHidesTheOverlayImmediately()
        {
            var camera = CreateTopDownCamera();
            var documentRoot = CreateDocumentRoot();
            yield return null;

            var capture = new FakePointerCaptureProvider();
            var router = new PlayerCommandRouter(new FakeSelectableSource(), capture, ~0, DragThreshold);
            var overlay = new DragSelectionOverlay(documentRoot, router);
            var box = documentRoot.Q(name: "drag-select-box");

            var downScreenPos = camera.WorldToScreenPoint(new Vector3(-3f, 0f, -3f));
            var midScreenPos = camera.WorldToScreenPoint(new Vector3(5f, 0f, 5f));
            router.HandlePointerDown(downScreenPos, new Ray(new Vector3(-3f, 20f, -3f), Vector3.down)); // clean at press time
            router.HandlePointerMove(midScreenPos); // exceeds threshold -- box now shown
            Assert.AreEqual(DisplayStyle.Flex, box.style.display.value);

            capture.CaptureAll = true; // a modal opens mid-drag
            router.HandlePointerMove(midScreenPos);
            Assert.AreEqual(DisplayStyle.None, box.style.display.value); // hides immediately, without waiting for release

            overlay.Dispose();
        }
    }
}
