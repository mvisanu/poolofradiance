# Design & Build an Application — Pool of Radiance Modernized (3D Co-op CRPG)

GOAL: Build a 3D online co-op party-based CRPG for small groups of friends (2–4 players)
that recreates Pool of Radiance's core loop — create a party, take quests from a hub
town, clear surrounding zones block-by-block, level up, defeat the villain — with
modern stylized third-person 3D graphics and camera in the style of World of Warcraft.

SUCCESS CRITERIA:
  - Two players on separate machines join one session via invite code, each controls
    their own character, and complete the first zone-clearing quest together.
  - Real-time exploration (WASD + WoW-style mouse camera) transitions cleanly into
    turn-based tactical grid combat when enemies are engaged — synced in multiplayer.
  - Full SRD 5.1 character depth: 6 ability scores, 4 classes (fighter/wizard/cleric/
    rogue), levels 1–5, spell slots, AC/HP, saving throws, death saves.
  - 60fps on a mid-range gaming PC; server-authoritative HP/position/loot so basic
    state can't be trivially cheated.
  - A non-technical friend can install, join via code, and play with no manual.
  - Zero Wizards of the Coast intellectual property in names, setting, or monsters —
    SRD 5.1 rules only (Creative Commons), original town/villain/lore.

USERS: 2–4 players per session, PC gamers, non-technical. Player-hosted sessions with
invite codes (no accounts, no passwords, no payments v1). This is a co-op CRPG like
Baldur's Gate 3 / Solasta — explicitly NOT an MMO; "like WoW" means art style and
camera only.

CORE WORKFLOWS (ranked):
  1. HOST & JOIN: Player A hosts → gets invite code → B–D join → each creates a
     character (race/class/abilities/appearance) → party spawns together in the hub
     town with movement, chat, and world state visibly synced.
  2. EXPLORE & FIGHT: Party roams the 3D world in real time → contact with an enemy
     group triggers turn-based combat on a grid overlay → initiative order, move/
     action/bonus-action economy, attack rolls, spells → victory yields XP and loot
     from tables.
  3. QUEST LOOP: Town council NPC issues zone-clearing quests → journal tracks
     objectives → a cleared zone stays permanently pacified (the Pool of Radiance
     signature) → turn-in grants gold/XP/story beat → next zone unlocks.
  4. PROGRESSION: Level-up screen per SRD rules, inventory/equipment with stat
     effects, town vendors, short/long rest system with random-encounter risk.
  5. (nice-to-have — cut first if scope grows) Snippets of ambience: day/night,
     weather, voice barks, mounts, a second region, chat bubbles.

STACK: My default — challenge it in Phase 1 with justification, don't silently
substitute:
  - Unity 6 (C#), URP with stylized shaders; purchased/free low-poly asset packs
    (Synty-style) for the WoW look — no custom art production.
  - Networking: FishNet (server-authoritative) + Unity Relay or Steam transport for
    invite-code NAT traversal.
  - Rules engine: pure-C# SRD 5.1 library (dice, combat math, spell effects as data),
    unit-tested independently of Unity.
  - Content as data: zones, quests, monsters, items, dialogue in JSON/ScriptableObjects
    so content grows without code changes.
  - Host = headless-capable same build; host migration out of scope v1.

CONSTRAINTS:
  - IP: SRD 5.1 (Creative Commons) rules content ONLY. No "Dungeons & Dragons" name,
    no Forgotten Realms, Phlan, Tyranthraxus, beholders, mind flayers, or any WotC
    Product Identity. Flag anything borderline in Phase 1.
  - Scope: co-op sessions only — no persistent open world, no thousands of players,
    no economy/auction house, no mobile.
  - Solo developer + AI pace: every phase ends playable; prefer asset packs over
    custom art everywhere.
  - Target: Windows PC first; Steam distribution assumed later, not built now.

DATA: Game content as versioned JSON in-repo; campaign saves as local files owned by
the host; player identity = display name + GUID only (no PII, no credentials);
no telemetry v1.

PROCESS — do these as separate checkpoints, wait for my OK between each:
  PHASE 1 — DESIGN: Architecture doc — engine/networking justification, client/server
    authority split (server-owned vs client-predicted), SRD rules-library API, combat
    state machine including the explore↔turn-based transition under multiplayer,
    content data schemas, first region layout (hub + 3 zones), asset-pack shopping
    list with names and costs, IP-risk checklist, top 5 technical risks with
    mitigations. No code yet.
  PHASE 2 — SKELETON: One character walking a gray-boxed zone in third person; a
    second client joins via invite code and both see each other move. This is the
    networking vertical slice — it proves or kills the project before any content
    exists. Runnable.
  PHASE 3 — BUILD: Remaining workflows one at a time, playable after each:
    3a. Rules library — character model, dice, attacks, AC/HP, 10 SRD spells, unit tests.
    3b. Turn-based grid combat, one monster type, synced for 2 players.
    3c. Character creation + level-up, levels 1–5, four classes.
    3d. Quest loop — hub town, quest-giver, first clearable zone, journal, rewards.
    3e. Inventory, equipment, vendors, loot tables, rest system.
    3f. Art pass — swap gray-box for stylized packs, lighting, UI theme.
  PHASE 4 — HARDEN: Disconnect/rejoin, save/load mid-campaign, server-side validation
    of all combat actions, error surfacing (never silent desync), settings menu
    (audio/video/keybinds), build pipeline/installer, README + hosting guide.

DELIVERABLE PER PHASE:
  PHASE 1: ARCHITECTURE.md + CONTENT-PLAN.md + IP-CHECKLIST.md — reviewed before code.
  PHASE 2: Runnable build + a two-machine join test script.
  PHASE 3: One commit per sub-step (3a–3f), each launchable, each with a 3-line
    playtest checklist ("host, join, do X, expect Y").
  PHASE 4: Distributable build, docs, known-limitations list.