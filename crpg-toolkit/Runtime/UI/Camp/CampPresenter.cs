using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;

namespace CRPG.UI
{
    /// <summary>
    /// Pure data/command layer for the Camp screen -- no UI Toolkit dependency, so the View (CampView, or any
    /// future replacement) can change without touching this. Long Rest lives behind this screen; Short Rest is
    /// meant to be callable from anywhere and doesn't need to come through here.
    /// </summary>
    public sealed class CampPresenter
    {
        private readonly PartyState party;
        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;
        private readonly PartyInventoryState inventory;
        private readonly RestService restService;
        private readonly ResourceDefinition hitPointsResource;
        private readonly IReadOnlyDictionary<RuntimeEntityId, string> memberNames;

        public CampPresenter(
            PartyState party,
            ICharacterRegistry characters,
            IRulesDatabase rulesDatabase,
            PartyInventoryState inventory,
            RestService restService,
            ResourceDefinition hitPointsResource,
            IReadOnlyDictionary<RuntimeEntityId, string> memberNames)
        {
            this.party = party;
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.inventory = inventory;
            this.restService = restService;
            this.hitPointsResource = hitPointsResource;
            this.memberNames = memberNames;
        }

        public IReadOnlyList<PartyMemberSummary> GetPartyMembers()
        {
            var summaries = new List<PartyMemberSummary>();

            foreach (var memberId in party.Members)
            {
                if (!characters.TryGetCharacter(memberId, out var character)) continue;

                var currentHp = 0;
                var maxHp = 0;
                if (character.TryGetResource(hitPointsResource.Id, out var hpState))
                {
                    currentHp = hpState.Current;
                    maxHp = hpState.Max;
                }

                var equippedSlots = new List<EquippedSlotSummary>();
                foreach (var equipped in inventory.ItemsEquippedBy(memberId))
                {
                    var location = inventory.GetLocation(equipped.InstanceId);
                    var slotName = rulesDatabase.TryResolve<EquipmentSlotDefinition>(location.Slot, out var slot)
                        ? slot.DisplayName
                        : location.Slot.ToString();
                    var itemName = rulesDatabase.TryResolve<ItemDefinition>(equipped.DefinitionId, out var item)
                        ? item.DisplayName
                        : equipped.DefinitionId.ToString();
                    equippedSlots.Add(new EquippedSlotSummary(slotName, itemName));
                }

                var name = memberNames.TryGetValue(memberId, out var resolvedName) ? resolvedName : memberId.ToString();
                summaries.Add(new PartyMemberSummary(name, currentHp, maxHp, equippedSlots));
            }

            return summaries;
        }

        public void ConfirmLongRest() => restService.Rest(party.Members, GameEvent.LongRest);
    }
}
