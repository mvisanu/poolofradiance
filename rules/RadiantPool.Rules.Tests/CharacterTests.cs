using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    public class CharacterTests
    {
        private static CharacterSheet Fighter(int level = 1) =>
            new CharacterSheet("pc1", "Brannic", Race.Human, CharacterClass.Fighter,
                new AbilityScores(15, 13, 14, 8, 12, 10), level);

        private static CharacterSheet Wizard(int level = 1) =>
            new CharacterSheet("pc2", "Selune", Race.Elf, CharacterClass.Wizard,
                new AbilityScores(8, 14, 13, 15, 12, 10), level);

        [Fact]
        public void RacialBonuses_Apply()
        {
            var f = Fighter();      // Human: +1 all → Str 16
            Assert.Equal(16, f.Abilities[Ability.Str]);
            var w = Wizard();       // High elf: +2 Dex, +1 Int → Dex 16, Int 16
            Assert.Equal(16, w.Abilities[Ability.Dex]);
            Assert.Equal(16, w.Abilities[Ability.Int]);
        }

        [Fact]
        public void Level1Hp_IsMaxDiePlusConMod()
        {
            var f = Fighter();      // d10 + Con 15→+2 = 12
            Assert.Equal(12, f.MaxHp);
            var w = Wizard();       // d6 + Con 13→+1 = 7
            Assert.Equal(7, w.MaxHp);
        }

        [Fact]
        public void HillDwarf_GetsBonusHpPerLevel()
        {
            var d = new CharacterSheet("pc3", "Korga", Race.Dwarf, CharacterClass.Cleric,
                new AbilityScores(14, 8, 14, 10, 15, 12), 2);
            // Con 14+2 racial = 16 → +3; d8: L1 = 8+3+1, L2 = 5+3+1 → 21
            Assert.Equal(21, d.MaxHp);
        }

        [Fact]
        public void Ac_ArmorDexCapAndShield()
        {
            var f = Fighter();                        // Dex 14 → +2
            Assert.Equal(12, f.ArmorClass);           // unarmored 10+2
            f.EquipArmor(ArmorDefinition.ScaleMail);  // 14 + min(2, cap 2)
            Assert.Equal(16, f.ArmorClass);
            f.SetShield(true);
            Assert.Equal(18, f.ArmorClass);
            f.EquipArmor(ArmorDefinition.ChainMail);  // 16 + 0 dex + 2 shield
            Assert.Equal(18, f.ArmorClass);
        }

        [Fact]
        public void SaveProficiencies_MatchClass()
        {
            var f = Fighter();  // Str/Con proficient, prof +2; Str 16 → +3+2
            Assert.Equal(5, f.SaveBonus(Ability.Str));
            Assert.Equal(1, f.SaveBonus(Ability.Wis)); // Wis 13 → +1, not proficient
        }

        [Fact]
        public void MagicShield_AddsItsPlusOnTopOfTheBaseTwo()
        {
            var f = Fighter();                        // Dex 14 → +2, unarmored 12
            f.EquipArmor(ArmorDefinition.ScaleMail);  // 14 + 2 = 16
            f.SetShield(true, 4);                     // +2 base +2 magic
            Assert.Equal(20, f.ArmorClass);
            f.SetShield(false);                       // off hand emptied
            Assert.Equal(16, f.ArmorClass);
        }

        [Fact]
        public void MagicDefense_AddsToAcAndSaves_AndClearsToZero()
        {
            var f = Fighter();                        // AC 12, Str save +5
            f.SetMagicDefense(2, 1);                  // ring of protection style
            Assert.Equal(14, f.ArmorClass);
            Assert.Equal(6, f.SaveBonus(Ability.Str));
            Assert.Equal(2, f.SaveBonus(Ability.Wis)); // was +1, +1 ring
            f.SetMagicDefense(0, 0);
            Assert.Equal(12, f.ArmorClass);
            Assert.Equal(5, f.SaveBonus(Ability.Str));
        }

        [Fact]
        public void CasterRobe_GivesFlatBasePlusFullDex()
        {
            var w = Wizard();                          // Dex 16 → +3
            Assert.Equal(13, w.ArmorClass);            // unarmored 10 + 3
            w.EquipArmor(ArmorDefinition.ApprenticeRobe);
            Assert.Equal(15, w.ArmorClass);            // 12 + 3 (uncapped Dex)
            w.EquipArmor(ArmorDefinition.ArchmageRobe);
            Assert.Equal(18, w.ArmorClass);            // 15 + 3
        }

        [Fact]
        public void AcSources_Stack_Armor_Shield_AndMagic()
        {
            var f = Fighter();                          // Dex +2
            f.EquipArmor(ArmorDefinition.HalfPlate);    // 15 + 2
            f.SetShield(true, 3);                       // +3 magic shield
            f.SetMagicDefense(2, 0);                    // +2 ring
            Assert.Equal(22, f.ArmorClass);             // 15 + 2 + 3 + 2
        }

        [Fact]
        public void SpellSlots_FollowFullCasterTable()
        {
            var w = Wizard(1);
            Assert.Equal(new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 }, w.SlotsRemaining);
            var w5 = Wizard(5);
            Assert.Equal(new[] { 4, 3, 2, 0, 0, 0, 0, 0, 0 }, w5.SlotsRemaining);
            var w20 = Wizard(20);
            Assert.Equal(new[] { 4, 3, 3, 3, 3, 2, 2, 1, 1 }, w20.SlotsRemaining);
            var f = Fighter(20);
            Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 }, f.SlotsRemaining);
        }

        [Fact]
        public void SpellDcAndAttack_UseCastingAbility()
        {
            var w = Wizard();   // Int 16 → +3, prof +2
            Assert.Equal(13, w.SpellSaveDc);
            Assert.Equal(5, w.SpellAttackBonus);
        }

        [Fact]
        public void ConsumeSlot_EnforcesAvailability()
        {
            var w = Wizard(1);
            w.ConsumeSlot(1);
            w.ConsumeSlot(1);
            Assert.Throws<RuleViolationException>(() => w.ConsumeSlot(1));
            Assert.Throws<RuleViolationException>(() => w.ConsumeSlot(2));
        }

        [Fact]
        public void LevelUp_RequiresXpAndAppliesGains()
        {
            var f = Fighter(1);
            Assert.False(f.CanLevelUp);
            Assert.Throws<RuleViolationException>(() => f.LevelUp());

            f.GainXp(300);
            Assert.True(f.CanLevelUp);
            int before = f.MaxHp;
            var result = f.LevelUp();
            Assert.Equal(2, result.NewLevel);
            // d10 avg 6 + Con +2 = 8
            Assert.Equal(8, f.MaxHp - before);
            Assert.Contains("Action Surge", result.NewFeatures);
        }

        [Fact]
        public void ProficiencyBonus_ReachesSixAtLevel17()
        {
            var f = Fighter(1);
            f.GainXp(225000);
            while (f.CanLevelUp) f.LevelUp();
            Assert.Equal(17, f.Level);
            Assert.Equal(6, f.ProficiencyBonus);
        }

        [Fact]
        public void XpThresholds_MatchSrd()
        {
            Assert.Equal(1, ClassData.LevelForXp(299));
            Assert.Equal(2, ClassData.LevelForXp(300));
            Assert.Equal(3, ClassData.LevelForXp(900));
            Assert.Equal(4, ClassData.LevelForXp(2700));
            Assert.Equal(5, ClassData.LevelForXp(6500));
            Assert.Equal(10, ClassData.LevelForXp(64000));
            Assert.Equal(15, ClassData.LevelForXp(165000));
            Assert.Equal(20, ClassData.LevelForXp(355000));
            Assert.Equal(20, ClassData.LevelForXp(999999));
        }

        [Fact]
        public void FighterExtraAttack_ScalesAtFiveElevenAndTwenty()
        {
            Assert.Equal(1, ClassData.AttacksPerAction(CharacterClass.Fighter, 4));
            Assert.Equal(2, ClassData.AttacksPerAction(CharacterClass.Fighter, 5));
            Assert.Equal(3, ClassData.AttacksPerAction(CharacterClass.Fighter, 11));
            Assert.Equal(4, ClassData.AttacksPerAction(CharacterClass.Fighter, 20));
            Assert.Equal(1, ClassData.AttacksPerAction(CharacterClass.Rogue, 20));
        }
    }
}
