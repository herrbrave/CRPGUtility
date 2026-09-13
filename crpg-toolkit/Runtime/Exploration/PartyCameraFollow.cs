using CRPG.Camera;
using CRPG.Gameplay;
using CRPG.Party;

namespace CRPG.Exploration
{
    /// <summary>
    /// Bridges <see cref="PartyState"/>'s active character to a <see cref="CameraRigState"/>'s smoothed
    /// follow, without <c>CRPG.Camera</c> itself ever knowing about Party -- matches
    /// <see cref="PartyExplorationController"/>'s own "the input adapter doesn't know what a command means"
    /// separation, just for camera focus instead of movement commands. Matches <see cref="PartyState"/>'s own
    /// documented intent ("only the active character drives... camera focus") rather than a selected-group
    /// centroid.
    /// </summary>
    public sealed class PartyCameraFollow
    {
        private readonly PartyState party;
        private readonly ISelectableSource selectableSource;
        private readonly CameraRigState rig;

        public PartyCameraFollow(PartyState party, ISelectableSource selectableSource, CameraRigState rig)
        {
            this.party = party;
            this.selectableSource = selectableSource;
            this.rig = rig;
        }

        /// <summary>Smoothly follows the active character's current position. A no-op if there is no active
        /// character, or it isn't currently found in <paramref name="selectableSource"/> (destroyed, not yet
        /// spawned, ...) -- never guesses at a position, never throws.</summary>
        public void Tick(float deltaTime)
        {
            if (!TryGetActiveCharacterPosition(out var position)) return;
            rig.FollowTarget(position, deltaTime);
        }

        /// <summary>Instantly snaps to the active character's current position -- the explicit teleport/
        /// initial-load entry point. A no-op under the same "missing means don't guess" rule as <see cref="Tick"/>.</summary>
        public void SnapToActiveCharacter()
        {
            if (TryGetActiveCharacterPosition(out var position)) rig.SnapTo(position);
        }

        private bool TryGetActiveCharacterPosition(out UnityEngine.Vector3 position)
        {
            position = default;
            var active = party.ActiveCharacter;
            if (!active.IsValid) return false;

            foreach (var entity in selectableSource.GetSelectables())
            {
                if (entity.Id != active) continue;
                position = entity.WorldPosition;
                return true;
            }

            return false;
        }
    }
}
