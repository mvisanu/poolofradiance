using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    public enum ArmorKind { None, Light, Medium, Heavy }

    public sealed class ArmorDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public ArmorKind Kind { get; }
        public int BaseAc { get; }
        public int MaxDexBonus { get; }   // int.MaxValue = uncapped

        public ArmorDefinition(string id, string name, ArmorKind kind, int baseAc, int maxDexBonus)
        {
            Id = id; Name = name; Kind = kind; BaseAc = baseAc; MaxDexBonus = maxDexBonus;
        }

        public static readonly ArmorDefinition Unarmored =
            new ArmorDefinition("unarmored", "Unarmored", ArmorKind.None, 10, int.MaxValue);
        public static readonly ArmorDefinition Leather =
            new ArmorDefinition("leather", "Leather", ArmorKind.Light, 11, int.MaxValue);
        public static readonly ArmorDefinition ScaleMail =
            new ArmorDefinition("scale_mail", "Scale Mail", ArmorKind.Medium, 14, 2);
        public static readonly ArmorDefinition ChainMail =
            new ArmorDefinition("chain_mail", "Chain Mail", ArmorKind.Heavy, 16, 0);
    }

    /// <summary>A player character: identity + build + derived stats. Extends Creature with
    /// class/level/spell-slot tracking. XP and level-up live in Progression.</summary>
    public sealed class CharacterSheet : Creature
    {
        public Race Race { get; }
        public CharacterClass Class { get; }
        public int Level { get; private set; }
        public int Xp { get; private set; }
        public ArmorDefinition Armor { get; private set; }
        public bool HasShield { get; private set; }
        public List<string> KnownSpells { get; } = new List<string>();

        // Remaining spell slots by slot level (index 0 = 1st-level slots).
        private int[] _slotsRemaining;

        public CharacterSheet(string id, string name, Race race, CharacterClass cls,
                              AbilityScores baseScores, int level = 1)
            : base(id, name, ApplyRace(baseScores, race), baseAc: 10, maxHp: 1)
        {
            if (level < 1 || level > 5) throw new ArgumentOutOfRangeException(nameof(level));
            Race = race;
            Class = cls;
            Level = level;
            Xp = ClassData.XpThresholds[level - 1];
            IsPlayerCharacter = true;
            Speed = RaceData.Speed(race);
            Armor = ArmorDefinition.Unarmored;

            var (s1, s2) = ClassData.SaveProficiencies(cls);
            SaveProficiencies.Add(s1);
            SaveProficiencies.Add(s2);

            ProficiencyBonus = ClassData.ProficiencyBonus(level);
            MaxHp = ComputeMaxHp();
            RestoreFull();
            _slotsRemaining = ClassData.SpellSlots(cls, level);
            RecomputeAc();
        }

        private static AbilityScores ApplyRace(AbilityScores s, Race race)
        {
            foreach (var kv in RaceData.AbilityBonuses(race))
                s = s.WithIncrease(kv.Key, kv.Value);
            return s;
        }

        /// <summary>SRD: max hit die at level 1, fixed average (die/2+1) per level after,
        /// +Con mod per level, + racial bonus per level. Recomputed from scratch so Con
        /// increases retroactively apply.</summary>
        public int ComputeMaxHp()
        {
            int die = ClassData.HitDie(Class);
            int perLevelBonus = Abilities.Modifier(Ability.Con) + RaceData.BonusHpPerLevel(Race);
            int hp = die + perLevelBonus;                       // level 1
            hp += (Level - 1) * (die / 2 + 1 + perLevelBonus);  // levels 2+
            return Math.Max(hp, Level); // never below 1/level
        }

        public void EquipArmor(ArmorDefinition armor)
        {
            Armor = armor;
            RecomputeAc();
        }

        public void SetShield(bool equipped)
        {
            HasShield = equipped;
            RecomputeAc();
        }

        private void RecomputeAc()
        {
            int dex = Math.Min(Abilities.Modifier(Ability.Dex), Armor.MaxDexBonus);
            BaseArmorClass = Armor.BaseAc + dex + (HasShield ? 2 : 0);
        }

        public Ability? SpellcastingAbility => ClassData.SpellcastingAbility(Class);

        public int SpellSaveDc => SpellcastingAbility is Ability a
            ? 8 + ProficiencyBonus + Abilities.Modifier(a)
            : throw new InvalidOperationException($"{Class} is not a spellcaster.");

        public int SpellAttackBonus => SpellcastingAbility is Ability a
            ? ProficiencyBonus + Abilities.Modifier(a)
            : throw new InvalidOperationException($"{Class} is not a spellcaster.");

        public IReadOnlyList<int> SlotsRemaining => _slotsRemaining;

        public bool HasSlot(int slotLevel) =>
            slotLevel >= 1 && slotLevel <= _slotsRemaining.Length && _slotsRemaining[slotLevel - 1] > 0;

        public void ConsumeSlot(int slotLevel)
        {
            if (!HasSlot(slotLevel))
                throw new RuleViolationException($"No level-{slotLevel} spell slot remaining.");
            _slotsRemaining[slotLevel - 1]--;
        }

        public void RestoreAllSlots() => _slotsRemaining = ClassData.SpellSlots(Class, Level);

        public void GainXp(int amount)
        {
            if (amount < 0) throw new ArgumentException("xp must be >= 0");
            Xp += amount;
        }

        public bool CanLevelUp => Level < 5 && ClassData.LevelForXp(Xp) > Level;

        /// <summary>Applies one level. Returns a summary of what changed (for the level-up UI).</summary>
        public LevelUpResult LevelUp()
        {
            if (!CanLevelUp)
                throw new RuleViolationException("Not enough XP to level up (or at cap).");
            int oldMax = MaxHp;
            Level++;
            ProficiencyBonus = ClassData.ProficiencyBonus(Level);
            MaxHp = ComputeMaxHp();
            Heal(MaxHp - oldMax); // level-up HP arrives as current HP too
            var oldSlots = _slotsRemaining;
            var newSlots = ClassData.SpellSlots(Class, Level);
            // Preserve spent slots: add only the delta of the new table.
            var prevTable = ClassData.SpellSlots(Class, Level - 1);
            for (int i = 0; i < newSlots.Length; i++)
                newSlots[i] = Math.Max(0, newSlots[i] - (prevTable[i] - Math.Min(oldSlots[i], prevTable[i])));
            _slotsRemaining = newSlots;

            return new LevelUpResult(Level, MaxHp - oldMax, ClassData.FeaturesAt(Class, Level).ToArray());
        }
    }

    public readonly struct LevelUpResult
    {
        public int NewLevel { get; }
        public int HpGained { get; }
        public IReadOnlyList<string> NewFeatures { get; }

        public LevelUpResult(int newLevel, int hpGained, IReadOnlyList<string> newFeatures)
        {
            NewLevel = newLevel;
            HpGained = hpGained;
            NewFeatures = newFeatures;
        }
    }

    /// <summary>Thrown when an intent violates the rules; the server catches this and
    /// returns a user-visible rejection — never silent.</summary>
    public sealed class RuleViolationException : Exception
    {
        public RuleViolationException(string message) : base(message) { }
    }
}
