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
        public int ActivationCursor;
    }

    [Serializable]
    public sealed class QuestSaveData
    {
        public string QuestId;
        public QuestStatus Status;
        public bool RewardClaimed;
        public List<QuestObjectiveSaveEntry> Objectives = new();
    }
}
