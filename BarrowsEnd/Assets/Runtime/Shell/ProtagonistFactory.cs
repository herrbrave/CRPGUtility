using System;
using BarrowsEnd.Gate;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation.Creator;
using CRPG.Rules.SRD5;
using UnityEngine;

namespace BarrowsEnd.Shell
{
    /// <summary>
    /// Turns the creator's <see cref="CreatorResult"/> into the campaign's protagonist (Task 4.3): the character the build service produces, plus the one thing it
    /// never grants, hit points. <c>CharacterBuildService</c> adds no <c>resource-hp</c> (<see cref="ClassDefinition.HitDie"/> is used nowhere else), and the death-save,
    /// heal and cue code all key on it, so a protagonist without it would be unkillable-or-broken.
    /// <para>Level-1 maximum = hit-die maximum (<c>Count * Sides + Modifier</c>) + the Constitution modifier (<see cref="AbilityModifier.From"/>, Human's +1 included), at
    /// least 1. A consumer-side stopgap: the toolkit has no hit-point rule yet, and this refuses any plan above level 1 so it cannot silently under-supply HP.</para>
    /// </summary>
    public static class ProtagonistFactory
    {
        private static readonly DefinitionId Constitution = new("stat-constitution");

        public static CharacterState Build(IRulesDatabase rules, CreatorResult result, RuntimeEntityId id)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (result?.Plan == null) throw new ArgumentNullException(nameof(result));

            var plan = result.Plan;
            if (plan.Class == null) throw new InvalidOperationException("The protagonist's plan has no class, so there is no hit die to derive hit points from.");
            if (plan.Level != 1)
                throw new InvalidOperationException($"Hit points are only derived for a level 1 protagonist (this plan is level {plan.Level}); the toolkit has no level-up hit-point rule yet.");

            var character = new CharacterBuildService(rules).Build(plan, id);

            var die = plan.Class.HitDie;
            var modifier = AbilityModifier.From(character.Stats.GetScore(Constitution));
            var maxHitPoints = Mathf.Max(1, die.Count * die.Sides + die.Modifier + modifier);
            character.AddResource(new ResourceState(GateSessionServices.HitPoints, maxHitPoints));
            return character;
        }
    }
}
