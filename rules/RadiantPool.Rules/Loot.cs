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
    /// ContentValidationTests keeps them aligned by id).</summary>
    public static class LootLibrary
    {
        public static readonly IReadOnlyDictionary<string, LootTable> All = Build();

        public static LootTable Get(string id) => All[id];

        private static Dictionary<string, LootTable> Build()
        {
            var tables = new List<LootTable>
            {
                new LootTable("lt_skulker", "2d6", 1, new (int, string?)[]
                    { (70, null), (25, "dagger"), (5, "potion_healing") }),
                new LootTable("lt_vermin", "1d4", 1, new (int, string?)[]
                    { (90, null), (10, "torch") }),
                new LootTable("lt_undead", "2d8", 1, new (int, string?)[]
                    { (60, null), (20, "shortsword"), (15, "shortbow"), (5, "potion_healing") }),
                new LootTable("lt_kindled", "3d6", 1, new (int, string?)[]
                    { (55, null), (25, "mace"), (15, "potion_healing"), (5, "scale_mail") }),
                new LootTable("lt_warehouse_cache", "6d6", 2, new (int, string?)[]
                    { (30, "potion_healing"), (30, "scale_mail"), (25, "longsword"), (15, "light_crossbow") }),
                new LootTable("lt_sunken_vault", "10d6", 2, new (int, string?)[]
                    { (40, "potion_healing"), (30, "chain_mail"), (30, "longsword") }),
            };
            return tables.ToDictionary(t => t.Id);
        }
    }
}
