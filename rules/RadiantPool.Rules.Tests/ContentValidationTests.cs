using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RadiantPool.Rules;
using Xunit;

namespace RadiantPool.Rules.Tests
{
    /// <summary>Validates the /content JSON: parseable, versioned, dice expressions legal,
    /// and every cross-reference (monster/loot/item/zone/quest) resolves. Also enforces the
    /// IP-CHECKLIST banned-term list. Content bugs fail CI, not a play session.</summary>
    public class ContentValidationTests
    {
        private static readonly string ContentRoot = FindContentRoot();

        private static string FindContentRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "content");
        }

        private static IEnumerable<string> AllContentFiles() =>
            Directory.EnumerateFiles(ContentRoot, "*.json", SearchOption.AllDirectories);

        private static JsonDocument Load(string relative) =>
            JsonDocument.Parse(File.ReadAllText(Path.Combine(ContentRoot, relative)));

        [Fact]
        public void AllFiles_ParseAndCarrySchemaVersion()
        {
            var files = AllContentFiles().ToList();
            Assert.True(files.Count >= 10, "expected content files");
            foreach (var f in files)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                Assert.True(doc.RootElement.TryGetProperty("schemaVersion", out var v),
                    $"{f} missing schemaVersion");
                Assert.Equal(1, v.GetInt32());
            }
        }

        [Fact]
        public void Monsters_MatchRulesLibrary_AndDiceParse()
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ContentRoot, "monsters"), "*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var root = doc.RootElement;
                string id = root.GetProperty("id").GetString()!;
                Assert.True(MonsterLibrary.All.ContainsKey(id),
                    $"monster '{id}' not in MonsterLibrary");
                DiceExpression.Parse(root.GetProperty("hpDice").GetString()!);
                Assert.False(string.IsNullOrEmpty(root.GetProperty("srdRef").GetString()),
                    $"monster '{id}' missing srdRef (IP guardrail)");
                foreach (var atk in root.GetProperty("attacks").EnumerateArray())
                    DiceExpression.Parse(atk.GetProperty("damage").GetString()!);

                // JSON stats agree with the in-code library copy.
                var def = MonsterLibrary.Get(id);
                Assert.Equal(def.ArmorClass, root.GetProperty("ac").GetInt32());
                Assert.Equal(def.HpDice, root.GetProperty("hpDice").GetString());
                Assert.Equal(def.Xp, root.GetProperty("xp").GetInt32());
            }
        }

        [Fact]
        public void Zones_ReferenceRealMonstersAndQuests()
        {
            var questIds = QuestIds();
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ContentRoot, "zones"), "*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var root = doc.RootElement;
                foreach (var enc in root.GetProperty("encounters").EnumerateArray())
                {
                    foreach (var unit in enc.GetProperty("units").EnumerateArray())
                        Assert.True(MonsterLibrary.All.ContainsKey(unit.GetString()!),
                            $"{f}: unknown monster '{unit.GetString()}'");
                    Assert.True(enc.GetProperty("units").GetArrayLength() > 0);
                }
                string clearQuest = root.GetProperty("clearQuest").GetString()!;
                Assert.Contains(clearQuest, questIds);
            }
        }

        [Fact]
        public void Quests_ChainAndZoneReferencesResolve()
        {
            var questIds = QuestIds();
            var zoneIds = Directory.EnumerateFiles(Path.Combine(ContentRoot, "zones"), "*.json")
                .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement
                    .GetProperty("id").GetString()!).ToHashSet();

            foreach (var f in Directory.EnumerateFiles(Path.Combine(ContentRoot, "quests"), "*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var root = doc.RootElement;
                if (root.TryGetProperty("zone", out var zone))
                    Assert.Contains(zone.GetString()!, zoneIds);
                if (root.TryGetProperty("unlocks", out var unlocks))
                    foreach (var u in unlocks.EnumerateArray())
                        Assert.Contains(u.GetString()!, questIds);
                Assert.True(root.GetProperty("objectives").GetArrayLength() > 0);
                Assert.True(root.GetProperty("rewards").GetProperty("xpEach").GetInt32() >= 0);
            }
        }

        [Fact]
        public void LootTables_ReferenceRealItems_AndCoverMonsterTables()
        {
            using var itemsDoc = Load(Path.Combine("items", "items.json"));
            var itemIds = itemsDoc.RootElement.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetString()!).ToHashSet();

            using var lootDoc = Load(Path.Combine("loot", "loot_tables.json"));
            var tableIds = new HashSet<string>();
            foreach (var t in lootDoc.RootElement.GetProperty("tables").EnumerateArray())
            {
                tableIds.Add(t.GetProperty("id").GetString()!);
                DiceExpression.Parse(t.GetProperty("gold").GetString()!);
                int weight = 0;
                foreach (var e in t.GetProperty("entries").EnumerateArray())
                {
                    weight += e.GetProperty("weight").GetInt32();
                    var item = e.GetProperty("item");
                    if (item.ValueKind != JsonValueKind.Null)
                        Assert.Contains(item.GetString()!, itemIds);
                }
                Assert.True(weight > 0);
            }

            foreach (var def in MonsterLibrary.All.Values)
                Assert.Contains(def.LootTable, tableIds);
        }

        [Fact]
        public void Items_DiceAndConsumablesParse()
        {
            using var doc = Load(Path.Combine("items", "items.json"));
            foreach (var i in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                if (i.TryGetProperty("weapon", out var w))
                    DiceExpression.Parse(w.GetProperty("damage").GetString()!);
                if (i.TryGetProperty("consumable", out var c)
                    && c.TryGetProperty("dice", out var d))
                    DiceExpression.Parse(d.GetString()!);
                Assert.True(i.GetProperty("costCp").GetInt32() >= 0);
            }
        }

        [Fact]
        public void ZoneProgression_FormsCompleteChain()
        {
            // docks → market → temple, temple completes the campaign.
            using var docks = Load(Path.Combine("zones", "old_docks.json"));
            Assert.Equal("drowned_market",
                docks.RootElement.GetProperty("onCleared").GetProperty("unlocks").GetString());
            using var market = Load(Path.Combine("zones", "drowned_market.json"));
            Assert.Equal("glasslit_temple",
                market.RootElement.GetProperty("onCleared").GetProperty("unlocks").GetString());
            using var temple = Load(Path.Combine("zones", "glasslit_temple.json"));
            Assert.True(temple.RootElement.GetProperty("onCleared")
                .GetProperty("campaignComplete").GetBoolean());
        }

        [Fact]
        public void NoBannedIpTerms_AnywhereInContent()
        {
            // Per IP-CHECKLIST.md hard bans.
            var banned = new[]
            {
                "dungeons & dragons", "dungeons and dragons", "d&d",
                "forgotten realms", "faerun", "faerûn", "moonsea",
                "phlan", "sokal", "valjevo", "tyranthraxus",
                "beholder", "mind flayer", "illithid", "yuan-ti",
                "githyanki", "displacer beast", "umber hulk", "kuo-toa"
            };
            foreach (var f in AllContentFiles())
            {
                string text = File.ReadAllText(f).ToLowerInvariant();
                foreach (var term in banned)
                    Assert.False(text.Contains(term), $"{f} contains banned term '{term}'");
            }
        }

        private static HashSet<string> QuestIds() =>
            Directory.EnumerateFiles(Path.Combine(ContentRoot, "quests"), "*.json")
                .Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement
                    .GetProperty("id").GetString()!).ToHashSet();
    }
}
