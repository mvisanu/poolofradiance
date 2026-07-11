using System.Linq;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    public class LootTests
    {
        [Fact]
        public void Roll_GoldAndWeightedItem()
        {
            var table = LootLibrary.Get("lt_skulker");
            // gold 2d6 = 3+4 = 7; pick roll 1..100 → 75 lands in dagger band (71-95)
            var result = table.Roll(new FixedRng(3, 4, 75));
            Assert.Equal(7, result.Gold);
            Assert.Equal(new[] { "dagger" }, result.ItemIds);
        }

        [Fact]
        public void Roll_NullEntryYieldsNoItem()
        {
            var table = LootLibrary.Get("lt_skulker");
            var result = table.Roll(new FixedRng(1, 1, 30)); // 30 ≤ 70 → empty entry
            Assert.Empty(result.ItemIds);
        }

        [Fact]
        public void Roll_MultiRollTablesGiveMultipleItems()
        {
            var table = LootLibrary.Get("lt_warehouse_cache"); // 2 rolls, no null entries
            var result = table.Roll(new FixedRng(6, 6, 6, 6, 6, 6, 10, 95));
            Assert.Equal(36, result.Gold);
            Assert.Equal(2, result.ItemIds.Count);
        }

        [Fact]
        public void AllMonsterTables_ExistAndRollSafely()
        {
            var rng = new SeededRng(99);
            foreach (var monster in MonsterLibrary.All.Values)
            {
                var table = LootLibrary.Get(monster.LootTable);
                for (int i = 0; i < 50; i++)
                {
                    var r = table.Roll(rng);
                    Assert.True(r.Gold >= 0);
                }
            }
        }
    }

    public class RestTests
    {
        private static CharacterSheet Fighter(int level = 3) =>
            new CharacterSheet("f", "Bran", Race.Human, CharacterClass.Fighter,
                new AbilityScores(15, 13, 14, 8, 12, 10), level);

        [Fact]
        public void ShortRest_HealsPerDiePlusCon_CappedByDiceAllowance()
        {
            var f = Fighter(3);                       // max 2 dice at level 3
            f.TakeDamage(20, DamageType.Slashing);
            // asks for 5 dice → clamped to 2: (6+2)+(4+2) = 14
            int healed = Rest.ShortRest(f, 5, new FixedRng(6, 4));
            Assert.Equal(14, healed);
        }

        [Fact]
        public void LongRest_RestoresHpSlotsAndConditions()
        {
            var w = new CharacterSheet("w", "Selra", Race.Elf, CharacterClass.Wizard,
                new AbilityScores(8, 14, 13, 15, 12, 10), 3);
            w.ConsumeSlot(1);
            w.ConsumeSlot(2);
            w.TakeDamage(5, DamageType.Fire);
            w.Conditions.Add(ConditionType.Poisoned);

            Rest.LongRest(w);
            Assert.Equal(w.MaxHp, w.CurrentHp);
            Assert.Equal(ClassData.SpellSlots(CharacterClass.Wizard, 3), w.SlotsRemaining);
            Assert.False(w.Conditions.Has(ConditionType.Poisoned));
        }

        [Fact]
        public void LongRest_DoesNotRaiseTheDead()
        {
            var f = Fighter(1);
            f.TakeDamage(100, DamageType.Fire); // massive damage → dead
            Assert.True(f.IsDead);
            Rest.LongRest(f);
            Assert.True(f.IsDead);
        }
    }

    public class PointBuyTests
    {
        [Fact]
        public void StandardArray_IsAffordable()
        {
            // 15,14,13,12,10,8 = 9+7+5+4+2+0 = 27 exactly.
            Assert.Equal(27, PointBuy.TotalCost(15, 14, 13, 12, 10, 8));
            Assert.True(PointBuy.IsValid(15, 14, 13, 12, 10, 8, out _));
        }

        [Fact]
        public void OverBudget_Rejected()
        {
            Assert.False(PointBuy.IsValid(15, 15, 15, 9, 8, 8, out string error));
            Assert.Contains("budget", error);
        }

        [Fact]
        public void OutOfRangeScore_Rejected()
        {
            Assert.False(PointBuy.IsValid(16, 8, 8, 8, 8, 8, out string error));
            Assert.Contains("8-15", error);
        }
    }
}
