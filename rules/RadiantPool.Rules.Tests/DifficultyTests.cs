using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    /// <summary>Pins the monster-side difficulty easing (Difficulty.cs): monsters spawn
    /// with scaled-down HP and attack with a flat to-hit penalty, while PC attacks stay
    /// pure SRD. If these knobs are retuned, update the expectations here.</summary>
    public class DifficultyTests
    {
        private static Creature Target(int ac) =>
            new Creature("t", "Target", new AbilityScores(10, 10, 10, 10, 10, 10), ac, 20)
            { IsPlayerCharacter = true };

        [Fact]
        public void MonsterSpawn_HpScaledDown_NeverBelowOne()
        {
            var def = MonsterLibrary.Get("marsh_skulker");   // 2d8+2
            // Max roll: 8+8+2 = 18 → eased to round(18 * scale).
            var strong = def.Spawn("m1", new FixedRng(8, 8));
            Assert.Equal(Difficulty.EaseMonsterHp(18), strong.MaxHp);
            Assert.True(strong.MaxHp < 18);

            Assert.Equal(1, Difficulty.EaseMonsterHp(1));   // floor at 1 HP
        }

        [Fact]
        public void MonsterAttack_TakesToHitPenalty_PcDoesNot()
        {
            var attack = new AttackDefinition("Claw", 5, "1d6", DamageType.Slashing, 5);
            var monster = new Creature("m", "Monster",
                new AbilityScores(10, 10, 10, 10, 10, 10), 12, 10);
            Assert.False(monster.IsPlayerCharacter);

            // d20=7 +5 = 12 vs AC 12: a PC hits exactly...
            var pcHit = CombatMath.ResolveAttack(Target(ac: 2), Target(ac: 12), attack,
                new FixedRng(7, 4));
            Assert.True(pcHit.Hit);

            // ...but a monster's 12 is eased below AC and misses.
            var monsterMiss = CombatMath.ResolveAttack(monster, Target(ac: 12), attack,
                new FixedRng(7, 4));
            Assert.False(monsterMiss.Hit);
            Assert.Equal(12 - Difficulty.MonsterToHitPenalty, monsterMiss.Total);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        [InlineData(4, 3)]
        [InlineData(5, 4)]
        [InlineData(99, 4)]
        public void QuestMonsterLevel_IsOneBelowHero_WithLevelOneFloor(int hero, int monster)
        {
            Assert.Equal(monster, Difficulty.TargetMonsterLevel(hero));
        }

        [Fact]
        public void HigherEncounterLevel_ScalesRuntimeStats_NotCanonicalDefinition()
        {
            var def = MonsterLibrary.Get("marsh_skulker");
            int canonicalAc = def.ArmorClass;
            var scaled = def.Spawn("scaled", new FixedRng(8, 8), encounterLevel: 4);

            Assert.Equal(4, scaled.EncounterLevel);
            Assert.Equal(Difficulty.ScaleMonsterHp(18, 4), scaled.MaxHp);
            Assert.Equal(canonicalAc + Difficulty.MonsterArmorBonus(4), scaled.BaseArmorClass);
            Assert.Equal(canonicalAc, def.ArmorClass); // source stat block stayed canonical
        }

        [Fact]
        public void HigherEncounterLevel_AddsAttackPressureAndDamage()
        {
            var monster = new Creature("m", "Scaled monster",
                new AbilityScores(10, 10, 10, 10, 10, 10), 12, 10)
            { EncounterLevel = 4 };
            var attack = new AttackDefinition("Claw", 5, "1d6", DamageType.Slashing);
            var result = CombatMath.ResolveAttack(monster, Target(ac: 2), attack,
                new FixedRng(10, 4));

            Assert.Equal(10 + 5 - Difficulty.MonsterToHitPenalty
                         + Difficulty.MonsterToHitBonus(4), result.Total);
            Assert.Equal(4 + Difficulty.MonsterDamageBonus(4), result.DamageDealt);
        }
    }
}
