using System;

namespace RadiantPool.Rules
{
    /// <summary>All randomness in the rules library flows through this. The server owns the
    /// only seeded instance; tests inject fixed sequences.</summary>
    public interface IRng
    {
        /// <summary>Uniform integer in [minInclusive, maxInclusive].</summary>
        int Next(int minInclusive, int maxInclusive);
    }

    public sealed class SeededRng : IRng
    {
        private readonly Random _random;

        public SeededRng(int seed) => _random = new Random(seed);

        public int Next(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
                throw new ArgumentException("max must be >= min");
            return _random.Next(minInclusive, maxInclusive + 1);
        }
    }

    /// <summary>Returns a scripted sequence of values; for tests.</summary>
    public sealed class FixedRng : IRng
    {
        private readonly int[] _values;
        private int _index;

        public FixedRng(params int[] values) => _values = values;

        public int Next(int minInclusive, int maxInclusive)
        {
            if (_index >= _values.Length)
                throw new InvalidOperationException("FixedRng ran out of scripted values.");
            int v = _values[_index++];
            if (v < minInclusive || v > maxInclusive)
                throw new InvalidOperationException(
                    $"FixedRng value {v} outside requested range [{minInclusive},{maxInclusive}].");
            return v;
        }
    }
}
