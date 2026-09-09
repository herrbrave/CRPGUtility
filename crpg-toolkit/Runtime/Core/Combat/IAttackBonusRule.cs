namespace CRPG.Core
{
    /// <summary>Attacker-side derived stats for resolving an attack roll -- mirrors IInitiativeRule's
    /// "swappable ruleset math" shape. Lives in CRPG.Core (not CRPG.Combat, where its default implementation
    /// does) for the same reason IPositionProvider/InfluenceMap do: CRPG.Abilities needs to reference this
    /// from AbilityExecutionContext/AttackRollEffect, and CRPG.Combat already depends on CRPG.Abilities, so
    /// CRPG.Abilities depending back on CRPG.Combat would be circular.</summary>
    public interface IAttackBonusRule
    {
        int ComputeAttackBonus(RuntimeEntityId attacker, DefinitionId governingStatId);

        /// <summary>The natural d20 result at or above which an attack is a critical hit (20 by default,
        /// lowered by e.g. Improved Critical).</summary>
        int ComputeCriticalThreshold(RuntimeEntityId attacker);

        /// <summary>Combines the attacker's own self-imposed advantage/disadvantage (e.g. Reckless Attack) with
        /// whatever the target imposes on attacks against them (e.g. a Prone or Blinded target).</summary>
        AdvantageState ComputeAdvantage(RuntimeEntityId attacker, RuntimeEntityId target);

        /// <summary>How many separate attacks a single use of an attack-roll ability grants (1 by default,
        /// 2+ with Extra Attack). AttackRollEffect repeats its whole roll-and-resolve loop this many times,
        /// each a fully independent roll/hit/damage instance against the same targets -- an approximation of
        /// "make two separate attacks" that doesn't model attacking different targets per swing.</summary>
        int ComputeAttackCount(RuntimeEntityId attacker);
    }
}
