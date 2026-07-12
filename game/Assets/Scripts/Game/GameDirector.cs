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
            /// <summary>Journal text: what to do and WHERE to go (compass direction).</summary>
            public string Description = "";
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

        /// <summary>Smith stock: SRD list price in gold (sell-back via the Exchange is
        /// half). Order here is the order shown in the smith UI.</summary>
        public static readonly System.Collections.Generic.List<(string id, int price)> SmithStock =
            new System.Collections.Generic.List<(string, int)>
        {
            ("dagger", 2), ("mace", 5), ("shortsword", 10), ("longsword", 15),
            ("shortbow", 25), ("light_crossbow", 25),
            ("leather_armor", 10), ("scale_mail", 50), ("chain_mail", 75), ("shield", 10)
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
            // Self-heal saves from before the pre-accept-clear fix: a zone whose
            // encounters were all cleared while the quest wasn't Active yet.
            for (int i = 0; i < Zones.Length; i++) ServerRecheckZone(i);
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
            ServerRecheckZone(zone);
            if (GetZoneState(zone) != QuestState.ObjectivesMet)
            {
                RpcNotice($"Encounter cleared ({Mathf.Min(ZoneClearedCounts[zone], cfg.RequiredEncounters)}" +
                          $"/{cfg.RequiredEncounters} in {cfg.DisplayName}).");
            }
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
                && ZoneClearedCounts[zone] >= cfg.RequiredEncounters)
            {
                ZoneStates[zone] = (int)QuestState.ObjectivesMet;
                RpcNotice($"{cfg.DisplayName} has been cleared! Return to Councilor Veresk.");
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

        /// <summary>Fills the party to 4 with AI companions covering every class no one
        /// is playing (one each — fighter, cleric, wizard, rogue order), then repeats
        /// classes only if the party still has room. Names are drawn without repeats
        /// from a medieval pool.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdRecruitCompanions(NetworkConnection conn = null)
        {
            if (CompanionPrefab == null) { RpcNotice("No sellswords available."); return; }
            var holders = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.Sheet != null).ToList();
            int needed = 4 - holders.Count;
            if (needed <= 0) { RpcNotice("The party is already full."); return; }

            var have = holders.Select(h => (int)h.Sheet.Class).ToHashSet();
            var priority = new[] { 0, 1, 2, 3 };   // fighter, cleric, wizard, rogue
            var classes = priority.Where(c => !have.Contains(c))
                .Concat(priority).Take(needed);

            var rng = new System.Random();
            var usedNames = holders.Select(h => h.Sheet.Name).ToHashSet();
            var namePool = CompanionNames.OrderBy(_ => rng.Next())
                .Where(n => !usedNames.Contains(n)).ToList();

            int spawned = 0;
            foreach (int cls in classes)
            {
                var anchor = holders.FirstOrDefault(h => !h.IsCompanion);
                Vector3 pos = anchor != null
                    ? anchor.transform.position + new Vector3(1.5f + spawned, 0.2f, -1.5f)
                    : new Vector3(0, 0.2f, -8);
                var nob = Instantiate(CompanionPrefab, pos, Quaternion.identity);
                FishNet.InstanceFinder.ServerManager.Spawn(nob);   // no owner = server AI
                var holder = nob.GetComponent<PlayerCharacterHolder>();
                string name = spawned < namePool.Count
                    ? namePool[spawned]
                    : $"Sellsword {spawned + 1}";   // pool exhausted (never in practice)
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
                ServerRecheckZone(0);   // encounters may have been cleared pre-accept
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
                    ServerRecheckZone(zone + 1);   // may already be cleared from wandering
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

        /// <summary>Buy a weapon/armor upgrade from the smith into the party stash;
        /// equip it afterwards from the inventory (I).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdBuyItem(string itemId, NetworkConnection conn = null)
        {
            int price = SmithStock.FirstOrDefault(s => s.id == itemId).price;
            if (price <= 0) { RpcNotice("The smith doesn't stock that."); return; }
            if (PartyGold.Value < price)
            { RpcNotice($"Not enough gold ({price}g needed)."); return; }
            PartyGold.Value -= price;
            Stash.Add(itemId);
            var item = GameItem.Get(itemId);
            RpcNotice($"Bought {item?.Name ?? itemId} for {price}g — equip it from the inventory (I).");
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

        /// <summary>Equip an item from the party stash onto the calling player's
        /// character. Class restrictions enforced server-side; the replaced item
        /// returns to the stash.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdEquipItem(string itemId, NetworkConnection conn = null)
        {
            var item = GameItem.Get(itemId);
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.Owner == conn);
            if (item == null || holder?.Sheet == null) return;
            if (CombatManager.Instance != null && CombatManager.Instance.InCombat.Value)
            { RpcNotice("Not during combat."); return; }
            int idx = Stash.IndexOf(itemId);
            if (idx < 0) { RpcNotice("That item is not in the stash."); return; }
            if (!item.UsableBy(holder.Sheet.Class))
            { RpcNotice($"A {holder.Sheet.Class} cannot use {item.Name}."); return; }

            Stash.RemoveAt(idx);
            string previous = holder.ServerEquip(item);
            if (previous != null) Stash.Add(previous);
            RpcNotice($"{holder.Sheet.Name} equips {item.Name}" +
                      (item.Slot == ItemSlot.Armor ? $" (AC {holder.Sheet.ArmorClass})" : "") + ".");
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
        private Vector2 _journalScroll;

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
                Theme.DrawToast(Ui.W / 2f, 16, LocalNotice);

            if (!_journalOpen) return;
            float jh = Mathf.Min(Ui.H - 110f, 480f);
            GUILayout.BeginArea(new Rect(Ui.W / 2f - 215, 94, 430, jh), Theme.PanelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Journal", Theme.Header);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#d0c5af>J to close</color>", Theme.Body);
            GUILayout.EndHorizontal();

            _journalScroll = GUILayout.BeginScrollView(_journalScroll,
                GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(Theme.ParchmentStyle);
            DrawQuest("Report to the Council", (QuestState)MusterState.Value,
                "Speak with Councilor Veresk on the council platform in the hub plaza " +
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
                DrawQuest(cfg.QuestName, GetZoneState(i), detail,
                    (float)done / cfg.RequiredEncounters);
            }
            if (CampaignComplete.Value)
                GUILayout.Label("<color=#b8860b>★ Aldenmere stands free. The campaign is " +
                    "complete!</color>", Theme.BodyInk);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            int potions = Stash.Count(s => s == "potion_healing");
            int salvage = Stash.Count - potions;
            GUILayout.Label($"<color=#f2ca50><b>{PartyGold.Value}</b> gold</color>" +
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
        private void DrawQuest(string title, QuestState state, string detail, float progress)
        {
            if (state == QuestState.Locked) return;
            if (state == QuestState.Completed)
            {
                GUILayout.Label($"<color=#2e7d32>✔</color>  <color=#8a8a8a>{title}</color>",
                    Theme.BodyInk);
                return;
            }

            GUILayout.BeginVertical(Theme.GoldWashStyle);
            string mark = state == QuestState.ObjectivesMet
                ? "<color=#b8860b><b>!</b></color>" : "<color=#b8860b>★</color>";
            GUILayout.Label($"{mark}  <b>{title}</b>", Theme.HeaderInk);
            GUILayout.Label(state == QuestState.ObjectivesMet
                ? "<i>Return to Councilor Veresk for your reward.</i>" : detail, Theme.BodyInk);
            if (progress >= 0f && state == QuestState.Active)
            {
                var r = GUILayoutUtility.GetRect(240, 9);
                Theme.Bar(new Rect(r.x + 2, r.y + 1, 220, 7), progress, Theme.MpBlue);
            }
            GUILayout.EndVertical();
            GUILayout.Space(3);
        }
    }
}
