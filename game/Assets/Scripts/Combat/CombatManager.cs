using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Server-authoritative combat: the whole party is pulled in when any member
    /// trips an encounter (ARCHITECTURE §4). Clients send intents; the server validates
    /// against the rules library and broadcasts results. Clients only render.</summary>
    public class CombatManager : NetworkBehaviour
    {
        public static CombatManager Instance { get; private set; }

        public const float CellSize = 1.5f;           // 5 ft
        private const float TurnSeconds = 60f;

        public readonly SyncVar<bool> InCombat = new SyncVar<bool>(false);

        // ---------- shared client-side view ----------
        public class UnitView
        {
            public string Id;
            public string Name;
            public bool IsPc;
            public int MaxHp, Hp;
            public Vector2Int Cell;
            public int OwnerId = -1;         // players only
            public Transform Visual;          // player object or local monster capsule
            public bool Down, Dead;
        }

        public readonly List<UnitView> ClientUnits = new List<UnitView>();
        public readonly List<string> Log = new List<string>();
        public string ActiveUnitId { get; private set; } = "";
        public int MoveLeft { get; private set; }
        public bool ActionLeft { get; private set; }
        public bool BonusLeft { get; private set; }
        public int Round { get; private set; }
        public int[] MySlots { get; private set; } = { 0, 0, 0 };
        public string LastRejection { get; private set; } = "";
        public Vector3 GridOrigin { get; private set; }

        public UnitView MyUnit => ClientUnits.FirstOrDefault(
            u => u.IsPc && u.OwnerId == LocalConnection.ClientId);
        public bool IsMyTurn => MyUnit != null && MyUnit.Id == ActiveUnitId;

        // ---------- server state ----------
        private class ServerUnit
        {
            public string Id;
            public Creature Creature;
            public PlayerCharacterHolder Player;   // null for monsters
            public MonsterDefinition MonsterDef;    // null for players
            public Vector2Int Cell;
        }

        private readonly Dictionary<string, ServerUnit> _server = new Dictionary<string, ServerUnit>();
        private TurnEngine _engine;
        private IRng _rng;
        private EncounterTrigger _encounter;
        private Vector3 _origin;
        private bool _turnDone;
        private GameObject _overlay;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private Vector3 CellToWorld(Vector2Int c) =>
            _originForClients() + new Vector3(c.x * CellSize, 0f, c.y * CellSize);
        private Vector3 _originForClients() => IsServerStarted ? _origin : GridOrigin;

        // ==================== SERVER ====================

        [Server]
        public void StartEncounter(EncounterTrigger encounter)
        {
            if (InCombat.Value) return;
            _rng ??= new SeededRng(Environment.TickCount);
            _encounter = encounter;
            _server.Clear();
            _origin = new Vector3(
                Mathf.Round(encounter.transform.position.x / CellSize) * CellSize, 0f,
                Mathf.Round(encounter.transform.position.z / CellSize) * CellSize);

            var players = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.Sheet != null && !p.Sheet.IsDead).ToList();
            if (players.Count == 0) return;

            int i = 0;
            foreach (var p in players)
            {
                p.Sheet.Conditions.Remove(ConditionType.Dodging);
                _server[p.Sheet.Id] = new ServerUnit
                {
                    Id = p.Sheet.Id, Creature = p.Sheet, Player = p,
                    Cell = new Vector2Int(-2 + (i % 4) * 1, -3 - (i / 4))
                };
                i++;
            }

            int m = 0;
            foreach (var monsterId in encounter.MonsterIds)
            {
                var def = MonsterLibrary.Get(monsterId);
                var creature = def.Spawn($"m{m}_{monsterId}", _rng);
                _server[creature.Id] = new ServerUnit
                {
                    Id = creature.Id, Creature = creature, MonsterDef = def,
                    Cell = new Vector2Int(-2 + (m % 4), 2 + (m / 4))
                };
                m++;
            }

            _engine = new TurnEngine(_server.Values.Select(u => u.Creature), _rng);
            InCombat.Value = true;

            // Companions are server-moved: put their bodies on their assigned cells.
            foreach (var u in _server.Values) ServerRepositionCompanion(u);

            var ordered = _engine.InitiativeOrder.Select(c => _server[c.Id]).ToList();
            RpcCombatStarted(_origin,
                ordered.Select(u => u.Id).ToArray(),
                ordered.Select(u => u.Creature.Name).ToArray(),
                ordered.Select(u => u.Player != null).ToArray(),
                ordered.Select(u => u.Creature.MaxHp).ToArray(),
                ordered.Select(u => u.Creature.CurrentHp).ToArray(),
                ordered.Select(u => u.Cell.x).ToArray(),
                ordered.Select(u => u.Cell.y).ToArray(),
                ordered.Select(u => u.Player != null ? u.Player.OwnerId : -1).ToArray());
            ServerLog($"Ambush at {encounter.DisplayName}! Roll initiative.");
            RpcSfx("combat_start");
            StartCoroutine(TurnLoop());
        }

        [Server]
        private IEnumerator TurnLoop()
        {
            yield return new WaitForSeconds(1.2f);
            while (InCombat.Value)
            {
                if (CheckCombatEnd()) yield break;

                var active = _engine.ActiveCreature;
                var unit = _server[active.Id];
                var budget = _engine.ActiveBudget;
                RpcTurnStarted(active.Id, _engine.Round, budget.MovementRemaining,
                    budget.ActionAvailable, budget.BonusActionAvailable);

                if (active.IsPlayerCharacter && active.IsDown && !active.IsStable && !active.IsDead)
                {
                    yield return new WaitForSeconds(1f);
                    var save = CombatMath.RollDeathSave(active, _rng);
                    ServerLog($"{active.Name} death save: {save.Roll.Value} — " +
                              (active.IsDead ? "has died." :
                               !active.IsDown ? "back on their feet!" :
                               active.IsStable ? "stable." :
                               save.Success ? $"{active.DeathSaveSuccesses} success(es)."
                                            : $"{active.DeathSaveFailures} failure(s)."));
                    RpcStatusPopup(active.Id,
                        active.IsDead ? "DEAD" :
                        !active.IsDown ? "UP!" :
                        active.IsStable ? "stable" :
                        save.Success ? $"save {active.DeathSaveSuccesses}/3"
                                     : $"fail {active.DeathSaveFailures}/3",
                        save.Success || !active.IsDown
                            ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.4f, 0.35f), 1.1f);
                    SyncHp(active.Id);
                    yield return new WaitForSeconds(0.6f);
                    EndActiveTurn();
                    continue;
                }

                if (!TurnEngine.CanAct(active))
                {
                    yield return new WaitForSeconds(0.4f);
                    EndActiveTurn();
                    continue;
                }

                if (unit.Player == null)
                {
                    yield return new WaitForSeconds(0.45f);
                    yield return MonsterTurn(unit);
                    if (CheckCombatEnd()) yield break;
                    EndActiveTurn();
                    continue;
                }

                if (unit.Player.IsCompanion)
                {
                    yield return new WaitForSeconds(0.45f);
                    yield return CompanionTurn(unit);
                    if (CheckCombatEnd()) yield break;
                    EndActiveTurn();
                    continue;
                }

                // Player turn: wait for intents until EndTurn or timeout.
                _turnDone = false;
                float deadline = Time.time + TurnSeconds;
                while (!_turnDone && Time.time < deadline && InCombat.Value)
                {
                    if (CheckCombatEnd()) yield break;
                    yield return null;
                }
                if (!_turnDone && InCombat.Value)
                {
                    try { _engine.Dodge(); ServerLog($"{active.Name} hesitates and dodges."); }
                    catch (RuleViolationException) { }
                }
                if (CheckCombatEnd()) yield break;
                if (InCombat.Value) EndActiveTurn();
            }
        }

        [Server]
        private void EndActiveTurn()
        {
            var next = _engine.EndTurn();
            // Round ticks may have woken/expired conditions; resync everyone cheaply.
            foreach (var u in _server.Values) SyncHp(u.Id);
        }

        /// <summary>Monster AI turn, paced so clients can read it: walk (clients glide
        /// the visual at CombatFx.GlideSpeed), a beat, then the attack. Rules calls are
        /// try/caught so a bad monster definition can never stall the TurnLoop.</summary>
        [Server]
        private IEnumerator MonsterTurn(ServerUnit monster)
        {
            var target = _server.Values
                .Where(u => u.Player != null && !u.Creature.IsDead && !u.Creature.IsDown)
                .OrderBy(u => Chebyshev(u.Cell, monster.Cell)).FirstOrDefault();
            if (target == null) yield break;

            Vector2Int from = monster.Cell;
            int steps = monster.Creature.Speed / 5;
            while (Chebyshev(target.Cell, monster.Cell) > 1 && steps-- > 0)
            {
                var step = new Vector2Int(
                    monster.Cell.x + Math.Sign(target.Cell.x - monster.Cell.x),
                    monster.Cell.y + Math.Sign(target.Cell.y - monster.Cell.y));
                if (Occupied(step)) break;
                monster.Cell = step;
            }
            if (monster.Cell != from)
            {
                RpcUnitMoved(monster.Id, monster.Cell.x, monster.Cell.y);
                yield return new WaitForSeconds(GlideSeconds(from, monster.Cell) + 0.15f);
            }

            if (Chebyshev(target.Cell, monster.Cell) <= 1)
            {
                try
                {
                    var attack = monster.MonsterDef.Attacks[0];
                    var result = CombatMath.ResolveAttack(
                        monster.Creature, target.Creature, attack, _rng);
                    Narrate(monster.Creature, target.Creature, attack.Name, result);
                    SyncHp(target.Id);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Combat] Monster attack failed: {e}");
                }
                yield return new WaitForSeconds(0.75f);
            }
        }

        /// <summary>How long clients take to glide a unit between these cells — the
        /// server waits this out so the attack lands after the walk, not during it.</summary>
        private float GlideSeconds(Vector2Int from, Vector2Int to) =>
            Vector3.Distance(CellToWorld(from), CellToWorld(to)) / CombatFx.GlideSpeed;

        /// <summary>Server-side AI turn for a hired companion, paced like MonsterTurn:
        /// clerics walk to and heal downed or badly hurt allies, wizards sling fire
        /// bolts, everyone else closes and swings. Decisions are plain queries; the
        /// rules-engine calls live in try/caught helpers so the TurnLoop never stalls.</summary>
        [Server]
        private IEnumerator CompanionTurn(ServerUnit self)
        {
            var sheet = self.Creature as CharacterSheet;
            if (sheet == null) yield break;

            // Cleric first: heal whoever needs it most.
            if (sheet.Class == CharacterClass.Cleric && sheet.HasSlot(1))
            {
                var hurt = _server.Values
                    .Where(u => u.Player != null && !u.Creature.IsDead
                                && (u.Creature.IsDown
                                    || u.Creature.CurrentHp * 2 < u.Creature.MaxHp))
                    .OrderByDescending(u => u.Creature.IsDown)
                    .ThenBy(u => Chebyshev(u.Cell, self.Cell))
                    .FirstOrDefault();
                if (hurt != null)
                {
                    yield return MoveCompanion(self, hurt.Cell, stopAdjacent: true);
                    if (Chebyshev(self.Cell, hurt.Cell) <= 1)
                    {
                        TryCompanionCast(self, "cure_wounds", hurt, 1);
                        yield return new WaitForSeconds(0.75f);
                        yield break;
                    }
                    // Couldn't reach: fall through to attack from range.
                }
            }

            var target = _server.Values
                .Where(u => u.Player == null && !u.Creature.IsDead
                            && !u.Creature.Conditions.Has(ConditionType.Asleep))
                .OrderBy(u => Chebyshev(u.Cell, self.Cell)).FirstOrDefault();
            if (target == null) yield break;

            if (sheet.Class == CharacterClass.Wizard || sheet.Class == CharacterClass.Cleric)
            {
                TryCompanionCast(self, sheet.Class == CharacterClass.Wizard
                    ? "fire_bolt" : "sacred_flame", target, 0);
                yield return new WaitForSeconds(0.75f);
                yield break;
            }

            yield return MoveCompanion(self, target.Cell, stopAdjacent: true);
            if (Chebyshev(self.Cell, target.Cell) <= 1)
            {
                TryCompanionAttack(self, target);
                yield return new WaitForSeconds(0.75f);
            }
        }

        [Server]
        private void TryCompanionCast(ServerUnit self, string spellId, ServerUnit target, int slot)
        {
            try
            {
                var sheet = (CharacterSheet)self.Creature;
                var spell = SpellLibrary.Get(spellId);
                var events = SpellEngine.Cast(sheet, spell, new[] { target.Creature }, slot, _rng);
                NarrateSpell(sheet, spell, events);
                foreach (var u in _server.Values) SyncHp(u.Id);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Combat] Companion cast failed: {e}");
            }
        }

        [Server]
        private void TryCompanionAttack(ServerUnit self, ServerUnit target)
        {
            try
            {
                var attack = self.Player.BasicAttack();
                var result = CombatMath.ResolveAttack(self.Creature, target.Creature,
                    attack, _rng);
                Narrate(self.Creature, target.Creature, attack.Name, result);
                SyncHp(target.Id);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Combat] Companion attack failed: {e}");
            }
        }

        /// <summary>Grid movement for a companion: pick the cells, replicate the new
        /// cell, then walk the body there over time. The server owns the transform, the
        /// NetworkTransform shows the walk on clients, and MotionAnimator plays the
        /// cycle from the actual displacement — no extra networking.</summary>
        [Server]
        private IEnumerator MoveCompanion(ServerUnit unit, Vector2Int dest, bool stopAdjacent)
        {
            Vector2Int from = unit.Cell;
            int steps = unit.Creature.Speed / 5;
            while (steps-- > 0
                   && Chebyshev(dest, unit.Cell) > (stopAdjacent ? 1 : 0))
            {
                var step = new Vector2Int(
                    unit.Cell.x + Math.Sign(dest.x - unit.Cell.x),
                    unit.Cell.y + Math.Sign(dest.y - unit.Cell.y));
                if (Occupied(step)) break;
                unit.Cell = step;
            }
            if (unit.Cell == from) yield break;
            RpcUnitMoved(unit.Id, unit.Cell.x, unit.Cell.y);

            if (unit.Player == null || !unit.Player.IsCompanion) yield break;
            var body = unit.Player.transform;
            var cc = unit.Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Vector3 target = CellToWorld(unit.Cell);
            CombatFx.Face(body, target);
            while (body != null && (body.position - target).sqrMagnitude > 0.0004f)
            {
                body.position = Vector3.MoveTowards(
                    body.position, target, CombatFx.GlideSpeed * Time.deltaTime);
                yield return null;
            }
            if (body == null) yield break;
            body.position = target;
            if (cc != null) cc.enabled = true;
        }

        [Server]
        private void ServerRepositionCompanion(ServerUnit unit)
        {
            if (unit.Player == null || !unit.Player.IsCompanion) return;
            var t = unit.Player.transform;
            var cc = unit.Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            t.position = CellToWorld(unit.Cell);
            if (cc != null) cc.enabled = true;
        }

        [Server]
        private bool CheckCombatEnd()
        {
            if (_engine == null || !_engine.CombatOver(out bool playersWon)) return false;

            InCombat.Value = false;
            if (playersWon)
            {
                // Full encounter XP to every member (co-op convention; the level-1..5
                // curve is tuned for this — see CampaignSimulationTests).
                int xpEach = _server.Values.Where(u => u.MonsterDef != null)
                    .Sum(u => u.MonsterDef.Xp);
                var pcs = _server.Values.Where(u => u.Player != null).ToList();
                foreach (var pc in pcs)
                {
                    pc.Player.Sheet.GainXp(xpEach);
                    pc.Player.Sheet.Conditions.Remove(ConditionType.Blessed);
                    // Fallen heroes are dragged back to their feet by the party — no
                    // one is ever permanently out of the campaign.
                    if (pc.Player.Sheet.IsDead)
                    {
                        pc.Player.Sheet.ReviveFull();
                        pc.Player.Sheet.TakeDamage(
                            pc.Player.Sheet.MaxHp - 1, DamageType.Bludgeoning);
                        ServerLog($"{pc.Player.Sheet.Name} is dragged back to their feet " +
                                  "(1 HP) — rest or drink a potion!");
                    }
                }
                ServerLog($"Victory! Each hero gains {xpEach} XP.");

                // Roll loot per monster (+ any bonus cache) server-side.
                int gold = 0;
                var items = new System.Collections.Generic.List<string>();
                foreach (var mu in _server.Values.Where(u => u.MonsterDef != null))
                {
                    var roll = LootLibrary.Get(mu.MonsterDef.LootTable).Roll(_rng);
                    gold += roll.Gold;
                    items.AddRange(roll.ItemIds);
                }
                if (_encounter != null && !string.IsNullOrEmpty(_encounter.BonusLootTable))
                {
                    var bonus = LootLibrary.Get(_encounter.BonusLootTable).Roll(_rng);
                    gold += bonus.Gold;
                    items.AddRange(bonus.ItemIds);
                }
                GameDirector.Instance?.ServerAddLoot(gold, items);
                GameDirector.Instance?.ServerEncounterCleared(_encounter);
                RpcSfx("victory");
                string lootSummary = gold > 0 || items.Count > 0
                    ? $"{gold} gold" + (items.Count > 0
                        ? ", " + string.Join(", ", items.Select(PrettyItem)) : "")
                    : "";
                RpcCombatEnded(true, xpEach, lootSummary);
            }
            else
            {
                int slot = 0;
                foreach (var pc in _server.Values.Where(u => u.Player != null))
                {
                    pc.Player.Sheet.ReviveFull();
                    // Everyone wakes at the Dawnmother shrine, well away from danger.
                    Vector3 spot = RespawnPoint
                        + new Vector3((slot % 2) * 1.6f, 0f, (slot / 2) * 1.6f);
                    slot++;
                    if (pc.Player.IsCompanion)
                    {
                        var cc = pc.Player.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        pc.Player.transform.position = spot;
                        if (cc != null) cc.enabled = true;
                    }
                    else if (pc.Player.Owner != null && pc.Player.Owner.IsValid)
                    {
                        TargetRespawn(pc.Player.Owner, spot);
                    }
                }
                ServerLog("The party falls… and wakes at the Dawnmother's shrine, bruised but alive.");
                RpcSfx("defeat");
                RpcCombatEnded(false, 0, "");
            }
            _engine = null;
            _encounter = null;
            return true;
        }

        private static int Chebyshev(Vector2Int a, Vector2Int b) =>
            Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));

        private bool Occupied(Vector2Int cell) =>
            _server.Values.Any(u => u.Cell == cell && !u.Creature.IsDead);

        [Server]
        private void Narrate(Creature attacker, Creature target, string attackName, AttackResult r)
        {
            string line = !r.Hit
                ? $"{attacker.Name}'s {attackName} misses {target.Name} ({r.Total} vs AC {target.ArmorClass})."
                : $"{attacker.Name}'s {attackName}{(r.Critical ? " CRITS" : " hits")} {target.Name} " +
                  $"for {r.DamageDealt} ({r.Total} vs AC {target.ArmorClass})." +
                  (r.TargetDied ? $" {target.Name} is slain!" :
                   r.TargetDowned ? $" {target.Name} goes down!" : "");
            ServerLog(line);
            RpcAttackFx(attacker.Id, target.Id, r.Hit, r.Critical, r.DamageDealt);
            if (r.TargetDied || r.TargetDowned) RpcSfx("down");
        }

        [ObserversRpc]
        private void RpcSfx(string id) => GameAudio.Play(id);

        /// <summary>Floating status text over a unit — saves, conditions, death saves.
        /// Everything that used to be log-only feedback goes through here.</summary>
        [ObserversRpc]
        private void RpcStatusPopup(string targetId, string text, Color color, float size)
        {
            var view = ClientUnits.FirstOrDefault(u => u.Id == targetId);
            if (view?.Visual != null)
                CombatFx.Instance?.Popup(view.Visual.position, text, color, size);
        }

        [ObserversRpc]
        private void RpcAttackFx(string attackerId, string targetId, bool hit, bool crit, int damage)
        {
            var attacker = ClientUnits.FirstOrDefault(u => u.Id == attackerId);
            var target = ClientUnits.FirstOrDefault(u => u.Id == targetId);
            var fx = CombatFx.Instance;
            GameAudio.Play(!hit ? "miss" : crit ? "crit" : "hit");
            if (fx == null || target?.Visual == null) return;
            if (attacker?.Visual != null)
            {
                CombatFx.Face(attacker.Visual, target.Visual.position);
                CombatFx.Face(target.Visual, attacker.Visual.position);
                fx.Lunge(attacker.Visual, target.Visual.position);
                CharacterVisuals.Trigger(attacker.Visual, "Attack");
                // Triangle over MY current target so the fight is easy to follow.
                if (attacker.IsPc && attacker.OwnerId == LocalConnection.ClientId)
                    fx.ShowTargetMarker(target.Visual);
            }
            if (hit)
            {
                // Land the impact when the lunge reaches the target, not at wind-up.
                fx.After(0.12f, () =>
                {
                    if (target.Visual == null) return;
                    CharacterVisuals.Trigger(target.Visual, "Hit");
                    fx.Blood(target.Visual.position, crit);
                    fx.Flash(target.Visual, new Color(1f, 0.35f, 0.3f));
                    fx.Popup(target.Visual.position, crit ? $"{damage}!" : damage.ToString(),
                        crit ? new Color(1f, 0.6f, 0.15f) : Color.white, crit ? 1.4f : 1f);
                });
            }
            else
            {
                fx.Popup(target.Visual.position, "miss", new Color(0.8f, 0.8f, 0.85f), 0.8f);
            }
        }

        [ObserversRpc]
        private void RpcSpellFx(string casterId, string targetId, int amount, bool isHeal, int colorIdx)
        {
            var caster = ClientUnits.FirstOrDefault(u => u.Id == casterId);
            var target = ClientUnits.FirstOrDefault(u => u.Id == targetId);
            var fx = CombatFx.Instance;
            GameAudio.Play(isHeal ? "heal" : "spell");
            if (fx == null || target?.Visual == null) return;
            Color[] palette =
            {
                new Color(1f, 0.55f, 0.15f),   // fire
                new Color(1f, 0.9f, 0.5f),     // radiant
                new Color(0.7f, 0.5f, 1f),     // force/arcane
                new Color(0.45f, 1f, 0.55f)    // heal green
            };
            var color = palette[Mathf.Clamp(colorIdx, 0, palette.Length - 1)];
            if (caster?.Visual != null && caster != target)
            {
                CombatFx.Face(caster.Visual, target.Visual.position);
                CharacterVisuals.Trigger(caster.Visual, "Attack");
                fx.CastFlare(caster.Visual.position, color);
                fx.Bolt(caster.Visual.position, target.Visual.position, color);
                if (!isHeal && caster.IsPc && caster.OwnerId == LocalConnection.ClientId)
                    fx.ShowTargetMarker(target.Visual);
            }
            if (isHeal)
                fx.Popup(target.Visual.position, $"+{amount}", palette[3]);
            else if (amount > 0)
            {
                fx.Flash(target.Visual, color);
                fx.Popup(target.Visual.position, amount.ToString(), color);
            }
        }

        [Server]
        private void SyncHp(string unitId)
        {
            if (!_server.TryGetValue(unitId, out var u)) return;
            RpcHpSync(unitId, u.Creature.CurrentHp, u.Creature.IsDown, u.Creature.IsDead);
            // Companions are server-owned AI with no connection: firing a TargetRpc at
            // them logged "Target is not an observer" on every HP sync (hundreds of lines
            // a fight). Only real clients get their slot counts.
            if (u.Player != null && !u.Player.IsCompanion
                && u.Player.Owner != null && u.Player.Owner.IsValid
                && u.Creature is CharacterSheet sheet)
            {
                var slots = sheet.SlotsRemaining;
                TargetSlots(u.Player.Owner, slots[0], slots[1], slots[2]);
            }
        }

        [Server]
        private void ServerLog(string line) => RpcLog(line);

        private ServerUnit ValidatedActor(NetworkConnection conn, out string error)
        {
            error = "";
            if (!InCombat.Value || _engine == null) { error = "No combat in progress."; return null; }
            var active = _engine.ActiveCreature;
            var unit = _server[active.Id];
            if (unit.Player == null || unit.Player.Owner != conn)
            {
                error = "It is not your turn.";
                return null;
            }
            return unit;
        }

        // ---------- intents ----------

        [ServerRpc(RequireOwnership = false)]
        public void CmdMove(int dx, int dy, NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }
            if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0))
            { TargetReject(conn, "Invalid step."); return; }

            var dest = new Vector2Int(unit.Cell.x + dx, unit.Cell.y + dy);
            if (Math.Abs(dest.x) > 8 || Math.Abs(dest.y) > 8)
            { TargetReject(conn, "Edge of the battlefield."); return; }
            if (Occupied(dest)) { TargetReject(conn, "That space is occupied."); return; }

            try { _engine.SpendMovement(5); }
            catch (RuleViolationException e) { TargetReject(conn, e.Message); return; }

            unit.Cell = dest;
            RpcUnitMoved(unit.Id, dest.x, dest.y);
            BroadcastBudget();
        }

        /// <summary>Click-to-move: walk toward a destination cell, spending movement per
        /// 5 ft step until we arrive, get blocked, or run dry. Clicking an occupied cell
        /// (an enemy) stops adjacent to it. Same greedy stepping as the AI.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdMoveTo(int cx, int cy, NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }
            if (Math.Abs(cx) > 8 || Math.Abs(cy) > 8)
            { TargetReject(conn, "Edge of the battlefield."); return; }

            var dest = new Vector2Int(cx, cy);
            if (dest == unit.Cell) return;
            bool stopAdjacent = Occupied(dest);
            Vector2Int from = unit.Cell;
            while (Chebyshev(dest, unit.Cell) > (stopAdjacent ? 1 : 0))
            {
                var step = new Vector2Int(
                    unit.Cell.x + Math.Sign(dest.x - unit.Cell.x),
                    unit.Cell.y + Math.Sign(dest.y - unit.Cell.y));
                if (Occupied(step)) break;
                try { _engine.SpendMovement(5); }
                catch (RuleViolationException) { break; }
                unit.Cell = step;
            }
            if (unit.Cell == from)
            { TargetReject(conn, "Can't move there — blocked or out of movement."); return; }
            RpcUnitMoved(unit.Id, unit.Cell.x, unit.Cell.y);
            BroadcastBudget();
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdAttack(string targetId, NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }
            if (!_server.TryGetValue(targetId, out var target) || target.Creature.IsDead)
            { TargetReject(conn, "No such target."); return; }
            var reach = unit.Player.BasicAttack();
            if (Chebyshev(unit.Cell, target.Cell) * 5 > reach.RangeFeet)
            {
                TargetReject(conn, reach.RangeFeet > 5
                    ? $"Out of range ({reach.RangeFeet} ft max)."
                    : "Target is out of reach — move adjacent first.");
                return;
            }

            try { _engine.SpendAction(); }
            catch (RuleViolationException e) { TargetReject(conn, e.Message); return; }

            try
            {
                var attack = unit.Player.BasicAttack();
                var result = CombatMath.ResolveAttack(unit.Creature, target.Creature, attack, _rng);
                Narrate(unit.Creature, target.Creature, attack.Name, result);
            }
            catch (Exception e)
            {
                // Never let an exception escape a ServerRpc — FishNet kicks the sender.
                Debug.LogError($"[Combat] Attack resolution failed: {e}");
                TargetReject(conn, "The attack fizzled — a bug was logged.");
            }
            SyncHp(target.Id);
            BroadcastBudget();
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdCast(string spellId, string targetId, NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }
            var sheet = (CharacterSheet)unit.Creature;
            if (!sheet.KnownSpells.Contains(spellId))
            { TargetReject(conn, "You don't know that spell."); return; }
            SpellDefinition spell;
            try { spell = SpellLibrary.Get(spellId); }
            catch (KeyNotFoundException) { TargetReject(conn, "Unknown spell."); return; }
            if (!_server.TryGetValue(targetId, out var target) || target.Creature.IsDead)
            { TargetReject(conn, "No such target."); return; }

            int distFeet = Chebyshev(unit.Cell, target.Cell) * 5;
            if (spell.RangeFeet > 0 && distFeet > spell.RangeFeet)
            { TargetReject(conn, $"Out of range ({distFeet} ft > {spell.RangeFeet} ft)."); return; }

            try
            {
                if (spell.IsBonusAction) _engine.SpendBonusAction();
                else _engine.SpendAction();
            }
            catch (RuleViolationException e) { TargetReject(conn, e.Message); return; }

            try
            {
                List<SpellEvent> events;
                if (spellId == "sleep")
                {
                    var area = _server.Values
                        .Where(u => !u.Creature.IsDead && Chebyshev(u.Cell, target.Cell) <= 2
                                    && u.Player == null)
                        .Select(u => u.Creature).ToList();
                    events = SpellEngine.CastSleep(sheet, spell, area, Math.Max(1, spell.Level), _rng);
                }
                else if (spellId == "burning_hands")
                {
                    var area = _server.Values
                        .Where(u => !u.Creature.IsDead && u.Id != unit.Id
                                    && Chebyshev(u.Cell, target.Cell) <= 1)
                        .Select(u => u.Creature).Distinct().ToList();
                    if (!area.Contains(target.Creature)) area.Add(target.Creature);
                    events = SpellEngine.Cast(sheet, spell, area, Math.Max(1, spell.Level), _rng);
                }
                else if (spellId == "magic_missile")
                {
                    events = SpellEngine.Cast(sheet, spell,
                        new[] { target.Creature, target.Creature, target.Creature },
                        Math.Max(1, spell.Level), _rng);
                }
                else
                {
                    events = SpellEngine.Cast(sheet, spell, new[] { target.Creature },
                        Math.Max(1, spell.Level), _rng);
                }
                NarrateSpell(sheet, spell, events);
            }
            catch (RuleViolationException e)
            {
                TargetReject(conn, e.Message);
                return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Combat] Spell resolution failed: {e}");
                TargetReject(conn, "The spell fizzled — a bug was logged.");
            }

            foreach (var u in _server.Values) SyncHp(u.Id);
            BroadcastBudget();
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdDodge(NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }
            try { _engine.Dodge(); }
            catch (RuleViolationException e) { TargetReject(conn, e.Message); return; }
            ServerLog($"{unit.Creature.Name} takes the Dodge action.");
            BroadcastBudget();
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdEndTurn(NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }
            _turnDone = true;
        }

        [Server]
        private void NarrateSpell(CharacterSheet caster, SpellDefinition spell,
            List<SpellEvent> events)
        {
            foreach (var ev in events)
            {
                string targetName = _server.TryGetValue(GetTargetId(ev), out var t)
                    ? t.Creature.Name : "?";
                switch (ev)
                {
                    case SpellAttackEvent a:
                        ServerLog($"{caster.Name} casts {spell.Name} at {targetName}: " +
                                  (a.Result.Hit ? $"hit ({a.Result.Total})." : $"miss ({a.Result.Total})."));
                        if (!a.Result.Hit)
                        {
                            RpcStatusPopup(a.TargetId, "miss",
                                new Color(0.8f, 0.8f, 0.85f), 0.8f);
                            RpcSfx("miss");
                        }
                        break;
                    case SpellSaveEvent s:
                        ServerLog($"{targetName} {(s.Result.Success ? "saves against" : "fails to resist")} " +
                                  $"{spell.Name} ({s.Result.Total} vs DC {s.Dc}).");
                        if (s.Result.Success)
                            RpcStatusPopup(s.TargetId, "resisted",
                                new Color(0.7f, 0.85f, 1f), 0.9f);
                        break;
                    case SpellDamageEvent d:
                        ServerLog($"{spell.Name} deals {d.Damage} {d.DamageType} to {targetName}." +
                                  (d.TargetDied ? $" {targetName} is destroyed!" :
                                   d.TargetDowned ? $" {targetName} goes down!" : ""));
                        RpcSpellFx(caster.Id, d.TargetId, d.Damage, false,
                            d.DamageType == DamageType.Fire ? 0 :
                            d.DamageType == DamageType.Radiant ? 1 : 2);
                        if (d.TargetDied || d.TargetDowned) RpcSfx("down");
                        break;
                    case SpellHealEvent h:
                        ServerLog($"{spell.Name} restores {h.Healed} HP to {targetName}.");
                        RpcSpellFx(caster.Id, h.TargetId, h.Healed, true, 3);
                        break;
                    case SpellConditionEvent c:
                        ServerLog($"{targetName} is {c.Condition} ({spell.Name}).");
                        RpcStatusPopup(c.TargetId, c.Condition.ToString(),
                            new Color(0.8f, 0.6f, 1f), 1f);
                        RpcSfx("chime");
                        break;
                }
            }
        }

        private static string GetTargetId(SpellEvent ev) => ev switch
        {
            SpellAttackEvent a => a.TargetId,
            SpellSaveEvent s => s.TargetId,
            SpellDamageEvent d => d.TargetId,
            SpellHealEvent h => h.TargetId,
            SpellConditionEvent c => c.TargetId,
            _ => ""
        };

        [Server]
        private void BroadcastBudget()
        {
            if (_engine == null) return;
            var b = _engine.ActiveBudget;
            RpcBudget(_engine.ActiveCreature.Id, b.MovementRemaining,
                b.ActionAvailable, b.BonusActionAvailable);
            MaybeAutoEndTurn();
        }

        /// <summary>Ends a player's turn automatically once nothing useful remains:
        /// action spent, no movement left, and no usable bonus action (only the cleric's
        /// Healing Word qualifies in v1). Short grace delay so results stay readable.</summary>
        [Server]
        private void MaybeAutoEndTurn()
        {
            if (!InCombat.Value || _engine == null) return;
            var active = _engine.ActiveCreature;
            if (!_server.TryGetValue(active.Id, out var unit) || unit.Player == null) return;
            var b = _engine.ActiveBudget;
            if (b.ActionAvailable || b.MovementRemaining >= 5) return;

            bool bonusUseful = false;
            if (b.BonusActionAvailable && active is CharacterSheet sheet)
            {
                foreach (var spellId in sheet.KnownSpells)
                {
                    SpellDefinition spell;
                    try { spell = SpellLibrary.Get(spellId); }
                    catch (KeyNotFoundException) { continue; }
                    if (spell.IsBonusAction
                        && (spell.Level == 0 || sheet.SlotsRemaining.Any(s => s > 0)))
                    { bonusUseful = true; break; }
                }
            }
            if (!bonusUseful) StartCoroutine(AutoEndSoon(active.Id));
        }

        [Server]
        private IEnumerator AutoEndSoon(string unitId)
        {
            yield return new WaitForSeconds(1.1f);
            if (InCombat.Value && _engine != null && _engine.ActiveCreature.Id == unitId)
            {
                ServerLog($"{_engine.ActiveCreature.Name}'s turn ends.");
                _turnDone = true;
            }
        }

        // ==================== CLIENT ====================

        [ObserversRpc]
        private void RpcCombatStarted(Vector3 origin, string[] ids, string[] names,
            bool[] isPc, int[] maxHp, int[] hp, int[] cellX, int[] cellY, int[] ownerIds)
        {
            GridOrigin = origin;
            ClientUnits.Clear();
            Log.Clear();
            LastRejection = "";
            for (int i = 0; i < ids.Length; i++)
            {
                var view = new UnitView
                {
                    Id = ids[i], Name = names[i], IsPc = isPc[i],
                    MaxHp = maxHp[i], Hp = hp[i],
                    Cell = new Vector2Int(cellX[i], cellY[i]), OwnerId = ownerIds[i]
                };
                view.Visual = ResolveVisual(view);
                if (view.Visual != null)
                    SnapVisual(view);
                ClientUnits.Add(view);
            }
            BuildOverlay();
        }

        /// <summary>Monster id → (model names tried in order '|'-separated, tint, scale).
        /// First names are drop-in slots for imported packs (e.g. an Asset Store "Orc"
        /// prefab saved under Resources/Characters); later names are the KayKit
        /// stand-ins that ship today. Final fallback is a red capsule.</summary>
        private static readonly Dictionary<string, (string model, Color tint, float scale)>
            MonsterModels = new Dictionary<string, (string, Color, float)>
        {
            { "marsh_skulker", ("Ranger", new Color(0.75f, 0.8f, 0.6f), 1f) },
            // Bear and Rat are our own generated meshes (scripts/make_beasts.py): true
            // quadrupeds, already brown/grey and already at life scale, so no tint and
            // no resize. Before they existed both fell through to the capsule.
            { "dock_rat", ("Rat|Rogue", Color.white, 1.1f) },
            { "risen_drowned", ("Skeleton_Minion", new Color(0.6f, 0.85f, 0.7f), 1f) },
            { "bonewalker", ("Skeleton_Warrior", Color.white, 1f) },
            { "kindled_zealot", ("Rogue_Hooded", new Color(1f, 0.55f, 0.45f), 1f) },
            { "hollow_warden", ("Knight", new Color(0.65f, 0.4f, 0.4f), 1.3f) },
            // Quaternius orcs are already green — no tint when those prefabs exist.
            { "orc", ("Orc|Barbarian", Color.white, 1.05f) },
            { "orc_warchief", ("Orc_Skull|Orc|Barbarian", new Color(1f, 0.88f, 0.85f), 1.35f) },
            { "giant_spider", ("Spider", new Color(0.3f, 0.22f, 0.35f), 1.1f) },
            { "brown_bear", ("Bear", Color.white, 1f) },
            // A goblin is a small brutish humanoid: the orc model shrunk reads far better
            // than a green-tinted human rogue did.
            { "goblin", ("Goblin|Orc|Barbarian", new Color(0.85f, 1f, 0.75f), 0.72f) },
        };

        private Transform ResolveVisual(UnitView view)
        {
            if (view.IsPc)
            {
                // Companions all report owner -1; match those by replicated name.
                var holder = view.OwnerId >= 0
                    ? FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                        .FirstOrDefault(p => p.OwnerId == view.OwnerId)
                    : FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                        .FirstOrDefault(p => p.IsCompanion
                                             && p.CharacterName.Value == view.Name);
                return holder != null ? holder.transform : null;
            }

            // Character model when we have one (id format: m<i>_<monsterId>).
            string monsterId = view.Id.Substring(view.Id.IndexOf('_') + 1);
            if (MonsterModels.TryGetValue(monsterId, out var spec))
            {
                var root = new GameObject($"Monster_{view.Id}");
                GameObject model = null;
                foreach (var name in spec.model.Split('|'))
                {
                    model = CharacterVisuals.Attach(root.transform, name,
                        spec.tint, spec.scale);
                    if (model != null) break;
                }
                if (model != null)
                {
                    AttachLabel(root.transform, view.Name);
                    return root.transform;
                }
                Destroy(root);
            }

            // Capsule fallback. This used to happen silently, which is how the bear
            // shipped as a pill for so long — say so loudly in the log instead.
            Debug.LogWarning($"[RadiantPool] no model for monster '{monsterId}' " +
                             "— falling back to a capsule. Add a prefab under " +
                             "Resources/Characters and map it in CombatManager.MonsterModels.");
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Monster_{view.Id}";
            bool boss = view.MaxHp >= 30;   // bosses loom larger and darker
            go.transform.localScale = boss
                ? new Vector3(1.35f, 1.35f, 1.35f) : new Vector3(0.9f, 0.9f, 0.9f);
            var renderer = go.GetComponent<Renderer>();
            renderer.material.color = boss
                ? new Color(0.55f, 0.1f, 0.12f) : new Color(0.75f, 0.25f, 0.2f);
            AttachLabel(go.transform, view.Name);
            return go.transform;
        }

        private static void AttachLabel(Transform parent, string text)
        {
            var label = new GameObject("Label");
            label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            var tm = label.AddComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = 0.09f;
            tm.fontSize = 40;
            tm.anchor = TextAnchor.LowerCenter;
            tm.color = new Color(1f, 0.7f, 0.6f);
            label.AddComponent<Billboard>();
        }

        private void SnapVisual(UnitView view)
        {
            var pos = CellToWorld(view.Cell);
            if (view.IsPc && view.OwnerId == LocalConnection.ClientId)
                view.Visual.position = pos;                       // my own controller
            else if (!view.IsPc)
            {
                // Character-model roots sit at ground level; bare capsules pivot mid-body.
                bool hasModel = view.Visual.Find(CharacterVisuals.VisualName) != null;
                view.Visual.position = pos + (hasModel ? Vector3.zero : Vector3.up);
            }
            else if (view.Visual.GetComponent<NetworkObject>()?.IsOwner != true)
                return;                                            // other players sync via NT
        }

        [ObserversRpc]
        private void RpcTurnStarted(string unitId, int round, int move, bool action, bool bonus)
        {
            ActiveUnitId = unitId;
            Round = round;
            MoveLeft = move;
            ActionLeft = action;
            BonusLeft = bonus;
            LastRejection = "";
            var view = ClientUnits.FirstOrDefault(u => u.Id == unitId);
            CombatFx.Instance?.SetTurnMarker(view?.Visual, view?.IsPc ?? false);
            if (IsMyTurn) GameAudio.Play("chime", 0.7f);   // heads-up: you're up
        }

        [ObserversRpc]
        private void RpcBudget(string unitId, int move, bool action, bool bonus)
        {
            if (unitId != ActiveUnitId) return;
            MoveLeft = move;
            ActionLeft = action;
            BonusLeft = bonus;
        }

        [ObserversRpc]
        private void RpcUnitMoved(string unitId, int cx, int cy)
        {
            var view = ClientUnits.FirstOrDefault(u => u.Id == unitId);
            if (view == null) return;
            Vector2Int oldCell = view.Cell;
            view.Cell = new Vector2Int(cx, cy);
            if (view.Visual != null)
            {
                bool isMyBody = view.IsPc && view.OwnerId == LocalConnection.ClientId;
                if ((!view.IsPc || isMyBody) && CombatFx.Instance != null)
                {
                    // Monsters and my own character walk to the cell (MotionAnimator
                    // plays the cycle from the displacement) instead of teleporting.
                    // Other players' bodies arrive via their own NetworkTransform.
                    bool hasModel = view.Visual.Find(CharacterVisuals.VisualName) != null;
                    CombatFx.Instance.Glide(view.Visual, CellToWorld(view.Cell)
                        + (!view.IsPc && !hasModel ? Vector3.up : Vector3.zero));
                }
                else
                {
                    Vector3 oldPos = CellToWorld(oldCell);
                    SnapVisual(view);
                    CombatFx.Face(view.Visual, oldPos + (CellToWorld(view.Cell) - oldPos) * 2f);
                }
            }
            // Local guess until the authoritative RpcBudget lands right behind this.
            if (unitId == ActiveUnitId && IsMyTurn)
                MoveLeft = Math.Max(0, MoveLeft - 5 * Chebyshev(oldCell, view.Cell));
        }

        [ObserversRpc]
        private void RpcHpSync(string unitId, int hp, bool down, bool dead)
        {
            var view = ClientUnits.FirstOrDefault(u => u.Id == unitId);
            if (view == null) return;
            view.Hp = hp;
            view.Down = down;
            view.Dead = dead;
            if (view.Visual == null) return;
            bool hasModel = view.Visual.Find(CharacterVisuals.VisualName) != null;
            if (hasModel)
            {
                // Character models play their death pose and stay as corpses.
                CharacterVisuals.SetDead(view.Visual, dead || down);
            }
            else if (!view.IsPc)
            {
                view.Visual.gameObject.SetActive(!dead);
            }
            else
            {
                view.Visual.localRotation = down || dead
                    ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            }
        }

        /// <summary>Hub shrine where a defeated party wakes (matches the bootstrap's
        /// Shrine of the Dawnmother).</summary>
        public static readonly Vector3 RespawnPoint = new Vector3(-9f, 0.15f, -14f);

        /// <summary>Moves the receiving player's own character (their client owns the
        /// transform; the NetworkTransform replicates it back out).</summary>
        [FishNet.Object.TargetRpc]
        private void TargetRespawn(NetworkConnection conn, Vector3 position)
        {
            var mine = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);
            if (mine == null) return;
            var cc = mine.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            mine.transform.position = position;
            if (cc != null) cc.enabled = true;
        }

        [FishNet.Object.TargetRpc]
        private void TargetSlots(NetworkConnection conn, int s1, int s2, int s3) =>
            MySlots = new[] { s1, s2, s3 };

        [FishNet.Object.TargetRpc]
        private void TargetReject(NetworkConnection conn, string reason) =>
            LastRejection = reason;

        [ObserversRpc]
        private void RpcLog(string line)
        {
            Log.Add(line);
            if (Log.Count > 60) Log.RemoveAt(0);
        }

        private static string PrettyItem(string id) =>
            id.Replace('_', ' ');

        // Outcome banner state, read by CombatClientUI after the fight ends.
        public string BannerTitle { get; private set; } = "";
        public string BannerDetail { get; private set; } = "";
        public bool BannerVictory { get; private set; }
        public float BannerUntil { get; private set; }

        [ObserversRpc]
        private void RpcCombatEnded(bool victory, int xpEach, string lootSummary)
        {
            BannerVictory = victory;
            BannerTitle = victory ? "VICTORY!" : "DEFEATED";
            BannerDetail = victory
                ? $"+{xpEach} XP each" + (lootSummary.Length > 0 ? $"\nLoot: {lootSummary}" : "")
                : "The party is carried back to Havenrock,\nbruised but alive. The block remains hostile.";
            BannerUntil = Time.time + 6f;

            ActiveUnitId = "";
            CombatFx.Instance?.ClearTurnMarker();
            foreach (var u in ClientUnits.Where(u => !u.IsPc && u.Visual != null))
                Destroy(u.Visual.gameObject);
            foreach (var u in ClientUnits.Where(u => u.IsPc && u.Visual != null))
            {
                u.Visual.localRotation = Quaternion.identity;
                // Everyone alive is on their feet after combat (revive/respawn) — clear
                // the death pose too, not just the capsule rotation.
                CharacterVisuals.SetDead(u.Visual, false);
            }
            if (_overlay != null) Destroy(_overlay);
            ClientUnits.Clear();
        }

        private void BuildOverlay()
        {
            if (_overlay != null) Destroy(_overlay);
            _overlay = new GameObject("GridOverlay");
            // Resources material: shader guaranteed in build (Shader.Find gets stripped).
            var mat = Resources.Load<Material>("Fx/M_GridOverlay");
            if (mat == null)
            {
                var quadTemp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                mat = new Material(quadTemp.GetComponent<Renderer>().sharedMaterial);
                Destroy(quadTemp);
            }
            for (int x = -8; x <= 8; x++)
                for (int y = -8; y <= 8; y++)
                {
                    if ((x + y) % 2 != 0) continue;   // checkerboard
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(_overlay.transform, false);
                    quad.transform.position = GridOrigin
                        + new Vector3(x * CellSize, 0.03f, y * CellSize);
                    quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    quad.transform.localScale = Vector3.one * (CellSize * 0.96f);
                    quad.GetComponent<Renderer>().sharedMaterial = mat;
                }
        }
    }
}
