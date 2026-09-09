using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CRPG.Core
{
    /// <summary>Standard dice notation: "d20", "2d6", "1d8+3", "4d6-2".</summary>
    [Serializable]
    public struct DiceExpression : IEquatable<DiceExpression>
    {
        private static readonly Regex Pattern = new Regex(
            @"^\s*(\d*)d(\d+)\s*(?:([+-])\s*(\d+))?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Not a `readonly struct`, and these are real fields rather than get-only auto-properties, because
        // Unity's serializer can't write into readonly fields or bare properties -- needed so a DiceExpression
        // can be typed directly into the Inspector (e.g. an ability's damage amount), not just constructed in code.
        [SerializeField] private int count;
        [SerializeField] private int sides;
        [SerializeField] private int modifier;

        public DiceExpression(int count, int sides, int modifier = 0)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), "A dice expression needs at least one die.");
            if (sides < 2) throw new ArgumentOutOfRangeException(nameof(sides), "A die needs at least two sides.");

            this.count = count;
            this.sides = sides;
            this.modifier = modifier;
        }

        public readonly int Count => count;
        public readonly int Sides => sides;
        public readonly int Modifier => modifier;

        public static DiceExpression Parse(string expression)
        {
            if (!TryParse(expression, out var result))
                throw new FormatException($"'{expression}' is not a valid dice expression (expected e.g. \"2d6+3\").");
            return result;
        }

        public static bool TryParse(string expression, out DiceExpression result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(expression)) return false;

            var match = Pattern.Match(expression);
            if (!match.Success) return false;

            var count = match.Groups[1].Success && match.Groups[1].Value.Length > 0
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                : 1;
            var sides = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            var modifier = 0;
            if (match.Groups[3].Success)
            {
                var magnitude = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                modifier = match.Groups[3].Value == "-" ? -magnitude : magnitude;
            }

            if (count < 1 || sides < 2) return false;

            result = new DiceExpression(count, sides, modifier);
            return true;
        }

        public readonly bool Equals(DiceExpression other) => Count == other.Count && Sides == other.Sides && Modifier == other.Modifier;
        public override readonly bool Equals(object obj) => obj is DiceExpression other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(Count, Sides, Modifier);

        public override readonly string ToString()
        {
            var expression = $"{Count}d{Sides}";
            if (Modifier > 0) expression += $"+{Modifier.ToString(CultureInfo.InvariantCulture)}";
            else if (Modifier < 0) expression += Modifier.ToString(CultureInfo.InvariantCulture);
            return expression;
        }

        /// <summary>Convenience for authoring literals like <c>DiceExpression d = "2d6+3";</c>. Throws on malformed input.</summary>
        public static implicit operator DiceExpression(string expression) => Parse(expression);
    }
}
