using RadiantPool.Rules;
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
            string description, string[] prerequisites,
            bool startsAvailable, bool finalQuest, Vector3 center, CampaignSiteTheme theme,
            params CampaignEncounterPlan[] encounters)
        {
            var reward = CampaignRewardLibrary.Get(zoneId);
            ZoneId = zoneId;
            DisplayName = displayName;
            QuestName = questName;
            Description = description;
            XpEach = reward.QuestXp;
            Gold = reward.Gold;
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
                PrerequisiteZoneIds = PrerequisiteZoneIds,
                StartsAvailable = StartsAvailable,
                FinalQuest = FinalQuest,
                SiteAction = action,
                ChoiceA = choiceA,
                ChoiceB = choiceB
            }.ApplyCampaignReward();
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
                case "duskmire_crossing":
                    action = "Choose how the crossing will be secured.";
                    choiceA = "Evacuate the fenfolk"; choiceB = "Fortify their homes"; return;
                case "whispervault":
                    action = "Settle the oath bound into the lowest crypt.";
                    choiceA = "Release the wardens"; choiceB = "Renew the vigil"; return;
                case "stormglass_foundry":
                    action = "Decide the fate of the stormglass engine.";
                    choiceA = "Repair the engine"; choiceB = "Shatter its heart"; return;
                case "frostvein_pass": action = "Light the three refuge beacons."; return;
                case "hoarfire_halls":
                    action = "Choose who receives the recovered winter stores.";
                    choiceA = "Supply the frontier"; choiceB = "Arm the Council"; return;
                case "winter_crown_vault": action = "Seal the rime fissure beneath the vault."; return;
                case "shattered_coast":
                    action = "Set the terms of the coastward alliance.";
                    choiceA = "Share command"; choiceB = "Guarantee independence"; return;
                case "colossus_road": action = "Free the road crews trapped in the titanworks."; return;
                case "titan_foundry":
                    action = "Choose how the last colossus forge will fall.";
                    choiceA = "Overload the crucible"; choiceB = "Claim the controls"; return;
                case "veil_threshold": action = "Close the three breaches in the dusk veil."; return;
                case "hollow_star_depths":
                    action = "Choose which memory anchors the failing seal.";
                    choiceA = "The city's first dawn"; choiceB = "The party's oath"; return;
                case "dawnspire_nexus":
                    action = "End the Hollow Star and decide the nexus's future.";
                    choiceA = "Seal it forever"; choiceB = "Turn it toward the dawn"; return;
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
                System.Array.Empty<string>(), true, false,
                new Vector3(-150f, 0f, -150f), CampaignSiteTheme.Keep,
                E("watch", "the drowned watch", "risen_drowned", "bonewalker", "bonewalker"),
                E("hall", "the echoing hall", "risen_drowned", "risen_drowned", "bonewalker"),
                E("vault", "the sealed vault", "risen_drowned", "bonewalker", "bonewalker")),

            new CampaignSitePlan("cinderwell_yard", "Cinderwell Yard", "The Gray Knives",
                Travel("Cinderwell Yard", "Break the raider cordon and find the descent they guard."),
                System.Array.Empty<string>(), true, false,
                new Vector3(-100f, 0f, -150f), CampaignSiteTheme.Ruins,
                E("cordon", "the broken cordon", "goblin", "goblin", "marsh_skulker"),
                E("well", "the cinder well", "marsh_skulker", "marsh_skulker", "goblin"),
                E("captain", "the raider captain's yard", "orc", "goblin", "goblin")),

            new CampaignSitePlan("cinderwell_undercroft", "Cinderwell Undercroft",
                "The Gray Knives: Below",
                Travel("the Cinderwell Undercroft", "Clear the crypt road and end the raider lair below."),
                new[] { "cinderwell_yard" }, false, false,
                new Vector3(-50f, 0f, -150f), CampaignSiteTheme.Crypt,
                E("crypt", "the pillaged crypt", "bonewalker", "bonewalker", "goblin"),
                E("passage", "the knife passage", "goblin", "goblin", "orc"),
                E("lair", "the undercroft lair", "orc_warchief", "goblin", "goblin")),

            new CampaignSitePlan("ember_archive", "The Ember Archive",
                "Records of the First Flame",
                Travel("the Ember Archive", "Recover the civic records by defeating the three forces that hold its stacks."),
                new[] { "old_docks" }, false, false,
                new Vector3(0f, 0f, -150f), CampaignSiteTheme.Archive,
                E("stacks", "the shattered stacks", "bonewalker", "bonewalker", "kindled_zealot"),
                E("scriptorium", "the ash scriptorium", "kindled_zealot", "kindled_zealot", "bonewalker"),
                E("records", "the sealed records hall", "risen_drowned", "bonewalker", "kindled_zealot")),

            new CampaignSitePlan("loomhouse_enclave", "The Loomhouse Enclave",
                "The Lost Council Seal",
                Travel("the Loomhouse Enclave", "Open the occupied compound and recover the Council seal from its inner court."),
                new[] { "drowned_bastion" }, false, false,
                new Vector3(50f, 0f, -150f), CampaignSiteTheme.Enclave,
                E("gate", "the loom gate", "marsh_skulker", "marsh_skulker", "orc"),
                E("workshop", "the captive workshop", "orc", "orc", "marsh_skulker"),
                E("seal", "the seal chamber", "orc_warchief", "orc", "marsh_skulker")),

            new CampaignSitePlan("blackbriar_manor", "Blackbriar Manor",
                "Knives in Blackbriar",
                Travel("Blackbriar Manor", "Defeat its patrols, secret guard, and the knife captain in the great room."),
                new[] { "drowned_bastion" }, false, false,
                new Vector3(100f, 0f, -150f), CampaignSiteTheme.Manor,
                E("garden", "the briar garden", "marsh_skulker", "marsh_skulker", "goblin"),
                E("passage", "the hidden passage", "goblin", "orc", "orc"),
                E("captain", "the knife captain's hall", "orc_warchief", "marsh_skulker", "marsh_skulker")),

            new CampaignSitePlan("gilded_quarter", "The Gilded Quarter", "Gold Under Ash",
                Travel("the Gilded Quarter", "Reclaim the three treasure streets and drive out their occupiers."),
                new[] { "blackbriar_manor" }, false, false,
                new Vector3(150f, 0f, -150f), CampaignSiteTheme.Quarter,
                E("promenade", "the ash promenade", "orc", "orc", "marsh_skulker"),
                E("treasury", "the broken treasury", "orc", "orc", "orc"),
                E("estate", "the last gilded estate", "orc_warchief", "orc", "orc")),

            new CampaignSitePlan("emberwild_expanse", "The Emberwild Expanse",
                "The Bound Ember",
                Travel("the Emberwild Expanse", "Follow the three marked trails to recover the lost ember vessel."),
                new[] { "old_docks" }, false, false,
                new Vector3(-150f, 0f, -95f), CampaignSiteTheme.Wilds,
                E("trail", "the scorched trail", "giant_spider", "giant_spider"),
                E("crossing", "the wild crossing", "brown_bear", "goblin", "goblin"),
                E("vessel", "the vessel hollow", "brown_bear", "giant_spider", "giant_spider")),

            new CampaignSitePlan("wild_lairs", "The Wilder Dens", "Marks on the Wild",
                Travel("the Wilder Dens", "Hunt the marked beasts in all three lairs."),
                new[] { "sunken_warcamp" }, false, false,
                new Vector3(-100f, 0f, -95f), CampaignSiteTheme.Wilds,
                E("web", "the webbed den", "giant_spider", "giant_spider", "giant_spider"),
                E("briar", "the bear's briar", "brown_bear", "brown_bear"),
                E("cave", "the marked cave", "goblin", "goblin", "orc")),

            new CampaignSitePlan("reedwind_encampment", "Reedwind Encampment",
                "The Reedwind Accord",
                Travel("Reedwind Encampment", "Defend its outer fires and break the counter-raid threatening the accord."),
                new[] { "ember_archive" }, false, false,
                new Vector3(-50f, 0f, -95f), CampaignSiteTheme.Camp,
                E("fires", "the outer fires", "goblin", "goblin", "orc"),
                E("ford", "the reed ford", "orc", "orc", "goblin"),
                E("raid", "the counter-raid", "orc_warchief", "orc", "orc")),

            new CampaignSitePlan("goblin_delves", "The Gloam Delves", "The Gloam Pact",
                Travel("the Gloam Delves", "Fight through the cave waves and confront the chieftain at the pact stone."),
                new[] { "drowned_market" }, false, false,
                new Vector3(0f, 0f, -95f), CampaignSiteTheme.Caves,
                E("mouth", "the delve mouth", "goblin", "goblin", "goblin"),
                E("pens", "the prisoner pens", "goblin", "goblin", "orc"),
                E("pact", "the pact stone", "orc_warchief", "goblin", "goblin")),

            new CampaignSitePlan("drowned_observatory_approach", "Drowned Observatory Approach",
                "Poison in the Cinderflow: Approach",
                Travel("the Drowned Observatory Approach", "Secure the island landing and open the route into the underworks."),
                new[] { "ember_archive" }, false, false,
                new Vector3(50f, 0f, -95f), CampaignSiteTheme.Observatory,
                E("landing", "the drowned landing", "risen_drowned", "risen_drowned", "bonewalker"),
                E("laboratory", "the outer laboratory", "kindled_zealot", "bonewalker", "bonewalker"),
                E("lift", "the underworks lift", "hollow_warden", "kindled_zealot")),

            new CampaignSitePlan("drowned_observatory_underworks", "Drowned Observatory Underworks",
                "Poison in the Cinderflow: Underworks",
                Travel("the Observatory Underworks", "Shut down the water controls and clear the experiment cells."),
                new[] { "drowned_observatory_approach" }, false, false,
                new Vector3(100f, 0f, -95f), CampaignSiteTheme.Observatory,
                E("controls", "the blackwater controls", "risen_drowned", "bonewalker", "bonewalker"),
                E("cells", "the experiment cells", "risen_drowned", "risen_drowned", "kindled_zealot"),
                E("shaft", "the crown shaft", "hollow_warden", "bonewalker", "kindled_zealot")),

            new CampaignSitePlan("drowned_observatory_crown", "Drowned Observatory Crown",
                "Poison in the Cinderflow: Crown",
                Travel("the Observatory Crown", "Destroy the pollution source and defeat its master at the summit."),
                new[] { "drowned_observatory_underworks" }, false, false,
                new Vector3(150f, 0f, -95f), CampaignSiteTheme.Observatory,
                E("gallery", "the crown gallery", "kindled_zealot", "kindled_zealot", "bonewalker"),
                E("source", "the poisoned source", "risen_drowned", "hollow_warden", "bonewalker"),
                E("summit", "the drowned summit", "hollow_warden", "kindled_zealot", "kindled_zealot")),

            new CampaignSitePlan("mirewatch_citadel", "Mirewatch Citadel",
                "The Mirewatch Choice",
                Travel("Mirewatch Citadel", "Reach the hostages and break the three forces blocking a marsh accord."),
                new[] { "glasslit_temple" }, false, false,
                new Vector3(-150f, 0f, 95f), CampaignSiteTheme.Marsh,
                E("causeway", "the mire causeway", "marsh_skulker", "marsh_skulker", "giant_spider"),
                E("hostages", "the hostage court", "orc", "marsh_skulker", "marsh_skulker"),
                E("undercroft", "the mire undercroft", "hollow_warden", "marsh_skulker", "marsh_skulker")),

            new CampaignSitePlan("tidebreaker_anchorage", "Tidebreaker Anchorage",
                "The Tidebreaker Ransom",
                Travel("Tidebreaker Anchorage", "Storm the shipyard, find the captive heir, and break the ransom guard."),
                new[] { "drowned_bastion" }, false, false,
                new Vector3(-100f, 0f, 95f), CampaignSiteTheme.Anchorage,
                E("shipyard", "the chained shipyard", "marsh_skulker", "marsh_skulker", "orc"),
                E("stockade", "the ransom stockade", "orc", "orc", "marsh_skulker"),
                E("flagship", "the grounded flagship", "orc_warchief", "orc", "marsh_skulker")),

            new CampaignSitePlan("iron_concord_redoubt", "Iron Concord Redoubt",
                "Envoy to the Iron Concord",
                Travel("the Iron Concord Redoubt", "Survive the double-cross and recover the commandant's evidence."),
                new[] { "loomhouse_enclave" }, false, false,
                new Vector3(-50f, 0f, 95f), CampaignSiteTheme.Redoubt,
                E("embassy", "the broken embassy", "orc", "orc", "kindled_zealot"),
                E("barracks", "the iron barracks", "orc", "orc", "orc"),
                E("command", "the commandant's chamber", "orc_warchief", "orc", "kindled_zealot")),

            new CampaignSitePlan("lanternfall_necropolis", "Lanternfall Necropolis",
                "The Lanternfall Contract",
                Travel("Lanternfall Necropolis", "Fulfil the scaling contract by clearing its three sealed burial grounds."),
                new[] { "ember_archive", "drowned_market", "blackbriar_manor", "glasslit_temple" },
                false, false, new Vector3(0f, 0f, 95f), CampaignSiteTheme.Necropolis,
                E("field", "the lantern field", "bonewalker", "bonewalker", "risen_drowned"),
                E("crypt", "the sealed crypt", "bonewalker", "bonewalker", "bonewalker", "risen_drowned"),
                E("mausoleum", "the black mausoleum", "hollow_warden", "bonewalker", "bonewalker")),

            new CampaignSitePlan("cinder_gate", "Cinder Gate", "Take the Cinder Gate",
                Travel("Cinder Gate", "Capture both towers and defeat the force holding the gatehouse."),
                new[] { "iron_concord_redoubt" }, false, false,
                new Vector3(50f, 0f, 95f), CampaignSiteTheme.Gate,
                E("west", "the west tower", "orc", "orc", "kindled_zealot"),
                E("east", "the east tower", "orc", "orc", "kindled_zealot"),
                E("gatehouse", "the cinder gatehouse", "orc_warchief", "orc", "orc", "kindled_zealot")),

            new CampaignSitePlan("crownless_citadel", "The Crownless Citadel",
                "The Ember Crown: Citadel",
                Travel("the Crownless Citadel", "Breach the alarm court and clear the elite barracks beyond it."),
                new[] { "cinder_gate" }, false, false,
                new Vector3(100f, 0f, 95f), CampaignSiteTheme.Citadel,
                E("alarm", "the alarm court", "orc", "orc", "orc", "kindled_zealot"),
                E("barracks", "the giant barracks", "brown_bear", "orc", "orc"),
                E("keep", "the crownless keep", "orc_warchief", "orc", "kindled_zealot")),

            new CampaignSitePlan("thornmaze", "The Thornmaze", "The Ember Crown: Thornmaze",
                Travel("the Thornmaze", "Find the true route and defeat the elite guard at its secret gate."),
                new[] { "crownless_citadel" }, false, false,
                new Vector3(150f, 0f, 95f), CampaignSiteTheme.Maze,
                E("false", "the false route", "giant_spider", "giant_spider", "orc"),
                E("patrol", "the thorn patrol", "orc", "orc", "kindled_zealot"),
                E("secret", "the secret gate", "orc_warchief", "orc", "orc")),

            new CampaignSitePlan("ember_crown_spire", "Ember Crown Spire",
                "The Ember Crown: Final Ascent",
                Travel("Ember Crown Spire", "Take the secret stair, defeat the last guard, and end the campaign at the crown chamber."),
                new[] { "thornmaze" }, false, false,
                new Vector3(150f, 0f, 150f), CampaignSiteTheme.Spire,
                E("stair", "the secret stair", "kindled_zealot", "kindled_zealot", "orc"),
                E("guard", "the ember guard", "orc_warchief", "kindled_zealot", "kindled_zealot"),
                E("crown", "the ember crown chamber", "hollow_warden", "orc_warchief", "kindled_zealot")),

            new CampaignSitePlan("duskmire_crossing", "Duskmire Crossing",
                "The Stormglass Engine: Crossing",
                Travel("Duskmire Crossing", "Investigate the vanished caravans, defend the fenfolk, and open the road to the Whispervault."),
                new[] { "ember_crown_spire" }, false, false,
                new Vector3(-150f, 0f, 205f), CampaignSiteTheme.Marsh,
                E("trail", "the ashfang trail", "ashfang_stalker", "ashfang_stalker", "ashfang_stalker"),
                E("bridge", "the broken bridge", "ironbound_veteran", "ironbound_veteran", "ashfang_stalker"),
                E("crossing", "the flooded crossing", "mire_troll", "ashfang_stalker", "ashfang_stalker")),

            new CampaignSitePlan("whispervault", "The Whispervault",
                "The Stormglass Engine: Whispervault",
                Travel("the Whispervault", "Descend through the speaking crypts and discover who awakened their oathbound dead."),
                new[] { "duskmire_crossing" }, false, false,
                new Vector3(-100f, 0f, 205f), CampaignSiteTheme.Crypt,
                E("vestibule", "the oathbound vestibule", "ironbound_veteran", "veil_adept", "grave_wraith"),
                E("choir", "the whispering choir", "grave_wraith", "grave_wraith", "ironbound_veteran"),
                E("seal", "the lowest seal", "grave_wraith", "mire_troll", "ironbound_veteran")),

            new CampaignSitePlan("stormglass_foundry", "Stormglass Foundry",
                "The Stormglass Engine: Foundry",
                Travel("Stormglass Foundry", "Breach the furnace galleries and stop the storm engine before it drowns the eastern road."),
                new[] { "whispervault" }, false, false,
                new Vector3(-50f, 0f, 205f), CampaignSiteTheme.Redoubt,
                E("gate", "the thunder gate", "ironbound_veteran", "ironbound_veteran", "storm_magus"),
                E("furnace", "the storm furnace", "mire_troll", "storm_magus", "ironbound_veteran"),
                E("engine", "the stormglass engine", "storm_magus", "storm_magus", "veil_adept")),

            new CampaignSitePlan("frostvein_pass", "Frostvein Pass",
                "The Frostbound Crown: Pass",
                Travel("Frostvein Pass", "Relight the refuge beacons and break the reavers hunting the mountain road."),
                new[] { "stormglass_foundry" }, false, false,
                new Vector3(0f, 0f, 205f), CampaignSiteTheme.Wilds,
                E("beacon", "the buried beacon", "frost_reaver", "frost_reaver", "ironbound_veteran"),
                E("shelf", "the windcut shelf", "storm_magus", "frost_reaver", "ironbound_veteran"),
                E("summit", "the pass summit", "mire_troll", "storm_magus", "frost_reaver")),

            new CampaignSitePlan("hoarfire_halls", "Hoarfire Halls",
                "The Frostbound Crown: Halls",
                Travel("Hoarfire Halls", "Search the frozen feast halls, rescue the quartermasters, and end the giant occupation."),
                new[] { "frostvein_pass" }, false, false,
                new Vector3(50f, 0f, 205f), CampaignSiteTheme.Citadel,
                E("feast", "the frozen feast hall", "cinder_giant", "frost_reaver"),
                E("stores", "the winter stores", "storm_magus", "cinder_giant"),
                E("throne", "the hoarfire throne", "cinder_giant", "frost_reaver")),

            new CampaignSitePlan("winter_crown_vault", "Winter Crown Vault",
                "The Frostbound Crown: Vault",
                Travel("the Winter Crown Vault", "Cross the rime machinery and seal the fissure beneath the crown chamber."),
                new[] { "hoarfire_halls" }, false, false,
                new Vector3(100f, 0f, 205f), CampaignSiteTheme.Caves,
                E("gallery", "the rime gallery", "stone_colossus", "frost_reaver"),
                E("gears", "the frozen gears", "stone_colossus", "storm_magus"),
                E("fissure", "the crown fissure", "stone_colossus", "ironbound_veteran")),

            new CampaignSitePlan("shattered_coast", "The Shattered Coast",
                "The Titan's Chain: Coast",
                Travel("the Shattered Coast", "Rally its isolated watchposts and uncover the road feeding the titan foundry."),
                new[] { "winter_crown_vault" }, false, false,
                new Vector3(-150f, 0f, 260f), CampaignSiteTheme.Anchorage,
                E("watch", "the drowned watchpost", "storm_magus", "frost_reaver"),
                E("causeway", "the shattered causeway", "stone_colossus", "frost_reaver"),
                E("signal", "the coastward signal", "cinder_giant", "ironbound_veteran")),

            new CampaignSitePlan("colossus_road", "Colossus Road",
                "The Titan's Chain: Road",
                Travel("Colossus Road", "Break the marching constructs and free the crews forced to repair the titanworks."),
                new[] { "shattered_coast" }, false, false,
                new Vector3(-100f, 0f, 260f), CampaignSiteTheme.Ruins,
                E("march", "the stone march", "stone_colossus", "ironbound_veteran"),
                E("quarry", "the chained quarry", "stone_colossus", "storm_magus"),
                E("road", "the titan road", "cinder_giant", "ironbound_veteran")),

            new CampaignSitePlan("titan_foundry", "Titan Foundry",
                "The Titan's Chain: Foundry",
                Travel("Titan Foundry", "Sabotage the crucible lines and defeat the regent commanding the last colossus forge."),
                new[] { "colossus_road" }, false, false,
                new Vector3(-50f, 0f, 260f), CampaignSiteTheme.Redoubt,
                E("crucible", "the outer crucible", "cinder_giant", "ironbound_veteran"),
                E("forge", "the titan forge", "stone_colossus", "frost_reaver"),
                E("regent", "the regent's dais", "night_regent")),

            new CampaignSitePlan("veil_threshold", "The Veil Threshold",
                "The Hollow Star: Threshold",
                Travel("the Veil Threshold", "Close the dusk breaches and find the descent followed by the vanished vanguard."),
                new[] { "titan_foundry" }, false, false,
                new Vector3(0f, 0f, 260f), CampaignSiteTheme.Necropolis,
                E("breach", "the first breach", "grave_wraith", "storm_magus", "veil_adept"),
                E("court", "the lightless court", "grave_wraith", "grave_wraith", "storm_magus"),
                E("descent", "the veiled descent", "storm_magus", "storm_magus", "veil_adept")),

            new CampaignSitePlan("hollow_star_depths", "Hollow Star Depths",
                "The Hollow Star: Depths",
                Travel("the Hollow Star Depths", "Recover the vanguard's memory seals and cross the impossible halls beneath the city."),
                new[] { "veil_threshold" }, false, false,
                new Vector3(50f, 0f, 260f), CampaignSiteTheme.Maze,
                E("memory", "the stolen memory", "grave_wraith", "storm_magus", "veil_adept"),
                E("mirror", "the inverted gallery", "grave_wraith", "grave_wraith", "storm_magus"),
                E("anchor", "the memory anchor", "storm_magus", "storm_magus", "veil_adept")),

            new CampaignSitePlan("dawnspire_nexus", "Dawnspire Nexus",
                "The Hollow Star: Last Dawn",
                Travel("Dawnspire Nexus", "Defeat the night regent, break the starbound guardian, and end the Hollow Star at the nexus."),
                new[] { "hollow_star_depths" }, false, true,
                new Vector3(100f, 0f, 260f), CampaignSiteTheme.Spire,
                E("regent", "the last regent", "night_regent"),
                E("guardian", "the nexus guardian", "starbound_juggernaut"),
                E("star", "the hollow star", "hollow_star_lich"))
        };
    }
}
