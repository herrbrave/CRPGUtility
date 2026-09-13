using System;
using System.Collections.Generic;
using System.IO;
using CRPG.Saving;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SaveFileStoreTests
    {
        private string tempDir;
        private string savePath;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "crpg-savefilestore-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            savePath = Path.Combine(tempDir, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }

        private static string JsonFor(int level)
        {
            var data = new SaveData { Header = { FormatVersion = SaveData.CurrentFormatVersion } };
            data.Characters.Add(new CharacterSaveData { Id = "char-1", Level = level });
            return SaveService.ToJson(data);
        }

        [Test]
        public void SaveThenLoadRoundTrips()
        {
            var store = new SaveFileStore();
            var writeResult = store.Save(savePath, JsonFor(5));
            Assert.IsTrue(writeResult.Success);

            var loadResult = store.Load(savePath);

            Assert.AreEqual(SaveLoadOutcome.Success, loadResult.Outcome);
            Assert.AreEqual(5, loadResult.Data.Characters[0].Level);
        }

        [Test]
        public void LoadOfAMissingFileReturnsFileNotFound()
        {
            var store = new SaveFileStore();
            var result = store.Load(savePath);
            Assert.AreEqual(SaveLoadOutcome.FileNotFound, result.Outcome);
        }

        [Test]
        public void LoadOfCorruptJsonReturnsCorrupt()
        {
            File.WriteAllText(savePath, "{ not valid json at all !!");
            var store = new SaveFileStore();

            var result = store.Load(savePath);

            Assert.AreEqual(SaveLoadOutcome.Corrupt, result.Outcome);
        }

        [Test]
        public void LoadOfANewerUnsupportedVersionIsRejected()
        {
            var futureJson = SaveService.ToJson(new SaveData { Header = { FormatVersion = SaveData.CurrentFormatVersion + 1 } });
            File.WriteAllText(savePath, futureJson);
            var store = new SaveFileStore();

            var result = store.Load(savePath);

            Assert.AreEqual(SaveLoadOutcome.UnsupportedNewerVersion, result.Outcome);
        }

        [Test]
        public void LoadOfAVersionStuckBelowCurrentWithNoMigrationPathIsRejected()
        {
            // A save at FormatVersion 0 with no migration registered to reach CurrentFormatVersion -- distinct
            // from "already current" (Task 3.5's fix for SaveMigrationRunner.Apply silently passing this through).
            var stuckJson = "{\"Header\":{\"FormatVersion\": 0,\"SavedAtUtc\":\"x\"}}";
            File.WriteAllText(savePath, stuckJson);
            var store = new SaveFileStore(migrations: new List<ISaveMigration>()); // deliberately no migrations registered

            var result = store.Load(savePath);

            Assert.AreEqual(SaveLoadOutcome.NoMigrationPath, result.Outcome);
        }

        [Test]
        public void SecondSaveProducesACorrectBackupOfTheFirst()
        {
            var store = new SaveFileStore();
            store.Save(savePath, JsonFor(1));
            store.Save(savePath, JsonFor(2));

            var current = store.Load(savePath);
            var backup = store.LoadBackup(savePath);

            Assert.AreEqual(2, current.Data.Characters[0].Level);
            Assert.AreEqual(1, backup.Data.Characters[0].Level);
        }

        [Test]
        public void ACorruptExistingPrimaryIsNotRotatedOverAGoodBackup()
        {
            var store = new SaveFileStore();
            store.Save(savePath, JsonFor(1)); // real backup-worthy save
            store.Save(savePath, JsonFor(2)); // path now valid (2), .bak now valid (1)
            File.WriteAllText(savePath, "{ corrupt, not valid json"); // simulate external corruption of the primary only

            store.Save(savePath, JsonFor(3)); // the new save must not rotate the CORRUPT primary into .bak

            var current = store.Load(savePath);
            var backup = store.LoadBackup(savePath);

            Assert.AreEqual(3, current.Data.Characters[0].Level);
            Assert.AreEqual(1, backup.Data.Characters[0].Level, "The pre-corruption backup (Level 1) must survive untouched, not be overwritten by the corrupt primary.");
        }

        /// <summary>Fault-injection seam -- lets a test force a failure at an exact step a real filesystem can't
        /// reliably be coerced into on demand (Task 3.5, responding to "an invalid-path test does not prove
        /// failure safety when replacing the same existing target").</summary>
        private sealed class FaultInjectingFileIO : ISaveFileIO
        {
            private readonly ISaveFileIO real = new SystemFileIO();
            public bool FailOnReplace;

            public bool FileExists(string path) => real.FileExists(path);
            public string ReadAllText(string path) => real.ReadAllText(path);
            public void WriteAllText(string path, string contents) => real.WriteAllText(path, contents);

            public void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName)
            {
                if (FailOnReplace) throw new IOException("Simulated failure during atomic replace.");
                real.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
            }

            public void Move(string sourceFileName, string destinationFileName) => real.Move(sourceFileName, destinationFileName);
        }

        [Test]
        public void AFailureDuringReplaceLeavesBothThePrimaryAndBackupUnchanged()
        {
            var io = new FaultInjectingFileIO();
            var store = new SaveFileStore(io);
            store.Save(savePath, JsonFor(1)); // no existing file yet -- goes through Move, not Replace
            store.Save(savePath, JsonFor(2)); // path already exists and is well-formed -- rotates it into .bak via Replace

            var beforePrimary = File.ReadAllText(savePath);
            var backupPath = savePath + ".bak";
            var backupExistedBefore = File.Exists(backupPath);
            var beforeBackup = backupExistedBefore ? File.ReadAllText(backupPath) : null;

            io.FailOnReplace = true;
            var writeResult = store.Save(savePath, JsonFor(3)); // temp write succeeds; Replace throws

            Assert.IsFalse(writeResult.Success);
            Assert.AreEqual(beforePrimary, File.ReadAllText(savePath), "Primary file must be byte-for-byte unchanged after a failed replace.");
            Assert.AreEqual(backupExistedBefore, File.Exists(backupPath));
            if (backupExistedBefore) Assert.AreEqual(beforeBackup, File.ReadAllText(backupPath));
        }
    }
}
