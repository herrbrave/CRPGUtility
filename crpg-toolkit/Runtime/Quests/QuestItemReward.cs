using System;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Quests
{
    /// <summary>One item a QuestDefinition grants on completion -- same shape as Dialogue's GiveItemAction.</summary>
    [Serializable]
    public sealed class QuestItemReward
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int quantity = 1;

        public ItemDefinition Item => item;
        public int Quantity => quantity;
    }
}
