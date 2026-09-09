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
    }
}
