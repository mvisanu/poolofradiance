using System;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    public class DiceTests
    {
        [Theory]
        [InlineData("2d6+3", 2, 6, 3)]
        [InlineData("1d8-1", 1, 8, -1)]
        [InlineData("d20", 1, 20, 0)]
        [InlineData("3d6", 3, 6, 0)]
        [InlineData("5", 0, 0, 5)]
        [InlineData("1d4 + 1", 1, 4, 1)]
        public void Parse_ValidExpressions(string text, int count, int sides, int mod)
        {
            var e = DiceExpression.Parse(text);
            Assert.Equal(count, e.Count);
            if (count > 0) Assert.Equal(sides, e.Sides);
            Assert.Equal(mod, e.Modifier);
        }

        [Theory]
        [InlineData("")]
        [InlineData("d")]
        [InlineData("2x6")]
        [InlineData("d6+")]
        public void Parse_InvalidExpressions_Throw(string text)
        {
            Assert.Throws<FormatException>(() => DiceExpression.Parse(text));
        }

        [Fact]
        public void Roll_UsesRngAndModifier()
        {
            var result = Dice.Roll("2d6+3", new FixedRng(4, 5));
            Assert.Equal(12, result.Total);
            Assert.Equal(new[] { 4, 5 }, result.Dice);
            Assert.Equal(3, result.Modifier);
        }

        [Fact]
        public void Roll_SeededIsDeterministic()
        {
            var a = Dice.Roll("4d8+2", new SeededRng(42));
            var b = Dice.Roll("4d8+2", new SeededRng(42));
            Assert.Equal(a.Total, b.Total);
        }

        [Fact]
        public void Roll_StaysInBounds()
        {
            var rng = new SeededRng(7);
            for (int i = 0; i < 1000; i++)
            {
                int t = Dice.Roll("2d6", rng).Total;
                Assert.InRange(t, 2, 12);
            }
        }

        [Fact]
        public void Average_MatchesSrdConvention()
        {
            Assert.Equal(4, DiceExpression.Parse("1d8").Average);   // 4.5 floored
            Assert.Equal(11, DiceExpression.Parse("2d8+2").Average); // 9 + 2
        }

        [Fact]
        public void D20_AdvantageTakesHigher_DisadvantageTakesLower()
        {
            Assert.Equal(15, Dice.RollD20(new FixedRng(3, 15), Advantage.Advantage).Value);
            Assert.Equal(3, Dice.RollD20(new FixedRng(3, 15), Advantage.Disadvantage).Value);
            Assert.Equal(3, Dice.RollD20(new FixedRng(3), Advantage.None).Value);
        }
    }

    public class AbilityTests
    {
        [Theory]
        [InlineData(1, -5)]
        [InlineData(8, -1)]
        [InlineData(10, 0)]
        [InlineData(11, 0)]
        [InlineData(15, 2)]
        [InlineData(20, 5)]
        public void Modifier_MatchesSrdTable(int score, int expected)
        {
            var s = new AbilityScores(score, 10, 10, 10, 10, 10);
            Assert.Equal(expected, s.Modifier(Ability.Str));
        }
    }
}
