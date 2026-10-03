using System;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>Starts a quest the first time a real party member walks into it (e.g. entering the Glimmering Caverns).
    /// Starting is idempotent -- the quest log keeps the existing state -- so re-entering does nothing. There is no
    /// "location entered" hook in the shell, so a small trigger volume wired by <c>ShellBootstrap</c> is the mechanism.</summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class QuestStartTrigger : MonoBehaviour
    {
        [SerializeField] private string questId;

        public GameSession Session { get; set; }
        public Action<string> RequestStartQuest { get; set; }

        public string QuestId => questId;

        private void OnTriggerEnter(Collider other)
        {
            if (Session == null || RequestStartQuest == null) return;

            // Same rule as EncounterTrigger: locations share world coordinates and the next level loads additively while the
            // party is still leaving this one, so only the ACTIVE location's volumes may act.
            if (gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) return;

            var agent = other.GetComponentInParent<CharacterAgent>();
            if (agent == null || !agent.IsBound || !Session.Party.Members.Contains(agent.EntityId)) return;

            RequestStartQuest(questId);
        }
    }
}
