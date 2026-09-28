using System.Collections;
using System.Reflection;
using CRPG.Core;
using CRPG.InputSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D-Campaign-ExplorationUX-Followups.md task item 3: InputSystemCommandSource's
    /// IDragSelectionFeedback pass-through is a hand-written add/remove pair, not a compiler-generated one --
    /// worth its own real-Input-System-driven test rather than trusting a compile-time interface check alone
    /// (Opus review, should-fix S6). Also covers OnClick's ActiveCamera==null release path, which must still
    /// end any in-flight drag (and any visible drag box) rather than leaving it stuck forever.</summary>
    public class InputSystemCommandSourceTests : InputTestFixture
    {
        private Mouse mouse;
        private GameObject sourceGo;
        private GameObject cameraGo;
        private InputSystemCommandSource source;

        public override void Setup()
        {
            base.Setup();
            // Fully qualified: CRPG.InputSystem (this project's own namespace, referenced by this assembly)
            // shadows UnityEngine.InputSystem's "InputSystem" static class name, same gotcha as CameraInputTests.
            mouse = UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();
        }

        public override void TearDown()
        {
            if (sourceGo != null) Object.DestroyImmediate(sourceGo);
            if (cameraGo != null) Object.DestroyImmediate(cameraGo);
            base.TearDown();
        }

        private static void SetField(object target, string fieldName, object value) =>
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private void CreateSourceWithCamera()
        {
            cameraGo = new GameObject("Camera");
            var camera = cameraGo.AddComponent<UnityEngine.Camera>();
            camera.orthographic = true;
            cameraGo.transform.position = new Vector3(0f, 10f, 0f);
            cameraGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            sourceGo = new GameObject("InputSystemCommandSource");
            source = sourceGo.AddComponent<InputSystemCommandSource>();
            SetField(source, "raycastCamera", camera);
            SetField(source, "dragThresholdPixels", 10f);
        }

        [UnityTest]
        public IEnumerator DragBoxChangedAndEndedForwardThroughFromTheInternalRouter()
        {
            CreateSourceWithCamera();
            var feedback = (IDragSelectionFeedback)source;
            var changedCount = 0;
            var endedCount = 0;
            Rect? lastRect = null;
            feedback.DragBoxChanged += rect => { changedCount++; lastRect = rect; };
            feedback.DragBoxEnded += () => endedCount++;

            Set(mouse.position, new Vector2(100f, 100f));
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Assert.AreEqual(0, changedCount); // no movement yet -- still just a press

            Set(mouse.position, new Vector2(150f, 150f)); // exceeds the drag threshold
            yield return null;
            Assert.AreEqual(1, changedCount);
            Assert.IsTrue(lastRect.HasValue);

            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(1, endedCount);
        }

        [UnityTest]
        public IEnumerator ReleaseWithNoActiveCameraAbortsTheGestureAndEndsAVisibleDragBoxInsteadOfLeavingItStuck()
        {
            CreateSourceWithCamera();
            var feedback = (IDragSelectionFeedback)source;
            var endedCount = 0;
            feedback.DragBoxEnded += () => endedCount++;

            Set(mouse.position, new Vector2(100f, 100f));
            yield return null;
            Press(mouse.leftButton); // starts clean, with a real camera in place
            yield return null;

            Set(mouse.position, new Vector2(150f, 150f)); // exceeds threshold -- box now shown
            yield return null;
            Assert.AreEqual(0, endedCount);

            // ActiveCamera falls back to Camera.main once raycastCamera is cleared -- null in this bare test
            // scene, simulating the camera briefly going away mid-drag (e.g. a scene transition).
            SetField(source, "raycastCamera", null);
            Release(mouse.leftButton);
            yield return null;

            Assert.AreEqual(1, endedCount); // aborted, not left stuck
        }
    }
}
