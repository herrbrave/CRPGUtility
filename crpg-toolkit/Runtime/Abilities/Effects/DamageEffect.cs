using System;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// Rolls dice, applies resistance/immunity/vulnerability (if this damage is typed and a
    /// DamageResistanceRule is wired up), and spends the result off a designated "hit points"-like resource.
    /// Publishes BeforeDamage with the resistance-adjusted amount in Data["Amount"] (and whether this hit was a
    /// crit in Data["IsCritical"]) before applying it -- DeathSaveService's "damage at 0 HP" handling subscribes
    /// and overwrites that entry without this class needing to know about death saves at all. Publishes
    /// AfterDamage, and CreatureDroppedToZeroHP -- only on the actual positive-to-zero
    /// transition, not on a later hit against an already-downed target -- with Data["Overkill"] (how far past
    /// zero this hit went) for a subscriber to decide instant-death-by-massive-damage. What zero HP actually
    /// means (unconscious, dead, removed from the encounter) is left to whoever subscribes; see
    /// CombatEncounter.RemoveCombatant's remarks and DeathSaveService.
    /// </summary>
    [Serializable]
    public sealed class DamageEffect : AbilityEffect
    {
        [SerializeField] private ResourceDefinition hitPointResource;
        [SerializeField] private DiceExpression amount;

        /// <summary>The base damage dice (before cantrip scaling, upcasting and critical doubling).</summary>
        public DiceExpression Amount => amount;

        /// <summary>Optional -- DefinitionId.None (the default) means untyped damage, which never triggers
        /// resistance/immunity/vulnerability (matches every DamageEffect authored before damage types existed).
        /// Set this to apply the target's DefaultDamageResistanceRule-gathered modifiers for this damage type.</summary>
        [SerializeField] private DefinitionId damageTypeId;

        /// <summary>SRD5 cantrip scaling: an extra die of the same type at caster level 5, 11, and 17 (so up to
        /// +3 dice by level 17). Reads the caster's level directly off CharacterState rather than needing a
        /// dedicated rule, since it's a flat level breakpoint table, not a query-key-driven derived stat.</summary>
        [SerializeField] private bool scalesWithCasterLevel;

        /// <summary>Upcasting: how many extra dice of the *base* amount's own die type (Sides/Modifier are
        /// always taken from <see cref="amount"/>, never re-specified here) get added per slot level above this
        /// spell's own AbilityDefinition.SpellLevel it was cast at -- e.g. Cure Wounds' real "+1d8 per slot
        /// level above 1st" is 1 here, matching its base 1d8. Zero (the default) means this spell doesn't scale
        /// with upcasting.</summary>
        [SerializeField] private int extraDiceCountPerSlotLevel;

        /// <summary>Half the rolled damage, rounded down (a successful save against Burning Hands, Shatter, Fireball: the 5e "half as much damage on a successful save"). Authored as a second copy of the damage effect on the save effect's success branch.</summary>
        [SerializeField] private bool halved;
        public bool Halved => halved;

        /// <summary>The hit-point-like resource this damage spends.</summary>
        public ResourceDefinition HitPointResource => hitPointResource;

        /// <summary>The damage type (None: untyped).</summary>
        public DefinitionId DamageTypeId => damageTypeId;

        /// <summary>The extra damage of an attack rider: plain dice of the same resource and type as the weapon's own damage, dealt through <see cref="Apply"/> (resistance, events, the log, doubled dice on a critical hit).</summary>
        public static DamageEffect CreateRider(ResourceDefinition hitPointResource, DiceExpression amount, DefinitionId damageTypeId) =>
            new() { hitPointResource = hitPointResource, amount = amount, damageTypeId = damageTypeId };

        public override void Apply(AbilityExecutionContext context)
        {
            var casterLevel = context.Characters.TryGetCharacter(context.Source, out var caster) ? caster.Level : 1;
            var upcastBonusDice = context.UpcastLevels * extraDiceCountPerSlotLevel;
            var scaledCount = amount.Count + (scalesWithCasterLevel ? CantripScalingBonus(casterLevel) : 0) + upcastBonusDice;

            foreach (var targetId in context.Targets)
            {
                if (!context.Characters.TryGetCharacter(targetId, out var target)) continue;
                if (!target.TryGetResource(hitPointResource.Id, out var hp)) continue;

                // A critical hit doubles the (already level-scaled) dice rolled, not the flat modifier (5e rule)
                // -- see AttackRollEffect, which populates CriticalTargets on the narrowed context it hands this
                // effect.
                var isCritical = context.CriticalTargets != null && context.CriticalTargets.Contains(targetId);
                var rollAmount = new DiceExpression(isCritical ? scaledCount * 2 : scaledCount, amount.Sides, amount.Modifier);
                var roll = context.DiceService.Roll(new RollRequest(rollAmount, RollPurpose.DamageRoll, source: context.Source, target: targetId));
                var rawAmount = Mathf.RoundToInt((float)roll.Total);
                if (halved) rawAmount /= 2;
                if (damageTypeId.IsValid && context.DamageResistanceRule != null)
                    rawAmount = context.DamageResistanceRule.ApplyResistance(targetId, damageTypeId, rawAmount);

                var beforeDamage = new GameEventContext(GameEvent.BeforeDamage, context.Source, targetId);
                beforeDamage.Data["Amount"] = rawAmount;
                beforeDamage.Data["IsCritical"] = isCritical;
                context.EventBus?.Publish(beforeDamage);
                var finalAmount = beforeDamage.Data.TryGetValue("Amount", out var overridden) && overridden is int overriddenAmount
                    ? overriddenAmount
                    : rawAmount;

                var wasAboveZero = hp.Current > 0;
                var overkill = finalAmount - hp.Current;
                hp.Spend(finalAmount);
                context.CombatLog?.Log(new RollLogEntry(roll));
                context.CombatLog?.Log(new DamageLogEntry(context.Source, targetId, finalAmount, hp.Current));
                context.EventBus?.Publish(new GameEventContext(GameEvent.AfterDamage, context.Source, targetId));

                // Only the hit that actually drops a target to 0 fires this -- a subsequent hit against an
                // already-downed target is a different moment (see DeathSaveService's "damage at 0 HP" handling
                // via BeforeDamage) and shouldn't re-trigger "just dropped" logic.
                if (hp.Current <= 0 && wasAboveZero)
                {
                    context.CombatLog?.Log(new DeathLogEntry(context.Source, targetId));
                    var droppedToZero = new GameEventContext(GameEvent.CreatureDroppedToZeroHP, context.Source, targetId);
                    droppedToZero.Data["Overkill"] = overkill;
                    droppedToZero.Data["IsCritical"] = isCritical;
                    context.EventBus?.Publish(droppedToZero);
                }
            }
        }

        private static int CantripScalingBonus(int casterLevel) =>
            casterLevel >= 17 ? 3 : casterLevel >= 11 ? 2 : casterLevel >= 5 ? 1 : 0;
    }
}
