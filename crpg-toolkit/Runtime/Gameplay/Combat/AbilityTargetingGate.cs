using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>What an AIMED ability would do if the player clicked now (see <see cref="Hover"/>): the aim, whether it is allowed (and why not), and the creatures inside its shape.</summary>
        public readonly struct AimPreview
        {
            public AimPreview(AbilityDefinition ability, bool hasPoint, AbilityAim aim, bool valid, string reason, IReadOnlyList<RuntimeEntityId> targets)
            { Ability = ability; HasPoint = hasPoint; Aim = aim; Valid = valid; Reason = reason; Targets = targets ?? Array.Empty<RuntimeEntityId>(); }
            public AbilityDefinition Ability { get; }
            public bool HasPoint { get; }
            public AbilityAim Aim { get; }
            public bool Valid { get; }
            public string Reason { get; }
            public IReadOnlyList<RuntimeEntityId> Targets { get; }
        }

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

        /// <summary>Which layers an aimed click may hit for its ground point (default: the gate's own mask). A game that already has a move-preview mask passes the same one, so aiming and moving agree.</summary>
        public LayerMask AimMask { get; set; } = default;

        /// <summary>What is armed and for whom (null / default when nothing is): a UI draws the ability's range and its valid targets from this.</summary>
        public AbilityDefinition ArmedAbility => armedAbility;
        public RuntimeEntityId ArmedSource => source;

        /// <summary>Raised whenever a click while armed lands on a target that fails Validate (out of range,
        /// wrong relation, etc.) -- confirmed a real, previously silent gap: without this, a rejected click
        /// simply stays armed with zero feedback anywhere, which reads as "the click did nothing" to a player
        /// (or a tester) with no way to tell a genuine input bug from a merely-too-far target. Never raised for
        /// an empty-space/non-combatant click (that path cancels targeting outright, not a validation
        /// rejection) or a successful cast.</summary>
        public event Action<AbilityDefinition, RuntimeEntityId, AbilityValidationResult> TargetRejected;

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

        private LayerMask EffectiveAimMask => AimMask.value != 0 ? AimMask : hittableMask;

        /// <summary>The ground point under a ray for an aimed ability: the nearest hit; a click ON a creature aims at its feet. False when nothing is under the pointer.</summary>
        private bool TryAimPoint(Ray ray, out Vector3 point)
        {
            point = default;
            var hits = Physics.RaycastAll(ray, maxRayDistance, EffectiveAimMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var agent = hit.collider.GetComponentInParent<CharacterAgent>();
                point = agent != null ? agent.transform.position : hit.point;
                return true;
            }

            return false;
        }

        private static Vector3 PositionOf(RuntimeEntityId id)
        {
            var agent = CharacterAgent.AllLive.FirstOrDefault(a => a != null && a.EntityId == id);
            return agent != null ? agent.transform.position : Vector3.zero;
        }

        /// <summary>What clicking under this ray would do, for the presentation to draw every frame (the router has no per-frame hover, so the UI polls this, as the fight feedback does for the hovered fighter). No point under the pointer gives <c>HasPoint == false</c>.</summary>
        public AimPreview Hover(Ray ray)
        {
            if (armedAbility == null || !armedAbility.Targeting.RequiresAim) return default;
            if (!TryAimPoint(ray, out var point)) return new AimPreview(armedAbility, false, AbilityAim.None, false, "Aim at the ground.", null);
            var aim = AbilityAim.FromCaster(PositionOf(source), point);
            var validation = abilityResolution.Validate(source, armedAbility, aim);
            return new AimPreview(armedAbility, true, aim, validation.IsValid, validation.IsValid ? null : validation.Message, abilityResolution.ResolveTargets(source, armedAbility, aim));
        }

        private bool TryConsumeAim(Ray ray)
        {
            if (!TryAimPoint(ray, out var point)) { Disarm(); return true; }   // nothing under the pointer: cancel, as for a creature ability
            var aim = AbilityAim.FromCaster(PositionOf(source), point);
            var validation = abilityResolution.Validate(source, armedAbility, aim);
            if (!validation.IsValid)
            {
                TargetRejected?.Invoke(armedAbility, default, validation);
                return true;   // stay armed: try another point
            }

            scheduler.StartAbility(source, armedAbility, aim);
            Disarm();
            return true;
        }

        /// <returns>True whenever armed -- every click is consumed while choosing a target, whether it lands
        /// on a valid target, an invalid one (stays armed so the player can simply try again), or empty space/
        /// a non-combatant (cancels targeting). False (falls through to ordinary Select/Move/Interact) only
        /// when nothing is armed.</returns>
        public bool TryConsume(Ray ray)
        {
            if (armedAbility == null) return false;
            if (armedAbility.Targeting.RequiresAim) return TryConsumeAim(ray);

            // Every hit along the ray, nearest first: a trigger volume (an encounter/dialogue zone) or a prop between
            // the camera and the creature must not swallow the click, so triggers are ignored and the first hit that
            // actually belongs to a creature wins. Only if NOTHING creature-shaped is under the pointer does the click
            // cancel targeting.
            var hits = Physics.RaycastAll(ray, maxRayDistance, hittableMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            CharacterAgent agent = null;
            foreach (var hit in hits)
            {
                agent = hit.collider.GetComponentInParent<CharacterAgent>();
                if (agent != null) break;
            }

            if (agent == null)
            {
                Disarm();
                return true;
            }

            var targets = new[] { agent.EntityId };
            var validation = abilityResolution.Validate(source, armedAbility, targets);
            if (!validation.IsValid)
            {
                TargetRejected?.Invoke(armedAbility, agent.EntityId, validation);
                return true; // stay armed -- let the player try a different target
            }

            scheduler.StartAbility(source, armedAbility, targets);
            Disarm();
            return true;
        }
    }
}
