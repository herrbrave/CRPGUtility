using CRPG.Camera;
using CRPG.Gameplay;
using CRPG.Party;
using UnityEngine;

namespace CRPG.Exploration
{
    /// <summary>
    /// Scene-attachable owner of <see cref="PartyCameraFollow"/> -- the MonoBehaviour a real scene wires up
    /// (matches <see cref="CRPG.Gameplay.CharacterAgent"/>/<see cref="CRPG.Gameplay.GameSessionHost"/>'s own
    /// "MonoBehaviour hosts a plain-C# logic class" split). <see cref="Initialize"/> is called once whatever
    /// composes the session has a real <see cref="PartyState"/> and <see cref="ISelectableSource"/> to hand
    /// it, mirroring <c>CampView.Initialize(presenter)</c>'s existing pattern.
    /// </summary>
    /// <remarks>
    /// <see cref="DefaultExecutionOrder"/> is deliberately negative: Unity does not guarantee relative order
    /// between unrelated MonoBehaviours' LateUpdate methods, and this one's LateUpdate must run BEFORE
    /// <see cref="IsometricCameraRig"/>'s (default order) applies the rig state to real transforms -- getting
    /// this backwards would apply last frame's focus point instead of this frame's.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class PartyCameraFollowBehaviour : MonoBehaviour
    {
        [SerializeField] private IsometricCameraRig cameraRig;
        [SerializeField] private CRPGCameraSettings settings;

        private PartyCameraFollow follow;

        public void Initialize(PartyState party, ISelectableSource selectableSource)
        {
            follow = new PartyCameraFollow(party, selectableSource, cameraRig.Rig);
            follow.SnapToActiveCharacter(); // start AT the party instead of lerping in from the rig's default focus point
        }

        private void LateUpdate()
        {
            if (follow == null || cameraRig == null || settings == null) return;
            if (!settings.FollowSelection) return; // a live toggle, checked every tick, not just once at Initialize

            follow.Tick(Time.deltaTime);
        }
    }
}
