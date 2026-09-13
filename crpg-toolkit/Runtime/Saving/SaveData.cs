using System;
using System.Collections.Generic;

namespace CRPG.Saving
{
    /// <summary>
    /// The root save document (spec §51): pure state, every definition referenced by a stable DefinitionId
    /// string rather than a direct asset reference, so a save survives content being reorganized. SceneState
    /// and CombatState from the spec's list are deliberately not here yet -- there's no scene-persistence
    /// system (which scene is loaded, where entities are positioned in the world) or combat-encounter
    /// serialization built to produce them, and spec itself marks CombatState as "if supported."
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        // Task 3.5: bumped 1 -> 2, a real schema change (six new CharacterSaveData fields plus header/world/
        // store/loot sections). See Migrations/V1ToV2Migration.cs.
        public const int CurrentFormatVersion = 2;

        public SaveHeader Header = new();
        public PartySaveData Party = new();
        public List<CharacterSaveData> Characters = new();
        public List<ItemSaveData> InventoryItems = new();
        public List<TimelineEventSaveData> TimelineEvents = new();
        public List<QuestSaveData> Quests = new();
        public List<WorldSpawnSaveEntry> WorldSpawns = new();
        public List<StoreSaveData> Stores = new();
        public List<LootContainerSaveData> LootContainers = new();
    }
}
