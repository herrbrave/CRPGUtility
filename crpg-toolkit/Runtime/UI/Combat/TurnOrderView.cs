using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional turn-order/round display -- built procedurally, same first-pass placeholder
    /// convention as PartyView/CampView. Builds into an externally-supplied container region (see HudRoot)
    /// rather than owning its own UIDocument.
    /// </summary>
    public sealed class TurnOrderView : MonoBehaviour
    {
        private TurnOrderPresenter presenter;
        private IGameEventBus eventBus;
        private VisualElement container;
        private Label roundLabel;
        private VisualElement orderContainer;

        public void Initialize(TurnOrderPresenter turnOrderPresenter, VisualElement hostContainer, IGameEventBus gameEventBus)
        {
            Unsubscribe();

            presenter = turnOrderPresenter;
            eventBus = gameEventBus;
            container = hostContainer;
            BuildLayout();
            Refresh();
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (eventBus == null) return;
            eventBus.Subscribe(GameEvent.CombatStarted, OnChanged);
            eventBus.Subscribe(GameEvent.TurnStarted, OnChanged);
            eventBus.Subscribe(GameEvent.TurnEnded, OnChanged);
            eventBus.Subscribe(GameEvent.CombatEnded, OnChanged);
        }

        private void Unsubscribe()
        {
            if (eventBus == null) return;
            eventBus.Unsubscribe(GameEvent.CombatStarted, OnChanged);
            eventBus.Unsubscribe(GameEvent.TurnStarted, OnChanged);
            eventBus.Unsubscribe(GameEvent.TurnEnded, OnChanged);
            eventBus.Unsubscribe(GameEvent.CombatEnded, OnChanged);
        }

        private void OnChanged(GameEventContext context) => Refresh();

        private void BuildLayout()
        {
            container.Clear();
            container.style.paddingLeft = 16;
            container.style.paddingTop = 16;
            container.style.paddingRight = 16;
            container.style.paddingBottom = 16;
            container.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);

            roundLabel = new Label();
            roundLabel.style.fontSize = 20;
            roundLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            roundLabel.style.color = Color.white;
            roundLabel.style.marginBottom = 8;
            container.Add(roundLabel);

            orderContainer = new VisualElement();
            container.Add(orderContainer);
        }

        private void Refresh()
        {
            roundLabel.text = presenter.HasActiveCombat ? $"Round {presenter.Round}" : "No combat in progress";

            orderContainer.Clear();
            foreach (var entry in presenter.GetTurnOrder()) orderContainer.Add(BuildPill(entry));
        }

        private static VisualElement BuildPill(TurnOrderEntrySummary entry)
        {
            var pill = new VisualElement();
            pill.style.marginBottom = 4;
            pill.style.flexShrink = 0;
            if (entry.IsCurrentTurn) pill.style.backgroundColor = new Color(0.9f, 0.8f, 0.2f, 0.25f);
            pill.style.paddingLeft = 8;
            pill.style.paddingRight = 8;
            pill.style.paddingTop = 4;
            pill.style.paddingBottom = 4;
            pill.style.borderTopWidth = entry.IsCurrentTurn ? 2 : 1;
            pill.style.borderBottomWidth = entry.IsCurrentTurn ? 2 : 1;
            pill.style.borderLeftWidth = entry.IsCurrentTurn ? 2 : 1;
            pill.style.borderRightWidth = entry.IsCurrentTurn ? 2 : 1;
            var borderColor = entry.IsCurrentTurn ? Color.yellow : Color.gray;
            pill.style.borderTopColor = borderColor;
            pill.style.borderBottomColor = borderColor;
            pill.style.borderLeftColor = borderColor;
            pill.style.borderRightColor = borderColor;

            var label = new Label(entry.DisplayName);
            label.style.color = Color.white;
            // A caller with no real display-name lookup falls back to RuntimeEntityId's own 32-character
            // string (see TurnOrderPresenter.GetTurnOrder) -- confirmed live: without NoWrap, a narrow HUD
            // region wraps that into a dense wall of short fragments rather than clipping/overflowing legibly.
            label.style.whiteSpace = WhiteSpace.NoWrap;
            pill.Add(label);
            return pill;
        }
    }
}
