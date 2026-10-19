using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>One named part of a check's bonus (for display: "Dexterity +2", "proficient +2").</summary>
    public readonly struct CheckBonusPart
    {
        public CheckBonusPart(string label, int value) { Label = label; Value = value; }
        public string Label { get; }
        public int Value { get; }
    }

    /// <summary>A skill-check rule that can also say WHERE its bonus comes from, so a dice popup can print the parts. <see cref="ISkillCheckRule.ComputeSkillCheckBonus"/> must equal the sum of the parts.</summary>
    public interface IDetailedSkillCheckRule : ISkillCheckRule
    {
        IReadOnlyList<CheckBonusPart> ComputeBreakdown(RuntimeEntityId character, string queryKey);
    }
}
