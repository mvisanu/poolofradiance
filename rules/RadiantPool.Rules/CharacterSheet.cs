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
        public static readonly ArmorDefinition StuddedLeather =
            new ArmorDefinition("studded_leather", "Studded Leather", ArmorKind.Light, 12, int.MaxValue);
        public static readonly ArmorDefinition ScaleMail =
            new ArmorDefinition("scale_mail", "Scale Mail", ArmorKind.Medium, 14, 2);
        public static readonly ArmorDefinition HalfPlate =
            new ArmorDefinition("half_plate", "Half Plate", ArmorKind.Medium, 15, 2);
        public static readonly ArmorDefinition ChainMail =
            new ArmorDefinition("chain_mail", "Chain Mail", ArmorKind.Heavy, 16, 0);
        public static readonly ArmorDefinition Splint =
            new ArmorDefinition("splint", "Splint", ArmorKind.Heavy, 17, 0);

        // Caster cloth. Wizards have no armour proficiency, so their protection comes from
        // enchanted robes that behave like unarmoured defence: a flat base plus the FULL Dex
        // bonus (Kind.None keeps MaxDexBonus uncapped). These scale the mage across 1-20 the
        // way plate scales the fighter. The archmage robe mirrors the SRD's AC-15 robe.
        public static readonly ArmorDefinition ApprenticeRobe =
            new ArmorDefinition("apprentice_robe", "Apprentice Robe", ArmorKind.None, 12, int.MaxValue);
        public static readonly ArmorDefinition WardedRobe =
            new ArmorDefinition("warded_robe", "Warded Robe", ArmorKind.None, 13, int.MaxValue);
        public static readonly ArmorDefinition ArchmageRobe =
            new ArmorDefinition("archmage_robe", "Archmage Robe", ArmorKind.None, 15, int.MaxValue);
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

        /// <summary>AC the equipped shield contributes (SRD 5.1 base +2; a magical shield adds
        /// its plus on top). Zero when nothing is in the off hand.</summary>
        public int ShieldAcBonus { get; private set; }

        /// <summary>AC from worn magic that is NOT the armour or the shield — enchanted metal
        /// armour's plus, a ring of protection, a warding charm — summed by the caller into one
        /// number. Rings/charms also feed <see cref="Creature.MagicSaveBonus"/> for saves.</summary>
        public int MagicArmorBonus { get; private set; }
        public List<string> KnownSpells { get; } = new List<string>();

        // Remaining spell slots by slot level (index 0 = 1st-level slots).
        private int[] _slotsRemaining;

        public CharacterSheet(string id, string name, Race race, CharacterClass cls,
                              AbilityScores baseScores, int level = 1)
            : base(id, name, ApplyRace(baseScores, race), baseAc: 10, maxHp: 1)
        {
            if (level < 1 || level > Progression.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
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

        /// <summary>Plain shield: SRD +2. Overload below carries a magical shield's plus.</summary>
        public void SetShield(bool equipped) => SetShield(equipped, 2);

        public void SetShield(bool equipped, int totalAcBonus)
        {
            HasShield = equipped;
            ShieldAcBonus = equipped ? Math.Max(0, totalAcBonus) : 0;
            RecomputeAc();
        }

        /// <summary>Sets the aggregate magic AC/save bonus from rings, charms, and enchanted
        /// armour. The caller sums every worn source; this is the one number AC and saves read,
        /// so no bonus is applied twice.</summary>
        public void SetMagicDefense(int acBonus, int saveBonus)
        {
            MagicArmorBonus = acBonus;
            MagicSaveBonus = saveBonus;
            RecomputeAc();
        }

        private void RecomputeAc()
        {
            int dex = Math.Min(Abilities.Modifier(Ability.Dex), Armor.MaxDexBonus);
            BaseArmorClass = Armor.BaseAc + dex + ShieldAcBonus + MagicArmorBonus;
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

        public bool CanLevelUp => Level < Progression.MaxLevel && ClassData.LevelForXp(Xp) > Level;

        /// <summary>Ability points earned by levelling and not yet spent (Progression's house
        /// rule: one per level, two at 4th). The level-up UI turns these into +1s.</summary>
        public int PendingAbilityPoints { get; private set; }

        private readonly int[] _abilityIncreases = new int[6];

        /// <summary>Where this character's spent points went, by ability — the save replays
        /// exactly this, so a reloaded character is the one you built, not the one you rolled.</summary>
        public IReadOnlyList<int> AbilityIncreases => _abilityIncreases;

        public bool CanSpendPointOn(Ability a) =>
            PendingAbilityPoints > 0 && Abilities[a] < Progression.MaxAbilityScore;

        /// <summary>Spends one earned point: +1 to the ability, capped at the SRD's 20. Con
        /// raises max HP retroactively across every level (and hands you the new HP), Dex may
        /// raise AC — derived stats are recomputed, never patched.</summary>
        public void SpendAbilityPoint(Ability a)
        {
            if (PendingAbilityPoints <= 0)
                throw new RuleViolationException("No ability points to spend.");
            if (Abilities[a] >= Progression.MaxAbilityScore)
                throw new RuleViolationException(
                    $"{a} is already at the maximum of {Progression.MaxAbilityScore}.");

            Abilities = Abilities.WithIncrease(a, 1);
            PendingAbilityPoints--;
            _abilityIncreases[(int)a]++;

            int oldMax = MaxHp;
            MaxHp = ComputeMaxHp();
            if (MaxHp > oldMax) Heal(MaxHp - oldMax);
            RecomputeAc();
        }

        /// <summary>Applies one level. Returns a summary of what changed (for the level-up UI).</summary>
        public LevelUpResult LevelUp()
        {
            if (!CanLevelUp)
                throw new RuleViolationException("Not enough XP to level up (or at cap).");
            int oldMax = MaxHp;
            Level++;
            ProficiencyBonus = ClassData.ProficiencyBonus(Level);
            int points = Progression.AbilityPointsForLevel(Level);
            PendingAbilityPoints += points;
            MaxHp = ComputeMaxHp();
            Heal(MaxHp - oldMax); // level-up HP arrives as current HP too
            var oldSlots = _slotsRemaining;
            var newSlots = ClassData.SpellSlots(Class, Level);
            // Preserve spent slots: add only the delta of the new table.
            var prevTable = ClassData.SpellSlots(Class, Level - 1);
            for (int i = 0; i < newSlots.Length; i++)
                newSlots[i] = Math.Max(0, newSlots[i] - (prevTable[i] - Math.Min(oldSlots[i], prevTable[i])));
            _slotsRemaining = newSlots;

            return new LevelUpResult(Level, MaxHp - oldMax, points,
                ClassData.FeaturesAt(Class, Level).ToArray());
        }
    }

    public readonly struct LevelUpResult
    {
        public int NewLevel { get; }
        public int HpGained { get; }
        public int AbilityPointsGranted { get; }
        public IReadOnlyList<string> NewFeatures { get; }

        public LevelUpResult(int newLevel, int hpGained, int abilityPointsGranted,
            IReadOnlyList<string> newFeatures)
        {
            NewLevel = newLevel;
            HpGained = hpGained;
            AbilityPointsGranted = abilityPointsGranted;
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
