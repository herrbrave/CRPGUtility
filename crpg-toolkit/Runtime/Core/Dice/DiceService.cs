using System.Collections.Generic;
using System.Linq;

namespace CRPG.Core
{
    public sealed class DiceService : IDiceService
    {
        private readonly IRandomSource random;

        public DiceService(IRandomSource random)
        {
            this.random = random;
        }

        public RollResult Roll(RollRequest request)
        {
            var expression = request.Dice;
            var primarySet = RollSet(expression);

            IReadOnlyList<DieRollResult> dice;
            if (request.Advantage == AdvantageState.Normal)
            {
                dice = primarySet;
            }
            else
            {
                // Advantage/disadvantage rolls the whole expression a second time and keeps the better/worse
                // total outright. For the common case (a single d20) this matches 5e-style advantage exactly;
                // rulesets that only want the primary die doubled should request a 1-die expression.
                var secondarySet = RollSet(expression);
                var primaryTotal = primarySet.Sum(d => d.Value);
                var secondaryTotal = secondarySet.Sum(d => d.Value);

                var keepPrimary = request.Advantage == AdvantageState.Advantage
                    ? primaryTotal >= secondaryTotal
                    : primaryTotal <= secondaryTotal;

                dice = primarySet.Select(d => new DieRollResult(d.Sides, d.Value, keepPrimary))
                    .Concat(secondarySet.Select(d => new DieRollResult(d.Sides, d.Value, !keepPrimary)))
                    .ToList();
            }

            var modifierTotal = request.Modifiers.Sum(m => m.Value);
            return new RollResult(request, dice, modifierTotal);
        }

        private List<DieRollResult> RollSet(DiceExpression expression)
        {
            var results = new List<DieRollResult>(expression.Count);
            for (var i = 0; i < expression.Count; i++)
                results.Add(new DieRollResult(expression.Sides, random.RollDie(expression.Sides), kept: true));
            return results;
        }
    }
}
