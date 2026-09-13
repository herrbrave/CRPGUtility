using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;

namespace CRPG.Dialogue
{
    /// <summary>
    /// The participants and shared services one conversation needs (spec's DialogueContext: Speaker, Listener,
    /// Party, WorldState -- WorldState is Timeline here, per the session's spec deviation). NpcId is who the
    /// party is talking to; the player side is always the whole Party, since checks can be posed to
    /// "the active character" or "the best party member" rather than a single fixed PC.
    /// </summary>
    public sealed class DialogueContext
    {
        public DialogueContext(
            RuntimeEntityId npcId,
            PartyState party,
            ICharacterRegistry characters,
            IRulesDatabase rulesDatabase,
            ITimeline timeline,
            PartyInventoryState partyInventory,
            IDiceService diceService,
            ICombatLog combatLog = null,
            StoreInventoryState store = null,
            DefinitionId currencyItemId = default)
        {
            NpcId = npcId;
            Party = party;
            Characters = characters;
            RulesDatabase = rulesDatabase;
            Timeline = timeline;
            PartyInventory = partyInventory;
            DiceService = diceService;
            CombatLog = combatLog;
            Store = store;
            CurrencyItemId = currencyItemId;
        }

        public RuntimeEntityId NpcId { get; }
        public PartyState Party { get; }
        public ICharacterRegistry Characters { get; }
        public IRulesDatabase RulesDatabase { get; }
        public ITimeline Timeline { get; }
        public PartyInventoryState PartyInventory { get; }
        public IDiceService DiceService { get; }
        public ICombatLog CombatLog { get; }

        /// <summary>The shop this NPC runs, if any -- null for an NPC that doesn't sell anything.</summary>
        public StoreInventoryState Store { get; }

        /// <summary>Which authored ItemDefinition this game treats as currency -- needed to build a StoreService
        /// for the conversation's store handoff. Only meaningful when Store is non-null; left DefinitionId.None
        /// (default) for an NPC that doesn't sell anything.</summary>
        public DefinitionId CurrencyItemId { get; }

        private bool storeOpenRequested;

        public void RequestOpenStore() => storeOpenRequested = true;

        /// <summary>Consume-once: returns true exactly once per RequestOpenStore() call, clearing the flag so a
        /// caller driving the runner in a loop (ConversationController) reacts to each store request exactly
        /// once instead of re-triggering on every subsequent check.</summary>
        public bool ConsumeStoreOpenRequest()
        {
            if (!storeOpenRequested) return false;
            storeOpenRequested = false;
            return true;
        }
    }
}
