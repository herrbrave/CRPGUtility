namespace CRPG.Saving
{
    /// <summary>
    /// Upgrades a save's raw JSON from one SaveData.FormatVersion to the next, so a package update doesn't
    /// destroy customer save files (spec §51). Operates on raw JSON text rather than SaveData objects -- a
    /// save from an older format may have fields the current SaveData no longer declares (or lack ones it now
    /// requires), so text-level rewriting is what actually survives a schema change; deserializing straight
    /// into today's SaveData first would silently drop or default exactly the fields a migration needs to see.
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>The exact FormatVersion this migration knows how to upgrade from.</summary>
        int FromVersion { get; }

        /// <returns>JSON at FromVersion + 1.</returns>
        string Migrate(string json);
    }
}
