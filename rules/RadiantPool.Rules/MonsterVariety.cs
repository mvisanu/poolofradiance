using System.Collections.Generic;

namespace RadiantPool.Rules
{
    /// <summary>Themed, equal-XP substitution pools that widen the monster mix an
    /// encounter can field. Each authored "mook" id maps to a small family of
    /// interchangeable creatures that all share the SAME <see cref="MonsterDefinition.Xp"/>,
    /// so swapping one for another at spawn time leaves every encounter's total XP — and
    /// therefore the pinned campaign level curve and difficulty floor — completely
    /// unchanged. Named bosses and unique elites are intentionally absent: they never
    /// substitute, so story beats keep their exact monster.</summary>
    public static class MonsterVariety
    {
        // Families. Every member of a family must share one XP value (asserted by
        // MonsterVarietyTests). Grouped by theme so an undead crypt never fields a bandit.
        private static readonly string[][] Families =
        {
            new[] { "dock_rat", "plague_rat", "fen_croaker", "grave_crawler" }, // vermin, 25
            new[] { "marsh_skulker", "gray_knife" },                            // bandits, 25
            new[] { "kindled_zealot", "ember_acolyte" },                        // cultists, 25
            new[] { "risen_drowned", "bonewalker" },                            // lesser undead, 50
            new[] { "goblin", "reed_scout" },                                   // raiders, 50
            new[] { "dust_jackal", "sand_scuttler" },                           // lesser beasts, 50
            new[] { "orc", "ironpost_soldier" },                                // raiders, 100
            new[] { "giant_spider", "brown_bear", "ashfang_stalker", "thornback_boar" }, // beasts, 200
            new[] { "bloated_drowned", "iron_sentinel" },                       // heavy bruisers, 200
            new[] { "veil_adept", "ash_ogre" },                                 // cult elites, 450
        };

        private static readonly Dictionary<string, string[]> ByMember = BuildIndex();

        private static Dictionary<string, string[]> BuildIndex()
        {
            var map = new Dictionary<string, string[]>();
            foreach (var family in Families)
                foreach (var id in family)
                    map[id] = family;
            return map;
        }

        /// <summary>All ids that participate in substitution (for tests/tools).</summary>
        public static IEnumerable<string> AllPooledIds => ByMember.Keys;

        /// <summary>The family an id belongs to, or a single-element array of just the id
        /// when it does not substitute.</summary>
        public static IReadOnlyList<string> FamilyOf(string monsterId) =>
            ByMember.TryGetValue(monsterId, out var family) ? family : new[] { monsterId };

        /// <summary>Pick a same-XP, same-theme alternative for an authored monster. Ids
        /// outside every family (bosses, uniques, high tiers) are returned unchanged.</summary>
        public static string Pick(string authoredId, IRng rng)
        {
            if (!ByMember.TryGetValue(authoredId, out var family) || family.Length < 2)
                return authoredId;
            return family[rng.Next(0, family.Length - 1)];
        }
    }
}
