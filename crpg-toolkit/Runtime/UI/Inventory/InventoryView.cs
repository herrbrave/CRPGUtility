using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional inventory panel: the party's shared items, plus the currently-focused character's
    /// equipped items, with Equip/Unequip/Drop/Use buttons. Built procedurally, same first-pass placeholder
    /// convention as Camp/Store/Party. Builds into an externally-supplied container region (see HudRoot)
    /// rather than owning its own UIDocument -- Task 3.6's HUD consolidation.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        private InventoryPresenter presenter;
        private IGameEventBus eventBus;
        private VisualElement container;
        private VisualElement sharedContainer;
        private VisualElement equippedContainer;
        private Label statusLabel;

        public ITextLocalizer Localizer { get; set; }

        private RuntimeEntityId focusedCharacter = RuntimeEntityId.None;

        /// <summary>Which character's equipped items are shown / Equip targets -- the composition root keeps
        /// this in sync with the party's active character (e.g. PartyState.ActiveCharacter via PartyView).</summary>
        public RuntimeEntityId FocusedCharacter
        {
            get => focusedCharacter;
            set
            {
                focusedCharacter = value;
                if (presenter != null) Refresh();
            }
        }

        public void Initialize(InventoryPresenter inventoryPresenter, VisualElement hostContainer, IGameEventBus inventoryEventBus = null)
        {
            Unsubscribe();

            presenter = inventoryPresenter;
            container = hostContainer;
            eventBus = inventoryEventBus;
            BuildLayout();
            Refresh();
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        // Task 3.6: inventory changes via loot/store/quest rewards too, not just this view's own buttons --
        // subscribes to the shared event bus rather than needing a bespoke PartyInventoryState.Changed event.
        private void Subscribe()
        {
            if (eventBus == null) return;
            eventBus.Subscribe(GameEvent.ItemEquipped, HandleInventoryEvent);
            eventBus.Subscribe(GameEvent.ItemUnequipped, HandleInventoryEvent);
        }

        private void Unsubscribe()
        {
            if (eventBus == null) return;
            eventBus.Unsubscribe(GameEvent.ItemEquipped, HandleInventoryEvent);
            eventBus.Unsubscribe(GameEvent.ItemUnequipped, HandleInventoryEvent);
        }

        private void HandleInventoryEvent(GameEventContext context) => Refresh();

        private void BuildLayout()
        {
            container.Clear();
            container.style.paddingLeft = 16;
            container.style.paddingTop = 16;
            container.style.paddingRight = 16;
            container.style.paddingBottom = 16;
            container.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            container.style.color = Color.white;

            var title = new Label("Inventory");
            title.style.fontSize = 28;
            title.style.marginBottom = 8;
            container.Add(title);

            statusLabel = new Label();
            statusLabel.style.fontSize = 13;
            statusLabel.style.color = Color.red;
            statusLabel.style.marginBottom = 8;
            container.Add(statusLabel);

            var columns = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            container.Add(columns);

            sharedContainer = new VisualElement { style = { width = 320, marginRight = 32 } };
            columns.Add(sharedContainer);

            equippedContainer = new VisualElement { style = { width = 320 } };
            columns.Add(equippedContainer);
        }

        // Composition roots also refresh after loot/dialogue operations without equipment events.
        public void Refresh()
        {
            sharedContainer.Clear();
            sharedContainer.Add(new Label("Shared") { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
            foreach (var item in presenter.GetSharedItems()) sharedContainer.Add(BuildRow(item, showEquip: focusedCharacter.IsValid));

            equippedContainer.Clear();
            equippedContainer.Add(new Label("Equipped") { style = { fontSize = 18, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
            if (focusedCharacter.IsValid)
            {
                foreach (var item in presenter.GetEquippedItems(focusedCharacter)) equippedContainer.Add(BuildRow(item, showEquip: false));
            }
        }

        private VisualElement BuildRow(InventoryItemSummary item, bool showEquip)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6, alignItems = Align.Center } };

            var slotText = item.SlotName.HasValue ? $" [{TextKeyResolver.Resolve(item.SlotName.Value, Localizer)}]" : string.Empty;
            var label = new Label($"{TextKeyResolver.Resolve(item.Name, Localizer)} x{item.Quantity}{slotText}");
            label.style.flexGrow = 1;
            row.Add(label);

            if (item.IsEquipped)
            {
                row.Add(new Button(() => OnUnequipClicked(item.InstanceId)) { text = "Unequip" });
            }
            else if (showEquip && item.RequiredSlot.IsValid)
            {
                var equipButton = new Button(() => OnEquipClicked(item)) { text = "Equip" };
                row.Add(equipButton);
            }

            var useButton = new Button(() => OnUseClicked(item.InstanceId)) { text = "Use" };
            useButton.SetEnabled(presenter.HasUsableAbility(item.InstanceId));
            row.Add(useButton);

            if (!item.IsEquipped) row.Add(new Button(() => OnDropClicked(item.InstanceId)) { text = "Drop" });

            return row;
        }

        private void OnEquipClicked(InventoryItemSummary item) =>
            ShowResult(presenter.Equip(item.InstanceId, focusedCharacter, item.RequiredSlot));

        private void OnUnequipClicked(RuntimeEntityId instanceId) => ShowResult(presenter.Unequip(instanceId));
        private void OnDropClicked(RuntimeEntityId instanceId) => ShowResult(presenter.Drop(instanceId));
        private void OnUseClicked(RuntimeEntityId instanceId) => ShowResult(presenter.Use(instanceId, focusedCharacter));

        private void ShowResult(InventoryOperationResult result)
        {
            statusLabel.text = result.Success ? string.Empty : DescribeFailure(result.Outcome);
            Refresh();
        }

        private static string DescribeFailure(InventoryOperationOutcome outcome) => outcome switch
        {
            InventoryOperationOutcome.ItemNotFound => "That item isn't available.",
            InventoryOperationOutcome.NotEquippable => "That item can't be equipped.",
            InventoryOperationOutcome.WrongSlot => "That item doesn't fit that slot.",
            InventoryOperationOutcome.ItemEquipped => "Unequip that item first.",
            InventoryOperationOutcome.NotSupportedYet => "Not supported yet.",
            _ => "That didn't work.",
        };
    }
}
