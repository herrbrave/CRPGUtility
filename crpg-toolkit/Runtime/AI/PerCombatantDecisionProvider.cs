using System;
using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.AI
{
    /// <summary>
    /// Routes each combatant's turn decision to the decision provider of ITS OWN <see cref="BehaviorSetDefinition"/>, so one encounter can mix enemy types (grunts, archers, a shaman, a
    /// boss) under the single <c>TacticalAiTurnDriver</c> the encounter has. Providers are built lazily, once per distinct behaviour set, by <c>createProvider</c> (typically a
    /// <see cref="UtilityAIDecisionProvider"/>, optionally wrapped). A combatant with no behaviour set (the callback returns null) gets no decision.
    /// </summary>
    public sealed class PerCombatantDecisionProvider : ITurnDecisionProvider
    {
        private readonly Func<RuntimeEntityId, BehaviorSetDefinition> brainFor;
        private readonly Func<BehaviorSetDefinition, ITurnDecisionProvider> createProvider;
        private readonly Dictionary<BehaviorSetDefinition, ITurnDecisionProvider> providers = new();

        public PerCombatantDecisionProvider(Func<RuntimeEntityId, BehaviorSetDefinition> brainFor, Func<BehaviorSetDefinition, ITurnDecisionProvider> createProvider)
        {
            this.brainFor = brainFor ?? throw new ArgumentNullException(nameof(brainFor));
            this.createProvider = createProvider ?? throw new ArgumentNullException(nameof(createProvider));
        }

        public TurnDecision GetDecision(TurnContext context)
        {
            var set = brainFor(context.Self);
            if (set == null) return null;
            if (!providers.TryGetValue(set, out var provider)) providers[set] = provider = createProvider(set);
            return provider.GetDecision(context);
        }
    }
}
