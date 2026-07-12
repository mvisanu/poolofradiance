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
                    SyncHp(active.Id);
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
                    yield return new WaitForSeconds(0.9f);
                    MonsterAct(unit);
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

        [Server]
        private void MonsterAct(ServerUnit monster)
        {
            var targets = _server.Values
                .Where(u => u.Player != null && !u.Creature.IsDead && !u.Creature.IsDown)
                .OrderBy(u => Chebyshev(u.Cell, monster.Cell)).ToList();
            if (targets.Count == 0) return;
            var target = targets[0];

            int steps = monster.Creature.Speed / 5;
            while (Chebyshev(target.Cell, monster.Cell) > 1 && steps-- > 0)
            {
                var step = new Vector2Int(
                    monster.Cell.x + Math.Sign(target.Cell.x - monster.Cell.x),
                    monster.Cell.y + Math.Sign(target.Cell.y - monster.Cell.y));
                if (Occupied(step)) break;
                monster.Cell = step;
            }
            RpcUnitMoved(monster.Id, monster.Cell.x, monster.Cell.y);

            if (Chebyshev(target.Cell, monster.Cell) <= 1)
            {
                var attack = monster.MonsterDef.Attacks[0];
                var result = CombatMath.ResolveAttack(monster.Creature, target.Creature, attack, _rng);
                Narrate(monster.Creature, target.Creature, attack.Name, result);
                SyncHp(target.Id);
            }
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
                foreach (var pc in _server.Values.Where(u => u.Player != null))
                    pc.Player.Sheet.ReviveFull();
                ServerLog("The party falls… and wakes back at Havenrock, bruised but alive.");
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
                fx.Lunge(attacker.Visual, target.Visual.position);
                CharacterVisuals.Trigger(attacker.Visual, "Attack");
            }
            if (hit)
            {
                CharacterVisuals.Trigger(target.Visual, "Hit");
                fx.Flash(target.Visual, new Color(1f, 0.35f, 0.3f));
                fx.Popup(target.Visual.position, crit ? $"{damage}!" : damage.ToString(),
                    crit ? new Color(1f, 0.6f, 0.15f) : Color.white, crit ? 1.4f : 1f);
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
                CharacterVisuals.Trigger(caster.Visual, "Attack");
                fx.Bolt(caster.Visual.position, target.Visual.position, color);
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
            if (u.Player != null && u.Creature is CharacterSheet sheet)
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

        [ServerRpc(RequireOwnership = false)]
        public void CmdAttack(string targetId, NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }
            if (!_server.TryGetValue(targetId, out var target) || target.Creature.IsDead)
            { TargetReject(conn, "No such target."); return; }
            if (Chebyshev(unit.Cell, target.Cell) > 1)
            { TargetReject(conn, "Target is out of reach — move adjacent first."); return; }

            try { _engine.SpendAction(); }
            catch (RuleViolationException e) { TargetReject(conn, e.Message); return; }

            var attack = unit.Player.BasicAttack();
            var result = CombatMath.ResolveAttack(unit.Creature, target.Creature, attack, _rng);
            Narrate(unit.Creature, target.Creature, attack.Name, result);
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
                        break;
                    case SpellSaveEvent s:
                        ServerLog($"{targetName} {(s.Result.Success ? "saves against" : "fails to resist")} " +
                                  $"{spell.Name} ({s.Result.Total} vs DC {s.Dc}).");
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

        /// <summary>Monster id → (KayKit model, tint, scale). Fallback is a red capsule.</summary>
        private static readonly Dictionary<string, (string model, Color tint, float scale)>
            MonsterModels = new Dictionary<string, (string, Color, float)>
        {
            { "marsh_skulker", ("Ranger", new Color(0.75f, 0.8f, 0.6f), 1f) },
            { "risen_drowned", ("Skeleton_Minion", new Color(0.6f, 0.85f, 0.7f), 1f) },
            { "bonewalker", ("Skeleton_Warrior", Color.white, 1f) },
            { "kindled_zealot", ("Rogue_Hooded", new Color(1f, 0.55f, 0.45f), 1f) },
            { "hollow_warden", ("Knight", new Color(0.65f, 0.4f, 0.4f), 1.3f) },
        };

        private Transform ResolveVisual(UnitView view)
        {
            if (view.IsPc)
            {
                var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => p.OwnerId == view.OwnerId);
                return holder != null ? holder.transform : null;
            }

            // Character model when we have one (id format: m<i>_<monsterId>).
            string monsterId = view.Id.Substring(view.Id.IndexOf('_') + 1);
            if (MonsterModels.TryGetValue(monsterId, out var spec))
            {
                var root = new GameObject($"Monster_{view.Id}");
                var model = CharacterVisuals.Attach(root.transform, spec.model,
                    spec.tint, spec.scale);
                if (model != null)
                {
                    AttachLabel(root.transform, view.Name);
                    return root.transform;
                }
                Destroy(root);
            }

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
            view.Cell = new Vector2Int(cx, cy);
            if (view.Visual != null) SnapVisual(view);
            if (unitId == ActiveUnitId && IsMyTurn) MoveLeft = Math.Max(0, MoveLeft - 5);
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
                u.Visual.localRotation = Quaternion.identity;
            if (_overlay != null) Destroy(_overlay);
            if (!victory)
            {
                var mine = MyUnit;
                if (mine?.Visual != null)
                    mine.Visual.position = new Vector3(0f, 0.1f, -4f); // hub spawn area
            }
            ClientUnits.Clear();
        }

        private void BuildOverlay()
        {
            if (_overlay != null) Destroy(_overlay);
            _overlay = new GameObject("GridOverlay");
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.color = new Color(1f, 1f, 1f, 0.14f);
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
