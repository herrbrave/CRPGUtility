using CRPG.Gameplay;
using UnityEngine;

namespace VikingRaid.Game
{
    /// <summary>
    /// Begins an encounter when a party member is inside its box. POLLS the overlap instead of relying on OnTriggerEnter (review finding 4, learned the hard way in BarrowsEnd Phase 6):
    /// E4's trigger stands in the gate, right beside E3's goblins, so a hero is usually already inside it when E3 ends, and an enter-once event would never fire again.
    /// Encounters fire strictly in order (only the next unwon one is eligible), so a trigger reached early simply waits.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VikingEncounterTrigger : MonoBehaviour
    {
        private const float PollSeconds = 0.1f;

        public VikingGame Game { get; set; }
        public int EncounterIndex { get; set; }
        public bool Armed { get; set; } = true;

        private BoxCollider box;
        private float nextPoll;
        private readonly Collider[] hits = new Collider[16];

        private void Awake() => box = GetComponent<BoxCollider>();

        private void Update()
        {
            if (!Armed || Game == null || Time.time < nextPoll || !Game.CanBeginEncounter(EncounterIndex)) return;
            nextPoll = Time.time + PollSeconds;
            if (AnyPartyMemberInside()) Game.BeginEncounter(EncounterIndex);
        }

        public bool AnyPartyMemberInside()
        {
            if (Game?.Session == null) return false;
            var centre = transform.TransformPoint(box.center);
            var half = Vector3.Scale(box.size, transform.lossyScale) * 0.5f;
            var count = Physics.OverlapBoxNonAlloc(centre, half, hits, transform.rotation, 1 << WorldLayers.Character, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var agent = hits[i].GetComponentInParent<CharacterAgent>();
                if (agent != null && agent.IsBound && Game.Session.Party.IsMember(agent.EntityId)) return true;
            }

            return false;
        }
    }
}
