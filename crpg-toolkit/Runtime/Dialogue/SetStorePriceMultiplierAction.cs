using System;
using UnityEngine;

namespace CRPG.Dialogue
{
    /// <summary>Changes DialogueContext.Store's price multipliers -- e.g. a merchant's prices improving once a
    /// Timeline flag a BranchNode already checked (like "cave-cleared") is true. Tied to the exact dialogue
    /// moment that noticed the change, rather than a background GameEventBus subscription -- GameEvent.
    /// QuestCompleted carries no "which quest" data today, so a subscriber can't tell them apart anyway, and
    /// this is more narratively coherent besides (prices change because the merchant comments on it, not via
    /// invisible wiring). A no-op if this NPC doesn't run a store.</summary>
    [Serializable]
    public sealed class SetStorePriceMultiplierAction : DialogueAction
    {
        [SerializeField] private float buyPriceMultiplier = 1f;
        [SerializeField] private float sellPriceMultiplier = 0.5f;

        public override void Execute(DialogueContext context)
        {
            if (context.Store == null) return;
            context.Store.BuyPriceMultiplier = buyPriceMultiplier;
            context.Store.SellPriceMultiplier = sellPriceMultiplier;
        }
    }
}
