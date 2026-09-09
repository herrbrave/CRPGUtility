using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Combat
{
    /// <summary>
    /// Rolls 1d20 plus whatever the ruleset has tagged with the "Initiative" query key (e.g. a FeatureDefinition
    /// contributing the Dexterity modifier), gathered the same way any other rule query does (see
    /// FeatureModifierCollector). The engine never hardcodes "Dexterity" -- a ruleset opts in just by tagging
    /// a modifier with this key.
    /// </summary>
    public sealed class DefaultInitiativeRule : IInitiativeRule
    {
        public const string QueryKey = "Initiative";

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;
        private readonly IDiceService diceService;

        public DefaultInitiativeRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase, IDiceService diceService)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.diceService = diceService;
        }

        public RollResult RollInitiative(RuntimeEntityId combatant)
        {
            var modifiers = new List<RollModifier>();

            if (characters.TryGetCharacter(combatant, out var character))
            {
                var query = new NumericRuleQuery(character, 0);
                foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, QueryKey))
                    query.AddModifier(modifier.ToRuleModifier(source));

                var bonus = query.Evaluate();
                if (bonus != 0) modifiers.Add(new RollModifier("Initiative Modifier", bonus));
            }

            return diceService.Roll(new RollRequest(new DiceExpression(1, 20), RollPurpose.Initiative, source: combatant, modifiers: modifiers));
        }
    }
}
