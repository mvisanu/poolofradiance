using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RadiantPool.Rules
{
    /// <summary>A parsed dice expression like "2d6+3", "d20", "1d8-1", or a flat "5".</summary>
    public readonly struct DiceExpression
    {
        public int Count { get; }
        public int Sides { get; }
        public int Modifier { get; }

        public DiceExpression(int count, int sides, int modifier)
        {
            if (count < 0) throw new ArgumentException("count must be >= 0");
            if (count > 0 && sides < 2) throw new ArgumentException("sides must be >= 2");
            Count = count;
            Sides = sides;
            Modifier = modifier;
        }

        private static readonly Regex Pattern = new Regex(
            @"^\s*(?:(?<count>\d*)d(?<sides>\d+))?\s*(?:(?<sign>[+-])\s*(?<mod>\d+))?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static DiceExpression Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Empty dice expression.");

            // Flat number ("5") — no dice part.
            if (int.TryParse(text.Trim(), out int flat))
                return new DiceExpression(0, 0, flat);

            var m = Pattern.Match(text);
            if (!m.Success || !m.Groups["sides"].Success)
                throw new FormatException($"Invalid dice expression '{text}'.");

            int count = m.Groups["count"].Value.Length == 0 ? 1 : int.Parse(m.Groups["count"].Value);
            int sides = int.Parse(m.Groups["sides"].Value);
            int mod = 0;
            if (m.Groups["mod"].Success)
            {
                mod = int.Parse(m.Groups["mod"].Value);
                if (m.Groups["sign"].Value == "-") mod = -mod;
            }
            return new DiceExpression(count, sides, mod);
        }

        public RollResult Roll(IRng rng)
        {
            var rolls = new int[Count];
            int sum = 0;
            for (int i = 0; i < Count; i++)
            {
                rolls[i] = rng.Next(1, Sides);
                sum += rolls[i];
            }
            return new RollResult(sum + Modifier, rolls, Modifier);
        }

        /// <summary>Average result rounded down, per SRD fixed-HP convention.</summary>
        public int Average => (int)Math.Floor(Count * (Sides + 1) / 2.0) + Modifier;

        public override string ToString() =>
            Count == 0 ? Modifier.ToString()
            : Modifier == 0 ? $"{Count}d{Sides}"
            : $"{Count}d{Sides}{(Modifier > 0 ? "+" : "")}{Modifier}";
    }

    public readonly struct RollResult
    {
        public int Total { get; }
        public IReadOnlyList<int> Dice { get; }
        public int Modifier { get; }

        public RollResult(int total, IReadOnlyList<int> dice, int modifier)
        {
            Total = total;
            Dice = dice;
            Modifier = modifier;
        }
    }

    public static class Dice
    {
        public static RollResult Roll(string expression, IRng rng) =>
            DiceExpression.Parse(expression).Roll(rng);

        /// <summary>d20 roll honoring advantage/disadvantage. Returns both raw dice for logs.</summary>
        public static D20Result RollD20(IRng rng, Advantage advantage = Advantage.None)
        {
            int first = rng.Next(1, 20);
            if (advantage == Advantage.None)
                return new D20Result(first, first, first);

            int second = rng.Next(1, 20);
            int chosen = advantage == Advantage.Advantage
                ? Math.Max(first, second)
                : Math.Min(first, second);
            return new D20Result(chosen, first, second);
        }
    }

    public enum Advantage { None, Advantage, Disadvantage }

    public readonly struct D20Result
    {
        public int Value { get; }
        public int First { get; }
        public int Second { get; }
        public bool IsNat20 => Value == 20;
        public bool IsNat1 => Value == 1;

        public D20Result(int value, int first, int second)
        {
            Value = value;
            First = first;
            Second = second;
        }
    }
}
