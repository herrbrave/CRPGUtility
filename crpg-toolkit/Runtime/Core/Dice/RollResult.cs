using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CRPG.Core
{
    public sealed class RollResult
    {
        public RollResult(RollRequest request, IReadOnlyList<DieRollResult> dice, double modifierTotal)
        {
            Request = request;
            Dice = dice;
            ModifierTotal = modifierTotal;
        }

        public RollRequest Request { get; }

        /// <summary>Every die actually rolled, including the discarded half of an advantage/disadvantage roll.</summary>
        public IReadOnlyList<DieRollResult> Dice { get; }

        public double ModifierTotal { get; }

        /// <summary>Kept dice plus the dice expression's own flat modifier (e.g. the "+3" in "1d20+3").</summary>
        public double DiceTotal => Dice.Where(d => d.Kept).Sum(d => d.Value) + Request.Dice.Modifier;

        public double Total => DiceTotal + ModifierTotal;

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(string.Join(" + ", Dice.Where(d => d.Kept).Select(d => d.Value.ToString())));

            if (Request.Dice.Modifier > 0) builder.Append($" + {Request.Dice.Modifier}");
            else if (Request.Dice.Modifier < 0) builder.Append($" - {-Request.Dice.Modifier}");

            foreach (var modifier in Request.Modifiers)
            {
                builder.Append(modifier.Value >= 0
                    ? $" + {modifier.Value} ({modifier.Source})"
                    : $" - {-modifier.Value} ({modifier.Source})");
            }

            builder.Append($" = {Total}");
            return builder.ToString();
        }
    }
}
