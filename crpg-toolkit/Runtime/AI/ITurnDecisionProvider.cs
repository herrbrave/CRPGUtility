namespace CRPG.AI
{
    /// <summary>Chooses what a combatant does on its turn. A human player's choice (driven directly through a
    /// combat UI calling AbilityResolutionService) and an AI's choice are both conceptually this -- mirrors
    /// IReactionDecisionProvider's shape.</summary>
    public interface ITurnDecisionProvider
    {
        /// <returns>The chosen ability and targets, or null if nothing is worth doing this turn.</returns>
        TurnDecision GetDecision(TurnContext context);
    }
}
