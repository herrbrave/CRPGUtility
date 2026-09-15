using CRPG.Abilities;
using CRPG.Core;
using CRPG.Gameplay.Actions;
using UnityEngine;

namespace CRPG.Gameplay.Combat
{
    /// <summary>
    /// Task 4.5: the first real implementation of <see cref="IPointerTargetingGate"/> (reserved by Task 3.1,
    /// never implemented until now) -- while armed with a chosen ability, consumes every click as that
    /// ability's target instead of letting <see cref="PlayerCommandRouter"/> fall through to ordinary
    /// Select/Move resolution. Mirrors <see cref="PlayerCommandRouter"/>'s own raycast-then-CharacterAgent
    /// pattern rather than depending on <c>ISelectableSource</c> (which is shaped for screen-space drag-box
    /// testing, not a single ray).
    /// </summary>
    public sealed class AbilityTargetingGate : IPointerTargetingGate
    {
        private readonly ActionScheduler scheduler;
        private readonly IAbilityResolutionService abilityResolution;
        private readonly LayerMask hittableMask;
        private readonly float maxRayDistance;

        private RuntimeEntityId source;
        private AbilityDefinition armedAbility;

        public AbilityTargetingGate(ActionScheduler scheduler, IAbilityResolutionService abilityResolution, LayerMask hittableMask, float maxRayDistance = 500f)
        {
            this.scheduler = scheduler;
            this.abilityResolution = abilityResolution;
            this.hittableMask = hittableMask;
            this.maxRayDistance = maxRayDistance;
        }

        public bool IsArmed => armedAbility != null;

        /// <summary>Enters targeting mode for <paramref name="ability"/> on behalf of <paramref name="source"/>
        /// (the UI bridge's own job to resolve -- normally whoever currently has the turn). Re-arming while
        /// already armed simply replaces the pending choice.</summary>
        public void Arm(RuntimeEntityId source, AbilityDefinition ability)
        {
            this.source = source;
            armedAbility = ability;
        }

        /// <summary>Leaves targeting mode without starting anything -- an explicit cancel, or the UI bridge's
        /// own cleanup once a target was chosen (successfully or not).</summary>
        public void Disarm() => armedAbility = null;

        /// <returns>True whenever armed -- every click is consumed while choosing a target, whether it lands
        /// on a valid target, an invalid one (stays armed so the player can simply try again), or empty space/
        /// a non-combatant (cancels targeting). False (falls through to ordinary Select/Move/Interact) only
        /// when nothing is armed.</returns>
        public bool TryConsume(Ray ray)
        {
            if (armedAbility == null) return false;

            if (!Physics.Raycast(ray, out var hit, maxRayDistance, hittableMask))
            {
                Disarm();
                return true;
            }

            var agent = hit.collider.GetComponentInParent<CharacterAgent>();
            if (agent == null)
            {
                Disarm();
                return true;
            }

            var targets = new[] { agent.EntityId };
            if (!abilityResolution.Validate(source, armedAbility, targets).IsValid) return true; // stay armed -- let the player try a different target

            scheduler.StartAbility(source, armedAbility, targets);
            Disarm();
            return true;
        }
    }
}
