using System;

namespace RadiantPool.Rules
{
    public enum Ability { Str, Dex, Con, Int, Wis, Cha }

    public sealed class AbilityScores
    {
        private readonly int[] _scores = new int[6];

        public AbilityScores(int str, int dex, int con, int intl, int wis, int cha)
        {
            _scores[0] = str; _scores[1] = dex; _scores[2] = con;
            _scores[3] = intl; _scores[4] = wis; _scores[5] = cha;
            foreach (var s in _scores)
                if (s < 1 || s > 30) throw new ArgumentException("Ability scores must be 1-30.");
        }

        public int this[Ability a] => _scores[(int)a];

        /// <summary>SRD modifier: floor((score - 10) / 2).</summary>
        public int Modifier(Ability a) => (int)Math.Floor((_scores[(int)a] - 10) / 2.0);

        public AbilityScores WithIncrease(Ability a, int amount)
        {
            var copy = new int[6];
            _scores.CopyTo(copy, 0);
            copy[(int)a] = Math.Min(20, copy[(int)a] + amount);
            return new AbilityScores(copy[0], copy[1], copy[2], copy[3], copy[4], copy[5]);
        }
    }

    public enum DamageType
    {
        Slashing, Piercing, Bludgeoning,
        Fire, Cold, Lightning, Thunder, Acid, Poison,
        Radiant, Necrotic, Force, Psychic
    }
}
