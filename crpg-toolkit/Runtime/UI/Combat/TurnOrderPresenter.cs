using System.Collections.Generic;
using CRPG.Combat;
using CRPG.Core;

namespace CRPG.UI
{
    /// <summary>
    /// Task 4.5: pure data layer for a tactical turn-order display, mirroring QuestLogPresenter/PartyPresenter's
    /// established plain-presenter shape. Depends only on <see cref="CombatEncounterService"/> (already
    /// legitimately referenced by CRPG.UI) -- never on <c>CRPG.Gameplay</c>'s <c>TacticalEncounterController</c>,
    /// which would cycle back (Gameplay -&gt; UI already exists).
    /// </summary>
    public sealed class TurnOrderPresenter
    {
        private CombatEncounterService encounterService;
        private readonly IReadOnlyDictionary<RuntimeEntityId, string> displayNames;

        public TurnOrderPresenter(CombatEncounterService encounterService, IReadOnlyDictionary<RuntimeEntityId, string> displayNames = null)
        {
            this.encounterService = encounterService;
            this.displayNames = displayNames;
        }

        public bool HasActiveCombat => encounterService.Encounter != null;
        public int Round => encounterService.Encounter?.Round ?? 0;
        public RuntimeEntityId? CurrentTurnId => encounterService.Encounter?.CurrentTurn?.Id;

        public IReadOnlyList<TurnOrderEntrySummary> GetTurnOrder()
        {
            var entries = new List<TurnOrderEntrySummary>();
            var encounter = encounterService.Encounter;
            if (encounter == null) return entries;

            var currentId = encounter.CurrentTurn?.Id;
            foreach (var combatant in encounter.Combatants)
            {
                var name = displayNames != null && displayNames.TryGetValue(combatant.Id, out var resolved) ? resolved : combatant.Id.ToString();
                entries.Add(new TurnOrderEntrySummary(combatant.Id, name, combatant.Faction, currentId.HasValue && currentId.Value == combatant.Id));
            }

            return entries;
        }

        /// <summary>Called by the composition root if the underlying encounter service instance is ever
        /// replaced (a new encounter) -- mirrors PartyPresenter/QuestLogPresenter's own Rebind precedent.</summary>
        public void Rebind(CombatEncounterService encounterService) => this.encounterService = encounterService;
    }
}
