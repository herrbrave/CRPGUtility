using System;
using CRPG.Abilities;
using CRPG.Core;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// What a tactical fight needs of an ability bar, so a game can supply its own presentation (the ship's native-kit dock) instead of the toolkit's procedural <see cref="AbilityBarView"/>. The bar knows nothing of the scheduler or the targeting gate: it only raises plain events,
    /// and the Gameplay-layer <c>TacticalCombatUIBridge</c> is the one place player intent crosses into real authority.
    /// </summary>
    public interface IAbilityBar
    {
        event Action<RuntimeEntityId, AbilityDefinition> AbilityChosen;
        event Action EndTurnRequested;
        event Action<RuntimeEntityId> RollDeathSaveRequested;

        /// <summary>Binds the bar to its data and the region it may build into (a bar that lives elsewhere may ignore the region).</summary>
        void Initialize(AbilityBarPresenter presenter, VisualElement hostContainer);

        /// <summary>Whose turn it is changed (or their abilities / resources did): show this character's abilities.</summary>
        void SetCurrentCharacter(RuntimeEntityId character);

        void ClearCurrentCharacter();

        /// <summary>Re-reads readiness (a spent action greys the buttons out).</summary>
        void Refresh();
    }
}
