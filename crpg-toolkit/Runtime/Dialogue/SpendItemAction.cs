using System;
using CRPG.Inventory;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>
    /// Takes <see cref="Quantity"/> of <see cref="Item"/> from the party's shared inventory (an inn's price: spend gold, then rest). All or nothing, and a no-op when the party is short: put a
    /// <see cref="BranchNode"/> with <see cref="BranchSource.PartyItemCount"/> in front of it so the conversation can say "come back with coin" instead of silently doing nothing.
    /// </summary>
    [Serializable]
    public sealed class SpendItemAction : DialogueAction
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int quantity = 1;

        public ItemDefinition Item => item;
        public int Quantity => quantity;

        public override void Execute(DialogueContext context)
        {
            if (item == null || context.PartyInventory == null) return;
            context.PartyInventory.TryRemoveShared(item.Id, quantity);
        }
    }
}
