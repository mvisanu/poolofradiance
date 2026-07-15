using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    /// <summary>End-to-end balance contract for the expanded campaign. These tests stop
    /// the runtime reward table, zone files, quest journal, catalog, monster XP, and loot
    /// tiers from silently drifting into six different answers.</summary>
    public class CampaignBalanceTests
    {
        private static readonly string ContentRoot = FindContentRoot();
        private static string FindContentRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "content");
        }

        private static JsonDocument Load(params string[] parts) => JsonDocument.Parse(
            File.ReadAllText(parts.Aggregate(ContentRoot,
                (current, part) => Path.Combine(current, part))));

        [Fact]
        public void EveryZone_MirrorsTheRuntimeRewardAndLevelBand()
        {
            var files = Directory.EnumerateFiles(Path.Combine(ContentRoot, "zones"), "*.json")
                .ToList();
            Assert.Equal(39, files.Count);
            Assert.Equal(files.Count, CampaignRewardLibrary.All.Count);

            foreach (string file in files)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                string id = root.GetProperty("id").GetString()!;
                var expected = CampaignRewardLibrary.Get(id);
                var band = root.GetProperty("levelBand");
                Assert.Equal(expected.MinLevel, band[0].GetInt32());
                Assert.Equal(expected.MaxLevel, band[1].GetInt32());
                AssertReward(root.GetProperty("clearReward"), expected);
                Assert.Contains(expected.LootTable, LootLibrary.All.Keys);
            }
        }

        [Fact]
        public void EveryQuestReward_MatchesItsLocationStagesOrSideAction()
        {
            foreach (string file in Directory.EnumerateFiles(
                         Path.Combine(ContentRoot, "quests"), "*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var q = doc.RootElement;
                string id = q.GetProperty("id").GetString()!;
                if (id == "q_muster") continue;

                if (id == "q_apothecary_delivery")
                {
                    AssertActionReward(q.GetProperty("rewards"),
                        CampaignRewardLibrary.Get("old_docks"));
                    continue;
                }
                if (id == "q_witness_brass_auction")
                {
                    AssertActionReward(q.GetProperty("rewards"),
                        CampaignRewardLibrary.Get("drowned_market"));
                    continue;
                }

                if (q.TryGetProperty("zones", out var zones))
                {
                    var stages = q.GetProperty("stageRewards").EnumerateArray().ToList();
                    Assert.Equal(zones.GetArrayLength(), stages.Count);
                    int xp = 0, gold = 0;
                    for (int i = 0; i < stages.Count; i++)
                    {
                        string zone = zones[i].GetString()!;
                        Assert.Equal(zone, stages[i].GetProperty("zone").GetString());
                        var expected = CampaignRewardLibrary.Get(zone);
                        AssertReward(stages[i], expected);
                        xp += expected.QuestXp;
                        gold += expected.Gold;
                    }
                    Assert.Equal(xp, q.GetProperty("rewards").GetProperty("xpEach").GetInt32());
                    Assert.Equal(gold, q.GetProperty("rewards").GetProperty("gold").GetInt32());
                }
                else
                {
                    string zone = q.GetProperty("zone").GetString()!;
                    AssertReward(q.GetProperty("rewards"), CampaignRewardLibrary.Get(zone));
                }
            }
        }

        [Fact]
        public void EncounterXpFitsTheAdvertisedMinimumLevel()
        {
            // Calibrated for the game's 2-player floor and its tested monster-side easing.
            // These are raw canonical XP ceilings per encounter, not altered stat blocks.
            var maxEncounterXp = new Dictionary<int, int>
            {
                [1] = 200, [2] = 600, [3] = 800, [4] = 1000, [5] = 1200,
                [9] = 6000, [10] = 6000, [11] = 6500, [12] = 12000,
                [14] = 14000, [15] = 11000, [17] = 13000,
                [18] = 17000, [19] = 34000
            };

            foreach (string file in Directory.EnumerateFiles(
                         Path.Combine(ContentRoot, "zones"), "*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                string zone = root.GetProperty("id").GetString()!;
                int minLevel = CampaignRewardLibrary.Get(zone).MinLevel;
                foreach (var encounter in root.GetProperty("encounters").EnumerateArray()
                             .Where(e => e.GetProperty("requiredForClear").GetBoolean()))
                {
                    int xp = encounter.GetProperty("units").EnumerateArray()
                        .Sum(u => MonsterLibrary.Get(u.GetString()!).Xp);
                    Assert.True(xp <= maxEncounterXp[minLevel],
                        $"{zone}/{encounter.GetProperty("id").GetString()}: {xp} XP exceeds " +
                        $"level {minLevel} ceiling {maxEncounterXp[minLevel]}");
                }
            }
        }

        [Fact]
        public void CatalogRewardsAndBands_MatchTheAuthoredQuestAndZoneFiles()
        {
            var authored = Directory.EnumerateFiles(Path.Combine(ContentRoot, "quests"), "*.json")
                .Select(f => JsonDocument.Parse(File.ReadAllText(f)))
                .ToDictionary(d => d.RootElement.GetProperty("id").GetString()!);
            try
            {
                using var catalog = Load("campaign", "full_campaign.json");
                using var expansion = Load("campaign", "level20_expansion.json");
                var locations = catalog.RootElement.GetProperty("locations").EnumerateArray()
                    .Concat(expansion.RootElement.GetProperty("locations").EnumerateArray());
                foreach (var location in locations)
                {
                    string id = location.GetProperty("id").GetString()!;
                    if (id == "council_quarter") continue;
                    var expected = CampaignRewardLibrary.Get(id);
                    Assert.Equal(expected.MinLevel,
                        location.GetProperty("levelBand")[0].GetInt32());
                    Assert.Equal(expected.MaxLevel,
                        location.GetProperty("levelBand")[1].GetInt32());
                }

                var entries = catalog.RootElement.GetProperty("commissions").EnumerateArray()
                    .Concat(catalog.RootElement.GetProperty("sideQuests").EnumerateArray())
                    .Concat(expansion.RootElement.GetProperty("commissions").EnumerateArray())
                    .Concat(expansion.RootElement.GetProperty("sideQuests").EnumerateArray());
                foreach (var entry in entries)
                {
                    string id = entry.GetProperty("id").GetString()!;
                    var actual = entry.GetProperty("rewards");
                    var expected = authored[id].RootElement.GetProperty("rewards");
                    Assert.Equal(expected.GetProperty("xpEach").GetInt32(),
                        actual.GetProperty("xpEach").GetInt32());
                    Assert.Equal(expected.GetProperty("gold").GetInt32(),
                        actual.GetProperty("gold").GetInt32());
                    Assert.Equal(LootId(expected), LootId(actual));
                }
            }
            finally
            {
                foreach (var doc in authored.Values) doc.Dispose();
            }
        }

        private static void AssertReward(JsonElement actual, CampaignReward expected)
        {
            Assert.Equal(expected.QuestXp, actual.GetProperty("xpEach").GetInt32());
            Assert.Equal(expected.Gold, actual.GetProperty("gold").GetInt32());
            Assert.Equal(expected.LootTable, LootId(actual));
        }

        private static void AssertActionReward(JsonElement actual, CampaignReward expected)
        {
            Assert.Equal(expected.ActionXp, actual.GetProperty("xpEach").GetInt32());
            Assert.Equal(expected.ActionGold, actual.GetProperty("gold").GetInt32());
            Assert.Equal(expected.ActionLootTable, LootId(actual));
        }

        private static string? LootId(JsonElement reward)
        {
            var value = reward.GetProperty("lootTable");
            return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        }
    }
}
