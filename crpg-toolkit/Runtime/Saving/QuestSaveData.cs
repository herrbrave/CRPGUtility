using System;
using System.Collections.Generic;
using CRPG.Quests;

namespace CRPG.Saving
{
    [Serializable]
    public sealed class QuestObjectiveSaveEntry
    {
        public string ObjectiveId;
        public int CurrentCount;
        public bool IsComplete;
    }

    [Serializable]
    public sealed class QuestSaveData
    {
        public string QuestId;
        public QuestStatus Status;
        public List<QuestObjectiveSaveEntry> Objectives = new();
    }
}
