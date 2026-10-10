using CRPG.Gameplay;
using UnityEngine;

namespace VikingRaid.Appearance
{
    /// <summary>
    /// Dresses a character from <c>CharacterState.Appearance</c> (the (slot, option) pairs the toolkit copies from <c>NpcDefinition.appearance</c> at spawn, and saves). Sits on CharacterRoot next to
    /// <c>ModelActionCues</c> and self-binds the same way (the toolkit's <c>WorldSpawnService.BindView</c> only initialises binders it knows about): in <c>Start</c>, so no frame shows the master's
    /// default look, with <c>Update</c> as the fallback for an agent bound later. Call <see cref="Refresh"/> after changing the character's appearance.
    /// </summary>
    public sealed class ModelAppearanceBinder : MonoBehaviour
    {
        [SerializeField] private ModelAppearanceCatalog catalog;

        private bool applied;

        public ModelAppearanceCatalog Catalog { get => catalog; set => catalog = value; }
        public bool IsApplied => applied;

        private void Start() => TryApply();

        private void Update()
        {
            if (!applied) TryApply();
        }

        /// <summary>Re-reads the character's appearance and dresses it again.</summary>
        public void Refresh()
        {
            applied = false;
            TryApply();
        }

        private void TryApply()
        {
            if (catalog == null) return;
            var agent = GetComponent<CharacterAgent>();
            if (agent == null || !agent.IsBound || agent.Session == null) return;
            if (!agent.Session.Characters.TryGetCharacter(agent.EntityId, out var state)) return;

            ModelAppearanceApplier.Apply(gameObject, catalog, state.Appearance);
            applied = true;
        }
    }
}
