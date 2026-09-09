using System.Collections.Generic;
using CRPG.Abilities;

namespace CRPG.Combat
{
    /// <summary>Chooses which (if any) eligible reaction to use for a ReactionOpportunity. A human player's
    /// choice and an AI's choice are both just an implementation of this.</summary>
    public interface IReactionDecisionProvider
    {
        /// <returns>The chosen reaction, or null to decline.</returns>
        AbilityDefinition ChooseReaction(ReactionOpportunity opportunity, IReadOnlyList<AbilityDefinition> eligibleReactions);
    }
}
