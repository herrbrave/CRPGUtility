using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Actors
{
    /// <summary>Concrete <see cref="IRulesetRegistrationBuilder"/> -- wraps the fresh <see cref="RulesDatabase"/>
    /// and <see cref="ICharacterRegistry"/> a session was just constructed with, plus the <see cref="PolicyRegistry"/>
    /// a provider populates. Constructed once per session start by <c>CRPG.Gameplay</c>'s <c>GameSessionHost</c>
    /// -- deliberately takes the concrete <see cref="RulesDatabase"/> (not the read-only <see cref="IRulesDatabase"/>)
    /// since registration, not just lookup, is required here.</summary>
    public sealed class RulesetRegistrationBuilder : IRulesetRegistrationBuilder
    {
        private readonly RulesDatabase rulesDatabase;
        private readonly PolicyRegistry policies;

        public RulesetRegistrationBuilder(RulesDatabase rulesDatabase, ICharacterRegistry characters, IDiceService diceService, PolicyRegistry policies)
        {
            this.rulesDatabase = rulesDatabase ?? throw new ArgumentNullException(nameof(rulesDatabase));
            Characters = characters ?? throw new ArgumentNullException(nameof(characters));
            DiceService = diceService ?? throw new ArgumentNullException(nameof(diceService));
            this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
        }

        public IRulesDatabase RulesDatabase => rulesDatabase;
        public ICharacterRegistry Characters { get; }
        public IDiceService DiceService { get; }

        public void RegisterDefinitions(IEnumerable<IDefinition> definitions) => rulesDatabase.RegisterRange(definitions);

        public void RegisterPolicy<TPolicy>(TPolicy policy) where TPolicy : class => policies.Register(policy);
    }
}
