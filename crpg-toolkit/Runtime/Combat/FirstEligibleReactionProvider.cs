using System.Collections.Generic;
using CRPG.Abilities;

namespace CRPG.Combat
{
    /// <summary>Minimal reference AI: always takes the first eligible reaction, if any.</summary>
    public sealed class FirstEligibleReactionProvider : IReactionDecisionProvider
    {
        public AbilityDefinition ChooseReaction(ReactionOpportunity opportunity, IReadOnlyList<AbilityDefinition> eligibleReactions) =>
            eligibleReactions.Count > 0 ? eligibleReactions[0] : null;
    }
}
