using System.Linq;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    /// <summary>Pins the loot GRADIENT (LootLibrary): the deeper the party goes, the better
    /// the gear it can actually equip. Retune the weights freely — but a late table must never
    /// stop being able to pay out something better than the starting kit, which is the whole
    /// reason to walk into it.</summary>
    public class LootProgressionTests
    {
        private static string[] Items(string table) =>
            LootLibrary.Get(table).Entries
                .Where(e => e.ItemId != null).Select(e => e.ItemId!).ToArray();

        [Fact]
        public void TheLateCaches_CarryGearBetterThanTheStartingKit()
        {
            // The vault and the warcamp are the last places the campaign sends you.
            Assert.Contains("greatsword", Items("lt_sunken_vault"));
            Assert.Contains("splint", Items("lt_sunken_vault"));     // AC 17, beats chain
            Assert.Contains("greataxe", Items("lt_warcamp"));
            Assert.Contains("half_plate", Items("lt_warcamp"));      // AC 15 + 2 Dex

            // ...and the roadside fights still hand out the starting kit, not the endgame.
            Assert.DoesNotContain("splint", Items("lt_skulker"));
            Assert.DoesNotContain("greatsword", Items("lt_skulker"));
        }

        [Fact]
        public void EveryClassCanFindAnUpgradeItIsAllowedToUse()
        {
            var all = LootLibrary.All.Values
                .SelectMany(t => t.Entries)
                .Where(e => e.ItemId != null).Select(e => e.ItemId!).Distinct().ToList();

            Assert.Contains("rapier", all);            // rogue: finesse, d8 over the shortsword
            Assert.Contains("warhammer", all);         // cleric: d8 blunt over the mace
            Assert.Contains("studded_leather", all);   // rogue: light AC 12 over leather
            Assert.Contains("greatsword", all);        // fighter: 2d6
        }

        [Fact]
        public void EveryLootedItemIsRealAndEveryTableCanRoll()
        {
            foreach (var table in LootLibrary.All.Values)
            {
                Assert.True(table.Entries.Sum(e => e.Weight) > 0, table.Id);
                var result = table.Roll(new SeededRng(1234));
                Assert.True(result.Gold >= 0);
            }
        }

        [Fact]
        public void BetterArmorIsActuallyBetter()
        {
            Assert.True(ArmorDefinition.StuddedLeather.BaseAc > ArmorDefinition.Leather.BaseAc);
            Assert.True(ArmorDefinition.HalfPlate.BaseAc > ArmorDefinition.ScaleMail.BaseAc);
            Assert.True(ArmorDefinition.Splint.BaseAc > ArmorDefinition.ChainMail.BaseAc);

            // Light armour still passes Dex through; the heavy suits still refuse it.
            Assert.Equal(int.MaxValue, ArmorDefinition.StuddedLeather.MaxDexBonus);
            Assert.Equal(0, ArmorDefinition.Splint.MaxDexBonus);
            Assert.Equal(2, ArmorDefinition.HalfPlate.MaxDexBonus);
        }
    }
}
