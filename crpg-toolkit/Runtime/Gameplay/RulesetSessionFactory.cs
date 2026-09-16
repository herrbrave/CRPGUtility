using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Task 5.2 gap fix: the build-then-validate candidate-session logic <see cref="GameSessionHost"/> already
    /// had for <see cref="GameSessionHost.StartSession(IRulesetDefinition, IDiceService, IReadOnlyList{Type})"/>,
    /// extracted so <see cref="SessionLoadCoordinator"/> can build a save/load-reconstructed session through the
    /// exact same registration path instead of the bare <see cref="GameSession"/> constructor -- a session
    /// started via an <see cref="IRulesetDefinition"/> was previously losing every registered policy the moment
    /// it was saved and reloaded, since the reload path never touched this logic at all.
    /// </summary>
    internal static class RulesetSessionFactory
    {
        /// <summary>Builds a fresh <see cref="RulesDatabase"/> + <see cref="GameSession"/>, lets
        /// <paramref name="provider"/> register its content/policies, then validates every required policy type
        /// was registered. On any failure the candidate is disposed and the exception rethrown -- never
        /// partially registered content left visible on a caller-held reference.</summary>
        public static GameSession BuildAndValidate(IRulesetDefinition provider, IDiceService diceService, IReadOnlyList<Type> requiredPolicyTypes)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            var rulesDatabase = new RulesDatabase();
            var candidate = new GameSession(rulesDatabase, diceService);
            try
            {
                var builder = new RulesetRegistrationBuilder(rulesDatabase, candidate.Characters, diceService, candidate.Policies);
                provider.Register(builder);
                candidate.Policies.ValidateRequired(requiredPolicyTypes);
            }
            catch
            {
                candidate.Dispose();
                throw;
            }

            return candidate;
        }
    }
}
