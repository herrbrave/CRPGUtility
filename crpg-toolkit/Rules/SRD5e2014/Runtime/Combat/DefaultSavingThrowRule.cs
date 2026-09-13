using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Rules.SRD5
{
    /// <summary>Ability modifier + proficiency bonus (only if proficient in this specific saving throw) +
    /// whatever's gathered under the "SavingThrow:&lt;ability&gt;" query key -- same NumericRuleQuery +
    /// FeatureModifierCollector shape as DefaultAttackBonusRule/DefaultArmorClassRule. Proficiency is checked via
    /// CharacterState.HasProficiency(savingStatId) -- the same generic proficiency bag skills already use; a
    /// stat id can't collide with a skill id since they're different DefinitionId strings.</summary>
    public sealed class DefaultSavingThrowRule : ISavingThrowRule
    {
        public const string QueryKeyPrefix = "SavingThrow:";
        public const string SpellSaveDCQueryKey = "SpellSaveDC";

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;

        public DefaultSavingThrowRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase)
        {
            this.characters = characters;
            this.rulesDatabase = rulesDatabase;
        }

        public int ComputeSavingThrowBonus(RuntimeEntityId characterId, DefinitionId savingStatId)
        {
            if (!characters.TryGetCharacter(characterId, out var character)) return 0;

            var abilityModifier = AbilityModifier.From(character.Stats.GetScore(savingStatId));
            var proficiencyBonus = character.HasProficiency(savingStatId) ? ProficiencyBonus.From(character.Level) : 0;
            var query = new NumericRuleQuery(character, abilityModifier + proficiencyBonus);
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rulesDatabase, QueryKeyPrefix + savingStatId.Value))
                query.AddModifier(modifier.ToRuleModifier(source));

            return (int)query.Evaluate();
        }

        public AdvantageState ComputeAdvantage(RuntimeEntityId characterId, DefinitionId savingStatId)
        {
            if (!characters.TryGetCharacter(characterId, out var character)) return AdvantageState.Normal;
            return AdvantageCollector.Collect(character, rulesDatabase, QueryKeyPrefix + savingStatId.Value);
        }

        public bool AutoFails(RuntimeEntityId characterId, DefinitionId savingStatId)
        {
            if (!characters.TryGetCharacter(characterId, out var character)) return false;

            return FeatureModifierCollector.Collect(character, rulesDatabase, QueryKeyPrefix + savingStatId.Value)
                .Any(m => m.Modifier.ToRuleModifier(m.Source).Operation == RuleModifierOperation.AutoFailSave);
        }

        public int ComputeSpellSaveDC(RuntimeEntityId casterId, DefinitionId spellcastingStatId)
        {
            if (!characters.TryGetCharacter(casterId, out var caster)) return 0;

            var abilityModifier = AbilityModifier.From(caster.Stats.GetScore(spellcastingStatId));
            var proficiencyBonus = ProficiencyBonus.From(caster.Level);
            var query = new NumericRuleQuery(caster, 8 + abilityModifier + proficiencyBonus);
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(caster, rulesDatabase, SpellSaveDCQueryKey))
                query.AddModifier(modifier.ToRuleModifier(source));

            return (int)query.Evaluate();
        }
    }
}
