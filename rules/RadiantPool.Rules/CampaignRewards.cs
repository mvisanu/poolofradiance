using System;
using System.Collections.Generic;
using System.Linq;

namespace RadiantPool.Rules
{
    /// <summary>The reward contract for one playable campaign location. Combat XP still
    /// comes from canonical monster stat blocks; this is the additional Council reward
    /// paid once when the location is turned in. Reward loot is location-tiered so an
    /// early encounter cannot expose endgame equipment merely because it reuses a later
    /// monster species.</summary>
    public sealed class CampaignReward
    {
        public string ZoneId { get; }
        public int MinLevel { get; }
        public int MaxLevel { get; }
        public int QuestXp { get; }
        public int Gold { get; }
        public string LootTable { get; }
        public int ActionXp { get; }
        public int ActionGold { get; }
        public string ActionLootTable { get; }

        public CampaignReward(string zoneId, int minLevel, int maxLevel,
            int questXp, int gold, string lootTable,
            int actionXp = 0, int actionGold = 0, string actionLootTable = "")
        {
            if (string.IsNullOrWhiteSpace(zoneId)) throw new ArgumentException("zone id required");
            if (minLevel < 1 || maxLevel > Progression.MaxLevel || minLevel > maxLevel)
                throw new ArgumentException("invalid level band");
            if (questXp < 0 || gold < 0 || actionXp < 0 || actionGold < 0)
                throw new ArgumentException("rewards cannot be negative");
            ZoneId = zoneId;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
            QuestXp = questXp;
            Gold = gold;
            LootTable = lootTable ?? "";
            ActionXp = actionXp;
            ActionGold = actionGold;
            ActionLootTable = actionLootTable ?? "";
        }
    }

    /// <summary>Single rules-side authority for level bands and non-combat rewards. Unity
    /// consumes this table directly and content validation pins every JSON mirror to it.</summary>
    public static class CampaignRewardLibrary
    {
        public static readonly IReadOnlyDictionary<string, CampaignReward> All = Build();

        public static CampaignReward Get(string zoneId) => All[zoneId];

        private static Dictionary<string, CampaignReward> Build()
        {
            const string T1 = "lt_quest_tier1";
            const string T2 = "lt_quest_tier2";
            const string T3 = "lt_quest_tier3";
            const string T4 = "lt_quest_tier4";
            var rewards = new[]
            {
                new CampaignReward("old_docks", 1, 2, 300, 100, T1, 100, 35, T1),
                new CampaignReward("drowned_market", 2, 3, 900, 250, T2, 450, 200, T2),
                new CampaignReward("sunken_warcamp", 3, 4, 1200, 400, T3),
                new CampaignReward("glasslit_temple", 3, 5, 3400, 600, T3),
                new CampaignReward("ashen_ward", 4, 5, 1200, 750, T4),

                new CampaignReward("drowned_bastion", 1, 3, 500, 180, T1),
                new CampaignReward("cinderwell_yard", 1, 3, 350, 150, T1),
                new CampaignReward("cinderwell_undercroft", 2, 4, 450, 180, T2),
                new CampaignReward("ember_archive", 2, 4, 650, 220, T2),
                new CampaignReward("loomhouse_enclave", 2, 4, 700, 250, T2),
                new CampaignReward("blackbriar_manor", 2, 4, 700, 260, T2),
                new CampaignReward("gilded_quarter", 3, 5, 750, 320, T3),
                new CampaignReward("emberwild_expanse", 2, 5, 400, 150, T2),
                new CampaignReward("wild_lairs", 2, 5, 650, 280, T2),
                new CampaignReward("reedwind_encampment", 3, 5, 800, 300, T3),
                new CampaignReward("goblin_delves", 3, 5, 850, 320, T3),
                new CampaignReward("drowned_observatory_approach", 3, 5, 400, 150, T3),
                new CampaignReward("drowned_observatory_underworks", 3, 5, 400, 150, T3),
                new CampaignReward("drowned_observatory_crown", 4, 5, 1200, 500, T4),
                new CampaignReward("mirewatch_citadel", 3, 5, 1000, 420, T3),
                new CampaignReward("tidebreaker_anchorage", 3, 5, 1000, 450, T3),
                new CampaignReward("iron_concord_redoubt", 4, 5, 1100, 500, T4),
                new CampaignReward("lanternfall_necropolis", 3, 5, 1000, 450, T3),
                new CampaignReward("cinder_gate", 4, 5, 1300, 650, T4),
                new CampaignReward("crownless_citadel", 5, 5, 700, 400, T4),
                new CampaignReward("thornmaze", 5, 5, 800, 400, T4),
                new CampaignReward("ember_crown_spire", 5, 5, 2000, 1200, T4)
            };
            return rewards.ToDictionary(r => r.ZoneId);
        }
    }
}
