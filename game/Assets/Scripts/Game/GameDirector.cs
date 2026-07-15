using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    public enum QuestState { Locked = 0, Active = 1, ObjectivesMet = 2, Completed = 3 }

    /// <summary>Server-owned campaign state for the full first region: the muster quest
    /// plus one zone-clearing quest per zone, in a fixed chain (docks → market → temple).
    /// Zone data mirrors /content/zones + /content/quests JSON and is wired by the
    /// bootstrap. Gold, stash, XP awards, saves, and the journal all live here.</summary>
    public class GameDirector : NetworkBehaviour
    {
        public static GameDirector Instance { get; private set; }

        [System.Serializable]
        public class ZoneConfig
        {
            public string ZoneId = "";
            public string DisplayName = "";
            public string QuestName = "";
            /// <summary>Journal text: what to do and WHERE to go (compass direction).</summary>
            public string Description = "";
            public int RequiredEncounters = 3;
            public int XpEach = 300;
            public int Gold = 100;
            /// <summary>Location-tiered equipment parcel paid once at turn-in.</summary>
            public string RewardLootTable = "";
            /// <summary>Location ids that must be turned in before this commission can
            /// activate. Empty is valid only for an explicitly available opening quest.</summary>
            public string[] PrerequisiteZoneIds = System.Array.Empty<string>();
            public bool StartsAvailable;
            public bool FinalQuest;
            /// <summary>Optional non-combat finale performed on site after its required
            /// encounters: recovery, rescue, control, evidence, or an alliance choice.</summary>
            public string SiteAction = "";
            public string ChoiceA = "";
            public string ChoiceB = "";
            /// <summary>Only separate side quests layered onto this location use these.
            /// The normal on-site objective remains part of the main turn-in reward.</summary>
            public int SiteActionXp;
            public int SiteActionGold;
            public string SiteActionLootTable = "";

            public ZoneConfig ApplyCampaignReward()
            {
                var reward = CampaignRewardLibrary.Get(ZoneId);
                XpEach = reward.QuestXp;
                Gold = reward.Gold;
                RewardLootTable = reward.LootTable;
                SiteActionXp = reward.ActionXp;
                SiteActionGold = reward.ActionGold;
                SiteActionLootTable = reward.ActionLootTable;
                return this;
            }
        }

        [Header("Zone chain (from content JSON, wired by bootstrap)")]
        public ZoneConfig[] Zones = System.Array.Empty<ZoneConfig>();

        public readonly SyncVar<int> MusterState = new SyncVar<int>((int)QuestState.Active);
        public readonly SyncList<int> ZoneStates = new SyncList<int>();
        public readonly SyncList<int> ZoneClearedCounts = new SyncList<int>();
        public readonly SyncVar<bool> CampaignComplete = new SyncVar<bool>(false);
        public readonly SyncVar<int> PartyGold = new SyncVar<int>(0);
        /// <summary>Authoritative host-computer local time as a fractional 24-hour clock.
        /// Clients derive presentation from this value so every co-op player sees the
        /// same sunlight, even if their own computers use different time zones.</summary>
        public readonly SyncVar<float> WorldHour = new SyncVar<float>(20.5f);

        /// <summary>Party-shared loot stash (item ids from content/items).</summary>
        public readonly SyncList<string> Stash = new SyncList<string>();

        /// <summary>Compact `name|class|active` records for the recruitment UI. The full
        /// sheets and loadouts remain server-only; clients only need to list who can be
        /// released or rehired.</summary>
        public readonly SyncList<string> CompanionRoster = new SyncList<string>();

        /// <summary>Sell value in gold = half list price (items.json costCp / 100 / 2).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> SellValue =
            new System.Collections.Generic.Dictionary<string, int>
        {
            { "dagger", 1 }, { "shortsword", 5 }, { "longsword", 7 }, { "mace", 2 },
            { "quarterstaff", 1 }, { "runed_staff", 5 }, { "light_crossbow", 12 }, { "shortbow", 12 },
            { "rapier", 12 }, { "warhammer", 7 }, { "greatsword", 25 }, { "greataxe", 15 },
            { "longbow", 25 },
            { "leather_armor", 5 }, { "studded_leather", 22 }, { "scale_mail", 25 },
            { "half_plate", 375 }, { "chain_mail", 37 }, { "splint", 100 },
            { "shield", 5 }, { "potion_healing", 25 }, { "torch", 1 }
        };
        public const int PotionBuyPrice = 50;

        /// <summary>Smith stock: SRD list price in gold (sell-back via the Exchange is
        /// half). Order here is the order shown in the smith UI.</summary>
        public static readonly System.Collections.Generic.List<(string id, int price)> SmithStock =
            new System.Collections.Generic.List<(string, int)>
        {
            ("dagger", 2), ("mace", 5), ("shortsword", 10), ("longsword", 15),
            ("warhammer", 15), ("rapier", 25), ("greataxe", 30), ("greatsword", 50),
            ("shortbow", 25), ("light_crossbow", 25), ("longbow", 50),
            ("leather_armor", 10), ("studded_leather", 45), ("scale_mail", 50),
            ("chain_mail", 75), ("splint", 200), ("half_plate", 750), ("shield", 10)
        };

        public string LocalNotice { get; private set; } = "";
        private float _noticeUntil;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public QuestState GetZoneState(int index) =>
            index >= 0 && index < ZoneStates.Count
                ? (QuestState)ZoneStates[index] : QuestState.Locked;

        // ---------------- server ----------------

        private readonly System.Collections.Generic.Dictionary<string, RadiantPool.Rules.CharacterSheet>
            _roster = new System.Collections.Generic.Dictionary<string, RadiantPool.Rules.CharacterSheet>();
        private readonly System.Collections.Generic.Dictionary<string, CharacterBuild>
            _builds = new System.Collections.Generic.Dictionary<string, CharacterBuild>();
        private readonly System.Collections.Generic.Dictionary<string, SavedCompanion>
            _companions = new System.Collections.Generic.Dictionary<string, SavedCompanion>();
        private bool _restoreActiveCompanions;
        private float _nextCompanionRestore;
        private float _serverWorldHour = 20.5f;
        private float _nextWorldTimeSync;
        private bool _allowWorldTimeOverride;
        private bool _worldTimeOverride;
        public bool ComputerClockActive => !_worldTimeOverride;
        /// <summary>Cleared encounter ids, replicated so clients can point quest markers
        /// at the nearest block that still needs fighting.</summary>
        public readonly SyncList<string> ConsumedEncounterIds = new SyncList<string>();
        public readonly SyncList<string> CompletedSiteActions = new SyncList<string>();

        public bool IsSiteActionComplete(int zone)
        {
            if (zone < 0 || zone >= Zones.Length || string.IsNullOrEmpty(Zones[zone].SiteAction))
                return true;
            string prefix = Zones[zone].ZoneId + "|";
            return CompletedSiteActions.Any(a => a.StartsWith(prefix));
        }

        public string SiteActionResult(int zone)
        {
            if (zone < 0 || zone >= Zones.Length) return "";
            string prefix = Zones[zone].ZoneId + "|";
            string entry = CompletedSiteActions.FirstOrDefault(a => a.StartsWith(prefix));
            return entry == null ? "" : entry.Substring(prefix.Length);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            var args = System.Environment.GetCommandLineArgs();
            int questLampCapture = System.Array.IndexOf(args, "-questlampcapture");
            int nextQuestCapture = System.Array.IndexOf(args, "-nextquestcapture");
            int siteCapture = System.Array.IndexOf(args, "-sitecapture");
            int worldMapCapture = System.Array.IndexOf(args, "-worldmapcapture");
            _allowWorldTimeOverride = System.Array.IndexOf(args, "-atmospheretest") >= 0
                                      || System.Array.IndexOf(args, "-atmospherecapture") >= 0
                                      || questLampCapture >= 0 || siteCapture >= 0;
            SyncFromComputerClock();
            Debug.Log($"[Atmosphere] host computer clock " +
                      $"{System.DateTime.Now:HH:mm:ss} ({System.TimeZoneInfo.Local.Id})");
            for (int i = 0; i < Zones.Length; i++)
            {
                ZoneStates.Add((int)QuestState.Locked);
                ZoneClearedCounts.Add(0);
            }

            if (System.Array.IndexOf(args, "-selltest") >= 0)
                StartCoroutine(ServerSellSelfTest());
            if (System.Array.IndexOf(args, "-leveltest") >= 0)
                StartCoroutine(ServerLevelSelfTest());
            if (System.Array.IndexOf(args, "-warpsmith") >= 0)
                StartCoroutine(ServerWarpToSmith());
            if (System.Array.IndexOf(args, "-attacktest") >= 0)
                StartCoroutine(AttackSelfTest());
            if (System.Array.IndexOf(args, "-combatflowtest") >= 0)
                StartCoroutine(CombatFlowSelfTest());
            if (System.Array.IndexOf(args, "-weapontest") >= 0)
                StartCoroutine(WeaponVisualSelfTest());
            if (System.Array.IndexOf(args, "-recruittest") >= 0)
                StartCoroutine(SoloRecruitmentSelfTest());
            if (System.Array.IndexOf(args, "-recruitrestoretest") >= 0)
                StartCoroutine(RecruitmentRestoreSelfTest());
            if (System.Array.IndexOf(args, "-traveltest") >= 0)
                StartCoroutine(CampaignTravelSelfTest());
            if (System.Array.IndexOf(args, "-scalingtest") >= 0)
                StartCoroutine(EncounterScalingSelfTest());
            if (System.Array.IndexOf(args, "-siteactiontest") >= 0)
                StartCoroutine(SiteActionInputSelfTest());
            if (questLampCapture >= 0 && questLampCapture + 1 < args.Length)
                StartCoroutine(QuestLampCapture(args[questLampCapture + 1]));
            if (nextQuestCapture >= 0 && nextQuestCapture + 1 < args.Length)
                StartCoroutine(NextQuestCapture(args[nextQuestCapture + 1]));
            if (siteCapture >= 0 && siteCapture + 1 < args.Length)
                StartCoroutine(CampaignSiteCapture(args[siteCapture + 1]));
            if (worldMapCapture >= 0 && worldMapCapture + 1 < args.Length)
                StartCoroutine(WorldMapCapture(args[worldMapCapture + 1]));

            if (!SaveSystem.Exists) return;
            var save = SaveSystem.Read();
            if (save == null) return;

            MusterState.Value = save.MusterState;
            for (int i = 0; i < Zones.Length && i < save.ZoneStates.Count; i++)
            {
                ZoneStates[i] = save.ZoneStates[i];
                // Older/shorter saves must not throw; the recount below fixes the counts anyway.
                if (i < save.ZoneClearedCounts.Count)
                    ZoneClearedCounts[i] = save.ZoneClearedCounts[i];
            }
            CampaignComplete.Value = save.CampaignComplete;
            PartyGold.Value = save.PartyGold;
            Stash.Clear();
            foreach (var s in save.Stash) Stash.Add(s);
            foreach (var saved in save.Roster)
            {
                _roster[saved.Name.ToLowerInvariant()] = SaveSystem.Restore(saved);
                _builds[saved.Name.ToLowerInvariant()] = new CharacterBuild
                {
                    ClassIndex = saved.ClassIndex, RaceIndex = saved.RaceIndex,
                    Str = saved.Str, Dex = saved.Dex, Con = saved.Con,
                    Int = saved.Int, Wis = saved.Wis, Cha = saved.Cha
                };
            }
            foreach (var saved in save.Companions
                         ?? new System.Collections.Generic.List<SavedCompanion>())
            {
                if (saved?.Character == null || string.IsNullOrWhiteSpace(saved.Character.Name))
                    continue;
                _companions[saved.Character.Name.ToLowerInvariant()] = saved;
            }
            RefreshCompanionRoster();
            _restoreActiveCompanions = _companions.Values.Any(c => c.Active);
            foreach (var id in save.ConsumedEncounters)
            {
                ConsumedEncounterIds.Add(id);
                var trigger = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                    .FirstOrDefault(t => t.EncounterId == id);
                if (trigger != null) trigger.Consume();
            }
            CompletedSiteActions.Clear();
            foreach (var action in save.CompletedSiteActions
                         ?? new System.Collections.Generic.List<string>())
                if (!string.IsNullOrWhiteSpace(action)) CompletedSiteActions.Add(action);
            string savedDate = save.SavedAtUtc.Length >= 10
                ? save.SavedAtUtc.Substring(0, 10) : save.SavedAtUtc;
            RpcNotice($"Campaign loaded (saved {savedDate}).");
            ServerUnlockAppendedCampaign(save);
            // Self-heal, in this order:
            //   1. Recount every zone from the consumed list. Saves written before the
            //      count/save ordering fix persisted counts that lagged the consumed
            //      encounters, which left zones demanding fights that were already gone.
            //   2. Recheck, which also covers zones cleared before the quest went Active.
            for (int i = 0; i < Zones.Length; i++) ServerRecountZone(i);
            for (int i = 0; i < Zones.Length; i++) ServerRecheckZone(i);
            ServerSaveCampaign();   // persist the repair so it happens once
        }

        private CampaignNode[] CampaignNodes() => Zones.Select(z => new CampaignNode(
            z.ZoneId, z.PrerequisiteZoneIds, z.StartsAvailable, z.FinalQuest)).ToArray();

        /// <summary>Activate every newly eligible commission. Multiple quests can open at
        /// once; the old N-to-N+1 array assumption could not represent council choices,
        /// optional branches, or sites requiring more than one prior success.</summary>
        [Server]
        private System.Collections.Generic.List<int> ServerActivateEligibleZones()
        {
            if ((QuestState)MusterState.Value != QuestState.Completed)
                return new System.Collections.Generic.List<int>();

            var completed = new System.Collections.Generic.List<string>();
            var started = new System.Collections.Generic.List<string>();
            for (int i = 0; i < Zones.Length; i++)
            {
                var state = GetZoneState(i);
                if (state == QuestState.Completed) completed.Add(Zones[i].ZoneId);
                if (state != QuestState.Locked) started.Add(Zones[i].ZoneId);
            }

            var eligible = new System.Collections.Generic.HashSet<string>(
                CampaignGraph.Eligible(CampaignNodes(), completed, started));
            var activated = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Zones.Length; i++)
            {
                if (!eligible.Contains(Zones[i].ZoneId)) continue;
                ZoneStates[i] = (int)QuestState.Active;
                activated.Add(i);
            }
            if (activated.Count > 0) CampaignComplete.Value = false;
            return activated;
        }

        /// <summary>Content updates may append branches after a save's former finale.
        /// Re-evaluate the graph on every load so a completed campaign receives every new
        /// eligible commission without assuming its position in the serialized array.</summary>
        [Server]
        private void ServerUnlockAppendedCampaign(CampaignSave save)
        {
            var activated = ServerActivateEligibleZones();
            if (activated.Count == 0) return;
            string quests = string.Join(", ", activated.Select(i => Zones[i].QuestName));
            if (save.CampaignComplete)
                Debug.Log($"[CampaignMigration] PASS - completed {save.ZoneStates.Count}-zone " +
                          $"save unlocked appended quest '{quests}'");
            RpcNotice(activated.Count == 1
                ? $"A new Council commission is ready: {quests}. Follow the gold waypoint."
                : $"New Council commissions are ready: {quests}. Check the journal and gold waypoint.");
        }

        [Server]
        public void ServerSetWorldHourForTest(float hour)
        {
            if (!_allowWorldTimeOverride)
            {
                Debug.LogWarning("[Atmosphere] refused time override outside an atmosphere test");
                return;
            }
            _worldTimeOverride = true;
            _serverWorldHour = Mathf.Repeat(hour, 24f);
            WorldHour.Value = _serverWorldHour;
            _nextWorldTimeSync = Time.unscaledTime + 0.25f;
        }

        [Server]
        public void ServerClearWorldHourTestOverride()
        {
            _worldTimeOverride = false;
            SyncFromComputerClock();
        }

        /// <summary>Fractional local wall-clock hour, including minutes, seconds, and
        /// daylight-saving/time-zone changes already applied by the operating system.</summary>
        public static float ComputerLocalHourNow()
        {
            var now = System.DateTime.Now.TimeOfDay;
            return (float)now.TotalHours;
        }

        private void SyncFromComputerClock()
        {
            _serverWorldHour = ComputerLocalHourNow();
            WorldHour.Value = _serverWorldHour;
            _nextWorldTimeSync = Time.unscaledTime + 0.25f;
        }

        [Server]
        public RadiantPool.Rules.CharacterSheet ServerGetOrCreateSheet(string name, CharacterBuild build)
        {
            string key = name.ToLowerInvariant();
            if (_roster.TryGetValue(key, out var existing))
            {
                RpcNotice($"{existing.Name} rejoins the party.");
                return existing;
            }
            var sheet = PlayerCharacterHolder.CreateSheetFromBuild(name, build);
            _roster[key] = sheet;
            _builds[key] = build;
            return sheet;
        }

        [Server]
        public void ServerSaveCampaign()
        {
            var save = new CampaignSave
            {
                MusterState = MusterState.Value,
                ZoneStates = ZoneStates.ToList(),
                ZoneClearedCounts = ZoneClearedCounts.ToList(),
                CampaignComplete = CampaignComplete.Value,
                PartyGold = PartyGold.Value,
                Stash = Stash.ToList(),
                ConsumedEncounters = ConsumedEncounterIds.ToList(),
                CompletedSiteActions = CompletedSiteActions.ToList(),
                Roster = _roster.Select(kv =>
                    SaveSystem.Capture(kv.Value, _builds[kv.Key])).ToList(),
                Companions = ServerCaptureCompanions()
            };
            SaveSystem.Write(save);
        }

        [Server]
        private System.Collections.Generic.List<SavedCompanion> ServerCaptureCompanions()
        {
            foreach (var holder in FindObjectsByType<PlayerCharacterHolder>(
                         FindObjectsSortMode.None).Where(p => p.IsCompanion && p.Sheet != null))
            {
                string key = holder.Sheet.Name.ToLowerInvariant();
                // Despawn is synchronized immediately but Unity may keep the object until
                // end-of-frame. A just-released record is authoritative during that gap.
                if (_companions.TryGetValue(key, out var existing) && !existing.Active) continue;
                _companions[key] = SaveSystem.CaptureCompanion(holder, active: true);
            }
            RefreshCompanionRoster();
            return _companions.Values.OrderBy(c => c.Character.Name).ToList();
        }

        [Server]
        private void RefreshCompanionRoster()
        {
            CompanionRoster.Clear();
            foreach (var saved in _companions.Values.OrderBy(c => c.Character.Name))
                CompanionRoster.Add($"{saved.Character.Name}|{saved.Character.ClassIndex}|" +
                                    $"{(saved.Active ? "1" : "0")}");
        }

        public static bool TryParseCompanionSummary(string summary, out string name,
            out CharacterClass characterClass, out bool active)
        {
            name = "";
            characterClass = CharacterClass.Fighter;
            active = false;
            var parts = (summary ?? "").Split('|');
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0])
                || !int.TryParse(parts[1], out int classIndex) || classIndex < 0 || classIndex > 3)
                return false;
            name = parts[0];
            characterClass = (CharacterClass)classIndex;
            active = parts[2] == "1";
            return true;
        }

        [Server]
        public void ServerEncounterCleared(EncounterTrigger trigger)
        {
            if (trigger == null) return;
            int zone = System.Array.FindIndex(Zones, z => z.ZoneId == trigger.ZoneId);
            trigger.Consume();
            ConsumedEncounterIds.Add(trigger.EncounterId);

            // Recount, THEN recheck, THEN save. The old order saved before crediting the
            // clear, so every autosave persisted a count one behind the consumed list;
            // reloading restored the stale count while the fight stayed consumed, and the
            // credit was gone for good — a zone could end up demanding fights that no
            // longer existed.
            if (zone >= 0 && trigger.RequiredForClear)
            {
                ServerRecountZone(zone);
                var cfg = Zones[zone];
                ServerRecheckZone(zone);
                if (GetZoneState(zone) != QuestState.ObjectivesMet)
                    RpcNotice($"Encounter cleared ({ZoneClearedCounts[zone]}" +
                              $"/{cfg.RequiredEncounters} in {cfg.DisplayName}).");
            }
            ServerSaveCampaign();   // autosave at every cleared block
        }

        /// <summary>Cleared-count is DERIVED, never accumulated: it is simply how many of
        /// the zone's required encounters are in ConsumedEncounterIds. The consumed list is
        /// the one thing that is always true (the trigger is physically gone), so counting
        /// from it cannot drift, and recounting on load repairs any save that already has.</summary>
        [Server]
        private void ServerRecountZone(int zone)
        {
            if (zone < 0 || zone >= Zones.Length || zone >= ZoneClearedCounts.Count) return;
            string zoneId = Zones[zone].ZoneId;
            int cleared = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                .Count(t => t.ZoneId == zoneId && t.RequiredForClear
                            && ConsumedEncounterIds.Contains(t.EncounterId));
            if (ZoneClearedCounts[zone] != cleared) ZoneClearedCounts[zone] = cleared;
        }

        /// <summary>Flips an Active zone to ObjectivesMet once enough encounters are
        /// cleared. Called both on encounter clears AND whenever a quest becomes Active:
        /// encounters can be fought before the quest is accepted, and without this
        /// recheck such a zone could never complete (no triggers left to fire it).</summary>
        [Server]
        private void ServerRecheckZone(int zone)
        {
            if (zone < 0 || zone >= Zones.Length) return;
            var cfg = Zones[zone];
            if (GetZoneState(zone) == QuestState.Active
                && ZoneClearedCounts[zone] >= cfg.RequiredEncounters
                && IsSiteActionComplete(zone))
            {
                ZoneStates[zone] = (int)QuestState.ObjectivesMet;
                RpcNotice($"{cfg.DisplayName} has been cleared! Follow the gold marker to " +
                          "Council Hall and speak with Councilor Veresk to turn it in.");
            }
        }

        [Header("Companions (prefab wired by bootstrap)")]
        public FishNet.Object.NetworkObject CompanionPrefab;

        private static readonly string[] CompanionNames =
        {
            "Aldric", "Berthold", "Cedrik", "Eadric", "Giselle", "Godwin",
            "Hamond", "Hilda", "Isolde", "Leofric", "Mabel", "Osric",
            "Oswin", "Rowena", "Sybilla", "Thurstan", "Wulfric", "Ysolde"
        };

        private PlayerCharacterHolder RecruitingLeader(NetworkConnection conn)
        {
            var players = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(h => !h.IsCompanion && h.Sheet != null).ToArray();
            if (conn != null && conn.IsValid)
                return players.FirstOrDefault(h => h.Owner == conn);
            return players.OrderByDescending(h => h.Sheet.Level)
                .ThenByDescending(h => h.Sheet.Xp).FirstOrDefault();
        }

        private static bool RecruiterNear(PlayerCharacterHolder leader)
        {
            return leader != null && FindObjectsByType<NpcInteract>(FindObjectsSortMode.None)
                .Any(n => Vector3.Distance(leader.transform.position, n.transform.position)
                          <= n.InteractRange + 0.75f);
        }

        private string NextCompanionName()
        {
            var used = new System.Collections.Generic.HashSet<string>(
                _companions.Values.Select(c => c.Character.Name),
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var holder in FindObjectsByType<PlayerCharacterHolder>(
                         FindObjectsSortMode.None).Where(h => h.Sheet != null))
                used.Add(holder.Sheet.Name);
            var available = CompanionNames.Where(n => !used.Contains(n)).ToArray();
            if (available.Length > 0)
                return available[new System.Random().Next(available.Length)];
            int suffix = 1;
            while (used.Contains($"Sellsword {suffix}")) suffix++;
            return $"Sellsword {suffix}";
        }

        [Server]
        private PlayerCharacterHolder ServerSpawnNewCompanion(CharacterClass characterClass,
            PlayerCharacterHolder leader)
        {
            int activeCount = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Count(p => p.Sheet != null);
            if (leader == null || activeCount >= PartyComposition.MaxPartySize) return null;

            string name = NextCompanionName();
            var nob = Instantiate(CompanionPrefab,
                leader.transform.position + new Vector3(1.5f + activeCount * 0.55f, 0.2f, -1.5f),
                Quaternion.identity);
            var holder = nob.GetComponent<PlayerCharacterHolder>();
            holder.ServerInitCompanion(name, (int)characterClass);
            ServerMatchCompanionToLeader(holder, leader);
            var identity = nob.GetComponent<PlayerIdentity>();
            if (identity != null) identity.ServerSetName(name);
            FishNet.InstanceFinder.ServerManager.Spawn(nob);
            _companions[name.ToLowerInvariant()] = SaveSystem.CaptureCompanion(holder, active: true);
            RefreshCompanionRoster();
            return holder;
        }

        [Server]
        private PlayerCharacterHolder ServerSpawnSavedCompanion(SavedCompanion saved,
            PlayerCharacterHolder leader)
        {
            int activeCount = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Count(p => p.Sheet != null);
            if (saved?.Character == null || leader == null
                || activeCount >= PartyComposition.MaxPartySize) return null;

            saved.Active = true;
            var nob = Instantiate(CompanionPrefab,
                leader.transform.position + new Vector3(1.5f + activeCount * 0.55f, 0.2f, -1.5f),
                Quaternion.identity);
            var holder = nob.GetComponent<PlayerCharacterHolder>();
            holder.ServerRestoreCompanion(saved);
            var identity = nob.GetComponent<PlayerIdentity>();
            if (identity != null) identity.ServerSetName(saved.Character.Name);
            FishNet.InstanceFinder.ServerManager.Spawn(nob);
            _companions[saved.Character.Name.ToLowerInvariant()] = saved;
            RefreshCompanionRoster();
            return holder;
        }

        /// <summary>Hire one explicitly selected class. The UI labels its party role;
        /// the server validates class, capacity, caller, and recruiter proximity.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdHireCompanionClass(int classIndex, NetworkConnection conn = null)
        {
            if (CompanionPrefab == null) { RpcNotice("No sellswords available."); return; }
            if (classIndex < 0 || classIndex > 3) { RpcNotice("That class is unavailable."); return; }
            var leader = RecruitingLeader(conn);
            if (leader == null) { RpcNotice("No adventurer is available to lead the hire."); return; }
            if (conn != null && conn.IsValid && !RecruiterNear(leader))
            { RpcNotice("Speak with the Council recruiter to hire companions."); return; }
            ServerHireSelectedClass((CharacterClass)classIndex, leader);
        }

        [Server]
        private PlayerCharacterHolder ServerHireSelectedClass(CharacterClass characterClass,
            PlayerCharacterHolder leader)
        {
            var holder = ServerSpawnNewCompanion(characterClass, leader);
            if (holder == null) { RpcNotice("The party is already full."); return null; }
            ServerSaveCampaign();
            RpcNotice($"{holder.Sheet.Name} the {holder.Sheet.Class} " +
                      $"({PartyComposition.RoleOf(holder.Sheet.Class)}) joined the party!");
            return holder;
        }

        /// <summary>Rehire a released named companion with their saved sheet and equipment.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdRehireCompanion(string companionName, NetworkConnection conn = null)
        {
            var leader = RecruitingLeader(conn);
            if (leader == null) return;
            if (conn != null && conn.IsValid && !RecruiterNear(leader))
            { RpcNotice("Speak with the Council recruiter to rehire companions."); return; }
            string key = (companionName ?? "").Trim().ToLowerInvariant();
            if (!_companions.TryGetValue(key, out var saved) || saved.Active)
            { RpcNotice("That companion is not waiting to be rehired."); return; }
            ServerRehireSavedCompanion(saved, leader);
        }

        [Server]
        private PlayerCharacterHolder ServerRehireSavedCompanion(SavedCompanion saved,
            PlayerCharacterHolder leader)
        {
            var holder = ServerSpawnSavedCompanion(saved, leader);
            if (holder == null) { RpcNotice("The party is already full."); return null; }
            ServerSaveCampaign();
            RpcNotice($"{holder.Sheet.Name} rejoins with their saved level and equipment.");
            return holder;
        }

        /// <summary>Release an active hire but retain their exact sheet and loadout in the
        /// campaign roster. They can later be rehired from the Council recruiter.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdReleaseCompanion(string companionName, NetworkConnection conn = null)
        {
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
            { RpcNotice("Companions cannot leave during combat."); return; }
            if (RecruitingLeader(conn) == null) return;
            string key = (companionName ?? "").Trim().ToLowerInvariant();
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsCompanion && p.Sheet != null
                    && p.Sheet.Name.ToLowerInvariant() == key);
            if (holder == null) { RpcNotice("That companion is not in the active party."); return; }

            ServerReleaseActiveCompanion(holder);
        }

        [Server]
        private void ServerReleaseActiveCompanion(PlayerCharacterHolder holder)
        {
            var saved = SaveSystem.CaptureCompanion(holder, active: false);
            saved.Active = false;
            _companions[holder.Sheet.Name.ToLowerInvariant()] = saved;
            string name = holder.Sheet.Name;
            FishNet.InstanceFinder.ServerManager.Despawn(holder.NetworkObject);
            RefreshCompanionRoster();
            ServerSaveCampaign();
            RpcNotice($"{name} leaves the active party and can be rehired later.");
        }

        /// <summary>Legacy/test convenience: fill every open slot with the balanced rules
        /// recommendation. Player-facing recruitment uses CmdHireCompanionClass instead.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdRecruitCompanions(NetworkConnection conn = null)
        {
            if (CompanionPrefab == null) { RpcNotice("No sellswords available."); return; }
            var holders = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.Sheet != null).ToList();
            int needed = RadiantPool.Rules.PartyComposition.MaxPartySize - holders.Count;
            if (needed <= 0) { RpcNotice("The party is already full."); return; }

            var leader = RecruitingLeader(conn);
            if (leader == null) { RpcNotice("No adventurer is available to lead the hires."); return; }

            var classes = RadiantPool.Rules.PartyComposition.Recruits(
                holders.Select(h => h.Sheet.Class), needed);

            var hired = new System.Collections.Generic.List<string>();
            foreach (var cls in classes)
            {
                var holder = ServerSpawnNewCompanion(cls, leader);
                if (holder == null) break;
                holders.Add(holder);
                hired.Add($"{holder.Sheet.Name} the {cls}");
            }
            ServerSaveCampaign();
            int spawned = hired.Count;
            RpcNotice(spawned == 1
                ? $"{hired[0]} joined the party!"
                : $"Sellswords joined the party: {string.Join(", ", hired)}.");
        }

        [Server]
        private void ServerRestoreActiveCompanionsWhenReady()
        {
            if (!_restoreActiveCompanions || Time.unscaledTime < _nextCompanionRestore) return;
            _nextCompanionRestore = Time.unscaledTime + 1f;
            var leader = RecruitingLeader(null);
            if (leader == null) return;

            var present = new System.Collections.Generic.HashSet<string>(
                FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                    .Where(p => p.IsCompanion && p.Sheet != null)
                    .Select(p => p.Sheet.Name), System.StringComparer.OrdinalIgnoreCase);
            var missing = _companions.Values.Where(c => c.Active
                    && !present.Contains(c.Character.Name)).OrderBy(c => c.Character.Name).ToList();
            foreach (var saved in missing)
            {
                if (ServerSpawnSavedCompanion(saved, leader) == null) break;
                present.Add(saved.Character.Name);
            }
            _restoreActiveCompanions = _companions.Values
                .Any(c => c.Active && !present.Contains(c.Character.Name));
            if (!_restoreActiveCompanions && missing.Count > 0)
                Debug.Log($"[CompanionRestore] restored {missing.Count} active companion(s)");
        }

        /// <summary>Dialogue actions arrive from whichever client is talking to the NPC.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdDialogueChoice(string action, NetworkConnection conn = null)
        {
            if (action == "muster_accept" && MusterState.Value == (int)QuestState.Active)
            {
                MusterState.Value = (int)QuestState.Completed;
                var activated = ServerActivateEligibleZones();
                AwardXpToAll(50);
                string quests = string.Join(", ", activated.Select(i => Zones[i].QuestName));
                RpcNotice(activated.Count == 1
                    ? $"New quest: {quests} (journal: J)."
                    : $"New quests: {quests} (journal: J). Choose a gold waypoint.");
                foreach (int opened in activated)
                    ServerRecheckZone(opened);   // may have been cleared pre-accept
                ServerSaveCampaign();
                return;
            }

            if (action.StartsWith("turnin_") && int.TryParse(action.Substring(7), out int zone)
                && zone >= 0 && zone < Zones.Length
                && GetZoneState(zone) == QuestState.ObjectivesMet)
            {
                var cfg = Zones[zone];
                ZoneStates[zone] = (int)QuestState.Completed;
                PartyGold.Value += cfg.Gold;
                AwardXpToAll(cfg.XpEach);
                string rewardLoot = ServerAwardRewardLoot(cfg.RewardLootTable);
                string rewardSummary = $"+{cfg.XpEach:N0} XP each, +{cfg.Gold:N0} gold" +
                    (string.IsNullOrEmpty(rewardLoot) ? ". " : $", reward: {rewardLoot}. ");
                var activated = ServerActivateEligibleZones();
                if (activated.Count > 0)
                {
                    string quests = string.Join(", ", activated.Select(i => Zones[i].QuestName));
                    RpcNotice("Quest complete! " + rewardSummary +
                              (activated.Count == 1 ? $"New quest: {quests}." : $"New quests: {quests}."));
                    foreach (int opened in activated)
                        ServerRecheckZone(opened);   // may already be cleared from wandering
                }
                else if (CampaignGraph.FinaleComplete(CampaignNodes(),
                    Enumerable.Range(0, Zones.Length)
                        .Where(i => GetZoneState(i) == QuestState.Completed)
                        .Select(i => Zones[i].ZoneId)))
                {
                    CampaignComplete.Value = true;
                    RpcNotice("Quest complete! " + rewardSummary +
                              "The Hollow Flame is sealed. Aldenmere stands free!");
                }
                else
                    RpcNotice("Quest complete! " + rewardSummary +
                              "Other Council commissions remain open.");
                ServerSaveCampaign();
            }
        }

        /// <summary>Fast travel is party travel: moving only the caller would strand a
        /// co-op session across different encounter cells. The server rechecks location
        /// access and waystone proximity before moving any body.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdTravelTo(int destinationZone, NetworkConnection conn = null)
        {
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
            { RpcNotice("The party cannot travel during combat."); return; }
            if (destinationZone < -1 || destinationZone >= Zones.Length)
            { RpcNotice("That destination does not exist."); return; }
            if (destinationZone >= 0 && GetZoneState(destinationZone) == QuestState.Locked)
            { RpcNotice("The Council has not opened that route."); return; }

            var caller = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.Owner == conn && !p.IsCompanion);
            if (caller == null) return;
            bool nearWaystone = FindObjectsByType<CampaignTravel>(FindObjectsSortMode.None)
                .Any(t => t.Allows(caller.transform.position, destinationZone));
            if (!nearWaystone)
            { RpcNotice("Reach a Council waystone before travelling."); return; }

            var destination = FindObjectsByType<CampaignDestination>(FindObjectsSortMode.None)
                .FirstOrDefault(d => d.ZoneIndex == destinationZone);
            if (destination == null)
            { RpcNotice("That waystone route is not ready."); return; }

            var party = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.Sheet != null).ToArray();
            for (int i = 0; i < party.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / Mathf.Max(1, party.Length);
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f;
                Warp(party[i].transform, destination.transform.position + offset);
            }
            string place = destinationZone < 0 ? "Council Hall" : Zones[destinationZone].DisplayName;
            RpcNotice($"The party travels to {place}.");
        }

        /// <summary>Resolve a quest's authored on-site objective after its combat blocks.
        /// The server validates ownership, proximity, progress, and the offered choice;
        /// no exception may escape a ServerRpc because FishNet treats that as malformed input.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdResolveSiteAction(int zone, int choice, NetworkConnection conn = null)
        {
            try
            {
                if (zone < 0 || zone >= Zones.Length) return;
                var cfg = Zones[zone];
                if (string.IsNullOrEmpty(cfg.SiteAction) || IsSiteActionComplete(zone)) return;
                if (GetZoneState(zone) != QuestState.Active)
                { RpcNotice("That commission is not active."); return; }
                if (zone >= ZoneClearedCounts.Count
                    || ZoneClearedCounts[zone] < cfg.RequiredEncounters)
                { RpcNotice("Secure the site before completing that objective."); return; }

                var caller = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => p.Owner == conn && !p.IsCompanion);
                var objective = FindObjectsByType<CampaignObjectiveInteract>(FindObjectsSortMode.None)
                    .FirstOrDefault(o => o.ZoneIndex == zone);
                if (caller == null || objective == null
                    || Vector3.Distance(caller.transform.position, objective.transform.position)
                    > objective.InteractRange + 0.75f)
                { RpcNotice("Reach the quest objective first."); return; }

                bool hasChoice = !string.IsNullOrEmpty(cfg.ChoiceA)
                                 && !string.IsNullOrEmpty(cfg.ChoiceB);
                if (hasChoice && choice != 0 && choice != 1) return;
                string result = hasChoice ? (choice == 0 ? cfg.ChoiceA : cfg.ChoiceB) : "completed";
                CompletedSiteActions.Add(cfg.ZoneId + "|" + result);
                if (cfg.SiteActionGold > 0) PartyGold.Value += cfg.SiteActionGold;
                if (cfg.SiteActionXp > 0) AwardXpToAll(cfg.SiteActionXp);
                string actionLoot = ServerAwardRewardLoot(cfg.SiteActionLootTable);
                string actionReward = cfg.SiteActionXp > 0 || cfg.SiteActionGold > 0
                    || !string.IsNullOrEmpty(actionLoot)
                    ? $" Side quest reward: +{cfg.SiteActionXp:N0} XP each, " +
                      $"+{cfg.SiteActionGold:N0} gold" +
                      (string.IsNullOrEmpty(actionLoot) ? "." : $", {actionLoot}.")
                    : "";
                RpcNotice((hasChoice
                    ? $"Decision recorded: {result}."
                    : $"Objective complete: {cfg.SiteAction}") + actionReward);
                ServerRecheckZone(zone);
                ServerSaveCampaign();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SiteAction] rejected safely: {e.Message}");
                RpcNotice("That objective could not be resolved.");
            }
        }

        [Server]
        public void ServerAddLoot(int gold, System.Collections.Generic.List<string> items)
        {
            PartyGold.Value += gold;
            foreach (var id in items) Stash.Add(id);
            if (gold > 0 || items.Count > 0)
                RpcNotice($"Loot: {gold:N0} gold" +
                    (items.Count > 0 ? $", {string.Join(", ", items)}" : "") + ".");
        }

        private int PartyRewardLevel()
        {
            var levels = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => !p.IsCompanion && p.Sheet != null)
                .Select(p => p.Sheet.Level).ToArray();
            return levels.Length == 0 ? 1 : levels.Max();
        }

        /// <summary>Find the strongest class-legal improvement in this reward tier for a
        /// human hero. Already-stashed items are skipped so successive quests distribute
        /// different options instead of repeatedly paying the same unequipped sword.</summary>
        private string BestPartyUpgrade(LootTable table)
        {
            var heroes = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => !p.IsCompanion && p.Sheet != null).ToArray();
            string bestId = null;
            float bestScore = 0.05f;
            foreach (string id in table.Entries.Where(e => e.ItemId != null)
                         .Select(e => e.ItemId).Distinct())
            {
                if (Stash.Contains(id)) continue;
                var item = GameItem.Get(id);
                if (item == null) continue;
                foreach (var hero in heroes)
                {
                    if (!item.UsableBy(hero.Sheet.Class)) continue;
                    int str = hero.Sheet.Abilities.Modifier(Ability.Str);
                    int dex = hero.Sheet.Abilities.Modifier(Ability.Dex);
                    float score = 0f;
                    if (item.Slot == ItemSlot.Weapon)
                    {
                        var worn = GameItem.Get(hero.WeaponId.Value);
                        score = item.AverageDamage(str, dex)
                                - (worn?.AverageDamage(str, dex) ?? 0f);
                    }
                    else if (item.Slot == ItemSlot.Armor)
                    {
                        var worn = GameItem.Get(hero.ArmorId.Value);
                        bool shield = hero.ShieldEquipped.Value;
                        int current = worn != null
                            ? worn.AcWith(dex, shield)
                            : ArmorDefinition.Unarmored.BaseAc + dex
                              + (shield ? GameItem.ShieldAcBonus : 0);
                        score = item.AcWith(dex, shield) - current;
                    }
                    else if (item.Slot == ItemSlot.Shield && !hero.ShieldEquipped.Value)
                        score = GameItem.ShieldAcBonus;

                    if (score > bestScore
                        || (System.Math.Abs(score - bestScore) < 0.001f
                            && string.CompareOrdinal(id, bestId) < 0))
                    {
                        bestScore = score;
                        bestId = id;
                    }
                }
            }
            return bestId;
        }

        /// <summary>Rolls a hero-level-matched quest parcel once, on the server. When the
        /// tier contains a real equipment upgrade, the first item is guaranteed to be one;
        /// remaining rolls stay random. Errors are contained so no RPC can kick a player.</summary>
        [Server]
        private string ServerAwardRewardLoot(string tableId)
        {
            if (string.IsNullOrEmpty(tableId)) return "";
            try
            {
                int heroLevel = PartyRewardLevel();
                string resolvedTable = LootLibrary.IsQuestTable(tableId)
                    ? LootLibrary.QuestTableForCharacterLevel(heroLevel) : tableId;
                var rng = new SeededRng(System.Environment.TickCount ^ PartyGold.Value
                    ^ CompletedSiteActions.Count ^ Stash.Count);
                var table = LootLibrary.Get(resolvedTable);
                var roll = table.Roll(rng);
                var awarded = roll.ItemIds.ToList();
                if (LootLibrary.IsQuestTable(resolvedTable))
                {
                    string upgrade = BestPartyUpgrade(table);
                    if (!string.IsNullOrEmpty(upgrade) && !awarded.Contains(upgrade))
                    {
                        if (awarded.Count == 0) awarded.Add(upgrade);
                        else awarded[0] = upgrade;
                    }
                }
                PartyGold.Value += roll.Gold;
                foreach (string item in awarded) Stash.Add(item);
                Debug.Log($"[QuestReward] hero L{heroLevel}: {tableId} -> {resolvedTable}; " +
                          $"awarded {string.Join(",", awarded)}");
                return string.Join(", ", awarded.Select(id =>
                {
                    var item = GameItem.Get(id);
                    return item == null ? id.Replace('_', ' ') : item.Name;
                }));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[QuestReward] {tableId} failed safely: {e.Message}");
                return "";
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdBuyPotion(NetworkConnection conn = null)
        {
            if (PartyGold.Value < PotionBuyPrice)
            { RpcNotice("Not enough gold for a healing potion (50g)."); return; }
            PartyGold.Value -= PotionBuyPrice;
            Stash.Add("potion_healing");
            RpcNotice("Bought a Potion of Healing (2d4+2).");
        }

        /// <summary>Buy a weapon/armor upgrade from the smith into the party stash;
        /// equip it afterwards from the inventory (I).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdBuyItem(string itemId, NetworkConnection conn = null)
        {
            int price = SmithStock.FirstOrDefault(s => s.id == itemId).price;
            if (price <= 0) { RpcNotice("The smith doesn't stock that."); return; }
            if (PartyGold.Value < price)
            { RpcNotice($"Not enough gold ({price:N0}g needed)."); return; }
            PartyGold.Value -= price;
            Stash.Add(itemId);
            var item = GameItem.Get(itemId);
            RpcNotice($"Bought {item?.Name ?? itemId} for {price}g — equip it from the inventory (I).");
        }

        /// <summary>Is a trader (the Exchange or the smithy) within talking distance of this
        /// point? The inventory greys its Sell buttons with this and the server re-checks the
        /// sale with it, so the button and the RPC can never disagree. The server allows
        /// <see cref="ServerRangeSlack"/> of extra reach: the seller's position it sees lags
        /// the client's by a tick or two, and a legitimate sale must not bounce.</summary>
        public const float ServerRangeSlack = 1.5f;

        public static bool TraderNear(Vector3 pos, out string traderName, float slack = 0f)
        {
            foreach (var v in FindObjectsByType<VendorInteract>(FindObjectsSortMode.None))
                if (Vector3.Distance(pos, v.transform.position) <= v.InteractRange + slack)
                { traderName = v.VendorName; return true; }
            foreach (var s in FindObjectsByType<SmithInteract>(FindObjectsSortMode.None))
                if (Vector3.Distance(pos, s.transform.position) <= s.InteractRange + slack)
                { traderName = s.VendorName; return true; }
            traderName = null;
            return false;
        }

        /// <summary>Sell ONE item out of the party stash to the trader the seller is standing
        /// next to — the way to turn gear the party will never wear into gold without dumping
        /// the whole pile. Gold and stash are party-shared, so every condition is checked
        /// server-side: out of combat, next to a trader, item really in the stash, and
        /// something that has a buyer. CmdSellAll remains for clearing out salvage wholesale
        /// (it keeps potions; sell those one at a time if you truly mean to).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdSellItem(string itemId, NetworkConnection conn = null)
        {
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
            { RpcNotice("Not during combat."); return; }

            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.Owner == conn);
            if (holder == null) return;
            if (!TraderNear(holder.transform.position, out string trader, ServerRangeSlack))
            { RpcNotice("No trader here to buy that."); return; }

            string label = GameItem.Get(itemId)?.Name ?? itemId.Replace('_', ' ');
            if (!SellValue.TryGetValue(itemId, out int gold) || gold <= 0)
            { RpcNotice($"Nobody buys {label}."); return; }
            int idx = Stash.IndexOf(itemId);
            if (idx < 0) { RpcNotice("That item is not in the stash."); return; }

            Stash.RemoveAt(idx);
            PartyGold.Value += gold;
            RpcNotice($"Sold {label} to {trader} for {gold:N0} gold.");
        }

        /// <summary>Unattended shop check (`RadiantPool.exe -autohost -selltest`, asserted by
        /// scripts/smoke-test.ps1). It drives the sale the way a player does — put a sword in
        /// the bag, walk up to the trader, press Sell — instead of poking the stash directly,
        /// so the log line proves the whole path works: proximity, bag, purse. Never runs
        /// without the flag.</summary>
        private System.Collections.IEnumerator ServerSellSelfTest()
        {
            yield return new WaitForSeconds(8f);   // a character has to spawn first

            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.IsOwner);
            var vendor = FindFirstObjectByType<VendorInteract>();
            if (holder == null || vendor == null)
            {
                Debug.Log("[SellTest] FAIL - no player character or no vendor in the scene");
                yield break;
            }

            const string itemId = "longsword";
            Stash.Add(itemId);
            int goldBefore = PartyGold.Value, bagBefore = Stash.Count;

            Warp(holder.transform, new Vector3(vendor.transform.position.x + 1.5f,
                holder.transform.position.y, vendor.transform.position.z));
            yield return null;
            Debug.Log($"[SellTest] seller {Vector3.Distance(holder.transform.position, vendor.transform.position):0.0}m " +
                      $"from {vendor.VendorName}");
            CmdSellItem(itemId);   // host owner: the same ServerRpc a real client sends
            yield return new WaitForSeconds(0.5f);

            int expected = goldBefore + SellValue[itemId];
            bool sold = PartyGold.Value == expected && Stash.Count == bagBefore - 1;
            Debug.Log($"[SellTest] {(sold ? "PASS" : "FAIL")} - gold {goldBefore} to " +
                      $"{PartyGold.Value} (expected {expected}), bag {bagBefore} to {Stash.Count}");

            // ...and out of a trader's reach, the same sale must be refused.
            Stash.Add(itemId);
            int goldAway = PartyGold.Value, bagAway = Stash.Count;
            Warp(holder.transform, holder.transform.position + new Vector3(60f, 0f, 0f));
            yield return null;
            CmdSellItem(itemId);
            yield return new WaitForSeconds(0.5f);

            bool refused = PartyGold.Value == goldAway && Stash.Count == bagAway;
            Debug.Log($"[SellTest] {(refused ? "PASS" : "FAIL")} - away from any trader the " +
                      $"sale was {(refused ? "refused" : "ALLOWED")}");
        }

        /// <summary>Unattended progression check (`RadiantPool.exe -autohost -leveltest`,
        /// asserted by scripts/smoke-test.ps1): award XP the way a kill does, and check the
        /// character actually LEVELS, banks a point, and can spend it into a real score — the
        /// whole chain the player sees behind the XP bar. Needs -savedir: spending a point
        /// persists the campaign.</summary>
        private System.Collections.IEnumerator ServerLevelSelfTest()
        {
            yield return new WaitForSeconds(8f);

            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.IsOwner);
            if (holder == null || holder.Sheet == null)
            {
                Debug.Log("[LevelTest] FAIL - no player character");
                yield break;
            }

            var sheet = holder.Sheet;
            int startLevel = sheet.Level;
            if (startLevel >= RadiantPool.Rules.Progression.MaxLevel)
            {
                Debug.Log($"[LevelTest] SKIP - character is already at the level cap");
                yield break;
            }

            // Enough XP to cross exactly one threshold, granted through the kill/quest road.
            int need = RadiantPool.Rules.Progression.XpToNext(sheet.Level, sheet.Xp);
            ServerGrantXp(holder, need);
            yield return null;

            bool levelled = sheet.Level == startLevel + 1;
            bool banked = sheet.PendingAbilityPoints > 0;
            Debug.Log($"[LevelTest] {(levelled && banked ? "PASS" : "FAIL")} - {need} XP took " +
                      $"{sheet.Name} from level {startLevel} to {sheet.Level} with " +
                      $"{sheet.PendingAbilityPoints} point(s) to spend");

            var ability = RadiantPool.Rules.Ability.Con;
            int before = sheet.Abilities[ability];
            int pointsBefore = sheet.PendingAbilityPoints;
            CmdSpendAbilityPoint((int)ability); // host owner: same ServerRpc a client sends
            yield return new WaitForSeconds(0.3f);

            bool spent = sheet.Abilities[ability] == before + 1
                         && sheet.PendingAbilityPoints == pointsBefore - 1;
            Debug.Log($"[LevelTest] {(spent ? "PASS" : "FAIL")} - spending a point took Con " +
                      $"{before} to {sheet.Abilities[ability]}, " +
                      $"{sheet.PendingAbilityPoints} left of {pointsBefore}");
        }

        /// <summary>Unattended visual-equipment check. It proves the replicated starting
        /// weapon appears, an equipment change replaces it, and every armed town NPC has
        /// its declared item mounted. Runs only with -weapontest.</summary>
        private System.Collections.IEnumerator WeaponVisualSelfTest()
        {
            yield return new WaitForSeconds(8f);

            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            if (holder == null || holder.Sheet == null)
            {
                Debug.Log("[WeaponTest] FAIL - no player character");
                yield break;
            }

            var starting = GameItem.Get(holder.WeaponId.Value);
            bool playerStart = starting != null && CharacterVisuals.HasHandItem(
                holder.transform, "r", starting.HandModel);

            // Change through the same authoritative equipment method used by the bags,
            // then restore the original item so this check leaves no campaign mutation.
            var swap = GameItem.Get("greataxe");
            holder.ServerEquip(swap);
            yield return new WaitForSeconds(0.5f);
            bool playerSwap = CharacterVisuals.HasHandItem(
                holder.transform, "r", swap.HandModel);
            if (starting != null) holder.ServerEquip(starting);

            var armedNpcs = FindObjectsByType<NpcVisual>(FindObjectsSortMode.None)
                .Where(n => !string.IsNullOrEmpty(n.WeaponId)).ToArray();
            int visibleNpcs = armedNpcs.Count(n =>
            {
                var item = GameItem.Get(n.WeaponId);
                return item != null && CharacterVisuals.HasHandItem(
                    n.transform, "r", item.HandModel);
            });

            bool pass = playerStart && playerSwap
                        && armedNpcs.Length >= 3 && visibleNpcs == armedNpcs.Length;
            Debug.Log($"[WeaponTest] {(pass ? "PASS" : "FAIL")} - player start " +
                      $"{(playerStart ? "visible" : "MISSING")}, swap " +
                      $"{(playerSwap ? "visible" : "MISSING")}, armed NPCs " +
                      $"{visibleNpcs}/{armedNpcs.Length} visible");
        }

        /// <summary>Built-player regression for explicit role/class choices, leader parity,
        /// individual quest-loot equipment, release, and same-name rehire.</summary>
        private System.Collections.IEnumerator SoloRecruitmentSelfTest()
        {
            yield return new WaitForSeconds(8f);

            var players = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => !p.IsCompanion && p.Sheet != null).ToArray();
            if (players.Length != 1)
            {
                Debug.Log($"[RecruitTest] FAIL - expected one solo player, found {players.Length}");
                yield break;
            }

            var leader = players[0];
            int levelFiveXp = RadiantPool.Rules.ClassData.XpThresholds[4];
            if (leader.Sheet.Xp < levelFiveXp)
                ServerGrantXp(leader, levelFiveXp - leader.Sheet.Xp);
            switch (leader.Sheet.Class)
            {
                case RadiantPool.Rules.CharacterClass.Fighter:
                    leader.ServerSetEquipment("greatsword", "splint", false); break;
                case RadiantPool.Rules.CharacterClass.Cleric:
                    leader.ServerSetEquipment("warhammer", "half_plate", true); break;
                case RadiantPool.Rules.CharacterClass.Rogue:
                    leader.ServerSetEquipment("rapier", "studded_leather", false); break;
                default:
                    leader.ServerSetEquipment("quarterstaff", "", false); break;
            }

            // Match the user's save: muster and every quest are complete, but the player
            // keeps clearing optional encounters under standing orders.
            MusterState.Value = (int)QuestState.Completed;
            CampaignComplete.Value = true;
            for (int i = 0; i < ZoneStates.Count; i++)
                ZoneStates[i] = (int)QuestState.Completed;

            var recruiter = FindObjectsByType<NpcInteract>(FindObjectsSortMode.None).FirstOrDefault();
            if (recruiter == null)
            {
                Debug.Log("[RecruitTest] FAIL - no Council recruiter in the built scene");
                yield break;
            }
            Warp(leader.transform, recruiter.transform.position + Vector3.forward);
            yield return null;

            int offered = NpcInteract.AvailableRecruitSlots();
            recruiter.ChooseHire(CharacterClass.Fighter);
            yield return new WaitForSeconds(0.2f);
            recruiter.ChooseHire(CharacterClass.Cleric);
            yield return new WaitForSeconds(0.2f);
            recruiter.ChooseHire(CharacterClass.Rogue);
            yield return new WaitForSeconds(1.5f);

            var initialHires = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.IsCompanion && p.Sheet != null).ToArray();
            bool selectedClasses = new[]
            {
                CharacterClass.Fighter, CharacterClass.Cleric, CharacterClass.Rogue
            }.All(cls => initialHires.Count(p => p.Sheet.Class == cls) == 1);
            int initialEquipmentMatched = initialHires.Count(p =>
                p.WeaponId.Value == CompanionLoadout.WeaponFor(
                    p.Sheet.Class, leader.WeaponId.Value)
                && p.ArmorId.Value == CompanionLoadout.ArmorFor(
                    p.Sheet.Class, leader.ArmorId.Value)
                && p.ShieldEquipped.Value == CompanionLoadout.ShieldFor(
                    p.Sheet.Class, leader.ShieldEquipped.Value));
            int levelMatched = initialHires.Count(p => p.Sheet.Level == leader.Sheet.Level
                                                        && p.Sheet.Xp == leader.Sheet.Xp);
            int modeled = initialHires.Count(p =>
                CharacterVisuals.HasVisibleCharacterModel(p.transform));
            int armed = initialHires.Count(p =>
            {
                var item = GameItem.Get(p.WeaponId.Value);
                return item != null && CharacterVisuals.HasHandItem(
                    p.transform, "r", item.HandModel);
            });
            int party = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Count(p => p.Sheet != null);
            bool parity = levelMatched == initialHires.Length
                          && initialEquipmentMatched == initialHires.Length;
            bool recruitPass = offered == 3 && initialHires.Length == 3 && selectedClasses
                               && modeled == 3 && armed == 3 && parity && party == 4
                               && NpcInteract.AvailableRecruitSlots() == 0;
            string parityDetails = string.Join("; ", initialHires.Select(p =>
                $"{p.Sheet.Name} L{p.Sheet.Level} {p.WeaponId.Value}/" +
                $"{(p.ArmorId.Value.Length > 0 ? p.ArmorId.Value : "unarmored")}"));
            Debug.Log($"[RecruitTest] {(recruitPass ? "PASS" : "FAIL")} - " +
                      $"chose tank/healer/damage for {offered} slot(s), spawned " +
                      $"{initialHires.Length} companion(s), level/XP parity " +
                      $"{levelMatched}/{initialHires.Length} at level {leader.Sheet.Level}, " +
                      $"initial equipment parity {initialEquipmentMatched}/{initialHires.Length}, " +
                      $"models {modeled}/{initialHires.Length}, weapons {armed}/{initialHires.Length}, " +
                      $"party {party}/{PartyComposition.MaxPartySize}");
            Debug.Log($"[RecruitParityTest] {(parity ? "PASS" : "FAIL")} - {parityDetails}");

            var fighter = initialHires.FirstOrDefault(p => p.Sheet.Class == CharacterClass.Fighter);
            string persistentName = fighter?.Sheet.Name ?? "";

            // Simulate assigning an independently won quest item from the shared stash.
            Stash.Add("greataxe");
            CmdEquipItem("greataxe", persistentName);
            yield return new WaitForSeconds(0.25f);
            bool questGearEquipped = fighter != null && fighter.WeaponId.Value == "greataxe"
                                     && !Stash.Contains("greataxe");

            CmdReleaseCompanion(persistentName);
            yield return new WaitForSeconds(0.25f);
            bool released = !FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Any(p => p.IsCompanion && p.CharacterName.Value == persistentName)
                && CompanionRoster.Any(s => TryParseCompanionSummary(s, out string n,
                    out _, out bool active) && n == persistentName && !active);
            var persisted = SaveSystem.Read()?.Companions;
            bool savedRoster = persisted != null && persisted.Count == 3
                               && persisted.Any(c => c.Character.Name == persistentName
                                                     && !c.Active && c.WeaponId == "greataxe");
            bool persistencePass = questGearEquipped && released && savedRoster;
            Debug.Log($"[RecruitPersistenceTest] {(persistencePass ? "PASS" : "FAIL")} - " +
                $"{persistentName} " +
                $"equipped quest greataxe, released, and remained saved for rehire");
        }

        /// <summary>Second-process proof that active named companions restore automatically
        /// from the campaign written by SoloRecruitmentSelfTest.</summary>
        private System.Collections.IEnumerator RecruitmentRestoreSelfTest()
        {
            yield return new WaitForSeconds(9f);
            var leader = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Sheet != null);
            var initiallyActive = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.IsCompanion && p.Sheet != null).ToArray();
            string releasedName = "";
            foreach (string summary in CompanionRoster)
                if (TryParseCompanionSummary(summary, out string name,
                        out CharacterClass cls, out bool active)
                    && cls == CharacterClass.Fighter && !active)
                    releasedName = name;
            bool releasedStayedOut = initiallyActive.Length == 2 && releasedName.Length > 0
                && !initiallyActive.Any(p => p.Sheet.Name == releasedName);

            var recruiter = FindObjectsByType<NpcInteract>(FindObjectsSortMode.None).FirstOrDefault();
            if (leader != null && recruiter != null)
            {
                Warp(leader.transform, recruiter.transform.position + Vector3.forward);
                yield return null;
                recruiter.ChooseRehire(releasedName);
                yield return new WaitForSeconds(1f);
            }

            var hired = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.IsCompanion && p.Sheet != null).ToArray();
            bool classes = new[] { CharacterClass.Fighter, CharacterClass.Cleric,
                CharacterClass.Rogue }.All(cls => hired.Count(p => p.Sheet.Class == cls) == 1);
            bool parity = leader != null && hired.All(p => p.Sheet.Level == leader.Sheet.Level
                                                           && p.Sheet.Xp == leader.Sheet.Xp);
            bool gear = hired.Any(p => p.Sheet.Class == CharacterClass.Fighter
                                       && p.WeaponId.Value == "greataxe");
            bool roster = CompanionRoster.Count == 3 && CompanionRoster.All(s =>
                TryParseCompanionSummary(s, out _, out _, out bool active) && active);
            bool pass = releasedStayedOut && hired.Length == 3 && classes && parity && gear && roster;
            Debug.Log($"[RecruitRestoreTest] {(pass ? "PASS" : "FAIL")} - " +
                      $"restored 2 active companions, released {releasedName} stayed benched, " +
                      $"then rehired {hired.Length}/3 by name; " +
                      $"classes {(classes ? "kept" : "LOST")}, " +
                      $"level/XP {(parity ? "kept" : "LOST")}, " +
                      $"quest gear {(gear ? "kept" : "LOST")}");
        }

        /// <summary>Full traditional-combat acceptance path. It uses the visible combat
        /// menu's public methods, real server intents, real enemy turns, a spell slot,
        /// damage/defeat rules, both outcome modals, and the retry command.</summary>
        private System.Collections.IEnumerator CombatFlowSelfTest()
        {
            yield return new WaitForSeconds(9f);

            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            var fights = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                .Where(t => !t.Consumed && t.MonsterIds.Length >= 2
                            && !ConsumedEncounterIds.Contains(t.EncounterId))
                .OrderBy(t => t.EncounterId == "enc_docks_01" ? 0 : 1)
                .ThenBy(t => t.MonsterIds.Length).ToArray();
            var combat = CombatManager.Instance;
            var ui = CombatClientUI.Instance;
            if (holder?.Sheet == null || holder.Sheet.Class != CharacterClass.Wizard
                || fights.Length < 2 || combat == null || ui == null)
            {
                Debug.Log("[CombatFlowTest] FAIL - needs a Wizard, combat UI, and two encounters");
                yield break;
            }

            // Initiative is random; keep the level-1 acceptance-test Wizard alive even
            // when every enemy acts before the first player turn.
            holder.Sheet.GrantTempHp(100);
            Warp(holder.transform, fights[0].transform.position);
            yield return null;
            combat.StartEncounter(fights[0]);

            float deadline = Time.time + 30f;
            while ((!combat.InCombat.Value || !combat.CanAcceptPlayerInput)
                   && Time.time < deadline)
                yield return new WaitForSeconds(0.1f);
            var mine = combat.MyUnit;
            if (mine == null || !combat.CanAcceptPlayerInput)
            {
                Debug.Log("[CombatFlowTest] FAIL - physical turn did not initialize");
                yield break;
            }
            var physicalTarget = combat.ClientUnits.Where(u => !u.IsPc && !u.Dead)
                .OrderByDescending(u => Chebyshev(mine.Cell, u.Cell)).FirstOrDefault();
            if (physicalTarget == null)
            {
                Debug.Log("[CombatFlowTest] FAIL - physical turn did not initialize");
                yield break;
            }

            // Default attack has no mode button: the world click is the complete command.
            ui.ClickCell(physicalTarget.Cell);
            deadline = Time.time + 15f;
            while (combat.InCombat.Value && (combat.ActionLeft || combat.ActionResolving)
                   && Time.time < deadline)
                yield return null;
            string heroName = holder.Sheet.Name;
            string physicalLine = combat.Log.LastOrDefault(l =>
                l.Contains(heroName) && l.Contains(physicalTarget.Name.Split('(')[0].Trim())) ?? "";
            bool physicalResolved = physicalLine.Length > 0 && !combat.ActionLeft;
            Debug.Log($"[CombatFlowTest] physical state {combat.State}, " +
                      $"action {combat.ActionLeft}, rejection '{combat.LastRejection}', " +
                      $"line '{physicalLine}'");

            int beforeEnemyTurns = combat.Log.Count;
            combat.CmdEndTurn();
            deadline = Time.time + 25f;
            while (combat.InCombat.Value
                   && (!combat.CanAcceptPlayerInput || combat.Round < 2)
                   && Time.time < deadline)
                yield return new WaitForSeconds(0.1f);
            bool enemyActed = combat.Log.Skip(beforeEnemyTurns).Any(l => l.Contains(heroName));

            string magicTarget = combat.ServerPrimeMagicFinishForTest();
            int slotsBefore = combat.MySlots.Sum();
            ui.PickSpell("burning_hands");
            ui.PickTarget(magicTarget);
            deadline = Time.time + 15f;
            while (!combat.OutcomeOpen && Time.time < deadline)
                yield return new WaitForSeconds(0.1f);
            bool magicResolved = combat.Log.Any(l => l.Contains("Burning Hands deals"));
            bool slotSpent = combat.MySlots.Sum() < slotsBefore;
            bool victoryModal = combat.OutcomeOpen && combat.BannerVictory
                                && combat.State == BattleState.Victory;
            Debug.Log($"[CombatFlowTest] magic state {combat.State}, " +
                      $"slots {slotsBefore}->{combat.MySlots.Sum()}, " +
                      $"target '{magicTarget}', rejection '{combat.LastRejection}', " +
                      $"outcome {combat.OutcomeOpen}/{combat.BannerTitle}");

            combat.DismissOutcome();
            yield return null;
            combat.StartEncounter(fights[1]);
            deadline = Time.time + 15f;
            while (!combat.InCombat.Value && Time.time < deadline)
                yield return new WaitForSeconds(0.1f);
            bool defeatApplied = combat.ServerDefeatPartyForTest();
            deadline = Time.time + 10f;
            while (!combat.OutcomeOpen && Time.time < deadline)
                yield return new WaitForSeconds(0.1f);
            bool defeatModal = defeatApplied && combat.OutcomeOpen && !combat.BannerVictory
                               && combat.State == BattleState.Defeat;

            combat.CmdRetryEncounter();
            deadline = Time.time + 10f;
            while (!combat.InCombat.Value && Time.time < deadline)
                yield return new WaitForSeconds(0.1f);
            bool retried = combat.InCombat.Value;

            bool pass = physicalResolved && enemyActed && magicResolved && slotSpent
                        && victoryModal && defeatModal && retried;
            Debug.Log($"[CombatFlowTest] {(pass ? "PASS" : "FAIL")} - " +
                      $"physical {physicalResolved}, enemy turn {enemyActed}, " +
                      $"magic+slot {magicResolved && slotSpent}, victory modal {victoryModal}, " +
                      $"defeat modal {defeatModal}, retry {retried}");
        }

        /// <summary>Unattended combat check (`RadiantPool.exe -autohost -attacktest`, asserted
        /// by scripts/smoke-test.ps1): start a real encounter, then drive ONE left-click on the
        /// enemy standing FURTHEST away and prove the fighter closes the distance and swings —
        /// no second click. It calls CombatClientUI.ClickCell, the same method the mouse calls,
        /// so it cannot pass on a path the player never takes.</summary>
        private System.Collections.IEnumerator AttackSelfTest()
        {
            yield return new WaitForSeconds(9f);   // spawn, then muster

            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            var fight = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                .Where(t => !t.Consumed && t.MonsterIds.Length > 0
                            && !ConsumedEncounterIds.Contains(t.EncounterId))
                .OrderByDescending(t => t.MonsterIds.Any(CombatManager.HasWeaponLoadout))
                // Deterministic, compact fight: two armed skulkers plus one rat. Scene
                // enumeration order is undefined and sometimes picked a five-enemy temple
                // group whose AI turns consumed the test's entire timeout.
                .ThenBy(t => t.EncounterId == "enc_docks_01" ? 0 : 1)
                .ThenBy(t => t.MonsterIds.Length)
                .FirstOrDefault();
            var combat = CombatManager.Instance;
            if (holder == null || fight == null || combat == null
                || CombatClientUI.Instance == null)
            {
                Debug.Log("[AttackTest] FAIL - no character, no encounter, or no combat UI");
                yield break;
            }

            Warp(holder.transform, fight.transform.position);
            yield return null;
            combat.StartEncounter(fight);

            float deadline = Time.time + 30f;
            while ((!combat.InCombat.Value || !combat.IsMyTurn) && Time.time < deadline)
                yield return new WaitForSeconds(0.2f);
            var mine = combat.MyUnit;
            if (!combat.InCombat.Value || !combat.IsMyTurn || mine == null)
            {
                Debug.Log("[AttackTest] FAIL - the fight never reached my turn");
                yield break;
            }

            // The FURTHEST enemy, so the click has to buy a walk as well as a swing.
            var enemy = combat.ClientUnits
                .Where(u => !u.IsPc && !u.Dead)
                .OrderByDescending(u => Chebyshev(mine.Cell, u.Cell))
                .FirstOrDefault();
            if (enemy == null)
            {
                Debug.Log("[AttackTest] FAIL - no enemy on the board");
                yield break;
            }

            bool blockedApproach = combat.ServerArrangeBlockedApproachForTest(mine.Id, enemy.Id);
            yield return new WaitForSeconds(0.2f); // board view + tactical light settle

            var atmosphere = WorldAtmosphere.Instance;
            bool combatLit = atmosphere != null && atmosphere.CombatVisibilityReady;
            Debug.Log($"[CombatLightTest] {(combatLit ? "PASS" : "FAIL")} - " +
                      $"fill {atmosphere?.CombatLightIntensity ?? 0f:0.00} at " +
                      $"{atmosphere?.CombatLightRange ?? 0f:0.0}m covers " +
                      $"{combat.ClientUnits.Count(u => !u.Dead && u.Visual != null)} living units; " +
                      $"party torch {atmosphere?.PartyTorchIntensity ?? 0f:0.00}");

            var armedEnemies = combat.ClientUnits
                .Where(u => !u.IsPc && CombatManager.WeaponModelForUnit(u.Id).Length > 0)
                .ToArray();
            int visibleWeapons = armedEnemies.Count(u => CharacterVisuals.HasHandItem(
                u.Visual, "r", CombatManager.WeaponModelForUnit(u.Id)));
            bool weaponsVisible = armedEnemies.Length > 0
                                  && visibleWeapons == armedEnemies.Length;
            Debug.Log($"[WeaponTest] {(weaponsVisible ? "PASS" : "FAIL")} - combat NPCs " +
                      $"{visibleWeapons}/{armedEnemies.Length} equipped weapons visible");

            var from = mine.Cell;
            int feet = Chebyshev(from, enemy.Cell) * 5;
            int feedbackBefore = CombatFx.Instance != null
                ? CombatFx.Instance.AttackFeedbackEvents : -1;
            int sfxBefore = GameAudio.Instance != null ? GameAudio.Instance.SfxEventsPlayed : -1;
            int licensedBefore = GameAudio.Instance != null
                ? GameAudio.Instance.LicensedSfxEventsPlayed : -1;
            var hudEnemies = combat.ClientUnits.Where(u => !u.IsPc && !u.Dead).ToArray();
            int distinctShapes = hudEnemies.Select(u => u.TargetShape).Distinct().Count();
            bool rendererClick = Camera.main != null && hudEnemies.Any(u =>
            {
                if (u.Visual == null) return false;
                Vector3 point = Camera.main.WorldToScreenPoint(u.Visual.position
                    + Vector3.up * Mathf.Max(0.5f, u.LabelHeight * 0.5f));
                return point.z > 0f && CombatClientUI.Instance.EnemyIdAtScreenPoint(point) == u.Id;
            });
            bool monsterHud = hudEnemies.Length > 0
                              && hudEnemies.All(u => u.Visual != null && u.MaxHp > 0
                                  && u.LabelHeight >= 1.25f
                                  && CombatClientUI.TargetShapeTexture(u.TargetShape) != null)
                              && distinctShapes == Mathf.Min(hudEnemies.Length,
                                  CombatClientUI.TargetShapeCount)
                              && rendererClick;
            Debug.Log($"[MonsterHudTest] {(monsterHud ? "PASS" : "FAIL")} - " +
                      $"health anchors {hudEnemies.Count(u => u.LabelHeight >= 1.25f)}/" +
                      $"{hudEnemies.Length}, distinct generated target shapes {distinctShapes}, " +
                      $"renderer click {(rendererClick ? "TARGETED" : "MISSED")}, " +
                      $"last on-screen overlays {CombatClientUI.Instance.LastMonsterOverlayCount}");
            // Root combat has no permanent instruction/info window. Open Attack exactly as
            // the restored hotbar slot does, give IMGUI a frame to lay out the picker, and
            // prove both the picker and hotbar stay inside the logical canvas.
            var combatUi = CombatClientUI.Instance;
            yield return null;
            bool instructionGone = combatUi.ActionPanelRectForTest.width <= 0f;
            bool renderedUi = !Application.isBatchMode
                && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            combatUi.PickAttack();
            if (renderedUi)
            {
                float uiDeadline = Time.time + 2f;
                while (combatUi.ActionPanelRectForTest.width <= 0f && Time.time < uiDeadline)
                    yield return null;
            }
            Rect picker = combatUi.ActionPanelRectForTest;
            Rect bar = HotBar.BarRect;
            bool pickerResponsive = combatUi.AttackPickerOpenForTest
                && (!renderedUi || (picker.width > 0f && picker.height > 0f
                    && picker.xMin >= -0.5f && picker.xMax <= Ui.W + 0.5f
                    && picker.yMin >= -0.5f && picker.yMax <= bar.yMin + 0.5f));
            bool hotbarResponsive = !renderedUi || (bar.width > 0f && bar.height > 0f
                && bar.xMin >= -0.5f && bar.xMax <= Ui.W + 0.5f
                && bar.yMin >= -0.5f && bar.yMax <= Ui.H + 0.5f);
            bool combatUiReady = instructionGone && pickerResponsive && hotbarResponsive;
            string layoutResult = renderedUi
                ? $"Attack picker {picker.width:0}x{picker.height:0} inside {Ui.W:0}x{Ui.H:0}; " +
                  $"hotbar {bar.width:0}x{bar.height:0}"
                : "headless Attack path active; rendered bounds covered by -combatuicapture";
            Debug.Log($"[CombatUiTest] {(combatUiReady ? "PASS" : "FAIL")} - " +
                      $"{layoutResult}; permanent instruction window " +
                      $"{(instructionGone ? "removed" : "VISIBLE")}");

            var args = System.Environment.GetCommandLineArgs();
            int uiCaptureIndex = System.Array.IndexOf(args, "-combatuicapture");
            if (uiCaptureIndex >= 0 && uiCaptureIndex + 1 < args.Length)
            {
                string uiCapture = args[uiCaptureIndex + 1];
                string directory = System.IO.Path.GetDirectoryName(uiCapture);
                if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
                ScreenCapture.CaptureScreenshot(uiCapture);
                yield return new WaitForSeconds(1f);
                Debug.Log($"[CombatUiCapture] wrote responsive Attack picker to {uiCapture}");
            }
            Debug.Log($"[AttackTest] Attack button targets {enemy.Name} at {feet} ft " +
                      $"(move {combat.MoveLeft} ft, action {combat.ActionLeft})");
            combatUi.PickTarget(enemy.Id);

            float until = Time.time + 15f;
            while (combat.ActionLeft && combat.IsMyTurn && combat.InCombat.Value
                   && Time.time < until)
                yield return null;

            var now = combat.MyUnit;
            bool walked = now != null && now.Cell != from;
            // The blow itself, in the combat log's own words — a spent action alone would
            // only prove that SOMETHING was done with it.
            // Enemy HUD labels include the new runtime level suffix, while combat
            // narration deliberately keeps the natural creature name.
            int levelSuffix = enemy.Name.LastIndexOf(" (L", System.StringComparison.Ordinal);
            string narratedEnemy = levelSuffix > 0 ? enemy.Name.Substring(0, levelSuffix) : enemy.Name;
            string blow = combat.Log.LastOrDefault(
                l => l.Contains(mine.Name) && l.Contains(narratedEnemy)) ?? "";
            bool struck = !combat.ActionLeft && blow.Length > 0;
            bool visualFeedback = CombatFx.Instance != null
                                  && CombatFx.Instance.CombatPresentationReady
                                  && CombatFx.Instance.AttackFeedbackEvents > feedbackBefore;
            bool soundFeedback = GameAudio.Instance != null
                                 && GameAudio.Instance.SfxEventsPlayed > sfxBefore;
            bool licensedWeapon = GameAudio.Instance != null
                                  && GameAudio.Instance.LicensedSfxEventsPlayed > licensedBefore
                                  && GameAudio.Instance.LastLicensedCue.StartsWith("weapon_");
            bool battleMusic = GameAudio.Instance != null
                               && GameAudio.Instance.AssetMusicReady
                               && GameAudio.Instance.CombatTrackChanges > 0
                               && GameAudio.Instance.ActiveCombatTrackName.Length > 0;

            // Exercise the same spell-audio entry point used by RpcSpellAudio. Waiting
            // through the impact proves both the cast and impact recordings loaded.
            int spellBefore = GameAudio.Instance != null
                ? GameAudio.Instance.LicensedSfxEventsPlayed : -1;
            GameAudio.PlaySpell("fire_bolt", false);
            yield return new WaitForSeconds(0.25f);
            bool licensedSpell = GameAudio.Instance != null
                                 && GameAudio.Instance.LicensedSfxEventsPlayed >= spellBefore + 2
                                 && GameAudio.Instance.LastLicensedCue == "spell_fire_impact";
            bool assetAudio = GameAudio.Instance != null && GameAudio.Instance.AssetAudioReady;
            Debug.Log($"[CombatAudioTest] {(assetAudio && licensedWeapon && battleMusic ? "PASS" : "FAIL")} - " +
                      $"Action RPG battle track '{GameAudio.Instance?.ActiveCombatTrackName ?? "none"}', " +
                      $"licensed weapon SFX {(licensedWeapon ? "played" : "MISSING")}, " +
                      $"Caves and Dungeons {GameAudio.Instance?.CavesTrackCount ?? 0}/5 zones");
            Debug.Log($"[SpellAudioTest] {(licensedSpell ? "PASS" : "FAIL")} - " +
                      $"fire cast + impact, last cue '{GameAudio.Instance?.LastLicensedCue ?? "none"}'");
            bool pass = blockedApproach && weaponsVisible && combatLit && monsterHud
                        && combatUiReady
                        && struck && visualFeedback && soundFeedback && assetAudio
                        && licensedWeapon && licensedSpell && battleMusic
                        && (feet <= 5 || walked);   // in reach already? then no walk owed
            Debug.Log($"[AttackTest] {(pass ? "PASS" : "FAIL")} - one click at {feet} ft: " +
                      $"walked {(walked ? "into reach" : "NOWHERE")} " +
                      $"({from} to {(now != null ? now.Cell.ToString() : "?")}), attack " +
                      $"{(struck ? "resolved" : "NEVER LANDED")}, presentation " +
                      $"{(visualFeedback ? "FX" : "NO FX")}/{(soundFeedback ? "SFX" : "NO SFX")}, " +
                      $"lighting {(combatLit ? "LIT" : "DARK")}, " +
                      $"blocked-path {(blockedApproach ? "detour" : "NOT ARRANGED")}");
            Debug.Log($"[AttackTest] the blow: {(blow.Length > 0 ? blow : "(no log line!)")}");
        }

        private static int Chebyshev(Vector2Int a, Vector2Int b) =>
            Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        /// <summary>`RadiantPool.exe -autohost -warpsmith`: park the character at the smithy so
        /// the shop UI can be opened (E) and LOOKED AT without walking there by hand. A shop
        /// panel is only true on screen — the last one of these caught a button whose label the
        /// skin's padding had eaten.</summary>
        private System.Collections.IEnumerator ServerWarpToSmith()
        {
            yield return new WaitForSeconds(8f);
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            var smith = FindFirstObjectByType<SmithInteract>();
            if (holder == null || smith == null)
            {
                Debug.Log("[WarpSmith] FAIL - no player or no smith in the scene");
                yield break;
            }
            Warp(holder.transform, new Vector3(smith.transform.position.x + 1.5f,
                holder.transform.position.y, smith.transform.position.z));
            Debug.Log($"[WarpSmith] parked at {smith.VendorName} " +
                      $"({Vector3.Distance(holder.transform.position, smith.transform.position):0.0} m)");
        }

        /// <summary>Build-only regression for server-authoritative party travel: use the
        /// same RPC as the waystone UI to visit every commissioned site and return. This
        /// also proves each live zone has exactly its configured required encounters.</summary>
        private System.Collections.IEnumerator CampaignTravelSelfTest()
        {
            yield return new WaitForSeconds(8f);
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            var directory = FindObjectsByType<CampaignTravel>(FindObjectsSortMode.None)
                .FirstOrDefault(t => t.IsDirectory);
            var hub = FindObjectsByType<CampaignDestination>(FindObjectsSortMode.None)
                .FirstOrDefault(d => d.ZoneIndex == -1);
            if (holder == null || directory == null || hub == null)
            {
                Debug.Log("[TravelTest] FAIL - player or waystone anchors missing");
                yield break;
            }

            int oldMuster = MusterState.Value;
            var oldZones = ZoneStates.ToArray();
            var oldCounts = ZoneClearedCounts.ToArray();
            var oldActions = CompletedSiteActions.ToArray();
            MusterState.Value = (int)QuestState.Completed;

            int reached = 0;
            int authored = 0;
            int objectiveAnchors = 0;
            int returned = 0;
            var triggers = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None);
            var actions = FindObjectsByType<CampaignObjectiveInteract>(FindObjectsSortMode.None);
            var environmentArt = FindObjectsByType<EnvironmentArtTag>(FindObjectsSortMode.None);
            int rpgPolyArt = environmentArt.Count(a => a.SourcePack == "RpgPoly");
            int natureArt = environmentArt.Count(a => a.SourcePack == "SimpleNature");
            int graveyardArt = environmentArt.Count(a => a.SourcePack == "GraveyardNature");
            int dungeonArt = environmentArt.Count(a => a.SourcePack == "Dungeon");
            int paintedGround = environmentArt.Count(a => a.SourcePack == "HandpaintedGrass");
            for (int i = 0; i < Zones.Length; i++)
            {
                if (i < ZoneStates.Count) ZoneStates[i] = (int)QuestState.Active;
                Warp(holder.transform, directory.transform.position + Vector3.right * 1.5f);
                CmdTravelTo(i, holder.Owner);
                yield return new WaitForSeconds(0.15f);

                var site = FindObjectsByType<CampaignDestination>(FindObjectsSortMode.None)
                    .FirstOrDefault(d => d.ZoneIndex == i);
                if (site != null
                    && Vector3.Distance(holder.transform.position, site.transform.position) < 3f)
                    reached++;
                if (triggers.Count(t => t.ZoneId == Zones[i].ZoneId && t.RequiredForClear)
                    == Zones[i].RequiredEncounters)
                    authored++;
                if (string.IsNullOrEmpty(Zones[i].SiteAction)
                    || actions.Count(a => a.ZoneIndex == i) == 1)
                    objectiveAnchors++;

                CmdTravelTo(-1, holder.Owner);
                yield return new WaitForSeconds(0.15f);
                if (Vector3.Distance(holder.transform.position, hub.transform.position) < 3f)
                    returned++;
            }

            int actionZone = System.Array.FindIndex(Zones, z => !string.IsNullOrEmpty(z.SiteAction));
            bool actionResolved = false;
            bool rewardPaid = false;
            if (actionZone >= 0)
            {
                var objective = actions.FirstOrDefault(a => a.ZoneIndex == actionZone);
                if (objective != null)
                {
                    int goldBefore = PartyGold.Value;
                    int xpBefore = holder.Sheet.Xp;
                    int stashBefore = Stash.Count;
                    ZoneStates[actionZone] = (int)QuestState.Active;
                    ZoneClearedCounts[actionZone] = Zones[actionZone].RequiredEncounters;
                    Warp(holder.transform, objective.transform.position + Vector3.right * 1.5f);
                    CmdResolveSiteAction(actionZone, 0, holder.Owner);
                    yield return new WaitForSeconds(0.2f);
                    actionResolved = IsSiteActionComplete(actionZone)
                                     && GetZoneState(actionZone) == QuestState.ObjectivesMet;
                    if (actionResolved)
                    {
                        CmdDialogueChoice($"turnin_{actionZone}", holder.Owner);
                        yield return new WaitForSeconds(0.2f);
                        var cfg = Zones[actionZone];
                        int expectedItems = LootLibrary.Get(cfg.SiteActionLootTable).Rolls
                                          + LootLibrary.Get(cfg.RewardLootTable).Rolls;
                        rewardPaid = PartyGold.Value == goldBefore + cfg.SiteActionGold + cfg.Gold
                                     && holder.Sheet.Xp == xpBefore + cfg.SiteActionXp + cfg.XpEach
                                     && Stash.Count == stashBefore + expectedItems
                                     && GetZoneState(actionZone) == QuestState.Completed;
                    }
                }
            }

            MusterState.Value = oldMuster;
            for (int i = 0; i < oldZones.Length && i < ZoneStates.Count; i++)
                ZoneStates[i] = oldZones[i];
            for (int i = 0; i < oldCounts.Length && i < ZoneClearedCounts.Count; i++)
                ZoneClearedCounts[i] = oldCounts[i];
            CompletedSiteActions.Clear();
            foreach (string action in oldActions) CompletedSiteActions.Add(action);
            bool pass = reached == Zones.Length && authored == Zones.Length
                        && objectiveAnchors == Zones.Length
                        && actionResolved
                        && rewardPaid
                        && rpgPolyArt > 0 && natureArt > 0 && dungeonArt > 0
                        && graveyardArt >= CampaignExpansionContent.Sites.Length * 14
                        && paintedGround == CampaignExpansionContent.Sites.Length
                        && returned == Zones.Length;
            Debug.Log($"[TravelTest] {(pass ? "PASS" : "FAIL")} - " +
                      $"{reached}/{Zones.Length} sites reached; " +
                      $"{authored}/{Zones.Length} encounter sets authored; " +
                      $"{objectiveAnchors}/{Zones.Length} objectives anchored; " +
                      $"site objective {(actionResolved ? "resolved" : "failed")}; " +
                      $"side/main rewards {(rewardPaid ? "paid" : "failed")}; " +
                      $"environment art RPG x{rpgPolyArt}, nature x{natureArt}, " +
                      $"graveyard/nature2 x{graveyardArt}, dungeon x{dungeonArt}, " +
                      $"painted ground x{paintedGround}; " +
                      $"{returned}/{Zones.Length} hub returns");
        }

        /// <summary>Built-player proof for the complete dynamic loop: a level-20 hero
        /// receives tier-7 gear from an authored tier-1 quest parcel and spawns level-19
        /// quest monsters. Kept isolated from attacktest because it starts its own fight.</summary>
        private System.Collections.IEnumerator EncounterScalingSelfTest()
        {
            yield return new WaitForSeconds(8f);
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Sheet != null);
            var combat = CombatManager.Instance;
            if (holder == null || combat == null)
            {
                Debug.Log("[ScalingTest] FAIL - player or combat manager missing");
                yield break;
            }

            ServerGrantXp(holder, ClassData.XpThresholds[Progression.MaxLevel - 1]
                                  - holder.Sheet.Xp);
            int stashBefore = Stash.Count;
            string reward = ServerAwardRewardLoot("lt_quest_tier1");
            var tier7 = LootLibrary.Get("lt_quest_tier7").Entries
                .Where(e => e.ItemId != null).Select(e => e.ItemId).ToHashSet();
            var newItems = Stash.Skip(stashBefore).ToArray();
            bool rewardScaled = newItems.Length == LootLibrary.Get("lt_quest_tier7").Rolls
                                && newItems.All(tier7.Contains);
            var staff = GameItem.Get("runed_staff");
            var quarterstaff = GameItem.Get("quarterstaff");
            bool casterUpgrade = staff != null && quarterstaff != null
                && staff.UsableBy(CharacterClass.Wizard)
                && staff.AverageDamage(0, 0) > quarterstaff.AverageDamage(0, 0);

            var trigger = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                .FirstOrDefault(t => t.RequiredForClear);
            if (trigger == null)
            {
                Debug.Log("[ScalingTest] FAIL - required encounter missing");
                yield break;
            }
            combat.StartEncounter(trigger);
            yield return new WaitForSeconds(0.5f);
            bool monsterScaled = combat.InCombat.Value
                && combat.EncounterCharacterLevel == Progression.MaxLevel
                && combat.EncounterMonsterLevel == Progression.MaxLevel - 1
                && combat.AllSpawnedMonstersMatchEncounterLevel;
            bool pass = holder.Sheet.Level == Progression.MaxLevel
                        && rewardScaled && casterUpgrade && monsterScaled;
            Debug.Log($"[ScalingTest] {(pass ? "PASS" : "FAIL")} - hero L{holder.Sheet.Level} " +
                      $"spawned monsters L{combat.EncounterMonsterLevel}; authored tier1 -> " +
                      $"tier7 items x{newItems.Length} ({reward}); Runed Staff caster upgrade " +
                      $"{(casterUpgrade ? "ready" : "missing")}");
        }

        /// <summary>Regression for the exact E-key path at The Watchers Below. Every
        /// campaign objective gets Update(), so this must survive a frame in which all
        /// distant anchors also run before the spectral-watch choice is resolved.</summary>
        private System.Collections.IEnumerator SiteActionInputSelfTest()
        {
            yield return new WaitForSeconds(8f);
            int zone = System.Array.FindIndex(Zones, z => z.ZoneId == "drowned_bastion");
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            var objective = FindObjectsByType<CampaignObjectiveInteract>(FindObjectsSortMode.None)
                .FirstOrDefault(o => o.ZoneIndex == zone);
            int anchors = FindObjectsByType<CampaignObjectiveInteract>(FindObjectsSortMode.None).Length;
            if (zone < 0 || holder == null || objective == null)
            {
                Debug.Log("[SiteActionInputTest] FAIL - Drowned Bastion player or objective missing");
                yield break;
            }

            Ui.CloseAll();
            ZoneStates[zone] = (int)QuestState.Active;
            ZoneClearedCounts[zone] = Zones[zone].RequiredEncounters;
            Warp(holder.transform, objective.transform.position + Vector3.right * 1.5f);
            yield return null;

            bool accepted = objective.TryInteract();
            yield return null; // every other CampaignObjectiveInteract.Update gets a turn
            bool panelStayedOpen = objective.OwnsOpenPanel;
            CmdResolveSiteAction(zone, 0, holder.Owner);
            yield return new WaitForSeconds(0.25f);
            bool resolved = IsSiteActionComplete(zone)
                            && GetZoneState(zone) == QuestState.ObjectivesMet
                            && SiteActionResult(zone) == Zones[zone].ChoiceA;
            Ui.CloseAll();

            bool pass = anchors > 1 && accepted && panelStayedOpen && resolved;
            Debug.Log($"[SiteActionInputTest] {(pass ? "PASS" : "FAIL")} - " +
                      $"E opened the spectral-watch choice and it survived {anchors - 1} " +
                      $"distant objective updates; decision " +
                      $"{(resolved ? $"recorded as '{SiteActionResult(zone)}'" : "NOT RECORDED")}");
        }

        /// <summary>Screenshot-only QA path: park north of the council forecourt, face the
        /// hall and its four flame posts, manufacture a ready turn-in, force midnight,
        /// capture, then restore campaign state and wall time. No synthetic input.</summary>
        private System.Collections.IEnumerator QuestLampCapture(string path)
        {
            yield return new WaitForSeconds(8f);
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            if (holder == null)
            {
                Debug.Log("[QuestLampCapture] FAIL - no player");
                yield break;
            }

            int oldMuster = MusterState.Value;
            var oldStates = ZoneStates.ToArray();
            MusterState.Value = (int)QuestState.Completed;
            for (int i = 0; i < ZoneStates.Count; i++)
                ZoneStates[i] = (int)QuestState.Locked;
            if (ZoneStates.Count > 0) ZoneStates[0] = (int)QuestState.ObjectivesMet;

            Warp(holder.transform, new Vector3(0f, 0.1f, -5f));
            holder.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (orbit != null) orbit.SetPresentationView(180f, 22f, 10f);
            ServerSetWorldHourForTest(0f);
            yield return new WaitForSeconds(2f);
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(2f);
            MusterState.Value = oldMuster;
            for (int i = 0; i < oldStates.Length; i++) ZoneStates[i] = oldStates[i];
            ServerClearWorldHourTestOverride();
            Debug.Log($"[QuestLampCapture] wrote midnight Council Hall turn-in frame to {path}; " +
                      $"restored host computer time {WorldHour.Value:0.00}");
        }

        /// <summary>Screenshot-only QA path for a migrated completed save. It parks at the
        /// Lightwell facing the newly opened postern and Ashen Ward while the normal quest
        /// card, toast, world marker, and steering arrow render from real campaign state.</summary>
        private System.Collections.IEnumerator NextQuestCapture(string path)
        {
            yield return new WaitForSeconds(8f);
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            if (holder == null)
            {
                Debug.Log("[NextQuestCapture] FAIL - no player");
                yield break;
            }

            Warp(holder.transform, new Vector3(39f, 0.1f, 18f));
            holder.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (orbit != null) orbit.SetPresentationView(35f, 24f, 16f);
            yield return new WaitForSeconds(2f);
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(2f);
            Debug.Log($"[NextQuestCapture] wrote Lightwell-to-Ashen-Ward waypoint frame to {path}");
        }

        /// <summary>Screenshot-only QA for the expansion's remote-cell composition.
        /// Parks at the final spire with a real active quest, daylight, world label,
        /// encounters, objective marker, and shared-palette environment all visible.</summary>
        private System.Collections.IEnumerator CampaignSiteCapture(string path)
        {
            yield return new WaitForSeconds(8f);
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
            var args = System.Environment.GetCommandLineArgs();
            int requestedIndex = System.Array.IndexOf(args, "-sitezone");
            string requestedZone = requestedIndex >= 0 && requestedIndex + 1 < args.Length
                ? args[requestedIndex + 1] : "ember_crown_spire";
            int zone = System.Array.FindIndex(Zones, z => z.ZoneId == requestedZone);
            var destination = FindObjectsByType<CampaignDestination>(FindObjectsSortMode.None)
                .FirstOrDefault(d => d.ZoneIndex == zone);
            if (holder == null || zone < 0 || destination == null)
            {
                Debug.Log("[SiteCapture] FAIL - player or final site missing");
                yield break;
            }

            int oldMuster = MusterState.Value;
            var oldStates = ZoneStates.ToArray();
            MusterState.Value = (int)QuestState.Completed;
            for (int i = 0; i < ZoneStates.Count; i++) ZoneStates[i] = (int)QuestState.Locked;
            ZoneStates[zone] = (int)QuestState.Active;
            // Destination anchors sit 14 m south of each cell's centre. Move into the
            // composition and look back across it so the camera is not outside the low
            // perimeter wall or tucked behind the return stone.
            Warp(holder.transform, destination.transform.position + new Vector3(0f, 0f, 14f));
            holder.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var orbit = Camera.main != null ? Camera.main.GetComponent<OrbitCamera>() : null;
            if (orbit != null) orbit.SetPresentationView(180f, 34f, 16f);
            ServerSetWorldHourForTest(14f);
            yield return new WaitForSeconds(2f);
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(2f);
            MusterState.Value = oldMuster;
            for (int i = 0; i < oldStates.Length; i++) ZoneStates[i] = oldStates[i];
            ServerClearWorldHourTestOverride();
            Debug.Log($"[SiteCapture] wrote {Zones[zone].QuestName} frame to {path}");
        }

        /// <summary>Screenshot-only QA for the maximized campaign atlas. It opens the map
        /// through MiniMap's public control path, demonstrates completed/active/locked route
        /// states, captures, and restores the campaign without sending desktop input.</summary>
        private System.Collections.IEnumerator WorldMapCapture(string path)
        {
            yield return new WaitForSeconds(8f);
            var map = FindFirstObjectByType<MiniMap>();
            if (map == null)
            {
                Debug.Log("[WorldMapCapture] FAIL - minimap missing");
                yield break;
            }

            int oldMuster = MusterState.Value;
            var oldStates = ZoneStates.ToArray();
            MusterState.Value = (int)QuestState.Completed;
            for (int i = 0; i < ZoneStates.Count; i++)
                ZoneStates[i] = (int)QuestState.Locked;

            void SetAtlasState(string zoneId, QuestState state)
            {
                int i = System.Array.FindIndex(Zones, z => z.ZoneId == zoneId);
                if (i >= 0) ZoneStates[i] = (int)state;
            }
            SetAtlasState("old_docks", QuestState.Completed);
            SetAtlasState("drowned_market", QuestState.Active);
            SetAtlasState("drowned_bastion", QuestState.Completed);
            SetAtlasState("loomhouse_enclave", QuestState.Active);
            SetAtlasState("cinderwell_yard", QuestState.Completed);
            SetAtlasState("cinderwell_undercroft", QuestState.Active);

            map.ShowCampaignAtlasForTest();
            yield return new WaitForSeconds(2f);
            int activeCommissions = ZoneStates.Count(state =>
                (QuestState)state == QuestState.Active);
            int questXs = map.AtlasObjectiveXCountForTest;
            string objective = map.AtlasObjectiveZoneIdForTest;
            bool singleX = activeCommissions == 3 && questXs == 1
                           && !string.IsNullOrEmpty(objective);
            Debug.Log($"[WorldMapObjectiveTest] {(singleX ? "PASS" : "FAIL")} - " +
                      $"{questXs} X marks '{objective}' while " +
                      $"{activeCommissions} commissions are active");
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(2f);
            MusterState.Value = oldMuster;
            for (int i = 0; i < oldStates.Length; i++) ZoneStates[i] = oldStates[i];
            Debug.Log($"[WorldMapCapture] wrote six-region campaign atlas to {path}");
        }

        /// <summary>A teleport that sticks. A CharacterController overwrites a direct
        /// transform write on the very next frame (CombatFx parks it to glide for the same
        /// reason) — so park it here too, or the body simply stays where it was.</summary>
        private static void Warp(Transform body, Vector3 pos)
        {
            var cc = body.GetComponent<CharacterController>();
            bool parked = cc != null && cc.enabled;
            if (parked) cc.enabled = false;
            body.position = pos;
            if (parked) cc.enabled = true;
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdSellAll(NetworkConnection conn = null)
        {
            int total = 0;
            for (int i = Stash.Count - 1; i >= 0; i--)
            {
                if (Stash[i] == "potion_healing") continue;   // keep potions
                if (SellValue.TryGetValue(Stash[i], out int v)) total += v;
                Stash.RemoveAt(i);
            }
            if (total == 0) { RpcNotice("Nothing to sell."); return; }
            PartyGold.Value += total;
            RpcNotice($"Sold salvage for {total} gold.");
        }

        /// <summary>Equip shared-stash loot onto the caller or one named active companion.
        /// Class restrictions are enforced server-side and the replaced item returns to
        /// the stash. Companion loadouts remain independent after their initial hire.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdEquipItem(string itemId, string companionName = "",
            NetworkConnection conn = null)
        {
            var item = GameItem.Get(itemId);
            var caller = RecruitingLeader(conn);
            if (caller == null) return;
            var holder = string.IsNullOrWhiteSpace(companionName) ? caller
                : FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => p.IsCompanion && p.Sheet != null
                        && string.Equals(p.Sheet.Name, companionName.Trim(),
                            System.StringComparison.OrdinalIgnoreCase));
            if (item == null || holder?.Sheet == null) return;
            ServerEquipStashItem(item, holder);
        }

        [Server]
        private bool ServerEquipStashItem(GameItem item, PlayerCharacterHolder holder)
        {
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
            { RpcNotice("Not during combat."); return false; }
            int idx = Stash.IndexOf(item.Id);
            if (idx < 0) { RpcNotice("That item is not in the stash."); return false; }
            if (!item.UsableBy(holder.Sheet.Class))
            { RpcNotice($"A {holder.Sheet.Class} cannot use {item.Name}."); return false; }

            Stash.RemoveAt(idx);
            string previous = holder.ServerEquip(item);
            if (previous != null) Stash.Add(previous);
            RpcNotice($"{holder.Sheet.Name} equips {item.Name}" +
                      (item.Slot == ItemSlot.Armor ? $" (AC {holder.Sheet.ArmorClass})" : "") + ".");
            ServerSaveCampaign();
            return true;
        }

        /// <summary>Party stash potion accessors for CombatManager — drinking mid-fight is
        /// a combat ACTION and resolves there (CmdDrinkPotion), but the potions live in
        /// the shared stash, which is this object's server-authoritative state.</summary>
        [Server]
        public bool ServerHasPotion() => Stash.Contains("potion_healing");

        [Server]
        public bool ServerConsumePotion()
        {
            int idx = Stash.IndexOf("potion_healing");
            if (idx < 0) return false;
            Stash.RemoveAt(idx);
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdUsePotion(NetworkConnection conn = null)
        {
            // In a fight, drinking costs your action and goes through the combat FSM.
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
            { RpcNotice("Drink it on your turn — the potion button costs your action."); return; }
            int idx = Stash.IndexOf("potion_healing");
            if (idx < 0) { RpcNotice("No potions in the party stash."); return; }
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.Owner == conn);
            if (holder?.Sheet == null || holder.Sheet.IsDead) return;
            var rng = new RadiantPool.Rules.SeededRng(System.Environment.TickCount);
            int healed = holder.Sheet.Heal(RadiantPool.Rules.Dice.Roll("2d4+2", rng).Total);
            Stash.RemoveAt(idx);
            RpcNotice($"{holder.Sheet.Name} drinks a potion (+{healed} HP).");
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdLongRest(NetworkConnection conn = null)
        {
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value) return;
            foreach (var p in FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None))
                if (p.Sheet != null) RadiantPool.Rules.Rest.LongRest(p.Sheet);
            RpcNotice("The party takes a long rest — HP and spell slots restored.");
        }

        /// <summary>The ONE road XP travels, whether it came from a quest or from a monster
        /// (CombatManager calls this too — it used to add XP without ever levelling anyone,
        /// so kills only paid out later, when a quest award happened to run the level loop).
        /// Levels are applied here, and each one announces the ability points it granted.
        /// Companions have nobody at the keyboard, so they spend their points themselves.</summary>
        [Server]
        public void ServerGrantXp(PlayerCharacterHolder p, int amount)
        {
            if (p == null || p.Sheet == null || amount <= 0) return;
            p.Sheet.GainXp(amount);

            while (p.Sheet.CanLevelUp)
            {
                var result = p.Sheet.LevelUp();
                if (p.IsCompanion)
                {
                    ServerAutoSpend(p.Sheet, result.AbilityPointsGranted);
                    RpcNotice($"{p.Sheet.Name} reaches level {result.NewLevel}! " +
                              $"(+{result.HpGained} HP)");
                    continue;
                }
                string points = result.AbilityPointsGranted == 1
                    ? "1 ability point" : $"{result.AbilityPointsGranted} ability points";
                RpcNotice($"{p.Sheet.Name} reaches level {result.NewLevel}! " +
                          $"(+{result.HpGained} HP, {points} to spend - press L)");
            }
        }

        /// <summary>An AI companion has no one to choose for it: it puts its points into the
        /// ability its class lives by, and into Constitution once that is capped.</summary>
        [Server]
        private static void ServerAutoSpend(RadiantPool.Rules.CharacterSheet sheet, int points)
        {
            var primary = RadiantPool.Rules.Progression.PrimaryAbility(sheet.Class);
            for (int i = 0; i < points; i++)
            {
                if (sheet.CanSpendPointOn(primary)) sheet.SpendAbilityPoint(primary);
                else if (sheet.CanSpendPointOn(RadiantPool.Rules.Ability.Con))
                    sheet.SpendAbilityPoint(RadiantPool.Rules.Ability.Con);
                else break;
            }
        }

        /// <summary>Bring a fresh hire to the leader's exact XP/level, auto-spend the hire's
        /// earned points, and copy the leader's equipment tier before the NetworkObject is
        /// spawned. Equal XP awards thereafter keep the party level-locked naturally.</summary>
        [Server]
        private static void ServerMatchCompanionToLeader(PlayerCharacterHolder companion,
            PlayerCharacterHolder leader)
        {
            if (companion?.Sheet == null || leader?.Sheet == null) return;
            int delta = System.Math.Max(0, leader.Sheet.Xp - companion.Sheet.Xp);
            companion.Sheet.GainXp(delta);
            while (companion.Sheet.CanLevelUp)
            {
                var result = companion.Sheet.LevelUp();
                ServerAutoSpend(companion.Sheet, result.AbilityPointsGranted);
            }
            companion.ServerMatchEquipment(leader);
        }

        [Server]
        private void AwardXpToAll(int amount)
        {
            foreach (var p in FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None))
                ServerGrantXp(p, amount);
        }

        /// <summary>Spend one of the points a level-up granted. The sheet is server-only, so
        /// the client can only ASK — and the rules lib is the one that says yes (points in
        /// hand, score below the SRD's 20). A refusal comes back as a notice, never silence,
        /// and nothing thrown escapes the RPC.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdSpendAbilityPoint(int abilityIndex, NetworkConnection conn = null)
        {
            if (abilityIndex < 0 || abilityIndex > 5) return;
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.Owner == conn);
            if (holder == null || holder.Sheet == null) return;

            var ability = (RadiantPool.Rules.Ability)abilityIndex;
            try
            {
                holder.Sheet.SpendAbilityPoint(ability);
            }
            catch (RadiantPool.Rules.RuleViolationException e)
            {
                RpcNotice(e.Message);
                return;
            }
            RpcNotice($"{holder.Sheet.Name}: {ability} raised to " +
                      $"{holder.Sheet.Abilities[ability]} " +
                      $"({holder.Sheet.PendingAbilityPoints} point" +
                      $"{(holder.Sheet.PendingAbilityPoints == 1 ? "" : "s")} left).");
            ServerSaveCampaign();   // a spent point is permanent: persist it at once
        }

        // ---------------- client ----------------

        [ObserversRpc]
        private void RpcNotice(string text)
        {
            LocalNotice = text;
            _noticeUntil = Time.time + 6f;
            // Player.log is the first place we look when a bug is reported — a notice that
            // only ever existed on screen for six seconds is exactly what we end up missing.
            Debug.Log($"[RadiantPool] notice: {text}");
        }

        private Vector2 _journalScroll;

        public void ToggleJournal() => Ui.Toggle(Ui.Panel.Journal);

        private void Update()
        {
            if (IsServerStarted)
            {
                ServerRestoreActiveCompanionsWhenReady();
                if (!_worldTimeOverride)
                    _serverWorldHour = ComputerLocalHourNow();
                if (Time.unscaledTime >= _nextWorldTimeSync)
                {
                    _nextWorldTimeSync = Time.unscaledTime + 0.25f;
                    WorldHour.Value = _serverWorldHour;
                }
            }
            if (Input.GetKeyDown(KeyCode.J) && !Ui.Typing) Ui.Toggle(Ui.Panel.Journal);
            if (Input.GetKeyDown(KeyCode.F5) && IsServerStarted)
            {
                ServerSaveCampaign();
                RpcNotice("Campaign saved.");
            }
        }

        private void OnGUI()
        {
            Ui.Begin();
            if (Time.time < _noticeUntil && LocalNotice.Length > 0)
                Theme.DrawToast(Ui.W / 2f, 16, LocalNotice);

            if (!Ui.IsOpen(Ui.Panel.Journal)) return;
            var rect = Ui.FitTop(440f, 480f, top: 92f, bottomMargin: 100f);
            GUILayout.BeginArea(rect, Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Journal", Theme.Header);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#d0c5af>J or Esc to close</color>", Theme.Body);
            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(22)))
                Ui.Close(Ui.Panel.Journal);
            GUILayout.EndHorizontal();

            _journalScroll = GUILayout.BeginScrollView(_journalScroll,
                GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            DrawQuest("Report to the Council", (QuestState)MusterState.Value,
                "Enter the lamplit Council Hall forecourt and speak with Councilor Veresk " +
                "(follow the red X on the minimap).", -1f);
            for (int i = 0; i < Zones.Length; i++)
            {
                var cfg = Zones[i];
                int done = Mathf.Min(i < ZoneClearedCounts.Count ? ZoneClearedCounts[i] : 0,
                    cfg.RequiredEncounters);
                string detail = (cfg.Description.Length > 0
                        ? cfg.Description + "\n" : $"Clear {cfg.DisplayName}. ")
                    + $"<b>Cleared {done}/{cfg.RequiredEncounters}</b> — the red X on the " +
                    "minimap marks the nearest fight.";
                if (!string.IsNullOrEmpty(cfg.SiteAction))
                {
                    string result = SiteActionResult(i);
                    detail += "\n" + (IsSiteActionComplete(i)
                        ? $"<b>Site objective complete</b>" +
                          (result == "completed" || string.IsNullOrEmpty(result)
                              ? "." : $": {result}.")
                        : $"<b>Site objective:</b> {cfg.SiteAction}");
                }
                detail += $"\n<b>Turn-in reward:</b> {cfg.XpEach:N0} XP each, " +
                          $"{cfg.Gold:N0} gold" +
                          (string.IsNullOrEmpty(cfg.RewardLootTable)
                              ? "." : ", plus level-matched equipment.");
                if (cfg.SiteActionXp > 0 || cfg.SiteActionGold > 0)
                    detail += $"\n<b>Side objective reward:</b> {cfg.SiteActionXp:N0} XP each, " +
                              $"{cfg.SiteActionGold:N0} gold" +
                              (string.IsNullOrEmpty(cfg.SiteActionLootTable)
                                  ? "." : ", plus level-matched equipment.");
                DrawQuest(cfg.QuestName, GetZoneState(i), detail,
                    (float)done / cfg.RequiredEncounters, i);
            }
            if (CampaignComplete.Value)
                GUILayout.Label("<color=#8a6d1f><b>Aldenmere stands free.</b> The campaign is " +
                    "complete!</color>", Theme.BodyInk);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            int potions = Stash.Count(s => s == "potion_healing");
            int salvage = Stash.Count - potions;
            GUILayout.Label($"<color=#f2ca50><b>{PartyGold.Value:N0}</b> gold</color>" +
                $"   <color=#d0c5af>Potions: <b>{potions}</b>   Salvage: <b>{salvage}</b></color>",
                Theme.Body);
            bool inCombat = CombatManager.Instance != null && CombatManager.Instance.InCombat.Value;
            if (!inCombat)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = potions > 0;
                if (GUILayout.Button("Drink potion")) CmdUsePotion();
                GUI.enabled = true;
                if (GUILayout.Button("Long rest")) CmdLongRest();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        /// <summary>One journal entry, mock-style: active quests sit in a gold-wash card
        /// with a progress bar; completed quests are muted; ready-to-turn-in glows.</summary>
        private void DrawQuest(string title, QuestState state, string detail, float progress,
            int zoneIndex = -1)
        {
            if (state == QuestState.Locked) return;
            if (state == QuestState.Completed)
            {
                // Markers are ASCII: the shipped fonts have no ✔/★ glyph, and a missing
                // glyph renders as a tofu box rather than the symbol you meant.
                GUILayout.Label($"{Theme.Check(true)}  <color=#6b6257>{title}</color>",
                    Theme.BodyInk);
                return;
            }

            GUILayout.BeginVertical(Theme.GoldWashStyle);
            string mark = state == QuestState.ObjectivesMet
                ? "<color=#8a6d1f><b>[!]</b></color>" : "<color=#8a6d1f><b>[ ]</b></color>";
            GUILayout.Label($"{mark}  <b>{title}</b>", Theme.HeaderInk);
            GUILayout.Label(state == QuestState.ObjectivesMet
                ? "<i>TURN IN: Follow the gold marker to Council Hall and speak with " +
                  "Councilor Veresk for your reward.</i>" : detail, Theme.BodyInk);
            if (progress >= 0f && state == QuestState.Active)
            {
                var r = GUILayoutUtility.GetRect(240, 9);
                Theme.Bar(new Rect(r.x + 2, r.y + 1, 220, 7), progress, Theme.MpBlue);
                bool tracked = zoneIndex >= 0 && QuestTracker.IsTrackedZone(zoneIndex);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (tracked)
                    GUILayout.Label("TRACKED", Theme.Caps, GUILayout.Width(76f));
                else if (zoneIndex >= 0 && GUILayout.Button("Track waypoint",
                             GUILayout.Width(126f), GUILayout.Height(24f)))
                    QuestTracker.Instance?.TrackZone(zoneIndex);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.Space(3);
        }
    }
}
