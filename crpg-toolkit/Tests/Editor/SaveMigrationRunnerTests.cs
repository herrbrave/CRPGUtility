using System.Collections.Generic;
using CRPG.Saving;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    public class SaveMigrationRunnerTests
    {
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
