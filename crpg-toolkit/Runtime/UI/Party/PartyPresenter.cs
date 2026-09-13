using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Party;

namespace CRPG.UI
{
    /// <summary>
    /// Pure data/command layer for a roster/selection panel during exploration -- distinct from CampPresenter
    /// (the rest-screen summary view). Reads/commands PartyState directly: PartyState already owns ALL
    /// selection/active-character state (Members/Selected/ActiveCharacter, Select/SelectMany/ToggleSelect/
    /// SetActive/CycleActiveNext/Previous), including world-driven changes from click/drag-box input via
    /// PartyExplorationController -- there is no separate Gameplay-layer selection state to reconcile.
    /// </summary>
    public sealed class PartyPresenter
    {
        private readonly IGameplayCueSink cueSink;
        private PartyState party;
        private ICharacterRegistry characters;
        private ResourceDefinition hitPointsResource;
        private IReadOnlyDictionary<RuntimeEntityId, string> memberNames;

        public PartyPresenter(
            PartyState party,
            ICharacterRegistry characters,
            ResourceDefinition hitPointsResource,
            IReadOnlyDictionary<RuntimeEntityId, string> memberNames,
            IGameplayCueSink cueSink = null)
        {
            this.party = party;
            this.characters = characters;
            this.hitPointsResource = hitPointsResource;
            this.memberNames = memberNames;
            this.cueSink = cueSink;
        }

        /// <summary>Task 3.6: called by the composition root after a successful load hands back a new session's
        /// PartyState/ICharacterRegistry -- a plain method rather than a shared ISessionConsumer interface,
        /// since ISessionConsumer lives in CRPG.Gameplay and CRPG.UI must never reference it (CRPG.Gameplay
        /// already references CRPG.UI; the reverse would be a cycle). Every type this takes already lives in an
        /// assembly CRPG.UI references.</summary>
        public void Rebind(PartyState party, ICharacterRegistry characters)
        {
            this.party = party;
            this.characters = characters;
        }

        public PartyState Party => party;

        public IReadOnlyList<PartyMemberSelectionSummary> GetMembers()
        {
            var summaries = new List<PartyMemberSelectionSummary>();
            foreach (var memberId in party.Members)
            {
                var currentHp = 0;
                var maxHp = 0;
                if (characters.TryGetCharacter(memberId, out var character) && character.TryGetResource(hitPointsResource.Id, out var hpState))
                {
                    currentHp = hpState.Current;
                    maxHp = hpState.Max;
                }

                var name = memberNames.TryGetValue(memberId, out var resolvedName) ? resolvedName : memberId.ToString();
                summaries.Add(new PartyMemberSelectionSummary(
                    memberId, TextKey.Literal(name), party.IsSelected(memberId), party.ActiveCharacter == memberId, currentHp, maxHp));
            }

            return summaries;
        }

        public void SelectCharacter(RuntimeEntityId id) => party.Select(id);
        public void ToggleSelect(RuntimeEntityId id) => party.ToggleSelect(id);
        public void SelectAll() => party.SelectAll();
        public void SelectGroup(string groupId) => party.SelectGroup(groupId);
        public void CycleActiveNext() => party.CycleActiveNext();
        public void CycleActivePrevious() => party.CycleActivePrevious();

        /// <summary>Pre-checks selection so a click on an unselected portrait reports a real outcome instead of
        /// letting PartyState.SetActive's InvalidOperationException reach a button handler.</summary>
        public PartyCommandResult SetActive(RuntimeEntityId id)
        {
            if (!party.IsSelected(id)) return PartyCommandResult.NotSelected();

            party.SetActive(id);
            cueSink?.Raise(new GameplayCue("party.set_active", id));
            return PartyCommandResult.Ok();
        }
    }
}
