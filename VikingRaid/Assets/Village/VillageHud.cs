using System.Linq;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Quests;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace VikingRaid.Village
{
    /// <summary>
    /// The village's one-corner HUD: the party's gold, a line of news (a quest started or advanced, a night's rest) that fades, and the toolkit's quest log on a key (J). Without it quests and money are invisible:
    /// the village scene has no other UI. Draws under the conversation and shop panels (they share the panel settings), lets world clicks through everywhere but the quest log while it is open, and registers with
    /// <see cref="UIToolkitPointerCapture"/> so a click on it never moves the party.
    /// </summary>
    public sealed class VillageHud : MonoBehaviour
    {
        private const float ToastSeconds = 5f;

        private GameSession session;
        private UIDocument document;
        private Label gold;
        private Label toast;
        private VisualElement logPanel;
        private QuestLogView logView;
        private float toastUntil;

        public string GoldText => gold?.text ?? string.Empty;
        public string ToastText => toast?.text ?? string.Empty;
        public bool QuestLogVisible => logPanel != null && logPanel.resolvedStyle.display != DisplayStyle.None && logPanel.style.display != DisplayStyle.None;

        public void Initialize(GameSession gameSession, PanelSettings panelSettings)
        {
            session = gameSession;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = -10;   // under the conversation and shop panels that share these settings

            var root = document.rootVisualElement;
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;

            gold = new Label("Gold: 0") { pickingMode = PickingMode.Ignore };
            gold.style.position = Position.Absolute;
            gold.style.left = 16;
            gold.style.top = 12;
            Style(gold, 18, FontStyle.Bold);
            root.Add(gold);

            var hint = new Label("J  quest log") { pickingMode = PickingMode.Ignore };
            hint.style.position = Position.Absolute;
            hint.style.left = 16;
            hint.style.top = 38;
            Style(hint, 12, FontStyle.Normal);
            hint.style.opacity = 0.7f;
            root.Add(hint);

            toast = new Label { pickingMode = PickingMode.Ignore };
            toast.style.position = Position.Absolute;
            toast.style.top = 16;
            toast.style.alignSelf = Align.Center;
            toast.style.left = new Length(30, LengthUnit.Percent);
            toast.style.right = new Length(30, LengthUnit.Percent);
            toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            Style(toast, 16, FontStyle.Bold);
            toast.style.display = DisplayStyle.None;
            root.Add(toast);

            logPanel = new VisualElement();
            logPanel.style.position = Position.Absolute;
            logPanel.style.left = 16;
            logPanel.style.top = 70;
            logPanel.style.width = 420;
            logPanel.style.maxHeight = new Length(70, LengthUnit.Percent);
            logPanel.style.display = DisplayStyle.None;
            root.Add(logPanel);

            logView = gameObject.AddComponent<QuestLogView>();
            logView.Initialize(new QuestLogPresenter(session.Quests, session.RulesDatabase), logPanel, session.EventBus);

            session.EventBus.Subscribe(GameEvent.QuestStarted, OnQuestStarted);
            session.EventBus.Subscribe(GameEvent.QuestObjectiveCompleted, OnObjectiveCompleted);
            session.EventBus.Subscribe(GameEvent.QuestCompleted, OnQuestCompleted);
            session.EventBus.Subscribe(GameEvent.LongRest, OnLongRest);
            Refresh();
        }

        private static void Style(Label label, int size, FontStyle weight)
        {
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = weight;
            label.style.color = new Color(0.94f, 0.91f, 0.85f, 1f);
            label.style.paddingLeft = 8;
            label.style.paddingRight = 8;
            label.style.paddingTop = 3;
            label.style.paddingBottom = 3;
            label.style.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 0.6f);
        }

        private void OnEnable() => UIToolkitPointerCapture.Register(GetComponent<UIDocument>());

        private void OnDisable() => UIToolkitPointerCapture.Unregister(GetComponent<UIDocument>());

        private void Update()
        {
            if (session == null) return;
            Refresh();

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.jKey.wasPressedThisFrame) ToggleQuestLog();
            if (toast.style.display != DisplayStyle.None && Time.unscaledTime > toastUntil) toast.style.display = DisplayStyle.None;
        }

        private void Refresh() => gold.text = $"Gold: {session.Inventory.CountShared(VillageBootstrap.GoldItemId)}";

        public void ToggleQuestLog()
        {
            var show = logPanel.style.display == DisplayStyle.None;
            logPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) logView.Refresh();   // the view refreshes on quest events, not on objective counts
        }

        public void ShowToast(string text)
        {
            toast.text = text;
            toast.style.display = DisplayStyle.Flex;
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private string QuestName(GameEventContext context) =>
            context.Data.TryGetValue("QuestId", out var id) && session.RulesDatabase.TryResolve<QuestDefinition>(new DefinitionId((string)id), out var quest) ? quest.DisplayName : "a quest";

        private void OnQuestStarted(GameEventContext context) => ShowToast($"Quest started: {QuestName(context)}");

        private void OnObjectiveCompleted(GameEventContext context) => ShowToast($"Objective done: {QuestName(context)}");

        private void OnQuestCompleted(GameEventContext context)
        {
            ShowToast($"Quest complete: {QuestName(context)}");
            logView.Refresh();
        }

        private void OnLongRest(GameEventContext context) => ShowToast("The party rests and wakes refreshed.");

        private void OnDestroy()
        {
            if (session == null) return;
            session.EventBus.Unsubscribe(GameEvent.QuestStarted, OnQuestStarted);
            session.EventBus.Unsubscribe(GameEvent.QuestObjectiveCompleted, OnObjectiveCompleted);
            session.EventBus.Unsubscribe(GameEvent.QuestCompleted, OnQuestCompleted);
            session.EventBus.Unsubscribe(GameEvent.LongRest, OnLongRest);
        }
    }
}
