using System;
using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>
    /// Rolls 1d20 + saving-throw bonus per target against a DC, then hands failed targets (and, separately,
    /// targets that succeeded) to their own wrapped effects -- same "build a narrowed context, hand off to the
    /// wrapped effect" shape AttackRollEffect/ConditionalEffect already use. Most saves only need OnFailure
    /// (e.g. a trap); a spell with "half damage on a successful save" would set both. No-ops gracefully if the
    /// context's SavingThrowRule isn't wired up, or neither wrapped effect is set.
    /// </summary>
    [Serializable]
    public sealed class SavingThrowEffect : AbilityEffect
    {
        [SerializeField] private DefinitionId savingStatId;
        [SerializeField] private int difficultyClass;

        /// <summary>When true, ignores <see cref="difficultyClass"/> and computes a spell save DC (8 +
        /// proficiency bonus + spellcasting ability modifier) from whoever is casting this ability
        /// (context.Source), via CharacterState.SpellcastingStatId. This is a spell asset shared across every
        /// caster of it, so the DC has to be resolved from the caster at cast time, not authored as a fixed
        /// number -- unlike a trap's DC, which really is fixed regardless of who triggers it.</summary>
        [SerializeField] private bool useCasterSpellSaveDC;

        [SerializeReference] private AbilityEffect onFailure;
        [SerializeReference] private AbilityEffect onSuccess;

        public override void Apply(AbilityExecutionContext context)
        {
            if (context.SavingThrowRule == null) return;
            if (onFailure == null && onSuccess == null) return;

            var resolvedDifficultyClass = difficultyClass;
            if (useCasterSpellSaveDC && context.Characters.TryGetCharacter(context.Source, out var caster))
                resolvedDifficultyClass = context.SavingThrowRule.ComputeSpellSaveDC(context.Source, caster.SpellcastingStatId);

            var failedTargets = new List<RuntimeEntityId>();
            var succeededTargets = new List<RuntimeEntityId>();

            foreach (var targetId in context.Targets)
            {
                context.EventBus?.Publish(new GameEventContext(GameEvent.BeforeSavingThrow, context.Source, targetId));

                // Paralyzed/Stunned/Unconscious-style auto-fail: no roll needed, the save is simply lost.
                if (context.SavingThrowRule.AutoFails(targetId, savingStatId))
                {
                    context.CombatLog?.Log(new SavingThrowLogEntry(context.Source, targetId, default, resolvedDifficultyClass, savingStatId, succeeded: false));
                    failedTargets.Add(targetId);
                    context.EventBus?.Publish(new GameEventContext(GameEvent.AfterSavingThrow, context.Source, targetId));
                    continue;
                }

                var bonus = context.SavingThrowRule.ComputeSavingThrowBonus(targetId, savingStatId);
                var advantage = context.SavingThrowRule.ComputeAdvantage(targetId, savingStatId);
                var roll = context.DiceService.Roll(new RollRequest(
                    new DiceExpression(1, 20), RollPurpose.SavingThrow, advantage, source: context.Source, target: targetId,
                    modifiers: new[] { new RollModifier("Saving Throw", bonus) }));

                var succeeded = roll.Total >= resolvedDifficultyClass;
                context.CombatLog?.Log(new SavingThrowLogEntry(context.Source, targetId, roll, resolvedDifficultyClass, savingStatId, succeeded));
                (succeeded ? succeededTargets : failedTargets).Add(targetId);
                context.EventBus?.Publish(new GameEventContext(GameEvent.AfterSavingThrow, context.Source, targetId));
            }

            if (failedTargets.Count > 0 && onFailure != null) onFailure.Apply(Narrow(context, failedTargets));
            if (succeededTargets.Count > 0 && onSuccess != null) onSuccess.Apply(Narrow(context, succeededTargets));
        }

        public override IEnumerable<AbilityEffect> GetChildEffects()
        {
            if (onFailure != null) yield return onFailure;
            if (onSuccess != null) yield return onSuccess;
        }

        private static AbilityExecutionContext Narrow(AbilityExecutionContext context, IReadOnlyList<RuntimeEntityId> targets) => new(
            context.Source, targets, context.Characters, context.DiceService, context.CombatLog, context.EventBus,
            context.Positions, context.InfluenceMap, context.AttackBonusRule, context.ArmorClassRule, context.CriticalTargets,
            context.SavingThrowRule, context.DamageResistanceRule, context.UpcastLevels);
    }
}
