using System;

namespace RadiantPool.Rules
{
    /// <summary>Monster-side runtime difficulty. Canonical SRD stat blocks never change;
    /// quest monsters receive an encounter level derived from the strongest human hero.
    /// All HP, AC, attack, and damage scaling knobs live here and nowhere else.</summary>
    public static class Difficulty
    {
        /// <summary>Monster max-HP multiplier at spawn.</summary>
        public const double MonsterHpScale = 0.85;

        /// <summary>Flat penalty to monster attack rolls.</summary>
        public const int MonsterToHitPenalty = 1;

        /// <summary>Flat HP per encounter level above 1. A flat increase strengthens
        /// fragile rank-and-file enemies without multiplying an elite boss's large pool.</summary>
        public const int MonsterHpPerLevel = 1;

        /// <summary>One AC/to-hit step at encounter level 4.</summary>
        public const int MonsterStatLevelsPerBonus = 3;

        /// <summary>One flat damage step at encounter level 4.</summary>
        public const int MonsterDamageLevelsPerBonus = 3;

        public static int EaseMonsterHp(int rolledHp) =>
            Math.Max(1, (int)Math.Round(rolledHp * MonsterHpScale,
                MidpointRounding.AwayFromZero));

        /// <summary>The requested quest challenge: one below the strongest hero, with
        /// level 1 as the floor because this campaign has no level-0 combatants.</summary>
        public static int TargetMonsterLevel(int characterLevel)
        {
            int cappedHero = Math.Max(1, Math.Min(Progression.MaxLevel, characterLevel));
            return Math.Max(1, cappedHero - 1);
        }

        public static int ScaleMonsterHp(int rolledHp, int encounterLevel)
        {
            int level = Math.Max(1, encounterLevel);
            return Math.Max(1, EaseMonsterHp(rolledHp) + (level - 1) * MonsterHpPerLevel);
        }

        public static int MonsterArmorBonus(int encounterLevel) =>
            Math.Max(0, encounterLevel - 1) / MonsterStatLevelsPerBonus;

        public static int MonsterToHitBonus(int encounterLevel) =>
            Math.Max(0, encounterLevel - 1) / MonsterStatLevelsPerBonus;

        public static int MonsterDamageBonus(int encounterLevel) =>
            Math.Max(0, encounterLevel - 1) / MonsterDamageLevelsPerBonus;
    }
}
