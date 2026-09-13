using UnityEngine;

namespace CRPG.Saving.Migrations
{
    /// <summary>
    /// v1 -> v2 (Task 3.5). Purely additive schema change (six new CharacterSaveData fields, plus header/
    /// world/store/loot sections) -- nothing renamed or restructured, so this doesn't need SaveMigrationRunner's
    /// documented "raw JSON text" rewriting: deserializing straight into today's SaveData shape is safe here
    /// (JsonUtility silently defaults any field absent from the v1 JSON), then this fixes up the one
    /// semantically-WRONG default that produces -- CharacterSaveData.Level defaults to the CLR's 0 when absent
    /// from JSON, not CharacterState's own real default of 1, which would otherwise silently mis-level every
    /// restored v1 character. A future migration that renames/restructures a field would need real text-level
    /// manipulation; that's a bridge to cross when a change actually needs it, not preemptively built here.
    /// </summary>
    public sealed class V1ToV2Migration : ISaveMigration
    {
        public int FromVersion => 1;

        public string Migrate(string json)
        {
            var data = JsonUtility.FromJson<SaveData>(json);

            foreach (var character in data.Characters)
            {
                if (character.Level == 0) character.Level = 1;
            }

            data.Header.FormatVersion = 2;
            return JsonUtility.ToJson(data, prettyPrint: true);
        }
    }
}
