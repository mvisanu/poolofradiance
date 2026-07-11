using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RadiantPool.Game
{
    public enum QuestState { Locked = 0, Active = 1, ObjectivesMet = 2, Completed = 3 }

    /// <summary>Server-owned campaign state for the vertical slice: the muster quest and
    /// the first zone-clearing quest (content ids from /content/quests). Zone-clear
    /// progress, party gold, XP awards, and the shared journal all live here. A
    /// data-driven quest engine over ContentDb replaces the hardcoded chain in 3d-full.</summary>
    public class GameDirector : NetworkBehaviour
    {
        public static GameDirector Instance { get; private set; }

        [Header("Zone quest (from content/zones + quests JSON)")]
        public string ZoneId = "old_docks";
        public string ZoneDisplayName = "The Old Docks";
        public int RequiredEncounters = 3;
        public int QuestXpEach = 300;
        public int QuestGold = 100;

        public readonly SyncVar<int> MusterState = new SyncVar<int>((int)QuestState.Active);
        public readonly SyncVar<int> ClearQuestState = new SyncVar<int>((int)QuestState.Locked);
        public readonly SyncVar<int> EncountersCleared = new SyncVar<int>(0);
        public readonly SyncVar<int> PartyGold = new SyncVar<int>(0);
        public readonly SyncVar<bool> ZonePacified = new SyncVar<bool>(false);

        /// <summary>Party-shared loot stash (item ids from content/items). Per-character
        /// inventories arrive with the 3e equipment pass.</summary>
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

        // ---------------- server ----------------

        // Roster survives disconnects: a rejoining display name reclaims its character.
        private readonly System.Collections.Generic.Dictionary<string, RadiantPool.Rules.CharacterSheet>
            _roster = new System.Collections.Generic.Dictionary<string, RadiantPool.Rules.CharacterSheet>();
        private readonly System.Collections.Generic.Dictionary<string, CharacterBuild>
            _builds = new System.Collections.Generic.Dictionary<string, CharacterBuild>();
        private readonly System.Collections.Generic.List<string>
            _consumedEncounters = new System.Collections.Generic.List<string>();

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (!SaveSystem.Exists) return;
            var save = SaveSystem.Read();
            if (save == null) return;

            MusterState.Value = save.MusterState;
            ClearQuestState.Value = save.ClearQuestState;
            EncountersCleared.Value = save.EncountersCleared;
            PartyGold.Value = save.PartyGold;
            ZonePacified.Value = save.ZonePacified;
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
                _consumedEncounters.Add(id);
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
                ClearQuestState = ClearQuestState.Value,
                EncountersCleared = EncountersCleared.Value,
                PartyGold = PartyGold.Value,
                ZonePacified = ZonePacified.Value,
                Stash = Stash.ToList(),
                ConsumedEncounters = _consumedEncounters.ToList(),
                Roster = _roster.Select(kv =>
                    SaveSystem.Capture(kv.Value, _builds[kv.Key])).ToList()
            };
            SaveSystem.Write(save);
        }

        [Server]
        public void ServerEncounterCleared(EncounterTrigger trigger)
        {
            if (trigger == null || trigger.ZoneId != ZoneId) return;
            trigger.Consume();
            _consumedEncounters.Add(trigger.EncounterId);
            ServerSaveCampaign();   // autosave at every cleared block
            if (!trigger.RequiredForClear) return;

            EncountersCleared.Value++;
            if (ClearQuestState.Value == (int)QuestState.Active
                && EncountersCleared.Value >= RequiredEncounters)
            {
                ClearQuestState.Value = (int)QuestState.ObjectivesMet;
                RpcNotice($"{ZoneDisplayName} has been cleared! Return to Councilor Veresk.");
            }
            else
            {
                RpcNotice($"Encounter cleared ({EncountersCleared.Value}/{RequiredEncounters} " +
                          $"in {ZoneDisplayName}).");
            }
        }

        /// <summary>Dialogue actions arrive from whichever client is talking to the NPC.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdDialogueChoice(string action, NetworkConnection conn = null)
        {
            switch (action)
            {
                case "muster_accept" when MusterState.Value == (int)QuestState.Active:
                    MusterState.Value = (int)QuestState.Completed;
                    ClearQuestState.Value = (int)QuestState.Active;
                    AwardXpToAll(50);
                    RpcNotice("New quest: Retake the Old Docks (journal: J).");
                    break;

                case "docks_turnin" when ClearQuestState.Value == (int)QuestState.ObjectivesMet:
                    ClearQuestState.Value = (int)QuestState.Completed;
                    ZonePacified.Value = true;
                    PartyGold.Value += QuestGold;
                    AwardXpToAll(QuestXpEach);
                    ServerSaveCampaign();
                    RpcNotice($"Quest complete! +{QuestXpEach} XP each, +{QuestGold} gold. " +
                              "The Old Docks are safe forever.");
                    break;
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
            if (Time.time < _noticeUntil && LocalNotice.Length > 0)
            {
                var style = new GUIStyle(GUI.skin.box)
                    { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
                GUI.Box(new Rect(Screen.width / 2f - 260, 16, 520, 34), LocalNotice, style);
            }

            if (!_journalOpen) return;
            GUILayout.BeginArea(new Rect(Screen.width / 2f - 210, 70, 420, 240), GUI.skin.box);
            GUILayout.Label("<b>Journal</b> (J to close)",
                new GUIStyle(GUI.skin.label) { richText = true });

            DrawQuest("Report to the Council", (QuestState)MusterState.Value,
                "Speak with Councilor Veresk at the Council Hall.");
            DrawQuest("Retake the Old Docks", (QuestState)ClearQuestState.Value,
                $"Defeat the squatters ({Mathf.Min(EncountersCleared.Value, RequiredEncounters)}" +
                $"/{RequiredEncounters}), then return to Veresk.");

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
            if (ZonePacified.Value)
                GUILayout.Label("The Old Docks are pacified — lanterns lit, no more ambushes.");
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
