using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>Derived stats for resolving a skill/ability check keyed by a bare query string (e.g.
    /// "Skill:Persuasion") -- lives in CRPG.Actors, not CRPG.Combat like the other Default*Rule classes,
    /// since checks aren't combat-specific (Dialogue is the only current consumer, and other non-combat
    /// systems may want them later) and CRPG.Dialogue already references CRPG.Actors but not CRPG.Combat.</summary>
    public interface ISkillCheckRule
    {
        int ComputeSkillCheckBonus(RuntimeEntityId character, string queryKey);
        AdvantageState ComputeAdvantage(RuntimeEntityId character, string queryKey);

        /// <summary>Task 4.1: the die a check rolls (SRD5: 1d20) -- moved here from being hardcoded in
        /// DialogueRunner so a check-performing caller never needs to know a specific die shape itself. This
        /// interface is explicitly roll-based (it computes bonus/advantage *for* a roll); a hypothetical
        /// non-roll-based ruleset would need its own different interface entirely, not a generalized version
        /// of this one.</summary>
        DiceExpression CheckDie { get; }
    }
}
