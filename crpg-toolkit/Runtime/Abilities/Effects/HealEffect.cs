using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    [Serializable]
    public sealed class HealEffect : AbilityEffect
    {
        [SerializeField] private ResourceDefinition hitPointResource;
        [SerializeField] private DiceExpression amount;

        /// <summary>Upcasting: extra dice of the base amount's own die type added per slot level above this
        /// spell's own AbilityDefinition.SpellLevel it was cast at (e.g. Cure Wounds' "+1d8 per slot level
        /// above 1st" is 1 here). Zero (the default) means this heal doesn't scale with upcasting. See
        /// DamageEffect's identical field for the full explanation.</summary>
        [SerializeField] private int extraDiceCountPerSlotLevel;

        public override void Apply(AbilityExecutionContext context)
        {
            var scaledAmount = context.UpcastLevels > 0
                ? new DiceExpression(amount.Count + context.UpcastLevels * extraDiceCountPerSlotLevel, amount.Sides, amount.Modifier)
                : amount;

            foreach (var targetId in context.Targets)
            {
                if (!context.Characters.TryGetCharacter(targetId, out var target)) continue;
                if (!target.TryGetResource(hitPointResource.Id, out var hp)) continue;

                var roll = context.DiceService.Roll(new RollRequest(scaledAmount, RollPurpose.Generic, source: context.Source, target: targetId));
                hp.Restore(Mathf.RoundToInt((float)roll.Total));
                context.CombatLog?.Log(new RollLogEntry(roll));
            }
        }
    }
}
