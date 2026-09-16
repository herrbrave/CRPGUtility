using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Consumer.Fate
{
    /// <summary>
    /// Gate D's combat resolution: an opposed 4dF+skill roll (attacker vs. defender), not the package's
    /// generic <c>AttackRollEffect</c>. <c>AttackRollEffect</c> hardcodes <c>new DiceExpression(1, 20)</c> for
    /// the roll itself and <c>naturalRoll == 1</c> for a fumble, where <c>naturalRoll</c> is just the FIRST
    /// kept die -- correct for exactly one d20, wrong for a 4-die pool (it would spuriously fumble on a coin
    /// flip's worth of rolls). Rather than extend the package's attack-bonus seam to fix a D&D-specific
    /// concept (crit/fumble-on-a-natural-roll has no Fate equivalent at all), this ability effect type lives
    /// entirely in the consumer project and rolls both sides directly.
    /// <para>
    /// On a hit, spends the excess ("shifts") off the target's Stress resource. If Stress can't absorb it all,
    /// the target is Taken Out -- which, per the real Fate Condensed rules, does NOT mean death ("the attacker
    /// decides the outcome... alternative fates include kidnapping, imperiling, or forced consequences").
    /// Never publishes <c>GameEvent.CreatureDied</c> (the package's <c>TacticalEncounterController</c>
    /// subscribes to that specifically for death) -- instead calls <see cref="OnTakenOut"/>, a plain static
    /// delegate <see cref="FateConflictService"/> assigns itself to once at startup. A constructor-injected
    /// reference isn't possible here: [SerializeReference] ability effects are Unity-serialized data (like
    /// every other effect in the package -- DamageEffect, AttackRollEffect, ...), never constructed with
    /// dependencies: the package's own effects solve this by reading everything from
    /// AbilityExecutionContext, which has no room for a Fate-specific "how do I remove someone from the
    /// encounter" callback. This static slot is the consumer-local equivalent, explicitly wired once by the
    /// composition root -- not a self-locating Manager.Instance.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class FateAttackEffect : CRPG.Abilities.AbilityEffect
    {
        /// <summary>Assigned once by <see cref="FateConflictService"/>'s constructor. Invoked with the
        /// defeated combatant's id when Stress can't absorb an attack's shifts.</summary>
        public static Action<RuntimeEntityId> OnTakenOut;

        [SerializeField] private ResourceDefinition attackSkillResource;
        [SerializeField] private ResourceDefinition defenseSkillResource;
        [SerializeField] private ResourceDefinition stressResource;

        /// <summary>0 for a plain Strike, +2 for "Strike, Invoking a Stunt" -- the real Fate Condensed rule
        /// ("invoke an aspect for... a +2 bonus to your roll") modeled as authored per-ability data instead of
        /// new context-passing machinery, since the fixed bonus is known at author time for this compact
        /// game's two ability variants.</summary>
        [SerializeField] private int invokeBonus;

        public override void Apply(CRPG.Abilities.AbilityExecutionContext context)
        {
            if (!context.Characters.TryGetCharacter(context.Source, out var attacker)) return;

            var attackBonus = SkillBonus(attacker, attackSkillResource) + invokeBonus;
            var attackRoll = context.DiceService.Roll(new RollRequest(FateCheckRule.FourDF, RollPurpose.AttackRoll, source: context.Source));
            var attackTotal = attackRoll.Total + attackBonus;

            foreach (var targetId in context.Targets)
            {
                if (!context.Characters.TryGetCharacter(targetId, out var defender)) continue;

                var defenseBonus = SkillBonus(defender, defenseSkillResource);
                var defenseRoll = context.DiceService.Roll(new RollRequest(FateCheckRule.FourDF, RollPurpose.Generic, source: targetId));
                var defenseTotal = defenseRoll.Total + defenseBonus;

                context.CombatLog?.Log(new RollLogEntry(attackRoll));
                context.CombatLog?.Log(new RollLogEntry(defenseRoll));

                var shifts = (int)Math.Round(attackTotal - defenseTotal, MidpointRounding.AwayFromZero);
                if (shifts <= 0) continue; // successfully defended

                if (stressResource == null || !defender.TryGetResource(stressResource.Id, out var stress)) continue;

                var absorbed = stress.Spend(shifts);
                if (absorbed < shifts) OnTakenOut?.Invoke(targetId);
            }
        }

        private static int SkillBonus(CharacterState character, ResourceDefinition skillResource) =>
            skillResource != null && character.TryGetResource(skillResource.Id, out var state) ? state.Max : 0;
    }
}
