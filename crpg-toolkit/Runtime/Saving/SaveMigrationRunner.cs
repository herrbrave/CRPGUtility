using System;
using System.Collections.Generic;
using UnityEngine;

namespace CRPG.Saving
{
    /// <summary>Repeatedly applies whichever registered ISaveMigration matches a save's current FormatVersion
    /// until none does (either it's caught up to SaveData.CurrentFormatVersion, or a version genuinely has no
    /// migration registered for it -- callers should treat that as an error, not silently proceed).</summary>
    public static class SaveMigrationRunner
    {
        public static string Apply(string json, IReadOnlyList<ISaveMigration> migrations)
        {
            while (true)
            {
                var version = ReadFormatVersion(json);
                var migration = FindMigration(migrations, version);
                if (migration == null) return json;
                json = migration.Migrate(json);
            }
        }

        /// <summary>Public so SaveFileStore can check the FINAL version after Apply -- Apply itself silently
        /// stops once no matching migration is found, whether that's because the save is genuinely caught up
        /// to CurrentFormatVersion or because there's a gap in the migration chain; the caller must tell those
        /// two cases apart (Task 3.5).</summary>
        public static int ReadFormatVersion(string json)
        {
            var partial = JsonUtility.FromJson<HeaderOnly>(json);
            return partial?.Header?.FormatVersion ?? SaveData.CurrentFormatVersion;
        }

        private static ISaveMigration FindMigration(IReadOnlyList<ISaveMigration> migrations, int version)
        {
            foreach (var migration in migrations)
            {
                if (migration.FromVersion == version) return migration;
            }

            return null;
        }

        [Serializable]
        private sealed class HeaderOnly
        {
            public SaveHeader Header;
        }
    }
}
