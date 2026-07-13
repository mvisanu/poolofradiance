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

            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-selltest") >= 0)
                StartCoroutine(ServerSellSelfTest());
            if (System.Array.IndexOf(args, "-leveltest") >= 0)
                StartCoroutine(ServerLevelSelfTest());
            if (System.Array.IndexOf(args, "-warpsmith") >= 0)
                StartCoroutine(ServerWarpToSmith());

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
            // Self-heal, in this order:
            //   1. Recount every zone from the consumed list. Saves written before the
            //      count/save ordering fix persisted counts that lagged the consumed
            //      encounters, which left zones demanding fights that were already gone.
            //   2. Recheck, which also covers zones cleared before the quest went Active.
            for (int i = 0; i < Zones.Length; i++) ServerRecountZone(i);
            for (int i = 0; i < Zones.Length; i++) ServerRecheckZone(i);
            ServerSaveCampaign();   // persist the repair so it happens once
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

        /// <summary>Fills the party to 4 with AI companions. WHICH classes is
        /// RadiantPool.Rules.PartyComposition's call (rules lib, unit-tested): a healer
        /// first, then damage dealers of two different classes, counting whoever is already
        /// playing. Names are drawn without repeats from a medieval pool.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdRecruitCompanions(NetworkConnection conn = null)
        {
            if (CompanionPrefab == null) { RpcNotice("No sellswords available."); return; }
            var holders = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.Sheet != null).ToList();
            int needed = RadiantPool.Rules.PartyComposition.MaxPartySize - holders.Count;
            if (needed <= 0) { RpcNotice("The party is already full."); return; }

            var classes = RadiantPool.Rules.PartyComposition.Recruits(
                holders.Select(h => h.Sheet.Class), needed);

            var rng = new System.Random();
            var usedNames = holders.Select(h => h.Sheet.Name).ToHashSet();
            var namePool = CompanionNames.OrderBy(_ => rng.Next())
                .Where(n => !usedNames.Contains(n)).ToList();

            var hired = new System.Collections.Generic.List<string>();
            int spawned = 0;
            foreach (var cls in classes)
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
                holder.ServerInitCompanion(name, (int)cls);
                var identity = nob.GetComponent<PlayerIdentity>();
                if (identity != null) identity.ServerSetName(name);
                holders.Add(holder);
                hired.Add($"{name} the {cls}");
                spawned++;
            }
            RpcNotice(spawned == 1
                ? $"{hired[0]} joined the party!"
                : $"Sellswords joined the party: {string.Join(", ", hired)}.");
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
            RpcNotice($"Sold {label} to {trader} for {gold} gold.");
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
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
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
            CmdSellItem(itemId);   // host: runs the same ServerRpc a client would send
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
                .FirstOrDefault(p => !p.IsCompanion && p.Owner != null && p.Owner.IsValid);
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
            CmdSpendAbilityPoint((int)ability);   // host: the same ServerRpc a client sends
            yield return new WaitForSeconds(0.3f);

            bool spent = sheet.Abilities[ability] == before + 1
                         && sheet.PendingAbilityPoints == pointsBefore - 1;
            Debug.Log($"[LevelTest] {(spent ? "PASS" : "FAIL")} - spending a point took Con " +
                      $"{before} to {sheet.Abilities[ability]}, " +
                      $"{sheet.PendingAbilityPoints} left of {pointsBefore}");
        }

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
                GUILayout.Label("<color=#8a6d1f><b>Aldenmere stands free.</b> The campaign is " +
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
