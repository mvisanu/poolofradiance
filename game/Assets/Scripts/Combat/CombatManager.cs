using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            public float DisplayHp;
            public float LabelHeight = 1.9f;
            public int TargetShape;
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
        public BattleState State { get; private set; } = BattleState.Inactive;
        public bool ActionResolving => State == BattleState.ExecutingPlayerAction
            || State == BattleState.ExecutingEnemyAction
            || State == BattleState.ApplyingDamage
            || State == BattleState.UpdatingUi
            || State == BattleState.CheckingBattleResult;
        public bool CanAcceptPlayerInput => InCombat.Value && IsMyTurn
            && State == BattleState.WaitingForPlayerInput;

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
        private EncounterTrigger _retryEncounter;
        private int _encounterCharacterLevel = 1;
        private int _encounterMonsterLevel = 1;
        private Vector3 _origin;
        private bool _turnDone;
        private GameObject _overlay;
        private Material _gridMaterial;
        private int _actionSequence;
        private bool _actionQueueRunning;

        private sealed class ServerCombatAction
        {
            public BattleState ExecutionState;
            public Func<IEnumerator> CreateRoutine;
        }

        private readonly CombatActionQueue<ServerCombatAction> _actionQueue =
            new CombatActionQueue<ServerCombatAction>();

        public int EncounterCharacterLevel => _encounterCharacterLevel;
        public int EncounterMonsterLevel => _encounterMonsterLevel;
        public bool AllSpawnedMonstersMatchEncounterLevel => _server.Values
            .Where(u => u.MonsterDef != null)
            .All(u => u.Creature.EncounterLevel == _encounterMonsterLevel);

        private void Awake() => Instance = this;
        private void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            int capture = System.Array.IndexOf(args, "-creaturecapture");
            string capturePath = capture >= 0 && capture + 1 < args.Length
                ? args[capture + 1] : "";
            if (System.Array.IndexOf(args, "-creaturetest") >= 0 || capturePath.Length > 0)
                StartCoroutine(CreatureVisualSelfTest(capturePath));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_gridMaterial != null) Destroy(_gridMaterial);
        }

        private Vector3 CellToWorld(Vector2Int c) =>
            _originForClients() + new Vector3(c.x * CellSize, 0f, c.y * CellSize);
        private Vector3 _originForClients() => IsServerStarted ? _origin : GridOrigin;

        [Server]
        private void SetBattleState(BattleState state)
        {
            State = state;
            RpcBattleState((int)state);
        }

        // ==================== SERVER ====================

        [Server]
        public void StartEncounter(EncounterTrigger encounter)
        {
            if (InCombat.Value) return;
            SetBattleState(BattleState.Initializing);
            _rng ??= new SeededRng(Environment.TickCount);
            _encounter = encounter;
            _retryEncounter = null;
            _server.Clear();
            _actionQueue.Clear();
            _actionQueueRunning = false;
            _origin = new Vector3(
                Mathf.Round(encounter.transform.position.x / CellSize) * CellSize, 0f,
                Mathf.Round(encounter.transform.position.z / CellSize) * CellSize);

            var players = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .Where(p => p.Sheet != null && !p.Sheet.IsDead).ToList();
            if (players.Count == 0)
            {
                SetBattleState(BattleState.Inactive);
                return;
            }

            SetBattleState(BattleState.StartingBattle);

            // Scale from the strongest connected hero, not an AI companion. This prevents
            // a high-level player from bringing level-1 enemies into co-op while ensuring
            // companions never raise the difficulty above their leader.
            _encounterCharacterLevel = players.Where(p => !p.IsCompanion)
                .Select(p => p.Sheet.Level).DefaultIfEmpty(players.Max(p => p.Sheet.Level)).Max();
            _encounterMonsterLevel = Difficulty.TargetMonsterLevel(_encounterCharacterLevel);

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
                var creature = def.Spawn($"m{m}_{monsterId}", _rng,
                    encounterLevel: _encounterMonsterLevel);
                _server[creature.Id] = new ServerUnit
                {
                    Id = creature.Id, Creature = creature, MonsterDef = def,
                    Cell = new Vector2Int(-2 + (m % 4), 2 + (m / 4))
                };
                m++;
            }

            SetBattleState(BattleState.CalculatingTurnOrder);
            _engine = new TurnEngine(_server.Values.Select(u => u.Creature), _rng);
            InCombat.Value = true;

            // Companions are server-moved: put their bodies on their assigned cells.
            foreach (var u in _server.Values) ServerRepositionCompanion(u);

            var ordered = _engine.InitiativeOrder.Select(c => _server[c.Id]).ToList();
            RpcCombatStarted(_origin,
                ordered.Select(u => u.Id).ToArray(),
                ordered.Select(u => u.MonsterDef == null ? u.Creature.Name
                    : $"{u.Creature.Name} (L{u.Creature.EncounterLevel})").ToArray(),
                ordered.Select(u => u.Player != null).ToArray(),
                ordered.Select(u => u.Creature.MaxHp).ToArray(),
                ordered.Select(u => u.Creature.CurrentHp).ToArray(),
                ordered.Select(u => u.Cell.x).ToArray(),
                ordered.Select(u => u.Cell.y).ToArray(),
                ordered.Select(u => u.Player != null ? u.Player.OwnerId : -1).ToArray());
            ServerLog($"Ambush at {encounter.DisplayName}! Level {_encounterMonsterLevel} " +
                      $"enemies challenge the level-{_encounterCharacterLevel} party. Roll initiative.");
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
                    SetBattleState(BattleState.ExecutingEnemyAction);
                    yield return new WaitForSeconds(0.45f);
                    yield return MonsterTurn(unit);
                    if (CheckCombatEnd()) yield break;
                    EndActiveTurn();
                    continue;
                }

                if (unit.Player.IsCompanion)
                {
                    SetBattleState(BattleState.ExecutingEnemyAction);
                    yield return new WaitForSeconds(0.45f);
                    yield return CompanionTurn(unit);
                    if (CheckCombatEnd()) yield break;
                    EndActiveTurn();
                    continue;
                }

                // Player turn: wait for intents until EndTurn or timeout.
                _turnDone = false;
                SetBattleState(BattleState.WaitingForPlayerInput);
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
            // Combat can resolve from the parallel controlled action timeline before the
            // turn coroutine resumes. Never advance a cleared engine (and never let a
            // coroutine exception escape into the player log).
            var engine = _engine;
            if (!InCombat.Value || engine == null) return;
            SetBattleState(BattleState.CheckingBattleResult);
            engine.EndTurn();
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
            int longestRange = monster.MonsterDef.Attacks
                .Select(a => a.RangeFeet).DefaultIfEmpty(5).Max();
            while (Chebyshev(target.Cell, monster.Cell) * 5 > longestRange && steps-- > 0)
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

            int distanceFeet = Chebyshev(target.Cell, monster.Cell) * 5;
            var chosen = monster.MonsterDef.Attacks
                .Where(a => a.RangeFeet >= distanceFeet)
                .OrderByDescending(a => a.Damage.Average)
                .ThenBy(a => a.RangeFeet)
                .FirstOrDefault();
            if (chosen != null)
            {
                yield return ResolvePhysicalAction(monster, target, chosen);
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
                        yield return TryCompanionCast(self, "cure_wounds", hurt, 1);
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
                yield return TryCompanionCast(self, sheet.Class == CharacterClass.Wizard
                    ? "fire_bolt" : "sacred_flame", target, 0);
                yield break;
            }

            yield return MoveCompanion(self, target.Cell, stopAdjacent: true);
            if (Chebyshev(self.Cell, target.Cell) <= 1)
            {
                yield return TryCompanionAttack(self, target);
            }
        }

        [Server]
        private IEnumerator TryCompanionCast(ServerUnit self, string spellId,
            ServerUnit target, int slot)
        {
            SpellDefinition spell;
            try { spell = SpellLibrary.Get(spellId); }
            catch (Exception e)
            {
                Debug.LogError($"[Combat] Companion spell lookup failed: {e}");
                yield break;
            }
            yield return ResolveSpellAction(self, target, spell, slot);
        }

        [Server]
        private IEnumerator TryCompanionAttack(ServerUnit self, ServerUnit target)
        {
            AttackDefinition attack;
            try
            {
                attack = self.Player.BasicAttack();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Combat] Companion attack lookup failed: {e}");
                yield break;
            }
            yield return ResolvePhysicalAction(self, target, attack);
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

            SetBattleState(playersWon ? BattleState.Victory : BattleState.Defeat);
            InCombat.Value = false;
            if (playersWon)
            {
                _retryEncounter = null;
                // Full encounter XP to every member (co-op convention; the level-1..5
                // curve is tuned for this — see CampaignSimulationTests).
                int xpEach = _server.Values.Where(u => u.MonsterDef != null)
                    .Sum(u => u.MonsterDef.Xp);
                var pcs = _server.Values.Where(u => u.Player != null).ToList();
                foreach (var pc in pcs)
                {
                    // Through the director, not Sheet.GainXp: that is where the level loop
                    // lives, so a kill can actually LEVEL you instead of quietly banking XP
                    // until the next quest award happened to run it.
                    if (GameDirector.Instance != null)
                        GameDirector.Instance.ServerGrantXp(pc.Player, xpEach);
                    else
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
                // Required quest fights can reveal one level-matched equipment cache in
                // addition to canonical creature drops. The rules library owns the chance
                // and tier so late play through an early branch still feels rewarding.
                if (_encounter != null && _encounter.RequiredForClear)
                {
                    var scaled = LootLibrary.RollScaledEncounterReward(
                        _encounterCharacterLevel, _rng);
                    items.AddRange(scaled.ItemIds);
                    if (scaled.ItemIds.Count > 0)
                        ServerLog($"Challenge cache (level {_encounterCharacterLevel}): " +
                                  string.Join(", ", scaled.ItemIds.Select(PrettyItem)) + ".");
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
                _retryEncounter = _encounter;
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
            _actionQueue.Clear();
            _engine = null;
            _encounter = null;
            return true;
        }

        private static int Chebyshev(Vector2Int a, Vector2Int b) =>
            Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));

        private bool Occupied(Vector2Int cell) =>
            _server.Values.Any(u => u.Cell == cell && !u.Creature.IsDead);

        [Server]
        private void Narrate(Creature attacker, Creature target, AttackDefinition attack,
            AttackResult r)
        {
            string line = !r.Hit
                ? $"{attacker.Name}'s {attack.Name} misses {target.Name} ({r.Total} vs AC {target.ArmorClass})."
                : $"{attacker.Name}'s {attack.Name}{(r.Critical ? " CRITS" : " hits")} {target.Name} " +
                  $"for {r.DamageDealt} ({r.Total} vs AC {target.ArmorClass})." +
                  (r.TargetDied ? $" {target.Name} is slain!" :
                   r.TargetDowned ? $" {target.Name} goes down!" : "");
            ServerLog(line);
            RpcAttackImpact(attacker.Id, target.Id, attack.Name,
                attack.VisualEffectId, r.Hit, r.Critical, r.DamageDealt);
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
        private void RpcAttackWindup(string attackerId, string targetId,
            string animationTrigger, bool requiresApproach)
        {
            var attacker = ClientUnits.FirstOrDefault(u => u.Id == attackerId);
            var target = ClientUnits.FirstOrDefault(u => u.Id == targetId);
            var fx = CombatFx.Instance;
            if (fx == null || target?.Visual == null) return;
            if (attacker?.Visual != null)
            {
                CombatFx.Face(attacker.Visual, target.Visual.position);
                CombatFx.Face(target.Visual, attacker.Visual.position);
                if (requiresApproach) fx.Lunge(attacker.Visual, target.Visual.position);
                CharacterVisuals.Trigger(attacker.Visual, animationTrigger);
                // Triangle over MY current target so the fight is easy to follow.
                if (attacker.IsPc && attacker.OwnerId == LocalConnection.ClientId)
                    fx.ShowTargetMarker(target.Visual);
            }
        }

        [ObserversRpc]
        private void RpcAttackImpact(string attackerId, string targetId, string attackName,
            string visualEffectId, bool hit, bool crit, int damage)
        {
            var attacker = ClientUnits.FirstOrDefault(u => u.Id == attackerId);
            var target = ClientUnits.FirstOrDefault(u => u.Id == targetId);
            var fx = CombatFx.Instance;
            GameAudio.PlayWeaponAttack(attackName, hit, crit);
            if (fx != null && target != null && target.Visual != null)
            {
                fx.AttackFeedback(target.Visual.position, hit, crit);
                if (hit) fx.ConfiguredEffect(visualEffectId, target.Visual.position);
            }
            if (fx == null || target?.Visual == null) return;
            if (hit)
            {
                CharacterVisuals.Trigger(target.Visual, "Hit");
                fx.Blood(target.Visual.position, crit);
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
        private void RpcSpellWindup(string casterId, string targetId, string spellId,
            string animationTrigger, string soundId, bool isHeal, int colorIdx)
        {
            var caster = ClientUnits.FirstOrDefault(u => u.Id == casterId);
            var target = ClientUnits.FirstOrDefault(u => u.Id == targetId);
            var fx = CombatFx.Instance;
            RpcSpellAudioLocal(soundId.Length > 0 ? soundId : spellId, isHeal);
            if (fx == null || caster?.Visual == null) return;
            Color[] palette =
            {
                new Color(1f, 0.55f, 0.15f),
                new Color(1f, 0.9f, 0.5f),
                new Color(0.7f, 0.5f, 1f),
                new Color(0.45f, 1f, 0.55f)
            };
            var color = palette[Mathf.Clamp(colorIdx, 0, palette.Length - 1)];
            if (target?.Visual != null)
            {
                CombatFx.Face(caster.Visual, target.Visual.position);
                if (!isHeal && caster.IsPc && caster.OwnerId == LocalConnection.ClientId)
                    fx.ShowTargetMarker(target.Visual);
            }
            CharacterVisuals.Trigger(caster.Visual, animationTrigger);
            fx.CastFlare(caster.Visual.position, color);
            if (target?.Visual != null && caster != target)
                fx.Bolt(caster.Visual.position, target.Visual.position, color);
        }

        [ObserversRpc]
        private void RpcSpellFx(string casterId, string targetId, int amount, bool isHeal,
            int colorIdx, string visualEffectId)
        {
            var caster = ClientUnits.FirstOrDefault(u => u.Id == casterId);
            var target = ClientUnits.FirstOrDefault(u => u.Id == targetId);
            var fx = CombatFx.Instance;
            if (fx == null || target?.Visual == null) return;
            Color[] palette =
            {
                new Color(1f, 0.55f, 0.15f),   // fire
                new Color(1f, 0.9f, 0.5f),     // radiant
                new Color(0.7f, 0.5f, 1f),     // force/arcane
                new Color(0.45f, 1f, 0.55f)    // heal green
            };
            var color = palette[Mathf.Clamp(colorIdx, 0, palette.Length - 1)];
            if (isHeal)
                fx.Popup(target.Visual.position, $"+{amount}", palette[3]);
            else if (amount > 0)
            {
                CharacterVisuals.Trigger(target.Visual, "Hit");
                fx.Flash(target.Visual, color);
                fx.Popup(target.Visual.position, amount.ToString(), color);
            }
            fx.ConfiguredEffect(visualEffectId, target.Visual.position);
        }

        [ObserversRpc]
        private void RpcSpellAudio(string spellId, bool isHeal) =>
            GameAudio.PlaySpell(spellId, isHeal);

        private static void RpcSpellAudioLocal(string spellId, bool isHeal) =>
            GameAudio.PlaySpell(spellId, isHeal);

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

        /// <summary>Every player action crosses one serial queue. The queue owns the input
        /// lock until the controlled animation timeline, impact, HP sync, and recovery are
        /// all complete.</summary>
        [Server]
        private void QueueServerAction(BattleState executionState, Func<IEnumerator> createRoutine)
        {
            string key = $"{_engine.ActiveCreature.Id}:{_engine.Round}:{++_actionSequence}";
            if (!_actionQueue.TryEnqueue(key, new ServerCombatAction
                {
                    ExecutionState = executionState,
                    CreateRoutine = createRoutine
                }))
                throw new InvalidOperationException($"Duplicate combat action '{key}'.");
            if (!_actionQueueRunning) StartCoroutine(DrainActionQueue());
        }

        [Server]
        private IEnumerator DrainActionQueue()
        {
            _actionQueueRunning = true;
            while (_actionQueue.TryStartNext(out var action))
            {
                SetBattleState(action.ExecutionState);
                var routine = action.CreateRoutine();
                while (routine != null)
                {
                    bool moved;
                    object current = null;
                    try
                    {
                        moved = routine.MoveNext();
                        if (moved) current = routine.Current;
                    }
                    catch (Exception e)
                    {
                        moved = false;
                        Debug.LogError($"[Combat] Queued action failed safely: {e}");
                        ServerLog("The action could not finish; combat recovered safely.");
                    }
                    if (!moved) break;
                    yield return current;
                }

                _actionQueue.Complete();
                if (!InCombat.Value) break;
                SetBattleState(BattleState.CheckingBattleResult);
                if (CheckCombatEnd()) break;
                BroadcastBudget();
                if (InCombat.Value && _engine != null
                    && _engine.ActiveCreature.IsPlayerCharacter)
                    SetBattleState(BattleState.WaitingForPlayerInput);
            }
            _actionQueueRunning = false;
        }

        [Server]
        private IEnumerator ResolvePhysicalAction(ServerUnit attacker, ServerUnit target,
            AttackDefinition attack)
        {
            RpcAttackWindup(attacker.Id, target.Id, attack.AnimationTrigger,
                attack.RequiresApproach);
            var timeline = new CombatActionTimeline(Time.time,
                attack.ImpactDelaySeconds, Math.Max(2.0, attack.ImpactDelaySeconds + 1.5));
            while (!timeline.TryTakeImpact(Time.time))
            {
                if (timeline.TimedOut(Time.time)) break;
                yield return null;
            }

            SetBattleState(BattleState.ApplyingDamage);
            var targetCheck = CombatTargeting.Validate(attacker.Creature, target.Creature,
                CombatTargetType.Hostile);
            if (!targetCheck.Allowed)
            {
                ServerLog($"{attacker.Creature.Name}'s action is cancelled: {targetCheck.Reason}");
            }
            else
            {
                try
                {
                    var result = CombatMath.ResolveAttack(
                        attacker.Creature, target.Creature, attack, _rng);
                    Narrate(attacker.Creature, target.Creature, attack, result);
                    SyncHp(target.Id);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Combat] Physical action failed safely: {e}");
                    ServerLog($"{attacker.Creature.Name}'s attack fizzles; combat continues.");
                }
            }

            SetBattleState(BattleState.UpdatingUi);
            yield return new WaitForSeconds(0.45f);
        }

        [Server]
        private IEnumerator ResolveSpellAction(ServerUnit caster, ServerUnit target,
            SpellDefinition spell, int slotLevel)
        {
            bool healing = spell.Effects.Any(e => e.Kind == EffectOpKind.Heal);
            RpcSpellWindup(caster.Id, target.Id, spell.Id, spell.AnimationTrigger,
                spell.SoundEffectId, healing, SpellColor(spell));
            var timeline = new CombatActionTimeline(Time.time,
                spell.ImpactDelaySeconds, Math.Max(2.0, spell.ImpactDelaySeconds + 1.5));
            while (!timeline.TryTakeImpact(Time.time))
            {
                if (timeline.TimedOut(Time.time)) break;
                yield return null;
            }

            SetBattleState(BattleState.ApplyingDamage);
            var targetCheck = CombatTargeting.Validate(caster.Creature, target.Creature,
                spell.TargetType, spell.AllowDownedTarget);
            if (!targetCheck.Allowed)
            {
                ServerLog($"{caster.Creature.Name}'s {spell.Name} is cancelled: " +
                          targetCheck.Reason);
            }
            else
            {
                try
                {
                    var events = ResolveSpellEvents(caster, target, spell, slotLevel);
                    NarrateSpell((CharacterSheet)caster.Creature, spell, events);
                    foreach (var u in _server.Values) SyncHp(u.Id);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Combat] Magic action failed safely: {e}");
                    ServerLog($"{caster.Creature.Name}'s {spell.Name} fizzles; combat continues.");
                }
            }

            SetBattleState(BattleState.UpdatingUi);
            yield return new WaitForSeconds(0.55f);
        }

        [Server]
        private List<SpellEvent> ResolveSpellEvents(ServerUnit caster, ServerUnit target,
            SpellDefinition spell, int slotLevel)
        {
            var sheet = (CharacterSheet)caster.Creature;
            if (spell.Id == "sleep")
            {
                var area = _server.Values
                    .Where(u => !u.Creature.IsDead && Chebyshev(u.Cell, target.Cell) <= 2
                                && u.Player == null)
                    .Select(u => u.Creature).ToList();
                return SpellEngine.CastSleep(sheet, spell, area, slotLevel, _rng);
            }
            if (spell.Id == "burning_hands")
            {
                var area = _server.Values
                    .Where(u => !u.Creature.IsDead && u.Id != caster.Id
                                && Chebyshev(u.Cell, target.Cell) <= 1)
                    .Select(u => u.Creature).Distinct().ToList();
                if (!area.Contains(target.Creature)) area.Add(target.Creature);
                return SpellEngine.Cast(sheet, spell, area, slotLevel, _rng);
            }
            if (spell.Id == "magic_missile")
                return SpellEngine.Cast(sheet, spell,
                    new[] { target.Creature, target.Creature, target.Creature }, slotLevel, _rng);
            return SpellEngine.Cast(sheet, spell, new[] { target.Creature }, slotLevel, _rng);
        }

        private static int SpellColor(SpellDefinition spell)
        {
            var damage = spell.Effects.FirstOrDefault(e => e.Kind == EffectOpKind.Damage);
            if (damage?.DamageType == DamageType.Fire) return 0;
            if (damage?.DamageType == DamageType.Radiant) return 1;
            return spell.Effects.Any(e => e.Kind == EffectOpKind.Heal) ? 3 : 2;
        }

        [Server]
        private IEnumerator ResolvePotionAction(ServerUnit unit, GameDirector director)
        {
            RpcSpellWindup(unit.Id, unit.Id, "potion_healing", "Attack",
                "potion_healing", true, 3);
            yield return new WaitForSeconds(0.2f);
            SetBattleState(BattleState.ApplyingDamage);
            try
            {
                int rolled = Dice.Roll("2d4+2", _rng).Total;
                int healed = unit.Creature.Heal(rolled);
                director.ServerConsumePotion();
                ServerLog($"{unit.Creature.Name} drinks a Potion of Healing " +
                          $"and restores {healed} HP.");
                RpcSpellFx(unit.Id, unit.Id, healed, true, 3, "potion_healing");
                RpcSfx("chime");
                SyncHp(unit.Id);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Combat] Potion action failed safely: {e}");
                ServerLog("The potion could not be used; combat continues.");
            }
            SetBattleState(BattleState.UpdatingUi);
            yield return new WaitForSeconds(0.4f);
        }

        private ServerUnit ValidatedActor(NetworkConnection conn, out string error)
        {
            error = "";
            if (!InCombat.Value || _engine == null) { error = "No combat in progress."; return null; }
            if (State != BattleState.WaitingForPlayerInput || _actionQueue.Count > 0)
            {
                error = "Wait for the current action to finish.";
                return null;
            }
            // A listen-server may execute its own ServerRpc locally; FishNet does not
            // consistently inject the sender connection on that direct host path. Remote
            // RPCs always carry their connection, so only a genuinely local null is
            // resolved here and the same owner check below remains authoritative.
            if (conn == null && IsClientStarted && LocalConnection != null
                && LocalConnection.IsValid)
                conn = LocalConnection;
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

        /// <summary>Click-to-move: shortest-path around occupied cells, spending movement
        /// per 5 ft step until we arrive or run dry. Clicking an occupied cell (an enemy)
        /// targets the nearest reachable adjacent cell.</summary>
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
            var path = FindGridPath(from, dest, stopAdjacent);
            if (path.Count == 0)
            {
                TargetReject(conn, "Can't move there - no open path.");
                return;
            }

            int availableSteps = _engine.ActiveBudget.MovementRemaining / 5;
            foreach (var step in path.Take(availableSteps))
            {
                try { _engine.SpendMovement(5); }
                catch (RuleViolationException) { break; }
                unit.Cell = step;
            }
            if (unit.Cell == from)
            { TargetReject(conn, "Can't move there — blocked or out of movement."); return; }
            RpcUnitMoved(unit.Id, unit.Cell.x, unit.Cell.y);
            BroadcastBudget();
        }

        /// <summary>Attack-test fixture: puts a second monster on the direct first step so
        /// the public one-click path must prove it can detour. Guarded by the executable
        /// flag and unavailable in normal play.</summary>
        [Server]
        public bool ServerArrangeBlockedApproachForTest(string moverId, string targetId)
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-attacktest") < 0)
                return false;
            if (!_server.TryGetValue(moverId, out var mover)
                || !_server.TryGetValue(targetId, out var target)) return false;
            var direct = new Vector2Int(
                mover.Cell.x + Math.Sign(target.Cell.x - mover.Cell.x),
                mover.Cell.y + Math.Sign(target.Cell.y - mover.Cell.y));
            if (direct == target.Cell || direct == mover.Cell) return false;

            var occupant = _server.Values.FirstOrDefault(u => u.Cell == direct && u != mover);
            if (occupant != null) return true;
            var blocker = _server.Values.FirstOrDefault(u => u != target && u != mover
                && u.Player == null && !u.Creature.IsDead);
            if (blocker == null) return false;
            blocker.Cell = direct;
            RpcUnitMoved(blocker.Id, direct.x, direct.y);
            return true;
        }

        /// <summary>Combat-flow fixture: clusters every surviving enemy beside the active
        /// hero and leaves it at 1 HP so one authored area spell can prove synchronized
        /// magic impact, resource cost, defeat removal, and victory resolution.</summary>
        [Server]
        public string ServerPrimeMagicFinishForTest()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(),
                    "-combatflowtest") < 0 || _engine == null)
                return "";
            if (!_server.TryGetValue(_engine.ActiveCreature.Id, out var actor)
                || actor.Player == null) return "";

            var enemies = _server.Values.Where(u => u.Player == null && !u.Creature.IsDead)
                .OrderBy(u => u.Id).ToArray();
            var offsets = new[]
            {
                new Vector2Int(0, 1), new Vector2Int(1, 1),
                new Vector2Int(-1, 1), new Vector2Int(1, 0),
                new Vector2Int(-1, 0), new Vector2Int(0, 2)
            };
            for (int i = 0; i < enemies.Length; i++)
            {
                enemies[i].Cell = actor.Cell + offsets[i % offsets.Length];
                if (enemies[i].Creature.CurrentHp > 1)
                    enemies[i].Creature.TakeDamage(
                        enemies[i].Creature.CurrentHp - 1, DamageType.Force);
                RpcUnitMoved(enemies[i].Id, enemies[i].Cell.x, enemies[i].Cell.y);
                SyncHp(enemies[i].Id);
            }
            return enemies.FirstOrDefault()?.Id ?? "";
        }

        /// <summary>Combat-flow fixture: defeats every hero through the real Creature
        /// damage path. The live turn loop must detect and present the loss.</summary>
        [Server]
        public bool ServerDefeatPartyForTest()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(),
                    "-combatflowtest") < 0 || _engine == null)
                return false;
            foreach (var hero in _server.Values.Where(u => u.Player != null))
            {
                hero.Creature.TakeDamage(hero.Creature.TempHp + hero.Creature.CurrentHp
                                         + hero.Creature.MaxHp,
                    DamageType.Force);
                SyncHp(hero.Id);
            }
            return true;
        }

        /// <summary>Breadth-first path on the small fixed combat board. Eight-way steps
        /// match the existing Chebyshev/5-foot movement rule. Occupied cells are walls,
        /// except that an occupied destination becomes an adjacency goal.</summary>
        private System.Collections.Generic.List<Vector2Int> FindGridPath(
            Vector2Int from, Vector2Int dest, bool stopAdjacent)
        {
            var open = new System.Collections.Generic.Queue<Vector2Int>();
            var seen = new System.Collections.Generic.HashSet<Vector2Int> { from };
            var previous = new System.Collections.Generic.Dictionary<Vector2Int, Vector2Int>();
            open.Enqueue(from);
            Vector2Int goal = from;
            bool found = false;

            while (open.Count > 0)
            {
                var cell = open.Dequeue();
                if ((stopAdjacent && Chebyshev(cell, dest) == 1)
                    || (!stopAdjacent && cell == dest))
                {
                    goal = cell;
                    found = true;
                    break;
                }

                // Prefer directions toward the target, then allow every detour. Sorting
                // keeps equally short paths deterministic across server runs.
                var neighbors = new System.Collections.Generic.List<Vector2Int>(8);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var next = new Vector2Int(cell.x + dx, cell.y + dy);
                        if (Math.Abs(next.x) > 8 || Math.Abs(next.y) > 8) continue;
                        if (seen.Contains(next) || Occupied(next)) continue;
                        neighbors.Add(next);
                    }
                foreach (var next in neighbors
                             .OrderBy(n => Chebyshev(n, dest))
                             .ThenBy(n => n.x).ThenBy(n => n.y))
                {
                    seen.Add(next);
                    previous[next] = cell;
                    open.Enqueue(next);
                }
            }

            var result = new System.Collections.Generic.List<Vector2Int>();
            if (!found || goal == from) return result;
            for (var at = goal; at != from; at = previous[at]) result.Add(at);
            result.Reverse();
            return result;
        }

        [ServerRpc(RequireOwnership = false)]
        public void CmdAttack(string targetId, NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }
            if (!_server.TryGetValue(targetId, out var target))
            { TargetReject(conn, "No such target."); return; }
            var targetCheck = CombatTargeting.Validate(unit.Creature, target.Creature,
                CombatTargetType.Hostile);
            if (!targetCheck.Allowed)
            { TargetReject(conn, targetCheck.Reason); return; }
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
                QueueServerAction(BattleState.ExecutingPlayerAction,
                    () => ResolvePhysicalAction(unit, target, attack));
            }
            catch (Exception e)
            {
                // Never let an exception escape a ServerRpc — FishNet kicks the sender.
                Debug.LogError($"[Combat] Attack resolution failed: {e}");
                TargetReject(conn, "The attack fizzled — a bug was logged.");
            }
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
            if (!_server.TryGetValue(targetId, out var target))
            { TargetReject(conn, "No such target."); return; }
            var targetCheck = CombatTargeting.Validate(sheet, target.Creature,
                spell.TargetType, spell.AllowDownedTarget);
            if (!targetCheck.Allowed)
            { TargetReject(conn, targetCheck.Reason); return; }

            int distFeet = Chebyshev(unit.Cell, target.Cell) * 5;
            if (spell.RangeFeet > 0 && distFeet > spell.RangeFeet)
            { TargetReject(conn, $"Out of range ({distFeet} ft > {spell.RangeFeet} ft)."); return; }
            int slotLevel = Math.Max(1, spell.Level);
            if (spell.Level > 0 && !sheet.HasSlot(slotLevel))
            { TargetReject(conn, $"No level-{slotLevel} spell slot remains."); return; }

            try
            {
                if (spell.IsBonusAction) _engine.SpendBonusAction();
                else _engine.SpendAction();
            }
            catch (RuleViolationException e) { TargetReject(conn, e.Message); return; }

            try
            {
                QueueServerAction(BattleState.ExecutingPlayerAction,
                    () => ResolveSpellAction(unit, target, spell, slotLevel));
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

        }

        /// <summary>Drink a Potion of Healing mid-fight. SRD 5.1: drinking a potion is an
        /// ACTION, so it costs the same as swinging — it is a real tactical choice, not a
        /// free top-up. Heals 2d4+2 and consumes one potion from the shared party stash.
        /// A downed character can't drink (they are unconscious); healing them is what
        /// Cure Wounds and Healing Word are for.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void CmdDrinkPotion(NetworkConnection conn = null)
        {
            var unit = ValidatedActor(conn, out string err);
            if (unit == null) { TargetReject(conn, err); return; }

            var director = GameDirector.Instance;
            if (director == null || !director.ServerHasPotion())
            { TargetReject(conn, "No potions in the party stash."); return; }
            if (unit.Creature.IsDown || unit.Creature.IsDead)
            { TargetReject(conn, "You are unconscious — you can't drink."); return; }
            if (unit.Creature.CurrentHp >= unit.Creature.MaxHp)
            { TargetReject(conn, "Already at full health — don't waste it."); return; }

            try { _engine.SpendAction(); }
            catch (RuleViolationException e) { TargetReject(conn, e.Message); return; }

            try
            {
                QueueServerAction(BattleState.ExecutingPlayerAction,
                    () => ResolvePotionAction(unit, director));
            }
            catch (Exception e)
            {
                // Never let an exception escape a ServerRpc — FishNet kicks the sender.
                Debug.LogError($"[Combat] Potion failed: {e}");
                TargetReject(conn, "The potion fizzled — a bug was logged.");
            }
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

        [ServerRpc(RequireOwnership = false)]
        public void CmdRetryEncounter(NetworkConnection conn = null)
        {
            if (InCombat.Value || State != BattleState.Defeat || _retryEncounter == null
                || _retryEncounter.Consumed)
            {
                TargetReject(conn, "That battle can no longer be retried.");
                return;
            }
            RpcDismissOutcome();
            StartEncounter(_retryEncounter);
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
                            d.DamageType == DamageType.Radiant ? 1 : 2,
                            spell.VisualEffectId);
                        if (d.TargetDied || d.TargetDowned) RpcSfx("down");
                        break;
                    case SpellHealEvent h:
                        ServerLog($"{spell.Name} restores {h.Healed} HP to {targetName}.");
                        RpcSpellFx(caster.Id, h.TargetId, h.Healed, true, 3,
                            spell.VisualEffectId);
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
        private void RpcBattleState(int state)
        {
            State = Enum.IsDefined(typeof(BattleState), state)
                ? (BattleState)state : BattleState.Inactive;
        }

        [ObserversRpc]
        private void RpcCombatStarted(Vector3 origin, string[] ids, string[] names,
            bool[] isPc, int[] maxHp, int[] hp, int[] cellX, int[] cellY, int[] ownerIds)
        {
            GridOrigin = origin;
            ClientUnits.Clear();
            Log.Clear();
            LastRejection = "";
            OutcomeOpen = false;
            BannerTitle = "";
            int monsterOrdinal = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                var view = new UnitView
                {
                    Id = ids[i], Name = names[i], IsPc = isPc[i],
                    MaxHp = maxHp[i], Hp = hp[i],
                    DisplayHp = hp[i],
                    Cell = new Vector2Int(cellX[i], cellY[i]), OwnerId = ownerIds[i]
                };
                if (!view.IsPc)
                    view.TargetShape = monsterOrdinal++ % CombatClientUI.TargetShapeCount;
                view.Visual = ResolveVisual(view);
                if (view.Visual != null)
                {
                    SnapVisual(view);
                    view.LabelHeight = MeasureOverlayHeight(view.Visual);
                }
                ClientUnits.Add(view);
            }
            BuildOverlay();
            if (CombatFx.Instance != null) CombatFx.Instance.CombatStarted(origin);
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

        /// <summary>Visible combat loadouts mirror each humanoid monster's authored
        /// attacks. Natural-weapon creatures intentionally have no entry.</summary>
        private static readonly Dictionary<string, (string weaponId, bool shield)>
            MonsterLoadouts = new Dictionary<string, (string, bool)>
        {
            { "marsh_skulker", ("light_crossbow", false) },
            { "bonewalker", ("shortsword", true) },
            { "kindled_zealot", ("dagger", false) },
            { "hollow_warden", ("greatsword", false) },
            { "orc", ("greataxe", false) },
            { "orc_warchief", ("greataxe", false) },
            { "goblin", ("shortsword", false) },
        };

        public static bool HasWeaponLoadout(string monsterId) =>
            MonsterLoadouts.ContainsKey(monsterId);

        /// <summary>Expected mounted hand model for a replicated monster unit id
        /// (`m0_monster_id`). Empty means natural weapons/no held item.</summary>
        public static string WeaponModelForUnit(string unitId)
        {
            int split = string.IsNullOrEmpty(unitId) ? -1 : unitId.IndexOf('_');
            if (split < 0 || split >= unitId.Length - 1) return "";
            string monsterId = unitId.Substring(split + 1);
            return MonsterLoadouts.TryGetValue(monsterId, out var loadout)
                ? GameItem.Get(loadout.weaponId)?.HandModel ?? "" : "";
        }

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
                    if (MonsterLoadouts.TryGetValue(monsterId, out var loadout))
                    {
                        var weapon = GameItem.Get(loadout.weaponId);
                        CharacterVisuals.SetHandItem(root.transform, "r",
                            weapon?.HandModel ?? "");
                        CharacterVisuals.SetHandItem(root.transform, "l",
                            loadout.shield ? "shield_badge" : "");
                    }
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
            RuntimeArt.Paint(go, boss
                ? new Color(0.55f, 0.1f, 0.12f) : new Color(0.75f, 0.25f, 0.2f));
            return go.transform;
        }

        private static float MeasureOverlayHeight(Transform root)
        {
            float maxY = root.position.y + 1.6f;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.GetComponent<TextMesh>() != null) continue;
                maxY = Mathf.Max(maxY, renderer.bounds.max.y);
            }
            return Mathf.Clamp(maxY - root.position.y + 0.25f, 1.25f, 4.5f);
        }

        /// <summary>Instantiates the actual ResolveVisual path for every rules-library
        /// monster, arranges them as a gallery, and validates renderers/materials/bounds.
        /// This catches missing Resources prefabs, capsule fallbacks, transparent art,
        /// degenerate imports, and FBX axis corrections that bury a creature.</summary>
        private IEnumerator CreatureVisualSelfTest(string capturePath)
        {
            yield return new WaitForSeconds(8f);
            var holder = FindObjectsByType<PlayerCharacterHolder>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);
            if (holder == null)
            {
                Debug.Log("[CreatureTest] FAIL - no locally owned character for gallery");
                yield break;
            }

            var definitions = MonsterLibrary.All.Values.OrderBy(m => m.Id).ToArray();
            var gallery = new List<(MonsterDefinition definition, Transform root)>();
            Vector3 forward = holder.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            for (int i = 0; i < definitions.Length; i++)
            {
                var def = definitions[i];
                var view = new UnitView
                {
                    Id = $"gallery_{def.Id}", Name = def.Name,
                    IsPc = false, MaxHp = 10, Hp = 10
                };
                Transform root = ResolveVisual(view);
                int row = i / 4;
                int column = i % 4;
                int rowCount = Mathf.Min(4, definitions.Length - row * 4);
                float x = (column - (rowCount - 1) * 0.5f) * 2.25f;
                float z = 4.0f + row * 2.65f;
                root.position = holder.transform.position + right * x + forward * z;
                Vector3 face = holder.transform.position - root.position;
                face.y = 0f;
                if (face.sqrMagnitude > 0.01f) root.rotation = Quaternion.LookRotation(face);
                gallery.Add((def, root));
            }

            // Let skinned bounds, Animator defaults, and runtime materials settle.
            yield return null;
            yield return null;

            int mapped = 0;
            int visible = 0;
            foreach (var entry in gallery)
            {
                bool hasMapping = MonsterModels.ContainsKey(entry.definition.Id);
                if (hasMapping) mapped++;
                bool healthy = CharacterVisuals.TryGetVisibleCharacterBounds(
                    entry.root, out var bounds, out string issue);
                if (healthy) visible++;
                Vector3 size = bounds.size;
                string detail = healthy
                    ? $"visible {size.x:0.00}x{size.y:0.00}x{size.z:0.00}m" : issue;
                Debug.Log($"[CreatureTest] {(hasMapping && healthy ? "PASS" : "FAIL")} " +
                          $"{entry.definition.Id}: " +
                          $"{(hasMapping ? "mapped" : "NO MAPPING")}, " +
                          detail);
            }

            bool pass = mapped == definitions.Length && visible == definitions.Length;
            Debug.Log($"[CreatureTest] {(pass ? "PASS" : "FAIL")} - " +
                      $"all creature visuals {visible}/{definitions.Length} visible, " +
                      $"mappings {mapped}/{definitions.Length}, no capsule fallbacks");

            if (!string.IsNullOrEmpty(capturePath))
            {
                string directory = Path.GetDirectoryName(capturePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                yield return new WaitForSeconds(2f);
                ScreenCapture.CaptureScreenshot(capturePath);
                yield return new WaitForSeconds(2f);
                Debug.Log($"[CreatureCapture] wrote complete gallery to {capturePath}");
            }

            foreach (var entry in gallery)
                if (entry.root != null) Destroy(entry.root.gameObject);
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
        public bool OutcomeOpen { get; private set; }

        public void DismissOutcome()
        {
            OutcomeOpen = false;
            BannerUntil = 0f;
        }

        [ObserversRpc]
        private void RpcDismissOutcome() => DismissOutcome();

        [ObserversRpc]
        private void RpcCombatEnded(bool victory, int xpEach, string lootSummary)
        {
            BannerVictory = victory;
            BannerTitle = victory ? "VICTORY!" : "DEFEATED";
            BannerDetail = victory
                ? $"+{xpEach} XP each" + (lootSummary.Length > 0 ? $"\nLoot: {lootSummary}" : "")
                : "The party is carried back to Havenrock,\nbruised but alive. The block remains hostile.";
            BannerUntil = Time.time + 6f;
            OutcomeOpen = true;
            if (CombatFx.Instance != null) CombatFx.Instance.CombatEnded(victory);

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
                if (victory) CharacterVisuals.Trigger(u.Visual, "Victory");
            }
            if (_overlay != null) Destroy(_overlay);
            if (_gridMaterial != null) Destroy(_gridMaterial);
            ClientUnits.Clear();
        }

        private void BuildOverlay()
        {
            if (_overlay != null) Destroy(_overlay);
            if (_gridMaterial != null) Destroy(_gridMaterial);
            _overlay = new GameObject("GridOverlay");
            // Resources material: shader guaranteed in build (Shader.Find gets stripped).
            var source = Resources.Load<Material>("Fx/M_GridOverlay");
            if (source != null) _gridMaterial = new Material(source);
            else
            {
                var quadTemp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _gridMaterial = new Material(quadTemp.GetComponent<Renderer>().sharedMaterial);
                Destroy(quadTemp);
            }
            RuntimeArt.Tint(_gridMaterial, new Color(0.22f, 0.38f, 0.43f, 0.34f));

            // Preserve the battlefield art: restrained lines communicate cells while
            // move range and hover overlays carry the stronger interaction colors.
            float edge = 8.5f * CellSize;
            for (int i = -8; i <= 9; i++)
            {
                float p = (i - 0.5f) * CellSize;
                GridLine($"GridX_{i}", GridOrigin + new Vector3(p, 0.045f, -edge),
                    GridOrigin + new Vector3(p, 0.045f, edge));
                GridLine($"GridY_{i}", GridOrigin + new Vector3(-edge, 0.045f, p),
                    GridOrigin + new Vector3(edge, 0.045f, p));
            }
        }

        private void GridLine(string name, Vector3 from, Vector3 to)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_overlay.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = 0.035f;
            line.endWidth = 0.035f;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = _gridMaterial;
        }
    }
}
