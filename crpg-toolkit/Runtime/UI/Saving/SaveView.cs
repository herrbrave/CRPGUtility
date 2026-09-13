using CRPG.Saving;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// Minimal functional save/load panel: one row per slot, Save/Load buttons disabled for the duration of a
    /// request. Built procedurally, same first-pass placeholder convention as Camp/Store/Party/Inventory.
    /// Builds into an externally-supplied container region (see HudRoot) rather than owning its own
    /// UIDocument -- Task 3.6's HUD consolidation.
    /// </summary>
    public sealed class SaveView : MonoBehaviour
    {
        private SavePresenter presenter;
        private VisualElement container;
        private VisualElement slotContainer;
        private Label statusLabel;

        public ITextLocalizer Localizer { get; set; }

        public void Initialize(SavePresenter savePresenter, VisualElement hostContainer)
        {
            presenter = savePresenter;
            container = hostContainer;
            BuildLayout();
            Refresh();
        }

        private void BuildLayout()
        {
            container.Clear();
            container.style.paddingLeft = 16;
            container.style.paddingTop = 16;
            container.style.paddingRight = 16;
            container.style.paddingBottom = 16;
            container.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            container.style.color = Color.white;

            var title = new Label("Save / Load");
            title.style.fontSize = 28;
            title.style.marginBottom = 8;
            container.Add(title);

            statusLabel = new Label();
            statusLabel.style.fontSize = 13;
            statusLabel.style.color = Color.red;
            statusLabel.style.marginBottom = 8;
            container.Add(statusLabel);

            slotContainer = new VisualElement();
            container.Add(slotContainer);
        }

        public void Refresh()
        {
            slotContainer.Clear();
            foreach (var slot in presenter.GetSlots()) slotContainer.Add(BuildSlotRow(slot));
        }

        private VisualElement BuildSlotRow(SaveSlotSummary slot)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 6, alignItems = Align.Center } };

            var timestamp = slot.SavedAtUtc.HasValue ? slot.SavedAtUtc.Value.ToLocalTime().ToString("g") : string.Empty;
            var label = new Label($"Slot {slot.SlotIndex + 1}: {TextKeyResolver.Resolve(slot.StatusLabel, Localizer)} {timestamp}");
            label.style.flexGrow = 1;
            row.Add(label);

            var saveButton = new Button(() => OnSaveClicked(slot.SlotIndex)) { text = "Save" };
            saveButton.SetEnabled(!presenter.IsLoading);
            row.Add(saveButton);

            var loadButton = new Button(() => OnLoadClicked(slot.SlotIndex)) { text = "Load" };
            loadButton.SetEnabled(slot.IsOccupied && !presenter.IsLoading);
            row.Add(loadButton);

            return row;
        }

        private void OnSaveClicked(int slotIndex)
        {
            var result = presenter.RequestSave(slotIndex);
            statusLabel.text = result.Outcome == SaveRequestOutcome.Success ? string.Empty : DescribeSaveFailure(result);
            Refresh();
        }

        private void OnLoadClicked(int slotIndex)
        {
            statusLabel.text = string.Empty;
            Refresh(); // buttons disable immediately (IsLoading flips true inside RequestLoad)
            presenter.RequestLoad(slotIndex, report =>
            {
                statusLabel.text = report.Success ? string.Empty : $"Load failed: {report.Outcome}";
                Refresh();
            });
        }

        private static string DescribeSaveFailure(SaveRequestResult result) => result.Outcome switch
        {
            SaveRequestOutcome.Blocked => $"Can't save right now: {result.BlockReason}.",
            SaveRequestOutcome.WriteFailed => $"Save failed: {result.Message}",
            _ => "Save failed.",
        };
    }
}
