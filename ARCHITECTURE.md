# ARCHITECTURE.md — Radiant Pool

3D online co-op party CRPG (2–4 players), Pool of Radiance *structure* with original IP,
WoW-style stylized visuals and camera, SRD 5.1 rules. This document is the Phase 1
design deliverable: stack justification, authority split, rules-library API, combat
state machine, data schemas, and top technical risks.

Working title: **Radiant Pool**. Setting names are original — see IP-CHECKLIST.md.

---

## 1. Engine & networking choice

### Engine: Unity 6 (6000.x LTS), C#, URP — **accepted, with justification**

| Option | Verdict | Why |
|---|---|---|
| **Unity 6 + URP** | ✅ chosen | Largest stylized low-poly asset ecosystem (Synty et al. target Unity first), C# end-to-end lets the rules library be shared verbatim between server and client, URP hits 60fps easily on mid-range PCs with this art style, headless server builds are first-class (`-batchmode -nographics` / dedicated server build target). |
| Unreal 5 | ❌ | Superior renderer we don't need (stylized low-poly doesn't exercise Nanite/Lumen), C++ + Blueprint splits the codebase from a pure-C# rules library, asset packs for this exact art style are thinner, heavier for a solo dev. |
| Godot 4 | ❌ | C# support is workable but the high-level multiplayer stack is younger, no equivalent of FishNet's client-side prediction maturity, far fewer ready-made stylized fantasy packs. Would add risk to the one phase (networking) that can kill the project. |

### Networking: FishNet + Unity Relay/Lobby (Steam transport later) — **accepted**

| Option | Verdict | Why |
|---|---|---|
| **FishNet** | ✅ chosen | Free/MIT-licensed, server-authoritative by design, built-in client-side prediction + reconciliation for movement, `SyncVar`/`SyncList`/RPC model maps directly onto our state, active development, works with Unity Transport → **Unity Relay** for NAT punch-through via join codes (exactly our invite-code model). |
| Unity Netcode for GameObjects | ❌ | Viable, but weaker prediction story and historically slower iteration; FishNet is a strict superset for our needs at the same price (free). |
| Mirror | ❌ | Mature but prediction/reconciliation is DIY; FishNet is its spiritual successor with that built in. |
| Photon Fusion | ❌ | Excellent tech but CCU-based pricing and vendor lock-in for a free co-op game; Relay's free tier fits 2–4 player sessions. |

**Topology:** listen server (host player's client is the server) for v1. The same build is
headless-hostable (`-server` flag) for groups that want a dedicated box. Host migration is
**out of scope v1** (host quits ⇒ session ends; campaign save lives on host disk).

**Invite codes:** Unity Lobby/Relay allocation returns a 6-char join code — that *is* the
invite code, no custom backend. Fallback/roadmap: Facepunch Steamworks transport once we're
on Steam. Identity = display name + locally generated GUID; no accounts, no PII.

---

## 2. Client/server authority split

Rule of thumb: **anything that touches rules, resources, or randomness is server-owned.
Anything cosmetic or latency-sensitive-but-verifiable is client-predicted.**

### Server-authoritative (never trusted from client)
- All dice rolls (attack, damage, saves, initiative, loot tables, random encounters). Seeded RNG lives server-side only.
- HP, death saves, conditions, spell slots, class resources.
- Inventory, equipment, gold, loot drops, vendor transactions.
- Position as ground truth: explore-mode movement is client-predicted but server-simulated and reconciled (FishNet prediction); combat-mode movement is **not predicted at all** — clients submit "move to cell", server validates and broadcasts.
- Combat state machine: encounter triggers, initiative order, whose turn it is, action-economy budget (action/bonus/move), legality of every submitted action.
- Quest state, zone cleared/pacified flags, NPC state, world doors/levers.
- Save/load (host disk).

### Client-predicted / client-owned
- Own-character explore movement + jump (predicted, server-reconciled).
- Camera (WoW-style orbit; never networked).
- Animation triggers, VFX, SFX, floating damage numbers (driven by server events, rendered locally).
- UI state (menus, journal open, targeting cursor). Targeting sends an *intent*; server re-validates range/LoS.

### Anti-cheat bar (per success criteria: "basic state can't be trivially cheated")
- Client messages are **intents only**: `MoveIntent(cell)`, `AttackIntent(targetId, weaponId)`, `CastIntent(spellId, slotLevel, target)`. Server validates against the rules library and rejects illegal ones with a user-visible error (never silent).
- Explore movement server-checked for speed/teleport bounds each tick.
- No client ever receives another player's RNG or the loot table pre-roll.
- Out of scope v1: memory-editing hosts (the host owns the server; friends-only trust model), packet encryption beyond transport defaults.

---

## 3. SRD rules library — `RadiantPool.Rules`

Pure C# (netstandard2.1) class library. **Zero Unity references.** Deterministic: every
public entry point takes an injected `IRng`. Unit-tested with xUnit, runs in CI without Unity.

```
RadiantPool.Rules/
  Dice.cs            // Roll("2d6+3", rng) → RollResult { Total, Dice[], Modifier }
  Abilities.cs       // Str/Dex/Con/Int/Wis/Cha, modifier = (score-10)/2 floor
  CharacterSheet.cs  // race, class, level 1..20, abilities, prof bonus, AC, HP, speed,
                     // saving-throw/skill proficiencies, spell slots, known/prepared spells
  Classes/           // Fighter, Wizard, Cleric, Rogue — level tables 1–20 as data
  CombatMath.cs      // ResolveAttack(attacker, target, attack) → AttackResult
                     //   (adv/disadv, crit on nat 20, auto-miss nat 1)
                     // ResolveSave(target, dc, ability) → SaveResult
                     // ApplyDamage(target, amount, type) → DamageResult (resist/immune hooks)
  Spells/            // SpellDefinition loaded from JSON: targeting, save/attack, effect ops
                     //   effect ops = small vocabulary: Damage, Heal, ApplyCondition,
                     //   GrantTempHp, ModifyAC … interpreted, not hard-coded per spell
  Conditions.cs      // prone, poisoned, unconscious, dying (death saves) …
  CombatFlow.cs      // battle states, targeting, result evaluation, serial action queue,
                     // controlled impact timeline + missing-event fallback
  TurnEngine.cs      // initiative roll + order, per-turn budget {Move, Action, Bonus},
                     //   ValidateAction(state, intent) → Ok | RuleViolation(reason)
                     //   ExecuteAction(state, intent, rng) → ActionEvents[]
  Progression.cs     // XP thresholds L1–20, level-up deltas (HP, slots, features)
  Loot.cs            // roll on LootTable JSON → item instances + gold
```

Quest combat scales from the strongest connected human hero. Runtime monsters are one
level below that hero (level 1 floor), while their canonical SRD definitions remain
unchanged. `Difficulty.cs` applies the small level-based HP/stat adjustments at spawn and
attack resolution. Required fights can reveal a level-matched challenge cache, and quest
turn-ins resolve their equipment tier from the current hero level; when that tier contains
a class-legal improvement, the server guarantees one upgrade before adding random rolls.

Key contract: a client sends only an intent. `CombatManager` validates it through the pure
rules services, serializes it through `CombatActionQueue`, broadcasts wind-up presentation,
applies `CombatMath`/`SpellEngine` at the configured impact time, then broadcasts impact and
the exact HP/resource snapshot. The explicit `BattleState` locks duplicate input throughout
resolution. One rules path, no client-side math to drift; a controlled timeline fallback
prevents a missing animation event from stalling the turn.

10 SRD spells for 3a: Fire Bolt, Sacred Flame, Magic Missile, Burning Hands, Cure Wounds,
Healing Word, Bless, Shield, Sleep, Guiding Bolt.

The level-20 expansion is split into a base catalog and `content/campaign/level20_expansion.json`.
Together they define 39 playable zones. Four original three-stage arcs progress through
Duskmire/Stormglass, Frostvein, the Titan's Chain, and the Hollow Star finale. Runtime site
plans use the same reward authority as JSON, old completed saves unlock the appended graph on
load, and `CampaignSimulationTests` proves the required two-player path reaches level 20.

---

## 4. Combat state machine (multiplayer explore ↔ turn-based)

Server-owned FSM per **party** (one party per session in v1):

```
            all players spawned                enemy group's trigger volume
   Lobby ──────────────────────▶ EXPLORING ──────────────────────────────┐
                                     ▲                                    ▼
                                     │                              COMBAT_SETUP
                                     │        victory                • freeze all movement (server flips mode flag)
   COMBAT_END ◀──────────────── TURN_LOOP ◀──────────────────────────• overlay grid (5-ft cells) on nav area
   • XP + loot events            • active unit = initiative[i]       • place ALL party members onto nearest
   • zone-clear progress         • player unit → wait for intents      valid cells around trigger point
   • defeat → party-wipe flow      (turn timer, default 60s,           (distant members are pulled in —
   • despawn grid                   then auto-Dodge + end turn)         Solasta model, see decision below)
                                 • monster unit → server AI decides  • place enemy group on its cells
                                 • every intent: Validate → Execute  • roll initiative (server)
                                   → broadcast ActionEvents          • broadcast full combat snapshot
```

Decisions that make this clean in multiplayer:

1. **The whole party enters combat** when any member triggers it (teleport-to-edge for
   stragglers). No mixed real-time/turn-based world. This is the single biggest
   complexity-killer and matches Solasta/BG3 party play at our scale.
2. **Clients never run combat logic.** They render the last snapshot + event stream. A
   late-joiner or lag spike just requests the current snapshot (combat is tiny state).
3. **Turn timer** so one AFK player can't lock 3 friends (auto-Dodge, configurable, off
   for solo).
4. Transition is a **server-driven mode flag** replicated to all clients; clients swap
   controller (character-motor ⇄ grid-cursor) and camera rig (orbit ⇄ tactical tilt) on
   the flag. No client may unfreeze itself.
5. Escape/flee: party can attempt to disengage via an action; v1 simple rule (all units
   reach grid edge). Defeat = respawn party at hub with gold penalty (no permadeath v1).

---

## 5. Content data schemas (versioned JSON in `/content`)

All content is data; code ships systems only. Every file has `"schemaVersion": 1`.

```jsonc
// content/monsters/marsh_skulker.json  (original monster using SRD stat math)
{ "schemaVersion": 1, "id": "marsh_skulker", "name": "Marsh Skulker",
  "size": "Small", "ac": 12, "hp": { "dice": "2d6" }, "speed": 30,
  "abilities": { "str": 8, "dex": 14, "con": 10, "int": 8, "wis": 10, "cha": 6 },
  "actions": [ { "type": "melee_attack", "name": "Rusty Blade",
                 "toHit": 4, "damage": "1d6+2", "damageType": "slashing" } ],
  "xp": 50, "ai": "skirmisher", "lootTable": "lt_skulker" }

// content/zones/old_docks.json
{ "schemaVersion": 1, "id": "old_docks", "name": "The Old Docks",
  "scene": "Zone_OldDocks", "levelBand": [1, 2],
  "encounters": [ { "id": "enc_docks_01", "trigger": "vol_docks_01",
                    "units": ["marsh_skulker", "marsh_skulker", "marsh_skulker"],
                    "requiredForClear": true } ],
  "clearQuest": "q_clear_docks", "onCleared": { "pacify": true, "unlocks": "drowned_market" } }

// content/quests/q_clear_docks.json
{ "schemaVersion": 1, "id": "q_clear_docks", "name": "Retake the Old Docks",
  "giver": "npc_council_veresk", "type": "clear_zone", "zone": "old_docks",
  "objectives": [ { "id": "o1", "text": "Defeat the squatters in the Old Docks",
                    "counter": "encounters_cleared", "target": 3 } ],
  "rewards": { "xpEach": 300, "gold": 100, "storyBeat": "sb_docks_cleared" },
  "unlocks": ["q_clear_market"] }

// content/items/shortsword.json
{ "schemaVersion": 1, "id": "shortsword", "name": "Shortsword", "slot": "mainhand",
  "weapon": { "damage": "1d6", "damageType": "piercing", "properties": ["finesse", "light"] },
  "cost": 1000, "weight": 2 }   // cost in copper

// content/loot/lt_skulker.json
{ "schemaVersion": 1, "id": "lt_skulker",
  "gold": "2d6", "rolls": 1,
  "entries": [ { "weight": 70, "item": null }, { "weight": 25, "item": "dagger" },
               { "weight": 5, "item": "potion_healing" } ] }
```

Spells, classes, dialogue follow the same pattern (`content/spells`, `content/classes`,
`content/dialogue`). Unity loads JSON at boot into immutable registries; the rules library
consumes the same files in unit tests. Content changes never require code changes.

**Save file** (host-owned, `%USERPROFILE%\Saved Games\RadiantPool\<campaign>.json`):
campaign seed, quest states, cleared zones, player sheets/XP, party gold and stash, plus a
named companion roster containing each hire's sheet, individual loadout, and active/released
state. Active companions return on load; released companions remain available for rehire.
World flags share the same versioned file with migration on load.

---

## 6. Repo layout

```
/ARCHITECTURE.md /CONTENT-PLAN.md /IP-CHECKLIST.md
/rules/            RadiantPool.Rules.sln  (pure C# + xUnit tests — no Unity)
/content/          versioned JSON (single source of truth, consumed by both)
/game/             Unity 6 project (URP). Assets/, Packages/, ProjectSettings/
/docs/             playtest checklists, hosting guide (Phase 4)
```

---

## 7. Top 5 technical risks & mitigations

1. **Explore↔combat transition desyncs in multiplayer** (the project-killer).
   *Mitigation:* server-only FSM + snapshot/event replication (§4); Phase 2 proves raw
   movement sync, 3b proves the transition with 2 real clients before any content exists;
   "never silent" rule — clients that detect a state they can't apply request a full
   snapshot and log loudly.
2. **NAT traversal / invite-code joining fails for real users.**
   *Mitigation:* Unity Relay does the punch-through (its whole job); free tier covers
   2–4 player sessions comfortably; Steam transport as the shipped fallback; Phase 2's
   two-machine test is the gate.
3. **Solo-dev scope explosion.**
   *Mitigation:* content-as-data so world growth is JSON not code; ranked workflow list
   with an explicit cut line (workflow 5); every phase ends playable so the project can
   ship from any checkpoint; asset packs, never custom art.
4. **Version churn: Unity 6 / URP / FishNet / Relay SDK compatibility.**
   *Mitigation:* pin Unity 6 LTS + exact FishNet release in manifest; upgrade only at
   phase boundaries; keep the transport behind a thin interface (Relay ⇄ Steam swap).
5. **60fps miss on mid-range PCs.**
   *Mitigation:* low-poly stylized art is cheap by construction; URP with baked/mixed
   lighting, one realtime shadow-casting light budget outdoors; frame budget checked at
   3f (art pass) with the Unity Profiler on a fixed test scene; SRP Batcher + GPU
   instancing on by default.

(Risk 6, acknowledged: SRD rules edge cases ballooning — held down by the effect-ops
vocabulary in §3 and the 10-spell cap for v1.)
