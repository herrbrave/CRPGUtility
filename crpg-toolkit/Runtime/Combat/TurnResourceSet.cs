using System.Collections.Generic;
using CRPG.Actors;

namespace CRPG.Combat
{
    /// <summary>
    /// The ResourceDefinitions a ruleset uses as per-turn budgets (Action, Bonus Action, Reaction, Movement,
    /// or whatever a different ruleset calls them). The engine doesn't care what they're named -- starting a
    /// turn just means restoring all of these to full for the active combatant.
    /// </summary>
    public sealed class TurnResourceSet
    {
        public TurnResourceSet(IReadOnlyList<ResourceDefinition> resources)
        {
            Resources = resources;
        }

        public IReadOnlyList<ResourceDefinition> Resources { get; }

        public void RestoreFull(CharacterState character)
        {
            foreach (var resource in Resources)
            {
                if (character.TryGetResource(resource.Id, out var state)) state.RestoreToFull();
            }
        }
    }
}
