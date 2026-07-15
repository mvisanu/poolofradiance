using System;
using System.Collections.Generic;

namespace RadiantPool.Rules
{
    public enum CharacterClass { Fighter, Wizard, Cleric, Rogue }
    public enum Race { Human, Dwarf, Elf, Halfling }

    /// <summary>SRD 5.1 class data for levels 1-20. Static tables here are the single
    /// source; the /content JSON mirrors them for tooling.</summary>
    public static class ClassData
    {
        public static int HitDie(CharacterClass c) => c switch
        {
            CharacterClass.Fighter => 10,
            CharacterClass.Wizard => 6,
            CharacterClass.Cleric => 8,
            CharacterClass.Rogue => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(c))
        };

        public static (Ability, Ability) SaveProficiencies(CharacterClass c) => c switch
        {
            CharacterClass.Fighter => (Ability.Str, Ability.Con),
            CharacterClass.Wizard => (Ability.Int, Ability.Wis),
            CharacterClass.Cleric => (Ability.Wis, Ability.Cha),
            CharacterClass.Rogue => (Ability.Dex, Ability.Int),
            _ => throw new ArgumentOutOfRangeException(nameof(c))
        };

        public static Ability? SpellcastingAbility(CharacterClass c) => c switch
        {
            CharacterClass.Wizard => Ability.Int,
            CharacterClass.Cleric => Ability.Wis,
            _ => (Ability?)null
        };

        /// <summary>Full-caster slot table, levels 1-20. [characterLevel-1][slotLevel-1].</summary>
        private static readonly int[][] FullCasterSlots =
        {
            new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 },
            new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 2, 0, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 0, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 2, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 0, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 1, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 2, 0, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 1, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 0, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 0, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 0, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 1, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 1, 0 },
            new[] { 4, 3, 3, 3, 2, 1, 1, 1, 1 },
            new[] { 4, 3, 3, 3, 3, 1, 1, 1, 1 },
            new[] { 4, 3, 3, 3, 3, 2, 1, 1, 1 },
            new[] { 4, 3, 3, 3, 3, 2, 2, 1, 1 },
        };

        /// <summary>Spell slots per slot level (1..3) at character levels 1..5. Empty for non-casters.</summary>
        public static int[] SpellSlots(CharacterClass c, int level)
        {
            if (level < 1 || level > Progression.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            return SpellcastingAbility(c) == null
                ? new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                : (int[])FullCasterSlots[level - 1].Clone();
        }

        /// <summary>Proficiency bonus follows the full SRD level 1-20 table.</summary>
        public static int ProficiencyBonus(int level)
        {
            if (level < 1 || level > Progression.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            return 2 + (level - 1) / 4;
        }

        /// <summary>XP required to reach each level in the full SRD level 1-20 table.</summary>
        public static readonly int[] XpThresholds =
        {
            0, 300, 900, 2700, 6500, 14000, 23000, 34000, 48000, 64000,
            85000, 100000, 120000, 140000, 165000, 195000, 225000, 265000,
            305000, 355000
        };

        public static int LevelForXp(int xp)
        {
            int level = 1;
            for (int i = 1; i < XpThresholds.Length; i++)
                if (xp >= XpThresholds[i]) level = i + 1;
            return Math.Min(level, Progression.MaxLevel);
        }

        /// <summary>Attacks made by a fighter when taking the Attack action.</summary>
        public static int AttacksPerAction(CharacterClass c, int level)
        {
            if (level < 1 || level > Progression.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            if (c != CharacterClass.Fighter) return 1;
            if (level >= 20) return 4;
            if (level >= 11) return 3;
            return level >= 5 ? 2 : 1;
        }

        /// <summary>Class features gained at each level, 1-20 (names only; mechanics that
        /// matter to v1 combat are implemented where noted).</summary>
        public static IReadOnlyList<string> FeaturesAt(CharacterClass c, int level) =>
            Features.TryGetValue((c, level), out var f) ? f : Array.Empty<string>();

        private static readonly Dictionary<(CharacterClass, int), string[]> Features =
            new Dictionary<(CharacterClass, int), string[]>
        {
            { (CharacterClass.Fighter, 1), new[] { "Fighting Style", "Second Wind" } },
            { (CharacterClass.Fighter, 2), new[] { "Action Surge" } },
            { (CharacterClass.Fighter, 3), new[] { "Martial Archetype: Champion (Improved Critical)" } },
            { (CharacterClass.Fighter, 4), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Fighter, 5), new[] { "Extra Attack" } },
            { (CharacterClass.Fighter, 6), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Fighter, 7), new[] { "Champion Feature" } },
            { (CharacterClass.Fighter, 8), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Fighter, 9), new[] { "Indomitable" } },
            { (CharacterClass.Fighter, 10), new[] { "Additional Fighting Style" } },
            { (CharacterClass.Fighter, 11), new[] { "Extra Attack (3 attacks)" } },
            { (CharacterClass.Fighter, 12), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Fighter, 13), new[] { "Indomitable (2 uses)" } },
            { (CharacterClass.Fighter, 14), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Fighter, 15), new[] { "Superior Critical" } },
            { (CharacterClass.Fighter, 16), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Fighter, 17), new[] { "Action Surge (2 uses)", "Indomitable (3 uses)" } },
            { (CharacterClass.Fighter, 18), new[] { "Survivor" } },
            { (CharacterClass.Fighter, 19), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Fighter, 20), new[] { "Extra Attack (4 attacks)" } },
            { (CharacterClass.Wizard, 1), new[] { "Spellcasting", "Arcane Recovery" } },
            { (CharacterClass.Wizard, 2), new[] { "Arcane Tradition: Evocation (Sculpt Spells)" } },
            { (CharacterClass.Wizard, 3), Array.Empty<string>() },
            { (CharacterClass.Wizard, 4), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Wizard, 5), Array.Empty<string>() },
            { (CharacterClass.Wizard, 6), new[] { "Potent Cantrip" } },
            { (CharacterClass.Wizard, 8), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Wizard, 10), new[] { "Empowered Evocation" } },
            { (CharacterClass.Wizard, 12), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Wizard, 14), new[] { "Overchannel" } },
            { (CharacterClass.Wizard, 16), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Wizard, 18), new[] { "Spell Mastery" } },
            { (CharacterClass.Wizard, 19), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Wizard, 20), new[] { "Signature Spells" } },
            { (CharacterClass.Cleric, 1), new[] { "Spellcasting", "Divine Domain: Life" } },
            { (CharacterClass.Cleric, 2), new[] { "Channel Divinity: Turn Undead" } },
            { (CharacterClass.Cleric, 3), Array.Empty<string>() },
            { (CharacterClass.Cleric, 4), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Cleric, 5), new[] { "Destroy Undead (CR 1/2)" } },
            { (CharacterClass.Cleric, 6), new[] { "Blessed Healer", "Channel Divinity (2 uses)" } },
            { (CharacterClass.Cleric, 8), new[] { "Ability Score Improvement", "Divine Strike" } },
            { (CharacterClass.Cleric, 10), new[] { "Divine Intervention" } },
            { (CharacterClass.Cleric, 12), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Cleric, 14), new[] { "Destroy Undead (CR 3)" } },
            { (CharacterClass.Cleric, 16), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Cleric, 17), new[] { "Supreme Healing" } },
            { (CharacterClass.Cleric, 18), new[] { "Channel Divinity (3 uses)" } },
            { (CharacterClass.Cleric, 19), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Cleric, 20), new[] { "Divine Intervention Improvement" } },
            { (CharacterClass.Rogue, 1), new[] { "Sneak Attack (1d6)", "Expertise", "Thieves' Cant" } },
            { (CharacterClass.Rogue, 2), new[] { "Cunning Action" } },
            { (CharacterClass.Rogue, 3), new[] { "Roguish Archetype: Thief", "Sneak Attack (2d6)" } },
            { (CharacterClass.Rogue, 4), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Rogue, 5), new[] { "Uncanny Dodge", "Sneak Attack (3d6)" } },
            { (CharacterClass.Rogue, 6), new[] { "Expertise" } },
            { (CharacterClass.Rogue, 7), new[] { "Evasion", "Sneak Attack (4d6)" } },
            { (CharacterClass.Rogue, 8), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Rogue, 9), new[] { "Supreme Sneak", "Sneak Attack (5d6)" } },
            { (CharacterClass.Rogue, 10), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Rogue, 11), new[] { "Reliable Talent", "Sneak Attack (6d6)" } },
            { (CharacterClass.Rogue, 12), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Rogue, 13), new[] { "Use Magic Device", "Sneak Attack (7d6)" } },
            { (CharacterClass.Rogue, 14), new[] { "Blindsense" } },
            { (CharacterClass.Rogue, 15), new[] { "Slippery Mind", "Sneak Attack (8d6)" } },
            { (CharacterClass.Rogue, 16), new[] { "Ability Score Improvement" } },
            { (CharacterClass.Rogue, 17), new[] { "Thief's Reflexes", "Sneak Attack (9d6)" } },
            { (CharacterClass.Rogue, 18), new[] { "Elusive" } },
            { (CharacterClass.Rogue, 19), new[] { "Ability Score Improvement", "Sneak Attack (10d6)" } },
            { (CharacterClass.Rogue, 20), new[] { "Stroke of Luck" } },
        };

        /// <summary>Sneak attack dice count at a given rogue level (SRD: ceil(level/2)).</summary>
        public static int SneakAttackDice(int level) => (level + 1) / 2;
    }

    public static class RaceData
    {
        /// <summary>Fixed ability increases (SRD).</summary>
        public static Dictionary<Ability, int> AbilityBonuses(Race r) => r switch
        {
            Race.Human => new Dictionary<Ability, int>
            {
                { Ability.Str, 1 }, { Ability.Dex, 1 }, { Ability.Con, 1 },
                { Ability.Int, 1 }, { Ability.Wis, 1 }, { Ability.Cha, 1 }
            },
            Race.Dwarf => new Dictionary<Ability, int> { { Ability.Con, 2 }, { Ability.Wis, 1 } },      // Hill Dwarf
            Race.Elf => new Dictionary<Ability, int> { { Ability.Dex, 2 }, { Ability.Int, 1 } },        // High Elf
            Race.Halfling => new Dictionary<Ability, int> { { Ability.Dex, 2 }, { Ability.Cha, 1 } },   // Lightfoot
            _ => throw new ArgumentOutOfRangeException(nameof(r))
        };

        public static int Speed(Race r) => r == Race.Dwarf || r == Race.Halfling ? 25 : 30;

        /// <summary>Hill dwarf: +1 HP per level. Other v1 racial traits handled in creation UI.</summary>
        public static int BonusHpPerLevel(Race r) => r == Race.Dwarf ? 1 : 0;
    }
}
