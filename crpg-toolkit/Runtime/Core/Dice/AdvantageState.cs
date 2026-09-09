using System.Collections.Generic;

namespace CRPG.Core
{
    public enum AdvantageState
    {
        Normal,
        Advantage,
        Disadvantage
    }

    public static class AdvantageStateExtensions
    {
        /// <summary>Advantage and disadvantage never stack; any source of one cancels any source of the other.</summary>
        public static AdvantageState Combine(IEnumerable<AdvantageState> states)
        {
            var hasAdvantage = false;
            var hasDisadvantage = false;

            foreach (var state in states)
            {
                if (state == AdvantageState.Advantage) hasAdvantage = true;
                else if (state == AdvantageState.Disadvantage) hasDisadvantage = true;
            }

            if (hasAdvantage && hasDisadvantage) return AdvantageState.Normal;
            if (hasAdvantage) return AdvantageState.Advantage;
            if (hasDisadvantage) return AdvantageState.Disadvantage;
            return AdvantageState.Normal;
        }
    }
}
