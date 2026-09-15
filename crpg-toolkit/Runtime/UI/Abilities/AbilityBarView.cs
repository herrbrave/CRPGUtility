using System;
using CRPG.Abilities;
using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional ability bar for the current turn's character -- built procedurally, same first-pass
    /// placeholder convention as PartyView/CampView. Deliberately knows nothing about
    /// <c>CRPG.Gameplay.ActionScheduler</c>/<c>AbilityTargetingGate</c>/<c>TacticalEncounterController</c> --
    /// CRPG.UI must never reference CRPG.Gameplay (the reverse reference already exists). Every button click
    /// only raises a plain event; the Gameplay-layer <c>TacticalCombatUIBridge</c> is the sole place player
    /// intent crosses into real scheduler/targeting authority.
    /// </summary>
    public sealed class AbilityBarView : MonoBehaviour
    {
        private AbilityBarPresenter presenter;
        private VisualElement container;
        private VisualElement abilitiesContainer;
        private Button deathSaveButton;

        private RuntimeEntityId currentCharacter;
        private bool hasCurrentCharacter;

        public event Action<RuntimeEntityId, AbilityDefinition> AbilityChosen;
        public event Action EndTurnRequested;
        public event Action<RuntimeEntityId> RollDeathSaveRequested;

        public void Initialize(AbilityBarPresenter abilityBarPresenter, VisualElement hostContainer)
        {
            presenter = abilityBarPresenter;
            container = hostContainer;
            BuildLayout();
            Refresh();
        }

        /// <summary>Called by the composition root whenever whose turn it is changes (or the current
        /// character's resource/known-ability state changes) -- this view never polls, and never reaches into
        /// CRPG.Gameplay to find out on its own.</summary>
        public void SetCurrentCharacter(RuntimeEntityId character)
        {
            currentCharacter = character;
            hasCurrentCharacter = true;
            Refresh();
        }

        public void ClearCurrentCharacter()
        {
            hasCurrentCharacter = false;
            Refresh();
        }

        public void Refresh()
        {
            if (abilitiesContainer == null) return; // BuildLayout hasn't run yet

            abilitiesContainer.Clear();

            if (!hasCurrentCharacter)
            {
                deathSaveButton.style.display = DisplayStyle.None;
                return;
            }

            if (presenter.IsIncapacitated(currentCharacter))
            {
                deathSaveButton.style.display = DisplayStyle.Flex;
                return;
            }

            deathSaveButton.style.display = DisplayStyle.None;
            foreach (var entry in presenter.GetAbilities(currentCharacter)) abilitiesContainer.Add(BuildAbilityButton(entry));
        }

        private VisualElement BuildAbilityButton(AbilityBarEntrySummary entry)
        {
            var button = new Button(() => AbilityChosen?.Invoke(currentCharacter, entry.Ability)) { text = entry.Ability.DisplayName };
            button.SetEnabled(entry.CanAttempt);
            button.style.marginRight = 8;
            if (!entry.CanAttempt) button.tooltip = entry.DisabledReason;
            return button;
        }

        private void BuildLayout()
        {
            container.Clear();
            container.style.paddingLeft = 16;
            container.style.paddingTop = 16;
            container.style.paddingRight = 16;
            container.style.paddingBottom = 16;
            container.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);

            abilitiesContainer = new VisualElement();
            abilitiesContainer.style.flexDirection = FlexDirection.Row;
            abilitiesContainer.style.marginBottom = 8;
            container.Add(abilitiesContainer);

            deathSaveButton = new Button(() => RollDeathSaveRequested?.Invoke(currentCharacter)) { text = "Roll Death Save" };
            deathSaveButton.style.display = DisplayStyle.None;
            deathSaveButton.style.marginBottom = 8;
            container.Add(deathSaveButton);

            var endTurnButton = new Button(() => EndTurnRequested?.Invoke()) { text = "End Turn" };
            container.Add(endTurnButton);
        }
    }
}
