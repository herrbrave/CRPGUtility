using System;
using System.Collections.Generic;
using CRPG.Saving;

namespace CRPG.UI
{
    /// <summary>
    /// Pure data/command layer for a save/load screen. Depends only on ISaveRequestPort/ILoadRequestPort/
    /// SaveSlotCatalog (all in CRPG.Saving) -- never on CRPG.Gameplay's SaveRequestService/SessionLoadCoordinator
    /// directly, since CRPG.Gameplay already references CRPG.UI and the reverse would be a cycle. The
    /// composition root wires the concrete Gameplay services in through these ports.
    /// </summary>
    public sealed class SavePresenter
    {
        private readonly ISaveRequestPort saveRequestPort;
        private readonly ILoadRequestPort loadRequestPort;
        private readonly string saveDirectory;
        private readonly int slotCount;
        private readonly IGameplayCueSink cueSink;

        public SavePresenter(ISaveRequestPort saveRequestPort, ILoadRequestPort loadRequestPort, string saveDirectory, int slotCount = 4, IGameplayCueSink cueSink = null)
        {
            this.saveRequestPort = saveRequestPort;
            this.loadRequestPort = loadRequestPort;
            this.saveDirectory = saveDirectory;
            this.slotCount = slotCount;
            this.cueSink = cueSink;
        }

        public bool IsLoading => loadRequestPort.IsLoading;

        public IReadOnlyList<SaveSlotSummary> GetSlots()
        {
            var slots = SaveSlotCatalog.ListSlots(saveDirectory, slotCount);
            var summaries = new List<SaveSlotSummary>(slots.Count);
            foreach (var slot in slots)
            {
                summaries.Add(new SaveSlotSummary(slot.SlotIndex, slot.SavedAtUtc, DescribeStatus(slot.Status), slot.Status == SaveSlotStatus.Occupied));
            }

            return summaries;
        }

        public SaveRequestResult RequestSave(int slotIndex)
        {
            var result = saveRequestPort.RequestSave(SaveSlotCatalog.SlotPath(saveDirectory, slotIndex));
            if (result.Outcome == SaveRequestOutcome.Success) cueSink?.Raise(new GameplayCue("save.completed"));
            return result;
        }

        public void RequestLoad(int slotIndex, Action<LoadOutcomeReport> onFinished)
        {
            loadRequestPort.RequestLoad(SaveSlotCatalog.SlotPath(saveDirectory, slotIndex), report =>
            {
                if (report.Success) cueSink?.Raise(new GameplayCue("load.completed"));
                onFinished(report);
            });
        }

        private static TextKey DescribeStatus(SaveSlotStatus status) => status switch
        {
            SaveSlotStatus.Empty => TextKey.Literal("Empty"),
            SaveSlotStatus.Occupied => TextKey.Literal("Saved"),
            SaveSlotStatus.UnsupportedNewerVersion => TextKey.Literal("From a newer version"),
            SaveSlotStatus.IoError => TextKey.Literal("Unreadable"),
            _ => TextKey.Literal("Corrupt"),
        };
    }
}
