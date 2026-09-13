using System.Collections;
using System.Reflection;
using CRPG.Camera;
using CRPG.InputSystem;
using CRPG.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>Task 3.2: InputSystemCommandSource's continuous camera input (held Pan/Rotate applied every
    /// frame, not just on the callback that changed them) and Recenter, driven through the REAL Input System
    /// via InputTestFixture -- a real keyboard press/release, not a simulated field write, is what actually
    /// proves "held while a key stays down" works, which is exactly the property a callback-only
    /// implementation would have gotten wrong (see InputSystemCommandSource.Update()'s own remarks).</summary>
    public class CameraInputTests : InputTestFixture
    {
        private Keyboard keyboard;
        private GameObject rigGo;
        private GameObject sourceGo;
        private IsometricCameraRig rig;
        private InputSystemCommandSource source;

        public override void Setup()
        {
            base.Setup();
            // Fully qualified: CRPG.InputSystem (this project's own namespace, referenced by this assembly)
            // shadows UnityEngine.InputSystem's "InputSystem" static class name via C#'s enclosing-namespace
            // lookup once the CRPG.InputSystem assembly is referenced -- the same gotcha fixed elsewhere for
            // "Camera" (CRPG.Camera vs UnityEngine.Camera).
            keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();
        }

        public override void TearDown()
        {
            if (sourceGo != null) Object.DestroyImmediate(sourceGo);
            if (rigGo != null) Object.DestroyImmediate(rigGo);
            base.TearDown();
        }

        private static void SetField(object target, string fieldName, object value) =>
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private void CreateRigAndSource()
        {
            // Built inactive so AddComponent does NOT trigger Awake() before the reflection-set fields below
            // are in place -- Awake() calls Apply(), which dereferences yawPivot/pitchPivot/cameraTransform
            // directly and NREs if AddComponent's own (synchronous, on an active GameObject) Awake() call
            // finds them still null. Activating afterward triggers the real Awake() exactly once, correctly.
            rigGo = new GameObject("CameraRoot");
            rigGo.SetActive(false);
            var yaw = new GameObject("YawPivot").transform;
            var pitch = new GameObject("PitchPivot").transform;
            var cameraTransform = new GameObject("Camera").transform;
            yaw.SetParent(rigGo.transform);
            pitch.SetParent(yaw);
            cameraTransform.SetParent(pitch);

            rig = rigGo.AddComponent<IsometricCameraRig>();
            SetField(rig, "yawPivot", yaw);
            SetField(rig, "pitchPivot", pitch);
            SetField(rig, "cameraTransform", cameraTransform);
            SetField(rig, "settings", ScriptableObject.CreateInstance<CRPGCameraSettings>());
            rigGo.SetActive(true);

            sourceGo = new GameObject("InputSystemCommandSource");
            source = sourceGo.AddComponent<InputSystemCommandSource>();
            SetField(source, "cameraRig", rig);
        }

        [UnityTest]
        public IEnumerator HeldPanContinuesToMoveTheRigAcrossMultipleFramesWithoutANewKeyEvent()
        {
            CreateRigAndSource();
            rig.Rig.SnapTo(Vector3.zero);

            Press(keyboard.wKey);
            yield return null; // let the press be processed and the callback fire

            var afterFirstFrame = rig.Rig.FocusPoint;
            Assert.AreNotEqual(Vector3.zero, afterFirstFrame); // Pan already applied once

            yield return null;
            yield return null;
            var afterMoreFrames = rig.Rig.FocusPoint;

            Assert.Greater(afterMoreFrames.z, afterFirstFrame.z); // kept moving with no further key events

            Release(keyboard.wKey);
            yield return null;
            var afterRelease = rig.Rig.FocusPoint;
            yield return null;
            yield return null;

            Assert.AreEqual(afterRelease, rig.Rig.FocusPoint); // stopped the instant the key was released
        }

        [UnityTest]
        public IEnumerator OpeningAModalMidHoldStopsCameraMovementOnTheNextFrame()
        {
            CreateRigAndSource();
            rig.Rig.SnapTo(Vector3.zero);

            Press(keyboard.wKey);
            yield return null;
            var beforeModal = rig.Rig.FocusPoint;
            Assert.AreNotEqual(Vector3.zero, beforeModal);

            using (UIToolkitPointerCapture.BeginModal())
            {
                yield return null;
                yield return null;
                Assert.AreEqual(beforeModal, rig.Rig.FocusPoint); // key still held, but gated every frame by the modal check
            }

            Release(keyboard.wKey);
        }

        [UnityTest]
        public IEnumerator RecenterClearsManualOverrideSoAFollowingFollowTargetCallResumes()
        {
            CreateRigAndSource();
            rig.Rig.SnapTo(Vector3.zero);

            Press(keyboard.wKey);
            yield return null;
            Release(keyboard.wKey);
            yield return null;
            Assert.IsTrue(rig.Rig.IsManualOverrideActive);

            PressAndRelease(keyboard.spaceKey);
            yield return null;

            Assert.IsFalse(rig.Rig.IsManualOverrideActive);
        }
    }
}
