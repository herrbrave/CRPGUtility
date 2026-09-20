using UnityEngine;

namespace CRPG.Saving.Migrations
{
    /// <summary>
    /// v2 -> v3 (2D campaign Task 2.4). Purely additive: CharacterSaveData gained an Appearance list. Deliberately an
    /// IDENTITY transform apart from the version number -- unlike V1ToV2Migration's Level fixup, an absent appearance
    /// list correctly defaults to empty (JsonUtility keeps the field initializer), so no value needs repairing.
    /// <para>The bump still matters: without it a v3-content save would be indistinguishable from v2, an older build would
    /// load it, silently drop the appearance and write that loss back on its next save. With it, SaveFileStore refuses a
    /// newer-version save cleanly (UnsupportedNewerVersion).</para>
    /// <para>Note the pre-existing SaveMigrationRunner.ReadFormatVersion asymmetry: JSON with no Header at all reads as the
    /// current version (and then fails as Corrupt), whereas a Header with no FormatVersion reads as 0 (NoMigrationPath).</para>
    /// </summary>
    public sealed class V2ToV3Migration : ISaveMigration
    {
        public int FromVersion => 2;

        public string Migrate(string json)
        {
            var data = JsonUtility.FromJson<SaveData>(json);
            data.Header.FormatVersion = 3;
            return JsonUtility.ToJson(data, prettyPrint: true);
        }
    }
}
