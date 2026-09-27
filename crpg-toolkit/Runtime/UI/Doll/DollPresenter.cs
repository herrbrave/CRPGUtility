using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;

namespace CRPG.UI
{
    /// <summary>One anatomical equip slot on the doll: an id, its display label, and whatever item currently fills
    /// it (null when empty).</summary>
    public readonly struct DollSlotSummary
    {
        public DollSlotSummary(DefinitionId slotId, TextKey slotName, InventoryItemSummary item)
        {
            SlotId = slotId;
            SlotName = slotName;
            Item = item;
        }

        public DefinitionId SlotId { get; }
        public TextKey SlotName { get; }

        /// <summary>Null when the slot is empty (InventoryItemSummary is a reference type, not a value type --
        /// this is a plain nullable reference, not System.Nullable&lt;T&gt;).</summary>
        public InventoryItemSummary Item { get; }
        public bool HasItem => Item != null;
    }

    /// <summary>One ability-score row: its abbreviation, read from the ruleset's own StatDefinition rather than a
    /// hard-coded string, and the character's current score.</summary>
    public readonly struct DollAbilitySummary
    {
        public DollAbilitySummary(DefinitionId statId, string abbreviation, int score)
        {
            StatId = statId;
            Abbreviation = abbreviation;
            Score = score;
        }

        public DefinitionId StatId { get; }
        public string Abbreviation { get; }
        public int Score { get; }
    }

    /// <summary>
    /// Pure data/command layer for the paper-doll screen: the five fixed anatomical equip slots (D-5.2.1 -- a
    /// deliberately fixed set, not every EquipmentSlotDefinition the ruleset happens to define), the six ability
    /// scores, current/max HP, the shared backpack, and which party member the doll currently shows. Wraps
    /// InventoryPresenter for every inventory read/command rather than duplicating its summary-building or
    /// equip/unequip validation (Task 5.2 review, finding 4). No UI Toolkit dependency, like every other Presenter
    /// in CRPG.UI.
    /// <para>Cycling does NOT use PartyPresenter.CycleActiveNext/Previous: normal play always has exactly one
    /// member selected (every portrait click collapses selection to one via PartyState.Select), so that pair
    /// cycles over a one-element "selected" subset and is a no-op -- a Task 5.2 review finding. This instead walks
    /// party.Members directly and calls PartyState.Select on the next one, which already both selects it and makes
    /// it active in one call (see PartyState.Select's own doc comment) -- calling SetActive afterwards would only
    /// double-fire PartyState.Changed for one logical operation.</para>
    /// </summary>
    public sealed class DollPresenter
    {
        /// <summary>The doll's fixed slot set, in display order. Confirmed against BarrowsEndGearAuthor: every
        /// shipped equippable item's EquipmentSlotId is one of these five (Task 5.2 review, blocker T-1: the
        /// original draft used "slot-clothing", not the real "slot-clothing-body").</summary>
        public static readonly IReadOnlyList<DefinitionId> AnatomicalSlots = new[]
        {
            new DefinitionId("slot-head"),
            new DefinitionId("slot-armor"),
            new DefinitionId("slot-mainhand"),
            new DefinitionId("slot-offhand"),
            new DefinitionId("slot-clothing-body"),
        };

        /// <summary>The six ability scores, in display order. Public so a view can size its stat-block layout
        /// without duplicating this list (DollView does exactly this).</summary>
        public static readonly IReadOnlyList<DefinitionId> AbilityStatIds = new[]
        {
            new DefinitionId("stat-strength"), new DefinitionId("stat-dexterity"), new DefinitionId("stat-constitution"),
            new DefinitionId("stat-intelligence"), new DefinitionId("stat-wisdom"), new DefinitionId("stat-charisma"),
        };

        private PartyState party;
        private ICharacterRegistry characters;
        private IRulesDatabase rules;
        private readonly InventoryPresenter inventoryPresenter;
        private readonly ResourceDefinition hitPointsResource;

        public DollPresenter(
            PartyState party,
            ICharacterRegistry characters,
            IRulesDatabase rules,
            InventoryPresenter inventoryPresenter,
            ResourceDefinition hitPointsResource)
        {
            this.party = party ?? throw new ArgumentNullException(nameof(party));
            this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.inventoryPresenter = inventoryPresenter ?? throw new ArgumentNullException(nameof(inventoryPresenter));
            this.hitPointsResource = hitPointsResource ?? throw new ArgumentNullException(nameof(hitPointsResource));
            CurrentCharacter = ResolveStart(party);
        }

        /// <summary>The party member the doll currently shows.</summary>
        public RuntimeEntityId CurrentCharacter { get; private set; }

        /// <summary>Total party membership (not just selected) -- lets a view disable its cycle arrows for a
        /// one-member party instead of cycling harmlessly to the same character.</summary>
        public int MemberCount => party.Members.Count;

        /// <summary>Re-points at the party's current active character. The doll always opens on it (D-5.2.1):
        /// the active member may have changed via the HUD roster while the doll was closed.</summary>
        public void SyncToActiveCharacter()
        {
            var active = party.ActiveCharacter;
            if (active.IsValid) CurrentCharacter = active;
        }

        /// <summary>Called by the composition root after a successful load hands back a new session's state --
        /// matches PartyPresenter.Rebind/InventoryPresenter.Rebind's precedent (a plain method rather than a
        /// shared ISessionConsumer interface, since CRPG.UI must never reference CRPG.Gameplay).</summary>
        public void Rebind(PartyState newParty, ICharacterRegistry newCharacters, IRulesDatabase newRules, PartyInventoryState newInventory, EquipmentService equipmentService)
        {
            party = newParty ?? throw new ArgumentNullException(nameof(newParty));
            characters = newCharacters ?? throw new ArgumentNullException(nameof(newCharacters));
            rules = newRules ?? throw new ArgumentNullException(nameof(newRules));
            inventoryPresenter.Rebind(newInventory, newRules, equipmentService);
            CurrentCharacter = ResolveStart(party);
        }

        public void CycleNext() => Cycle(1);
        public void CyclePrevious() => Cycle(-1);

        public IReadOnlyList<DollSlotSummary> GetAnatomicalSlots()
        {
            var summaries = new List<DollSlotSummary>(AnatomicalSlots.Count);
            var equipped = CurrentCharacter.IsValid ? inventoryPresenter.GetEquippedItems(CurrentCharacter) : Array.Empty<InventoryItemSummary>();

            foreach (var slotId in AnatomicalSlots)
            {
                var slotName = rules.TryResolve<EquipmentSlotDefinition>(slotId, out var slotDef)
                    ? TextKey.Literal(slotDef.DisplayName)
                    : TextKey.Literal(slotId.Value);

                InventoryItemSummary item = null;
                foreach (var summary in equipped)
                {
                    if (summary.RequiredSlot != slotId) continue;
                    item = summary;
                    break;
                }

                summaries.Add(new DollSlotSummary(slotId, slotName, item));
            }

            return summaries;
        }

        public IReadOnlyList<DollAbilitySummary> GetAbilityScores()
        {
            var summaries = new List<DollAbilitySummary>(AbilityStatIds.Count);
            if (!characters.TryGetCharacter(CurrentCharacter, out var character)) return summaries;

            foreach (var statId in AbilityStatIds)
            {
                var abbreviation = rules.TryResolve<StatDefinition>(statId, out var stat) ? stat.Abbreviation : statId.Value;
                summaries.Add(new DollAbilitySummary(statId, abbreviation, character.Stats.GetScore(statId)));
            }

            return summaries;
        }

        public (int current, int max) GetHitPoints()
        {
            if (CurrentCharacter.IsValid && characters.TryGetCharacter(CurrentCharacter, out var character) && character.TryGetResource(hitPointsResource.Id, out var hp))
                return (hp.Current, hp.Max);
            return (0, 0);
        }

        public IReadOnlyList<InventoryItemSummary> GetBackpackItems() => inventoryPresenter.GetSharedItems();

        public InventoryOperationResult Equip(RuntimeEntityId instanceId, DefinitionId slot) => inventoryPresenter.Equip(instanceId, CurrentCharacter, slot);
        public InventoryOperationResult Unequip(RuntimeEntityId instanceId) => inventoryPresenter.Unequip(instanceId);

        private static RuntimeEntityId ResolveStart(PartyState party)
        {
            if (party.ActiveCharacter.IsValid) return party.ActiveCharacter;
            return party.Members.Count > 0 ? party.Members[0] : RuntimeEntityId.None;
        }

        private void Cycle(int direction)
        {
            var members = party.Members;
            if (members.Count == 0) return;

            var index = IndexOf(members, CurrentCharacter);
            index = index < 0 ? 0 : ((index + direction) % members.Count + members.Count) % members.Count;
            CurrentCharacter = members[index];
            party.Select(CurrentCharacter); // Select() both selects and activates -- see the class doc comment.
        }

        private static int IndexOf(IReadOnlyList<RuntimeEntityId> members, RuntimeEntityId id)
        {
            for (var i = 0; i < members.Count; i++)
                if (members[i] == id) return i;
            return -1;
        }
    }
}
