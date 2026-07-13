using System;

namespace RadiantPool.Rules
{
    /// <summary>Levelling, and the one place the campaign's ability-point house rule lives.
    ///
    /// SRD 5.1 grants an Ability Score Improvement at 4th level only — across a level 1–5
    /// campaign that is exactly ONE choice about who your character becomes, which is too
    /// few for a game whose whole arc is those five levels. So: a point at every level-up,
    /// and the SRD's two at 4th. Nothing else about the PC is house-ruled — the XP table,
    /// hit dice, proficiency bonus and the 20 cap are all still SRD (monster easing stays in
    /// Difficulty.cs, and XP is untouched there).
    ///
    /// PartyComposition/Difficulty pattern: the knob is here, tested by ProgressionTests, and
    /// nothing else in the game gets to invent its own answer.</summary>
    public static class Progression
    {
        public const int MaxLevel = 5;

        /// <summary>SRD ceiling for a player-character ability score.</summary>
        public const int MaxAbilityScore = 20;

        /// <summary>Points granted for REACHING this level. Level 1 grants none: that is the
        /// character you rolled, not a reward.</summary>
        public static int AbilityPointsForLevel(int newLevel)
        {
            if (newLevel <= 1 || newLevel > MaxLevel) return 0;
            return newLevel == 4 ? 2 : 1;   // the SRD's ASI at 4th, one everywhere else
        }

        /// <summary>Every point a character will have earned by the time they hit this level.</summary>
        public static int TotalAbilityPointsByLevel(int level)
        {
            int total = 0;
            for (int l = 2; l <= level; l++) total += AbilityPointsForLevel(l);
            return total;
        }

        /// <summary>The ability a class lives by — what an AI companion raises when it levels,
        /// since no one is at the keyboard to choose for it.</summary>
        public static Ability PrimaryAbility(CharacterClass c)
        {
            switch (c)
            {
                case CharacterClass.Fighter: return Ability.Str;
                case CharacterClass.Rogue: return Ability.Dex;
                case CharacterClass.Wizard: return Ability.Int;
                case CharacterClass.Cleric: return Ability.Wis;
                default: return Ability.Con;
            }
        }

        /// <summary>XP needed to reach the next level, 0 at the cap.</summary>
        public static int XpToNext(int level, int xp)
        {
            if (level >= MaxLevel) return 0;
            return Math.Max(0, ClassData.XpThresholds[level] - xp);
        }

        /// <summary>How far this character is through their current level: XP earned into it,
        /// the size of the level, and the fraction (0–1) — what the XP bar draws. At the cap
        /// the bar is full, not empty.</summary>
        public static (int intoLevel, int levelSpan, float fraction) Progress(int level, int xp)
        {
            if (level >= MaxLevel) return (0, 0, 1f);
            int floor = ClassData.XpThresholds[level - 1];
            int ceiling = ClassData.XpThresholds[level];
            int span = ceiling - floor;
            if (span <= 0) return (0, 0, 1f);
            int into = Math.Min(Math.Max(xp - floor, 0), span);
            return (into, span, into / (float)span);
        }
    }
}
