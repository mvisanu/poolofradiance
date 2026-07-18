# PROMPT — Build "Radiant Pool" in Unreal Engine 5

You are building a complete, shippable game in **Unreal Engine 5 (latest LTS-quality
release, C++)**. This prompt is the full specification, distilled from a finished Unity
implementation of the same game. Follow it as the source of truth; where it names a
Unity mechanism, build the Unreal-native equivalent described alongside it.

---

## 1. What the game is

A 3D online co-op party CRPG for 2–4 friends that recreates the classic *Pool of
Radiance* core loop with original IP: create a party, take commissions from a hub-town
council, clear the surrounding quarters block-by-block, level 1→20, defeat the villain.
Real-time exploration (WASD + WoW-style orbit camera) transitions into turn-based
tactical grid combat when enemies are engaged — fully synced in multiplayer.

- **Rules**: SRD 5.1 (Creative Commons) only. Zero Wizards of the Coast Product
  Identity — no D&D name, no Forgotten Realms, no WotC monsters/places/characters.
  Maintain a banned-term list and an automated scan gate over all content and code.
- **Art direction**: stylized painted-fantasy in the *style* of World of Warcraft
  (art style and camera only — this is NOT an MMO): vivid saturated grass, bright
  golden near-white sun, azure sky, luminous haze, punchy warm color grade, gentle
  bloom. No Blizzard assets or names.
- **Scale**: co-op sessions only. No open persistent world, no accounts, no economy
  backend, no mobile. Windows PC first; a browser/pixel-stream edition is a
  nice-to-have (see §12).

### Success criteria
1. Two players on separate machines join one session via an invite code, each controls
   their own character, and complete the first zone-clearing quest together.
2. Explore ⇄ turn-based grid combat transition is clean and synced.
3. Full SRD 5.1 progression: 6 ability scores, 4 classes (Fighter/Wizard/Cleric/Rogue),
   levels 1–20, proficiency/XP/spell-slot tables, AC/HP, saving throws, death saves,
   cantrip scaling, fighter extra attacks (2/3/4 swings at levels 5/11/20).
4. A complete authored path reaches level 20 through **39 playable zones / 37 quests /
   37 monster families**, including four original three-stage high-level arcs
   (Stormglass, Frostbound, Titan's Chain, Hollow Star) and a three-boss finale.
5. 60 fps on a mid-range gaming PC; server-authoritative HP/position/loot.
6. A non-technical friend installs, joins via code, and plays with no manual.

---

## 2. Engine & networking (Unreal mapping)

- **Unreal 5, C++ core + minimal Blueprint glue.** Stylized low-poly art does not need
  Nanite/Lumen — prefer a lightweight forward/stylized setup tuned for 60 fps.
- **Topology: listen server** (host player's process is the server), same build
  headless-hostable with `-server`. Host migration out of scope; host quits ⇒ session
  ends, campaign save lives on host disk.
- **Invite codes / NAT traversal**: Epic Online Services (EOS) sessions + relay — the
  join code IS the invite code, no custom backend. Steam Sockets as the later
  fallback. Identity = display name + locally generated GUID. No accounts, no PII.
- **Replication model**: Unreal's built-in property replication + RPCs maps directly:
  - Client → Server: **intents only** (`Server_MoveTo(cell)`,
    `Server_Attack(targetId)`, `Server_Cast(spellId, slot, target)`), each validated
    against the rules library, illegal ones rejected with a **user-visible** error —
    never silent.
  - Server → Clients: replicated state + multicast result events
    (equivalent of SyncVar + ObserversRpc).
  - **Never let an exception escape a server RPC handler** — wrap all combat
    resolution in try/catch and surface the error; a crash mid-resolution must not
    kill or desync the session.
  - Companions are server-owned AI with **no owning connection** — guard every
    client-targeted RPC against them or you'll spam "not an observer"-class errors.

### Authority split (rule of thumb: rules, resources, randomness = server)
Server-owned: all dice rolls (seeded RNG lives server-side only), HP/conditions/slots,
inventory/gold/loot/vendors, combat FSM + initiative + action legality, quest and zone
state, saves. Client-predicted/owned: own-character explore movement (Character
Movement Component prediction is built in — use it), camera (never networked),
animation/VFX/SFX/damage numbers (driven by server events), UI state.

---

## 3. Rules library — a pure, engine-free module

Put ALL game math in a **standalone C++ module (`RulesCore`) with zero engine-type
dependencies in its public API** (plain structs, no UObjects), unit-tested via Unreal
Automation tests that run headless in CI. Deterministic: every entry point takes an
injected RNG interface. This mirrors the proven Unity design where the rules DLL was
shared verbatim and the **campaign simulation test plays the entire 39-zone campaign
headlessly** and enforces the complete level-20 XP curve and a two-player difficulty
floor — build that test early and run it after any balance change.

Contents: dice parser (`2d6+3`; render negative modifiers as `1d6-1`, never `1d6+-1`),
abilities/modifiers, character sheet, four class tables 1–20, attack/save/damage
resolution (adv/disadv, crit nat 20, auto-miss nat 1), conditions, spell definitions as
**data with a small effect-op vocabulary** (Damage, Heal, ApplyCondition, GrantTempHp,
ModifyAC …) interpreted rather than hard-coded per spell, initiative + per-turn budget
{Move, Action, Bonus}, `ValidateAction → Ok | RuleViolation(reason)`, XP thresholds,
loot tables.

### House rules (deliberate, playtested — keep them)
- **Difficulty knobs live in ONE file**: monsters spawn at **85% HP** and attack at
  **−1 to hit**; PCs stay pure SRD; XP untouched. Monster stat blocks stay canonical —
  retune the knobs, never the blocks. Pin the values with tests.
- **Ability points**: one point per level, **two at 4th** — every one of the twenty
  levels carries a build choice. The level-up UI must say what each ability buys THIS
  character (an odd score buys nothing until the next point completes the modifier).
- **Out-of-combat regen**: every 3 s the server heals living PCs+companions **2% of
  max HP (min 1)** afield, **6% (min 2)** within ~32 m of the hub's Council Hall;
  combat pauses it; the dead never trickle back. One server tick is the only caller.
- **Companion muster by ROLE, never class order**: a healer first, then damage dealers
  of two different classes, counting what the humans already play (nobody gets a
  second cleric while the party lacks a rogue). Companions auto-spend ability points
  on their class's primary ability.
- **Encounter variety without balance drift**: per-encounter seeded RNG (stable on
  retry within a session) scatters monsters across the enemy half of the board and
  swaps each authored "mook" for a same-XP, same-theme alternative from fixed
  substitution pools. **Every pool shares one XP value — enforce with a test** so kill
  XP and the pinned level curve never move. Named bosses never substitute.
- Victory revives dead PCs at 1 HP (no permadeath); party wipe revives all at the
  shrine with the retry option below.

---

## 4. Combat system

Server-owned FSM per party: `EXPLORING → COMBAT_SETUP → TURN_LOOP → COMBAT_END`.

- **The whole party enters combat** when any member triggers it (stragglers pulled to
  the grid edge — Solasta model). No mixed real-time/turn-based world.
- Clients never run combat logic — they render replicated snapshots + event streams.
- 5-ft grid overlay; initiative order panel; turn timer (auto-Dodge on expiry,
  off in solo).
- **One serial action queue** on the server: wind-up → impact → recovery timelines,
  paced AI turns, glide movement. Damage applies at the **impact** moment, not at
  button press. While a queued action resolves, player input is locked through
  wind-up, impact, HP sync, and recovery. Include a timeline fallback so a missing
  animation event can never stall the turn.
- **One-click attack UX (critical, playtested)**: in combat, clicking an enemy closes
  in AND attacks — one click, however far away. One function is THE single definition
  of a board click: in reach it swings; out of reach it paths, remembers the target,
  and lands the blow when the body settles. A completed weapon attack **auto-ends that
  player's turn** after impact + recovery. Click ground = move. Space = manual end
  turn. An Attack hotbar slot (hotkey A) opens the same legal-target path as a world
  click — both converge on that one function. Spells open only their legal target
  picker (Backspace cancels).
- **Combat-start camera assist (one-shot)**: at fight start, ease the orbit camera to
  a tactical pitch/zoom AND swing yaw to face the centroid of living enemies. Track
  the LIVE bearing while easing (units glide to their cells for the first second, so a
  bearing captured at combat start goes stale) and only complete once the bearing has
  held still ~0.35 s. Any camera input cancels the assist; it must never fight the
  player per-frame.
- **X-ray occlusion**: every combat unit gets a sight line; any environment mesh
  blocking one fades to a transparent copy (shadows off). Test renderer/mesh BOUNDS
  containment too, not just raycasts — it's what catches a monster spawned inside a
  building when encounter volumes overlap structures.
- Every living monster shows an exact `hp/max` bar overhead plus a generated
  shape-texture icon (triangle/square/circle/…) — never font glyphs.
- Victory/defeat are persistent modals; defeat offers a **server-validated retry** of
  the same encounter (same seed ⇒ same layout). Combat end must clear death poses and
  re-enable movement; a terminal result can race a resuming coroutine/timer — capture
  and null-check the battle context before advancing turns.

---

## 5. Content as data

Ship systems in code, world in **versioned JSON** (the existing `content/` tree can be
reused nearly verbatim): zones, quests, monsters, items, spells, loot tables, dialogue,
each with `schemaVersion`. Load at boot into immutable registries consumed by both the
game and the rules tests. Cross-reference validation tests keep JSON and code registries
aligned by id, and every monster carries an `srdRef` proving SRD ancestry.

- **Campaign**: 39 zones / 37 quests / 37 monsters; four-zone opening chain
  (docks → market → warcamp → temple) then hub-and-spoke expansion; a cleared zone
  stays permanently pacified (the signature mechanic); the normal required path
  reaches level 20 at the finale.
- **Loot gets better as the campaign deepens** — pin the gradient with tests
  (mid-campaign rapier/studded leather → vault greatsword/splint → warcamp
  greataxe/half plate). **Magic gear scales it to 20**: "+N" weapons/armour derived
  from a mundane `BaseId` (proficiency + model come from the base; the bonus lifts
  to-hit/damage or AC), caster robes as the wizard's only body defence, magical
  shields and a warding orb for the off hand, and two ring slots carrying
  AC/save/attack/damage riders. **Sum every worn source into one recompute** function
  so nothing double-counts. Quest turn-ins resolve equipment tier from current hero
  level and guarantee one class-legal upgrade before random rolls.
- Item icons: render each item's own 3D model to a texture at build time; armour
  without models is drawn procedurally from its KIND; "+N" variants fall back to the
  base item's icon.
- Selling: one function is THE definition of "a trader is near" — UI greys the button
  with it, the server RPC re-checks it with slack. Sell = half list price; sell-all
  keeps potions.

**Saves** (host-owned JSON): campaign seed, quest states, cleared zones, sheets/XP,
gold/stash, named-companion roster with loadouts and active/released state. Migrate on
load. **Never persist a counter that duplicates derived truth** — cleared-zone counts
are RE-DERIVED from the consumed-encounter list on every clear and on load, which
self-heals older broken saves. Order on load: recount → recheck unlock dead-ends → save.

---

## 6. Wayfinding (the player must always know what to do and where)

- Quest card top-left (collapsible to a title pill): active quest + `[x]/[ ]`
  checklist. Centre banner on updates. A gold steering arrow above the hotbar
  (camera-space: up = forward). Beyond ~26 m it names and aims at the target QUARTER;
  inside, it switches to the next fight. Lit district signs per quarter.
- Quest giver overhead marker: yellow `!` = commission available, gray `?` = active,
  yellow `?` = ready to turn in (priority), hidden otherwise. Billboarded, bobbed
  (respect Reduced Motion), derived from replicated quest state — never a duplicate
  counter.
- Fast travel ("Waystone Network"): the tracked quest's destination renders as a
  green-highlighted card + "Travel now"; a ready turn-in highlights nothing outbound
  (the target is Council Hall). Colour is always reinforced by words.
- Minimap top-right, three sizes (hidden → tactical → full atlas, hotkey M,
  persisted). The maximized view is a **world atlas, never a magnified tactical
  camera**: authored continent-style regions contain every destination; pins show
  open/done/locked; **exactly one gold X** marks the tracked destination; roads/sea
  lanes derive from zone prerequisites. Validate at startup that every region contains
  each configured zone exactly once. After the campaign ends, issue standing orders
  against any encounters still standing — never a questless state.

---

## 7. UI / HUD rules (hard-won — obey all of them)

Build the HUD in UMG/Slate with these invariants:

- **Logical canvas**: lay out against a fixed logical resolution (~630 units tall)
  scaled by BOTH height and width (a small window scales down, never crops), with a
  user scale multiplier in Settings. Long text sheds detail instead of overflowing.
- **One rect, one definition**: any screen region used by both rendering and
  click-blocking/hit-testing must come from a single shared property. Hand-copied
  duplicate geometry WILL drift and eat clicks.
- Panels (inventory/journal/settings) are mutually exclusive; Esc = back, then
  Settings. **An open panel owns the screen** — every other HUD element draws nothing
  while one is up. Guard single-letter hotkeys against text-field focus.
- Persistent bottom hotbar: **Attack is a first-class slot**, named spells get slots,
  the bar wraps combat actions above utilities instead of overflowing (a combat cleric
  needs 13 slots), and it stows to a "SHOW BAR (H)" handle that still blocks clicks.
  **Party gold lives on the hotbar permanently** (`1,234g` — always thousands-
  separated) so the purse visibly moves. **Your health rides above the bar**
  (bar + hp/max + %), synced from combat HP in a fight and sheet HP outside.
- **The combat log never owns the bottom edge**: it docks a fixed gap above the
  complete hotbar rect and is hidden entirely while an attack/spell target picker is
  open. Assert this geometry in an automated rendered-frame test.
- Character sheet: six ability scores first (score + modifier), then worn slots with
  per-piece stat lines and totals (AC breakdown, HP, attack, damage); stash items show
  a comparison vs equipped ("upgrade: +2 AC"). Level + XP live on the sheet and the
  level-up screen — never the main HUD.
- **No dingbat/arrow glyphs in UI text** — use ASCII, words, or generated textures.
  Tooltips are global state in immediate-mode-style UIs; gate any tooltip readout on
  the pointer actually being over YOUR panel. Icons on narrow buttons: draw the
  texture over the button, don't rely on content padding.
- Theme: one design system, one place to tune it. Academia palette — mahogany/oak
  panels, brass borders, parchment text fields; bright gold = active-state only;
  WCAG contrast ≥ 4.5:1. Type hierarchy from open-licensed fonts (display serif for
  titles, serif for controls, humanist sans for body). All styling flows through the
  theme module — never inline.

---

## 8. World look (the WoW-style target)

- Vivid painted grass (color lives in the ground texture, near-white material tint —
  never multiply two dark colors), warm golden sun (~48° elevation, slightly warm
  white, shadow strength ~0.75), azure sky, tri-tone ambient (sky blue / warm equator
  / dark warm ground).
- **Day haze is aerial perspective, not weather**: exponential fog, pale blue-white,
  low density (~0.0035 exponential-equivalent) so mid-ground stays crisp and only the
  far distance melts into the sky. Night runs denser and darker; combat thins it.
- Post: ACES-style tonemap, restrained warm bloom, +saturation and +contrast in a
  color grade, slight warm white balance, gentle vignette, film grain off.
- A **single runtime atmosphere controller owns all of this every frame** (day/night
  palettes, sun, fog, sky) — scene-baked lighting values are only the initial state.
  Put the palette numbers in one file.
- Dress the world from ASSET PACKS discovered by keyword buckets
  (tree/rock/house/grave/…) so any similar pack drops in; compose sites from buckets,
  never model names. Surround playable space with mountain-ring silhouettes and dense
  forest walls for enclosed WoW-like vistas. Themed sites get authored set-dressing
  grammars (e.g. a necropolis = walled perimeter, gate lane, mausoleum OUTSIDE the
  back wall as a backdrop — never between camera and combat, flanking statues,
  clustered graves). Decoration is collider-free and never replicated.
- The entire scene/level must be **regenerable from code** (an editor commandlet):
  scene files are disposable build artifacts, never hand-edited. Buildings are a
  collision box with the visual model parented inside.

---

## 9. Character & creature presentation

- Third-person orbit camera: WASD/middle-drag pan in combat, F recentres, scroll
  zoom; the camera is never networked.
- Humanoids retarget through one shared skeleton/AnimBP with semantic states
  (`Attack1H`, `Attack2H`, `AttackRanged`, `Cast`, `Death`); pick by weapon/damage
  words; align damage to the authored hit event with a timed fallback. Creature rigs
  fall back to their own `Attack` state. Death states must be driven so they cannot
  re-enter every frame (no self-transitions), and combat end must clear the death
  flag — revived characters otherwise walk in the death pose.
- **Every monster id must map to a real model** — a capsule/placeholder fallback is a
  logged bug, not a style. Missing beasts (bear, rat) were built as original geometry
  in headless Blender; keep that pipeline (script → FBX + preview PNG) so no license
  attaches.
- Teleporting a character with an active movement component: disable it, move,
  re-enable — a silent failed teleport looks exactly like a broken feature.

---

## 10. QA discipline (this is what made the Unity build shippable — replicate it)

- **Self-test flags over manual play**: the packaged game accepts flags
  (`-attacktest`, `-regentest`, `-combatflowtest`, `-selltest`, `-leveltest`,
  capture flags that screenshot specific UI states, `-savedir` to keep tests off real
  saves) that drive REAL gameplay through the same public entry points the mouse
  uses, assert in the log, and are gated by a smoke-test script that launches
  host+client and a dozen specialized instances, then greps their logs
  (60+ assertions). Unreal equivalents: automation specs + Gauntlet, plus the same
  in-game flag pattern for end-to-end paths.
- To cover a client-side path, pull it into one public method the test can call — the
  mouse and the self-test drive the very same code.
- **Never verify by injecting synthetic input into a live window** — it lands in
  whatever has focus. Screenshots fine; input no.
- Test/capture flags rarely self-quit: launch, sleep a bounded budget, kill, then
  read the copied log — never wait indefinitely.
- One instance per timing-sensitive test (a live fight under other tests fights them
  for the turn clock), and never run timed instances while a heavy build is pegging
  the CPU — budget starvation reads as fake failures.
- Rules unit tests + full-campaign simulation run in CI without the engine. Content
  validation + IP scan gate every build. Read the player log and the save file FIRST
  when a bug is reported — back up the save before running against it.

---

## 11. Process

Phased, every phase ends playable:
1. **DESIGN** — architecture doc, authority split, schemas, IP checklist, risk list.
2. **SKELETON** — networking vertical slice: two machines, invite code, both see each
   other move. This proves or kills the project before any content exists.
3. **BUILD** — rules library → grid combat (2 players) → creation/level-up →
   quest loop → inventory/vendors/loot/rest → art pass. One commit per step, each
   launchable, each with a 3-line playtest checklist.
4. **HARDEN** — disconnect/rejoin, mid-campaign save/load, server validation of every
   action, error surfacing (never silent desync), settings, installer, README.

Top risks (from experience): explore⇄combat desync in multiplayer (server-only FSM +
snapshots is the answer), NAT traversal in the wild (relay service + two-machine gate),
solo-dev scope explosion (content-as-data + ranked cut list), engine/plugin version
churn (pin versions, upgrade at phase boundaries), 60 fps miss (stylized art + profiled
art pass).

---

## 12. Nice-to-haves (cut first)

- Browser edition. In Unreal there is no first-class WebGL target — offer Pixel
  Streaming instead, or explicitly descope. (In Unity this consumed real effort:
  solo-only, in-process loopback transport, save-to-browser-storage, and build-time
  render-pipeline selection — shader variants are stripped at BUILD time, so swapping
  quality/pipeline configs at runtime silently renders nothing. The Unreal analog:
  never change shader-platform/PSO assumptions at runtime; bake per-platform.)
- Day/night ambience (implemented and worth it), weather, voice barks, mounts, chat
  bubbles, a second region.

Deliver a Windows build a non-technical friend can install and join with a code, plus
the automated test suite that proves the campaign is completable to level 20.
