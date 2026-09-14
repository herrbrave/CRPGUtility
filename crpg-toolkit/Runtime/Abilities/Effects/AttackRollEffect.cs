using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// Rolls 1d20 + attack bonus per target against their Armor Class, then hands only the targets that were
    /// hit (and, separately, which of those crit) to a wrapped effect -- same "build a narrowed context, hand
    /// off to the wrapped effect" shape ConditionalEffect already uses. A natural 1 always misses; a natural
    /// roll at or above the attacker's critical threshold (20 by default, lower with e.g. Improved Critical)
    /// always hits and crits. No-ops gracefully (like ConditionalEffect) if OnHit or the context's attack-roll
    /// rules aren't wired up -- an ability author forgetting to set OnHit, or code that resolves this without
    /// combat rules configured (e.g. a non-combat context), shouldn't throw.
    /// </summary>
    [Serializable]
    public sealed class AttackRollEffect : AbilityEffect
    {
        /// <summary>Left as DefinitionId.None (the default), this resolves to the caster's own
        /// CharacterState.SpellcastingStatId instead -- for a spell attack roll, since the same spell asset can
        /// be cast by different classes with different spellcasting abilities, so the governing stat has to
        /// come from the caster, not be baked into the asset. A mundane weapon/martial ability sets this
        /// explicitly (e.g. Strength) and ignores the caster's spellcasting stat entirely.</summary>
        [SerializeField] private DefinitionId governingStatId;
        [SerializeReference] private AbilityEffect onHit;

        public override void Apply(AbilityExecutionContext context)
        {
            if (onHit == null || context.AttackBonusRule == null || context.ArmorClassRule == null) return;

            var resolvedStatId = governingStatId;
            if (!resolvedStatId.IsValid && context.Characters.TryGetCharacter(context.Source, out var caster))
                resolvedStatId = caster.SpellcastingStatId;

            var attackBonus = context.AttackBonusRule.ComputeAttackBonus(context.Source, resolvedStatId);
            var criticalThreshold = context.AttackBonusRule.ComputeCriticalThreshold(context.Source);
            var attackCount = context.AttackBonusRule.ComputeAttackCount(context.Source);

            // Extra Attack: each pass is a fully independent roll/hit/damage instance against the same targets
            // (an approximation -- real SRD5 lets you split extra attacks across different targets, which this
            // engine's "targets chosen once at cast time" model doesn't support).
            for (var attackIndex = 0; attackIndex < attackCount; attackIndex++)
            {
                var hitTargets = new List<RuntimeEntityId>();
                var criticalTargets = new HashSet<RuntimeEntityId>();

                foreach (var targetId in context.Targets)
                {
                    var advantage = context.AttackBonusRule.ComputeAdvantage(context.Source, targetId);
                    var roll = context.DiceService.Roll(new RollRequest(
                        new DiceExpression(1, 20), RollPurpose.AttackRoll, advantage, source: context.Source, target: targetId,
                        modifiers: new[] { new RollModifier("Attack Bonus", attackBonus) }));

                    // With Advantage/Disadvantage, DiceService rolls two d20s and marks only one Kept -- index 0
                    // isn't necessarily the one actually used for the total.
                    var naturalRoll = roll.Dice.First(d => d.Kept).Value;
                    var isCriticalHit = naturalRoll >= criticalThreshold;
                    var isCriticalMiss = naturalRoll == 1;
                    var armorClass = context.ArmorClassRule.ComputeArmorClass(targetId);
                    var isHit = !isCriticalMiss && (isCriticalHit || roll.Total >= armorClass);

                    context.CombatLog?.Log(new AttackRollLogEntry(context.Source, targetId, roll, armorClass, isHit, isCriticalHit));

                    if (!isHit) continue;
                    hitTargets.Add(targetId);
                    if (isCriticalHit) criticalTargets.Add(targetId);
                }

                if (hitTargets.Count == 0) continue;

                var hitContext = new AbilityExecutionContext(
                    context.Source, hitTargets, context.Characters, context.DiceService, context.CombatLog, context.EventBus,
                    context.Positions, context.InfluenceMap, context.AttackBonusRule, context.ArmorClassRule, criticalTargets,
                    context.SavingThrowRule, context.DamageResistanceRule, context.UpcastLevels,
                    context.OwnerId, context.OwnedTargets);
                onHit.Apply(hitContext);
            }
        }

        public override IEnumerable<AbilityEffect> GetChildEffects() =>
            onHit != null ? new[] { onHit } : Array.Empty<AbilityEffect>();
    }
}
