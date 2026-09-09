using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Combat
{
    /// <summary>10 + Dexterity modifier + whatever's gathered under the "ArmorClass" query key (armor,
    /// shields, Fighting Style: Defense, ...) -- same NumericRuleQuery + FeatureModifierCollector shape
    /// DefaultInitiativeRule already established, just with a live ability-modifier base instead of a flat 0
    /// (AC has to track the character's *current* Dexterity, not a value baked into content at authoring time).</summary>
    public sealed class DefaultArmorClassRule : IArmorClassRule
    {
        public const string QueryKey = "ArmorClass";

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;
        private readonly DefinitionId dexterityStatId;

        public DefaultArmorClassRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase, DefinitionId dexterityStatId)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
            this.dexterityStatId = dexterityStatId;
        }

        public int ComputeArmorClass(RuntimeEntityId target)
        {
            if (!characters.TryGetCharacter(target, out var character)) return 10;

            var dexModifier = AbilityModifier.From(character.Stats.GetScore(dexterityStatId));
            var query = new NumericRuleQuery(character, 10 + dexModifier);
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, QueryKey))
                query.AddModifier(modifier.ToRuleModifier(source));

            return (int)query.Evaluate();
        }
    }
}
