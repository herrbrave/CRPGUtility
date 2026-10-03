using System;
using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using UnityEngine;

namespace BarrowsEnd.Gate
{
    /// <summary>Shows a one-line hint the first time a party member walks into the volume (e.g. "the portcullis is sealed -- find a
    /// lever"). Silent once <see cref="unlessKey"/> has occurred. Only the ACTIVE location's volumes speak (the same additive-scene
    /// overlap rule as the other triggers).</summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class HintTrigger : MonoBehaviour
    {
        [SerializeField, TextArea] private string message;
        [SerializeField] private string unlessKey = "";

        public Action<string> Notify { get; set; }
        public GameSession Session { get; set; }

        private bool shown;

        private void OnTriggerEnter(Collider other)
        {
            if (shown || Session == null || Notify == null) return;
            if (gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) return;
            if (!string.IsNullOrEmpty(unlessKey) && Session.Timeline.HasOccurred(unlessKey)) return;

            var agent = other.GetComponentInParent<CharacterAgent>();
            if (agent == null || !agent.IsBound || !Session.Party.Members.Contains(agent.EntityId)) return;

            shown = true;
            Notify(message);
        }
    }
}
