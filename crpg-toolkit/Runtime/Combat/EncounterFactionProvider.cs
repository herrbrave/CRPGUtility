using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>Task 4.5: the first real, shipped <see cref="IFactionProvider"/> -- none existed before this
    /// (the interface's own doc comment: "no shipped default implementation, faction storage is inherently
    /// game-specific"). Deliberately scoped to one live <see cref="CombatEncounterService"/>'s own per-encounter
    /// <see cref="Combatant.Faction"/> (set via <see cref="CombatEncounterService.StartCombat(System.Collections.Generic.IEnumerable{(RuntimeEntityId, DefinitionId)})"/>)
    /// -- NOT <c>CRPG.Gameplay.WorldSpawnService</c>'s separate out-of-combat faction store, which answers a
    /// different, standing question (see that class's own remarks). A combatant no longer in the encounter
    /// (removed via <see cref="CombatEncounter.RemoveCombatant"/>) has no faction here, matching
    /// <see cref="IFactionProvider"/>'s own "no data available must be distinguishable" contract.</summary>
    public sealed class EncounterFactionProvider : IFactionProvider
    {
        private readonly CombatEncounterService encounterService;

        public EncounterFactionProvider(CombatEncounterService encounterService)
        {
            this.encounterService = encounterService;
        }

        public bool TryGetFaction(RuntimeEntityId entity, out DefinitionId faction)
        {
            var encounter = encounterService.Encounter;
            if (encounter == null)
            {
                faction = default;
                return false;
            }

            foreach (var combatant in encounter.Combatants)
            {
                if (combatant.Id != entity) continue;
                faction = combatant.Faction;
                return true;
            }

            faction = default;
            return false;
        }
    }
}
