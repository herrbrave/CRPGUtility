using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CRPG.Saving
{
    public enum SaveSlotStatus { Empty, Occupied, Corrupt, UnsupportedNewerVersion, IoError }

    /// <summary>One save slot's probed state -- no multi-slot concept existed before Task 3.6 (everything was
    /// a caller-supplied path string). Status distinguishes what actually happened on a probe read rather than
    /// collapsing everything unreadable into "Corrupt": a newer-version or I/O-failed slot must not present as
    /// merely "empty" or "corrupt" when it's neither.</summary>
    public sealed class SaveSlot
    {
        public SaveSlot(string path, int slotIndex, DateTime? savedAtUtc, SaveSlotStatus status)
        {
            Path = path;
            SlotIndex = slotIndex;
            SavedAtUtc = savedAtUtc;
            Status = status;
        }

        public string Path { get; }
        public int SlotIndex { get; }
        public DateTime? SavedAtUtc { get; }
        public SaveSlotStatus Status { get; }
    }

    /// <summary>Enumerates a fixed-name set of save-slot files (save-slot-0.json, save-slot-1.json, ...),
    /// probing each via SaveFileStore.Load. Fine for a handful of local slots -- not built for scale (each
    /// ListSlots call re-reads every occupied slot's full file just to report its timestamp).</summary>
    public static class SaveSlotCatalog
    {
        public static string SlotPath(string directory, int slotIndex) => Path.Combine(directory, $"save-slot-{slotIndex}.json");

        public static IReadOnlyList<SaveSlot> ListSlots(string directory, int slotCount, SaveFileStore saveFileStore = null)
        {
            saveFileStore ??= new SaveFileStore();
            var slots = new List<SaveSlot>(slotCount);

            for (var i = 0; i < slotCount; i++)
            {
                var path = SlotPath(directory, i);
                if (!File.Exists(path))
                {
                    slots.Add(new SaveSlot(path, i, null, SaveSlotStatus.Empty));
                    continue;
                }

                var result = saveFileStore.Load(path);
                var status = result.Outcome switch
                {
                    SaveLoadOutcome.Success => SaveSlotStatus.Occupied,
                    SaveLoadOutcome.UnsupportedNewerVersion => SaveSlotStatus.UnsupportedNewerVersion,
                    SaveLoadOutcome.IoError => SaveSlotStatus.IoError,
                    SaveLoadOutcome.FileNotFound => SaveSlotStatus.Empty,
                    _ => SaveSlotStatus.Corrupt,
                };

                DateTime? savedAtUtc = null;
                if (result.Outcome == SaveLoadOutcome.Success &&
                    DateTime.TryParse(result.Data.Header.SavedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    savedAtUtc = parsed;
                }

                slots.Add(new SaveSlot(path, i, savedAtUtc, status));
            }

            return slots;
        }
    }
}
