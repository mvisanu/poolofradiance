# Build Prompt — "Radiant Pool" : 3D Co-op Party CRPG (Pool of Radiance modernized)

> Paste into Claude Code as the kickoff prompt. Read the IP and scope notes at the bottom before sending — they are the two things that will sink this project if ignored.

GOAL: Build a 3D online co-op party-based CRPG inspired by Pool of Radiance's structure — create a party of adventurers, take quests from a hub town, clear surrounding zones of monsters block-by-block, level up, uncover the main villain — rendered in a modern stylized-3D third-person world (World of Warcraft-like art direction and camera), where 2–4 friends can play the campaign together online.

SUCCESS CRITERIA:

- Two players on different machines can join the same session, each controlling their own character, and complete quest #1 (clear the first zone) together end-to-end.
- Turn-based tactical combat on encounter start (Pool of Radiance style), free real-time movement while exploring — transitions cleanly in multiplayer.
- Character sheet depth: 6 ability scores, classes, levels 1–5 minimum, spell slots, AC/HP/saving throws — implemented from the SRD 5.1 ruleset (see IP note).
- Runs at 60fps on a mid-range gaming PC; server-authoritative enough that basic state (HP, position, loot) can't be trivially cheated.
- A stranger can install the client, host or join via invite code, and play without reading a manual.

USERS: Small co-op groups (2–4 players per session), not an MMO (see scope note). Player-hosted or single dedicated server per group. Steam-style PC gamers, non-technical. Simple account = display name + invite code; no payments, no persistence across servers beyond local save files owned by the host.

CORE WORKFLOWS (ranked):

1. HOST & JOIN: Player A hosts a campaign → gets invite code → Players B–D join → each creates a character (race/class/abilities/appearance) → party spawns in the hub town together. All movement, chat, and state visibly synced.
2. EXPLORE & FIGHT: Party walks the 3D world in real time (WASD + mouse camera, WoW- style) → touching an enemy group triggers turn-based tactical combat on a grid overlay → initiative order, move/action/bonus-action economy, attack rolls, spells, death saves → victory yields XP + loot rolled from tables.
3. QUEST LOOP: Town council NPC issues zone-clearing quests → quest journal tracks objectives per player → clearing a zone permanently pacifies it (Pool of Radiance's signature block-clearing) → turn in for gold/XP/story beat → next zone unlocks.
4. PROGRESSION: Level-up screen (HP, spell slots, class features per SRD), inventory and equipment with stat effects, town vendors (buy/sell), rest system (short/long) that restores resources and can trigger random encounters.
5. (nice-to-have — cut first) Voice barks/ambient audio, weather/day-night, a second playable region, mounts, in-game text chat bubbles vs. plain chat box.

STACK: Propose in Phase 1 and justify, but here is my default — argue if you disagree:

- Engine: Unity 6 (C#) with URP + stylized shaders for the WoW-like look; huge free/ cheap asset ecosystem (Synty-style low-poly packs) so we are NOT hand-modeling a world.
- Networking: FishNet (free, server-authoritative, well-documented) with Unity Relay/Steam transport for NAT punch-through via invite codes.
- Rules engine: pure-C# class library implementing SRD 5.1 mechanics, unit-tested independently of Unity (dice, combat math, spell effects as data).
- Content as data: zones, quests, monsters, items, dialogue in JSON/ScriptableObjects so content grows without code changes.
- Server: same Unity build headless-hostable; host-migration OUT of scope v1.

CONSTRAINTS:

- IP — non-negotiable: use ONLY SRD 5.1 rules content (Creative Commons). NO D&D trademarks, no "Dungeons & Dragons" name, no Forgotten Realms, no "Phlan", no "Tyranthraxus", no beholders/mind flayers/other Product Identity monsters. Original town name, villain, and lore that mirror the STRUCTURE of Pool of Radiance, not its content. Flag anything borderline in Phase 1.
- Scope — non-negotiable: this is a CO-OP CRPG (Baldur's Gate 3 / Solasta model), NOT an MMO. No open persistent world, no thousands of concurrent players, no economy/auction house. "Like WoW" refers to art style and camera ONLY.
- Solo developer + AI pace: every phase must produce something playable. Prefer purchased/free art assets over custom art everywhere.
- Windows PC target first; Steam distribution assumed eventually but not built now.

DATA: Game content (zones/quests/monsters/items) as versioned JSON in-repo; campaign save state as local files on the host; player accounts = displayname + GUID only, no PII, no passwords v1 (invite-code security model). Playtest telemetry OUT of scope.

PROCESS — do these as separate checkpoints, wait for my OK between each: PHASE 1 — DESIGN: Architecture doc — engine/networking justification, client/server authority split (what the server owns vs. client-predicted), SRD rules-library API, combat state machine (explore ↔ turn-based transition, in multiplayer), content data schemas, zone/quest structure for the first region (hub + 3 zones), asset acquisition plan with actual pack names and costs, IP-risk list, and the top 5 technical risks with mitigations. No code. PHASE 2 — SKELETON: One character walks around one gray-boxed zone in third person; a second client joins via invite code and both see each other move. The vertical slice of networking — this phase kills the project or proves it. PHASE 3 — BUILD, one workflow at a time, playable after each: 3a. Rules library: character model, dice, attacks, AC/HP, 10 SRD spells — unit tests. 3b. Turn-based combat: grid overlay, initiative, one monster type, synced for 2 players. 3c. Character creation + level-up (levels 1–5, 4 classes: fighter/wizard/cleric/rogue). 3d. Quest loop: hub town, quest-giver NPC, first clearable zone, journal, rewards. 3e. Inventory/equipment/vendors/loot tables + rest system. 3f. Art pass: swap gray-box for stylized asset packs, lighting, UI theme. PHASE 4 — HARDEN: Disconnect/rejoin handling, save/load mid-campaign, server-side validation of all combat actions, error surfacing (never silent desync), settings menu (audio/video/keybinds), installer/build pipeline, README + hosting guide, known-limitations list.

DELIVERABLE PER PHASE: Phase 1: ARCHITECTURE.md + CONTENT-PLAN.md + IP-CHECKLIST.md — I review before code. Phase 2: Runnable build, two-machine join test script. Phase 3: One commit per sub-step (3a–3f), each launchable, each with a 3-line playtest checklist ("host, join, do X, expect Y"). Phase 4: Distributable build + docs.

---

## READ BEFORE SENDING — two honest warnings

1. **The MMO word.** A WoW-scale MMO is a 100+ engineer, multi-year project. What IS achievable solo-with-AI is what this prompt scopes: a 2–4 player co-op CRPG with WoW-style visuals — the Baldur's Gate 3 model, not the WoW model. If you truly want persistent-world MMO features, that's a different (much longer) conversation.

2. **The D&D word.** Pool of Radiance's rules heritage is legally usable via the SRD 5.1 (Creative Commons), but its names, setting, and signature monsters are Wizards of the Coast property. This prompt clones the game's *structure* — hub town, zone clearing, turn-based party combat — with original naming. Keep it that way if this ever ships.