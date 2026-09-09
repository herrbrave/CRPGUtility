using System.Collections.Generic;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Combat
{
    /// <summary>
    /// Resolves a ReactionOpportunity: gates on the reactor actually having a reaction available, filters the
    /// caller-supplied candidates down to ones tagged Reaction and currently usable, asks the decision
    /// provider to pick one, and executes it. Spending the reaction resource itself isn't done here -- it
    /// happens naturally through AbilityResolutionService.Execute's normal cost consumption, provided the
    /// reaction ability's own Costs include the reaction resource (as any real reaction ability should).
    /// </summary>
    /// <remarks>
    /// Doesn't implement "pause the interrupted sequence and resume it after" (spec: "the ability execution
    /// system therefore needs to support suspension/resumption") -- that needs the interrupted action's own
    /// call site to await this, which requires an async attack/turn pipeline that doesn't exist yet. This
    /// covers "find eligible reactions, let something decide, execute" so that pipeline has something to call.
    /// </remarks>
    public sealed class ReactionService
    {
        private readonly ICharacterRegistry characters;
        private readonly IAbilityResolutionService abilityResolution;
        private readonly IReactionDecisionProvider decisionProvider;
        private readonly ResourceDefinition reactionResource;

        public ReactionService(
            ICharacterRegistry characters,
            IAbilityResolutionService abilityResolution,
            IReactionDecisionProvider decisionProvider,
            ResourceDefinition reactionResource)
        {
            this.characters = characters;
            this.abilityResolution = abilityResolution;
            this.decisionProvider = decisionProvider;
            this.reactionResource = reactionResource;
        }

        /// <returns>true if a reaction was chosen and executed.</returns>
        public bool ResolveOpportunity(
            ReactionOpportunity opportunity, IReadOnlyList<AbilityDefinition> candidateReactions, IReadOnlyList<RuntimeEntityId> reactionTargets)
        {
            if (!characters.TryGetCharacter(opportunity.Reactor, out var reactor)) return false;
            if (!reactor.TryGetResource(reactionResource.Id, out var reactionPool) || reactionPool.Current <= 0) return false;

            var eligible = new List<AbilityDefinition>();
            foreach (var candidate in candidateReactions)
            {
                if ((candidate.Tags & AbilityTag.Reaction) == 0) continue;
                if (abilityResolution.CanUse(opportunity.Reactor, candidate)) eligible.Add(candidate);
            }

            if (eligible.Count == 0) return false;

            var chosen = decisionProvider.ChooseReaction(opportunity, eligible);
            if (chosen == null) return false;

            abilityResolution.Execute(opportunity.Reactor, chosen, reactionTargets);
            return true;
        }
    }
}
