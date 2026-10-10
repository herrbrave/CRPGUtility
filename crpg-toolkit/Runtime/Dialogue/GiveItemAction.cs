using System;
using CRPG.Core;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Dialogue
{
    [Serializable]
    public sealed class GiveItemAction : DialogueAction
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int quantity = 1;

        public ItemDefinition Item => item;
        public int Quantity => quantity;

        public override void Execute(DialogueContext context)
        {
            var instance = new ItemInstance(RuntimeEntityId.NewId(), item.Id, quantity);
            context.PartyInventory.AddItem(instance);
        }
    }
}
