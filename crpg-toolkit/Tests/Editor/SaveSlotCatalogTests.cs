using System;
using System.IO;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Quests;
using CRPG.Saving;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SaveSlotCatalogTests
    {
        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "crpg-saveslot-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }

        [Test]
        public void AnUnwrittenSlotReportsEmpty()
        {
            var slots = SaveSlotCatalog.ListSlots(tempDir, slotCount: 3);

            Assert.AreEqual(3, slots.Count);
            foreach (var slot in slots) Assert.AreEqual(SaveSlotStatus.Empty, slot.Status);
        }

        [Test]
        public void AWrittenSlotReportsOccupiedWithATimestamp()
        {
            var data = SaveService.Capture(new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog());
            var path = SaveSlotCatalog.SlotPath(tempDir, 0);
            new SaveFileStore().Save(path, SaveService.ToJson(data));

            var slots = SaveSlotCatalog.ListSlots(tempDir, slotCount: 1);

            Assert.AreEqual(SaveSlotStatus.Occupied, slots[0].Status);
            Assert.IsTrue(slots[0].SavedAtUtc.HasValue);
        }

        [Test]
        public void ACorruptSlotReportsCorruptNotEmpty()
        {
            var path = SaveSlotCatalog.SlotPath(tempDir, 0);
            File.WriteAllText(path, "{ not valid json");

            var slots = SaveSlotCatalog.ListSlots(tempDir, slotCount: 1);

            Assert.AreEqual(SaveSlotStatus.Corrupt, slots[0].Status);
        }

        [Test]
        public void ANewerFormatVersionSlotIsDistinguishedFromCorrupt()
        {
            var data = SaveService.Capture(new PartyState(), new CharacterRegistry(), new PartyInventoryState(), new Timeline(), new QuestLog());
            data.Header.FormatVersion = SaveData.CurrentFormatVersion + 1;
            var path = SaveSlotCatalog.SlotPath(tempDir, 0);
            File.WriteAllText(path, SaveService.ToJson(data));

            var slots = SaveSlotCatalog.ListSlots(tempDir, slotCount: 1);

            Assert.AreEqual(SaveSlotStatus.UnsupportedNewerVersion, slots[0].Status);
        }
    }
}
