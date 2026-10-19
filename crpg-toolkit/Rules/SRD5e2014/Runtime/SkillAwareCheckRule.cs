using System.Collections.Generic;
using System.Linq;
using System.Text;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// The SRD5 skill / ability check: the governing ability's modifier, plus the proficiency bonus when the character is proficient in the skill, plus whatever feature modifiers are gathered under the key (so content tagged the old way keeps working).
    /// Keys are the existing "Skill:Name" and "Ability:Name" strings, matched to the ruleset's <see cref="SkillDefinition"/> / <see cref="StatDefinition"/> ignoring case, spaces, hyphens and underscores. An ability check never adds proficiency: the proficiency bag also holds SAVE proficiencies (a stat id), which are not check proficiencies.
    /// An unknown skill, stat or malformed key is a zero bonus, never an exception. (The default rule, <see cref="DefaultSkillCheckRule"/>, adds only the tagged modifiers and stays the toolkit default.)
    /// </summary>
    public sealed class SkillAwareCheckRule : IDetailedSkillCheckRule
    {
        public const string SkillPrefix = "Skill:", AbilityPrefix = "Ability:";
        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rulesDatabase;

        public SkillAwareCheckRule(ICharacterRegistry characters, IRulesDatabase rulesDatabase)
        {
            this.characters = characters; this.rulesDatabase = rulesDatabase;
        }

        public DiceExpression CheckDie => new(1, 20);

        public int ComputeSkillCheckBonus(RuntimeEntityId characterId, string queryKey) => ComputeBreakdown(characterId, queryKey).Sum(p => p.Value);

        public AdvantageState ComputeAdvantage(RuntimeEntityId characterId, string queryKey)
        {
            if (!characters.TryGetCharacter(characterId, out var character)) return AdvantageState.Normal;
            return AdvantageCollector.Collect(character, rulesDatabase, queryKey);
        }

        public IReadOnlyList<CheckBonusPart> ComputeBreakdown(RuntimeEntityId characterId, string queryKey)
        {
            var parts = new List<CheckBonusPart>();
            if (string.IsNullOrEmpty(queryKey) || !characters.TryGetCharacter(characterId, out var character)) return parts;

            var baseValue = 0;
            if (TryResolve(queryKey, out var stat, out var skill))
            {
                var modifier = AbilityModifier.From(character.Stats.GetScore(stat.Id));
                parts.Add(new CheckBonusPart(stat.DisplayName, modifier)); baseValue += modifier;
                if (skill != null && character.HasProficiency(skill.Id))
                {
                    var proficiency = ProficiencyBonus.From(character.Level);
                    parts.Add(new CheckBonusPart("proficient", proficiency)); baseValue += proficiency;
                }
            }

            var query = new NumericRuleQuery(character, baseValue);
            var gathered = FeatureModifierCollector.Collect(character, rulesDatabase, queryKey).ToList();
            foreach (var (source, modifier) in gathered) query.AddModifier(modifier.ToRuleModifier(source));
            var total = (int)query.Evaluate();

            var named = baseValue;
            foreach (var (source, modifier) in gathered)
            {
                var rule = modifier.ToRuleModifier(source);
                if (rule.Operation != RuleModifierOperation.Add && rule.Operation != RuleModifierOperation.Subtract) continue;
                var value = (int)(rule.Operation == RuleModifierOperation.Subtract ? -rule.Value : rule.Value);
                parts.Add(new CheckBonusPart(source, value)); named += value;
            }

            if (named != total) parts.Add(new CheckBonusPart("other", total - named));   // a multiplier / override / clamp: still sums to the bonus
            return parts;
        }

        private bool TryResolve(string queryKey, out StatDefinition stat, out SkillDefinition skill)
        {
            stat = null; skill = null;
            if (queryKey.StartsWith(SkillPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                var wanted = Normalize(queryKey.Substring(SkillPrefix.Length));
                skill = rulesDatabase.All<SkillDefinition>().FirstOrDefault(s => Normalize(StripPrefix(s.Id.Value, "skill-")) == wanted || Normalize(s.DisplayName) == wanted);
                stat = skill != null ? skill.GoverningStat : null;
                return stat != null;
            }

            if (queryKey.StartsWith(AbilityPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                var wanted = Normalize(queryKey.Substring(AbilityPrefix.Length));
                stat = rulesDatabase.All<StatDefinition>().FirstOrDefault(s => Normalize(StripPrefix(s.Id.Value, "stat-")) == wanted || Normalize(s.DisplayName) == wanted || Normalize(s.Abbreviation) == wanted);
                return stat != null;
            }

            return false;
        }

        private static string StripPrefix(string id, string prefix) => id != null && id.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) ? id.Substring(prefix.Length) : id;

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var builder = new StringBuilder(text.Length);
            foreach (var c in text) if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            return builder.ToString();
        }
    }
}
