using System.Linq;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    /// <summary>Pins the hiring rule (PartyComposition.cs): help mustered from a quest
    /// always brings a healer and two damage dealers of different classes.</summary>
    public class PartyCompositionTests
    {
        private static int DistinctDamage(params CharacterClass[] party) =>
            PartyComposition.DamageDealers.Count(party.Contains);

        [Theory]
        [InlineData(CharacterClass.Fighter)]
        [InlineData(CharacterClass.Wizard)]
        [InlineData(CharacterClass.Cleric)]
        [InlineData(CharacterClass.Rogue)]
        public void SoloPlayer_PartyEndsWithHealerAndTwoDifferentDamageDealers(
            CharacterClass played)
        {
            var hires = PartyComposition.Recruits(new[] { played }, slots: 3);
            var party = new[] { played }.Concat(hires).ToArray();

            Assert.Equal(3, hires.Count);
            Assert.Equal(PartyComposition.MaxPartySize, party.Length);
            Assert.Contains(PartyComposition.Healer, party);
            Assert.True(DistinctDamage(party) >= 2, string.Join(", ", party));
        }

        [Fact]
        public void NonHealerSolo_HiresAreExactlyOneHealerAndTwoDamageDealers()
        {
            var hires = PartyComposition.Recruits(new[] { CharacterClass.Fighter }, slots: 3);

            Assert.Single(hires.Where(c => c == PartyComposition.Healer));
            var damage = hires.Where(c => c != PartyComposition.Healer).ToList();
            Assert.Equal(2, damage.Count);
            Assert.Equal(2, damage.Distinct().Count());   // different classes
        }

        [Fact]
        public void HealerComesFirst_EvenWhenOnlyOneSlotIsLeft()
        {
            // Three fighters hiring one sellsword used to get a wizard (enum order);
            // the party has damage to spare and nobody to heal it.
            var party = new[] { CharacterClass.Fighter, CharacterClass.Fighter,
                                CharacterClass.Fighter };
            Assert.Equal(new[] { PartyComposition.Healer },
                         PartyComposition.Recruits(party, slots: 1));
        }

        [Fact]
        public void PartyWithHealer_HiresDamage_NotASecondHealer()
        {
            var hires = PartyComposition.Recruits(
                new[] { CharacterClass.Cleric, CharacterClass.Fighter }, slots: 2);

            Assert.DoesNotContain(PartyComposition.Healer, hires);
            Assert.Equal(2, hires.Distinct().Count());
        }

        [Fact]
        public void FullParty_HiresNobody()
        {
            Assert.Empty(PartyComposition.Recruits(new[] { CharacterClass.Fighter }, slots: 0));
        }

        [Fact]
        public void EmptyParty_FillsAllFourRoles()
        {
            var hires = PartyComposition.Recruits(new CharacterClass[0],
                                                  PartyComposition.MaxPartySize);
            Assert.Equal(PartyComposition.MaxPartySize, hires.Count);
            Assert.Equal(PartyComposition.Healer, hires[0]);
            Assert.Equal(PartyComposition.MaxPartySize, hires.Distinct().Count());
        }
    }
}
