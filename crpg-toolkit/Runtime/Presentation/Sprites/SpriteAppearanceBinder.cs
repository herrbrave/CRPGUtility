using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// 2D campaign Task 2.4: applies a character's saved/authored appearance (<see cref="CharacterState.Appearance"/>, layer slot
    /// to option id) to its layered sprite view through the catalogs registered in the rules database. Applied at
    /// <see cref="Initialize"/> and on <see cref="Refresh"/>; there is no event subscription, so a caller that changes the
    /// appearance AFTER the view is bound must call <see cref="Refresh"/> (a composition root normally sets it before binding).
    /// Holds the <see cref="SpriteLayerCompositor"/> and reapplies when a model swap brings a new one, like SpriteEquipmentBinder.
    /// It does not own the appearance, so <see cref="Unbind"/> leaves the layers as they are. An unknown option degrades to an
    /// empty layer with a report; nothing here throws for content problems.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class SpriteAppearanceBinder : MonoBehaviour
    {
        private RuntimeEntityId character;
        private ICharacterRegistry characters;
        private IRulesDatabase rulesDatabase;

        private SpriteLayerCompositor compositor;
        private SpriteLayerState explicitTarget;
        private SpriteLayerState syncedState;
        private bool warnedNoTarget;
        private bool warnedMissingCharacter;

        public bool IsBound => characters != null;

        public void Initialize(RuntimeEntityId character, ICharacterRegistry characters, IRulesDatabase rulesDatabase)
        {
            if (this.characters != null)
            {
                if (this.character == character && ReferenceEquals(this.characters, characters) && ReferenceEquals(this.rulesDatabase, rulesDatabase))
                    return;

                throw new InvalidOperationException(
                    $"{name} is already bound to {this.character} -- call Unbind() before binding to a different character/session.");
            }

            if (!character.IsValid || characters == null || rulesDatabase == null)
            {
                Debug.LogWarning($"[SpriteAppearanceBinder] Cannot bind '{name}': it needs a valid character, character registry and rules database.", this);
                return;
            }

            this.character = character;
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            Apply();
        }

        /// <summary>Forgets the binding. Leaves the layers as they are (appearance is not owned by this binder).</summary>
        public void Unbind()
        {
            character = RuntimeEntityId.None;
            characters = null;
            rulesDatabase = null;
            syncedState = null;
            warnedNoTarget = false;
            warnedMissingCharacter = false;
        }

        /// <summary>Re-reads <see cref="CharacterState.Appearance"/> and the catalogs and reapplies them.</summary>
        public void Refresh() => Apply();

        /// <summary>Explicit target for a consumer with no compositor (a UI preview). Pass null to resolve a compositor again.</summary>
        public void SetTarget(SpriteLayerState target)
        {
            explicitTarget = target;
            Apply();
        }

        private void OnDestroy() => Unbind();

        private void LateUpdate()
        {
            if (characters == null) return;

            var state = ResolveState();
            if (state == null)
            {
                if (!warnedNoTarget)
                {
                    warnedNoTarget = true;
                    Debug.LogWarning($"[SpriteAppearanceBinder] '{name}' has no SpriteLayerCompositor and no explicit target; appearance will not be shown.", this);
                }

                return;
            }

            warnedNoTarget = false;
            if (!ReferenceEquals(state, syncedState)) Apply(); // a model swap brought a fresh compositor
        }

        private SpriteLayerState ResolveState()
        {
            if (explicitTarget != null) return explicitTarget;
            if (compositor == null) compositor = GetComponentInChildren<SpriteLayerCompositor>(true); // also true once destroyed
            return compositor != null ? compositor.State : null;
        }

        private void Apply()
        {
            if (characters == null) return;

            var state = ResolveState();
            syncedState = state;
            if (state == null) return;

            if (!characters.TryGetCharacter(character, out var characterState))
            {
                if (!warnedMissingCharacter)
                {
                    warnedMissingCharacter = true;
                    Debug.LogWarning($"[SpriteAppearanceBinder] Character {character} is not in the registry; no appearance applied to '{name}'.", this);
                }

                return;
            }

            warnedMissingCharacter = false;
            state.ApplyAppearance(characterState.Appearance, new SpriteCatalogSource(rulesDatabase));
        }
    }
}
