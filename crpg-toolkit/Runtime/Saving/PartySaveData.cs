using System;
using System.Collections.Generic;

namespace CRPG.Saving
{
    [Serializable]
    public sealed class PartyGroupSaveEntry
    {
        public string CharacterId;
        public string GroupId;
    }

    [Serializable]
    public sealed class PartySaveData
    {
        public List<string> MemberIds = new();
        public List<string> SelectedIds = new();
        public string ActiveCharacterId;
        public List<PartyGroupSaveEntry> Groups = new();
    }
}
