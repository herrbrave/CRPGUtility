using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Abilities
{
    /// <summary>
    /// The engine-side half of spec §17's resolution pipeline (Confirm onward: Consume Resources -&gt; Perform
    /// Rolls -&gt; Resolve Effects -&gt; Generate Rule Events -&gt; Complete). The earlier, interactive steps
    /// (Begin Targeting, incremental Validate Target(s) as the player picks) belong to a UI/presentation layer,
    /// which can call <see cref="Validate"/> as often as it needs while the player is still choosing -- it
    /// never mutates anything. Task 4.2: CanUse/CanUseAsRitual/CanUseAtSlotLevel/IsValidTargetSelection
    /// collapsed into this one method; Execute's return type changed from void to <see cref="AbilityExecutionResult"/>
    /// so a caller can distinguish success, an ordinary rejection, and an unexpected fault.
    /// </summary>
    public interface IAbilityResolutionService
    {
        AbilityValidationResult Validate(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null);

        AbilityExecutionResult Execute(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null);
    }
}
