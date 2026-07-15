using System;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    /// <summary>Pins levelling and the ability-point house rule (Progression.cs): what a
    /// level-up grants, what the XP bar reads, and that a spent point really moves the
    /// derived stats it should (HP from Con, AC from Dex) and nothing it shouldn't.</summary>
    public class ProgressionTests
    {
        private static CharacterSheet Fighter() => new CharacterSheet(
            "pc", "Anna", Race.Human, CharacterClass.Fighter,
            new AbilityScores(15, 13, 14, 10, 12, 8));

        [Fact]
        public void EveryLevelUp_GrantsAPoint_AndFourthGrantsTheSrdTwo()
        {
            Assert.Equal(0, Progression.AbilityPointsForLevel(1));
            Assert.Equal(1, Progression.AbilityPointsForLevel(2));
            Assert.Equal(1, Progression.AbilityPointsForLevel(3));
            Assert.Equal(2, Progression.AbilityPointsForLevel(4));   // the SRD's ASI
            Assert.Equal(1, Progression.AbilityPointsForLevel(5));
            Assert.Equal(20, Progression.TotalAbilityPointsByLevel(Progression.MaxLevel));
        }

        [Fact]
        public void LevellingToTwenty_LeavesTwentyPointsToSpend()
        {
            var f = Fighter();
            Assert.Equal(0, f.PendingAbilityPoints);

            f.GainXp(355000);
            int granted = 0;
            while (f.CanLevelUp) granted += f.LevelUp().AbilityPointsGranted;

            Assert.Equal(20, f.Level);
            Assert.Equal(20, granted);
            Assert.Equal(20, f.PendingAbilityPoints);
        }

        [Fact]
        public void XpBar_ReadsTheFractionThroughTheCurrentLevel()
        {
            // Level 2 spans 300..900. 600 XP is halfway.
            var (into, span, fraction) = Progression.Progress(level: 2, xp: 600);
            Assert.Equal(300, into);
            Assert.Equal(600, span);
            Assert.Equal(0.5f, fraction, 3);
            Assert.Equal(300, Progression.XpToNext(level: 2, xp: 600));

            // Fresh level 1: nothing earned into it yet, 300 to go.
            Assert.Equal(0f, Progression.Progress(1, 0).fraction);
            Assert.Equal(300, Progression.XpToNext(1, 0));

            // At the cap the bar is FULL, not empty, and nothing is owed.
            Assert.Equal(1f, Progression.Progress(Progression.MaxLevel, 99999).fraction);
            Assert.Equal(0, Progression.XpToNext(Progression.MaxLevel, 99999));
        }

        [Fact]
        public void SpendingOnCon_RaisesMaxHpForEveryLevel_AndHealsTheDifference()
        {
            // Human is +1 to every score, so the sheet's Con is the rolled 14 plus one: 15 (+2).
            var f = Fighter();
            Assert.Equal(15, f.Abilities[Ability.Con]);
            f.GainXp(900);
            while (f.CanLevelUp) f.LevelUp();   // level 3, 2 points
            int hpBefore = f.MaxHp, currentBefore = f.CurrentHp;

            f.SpendAbilityPoint(Ability.Con);   // 15 -> 16: +3 now, over all three levels
            Assert.Equal(hpBefore + 3, f.MaxHp);
            Assert.Equal(currentBefore + 3, f.CurrentHp);   // the new HP arrives filled

            f.SpendAbilityPoint(Ability.Con);   // 16 -> 17: an odd score buys nothing
            Assert.Equal(hpBefore + 3, f.MaxHp);
            Assert.Equal(0, f.PendingAbilityPoints);
        }

        [Fact]
        public void SpendingOnDex_RaisesArmorClass_WithinTheArmorsDexCap()
        {
            var f = Fighter();                  // Dex 13 rolled, 14 (+2) after the human bonus
            f.GainXp(2700);
            while (f.CanLevelUp) f.LevelUp();   // level 4: 4 points
            f.EquipArmor(ArmorDefinition.Leather);   // light: Dex uncapped
            int acBefore = f.ArmorClass;

            f.SpendAbilityPoint(Ability.Dex);    // 14 -> 15: still +2, no AC
            Assert.Equal(acBefore, f.ArmorClass);
            f.SpendAbilityPoint(Ability.Dex);    // 15 -> 16: +3, and leather passes it through
            Assert.Equal(acBefore + 1, f.ArmorClass);

            // Chain mail ignores Dex entirely (cap 0), so the same points buy no AC at all.
            f.EquipArmor(ArmorDefinition.ChainMail);
            int inChain = f.ArmorClass;
            f.SpendAbilityPoint(Ability.Dex);
            f.SpendAbilityPoint(Ability.Dex);
            Assert.Equal(inChain, f.ArmorClass);
        }

        [Fact]
        public void PointsCannotBeConjured_NorPushAScorePastTwenty()
        {
            var f = Fighter();
            Assert.False(f.CanSpendPointOn(Ability.Str));
            Assert.Throws<RuleViolationException>(() => f.SpendAbilityPoint(Ability.Str));

            var strong = new CharacterSheet("pc", "Brute", Race.Human, CharacterClass.Fighter,
                new AbilityScores(19, 10, 10, 10, 10, 10));   // human racial +1 -> Str 20
            strong.GainXp(6500);
            while (strong.CanLevelUp) strong.LevelUp();
            Assert.Equal(20, strong.Abilities[Ability.Str]);

            Assert.False(strong.CanSpendPointOn(Ability.Str));            // already capped
            Assert.Throws<RuleViolationException>(() => strong.SpendAbilityPoint(Ability.Str));
            Assert.True(strong.CanSpendPointOn(Ability.Dex));             // but Dex has room
            Assert.Equal(5, strong.PendingAbilityPoints);                 // nothing was consumed
        }

        [Fact]
        public void EachClassRaisesTheAbilityItLivesBy()
        {
            Assert.Equal(Ability.Str, Progression.PrimaryAbility(CharacterClass.Fighter));
            Assert.Equal(Ability.Dex, Progression.PrimaryAbility(CharacterClass.Rogue));
            Assert.Equal(Ability.Int, Progression.PrimaryAbility(CharacterClass.Wizard));
            Assert.Equal(Ability.Wis, Progression.PrimaryAbility(CharacterClass.Cleric));
        }

        [Fact]
        public void SpentPoints_AreRecorded_SoASaveCanReplayThem()
        {
            var f = Fighter();
            f.GainXp(2700);
            while (f.CanLevelUp) f.LevelUp();   // level 4: 1 + 1 + 2 = 4 points
            Assert.Equal(4, f.PendingAbilityPoints);

            f.SpendAbilityPoint(Ability.Str);
            f.SpendAbilityPoint(Ability.Str);
            f.SpendAbilityPoint(Ability.Con);

            Assert.Equal(2, f.AbilityIncreases[(int)Ability.Str]);
            Assert.Equal(1, f.AbilityIncreases[(int)Ability.Con]);
            Assert.Equal(0, f.AbilityIncreases[(int)Ability.Cha]);
            Assert.Equal(1, f.PendingAbilityPoints);
        }
    }
}
