using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Abilities
{
    /// <summary>
    /// The engine-side half of spec §17's resolution pipeline (Confirm onward: Consume Resources -&gt; Perform
    /// Rolls -&gt; Resolve Effects -&gt; Generate Rule Events -&gt; Complete). The earlier, interactive steps
    /// (Begin Targeting, incremental Validate Target(s) as the player picks) belong to a UI/presentation layer,
    /// which can call <see cref="CanUse"/> and <see cref="IsValidTargetSelection"/> as often as it needs while
    /// the player is still choosing.
    /// </summary>
    public interface IAbilityResolutionService
    {
        bool CanUse(RuntimeEntityId source, AbilityDefinition ability);
        bool CanUseAsRitual(RuntimeEntityId source, AbilityDefinition ability);
        bool CanUseAtSlotLevel(RuntimeEntityId source, AbilityDefinition ability, ResourceDefinition slotResource);
        bool IsValidTargetSelection(AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets);

        void Execute(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null);
    }
}
