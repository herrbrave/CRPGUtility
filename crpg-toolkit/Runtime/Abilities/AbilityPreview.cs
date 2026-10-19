using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Abilities
{
    /// <summary>What a UI can say about an ability against a target BEFORE it is used: nothing is rolled.</summary>
    public sealed class AbilityPreviewResult
    {
        public bool HasAttackRoll { get; set; }
        public int AttackBonus { get; set; }
        public int ArmorClass { get; set; }
        public int CriticalThreshold { get; set; } = 20;
        public AdvantageState Advantage { get; set; }
        /// <summary>The chance the attack hits (0..1), with the natural 1 and critical rules and any advantage or disadvantage applied.</summary>
        public float HitChance { get; set; }
        public bool HasDamage { get; set; }
        public DiceExpression Damage { get; set; }
        public string DamageText { get; set; } = "";
        public float AverageDamage { get; set; }
    }

    /// <summary>
    /// A roll-free preview of an attack ability, computed with the very rules <see cref="AttackRollEffect"/> rolls with (<see cref="IAttackBonusRule"/>, <see cref="IArmorClassRule"/>): a natural 1 always misses, a natural roll at or above the critical threshold always hits,
    /// otherwise the roll plus the attack bonus must reach the target's armour class. The first attack roll and the first damage effect found in the ability (looking inside wrapped effects) are reported; an ability with neither reports nothing.
    /// </summary>
    public static class AbilityPreview
    {
        public static AbilityPreviewResult Compute(AbilityDefinition ability, RuntimeEntityId source, RuntimeEntityId target, ICharacterRegistry characters, IAttackBonusRule attackRule, IArmorClassRule armorRule)
        {
            var result = new AbilityPreviewResult();
            if (ability == null) return result;
            var attack = Find<AttackRollEffect>(ability.Effects);
            if (attack != null && attackRule != null && armorRule != null)
            {
                var stat = attack.GoverningStatId;
                if (!stat.IsValid && characters != null && characters.TryGetCharacter(source, out var caster)) stat = caster.SpellcastingStatId;
                result.HasAttackRoll = true;
                result.AttackBonus = attackRule.ComputeAttackBonus(source, stat);
                result.CriticalThreshold = attackRule.ComputeCriticalThreshold(source);
                result.Advantage = attackRule.ComputeAdvantage(source, target);
                result.ArmorClass = armorRule.ComputeArmorClass(target);
                var p = SingleRollChance(result.AttackBonus, result.ArmorClass, result.CriticalThreshold);
                result.HitChance = result.Advantage == AdvantageState.Advantage ? 1f - (1f - p) * (1f - p) : result.Advantage == AdvantageState.Disadvantage ? p * p : p;
            }

            var damage = Find<DamageEffect>(ability.Effects);
            if (damage != null)
            {
                var amount = damage.Amount;
                result.HasDamage = true; result.Damage = amount;
                result.DamageText = $"{amount.Count}d{amount.Sides}" + (amount.Modifier > 0 ? $"+{amount.Modifier}" : amount.Modifier < 0 ? amount.Modifier.ToString() : "");
                result.AverageDamage = amount.Count * (amount.Sides + 1) / 2f + amount.Modifier;
            }

            return result;
        }

        /// <summary>The chance one d20 hits: a natural 1 never, a natural roll at or above the critical threshold always, otherwise roll + bonus >= armour class.</summary>
        public static float SingleRollChance(int attackBonus, int armorClass, int criticalThreshold)
        {
            var hits = 0;
            for (var natural = 1; natural <= 20; natural++)
                if (natural != 1 && (natural >= criticalThreshold || natural + attackBonus >= armorClass)) hits++;
            return hits / 20f;
        }

        private static T Find<T>(IEnumerable<AbilityEffect> effects) where T : AbilityEffect
        {
            if (effects == null) return null;
            foreach (var effect in effects)
            {
                if (effect == null) continue;
                if (effect is T match) return match;
                var inner = Find<T>(effect.GetChildEffects());
                if (inner != null) return inner;
            }

            return null;
        }
    }
}
