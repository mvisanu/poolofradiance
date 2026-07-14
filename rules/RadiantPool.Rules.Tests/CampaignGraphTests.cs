using System;
using System.Linq;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    public class CampaignGraphTests
    {
        private static CampaignNode[] Graph() => new[]
        {
            new CampaignNode("docks", startsAvailable: true),
            new CampaignNode("bastion", startsAvailable: true),
            new CampaignNode("archive", new[] { "docks" }),
            new CampaignNode("market", new[] { "docks" }),
            new CampaignNode("gate", new[] { "archive", "bastion" }),
            new CampaignNode("spire", new[] { "gate" }, isFinale: true)
        };

        [Fact]
        public void MultipleOpeningCommissions_AreEligibleTogether()
        {
            Assert.Equal(new[] { "docks", "bastion" },
                CampaignGraph.Eligible(Graph(), Array.Empty<string>()));
        }

        [Fact]
        public void OneCompletion_CanUnlockParallelFollowups()
        {
            Assert.Equal(new[] { "bastion", "archive", "market" },
                CampaignGraph.Eligible(Graph(), new[] { "docks" }));
        }

        [Fact]
        public void MultiPrerequisiteCommission_WaitsForEveryDependency()
        {
            Assert.DoesNotContain("gate",
                CampaignGraph.Eligible(Graph(), new[] { "archive" }));
            Assert.Contains("gate",
                CampaignGraph.Eligible(Graph(), new[] { "docks", "bastion", "archive" },
                    new[] { "market" }));
        }

        [Fact]
        public void StartedAndCompletedNodes_AreNeverOfferedAgain()
        {
            var eligible = CampaignGraph.Eligible(Graph(), new[] { "docks" },
                new[] { "bastion", "archive" });
            Assert.DoesNotContain("docks", eligible);
            Assert.DoesNotContain("bastion", eligible);
            Assert.DoesNotContain("archive", eligible);
        }

        [Fact]
        public void FinaleRequiresAnExplicitCompletedFinalNode()
        {
            Assert.False(CampaignGraph.FinaleComplete(Graph(), new[] { "gate" }));
            Assert.True(CampaignGraph.FinaleComplete(Graph(), new[] { "spire" }));
        }

        [Fact]
        public void InvalidGraphs_FailBeforePlay()
        {
            Assert.Throws<InvalidOperationException>(() => CampaignGraph.Validate(new[]
            {
                new CampaignNode("a", new[] { "missing" })
            }));
            Assert.Throws<InvalidOperationException>(() => CampaignGraph.Validate(new[]
            {
                new CampaignNode("a", new[] { "b" }),
                new CampaignNode("b", new[] { "a" })
            }));
            Assert.Throws<InvalidOperationException>(() => CampaignGraph.Validate(new[]
            {
                new CampaignNode("same"), new CampaignNode("same")
            }));
        }
    }
}
