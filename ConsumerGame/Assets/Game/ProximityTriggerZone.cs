using System;
using CRPG.Gameplay;
using UnityEngine;

namespace CRPG.Consumer.Game
{
    /// <summary>
    /// Compact-scope substitute for the package's full interaction lifecycle (Task 3.3's
    /// InteractionController/WorldInteractable, click-to-interact) -- mirrors the simpler trigger-volume
    /// pattern this repo's own Oakhaven demo used the first time it needed "walk near something, something
    /// happens" (DialogueTriggerZone/TeleportTriggerZone), reimplemented here since ConsumerGame can't
    /// reference TestProject. Fires <see cref="PartyMemberEntered"/> exactly once (a bool guard, not
    /// unsubscribing the trigger) the first time any <see cref="CharacterAgent"/> enters range -- this compact
    /// scene only ever has party members walking around, so it never needs to distinguish "which faction."
    /// Requires a Rigidbody on at least one side of the pair (the character prefabs already have one) --
    /// Unity raises no trigger callbacks at all otherwise.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ProximityTriggerZone : MonoBehaviour
    {
        private bool fired;

        public event Action<CharacterAgent> PartyMemberEntered;

        private void OnTriggerEnter(Collider other)
        {
            if (fired) return;

            var agent = other.GetComponentInParent<CharacterAgent>();
            if (agent == null) return;

            fired = true;
            PartyMemberEntered?.Invoke(agent);
        }
    }
}
