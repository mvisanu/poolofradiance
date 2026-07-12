using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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
            public int RequiredEncounters = 3;
            public int XpEach = 300;
            public int Gold = 100;
        }

        [Header("Zone chain (from content JSON, wired by bootstrap)")]
        public ZoneConfig[] Zones = System.Array.Empty<ZoneConfig>();

        public readonly SyncVar<int> MusterState = new SyncVar<int>((int)QuestState.Active);
        public readonly SyncList<int> ZoneStates = new SyncList<int>();
        public readonly SyncList<int> ZoneClearedCounts = new SyncList<int>();
        public readonly SyncVar<bool> CampaignComplete = new SyncVar<bool>(false);
        public readonly SyncVar<int> PartyGold = new SyncVar<int>(0);

        /// <summary>Party-shared loot stash (item ids from content/items).</summary>
        public readonly SyncList<string> Stash = new SyncList<string>();

        /// <summary>Sell value in gold = half list price (items.json costCp / 100 / 2).</summary>
        public static readonly System.Collections.Generic.Dictionary<string, int> SellValue =
            new System.Collections.Generic.Dictionary<string, int>
        {
            { "dagger", 1 }, { "shortsword", 5 }, { "longsword", 7 }, { "mace", 2 },
            { "quarterstaff", 1 }, { "light_crossbow", 12 }, { "shortbow", 12 },
            { "leather_armor", 5 }, { "scale_mail", 25 }, { "chain_mail", 37 },
            { "shield", 5 }, { "potion_healing", 25 }, { "torch", 1 }
        };
        public const int PotionBuyPrice = 50;

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
        /// <summary>Cleared encounter ids, replicated so clients can point quest markers
        /// at the nearest block that still needs fighting.</summary>
        public readonly SyncList<string> ConsumedEncounterIds = new SyncList<string>();

        public override void OnStartServer()
        {
            base.OnStartServer();
            for (int i = 0; i < Zones.Length; i++)
            {
                ZoneStates.Add((int)QuestState.Locked);
                ZoneClearedCounts.Add(0);
            }

            if (!SaveSystem.Exists) return;
            var save = SaveSystem.Read();
            if (save == null) return;

            MusterState.Value = save.MusterState;
            for (int i = 0; i < Zones.Length && i < save.ZoneStates.Count; i++)
            {
                ZoneStates[i] = save.ZoneStates[i];
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
            foreach (var id in save.ConsumedEncounters)
            {
                ConsumedEncounterIds.Add(id);
                var trigger = FindObjectsByType<EncounterTrigger>(FindObjectsSortMode.None)
                    .FirstOrDefault(t => t.EncounterId == id);
                if (trigger != null) trigger.Consume();
            }
            string savedDate = save.SavedAtUtc.Length >= 10
                ? save.SavedAtUtc.Substring(0, 10) : save.SavedAtUtc;
            RpcNotice($"Campaign loaded (saved {savedDate}).");
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
                Roster = _roster.Select(kv =>
                    SaveSystem.Capture(kv.Value, _builds[kv.Key])).ToList()
            };
            SaveSystem.Write(save);
        }

        [Server]
        public void ServerEncounterCleared(EncounterTrigger trigger)
        {
            if (trigger == null) return;
            int zone = System.Array.FindIndex(Zones, z => z.ZoneId == trigger.ZoneId);
            trigger.Consume();
            ConsumedEncounterIds.Add(trigger.EncounterId);
            ServerSaveCampaign();   // autosave at every cleared block
            if (zone < 0 || !trigger.RequiredForClear) return;

            ZoneClearedCounts[zone]++;
            var cfg = Zones[zone];
            if (GetZoneState(zone) == QuestState.Active
                && ZoneClearedCounts[zone] >= cfg.RequiredEncounters)
            {
                ZoneStates[zone] = (int)QuestState.ObjectivesMet;
                RpcNotice($"{cfg.DisplayName} has been cleared! Return to Councilor Veresk.");
            }
            else
            {
                RpcNotice($"Encounter cleared ({Mathf.Min(ZoneClearedCounts[zone], cfg.RequiredEncounters)}" +
                          $"/{cfg.RequiredEncounters} in {cfg.DisplayName}).");
            }
        }

        [Header("Companions (prefab wired by bootstrap)")]
        public FishNet.Object.NetworkObject CompanionPrefab;

        private static readonly string[] CompanionNames =
            { "Sera", "Aldric", "Wren", "Torvald", "Isolde", "Fenn" };

        /// <summary>Fills the party to 4 with AI companions, picking classes the party
        /// lacks (fighter first, then cleric, wizard, rogue).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdRecruitCompanions(NetworkConnection conn = null)
        {
            if (CompanionPrefab == null) { RpcNotice("No sellswords available."); return; }
            var holders = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.Sheet != null).ToList();
            int needed = 4 - holders.Count;
            if (needed <= 0) { RpcNotice("The party is already full."); return; }

            var have = holders.Select(h => (int)h.Sheet.Class)
                .ToHashSet();
            var priority = new[] { 0, 1, 2, 3 };   // fighter, cleric, wizard, rogue
            var rng = new System.Random();
            int spawned = 0;
            foreach (int cls in priority.Where(c => !have.Contains(c))
                         .Concat(priority).Take(needed))
            {
                var anchor = holders.FirstOrDefault(h => !h.IsCompanion);
                Vector3 pos = anchor != null
                    ? anchor.transform.position + new Vector3(1.5f + spawned, 0.2f, -1.5f)
                    : new Vector3(0, 0.2f, -8);
                var nob = Instantiate(CompanionPrefab, pos, Quaternion.identity);
                FishNet.InstanceFinder.ServerManager.Spawn(nob);   // no owner = server AI
                var holder = nob.GetComponent<PlayerCharacterHolder>();
                string name = CompanionNames[rng.Next(CompanionNames.Length)];
                int suffix = 2;
                while (holders.Any(h => h.Sheet != null && h.Sheet.Name == name))
                    name = name + " " + suffix++;
                holder.ServerInitCompanion(name, cls);
                var identity = nob.GetComponent<PlayerIdentity>();
                if (identity != null) identity.ServerSetName(name);
                holders.Add(holder);
                spawned++;
            }
            RpcNotice($"{spawned} sellsword{(spawned == 1 ? "" : "s")} joined the party!");
        }

        /// <summary>Dialogue actions arrive from whichever client is talking to the NPC.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdDialogueChoice(string action, NetworkConnection conn = null)
        {
            if (action == "muster_accept" && MusterState.Value == (int)QuestState.Active)
            {
                MusterState.Value = (int)QuestState.Completed;
                if (Zones.Length > 0) ZoneStates[0] = (int)QuestState.Active;
                AwardXpToAll(50);
                RpcNotice($"New quest: {Zones[0].QuestName} (journal: J).");
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
                if (zone + 1 < Zones.Length)
                {
                    ZoneStates[zone + 1] = (int)QuestState.Active;
                    RpcNotice($"Quest complete! +{cfg.XpEach} XP each, +{cfg.Gold} gold. " +
                              $"New quest: {Zones[zone + 1].QuestName}.");
                }
                else
                {
                    CampaignComplete.Value = true;
                    RpcNotice($"Quest complete! +{cfg.XpEach} XP each, +{cfg.Gold} gold. " +
                              "The Hollow Flame recedes — Aldenmere is free!");
                }
                ServerSaveCampaign();
            }
        }

        [Server]
        public void ServerAddLoot(int gold, System.Collections.Generic.List<string> items)
        {
            PartyGold.Value += gold;
            foreach (var id in items) Stash.Add(id);
            if (gold > 0 || items.Count > 0)
                RpcNotice($"Loot: {gold} gold" +
                    (items.Count > 0 ? $", {string.Join(", ", items)}" : "") + ".");
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

        [ServerRpc(RequireOwnership = false)]
        public void CmdUsePotion(NetworkConnection conn = null)
        {
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
            { RpcNotice("Not during combat (v1)."); return; }
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

        [Server]
        private void AwardXpToAll(int amount)
        {
            foreach (var p in FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None))
            {
                if (p.Sheet == null) continue;
                p.Sheet.GainXp(amount);
                while (p.Sheet.CanLevelUp)
                {
                    var result = p.Sheet.LevelUp();
                    RpcNotice($"{p.Sheet.Name} reaches level {result.NewLevel}! (+{result.HpGained} HP)");
                }
            }
        }

        // ---------------- client ----------------

        [ObserversRpc]
        private void RpcNotice(string text)
        {
            LocalNotice = text;
            _noticeUntil = Time.time + 6f;
        }

        private bool _journalOpen;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.J)) _journalOpen = !_journalOpen;
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
            {
                var style = new GUIStyle(GUI.skin.box)
                    { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
                GUI.Box(new Rect(Ui.W / 2f - 260, 16, 520, 34), LocalNotice, style);
            }

            if (!_journalOpen) return;
            GUILayout.BeginArea(new Rect(Ui.W / 2f - 210, 70, 420, 300), GUI.skin.box);
            GUILayout.Label("<b>Journal</b> (J to close)",
                new GUIStyle(GUI.skin.label) { richText = true });

            DrawQuest("Report to the Council", (QuestState)MusterState.Value,
                "Speak with Councilor Veresk at the Council Hall.");
            for (int i = 0; i < Zones.Length; i++)
            {
                var cfg = Zones[i];
                DrawQuest(cfg.QuestName, GetZoneState(i),
                    $"Clear {cfg.DisplayName} " +
                    $"({Mathf.Min(i < ZoneClearedCounts.Count ? ZoneClearedCounts[i] : 0, cfg.RequiredEncounters)}" +
                    $"/{cfg.RequiredEncounters}), then return to Veresk.");
            }
            if (CampaignComplete.Value)
                GUILayout.Label("★ Aldenmere stands free. The campaign is complete!");

            GUILayout.Space(8);
            int potions = Stash.Count(s => s == "potion_healing");
            int salvage = Stash.Count - potions;
            GUILayout.Label($"Party gold: {PartyGold.Value}   Potions: {potions}   Salvage: {salvage}");
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

        private void DrawQuest(string title, QuestState state, string detail)
        {
            if (state == QuestState.Locked) return;
            string tag = state switch
            {
                QuestState.Completed => "✔",
                QuestState.ObjectivesMet => "!",
                _ => "•"
            };
            GUILayout.Label($"{tag} {title}");
            if (state == QuestState.Active || state == QuestState.ObjectivesMet)
                GUILayout.Label($"      {detail}");
        }
    }
}
