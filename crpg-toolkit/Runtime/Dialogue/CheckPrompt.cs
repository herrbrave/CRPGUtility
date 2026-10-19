using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;

namespace CRPG.Dialogue
{
    /// <summary>One roller of a check as it will roll: who, the named parts of the bonus, the advantage from rules, and the die.</summary>
    public sealed class CheckerPrompt
    {
        public CheckerPrompt(RuntimeEntityId id, IReadOnlyList<CheckBonusPart> breakdown, int bonus, AdvantageState advantage, DiceExpression die)
        {
            Id = id; Breakdown = breakdown; Bonus = bonus; Advantage = advantage; Die = die;
        }

        public RuntimeEntityId Id { get; }
        public IReadOnlyList<CheckBonusPart> Breakdown { get; }
        public int Bonus { get; }
        public AdvantageState Advantage { get; }
        public DiceExpression Die { get; }
    }

    /// <summary>A check looked at BEFORE it is rolled (nothing is rolled, the conversation does not move): what is being tested, against what, and who rolls with what. A game's dice popup shows this and lets the player add extras.</summary>
    public sealed class CheckPrompt
    {
        public CheckPrompt(string queryKey, int difficultyClass, CheckPerformer performer, IReadOnlyList<CheckerPrompt> checkers)
        {
            QueryKey = queryKey; DifficultyClass = difficultyClass; Performer = performer; Checkers = checkers;
        }

        public string QueryKey { get; }
        public int DifficultyClass { get; }
        public CheckPerformer Performer { get; }
        public IReadOnlyList<CheckerPrompt> Checkers { get; }
    }

    /// <summary>What the player chose to add to a check: flat bonuses (a Guidance die already rolled becomes one of these) and an extra advantage state, combined with the rule's own advantage (advantage and disadvantage cancel). Applied to every roller of the check.</summary>
    public sealed class CheckRollOptions
    {
        public CheckRollOptions(IReadOnlyList<RollModifier> extraModifiers = null, AdvantageState extraAdvantage = AdvantageState.Normal)
        {
            ExtraModifiers = extraModifiers ?? System.Array.Empty<RollModifier>();
            ExtraAdvantage = extraAdvantage;
        }

        public IReadOnlyList<RollModifier> ExtraModifiers { get; }
        public AdvantageState ExtraAdvantage { get; }
    }
}
