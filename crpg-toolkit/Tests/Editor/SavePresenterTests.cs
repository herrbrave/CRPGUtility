using System;
using System.IO;
using CRPG.Saving;
using CRPG.UI;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SavePresenterTests
    {
        private sealed class FakeSaveRequestPort : ISaveRequestPort
        {
            public string LastPath;
            public SaveRequestResult NextResult = SaveRequestResult.Ok();
            public SaveRequestResult RequestSave(string path) { LastPath = path; return NextResult; }
        }

        private sealed class FakeLoadRequestPort : ILoadRequestPort
        {
            public bool IsLoading { get; set; }
            public string LastPath;
            public LoadOutcomeReport NextReport = new(LoadOutcome.Success, null);
            public void RequestLoad(string path, Action<LoadOutcomeReport> onFinished) { LastPath = path; onFinished(NextReport); }
        }

        private string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "crpg-savepresenter-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }

        [Test]
        public void GetSlotsReflectsTheCatalog()
        {
            var presenter = new SavePresenter(new FakeSaveRequestPort(), new FakeLoadRequestPort(), tempDir, slotCount: 2);

            var slots = presenter.GetSlots();

            Assert.AreEqual(2, slots.Count);
            Assert.IsFalse(slots[0].IsOccupied);
        }

        [Test]
        public void RequestSaveDelegatesToThePortWithTheSlotsPath()
        {
            var savePort = new FakeSaveRequestPort();
            var presenter = new SavePresenter(savePort, new FakeLoadRequestPort(), tempDir);

            var result = presenter.RequestSave(1);

            Assert.AreEqual(SaveRequestOutcome.Success, result.Outcome);
            Assert.AreEqual(SaveSlotCatalog.SlotPath(tempDir, 1), savePort.LastPath);
        }

        [Test]
        public void RequestLoadDelegatesToThePortAndReportsTheOutcome()
        {
            var loadPort = new FakeLoadRequestPort();
            var presenter = new SavePresenter(new FakeSaveRequestPort(), loadPort, tempDir);

            LoadOutcomeReport received = null;
            presenter.RequestLoad(2, r => received = r);

            Assert.AreEqual(SaveSlotCatalog.SlotPath(tempDir, 2), loadPort.LastPath);
            Assert.IsTrue(received.Success);
        }

        [Test]
        public void IsLoadingReflectsThePort()
        {
            var loadPort = new FakeLoadRequestPort { IsLoading = true };
            var presenter = new SavePresenter(new FakeSaveRequestPort(), loadPort, tempDir);

            Assert.IsTrue(presenter.IsLoading);
        }
    }
}
