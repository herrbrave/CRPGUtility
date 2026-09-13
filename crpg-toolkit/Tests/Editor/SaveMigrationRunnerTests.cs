using System.Collections.Generic;
using System.Linq;
using CRPG.Saving;
using CRPG.Saving.Migrations;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SaveMigrationRunnerTests
    {
        [Test]
        public void V1ToV2MigrationDefaultsLevelToOneNotTheClrDefaultOfZero()
        {
            // A real v1 save never had a Level field at all -- JsonUtility would otherwise leave it at the
            // CLR's default 0 rather than CharacterState's own real default of 1 (Task 3.5).
            var v1Character = new CharacterSaveData { Id = "char-1" };
            var v1Data = new SaveData { Header = { FormatVersion = 1 } };
            v1Data.Characters.Add(v1Character);
            var v1Json = SaveService.ToJson(v1Data);

            var migrated = new V1ToV2Migration().Migrate(v1Json);
            var v2Data = SaveService.FromJson(migrated);

            Assert.AreEqual(2, v2Data.Header.FormatVersion);
            Assert.AreEqual(1, v2Data.Characters.Single().Level);
        }

        [Test]
        public void V1ToV2MigrationPreservesAnAlreadyAuthoredLevel()
        {
            var v1Data = new SaveData { Header = { FormatVersion = 1 } };
            v1Data.Characters.Add(new CharacterSaveData { Id = "char-1", Level = 9 });
            var v1Json = SaveService.ToJson(v1Data);

            var migrated = new V1ToV2Migration().Migrate(v1Json);
            var v2Data = SaveService.FromJson(migrated);

            Assert.AreEqual(9, v2Data.Characters.Single().Level);
        }

        /// <summary>A synthetic "version 0" migration proving the mechanism actually rewrites JSON text and
        /// bumps FormatVersion -- no real version 0 format has ever shipped, this only exercises the runner.</summary>
        private sealed class RenameGoldToCurrencyMigration : ISaveMigration
        {
            public int FromVersion => 0;

            public string Migrate(string json) => json
                .Replace("\"OldGoldField\"", "\"NewCurrencyField\"")
                .Replace("\"FormatVersion\": 0", "\"FormatVersion\": 1");
        }

        [Test]
        public void ApplyRunsAMatchingMigrationAndStopsOnceNoMigrationMatchesTheNewVersion()
        {
            const string versionZeroJson = "{\"Header\":{\"FormatVersion\": 0,\"SavedAtUtc\":\"x\"},\"OldGoldField\":42}";
            var migrations = new List<ISaveMigration> { new RenameGoldToCurrencyMigration() };

            var result = SaveMigrationRunner.Apply(versionZeroJson, migrations);

            StringAssert.Contains("\"FormatVersion\": 1", result);
            StringAssert.Contains("\"NewCurrencyField\"", result);
            StringAssert.DoesNotContain("\"OldGoldField\"", result);
        }

        [Test]
        public void ApplyIsANoOpWhenTheSavesVersionAlreadyHasNoRegisteredMigration()
        {
            const string currentJson = "{\"Header\":{\"FormatVersion\": 1,\"SavedAtUtc\":\"x\"}}";
            var migrations = new List<ISaveMigration> { new RenameGoldToCurrencyMigration() }; // only handles version 0

            var result = SaveMigrationRunner.Apply(currentJson, migrations);

            Assert.AreEqual(currentJson, result);
        }
    }
}
