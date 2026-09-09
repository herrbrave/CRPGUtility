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
            StoreInventoryState store = null)
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

        /// <summary>Set by OpenStoreAction. The host reads this after driving the runner (same pattern as
        /// checking CurrentNode/IsComplete) to know it should show the store screen for Store.</summary>
        public bool StoreOpenRequested { get; private set; }

        public void RequestOpenStore() => StoreOpenRequested = true;
    }
}
