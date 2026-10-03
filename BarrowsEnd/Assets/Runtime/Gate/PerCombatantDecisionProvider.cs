using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.AI;

namespace BarrowsEnd.Gate
{
    /// <summary>Routes each hostile's turn decision to the utility brain of ITS OWN behavior set, so one encounter
    /// can mix enemy types (a melee grunt that closes in and an archer that keeps its distance) under one
    /// <see cref="CRPG.Gameplay.Combat.TacticalAiTurnDriver"/>. Providers are built lazily, one per distinct set.</summary>
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
