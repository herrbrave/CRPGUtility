using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>
    /// Raw stat scores for one character. Deliberately stores only the score a ruleset assigned (e.g. 16),
    /// never a derived modifier -- turning a score into a modifier goes through a ruleset's own
    /// <c>NumericRuleQuery</c> (CRPG.Rules), matching the spec's "ability modifiers are computed through a
    /// rule query rather than baked into character data".
    /// </summary>
    public sealed class StatBlock
    {
        private readonly Dictionary<DefinitionId, int> scoresByStatId = new();

        public int GetScore(DefinitionId statId) => scoresByStatId.TryGetValue(statId, out var score) ? score : 0;

        public void SetScore(DefinitionId statId, int score) => scoresByStatId[statId] = score;

        public bool HasScore(DefinitionId statId) => scoresByStatId.ContainsKey(statId);

        public IReadOnlyDictionary<DefinitionId, int> AllScores => scoresByStatId;
    }
}
