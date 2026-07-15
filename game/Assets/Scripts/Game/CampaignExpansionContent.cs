using UnityEngine;

namespace RadiantPool.Game
{
    public enum CampaignSiteTheme
    {
        Keep, Ruins, Crypt, Archive, Enclave, Manor, Quarter, Wilds, Camp, Caves,
        Observatory, Marsh, Anchorage, Redoubt, Necropolis, Gate, Citadel, Maze, Spire
    }

    public sealed class CampaignEncounterPlan
    {
        public string Suffix { get; }
        public string DisplayName { get; }
        public string[] MonsterIds { get; }

        public CampaignEncounterPlan(string suffix, string displayName, params string[] monsterIds)
        {
            Suffix = suffix;
            DisplayName = displayName;
            MonsterIds = monsterIds;
        }
    }

    /// <summary>One playable campaign cell beyond the original five city districts.
    /// This is the single source used by the bootstrap for geometry, encounters,
    /// waystones, quest graph configuration, journal text, and rewards.</summary>
    public sealed class CampaignSitePlan
    {
        public string ZoneId { get; }
        public string DisplayName { get; }
        public string QuestName { get; }
        public string Description { get; }
        public int XpEach { get; }
        public int Gold { get; }
        public string[] PrerequisiteZoneIds { get; }
        public bool StartsAvailable { get; }
        public bool FinalQuest { get; }
        public Vector3 Center { get; }
        public CampaignSiteTheme Theme { get; }
        public CampaignEncounterPlan[] Encounters { get; }

        public CampaignSitePlan(string zoneId, string displayName, string questName,
            string description, int xpEach, int gold, string[] prerequisites,
            bool startsAvailable, bool finalQuest, Vector3 center, CampaignSiteTheme theme,
            params CampaignEncounterPlan[] encounters)
        {
            ZoneId = zoneId;
            DisplayName = displayName;
            QuestName = questName;
            Description = description;
            XpEach = xpEach;
            Gold = gold;
            PrerequisiteZoneIds = prerequisites;
            StartsAvailable = startsAvailable;
            FinalQuest = finalQuest;
            Center = center;
            Theme = theme;
            Encounters = encounters;
        }

        public GameDirector.ZoneConfig ToZoneConfig()
        {
            CampaignExpansionContent.SiteActionFor(ZoneId,
                out string action, out string choiceA, out string choiceB);
            return new GameDirector.ZoneConfig
            {
                ZoneId = ZoneId,
                DisplayName = DisplayName,
                QuestName = QuestName,
                Description = Description,
                RequiredEncounters = Encounters.Length,
                XpEach = XpEach,
                Gold = Gold,
                PrerequisiteZoneIds = PrerequisiteZoneIds,
                StartsAvailable = StartsAvailable,
                FinalQuest = FinalQuest,
                SiteAction = action,
                ChoiceA = choiceA,
                ChoiceB = choiceB
            };
        }
    }

    /// <summary>Original-IP campaign expansion derived from the structural scope in
    /// content/campaign/full_campaign.json. Site indices append after the five existing
    /// districts, preserving every existing save's serialized zone positions.</summary>
    public static class CampaignExpansionContent
    {
        public static void SiteActionFor(string zoneId, out string action,
            out string choiceA, out string choiceB)
        {
            choiceA = "";
            choiceB = "";
            switch (zoneId)
            {
                case "drowned_bastion":
                    action = "Decide the fate of the spectral watch.";
                    choiceA = "Honor their oath"; choiceB = "Break the haunting"; return;
                case "cinderwell_undercroft": action = "Recover the Gray Knives' ledger."; return;
                case "ember_archive": action = "Secure the surviving civic records."; return;
                case "loomhouse_enclave": action = "Recover the lost Council seal."; return;
                case "emberwild_expanse": action = "Recover the bound ember vessel."; return;
                case "reedwind_encampment":
                    action = "Set the terms of the Reedwind accord.";
                    choiceA = "Forge an alliance"; choiceB = "Respect their independence"; return;
                case "goblin_delves":
                    action = "Decide the fate of the Gloam pact.";
                    choiceA = "Accept a guarded truce"; choiceB = "Break the pact stone"; return;
                case "drowned_observatory_approach": action = "Open the underworks lift."; return;
                case "drowned_observatory_underworks": action = "Shut down the blackwater controls."; return;
                case "drowned_observatory_crown": action = "Destroy the Cinderflow poison source."; return;
                case "mirewatch_citadel":
                    action = "Choose Mirewatch's place in the reclamation.";
                    choiceA = "Offer a Council alliance"; choiceB = "Guarantee marsh autonomy"; return;
                case "tidebreaker_anchorage": action = "Free the captive Tidebreaker heir."; return;
                case "iron_concord_redoubt":
                    action = "Resolve the Iron Concord mission.";
                    choiceA = "Offer terms to the commandant"; choiceB = "Seize the traitor evidence"; return;
                case "lanternfall_necropolis": action = "Seal the black mausoleum."; return;
                case "cinder_gate": action = "Raise the Council banner over the gatehouse."; return;
                case "crownless_citadel": action = "Recover the enemy campaign reports."; return;
                case "thornmaze": action = "Open the secret gate beyond the maze."; return;
                case "ember_crown_spire":
                    action = "Decide the defeated lieutenant's fate and claim the crown seal.";
                    choiceA = "Accept surrender"; choiceB = "Demand final justice"; return;
                default: action = ""; return;
            }
        }

        private static CampaignEncounterPlan E(string suffix, string name,
            params string[] monsters) => new CampaignEncounterPlan(suffix, name, monsters);

        private static string Travel(string place, string objective) =>
            $"Use the Council Waystone Network to reach {place}. {objective} " +
            "Then return by its waystone and report at Council Hall.";

        public static readonly CampaignSitePlan[] Sites =
        {
            new CampaignSitePlan("drowned_bastion", "The Drowned Bastion",
                "The Watchers Below",
                Travel("the Drowned Bastion", "Quiet the three haunted watches and secure the sealed keep."),
                500, 180, System.Array.Empty<string>(), true, false,
                new Vector3(-150f, 0f, -150f), CampaignSiteTheme.Keep,
                E("watch", "the drowned watch", "risen_drowned", "bonewalker", "bonewalker"),
                E("hall", "the echoing hall", "risen_drowned", "risen_drowned", "bonewalker"),
                E("vault", "the sealed vault", "hollow_warden", "bonewalker")),

            new CampaignSitePlan("cinderwell_yard", "Cinderwell Yard", "The Gray Knives",
                Travel("Cinderwell Yard", "Break the raider cordon and find the descent they guard."),
                350, 150, System.Array.Empty<string>(), true, false,
                new Vector3(-100f, 0f, -150f), CampaignSiteTheme.Ruins,
                E("cordon", "the broken cordon", "goblin", "goblin", "marsh_skulker"),
                E("well", "the cinder well", "marsh_skulker", "marsh_skulker", "goblin"),
                E("captain", "the raider captain's yard", "orc", "goblin", "goblin")),

            new CampaignSitePlan("cinderwell_undercroft", "Cinderwell Undercroft",
                "The Gray Knives: Below",
                Travel("the Cinderwell Undercroft", "Clear the crypt road and end the raider lair below."),
                450, 180, new[] { "cinderwell_yard" }, false, false,
                new Vector3(-50f, 0f, -150f), CampaignSiteTheme.Crypt,
                E("crypt", "the pillaged crypt", "bonewalker", "bonewalker", "goblin"),
                E("passage", "the knife passage", "goblin", "goblin", "orc"),
                E("lair", "the undercroft lair", "orc_warchief", "goblin", "goblin")),

            new CampaignSitePlan("ember_archive", "The Ember Archive",
                "Records of the First Flame",
                Travel("the Ember Archive", "Recover the civic records by defeating the three forces that hold its stacks."),
                650, 220, new[] { "old_docks" }, false, false,
                new Vector3(0f, 0f, -150f), CampaignSiteTheme.Archive,
                E("stacks", "the shattered stacks", "bonewalker", "bonewalker", "kindled_zealot"),
                E("scriptorium", "the ash scriptorium", "kindled_zealot", "kindled_zealot", "bonewalker"),
                E("records", "the sealed records hall", "hollow_warden", "kindled_zealot")),

            new CampaignSitePlan("loomhouse_enclave", "The Loomhouse Enclave",
                "The Lost Council Seal",
                Travel("the Loomhouse Enclave", "Open the occupied compound and recover the Council seal from its inner court."),
                700, 250, new[] { "drowned_bastion" }, false, false,
                new Vector3(50f, 0f, -150f), CampaignSiteTheme.Enclave,
                E("gate", "the loom gate", "marsh_skulker", "marsh_skulker", "orc"),
                E("workshop", "the captive workshop", "orc", "orc", "marsh_skulker"),
                E("seal", "the seal chamber", "orc_warchief", "orc", "marsh_skulker")),

            new CampaignSitePlan("blackbriar_manor", "Blackbriar Manor",
                "Knives in Blackbriar",
                Travel("Blackbriar Manor", "Defeat its patrols, secret guard, and the knife captain in the great room."),
                700, 260, new[] { "drowned_bastion" }, false, false,
                new Vector3(100f, 0f, -150f), CampaignSiteTheme.Manor,
                E("garden", "the briar garden", "marsh_skulker", "marsh_skulker", "goblin"),
                E("passage", "the hidden passage", "goblin", "orc", "orc"),
                E("captain", "the knife captain's hall", "orc_warchief", "marsh_skulker", "marsh_skulker")),

            new CampaignSitePlan("gilded_quarter", "The Gilded Quarter", "Gold Under Ash",
                Travel("the Gilded Quarter", "Reclaim the three treasure streets and drive out their occupiers."),
                750, 320, new[] { "blackbriar_manor" }, false, false,
                new Vector3(150f, 0f, -150f), CampaignSiteTheme.Quarter,
                E("promenade", "the ash promenade", "orc", "orc", "marsh_skulker"),
                E("treasury", "the broken treasury", "orc", "orc", "orc"),
                E("estate", "the last gilded estate", "orc_warchief", "orc", "orc")),

            new CampaignSitePlan("emberwild_expanse", "The Emberwild Expanse",
                "The Bound Ember",
                Travel("the Emberwild Expanse", "Follow the three marked trails to recover the lost ember vessel."),
                400, 150, new[] { "old_docks" }, false, false,
                new Vector3(-150f, 0f, -95f), CampaignSiteTheme.Wilds,
                E("trail", "the scorched trail", "giant_spider", "giant_spider"),
                E("crossing", "the wild crossing", "brown_bear", "goblin", "goblin"),
                E("vessel", "the vessel hollow", "brown_bear", "giant_spider", "giant_spider")),

            new CampaignSitePlan("wild_lairs", "The Wilder Dens", "Marks on the Wild",
                Travel("the Wilder Dens", "Hunt the marked beasts in all three lairs."),
                650, 280, new[] { "sunken_warcamp" }, false, false,
                new Vector3(-100f, 0f, -95f), CampaignSiteTheme.Wilds,
                E("web", "the webbed den", "giant_spider", "giant_spider", "giant_spider"),
                E("briar", "the bear's briar", "brown_bear", "brown_bear"),
                E("cave", "the marked cave", "goblin", "goblin", "orc")),

            new CampaignSitePlan("reedwind_encampment", "Reedwind Encampment",
                "The Reedwind Accord",
                Travel("Reedwind Encampment", "Defend its outer fires and break the counter-raid threatening the accord."),
                800, 300, new[] { "ember_archive" }, false, false,
                new Vector3(-50f, 0f, -95f), CampaignSiteTheme.Camp,
                E("fires", "the outer fires", "goblin", "goblin", "orc"),
                E("ford", "the reed ford", "orc", "orc", "goblin"),
                E("raid", "the counter-raid", "orc_warchief", "orc", "orc")),

            new CampaignSitePlan("goblin_delves", "The Gloam Delves", "The Gloam Pact",
                Travel("the Gloam Delves", "Fight through the cave waves and confront the chieftain at the pact stone."),
                850, 320, new[] { "drowned_market" }, false, false,
                new Vector3(0f, 0f, -95f), CampaignSiteTheme.Caves,
                E("mouth", "the delve mouth", "goblin", "goblin", "goblin"),
                E("pens", "the prisoner pens", "goblin", "goblin", "orc"),
                E("pact", "the pact stone", "orc_warchief", "goblin", "goblin")),

            new CampaignSitePlan("drowned_observatory_approach", "Drowned Observatory Approach",
                "Poison in the Cinderflow: Approach",
                Travel("the Drowned Observatory Approach", "Secure the island landing and open the route into the underworks."),
                400, 150, new[] { "ember_archive" }, false, false,
                new Vector3(50f, 0f, -95f), CampaignSiteTheme.Observatory,
                E("landing", "the drowned landing", "risen_drowned", "risen_drowned", "bonewalker"),
                E("laboratory", "the outer laboratory", "kindled_zealot", "bonewalker", "bonewalker"),
                E("lift", "the underworks lift", "hollow_warden", "kindled_zealot")),

            new CampaignSitePlan("drowned_observatory_underworks", "Drowned Observatory Underworks",
                "Poison in the Cinderflow: Underworks",
                Travel("the Observatory Underworks", "Shut down the water controls and clear the experiment cells."),
                400, 150, new[] { "drowned_observatory_approach" }, false, false,
                new Vector3(100f, 0f, -95f), CampaignSiteTheme.Observatory,
                E("controls", "the blackwater controls", "risen_drowned", "bonewalker", "bonewalker"),
                E("cells", "the experiment cells", "risen_drowned", "risen_drowned", "kindled_zealot"),
                E("shaft", "the crown shaft", "hollow_warden", "bonewalker", "kindled_zealot")),

            new CampaignSitePlan("drowned_observatory_crown", "Drowned Observatory Crown",
                "Poison in the Cinderflow: Crown",
                Travel("the Observatory Crown", "Destroy the pollution source and defeat its master at the summit."),
                1200, 500, new[] { "drowned_observatory_underworks" }, false, false,
                new Vector3(150f, 0f, -95f), CampaignSiteTheme.Observatory,
                E("gallery", "the crown gallery", "kindled_zealot", "kindled_zealot", "bonewalker"),
                E("source", "the poisoned source", "risen_drowned", "hollow_warden", "bonewalker"),
                E("summit", "the drowned summit", "hollow_warden", "kindled_zealot", "kindled_zealot")),

            new CampaignSitePlan("mirewatch_citadel", "Mirewatch Citadel",
                "The Mirewatch Choice",
                Travel("Mirewatch Citadel", "Reach the hostages and break the three forces blocking a marsh accord."),
                1000, 420, new[] { "glasslit_temple" }, false, false,
                new Vector3(-150f, 0f, 95f), CampaignSiteTheme.Marsh,
                E("causeway", "the mire causeway", "marsh_skulker", "marsh_skulker", "giant_spider"),
                E("hostages", "the hostage court", "orc", "marsh_skulker", "marsh_skulker"),
                E("undercroft", "the mire undercroft", "hollow_warden", "marsh_skulker", "marsh_skulker")),

            new CampaignSitePlan("tidebreaker_anchorage", "Tidebreaker Anchorage",
                "The Tidebreaker Ransom",
                Travel("Tidebreaker Anchorage", "Storm the shipyard, find the captive heir, and break the ransom guard."),
                1000, 450, new[] { "drowned_bastion" }, false, false,
                new Vector3(-100f, 0f, 95f), CampaignSiteTheme.Anchorage,
                E("shipyard", "the chained shipyard", "marsh_skulker", "marsh_skulker", "orc"),
                E("stockade", "the ransom stockade", "orc", "orc", "marsh_skulker"),
                E("flagship", "the grounded flagship", "orc_warchief", "orc", "marsh_skulker")),

            new CampaignSitePlan("iron_concord_redoubt", "Iron Concord Redoubt",
                "Envoy to the Iron Concord",
                Travel("the Iron Concord Redoubt", "Survive the double-cross and recover the commandant's evidence."),
                1100, 500, new[] { "loomhouse_enclave" }, false, false,
                new Vector3(-50f, 0f, 95f), CampaignSiteTheme.Redoubt,
                E("embassy", "the broken embassy", "orc", "orc", "kindled_zealot"),
                E("barracks", "the iron barracks", "orc", "orc", "orc"),
                E("command", "the commandant's chamber", "orc_warchief", "orc", "kindled_zealot")),

            new CampaignSitePlan("lanternfall_necropolis", "Lanternfall Necropolis",
                "The Lanternfall Contract",
                Travel("Lanternfall Necropolis", "Fulfil the scaling contract by clearing its three sealed burial grounds."),
                1000, 450, new[] { "ember_archive", "drowned_market", "blackbriar_manor", "glasslit_temple" },
                false, false, new Vector3(0f, 0f, 95f), CampaignSiteTheme.Necropolis,
                E("field", "the lantern field", "bonewalker", "bonewalker", "risen_drowned"),
                E("crypt", "the sealed crypt", "bonewalker", "bonewalker", "bonewalker", "risen_drowned"),
                E("mausoleum", "the black mausoleum", "hollow_warden", "bonewalker", "bonewalker")),

            new CampaignSitePlan("cinder_gate", "Cinder Gate", "Take the Cinder Gate",
                Travel("Cinder Gate", "Capture both towers and defeat the force holding the gatehouse."),
                1300, 650, new[] { "iron_concord_redoubt" }, false, false,
                new Vector3(50f, 0f, 95f), CampaignSiteTheme.Gate,
                E("west", "the west tower", "orc", "orc", "kindled_zealot"),
                E("east", "the east tower", "orc", "orc", "kindled_zealot"),
                E("gatehouse", "the cinder gatehouse", "orc_warchief", "orc", "orc", "kindled_zealot")),

            new CampaignSitePlan("crownless_citadel", "The Crownless Citadel",
                "The Ember Crown: Citadel",
                Travel("the Crownless Citadel", "Breach the alarm court and clear the elite barracks beyond it."),
                700, 400, new[] { "cinder_gate" }, false, false,
                new Vector3(100f, 0f, 95f), CampaignSiteTheme.Citadel,
                E("alarm", "the alarm court", "orc", "orc", "orc", "kindled_zealot"),
                E("barracks", "the giant barracks", "brown_bear", "orc", "orc"),
                E("keep", "the crownless keep", "orc_warchief", "orc", "kindled_zealot")),

            new CampaignSitePlan("thornmaze", "The Thornmaze", "The Ember Crown: Thornmaze",
                Travel("the Thornmaze", "Find the true route and defeat the elite guard at its secret gate."),
                800, 400, new[] { "crownless_citadel" }, false, false,
                new Vector3(150f, 0f, 95f), CampaignSiteTheme.Maze,
                E("false", "the false route", "giant_spider", "giant_spider", "orc"),
                E("patrol", "the thorn patrol", "orc", "orc", "kindled_zealot"),
                E("secret", "the secret gate", "orc_warchief", "orc", "orc")),

            new CampaignSitePlan("ember_crown_spire", "Ember Crown Spire",
                "The Ember Crown: Final Ascent",
                Travel("Ember Crown Spire", "Take the secret stair, defeat the last guard, and end the campaign at the crown chamber."),
                2000, 1200, new[] { "thornmaze" }, false, true,
                new Vector3(150f, 0f, 150f), CampaignSiteTheme.Spire,
                E("stair", "the secret stair", "kindled_zealot", "kindled_zealot", "orc"),
                E("guard", "the ember guard", "orc_warchief", "kindled_zealot", "kindled_zealot"),
                E("crown", "the ember crown chamber", "hollow_warden", "orc_warchief", "kindled_zealot"))
        };
    }
}
