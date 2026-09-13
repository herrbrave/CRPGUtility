using System.Linq;
using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional quest journal: active quests, then completed ones, each with objective progress and
    /// reward list. Read-only (no commands -- turn-in happens through dialogue/QuestService, Task 3.4). Built
    /// procedurally, same first-pass placeholder convention as Camp/Store/Party. Builds into an
    /// externally-supplied container region (see HudRoot) rather than owning its own UIDocument -- Task 3.6's
    /// HUD consolidation.
    /// </summary>
    public sealed class QuestLogView : MonoBehaviour
    {
        private QuestLogPresenter presenter;
        private IGameEventBus eventBus;
        private VisualElement container;
        private VisualElement questContainer;

        public ITextLocalizer Localizer { get; set; }

        public void Initialize(QuestLogPresenter questLogPresenter, VisualElement hostContainer, IGameEventBus questEventBus = null)
        {
            Unsubscribe();

            presenter = questLogPresenter;
            container = hostContainer;
            eventBus = questEventBus;
            BuildLayout();
            Refresh();
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        // Task 3.6: quest state changes from dialogue/gameplay, not from this view's own buttons (it has
        // none) -- subscribes to the shared event bus rather than needing a bespoke QuestLog.Changed event.
        private void Subscribe()
        {
            if (eventBus == null) return;
            eventBus.Subscribe(GameEvent.QuestStarted, HandleQuestEvent);
            eventBus.Subscribe(GameEvent.QuestCompleted, HandleQuestEvent);
            eventBus.Subscribe(GameEvent.QuestObjectiveCompleted, HandleQuestEvent);
        }

        private void Unsubscribe()
        {
            if (eventBus == null) return;
            eventBus.Unsubscribe(GameEvent.QuestStarted, HandleQuestEvent);
            eventBus.Unsubscribe(GameEvent.QuestCompleted, HandleQuestEvent);
            eventBus.Unsubscribe(GameEvent.QuestObjectiveCompleted, HandleQuestEvent);
        }

        private void HandleQuestEvent(GameEventContext context) => Refresh();

        private void BuildLayout()
        {
            container.Clear();
            container.style.paddingLeft = 16;
            container.style.paddingTop = 16;
            container.style.paddingRight = 16;
            container.style.paddingBottom = 16;
            container.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            container.style.color = Color.white;

            var title = new Label("Quest Log");
            title.style.fontSize = 28;
            title.style.marginBottom = 16;
            container.Add(title);

            questContainer = new VisualElement();
            container.Add(questContainer);
        }

        // Composition roots also refresh after loot/dialogue operations without equipment events.
        public void Refresh()
        {
            questContainer.Clear();

            questContainer.Add(new Label("Active") { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
            foreach (var quest in presenter.GetActiveQuests()) questContainer.Add(BuildQuestCard(quest));

            questContainer.Add(new Label("Completed") { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginTop = 16, marginBottom = 8 } });
            foreach (var quest in presenter.GetCompletedQuests()) questContainer.Add(BuildQuestCard(quest));
        }

        private VisualElement BuildQuestCard(QuestSummary quest)
        {
            var card = new VisualElement { style = { marginBottom = 12, paddingLeft = 8, borderLeftWidth = 2, borderLeftColor = Color.gray } };

            card.Add(new Label(quest.DisplayName) { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold } });
            if (!string.IsNullOrEmpty(quest.Description)) card.Add(new Label(quest.Description));

            foreach (var objective in quest.Objectives)
            {
                var check = objective.IsComplete ? "[x]" : "[ ]";
                card.Add(new Label($"{check} {objective.DisplayName} ({objective.CurrentCount}/{objective.TargetCount})"));
            }

            if (quest.Rewards.Count > 0)
            {
                var rewardText = string.Join(", ", quest.Rewards.Select(r => $"{TextKeyResolver.Resolve(r.ItemName, Localizer)} x{r.Quantity}"));
                card.Add(new Label($"Reward: {rewardText}"));
            }

            return card;
        }
    }
}
