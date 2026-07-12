using System;

namespace RadiantPool.Rules
{
    /// <summary>Global monster-side difficulty easing. Stat blocks stay canonical SRD
    /// (ContentValidationTests keeps them aligned with content JSON); these knobs shave
    /// the monster side at run time so a small party isn't ground down. Applied in
    /// MonsterDefinition.Spawn (max HP) and CombatMath.ResolveAttack (to-hit for
    /// non-player attackers). Tune here, nowhere else.</summary>
    public static class Difficulty
    {
        /// <summary>Monster max-HP multiplier at spawn.</summary>
        public const double MonsterHpScale = 0.85;

        /// <summary>Flat penalty to monster attack rolls.</summary>
        public const int MonsterToHitPenalty = 1;

        public static int EaseMonsterHp(int rolledHp) =>
            Math.Max(1, (int)Math.Round(rolledHp * MonsterHpScale,
                MidpointRounding.AwayFromZero));
    }
}
