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

# IP banned-term gate:
scripts/ip-scan.ps1

# Rebuild the self-made beast models (bear, rat) — writes FBX + preview PNGs:
& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b -P scripts/make_beasts.py
```

Build output: `game/Builds/Win64/RadiantPool.exe`. Exe flags for automation:
`-name <n> [-class Fighter|Wizard|Cleric|Rogue] -autohost` /
`-name <n> -autojoin localhost`; **self-tests** `-selltest`
(bag → trader → purse), `-leveltest` (XP → level → point spent), `-attacktest` (one click on
a distant enemy → walk → blow + monster HUD), `-combatflowtest` (direct world attack → enemy round →
slotted magic → victory modal → defeat modal → retry), `-warpsmith` (park at the smithy so a shop panel can be
LOOKED at); **visual QA** `-worldmapcapture <png>` opens the maximized campaign atlas through
`MiniMap.ShowCampaignAtlasForTest`, captures it without desktop input, and restores the
temporary quest states; it also asserts three simultaneous active commissions produce
exactly one `[WorldMapObjectiveTest]` X. `-uiskincapture <png>` renders the title/character-creation screen,
asserts all 19 RPG & MMO UI 7 skin roles are present, captures it without input, then quits;
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
  headlessly and enforces the level-5 XP curve; run it after any balance change.
  **Difficulty knobs live in `Difficulty.cs` and nowhere else**: monsters spawn at 85 % HP
  and attack at −1 to hit; PCs stay pure SRD, XP is untouched. Stat blocks in
  `Monsters.cs` stay canonical (ContentValidationTests pins them to the JSON) — retune
  the knobs, never the blocks. `DifficultyTests` pins the current values.
  **Levelling lives in `Progression.cs`** (the PC-side counterpart to `Difficulty.cs`): the
  XP table, hit dice and the 20 cap stay pure SRD, but the ability-point grant is a house
  rule — **one point per level, two at 4th** — because SRD's single ASI at 4th is one
  choice in a 1–5 campaign. `ProgressionTests` pins it. XP enters a character through
  **`GameDirector.ServerGrantXp` and nowhere else** (quests AND kills; combat used to call
  `Sheet.GainXp` directly and never level anyone). Companions auto-spend their points on
  `Progression.PrimaryAbility`. **Loot gets better as the campaign deepens** (`LootLibrary`,
  mirrored in `content/loot`): rapier/studded leather mid-campaign, greatsword/splint in the
  vault, greataxe/half plate in the warcamp — `LootProgressionTests` pins that gradient.
  **Party roles live in `PartyComposition.cs`**: the sellswords Veresk musters are picked
  by ROLE, never by class order — a healer first, then damage dealers of two *different*
  classes, counting whoever is already being played (so nobody is handed a second cleric
  while the party has no rogue). `GameDirector.CmdRecruitCompanions` only spawns what it
  returns; `PartyCompositionTests` pins the guarantee.
- `content/` — zones/quests/monsters/items/loot/dialogue as JSON. Cross-referenced and
  IP-scanned by `ContentValidationTests`. In-code mirrors: `MonsterLibrary`,
  `SpellLibrary`, `LootLibrary` (tests keep JSON and code aligned by id).
- `game/Assets/Scripts` — runtime (FishNet networking). Server-authoritative: clients
  send intents (`Cmd*` ServerRpcs), server validates via the rules lib, broadcasts
  results (`Rpc*`). `CombatManager` = explicit `BattleState` FSM + grid + one serial
  `CombatActionQueue` (click-to-move via `CmdMoveTo`, controlled wind-up/impact/recovery
  timelines, paced AI turns, glide movement). `CombatTargeting` and
  `BattleResultEvaluator` live in the pure rules library; `GameDirector` = quests/party state/saves
  (4-zone chain: docks → market → warcamp → temple; `ServerRecountZone` derives cleared
  counts from `ConsumedEncounterIds` and `ServerRecheckZone` heals cleared-before-active
  dead ends — both run on load, which repairs old saves); `SessionLauncher` = title screen
  + host/join. `Theme.cs` = RPG & MMO UI 7 design system with a generated Gilded Quest
  fallback, Academia palette (mahogany/oak panels, brass borders, parchment; bright gold =
  active states only; text contrast ≥4.5:1), and one global OFL type hierarchy:
  MedievalSharp display titles, Source Serif controls/tabs/buttons, Inter body/fields.
  The UI7 package itself contains no font files. All IMGUI styling
  flows through `Ui.Begin()` → `Theme.Apply()`; tune look there, never inline styles.
  `RpgMmoUi7Art` selectively imports the locally licensed **RPG & MMO UI 7** textures and
  bakes 19 semantic runtime roles (panels, button states, fields, slots, bars, sliders,
  toggles, tooltips, scrollbars, tabs) into ignored `Resources/UI/RpgMmoUi7`. `Theme.Apply`
  consumes them globally, while `Theme.SlotStyle` covers compact hotbar/minimap controls and
  `Theme.TabStyle` covers character tabs. No pack means the generated Gilded Quest fallback.
  **Gold lives on the HotBar** (`{PartyGold:N0}g`, always visible): it used to exist only
  inside the bags and the shops, so the purse never visibly moved and the total read like a
  placeholder. Format gold `:N0` everywhere — "1,234", never "1234".
  `HotBar.cs` = persistent bottom action bar. **Attack is a first-class slot again**
  (`CombatClientUI.PickAttack`, keyboard A), named spells delegate to `PickSpell`, and the
  bar shrinks or wraps combat actions above utilities instead of overflowing (a cleric in
  combat needs 13 slots). The old permanent combat instruction/info strip is gone; only a
  temporary responsive target picker appears above the bar. It uses fixed-width two-line
  target cards, column counts derived from `Ui.W`, and a capped scroll view. **Your HEALTH
  rides above the bar** (`HotBar.DrawHealth`):
  a slim strip with the bar, `hp/max` AND the percentage — the combat unit's HP while a fight
  is on (instant, via `RpcHpSync`) and `PlayerCharacterHolder.CurrentHpSynced` between fights
  (nothing used to tell the client how hurt it was out of combat). It stays when the bar is
  stowed: the bar is furniture, hit points are not.
  In combat: **click enemy = close in AND attack, one click, however far away**
  (`CombatClientUI.ClickCell` is the ONE definition of what a click on the board means: in
  reach it swings, out of reach it orders the walk and REMEMBERS the target, and
  `TickAutoAttack` lands the blow the moment the body settles on its new cell. It used to
  take two clicks — walk, then swing — and the second was the easiest thing in the game to
  forget). Click ground = move, Space = end turn, **WASD/middle-drag pans the
  camera and F recentres** (the grid owns movement, so those keys are free).
  Direct world clicks remain the fastest attack control, while the restored **Attack** slot
  opens the same legal hostile-target path; both converge on `ClickCell`, so walk-and-strike
  behavior cannot diverge. Each named spell remains a hotbar ability and opens only its legal
  target picker (Backspace cancels). Every living monster has an exact `hp/max` bar above its
  rendered head plus a deterministic generated triangle/square/circle/diamond/hex/cross icon;
  the shapes are textures, never unsupported font glyphs. While a queued action is
  resolving, input is locked until wind-up, configured impact, HP sync, and recovery finish.
  Damage is applied at impact, not at button press. Victory/defeat are persistent modals;
  defeat offers a server-validated retry of the same encounter.
  `RadiantPool.exe -autohost -attacktest` opens Attack, asserts the picker/hotbar fit the
  logical canvas and the instruction window is absent, chooses the FURTHEST enemy, and proves
  both walk and blow; `smoke-test.ps1` runs it in its OWN instance — a live fight
  under the sell/level self-tests would fight them for the turn clock.
  `RadiantPool.exe -class Wizard -autohost -combatflowtest` covers the complete physical,
  enemy, magic-resource, victory, defeat, and retry path; see `docs/combat-system.md`.
  `OrbitCamera` **x-rays whatever hides a combatant**: every unit on the board gets a
  sight line in combat (just the player out of it), and any environment renderer blocking
  one fades to a transparent clone of its own materials, shadows off. Kenney props have no
  colliders, so this tests renderer BOUNDS, not raycasts — and `bounds.Contains` is what
  catches a monster spawned *inside* a warehouse (encounter boxes overlap the buildings).
- **Responsive UI (`Ui.cs`) — the rules every panel obeys.** The HUD is laid out on a
  **logical canvas** (`Ui.W`/`Ui.H`, ~630 units tall), never `Screen.width/height`, so
  1080p/1440p/4K get the same layout at bigger pixels. `Ui.Scale` is driven by height AND
  width: a narrow or short window scales the whole UI DOWN instead of cropping the HUD off
  the edges, and `Ui.UserScale` (Settings slider, PlayerPrefs) multiplies it. Size panels
  with `Ui.Fit()`/`Ui.FitTop()` — they clamp to the space that actually exists; a raw
  `new Rect(...)` with design-size constants WILL run off a small window. Long HUD text
  sheds detail on a narrow canvas rather than overflowing (see the combat hint line).
  `Ui.OpenPanel` makes inventory/journal/settings **mutually exclusive** (they used to
  stack dead centre); Esc = back (closes what is open, only then opens Settings). Guard
  every single-letter hotkey with `!Ui.Typing` — naming a character "Jim" used to open the
  journal, the bags and the map on the way through.
  **An open panel OWNS the screen (`Ui.PanelOpen`)**: hotbar, minimap, quest card, banner,
  steering arrow, combat strip and the shop/NPC prompts all draw NOTHING while one is up —
  the bags used to open with the whole HUD bleeding through them. Every HUD drawer checks
  `Ui.PanelOpen` at the top of its `OnGUI`; nothing decides for itself when it is in the way.
  **The hotbar itself stows (`Ui.BarCollapsed`, H or its chevron button, PlayerPrefs)** down
  to a `SHOW BAR (H)` handle — never a one-way door, same rule as the map pill and the quest
  card. `BarRect` still reports the handle, so combat clicks can't fall through it.
- `SessionPanel.cs` — status + invite code, opened/closed from a **hotbar icon** (generated
  texture, not a font glyph). `SessionLauncher` still OWNS that state (`Status`/`HostCode`
  statics) and draws only the title screen; the old permanent top-left strip is gone, which
  is what freed the corner for the quest card.
- **Wayfinding** — the player must always know what to do and where. `QuestTracker` =
  quest card top-left, **collapsible to a title pill via Hide/Show (remembered in
  PlayerPrefs)** (active quest + `[x]/[ ]` checklist of what is left), centre banner,
  big gold steering arrow above the hotbar (rotated into camera space: up = walk forward),
  and a world beacon. While >26 m from the active quarter it aims at the QUARTER and names
  it ("The Old Docks"); inside, it switches to the next fight. Bootstrap plants a lit
  **district sign** over each quarter; `MiniMap` paints the quarter names on the map (gold
  = active quest). After the campaign ends the tracker issues standing orders against any
  encounters still standing, so there is never a questless state.
- `MiniMap.cs` — docked in the **top-right corner itself** (`MapTop`) and **starts collapsed**
  (pref key `minimap.size2`). Three sizes via header **icon** buttons or `M`, which cycles
  **hidden → normal tactical map → maximized campaign atlas → hidden**, remembered in
  PlayerPrefs. Combat caps the map at normal and the initiative panel still docks off
  `MapRect.yMax`. The normal view remains the live north-up render: left-drag pans, RECENTER
  returns to the player, scroll zooms, and shape+colour markers distinguish enemies, quests,
  NPCs, vendors, smiths, locked gates, and party members.
  The **maximized view is a world atlas, never a magnified tactical camera**. Six authored
  continent-like parent regions (Cinderwell Coast, Aldenmere, Emberwild, Drowned Observatory,
  Mirewatch Reach, Ember Crown) contain all 27 playable destinations plus Council Hall.
  Destination pins show open/done/locked state; **exactly one gold X** marks the next tracked
  destination from `QuestTracker.TargetMapZoneId`, even when several commissions are active.
  The normal map consumes the same selection, and a turn-in moves the X to Council Hall.
  Dashed roads and sea lanes are derived from `ZoneConfig.PrerequisiteZoneIds`, and a separate
  teal marker identifies the party's current destination. Coastlines, ocean grid, compass,
  pins, and controls are procedural textures/original code-native art — no imported bitmap
  or unsupported font glyphs.
  `ValidateAtlas` logs `[WorldMap] PASS` only when the six regions contain every configured
  zone exactly once; `smoke-test.ps1` gates on 27/27 coverage and no structural failure.
  `-worldmapcapture <png>` is the input-free visual QA path and must not overwrite the
  player's saved map-size preference.
- `InventoryUI.cs` (I) — left column = the character sheet: **the six ABILITY SCORES first**
  (score and modifier, from the `*Synced` SyncVars via `PlayerCharacterHolder.ModOf` — the one
  definition the level-up screen also uses), then what they are WEARING (slot rows + each
  piece's stat line + totals: AC with its breakdown, HP, attack, damage); right column =
  stash, every item showing damage/protection and compared against what is equipped
  ("upgrade: +2 AC"). Client display needs the derived stats, which the sheet cannot give
  it (server-only) — `PlayerCharacterHolder` mirrors them as SyncVars on a slow poll.
- `ProgressUI.cs` — **level and XP live on the CHARACTER SHEET (I), never on the main screen**
  (`ProgressUI.XpBlock`, drawn by `InventoryUI`: level, bar, XP either side, MAX at the cap —
  one definition, so sheet and screen cannot disagree) plus the
  **level-up screen (L)** that spends ability points; each row says what
  the ability buys *this* character, including that an odd score buys nothing until the next
  point completes the modifier. Client asks (`CmdSpendAbilityPoint`), rules lib decides.
- **Item icons** — `Editor/ItemIconBaker.cs` renders each item's OWN model
  (`GameItem.HandModel` → `Resources/Weapons`) to `Resources/ItemIcons/<id>.png` during
  bootstrap; armour has no model in any CC0 pack, so it is drawn in code **from the armour
  KIND** (light/medium/heavy + AC), which means new gear needs no new art. `ItemIcon.Get`
  caches and returns null when art is missing (rows fall back to text). The mace shows an axe
  because the pack has no mace — the icon is honest about what the hand actually holds.
- **Selling** — `CmdSellItem` sells ONE stash item at its `GameDirector.SellValue` (half
  list price); `CmdSellAll` still dumps the salvage pile (and keeps potions). The Sell
  buttons appear both in the vendor panel and on every bag row, and they need a buyer in
  reach: `GameDirector.TraderNear` is the ONE definition of that (the UI greys the button
  with it, the ServerRpc re-checks it with a little slack, so button and server cannot
  disagree). `RadiantPool.exe -autohost -selltest` drives a real sale (bag → trader →
  purse) and `smoke-test.ps1` asserts it — that is what caught the CharacterController
  teleport trap below. Every `RpcNotice` is also written to Player.log.
- `theme/` — Stitch design mockups, **gitignored**: they contain WotC placeholder
  names. Copy visuals only, never text.
- Asset Store packs can't be fetched headlessly (editor sign-in required). Drop-in
  slots instead: `Resources/SpellIcons/<id>.png`, `Resources/Music/{explore,combat,
  zone_<zoneId>}`, `Resources/Characters/<Name>.prefab` (pipe-separated fallbacks in
  `CombatManager.MonsterModels`). Import steps: `docs/asset-store-import.md`.
  **The download is the only manual step**: once the editor has downloaded a pack it is
  cached as a `.unitypackage` under `%APPDATA%\Unity\Asset Store-5.x\`, and
  `scripts/import-assetstore.ps1` imports it in batchmode (`-importPackage`), converts it
  to URP and re-bootstraps — no editor clicking to import.
  For **RPG & MMO UI 7**, use `scripts/import-rpg-mmo-ui7.ps1`: it extracts only image/sprite
  art and metadata into ignored `Assets/LocalLicensed`, excludes the legacy scripts/prefabs,
  rebakes the 19-role IMGUI skin, and fails unless the skin and global typography stack are
  complete. The skin applies to every screen through `Theme.Apply`, never per-panel.
  For **PBR Graveyard and Nature Set 2.0**, run
  `python scripts/install-graveyard-assets.py`; it dependency-closes 29 authored prefabs
  from the cached 3.3 GB package and installs only their required models/materials/textures.
- `PolyPackArt.cs` — **environment art from the owned Asset Store packs**, wired
  DISCOVERY-first, not by prefab name: it finds the pack wherever it imported, sorts every
  prefab into buckets by the words in its name (`Tree/Pine/Rock/Cliff/Bush/Grass/Flower/
  Mushroom/Log/House/Ruin/Grave/Fence/Tent/Prop`) and `DressWorld` composes from BUCKETS, never
  from model names. That is what lets it work against a pack whose contents can't be read
  until it is imported — and any similar low-poly pack drops into the same slots. Absent
  ⇒ `Available == false` ⇒ Kenney fallback, so the world always builds. PBR Graveyard uses
  authored prefabs only (never raw FBX submeshes) and supplies the dominant perimeter at all
  22 remote sites plus grave rings at crypt/necropolis sites. Asset Store packs ship
  **legacy/proprietary materials that render MAGENTA or black under URP** —
  `SetupMaterials()` reads serialized albedo/normal/metallic/occlusion slots even when the
  source shader is missing, then converts all 40 selected PBR materials to URP Lit.
  Buildings stay a **collider box with the model parented inside** (renderer off): the box
  is gameplay (blocks movement, and the combat x-ray fades what hides a creature), the
  model is only the look.
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
