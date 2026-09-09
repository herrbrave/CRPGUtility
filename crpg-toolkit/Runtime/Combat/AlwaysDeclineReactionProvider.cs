using System.Collections.Generic;
using CRPG.Abilities;

namespace CRPG.Combat
{
    public sealed class AlwaysDeclineReactionProvider : IReactionDecisionProvider
    {
        public AbilityDefinition ChooseReaction(ReactionOpportunity opportunity, IReadOnlyList<AbilityDefinition> eligibleReactions) => null;
    }
}
