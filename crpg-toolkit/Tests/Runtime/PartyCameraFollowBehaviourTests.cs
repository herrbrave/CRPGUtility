using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CRPG.Tests.PlayMode
{
    /// <summary>Task 3.2: PartyCameraFollowBehaviour's own MonoBehaviour-level responsibilities -- reading
    /// FollowSelection live (not just once at Initialize) and calling SnapToActiveCharacter on Initialize.
    /// PartyCameraFollow's own Tick/SnapToActiveCharacter logic is covered directly, without any scene, by
    /// Tests/Editor/PartyCameraFollowTests.cs -- these tests only cover what genuinely needs a real
    /// MonoBehaviour/scene.</summary>
    public class PartyCameraFollowBehaviourTests
    {
        private GameObject rigGo;
        private GameObject behaviourGo;

        private sealed class FakeSelectableSource : ISelectableSource
        {
            private readonly List<SelectableEntity> entities = new();
            public void Add(RuntimeEntityId id, Vector3 position) => entities.Add(new SelectableEntity(id, position));
            public IEnumerable<SelectableEntity> GetSelectables() => entities;
        }

        [TearDown]
        public void TearDown()
        {
            if (behaviourGo != null) Object.DestroyImmediate(behaviourGo);
            if (rigGo != null) Object.DestroyImmediate(rigGo);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(target, value);
        }

        private static object InvokePrivate(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

        private IsometricCameraRig CreateRig()
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

            var rig = rigGo.AddComponent<IsometricCameraRig>();
            SetField(rig, "yawPivot", yaw);
            SetField(rig, "pitchPivot", pitch);
            SetField(rig, "cameraTransform", cameraTransform);
            SetField(rig, "settings", ScriptableObject.CreateInstance<CRPGCameraSettings>());

            rigGo.SetActive(true);

            return rig;
        }

        private PartyCameraFollowBehaviour CreateBehaviour(IsometricCameraRig rig, CRPGCameraSettings settings)
        {
            behaviourGo = new GameObject("PartyCameraFollow");
            var behaviour = behaviourGo.AddComponent<PartyCameraFollowBehaviour>();
            SetField(behaviour, "cameraRig", rig);
            SetField(behaviour, "settings", settings);
            return behaviour;
        }

        [UnityTest]
        public IEnumerator InitializeSnapsToTheActiveCharacterImmediately()
        {
            var rig = CreateRig();
            var settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
            var behaviour = CreateBehaviour(rig, settings);

            var character = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { character });
            var selectables = new FakeSelectableSource();
            var position = new Vector3(8f, 0f, -2f);
            selectables.Add(character, position);

            behaviour.Initialize(party, selectables);

            Assert.AreEqual(position, rig.Rig.FocusPoint);
            yield break;
        }

        [UnityTest]
        public IEnumerator FollowSelectionFalseLeavesTheRigUntouchedAcrossManyTicks()
        {
            var rig = CreateRig();
            var settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
            SetField(settings, "followSelection", false);
            var behaviour = CreateBehaviour(rig, settings);

            var character = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { character });
            var selectables = new FakeSelectableSource();
            selectables.Add(character, new Vector3(50f, 0f, 50f));
            behaviour.Initialize(party, selectables); // snaps once regardless, then FollowSelection gates further ticks
            var afterInitialize = rig.Rig.FocusPoint;

            rig.Rig.SnapTo(Vector3.zero); // move the rig away so a subsequent tick would visibly change it if not gated
            for (var i = 0; i < 10; i++)
            {
                InvokePrivate(behaviour, "LateUpdate");
                yield return null;
            }

            Assert.AreEqual(Vector3.zero, rig.Rig.FocusPoint); // never moved back toward the character
            Assert.AreNotEqual(Vector3.zero, afterInitialize); // sanity: Initialize really did snap somewhere else first
        }

        [UnityTest]
        public IEnumerator FollowSelectionTrueTicksTowardTheActiveCharacter()
        {
            var rig = CreateRig();
            var settings = ScriptableObject.CreateInstance<CRPGCameraSettings>();
            SetField(settings, "followSelection", true);
            var behaviour = CreateBehaviour(rig, settings);

            var character = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { character });
            var selectables = new FakeSelectableSource();
            selectables.Add(character, new Vector3(50f, 0f, 0f));
            behaviour.Initialize(party, selectables);
            rig.Rig.SnapTo(Vector3.zero);

            InvokePrivate(behaviour, "LateUpdate");

            Assert.Greater(rig.Rig.FocusPoint.x, 0f);
            yield break;
        }
    }
}
