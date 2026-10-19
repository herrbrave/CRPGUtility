using CRPG.Core;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>What a tactical fight needs of a turn-order display, so a game can show the order in its own style. It refreshes itself on the combat events of the bus it is given.</summary>
    public interface ITurnOrderDisplay
    {
        void Initialize(TurnOrderPresenter presenter, VisualElement hostContainer, IGameEventBus eventBus);
    }
}
