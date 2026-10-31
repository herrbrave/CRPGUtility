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

        /// <summary>Task 4.5: the target-independent subset of <see cref="Validate"/> -- whether
        /// <paramref name="source"/> can even attempt <paramref name="ability"/> right now (not incapacitated,
        /// knows it if gated, affords its costs, every required effect service is wired), before any real
        /// target has been chosen. A UI ability bar uses this for a button's enabled state; <see cref="Validate"/>
        /// is still the sole authority once a real target exists.</summary>
        AbilityValidationResult CanAttempt(RuntimeEntityId source, AbilityDefinition ability, bool asRitual = false, ResourceDefinition upcastWithSlot = null);

        /// <summary>Validates an incomplete ordered selection without admitting it for execution. Legacy implementations keep their existing validation.</summary>
        AbilityValidationResult ValidateTargetPick(RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets) => Validate(source, ability, targets);

        AbilityExecutionResult Execute(
            RuntimeEntityId source, AbilityDefinition ability, IReadOnlyList<RuntimeEntityId> targets,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null);

        /// <summary>The aimed counterpart of <see cref="Validate"/>: an ability pointed at the ground (a Point, a shaped area) is validated from its aim (range, line of sight, the game's aim surface); its targets are derived from the shape.</summary>
        AbilityValidationResult Validate(
            RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null);

        /// <summary>The creatures an aimed ability would hit from this aim, without validating the aim (for a preview).</summary>
        IReadOnlyList<RuntimeEntityId> ResolveTargets(RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim);

        AbilityExecutionResult Execute(
            RuntimeEntityId source, AbilityDefinition ability, AbilityAim aim,
            bool asRitual = false, ResourceDefinition upcastWithSlot = null);
    }
}
