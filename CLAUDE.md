# CLAUDE.md — Radiant Pool

3D co-op party CRPG (2–4 players, SRD 5.1 rules, Pool of Radiance structure, original IP).
Read `goal.md` for the spec, `ARCHITECTURE.md` for design, `IP-CHECKLIST.md` before naming anything.

## Commands

```powershell
# Rules library tests (fast, no Unity):
dotnet test rules/RadiantPool.Rules.sln

# Compile-check ALL runtime game scripts without a Unity license (~2 s after first run):
scripts/compile-check.ps1

# Regenerate scene/prefabs/materials from code, then build the playable exe:
& "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe" -batchmode -quit `
  -projectPath game -executeMethod RadiantPool.EditorTools.ProjectBootstrap.Run -logFile boot.log
& "...same Unity.exe" -batchmode -quit -projectPath game `
  -executeMethod RadiantPool.EditorTools.HeadlessBuild.Win64 -logFile build.log
# (scripts/build-all.ps1 chains tests → bootstrap → build)

# Two-instance netplay verification (host + join on loopback, asserts via logs):
scripts/smoke-test.ps1

# Package the existing Win64 player as one friend-ready installer (requires Inno Setup):
scripts/build-installer.ps1

# IP banned-term gate:
scripts/ip-scan.ps1

# Rebuild the self-made beast models (bear, rat) — writes FBX + preview PNGs:
& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b -P scripts/make_beasts.py
```

Build output: `game/Builds/Win64/RadiantPool.exe`. Installer output:
`game/Builds/Installer/RadiantPool-Setup-1.0.0.exe`. Exe flags for automation:
`-name <n> [-class Fighter|Wizard|Cleric|Rogue] -autohost` /
`-name <n> -autojoin localhost`; **self-tests** `-selltest`
(bag → trader → purse), `-leveltest` (XP → level → point spent), `-attacktest` (one click on
a distant enemy → walk → blow → automatic turn end + monster HUD), `-combatflowtest` (direct world attack → enemy round →
slotted magic → victory modal → defeat modal → retry), `-warpsmith` (park at the smithy so a shop panel can be
LOOKED at), `-questmarkertest` (yellow ! → gray ? → yellow ? → hidden),
`-waystonehighlighttest` (tracked active quest → green route; turn-in → no outbound route);
**visual QA** `-questmarkercapture <dir>` captures all three visible quest-giver states;
`-waystonecapture <png>` opens the network and captures its green quest destination;
`-worldmapcapture <png>` opens the maximized campaign atlas through
`MiniMap.ShowCampaignAtlasForTest`, captures it without desktop input, and restores the
temporary quest states; it also asserts three simultaneous active commissions produce
exactly one `[WorldMapObjectiveTest]` X. `-uiskincapture <png>` renders the title/character-creation screen,
asserts all 26 RPG & MMO UI 7 skin roles are present, captures it without input, then quits;
`-savedir <dir>` keeps a test run off the real campaign.
Player log (first place to look when the user reports bugs):
`%USERPROFILE%\AppData\LocalLow\RadiantPool\Radiant Pool\Player.log`.

**Verify gameplay with those flags — never by driving the window.** The user is at this
desktop: synthetic keystrokes/clicks (`SendKeys`, `keybd_event`, `mouse_event`) land in
whatever window actually has focus (they have gone into his browser), and ALT-tapping to steal
foreground puts the game in system-menu mode, which eats every key after it and hangs the game.
Screenshotting the window is fine; sending it INPUT is not. To cover a client-side path, pull
it into one public method the test can call — `CombatClientUI.ClickCell` is the pattern: the
mouse and the self-test drive the very same code.

## Layout

- `rules/RadiantPool.Rules` — pure-C# SRD engine (dice/combat/spells/classes/loot/rest).
  Doubles as Unity local package (`package.json` + asmdef alongside the csproj; dotnet
  artifacts redirect to `/artifacts` via `rules/Directory.Build.props`). **All game math
  lives here and is unit-tested** — `CampaignSimulationTests` plays the whole campaign
  headlessly and enforces the complete level-20 XP curve and two-player difficulty floor;
  run it after any balance change.
  **Difficulty knobs live in `Difficulty.cs` and nowhere else**: monsters spawn at 85 % HP
  and attack at −1 to hit; PCs stay pure SRD, XP is untouched. Stat blocks in
  `Monsters.cs` stay canonical (ContentValidationTests pins them to the JSON) — retune
  the knobs, never the blocks. `DifficultyTests` pins the current values.
  **Levelling lives in `Progression.cs`** (the PC-side counterpart to `Difficulty.cs`): the
  XP table, hit dice and the 20 cap stay pure SRD, but the ability-point grant is a house
  rule — **one point per level, two at 4th** — so every one of the twenty levels carries
  a build choice. `ProgressionTests` pins it. XP enters a character through
  **`GameDirector.ServerGrantXp` and nowhere else** (quests AND kills; combat used to call
  `Sheet.GainXp` directly and never level anyone). Companions auto-spend their points on
  `Progression.PrimaryAbility`. **Loot gets better as the campaign deepens** (`LootLibrary`,
  mirrored in `content/loot`): rapier/studded leather mid-campaign, greatsword/splint in the
  vault, greataxe/half plate in the warcamp — `LootProgressionTests` pins that gradient.
  **Magic gear scales that gradient to level 20**: "+N" weapons/armour, caster robes (cloth
  body armour — the wizard's only defence), off-hand pieces beyond the shield (magical
  shields, a caster warding orb), and rings fill the higher tiers. Each variant is DERIVED in
  `GameItems.cs` from a mundane base by id (`longsword_1`): `BaseId` carries class
  proficiency + hand model, `MagicBonus` lifts weapon to-hit+damage / armour+shield AC, rings
  carry `RingAc/RingSave/RingAttack/RingDamage`. `PlayerCharacterHolder.RecomputeMagic` sums
  every worn source into one `CharacterSheet.SetShield(bool,bonus)` + `SetMagicDefense(ac,save)`
  so nothing double-counts; `BasicAttack` reads weapon+ring plus live. Two ring slots
  (`Ring1Id/Ring2Id`) + generalized off hand (`OffhandId`; `ShieldEquipped` = a REAL shield,
  for visual + companion parity). Woven into quest tiers 2/3/5/6/7 + vault/warcamp caches —
  **leave tier 4 alone**: `LootProgressionTests.RequiredFightCache` pins its weights (total
  100, `runed_staff` last so `FixedRng(70,100)` lands on it). New items go in BOTH
  `GameItems.cs` and `content/items/items.json` (tables reference the json) + `SellValue`.
  **Party roles live in `PartyComposition.cs`**: the sellswords Veresk musters are picked
  by ROLE, never by class order — a healer first, then damage dealers of two *different*
  classes, counting whoever is already being played (so nobody is handed a second cleric
  while the party has no rogue). `GameDirector.CmdRecruitCompanions` only spawns what it
  returns; `PartyCompositionTests` pins the guarantee.
- `content/` — zones/quests/monsters/items/loot/dialogue as JSON. Cross-referenced and
  IP-scanned by `ContentValidationTests`. In-code mirrors: `MonsterLibrary`,
  `SpellLibrary`, `LootLibrary` (tests keep JSON and code aligned by id).
  **The complete campaign is 39 zones / 37 quest files / 23 monsters.** The twelve-zone
  `level20_expansion.json` adds four original three-stage arcs: Stormglass, Frostbound,
  Titan's Chain, and Hollow Star. Its normal required path reaches level 20 during the
  Dawnspire finale. High-level quest rewards use tiers 5–7; full-caster slots serialize
  and replicate across all nine spell levels; fighter Attack actions scale to 2/3/4 swings
  at levels 5/11/20. `CampaignRewardLibrary` remains the reward authority.
  `game/neverwinternight.txt` is a copyrighted walkthrough used only as a structural pacing
  reference. Never copy its names, prose, characters, creatures, locations, or plot beats.
- `game/Assets/Scripts` — runtime (FishNet networking). Server-authoritative: clients
  send intents (`Cmd*` ServerRpcs), server validates via the rules lib, broadcasts
  results (`Rpc*`). `CombatManager` = explicit `BattleState` FSM + grid + one serial
  `CombatActionQueue` (click-to-move via `CmdMoveTo`, wind-up/impact/recovery timelines, paced
  AI turns, glide). `CombatTargeting`/`BattleResultEvaluator` live in the pure rules library;
  `GameDirector` = quests/party/saves (4-zone chain docks → market → warcamp → temple;
  `ServerRecountZone` derives cleared counts from `ConsumedEncounterIds`, `ServerRecheckZone`
  heals cleared-before-active dead ends — both run on load, repairing old saves);
  `SessionLauncher` = title + host/join. `Theme.cs` = RPG & MMO UI 7 design system with a
  generated Gilded Quest fallback, Academia palette (mahogany/oak panels, brass borders,
  parchment; bright gold = active only; contrast ≥4.5:1), one global OFL type hierarchy
  (MedievalSharp titles, Source Serif controls, Inter body; UI7 ships no fonts). All IMGUI
  styling flows through `Ui.Begin()` → `Theme.Apply()`; tune there, never inline.
  `RpgMmoUi7Art` selectively imports the locally licensed **RPG & MMO UI 7** textures and
  bakes 26 semantic runtime roles (panels, buttons, fields, slots, bars, sliders, toggles,
  tooltips, scrollbars, tabs, plus HUD art: `statbar_overlay`, `xpbar`/`xpbar_fill`,
  `currency_gold`, `divider`, `decoration`, `glow`) into ignored `Resources/UI/RpgMmoUi7`.
  `Theme.Apply` consumes chrome globally; `Theme.SlotStyle`/`TabStyle` cover hotbar/minimap/tabs;
  HUD art flows through `Theme.Bar`/`XpBar`/`CurrencyGlyph`/`Divider`/`Glow`/`Decoration`.
  **New roles need a `PreferredSuffix` entry (exact lowercased vendor path) in `RpgMmoUi7Art`
  AND a name in `RpgMmoUi7Skin.Roles`** — `-uiskincapture` asserts
  `LoadedRoleCount == Roles.Length`, so a role that fails to bake fails the smoke test. Bake
  only what a `Theme.*` helper consumes. No pack ⇒ generated Gilded Quest fallback.
  **Gold lives on the HotBar** (`{PartyGold:N0}g`, always visible): it used to exist only
  inside the bags and the shops, so the purse never visibly moved and the total read like a
  placeholder. Format gold `:N0` everywhere — "1,234", never "1234".
  `HotBar.cs` = persistent bottom action bar. **Attack is a first-class slot**
  (`CombatClientUI.PickAttack`, keyboard A), named spells delegate to `PickSpell`, and the
  bar shrinks/wraps combat actions above utilities instead of overflowing (a combat cleric
  needs 13 slots). Only a temporary responsive target picker appears above the bar (fixed-width
  two-line cards, columns from `Ui.W`, capped scroll). **Your HEALTH rides above the bar**
  (`HotBar.DrawHealth`): bar + `hp/max` + percentage — the combat unit's HP in a fight
  (`RpcHpSync`), `PlayerCharacterHolder.CurrentHpSynced` between fights. Stays when the bar is
  stowed. In combat: **click enemy = close in AND attack, one click, however far away**
  (`CombatClientUI.ClickCell` is the ONE definition of a board click: in reach it swings, out
  of reach it walks and REMEMBERS the target, `TickAutoAttack` lands the blow when the body
  settles). A completed weapon attack **auto-ends that player's turn after impact + recovery**
  (`CmdAttack` marks its queued action for handoff). Click ground = move, Space = manual
  end-turn, **WASD/middle-drag pans, F recentres** (the grid owns movement). The Attack slot
  opens the same legal target path as a world click; both converge on `ClickCell`. Each spell
  opens only its legal target picker (Backspace cancels). Every living monster has an exact
  `hp/max` bar over its head + a generated triangle/square/circle/diamond/hex/cross texture
  icon (never font glyphs). While a queued action resolves, input is locked through wind-up,
  impact, HP sync, and recovery; damage applies at impact, not button press. Victory/defeat
  are persistent modals; defeat offers a server-validated retry of the same encounter.
  `RadiantPool.exe -autohost -attacktest` opens Attack, asserts the picker/hotbar fit the
  logical canvas and the instruction window is absent, chooses the FURTHEST enemy, and proves
  walk, blow, and automatic turn handoff; `smoke-test.ps1` runs it in its OWN instance — a live fight
  under the sell/level self-tests would fight them for the turn clock.
  `RadiantPool.exe -class Wizard -autohost -combatflowtest` covers the complete physical,
  enemy, magic-resource, victory, defeat, and retry path; see `docs/combat-system.md`.
  `OrbitCamera` **x-rays whatever hides a combatant**: every unit on the board gets a
  sight line in combat (just the player out of it), and any environment renderer blocking
  one fades to a transparent clone of its own materials, shadows off. Kenney props have no
  colliders, so this tests renderer BOUNDS, not raycasts — and `bounds.Contains` is what
  catches a monster spawned *inside* a warehouse (encounter boxes overlap the buildings).
- **Responsive UI (`Ui.cs`) — the rules every panel obeys.** The HUD lays out on a **logical
  canvas** (`Ui.W`/`Ui.H`, ~630 units tall), never `Screen.width/height`, so 1080p/1440p/4K
  share one layout at bigger pixels. `Ui.Scale` follows height AND width (a narrow/short window
  scales DOWN, never crops); `Ui.UserScale` (Settings, PlayerPrefs) multiplies it. Size panels
  with `Ui.Fit()`/`Ui.FitTop()` — a raw `new Rect(...)` with design constants runs off a small
  window. Long HUD text sheds detail rather than overflowing. `Ui.OpenPanel` makes
  inventory/journal/settings **mutually exclusive**; Esc = back (closes what's open, then opens
  Settings). Guard every single-letter hotkey with `!Ui.Typing`. **An open panel OWNS the
  screen (`Ui.PanelOpen`)**: hotbar, minimap, quest card, banner, arrow, combat strip, shop/NPC
  prompts draw NOTHING while one is up — every HUD drawer checks `Ui.PanelOpen` atop its
  `OnGUI`. **The hotbar stows** (`Ui.BarCollapsed`, H/chevron, PlayerPrefs) to a `SHOW BAR (H)`
  handle; `BarRect` still reports the handle so combat clicks can't fall through it.
- `SessionPanel.cs` — status + invite code, opened/closed from a **hotbar icon** (generated
  texture, not a font glyph). `SessionLauncher` still OWNS that state (`Status`/`HostCode`
  statics) and draws only the title screen; the old permanent top-left strip is gone, which
  is what freed the corner for the quest card.
- **Wayfinding** — the player must always know what to do and where. `QuestTracker` = quest
  card top-left, **collapsible to a title pill (Hide/Show, PlayerPrefs)** (active quest +
  `[x]/[ ]` checklist), centre banner, gold steering arrow above the hotbar (camera-space: up =
  forward), world beacon. >26 m from the active quarter it aims at + names the QUARTER ("The Old
  Docks"); inside, it switches to the next fight. Bootstrap plants a lit **district sign** per
  quarter; `MiniMap` paints quarter names (gold = active). After the campaign ends the tracker
  issues standing orders against any encounters still standing — never a questless state.
  `NpcInteract` owns the quest giver's overhead marker: **yellow `!`** = opening commission
  available, **gray `?`** = accepted/active, **yellow `?`** = `ObjectivesMet` ready for
  turn-in (priority); no commission ⇒ hidden. Inter Bold world-space glyph, unlit/outlined/
  billboarded/bobbed (off under Reduced Motion), derived from replicated `MusterState`/
  `ZoneStates`; never a duplicate counter. `CampaignTravel` consumes
  `QuestTracker.RecommendedTravelZoneIndex`: the Waystone Network renders the tracked quest's
  destination as a pale-green card + green **Travel now**. Multiple commissions follow the
  journal's saved Track choice; a ready turn-in highlights no outbound site (target is Council
  Hall). Colour is reinforced by words.
- `MiniMap.cs` — docked in the **top-right corner itself** (`MapTop`) and **starts collapsed**
  (pref key `minimap.size2`). Three sizes via header **icon** buttons or `M`, which cycles
  **hidden → normal tactical map → maximized campaign atlas → hidden**, remembered in
  PlayerPrefs. Combat caps the map at normal and the initiative panel still docks off
  `MapRect.yMax`. The normal view remains the live north-up render: left-drag pans, RECENTER
  returns to the player, scroll zooms, and shape+colour markers distinguish enemies, quests,
  NPCs, vendors, smiths, locked gates, and party members.
  The **maximized view is a world atlas, never a magnified tactical camera**: authored
  continent-like parent regions (Cinderwell Coast, Aldenmere, Emberwild, Drowned Observatory,
  Mirewatch Reach, Ember Crown) contain all playable destinations plus Council Hall. Pins show
  open/done/locked; **exactly one gold X** marks the next tracked destination from
  `QuestTracker.TargetMapZoneId` even with several commissions active (the normal map consumes
  the same selection; a turn-in moves the X to Council Hall). Roads/sea lanes derive from
  `ZoneConfig.PrerequisiteZoneIds`; a teal marker = current destination. All procedural
  textures/code-native art — no bitmaps or font glyphs. `ValidateAtlas` logs `[WorldMap] PASS`
  only when every region contains each configured zone exactly once; `smoke-test.ps1` gates on
  full coverage. `-worldmapcapture <png>` must not overwrite the saved map-size preference.
- `InventoryUI.cs` (I) — left column = character sheet: **six ABILITY SCORES first** (score +
  modifier, from `*Synced` SyncVars via `PlayerCharacterHolder.ModOf` — the level-up screen's
  definition too), then WORN slots (weapon/armor/off hand/two rings) with each piece's stat
  line + totals (AC breakdown, HP, attack, damage); right column = stash, each item showing
  damage/protection compared vs equipped ("upgrade: +2 AC", `ItemCompare`). Derived stats are
  server-only, so `PlayerCharacterHolder` mirrors them as SyncVars on a slow poll.
- `ProgressUI.cs` — **level and XP live on the CHARACTER SHEET (I), never the main screen**
  (`ProgressUI.XpBlock` drawn by `InventoryUI`: level, bar, XP, MAX at cap — one definition)
  plus the **level-up screen (L)** that spends ability points; each row says what the ability
  buys *this* character (odd score buys nothing until the next point completes the modifier).
  Client asks (`CmdSpendAbilityPoint`), rules lib decides.
- **Item icons** — `Editor/ItemIconBaker.cs` renders each item's OWN model (`GameItem.HandModel`
  → `Resources/Weapons`) to `Resources/ItemIcons/<id>.png` at bootstrap; armour+robes have no
  model so they're drawn in code from the armour KIND (new gear needs no art). `ItemIcon.Get`
  caches, falls back to `BaseId`'s icon for "+N" variants, returns null when missing (text
  fallback). Rings have no icon (text).
- **Selling** — `CmdSellItem` sells ONE stash item at `GameDirector.SellValue` (half list
  price); `CmdSellAll` dumps salvage (keeps potions). Sell buttons in the vendor panel + every
  bag row need a buyer in reach: `GameDirector.TraderNear` is the ONE definition (UI greys the
  button, ServerRpc re-checks with slack). `-selltest` drives a real sale (bag → trader →
  purse); `smoke-test.ps1` asserts it. Every `RpcNotice` also writes to Player.log.
- `theme/` — Stitch design mockups, **gitignored**: they contain WotC placeholder
  names. Copy visuals only, never text.
- Asset Store packs can't be fetched headlessly (editor sign-in). Drop-in slots instead:
  `Resources/SpellIcons/<id>.png`, `Resources/Music/{explore,combat,zone_<zoneId>}`,
  `Resources/Characters/<Name>.prefab` (fallbacks in `CombatManager.MonsterModels`). Steps:
  `docs/asset-store-import.md`. **Download is the only manual step**: cached as `.unitypackage`
  under `%APPDATA%\Unity\Asset Store-5.x\`, then `scripts/import-assetstore.ps1` imports in
  batchmode, converts to URP, re-bootstraps. **RPG & MMO UI 7**: `scripts/import-rpg-mmo-ui7.ps1`
  extracts art/metadata into ignored `Assets/LocalLicensed`, rebakes the 26-role skin, fails
  unless skin + typography are complete. **PBR Graveyard / Nature Set 2.0**:
  `python scripts/install-graveyard-assets.py` dependency-closes 29 authored prefabs from the
  cached 3.3 GB package.
- `PolyPackArt.cs` — **environment art from the owned Asset Store packs**, wired
  DISCOVERY-first: it finds the pack wherever it imported, sorts prefabs into buckets by name
  words (`Tree/Pine/Rock/Cliff/Bush/Grass/Flower/Mushroom/Log/House/Ruin/Grave/Fence/Tent/
  Prop`), and `DressWorld` composes from BUCKETS, never model names — so any similar low-poly
  pack drops in. Absent ⇒ `Available == false` ⇒ Kenney fallback. PBR Graveyard uses authored
  prefabs only (never raw FBX submeshes), dominant at all 22 remote sites. **Necropolis/Crypt
  sites are designed graveyards, not a grave ring** (`ProjectBootstrap.RemoteSite`, gated on
  `graveyard`): walled perimeter (`Kind.Ruin` walls, tangent-rotated) with two gaps (front −Z
  gate lane, rear +Z mausoleum frame), a `church_tower` mausoleum **OUTSIDE the back wall as a
  backdrop** (never between camera and centre, or combat x-ray fades it), gate lantern + violet
  light, flanking `death_statue`s, clustered graves, storytelling props (`coffin_broken`,
  `rock_skull`, `dead_fern`/`Ivy_02`). **Haunted non-cemetery themes (`Keep`/`Manor`/
  `Observatory`) get graveyard ACCENTS** (`haunted` block: two grave clusters, a statue, a
  skull-stone, corrupted ferns/ivy, one violet candle) over their own dressing — small props,
  never a walled necropolis. All decoration is collider-free and not network-spawned. Verify:
  `RadiantPool.exe -autohost -sitecapture <png> -sitezone lanternfall_necropolis` (or
  `blackbriar_manor`/`drowned_bastion`). Asset Store packs ship **materials that render MAGENTA
  or black under URP** — `SetupMaterials()` reads serialized albedo/normal/metallic/occlusion
  slots even with the shader missing, then converts all 40 to URP Lit. Buildings stay a
  **collider box with the model parented inside** (renderer off): box is gameplay, model is look.
- `game/Assets/Editor` — `ProjectBootstrap` regenerates the ENTIRE scene, prefabs, URP
  config, and materials from code (scene is disposable; never hand-edit it). Includes
  `DressWorld()` (seeded forests/scatter/wilds sites, sunny lighting) and the district
  signs. `KenneyArt`/`KayKitArt`/`QuaterniusArt` integrate the CC0 packs under
  `game/Assets/Art` (Quaternius orcs + spider are animated FBX — the spider comes from
  the Easy Animated Enemy Pack via blend2fbx).
- **Beasts we make ourselves** — the CC0 packs ship no bear and no rat, so
  `scripts/make_beasts.py` builds them in headless Blender
  (`blender -b -P scripts/make_beasts.py` → `Art/Generated/*.fbx` + a preview PNG to
  eyeball) and `GeneratedArt.cs` imports them. Original geometry ⇒ no licence attaches.
  These are **not** height-normalised like the humanoid packs — a bear is long and low, so
  the prefab keeps the mesh's true metre scale. **Every monster id must map to a real
  prefab**: the capsule fallback is a bug, not a style, and now logs a warning.
- All project + memory `*.md` files auto-mirror to the Obsidian vault
  (`C:\Users\Bruce\Documents\obsidian\projects\poolofradiance`) via a Stop hook →
  `scripts/obsidian-sync.ps1`. Markdown edits sync themselves; don't copy manually.

## Iteration loop (what actually works here)

1. Edit scripts → `scripts/compile-check.ps1` (catches C# errors in seconds).
2. Bootstrap + build (background, ~2–4 min) → `scripts/smoke-test.ps1`.
3. Launch exe for the user, commit. The user playtests and reports; read their
   Player.log for stack traces.

## Hard-won gotchas (violate these and you will lose an hour)

- **PowerShell 5.1 mojibake**: `Get-Content -Raw` + `Set-Content` corrupts UTF-8 (— ✔ ⚔ →
  `â€”` etc.). For bulk edits use `[System.IO.File]::ReadAllText/WriteAllText` with
  explicit UTF8. Repair: `utf8.GetString(cp1252.GetBytes($text))`.
- **Never let an exception escape a ServerRpc** — FishNet kicks the sender as a
  malformed-packet attacker. All combat resolution is try/caught; keep it that way.
- **A terminal combat result can clear `_engine` while another combat coroutine is resuming.**
  `EndActiveTurn` captures and null-checks the engine before advancing. Keep the guard: retry
  testing found the old coroutine otherwise threw after an otherwise successful battle.
- **Character creation class labels must match `CharacterClass` numeric order exactly**
  (`Fighter, Wizard, Cleric, Rogue`). The launcher once displayed Cleric/Wizard in the
  opposite order and silently created the other caster class.
- **Dice strings**: negative modifiers must render `1d6-1`, never `1d6+-1`.
- **`Shader.Find` fails in builds** for shaders nothing references — materials used at
  runtime must be assets under `Resources/` (see `Resources/Fx/M_GridOverlay`).
- **Unity fake-null defeats `??`/`?.`** on `GetComponent` — use explicit `== null`.
- **FishNet scene NetworkObjects need SceneIds**: batchmode-generated scenes must call
  `NetworkObject.CreateSceneId` via reflection (bootstrap does).
- **Animator any-state transitions**: set `canTransitionToSelf = false` or bool-driven
  states (Death) re-enter every frame and freeze on frame 0.
- **Loopback is `127.0.0.1`**, not `localhost` (Tugboat binds IPv4).
- **itch.io downloads** (KayKit/Quaternius, all CC0): GET page → csrf token → POST
  `/download_url` → GET that page → POST `/file/<upload_id>?source=game_download` →
  CDN url (expires 60 s; run the whole flow in ONE PowerShell invocation — it fails
  inside functions). Do NOT append the `key` from the download-page URL — it arrives
  already URL-encoded and the endpoint rejects it ("invalid key"); the session cookie
  is what authorizes the POST.
- Kenney kits: one URP material per kit colormap, remapped via
  `ModelImporter.AddRemap`. KayKit free tier has NO melee clip — attack = `Throw`.
  Kenney Nature kit has no colormap atlas — build per-embedded-material URP materials
  instead (see `KenneyArt.ColorMat`).
- **Unity.exe is a GUI app** — PowerShell `&` returns immediately, so a chained
  bootstrap+build races itself ("another Unity instance is running"). Use
  `Start-Process -PassThru -Wait`. Also close the running game exe first: a locked
  `RadiantPool.exe` fails the build copy step.
- **Google Drive throttles bulk downloads** (Quaternius packs) — prefer converting the
  already-downloaded `.blend` files via headless Blender
  (`blender -b file.blend -P blend2fbx.py`, FBX export with
  `bake_anim_use_all_actions`) over re-fetching FBX from Drive.
- **PS 5.1 mangles embedded double quotes** passed to native exes — `git commit -m`
  with `"quoted"` text inside the message splits into bogus pathspecs. Keep commit
  messages free of double quotes.
- **`RadiantPool.exe` timestamp never changes** between builds (stock player stub).
  To verify a build is fresh, check `RadiantPool_Data/Managed/Assembly-CSharp.dll`.
- **CombatClientUI HUD rects gate click-to-move**: `IsMouseOverHud` must test the exact
  same rects the panels draw with. It used to re-declare them as hand-copied literals
  (`new Rect(12, Ui.H - 174, ...)`), which drifted from the panels the moment either moved.
  They are now `Rect` PROPERTIES (`LogRect`, `MyCardRect`, `InitiativeRect`, plus
  `HotBar.BarRect` / `MiniMap.MapRect`) — one definition, both users. Never re-type a rect.
- **`GUI.tooltip` is GLOBAL to the frame, not to your panel.** Whatever control was hovered
  last sets it, so any panel that prints `GUI.tooltip` prints the *other* panel's hint — the
  hotbar spent a build rendering the minimap's "Show map (M)" across the health readout. Gate
  it on your own rect: `if (GUI.tooltip.Length > 0 && MyRect.Contains(Ui.Mouse))`.
- **A narrow IMGUI button has no room for its own icon.** The skin's padding is subtracted from
  the button's width, so a 24 px button hands its `GUIContent` image a ~4 px content box and the
  icon renders invisible (the 46 px slots next to it were fine). Draw the texture OVER the
  button instead — `GUI.Button(...)` then `GUILayoutUtility.GetLastRect()` + `GUI.DrawTexture`
  (see `HotBar` stow button, `MiniMap.IconButton`).
- **NO dingbat/arrow glyphs in UI strings** — MedievalSharp and Inter carry no
  `✔ ✘ ⚔ ✝ ★ ● ▼ ► ↑↗→ ▶ −`, and a missing glyph renders as a **tofu box**, not the symbol
  you meant. This has bitten the minimap buttons AND the whole combat HUD. Use ASCII
  (`[x]`, `[ ]`, `>`), words (`Theme.Ready()` = "ready"/"spent", bearings as "ahead-right"),
  or a **generated texture** (`QuestTracker.MakeSteerArrow`, `MiniMap.Make*`).
- **The initiative panel sits BELOW the minimap** (`MiniMap.MapRect.yMax`), not in the
  top-right corner — pinned to the corner it drew straight through the map. Any new
  top-right HUD must dock off the same anchor, and cap its height with a scroll view.
- **`compile-check.ps1` does NOT compile `Assets/Editor`** — only the runtime scripts. An
  editor-only error (a missing `using RadiantPool.Rules`) sails past it and only surfaces as
  a failed bootstrap. After editing anything under `Assets/Editor`, run the bootstrap.
- **Run Unity with an ABSOLUTE `-projectPath`**: the shell's working directory persists
  between tool calls, so a stray `cd` turns `-projectPath game` into `<cwd>/game` and Unity
  exits 1 ("Couldn't set project path") — or worse, writes `boot.log` into `Assets/` where it
  gets imported as an asset.
- **A `CharacterController` eats direct `transform.position` writes** — the body is back
  where it started on the next frame. Park it (`cc.enabled = false`), move, re-enable
  (`CombatFx.GlideRoutine`, `GameDirector.Warp`). A "teleport" that silently does nothing
  looks exactly like a broken feature: the sell self-test failed with "no trader here"
  until the seller actually arrived at the shop.
- Combat-end must clear the Animator `Dead` flag (`CharacterVisuals.SetDead(v,false)`),
  not just rotation — revived characters otherwise walk around in the death pose.
- Victory revives dead PCs at 1 HP (no permanent death); party wipe revives all at
  the shrine (`CombatManager.RespawnPoint` must match the bootstrap shrine).
- **Never keep a counter that duplicates persisted truth.** `ZoneClearedCounts` used to be
  incremented by hand *after* the autosave ran, so every save persisted a count one behind
  `ConsumedEncounterIds`; reloading restored the stale count while the fight stayed
  consumed, and the zone ended up demanding fights that no longer existed. It is now
  **derived** from the consumed list (`ServerRecountZone`), recounted on every clear and on
  load — which self-heals broken saves. Order is always recount → recheck → save.
- **Companions have no connection** (server-owned AI, `Owner` invalid). A `TargetRpc` aimed
  at one logs "Target is not an observer" on every call — guard with
  `!IsCompanion && Owner != null && Owner.IsValid` (see `CombatManager.SyncHp`).
- **Camera assists must be one-shot, not per-frame.** The combat "tactical assist" ran every
  frame and hauled pitch/zoom back the moment the mouse was released — the camera would not
  stay where the player put it. It now fires once per fight and any camera input cancels it.
- **Read the save, not just the code**, when the user reports a stuck quest:
  `%USERPROFILE%\Saved Games\RadiantPool\campaign.json` holds `ZoneStates`,
  `ZoneClearedCounts` and `ConsumedEncounters` — it pinpointed the counter bug in one look.
  Back it up before running the game against it (the load path rewrites it).

## IP rule (non-negotiable)

SRD 5.1 (CC-BY) mechanics only. No WotC names/monsters/settings — `scripts/ip-scan.ps1`
enforces the banned list in `IP-CHECKLIST.md`. New monsters need `srdRef` in their JSON.
Art: Kenney (CC0) environments, KayKit (CC0) characters — credit kept in README.
