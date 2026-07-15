using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    /// <summary>Weighted loot tables (mirrors content/loot/loot_tables.json). The server
    /// rolls; clients only see results.</summary>
    public sealed class LootTable
    {
        public string Id { get; }
        public DiceExpression Gold { get; }
        public int Rolls { get; }
        public IReadOnlyList<(int Weight, string? ItemId)> Entries { get; }

        public LootTable(string id, string goldDice, int rolls,
            IEnumerable<(int weight, string? itemId)> entries)
        {
            if (rolls < 0) throw new ArgumentException("rolls must be >= 0");
            Id = id;
            Gold = DiceExpression.Parse(goldDice);
            Rolls = rolls;
            Entries = entries.ToList();
            if (Entries.Count == 0 || Entries.Sum(e => e.Weight) <= 0)
                throw new ArgumentException("Loot table needs positive total weight.");
        }

        public LootResult Roll(IRng rng)
        {
            int gold = Math.Max(0, Gold.Roll(rng).Total);
            var items = new List<string>();
            int total = Entries.Sum(e => e.Weight);
            for (int i = 0; i < Rolls; i++)
            {
                int pick = rng.Next(1, total);
                foreach (var (weight, itemId) in Entries)
                {
                    pick -= weight;
                    if (pick <= 0)
                    {
                        if (itemId != null) items.Add(itemId);
                        break;
                    }
                }
            }
            return new LootResult(gold, items);
        }
    }

    public readonly struct LootResult
    {
        public int Gold { get; }
        public IReadOnlyList<string> ItemIds { get; }

        public LootResult(int gold, IReadOnlyList<string> itemIds)
        {
            Gold = gold;
            ItemIds = itemIds;
        }
    }

    /// <summary>In-code copies of the v1 tables (content JSON is the authoring source;
    /// ContentValidationTests keeps them aligned by id).
    ///
    /// The tables get BETTER as the campaign deepens, so levelling pays out in gear you can
    /// actually equip and not just a bigger number: the roadside fights hand out the starting
    /// kit, the mid caches carry rapiers and studded leather, and the vault and the warcamp —
    /// the last places you reach — are where a greatsword, half plate or splint comes from.
    /// LootTests pins that gradient; if a table is retuned, the ceiling must not fall.</summary>
    public static class LootLibrary
    {
        public const int BaseEncounterGearChance = 30;
        public const int EncounterGearChancePerLevel = 10;

        public static readonly IReadOnlyDictionary<string, LootTable> All = Build();

        public static LootTable Get(string id) => All[id];

        /// <summary>Reward tier follows the hero's resulting monster level: level 1-2
        /// heroes receive tier 1, then each higher hero level advances one tier.</summary>
        public static string QuestTableForCharacterLevel(int characterLevel)
        {
            int threat = Difficulty.TargetMonsterLevel(characterLevel);
            if (threat <= 1) return "lt_quest_tier1";
            if (threat == 2) return "lt_quest_tier2";
            if (threat == 3) return "lt_quest_tier3";
            return "lt_quest_tier4";
        }

        public static bool IsQuestTable(string tableId) =>
            tableId != null && tableId.StartsWith("lt_quest_tier",
                StringComparison.Ordinal);

        public static int EncounterGearChancePercent(int characterLevel) =>
            Math.Min(75, BaseEncounterGearChance
                + Difficulty.TargetMonsterLevel(characterLevel) * EncounterGearChancePerLevel);

        /// <summary>Required quest fights can add one level-matched gear item. This is a
        /// single roll even when the turn-in table grants two items at tier 4.</summary>
        public static LootResult RollScaledEncounterReward(int characterLevel, IRng rng)
        {
            if (rng.Next(1, 100) > EncounterGearChancePercent(characterLevel))
                return new LootResult(0, Array.Empty<string>());
            var table = Get(QuestTableForCharacterLevel(characterLevel));
            int total = table.Entries.Sum(e => e.Weight);
            int pick = rng.Next(1, total);
            foreach (var entry in table.Entries)
            {
                pick -= entry.Weight;
                if (pick > 0) continue;
                return entry.ItemId == null
                    ? new LootResult(0, Array.Empty<string>())
                    : new LootResult(0, new[] { entry.ItemId });
            }
            return new LootResult(0, Array.Empty<string>());
        }

        private static Dictionary<string, LootTable> Build()
        {
            var tables = new List<LootTable>
            {
                new LootTable("lt_skulker", "2d6", 1, new (int, string?)[]
                    { (70, null), (25, "dagger"), (5, "potion_healing") }),
                new LootTable("lt_vermin", "1d4", 1, new (int, string?)[]
                    { (90, null), (10, "torch") }),
                new LootTable("lt_undead", "2d8", 1, new (int, string?)[]
                    { (58, null), (20, "shortsword"), (15, "shortbow"), (2, "warhammer"),
                      (5, "potion_healing") }),
                new LootTable("lt_kindled", "3d6", 1, new (int, string?)[]
                    { (52, null), (25, "mace"), (15, "potion_healing"), (5, "scale_mail"),
                      (3, "studded_leather") }),
                // Species drops stay conservative. High-grade gear is paid by the
                // location-tiered quest tables below, not by reusing a late monster early.
                new LootTable("lt_raider", "3d6", 1, new (int, string?)[]
                    { (55, null), (16, "longsword"), (10, "scale_mail"),
                      (9, "warhammer"), (10, "potion_healing") }),
                new LootTable("lt_warden", "4d6", 1, new (int, string?)[]
                    { (50, null), (15, "longsword"), (10, "chain_mail"),
                      (10, "warhammer"), (15, "potion_healing") }),
                new LootTable("lt_warehouse_cache", "6d6", 2, new (int, string?)[]
                    { (25, "potion_healing"), (22, "scale_mail"), (20, "longsword"),
                      (13, "light_crossbow"), (12, "rapier"), (8, "studded_leather") }),
                new LootTable("lt_sunken_vault", "10d6", 2, new (int, string?)[]
                    { (28, "potion_healing"), (20, "chain_mail"), (18, "longsword"),
                      (14, "greatsword"), (12, "longbow"), (8, "splint") }),
                new LootTable("lt_beast_den", "1d6", 1, new (int, string?)[]
                    { (80, null), (20, "potion_healing") }),
                new LootTable("lt_goblin", "2d6", 1, new (int, string?)[]
                    { (63, null), (20, "shortbow"), (10, "leather_armor"), (2, "studded_leather"),
                      (5, "potion_healing") }),
                new LootTable("lt_warcamp", "8d6", 2, new (int, string?)[]
                    { (26, "potion_healing"), (20, "longsword"), (16, "scale_mail"),
                      (14, "chain_mail"), (12, "greataxe"), (7, "warhammer"), (5, "half_plate") }),
                // Turn-ins already pay their listed gold; these zero-gold tables are the
                // equipment parcel. Tiers 1-4 follow CampaignRewardLibrary level bands.
                new LootTable("lt_quest_tier1", "0", 1, new (int, string?)[]
                    { (40, "potion_healing"), (20, "shortbow"), (15, "leather_armor"),
                      (15, "mace"), (10, "shield") }),
                new LootTable("lt_quest_tier2", "0", 1, new (int, string?)[]
                    { (14, "potion_healing"), (16, "rapier"), (16, "studded_leather"),
                      (16, "warhammer"), (13, "scale_mail"), (10, "longsword"),
                      (15, "runed_staff") }),
                new LootTable("lt_quest_tier3", "0", 1, new (int, string?)[]
                    { (14, "potion_healing"), (18, "greatsword"), (16, "longbow"),
                      (15, "greataxe"), (14, "half_plate"), (9, "studded_leather"),
                      (14, "runed_staff") }),
                new LootTable("lt_quest_tier4", "0", 2, new (int, string?)[]
                    { (10, "potion_healing"), (17, "greatsword"), (15, "longbow"),
                      (15, "greataxe"), (16, "half_plate"), (14, "splint"),
                      (13, "runed_staff") }),
            };
            return tables.ToDictionary(t => t.Id);
        }
    }
}
