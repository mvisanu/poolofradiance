using System.Linq;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    public class CombatTests
    {
        private static Creature Dummy(int ac = 12, int hp = 10, bool pc = false) =>
            new Creature("t1", "Dummy", new AbilityScores(10, 10, 10, 10, 10, 10), ac, hp)
            { IsPlayerCharacter = pc };

        private static readonly AttackDefinition Sword =
            new AttackDefinition("Sword", 5, "1d8+3", DamageType.Slashing, 5);

        [Fact]
        public void Attack_HitsWhenTotalMeetsAc()
        {
            var target = Dummy(ac: 12, hp: 20);
            // d20=7 +5 = 12 vs AC12 → hit; damage 1d8=4 +3 = 7
            var r = CombatMath.ResolveAttack(Dummy(), target, Sword, new FixedRng(7, 4));
            Assert.True(r.Hit);
            Assert.False(r.Critical);
            Assert.Equal(7, r.DamageDealt);
            Assert.Equal(13, target.CurrentHp);
        }

        [Fact]
        public void Attack_MissesBelowAc_AndNat1AlwaysMisses()
        {
            var target = Dummy(ac: 12, hp: 20);
            Assert.False(CombatMath.ResolveAttack(Dummy(), target, Sword, new FixedRng(6)).Hit);
            // Nat 1 with +5 = 6... make AC 2 so total would hit numerically
            var easy = Dummy(ac: 2, hp: 20);
            Assert.False(CombatMath.ResolveAttack(Dummy(), easy, Sword, new FixedRng(1)).Hit);
        }

        [Fact]
        public void Attack_Nat20_AlwaysHitsAndDoublesDice()
        {
            var target = Dummy(ac: 30, hp: 30);
            // nat 20, damage die 8, crit die 6 → 8+6+3 = 17
            var r = CombatMath.ResolveAttack(Dummy(), target, Sword, new FixedRng(20, 8, 6));
            Assert.True(r.Hit);
            Assert.True(r.Critical);
            Assert.Equal(17, r.DamageDealt);
        }

        [Fact]
        public void Attack_DodgingImposesDisadvantage()
        {
            var target = Dummy(ac: 12, hp: 20);
            target.Conditions.Add(ConditionType.Dodging);
            // disadvantage: rolls 15, 6 → takes 6; 6+5=11 < 12 → miss
            var r = CombatMath.ResolveAttack(Dummy(), target, Sword, new FixedRng(15, 6));
            Assert.False(r.Hit);
        }

        [Fact]
        public void Attack_GuidedGivesAdvantage_AndIsConsumed()
        {
            var target = Dummy(ac: 18, hp: 20);
            target.Conditions.Add(ConditionType.Guided);
            // advantage: rolls 4, 14 → 14 +5 = 19 ≥ 18 hit; damage 5+3
            var r = CombatMath.ResolveAttack(Dummy(), target, Sword, new FixedRng(4, 14, 5));
            Assert.True(r.Hit);
            Assert.False(target.Conditions.Has(ConditionType.Guided));
        }

        [Fact]
        public void Damage_TempHpAbsorbsFirst()
        {
            var c = Dummy(hp: 10);
            c.GrantTempHp(5);
            var o = c.TakeDamage(7, DamageType.Fire);
            Assert.Equal(7, o.DamageDealt);
            Assert.Equal(0, c.TempHp);
            Assert.Equal(8, c.CurrentHp);
        }

        [Fact]
        public void Damage_ResistanceHalves_ImmunityNegates()
        {
            var c = Dummy(hp: 20);
            c.Resistances.Add(DamageType.Fire);
            c.TakeDamage(9, DamageType.Fire);
            Assert.Equal(16, c.CurrentHp);  // 9/2 = 4
            c.Immunities.Add(DamageType.Poison);
            c.TakeDamage(100, DamageType.Poison);
            Assert.Equal(16, c.CurrentHp);
        }

        [Fact]
        public void Monster_DiesAtZeroHp()
        {
            var m = Dummy(hp: 5, pc: false);
            var o = m.TakeDamage(5, DamageType.Slashing);
            Assert.True(o.Died);
            Assert.True(m.IsDead);
        }

        [Fact]
        public void Pc_GoesDownAtZero_NotDead()
        {
            var pc = Dummy(hp: 5, pc: true);
            var o = pc.TakeDamage(5, DamageType.Slashing);
            Assert.False(o.Died);
            Assert.True(o.BecameDown);
            Assert.True(pc.IsDown);
            Assert.True(pc.Conditions.Has(ConditionType.Unconscious));
        }

        [Fact]
        public void Pc_MassiveDamage_InstantDeath()
        {
            var pc = Dummy(hp: 10, pc: true);
            var o = pc.TakeDamage(25, DamageType.Fire); // excess 15 >= max 10
            Assert.True(o.Died);
        }

        [Fact]
        public void DeathSaves_ThreeFailuresKill_ThreeSuccessesStabilize()
        {
            var pc = Dummy(hp: 5, pc: true);
            pc.TakeDamage(5, DamageType.Slashing);

            // Two failed saves (roll < 10), one success, then damage-while-down twice.
            CombatMath.RollDeathSave(pc, new FixedRng(3));
            CombatMath.RollDeathSave(pc, new FixedRng(12));
            CombatMath.RollDeathSave(pc, new FixedRng(4));
            Assert.Equal(2, pc.DeathSaveFailures);
            Assert.Equal(1, pc.DeathSaveSuccesses);

            pc.TakeDamage(3, DamageType.Slashing);   // third failure
            Assert.True(pc.IsDead);

            var pc2 = Dummy(hp: 5, pc: true);
            pc2.TakeDamage(5, DamageType.Slashing);
            CombatMath.RollDeathSave(pc2, new FixedRng(10));
            CombatMath.RollDeathSave(pc2, new FixedRng(15));
            CombatMath.RollDeathSave(pc2, new FixedRng(19));
            Assert.True(pc2.IsStable);
            Assert.False(pc2.IsDead);
        }

        [Fact]
        public void DeathSave_Nat20RestoresOneHp_Nat1CountsTwice()
        {
            var pc = Dummy(hp: 5, pc: true);
            pc.TakeDamage(5, DamageType.Slashing);
            CombatMath.RollDeathSave(pc, new FixedRng(20));
            Assert.Equal(1, pc.CurrentHp);
            Assert.False(pc.IsDown);

            var pc2 = Dummy(hp: 5, pc: true);
            pc2.TakeDamage(5, DamageType.Slashing);
            CombatMath.RollDeathSave(pc2, new FixedRng(1));
            Assert.Equal(2, pc2.DeathSaveFailures);
        }

        [Fact]
        public void Healing_RevivesDownedPc_AndClearsSaves()
        {
            var pc = Dummy(hp: 10, pc: true);
            pc.TakeDamage(10, DamageType.Slashing);
            CombatMath.RollDeathSave(pc, new FixedRng(3));
            int healed = pc.Heal(4);
            Assert.Equal(4, healed);
            Assert.False(pc.IsDown);
            Assert.False(pc.Conditions.Has(ConditionType.Unconscious));
            Assert.Equal(0, pc.DeathSaveFailures);
        }

        [Fact]
        public void Save_UsesProficiencyWhenProficient()
        {
            var c = Dummy();
            c.SaveProficiencies.Add(Ability.Dex);
            c.ProficiencyBonus = 2;
            // d20=10 +0 dex +2 prof = 12 vs DC 12 → success
            var r = CombatMath.ResolveSave(c, Ability.Dex, 12, new FixedRng(10));
            Assert.True(r.Success);
            // Not proficient in Con: d20=10 vs DC 12 → fail
            var r2 = CombatMath.ResolveSave(c, Ability.Con, 12, new FixedRng(10));
            Assert.False(r2.Success);
        }

        [Fact]
        public void Bless_AddsD4ToAttacksAndSaves()
        {
            var attacker = Dummy();
            attacker.Conditions.Add(ConditionType.Blessed, 10);
            var target = Dummy(ac: 15, hp: 20);
            // d20=9 +5 +d4(3) = 17 ≥ 15 → hit (would miss without bless); dmg die 1
            var r = CombatMath.ResolveAttack(attacker, target, Sword, new FixedRng(9, 3, 1));
            Assert.True(r.Hit);

            var saver = Dummy();
            saver.Conditions.Add(ConditionType.Blessed, 10);
            // d20=9 +0 +d4(2) = 11 vs DC 11 → success
            var s = CombatMath.ResolveSave(saver, Ability.Wis, 11, new FixedRng(9, 2));
            Assert.True(s.Success);
        }

        [Fact]
        public void AdvantageCombination_Cancels()
        {
            Assert.Equal(Advantage.None,
                CombatMath.Combine(Advantage.Advantage, Advantage.Disadvantage));
            Assert.Equal(Advantage.Advantage,
                CombatMath.Combine(Advantage.None, Advantage.Advantage));
        }
    }
}
