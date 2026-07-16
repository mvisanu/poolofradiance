using System.Linq;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    /// <summary>The substitution pools are only balance-neutral if every alternative in a
    /// family shares the exact XP of the monster it replaces, and every pooled id is a real
    /// monster. These tests fail the build if a future edit breaks either invariant.</summary>
    public class MonsterVarietyTests
    {
        [Fact]
        public void EveryPooledId_IsARealMonster()
        {
            foreach (var id in MonsterVariety.AllPooledIds)
                Assert.True(MonsterLibrary.All.ContainsKey(id), $"pooled id '{id}' is not a monster");
        }

        [Fact]
        public void EveryFamily_SharesOneXpValue()
        {
            foreach (var id in MonsterVariety.AllPooledIds)
            {
                var family = MonsterVariety.FamilyOf(id);
                var xps = family.Select(m => MonsterLibrary.Get(m).Xp).Distinct().ToList();
                Assert.True(xps.Count == 1,
                    $"family of '{id}' mixes XP values: {string.Join(",", xps)}");
            }
        }

        [Fact]
        public void Pick_StaysWithinFamily_AndBossesNeverSubstitute()
        {
            var rng = new SeededRng(12345);
            // A named boss is outside every family: it returns unchanged.
            Assert.Equal("orc_warchief", MonsterVariety.Pick("orc_warchief", rng));
            Assert.Equal("hollow_warden", MonsterVariety.Pick("hollow_warden", rng));

            // A pooled mook always resolves to a same-XP family member.
            for (int i = 0; i < 50; i++)
            {
                string picked = MonsterVariety.Pick("dock_rat", rng);
                Assert.Contains(picked, MonsterVariety.FamilyOf("dock_rat"));
                Assert.Equal(MonsterLibrary.Get("dock_rat").Xp, MonsterLibrary.Get(picked).Xp);
            }
        }
    }
}
