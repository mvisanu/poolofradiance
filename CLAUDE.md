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
```

Build output: `game/Builds/Win64/RadiantPool.exe`. Exe flags for automation:
`-name <n> -autohost` / `-name <n> -autojoin localhost`.
Player log (first place to look when the user reports bugs):
`%USERPROFILE%\AppData\LocalLow\RadiantPool\Radiant Pool\Player.log`.

## Layout

- `rules/RadiantPool.Rules` — pure-C# SRD engine (dice/combat/spells/classes/loot/rest).
  Doubles as Unity local package (`package.json` + asmdef alongside the csproj; dotnet
  artifacts redirect to `/artifacts` via `rules/Directory.Build.props`). **All game math
  lives here and is unit-tested** — `CampaignSimulationTests` plays the whole campaign
  headlessly and enforces the level-5 XP curve; run it after any balance change.
- `content/` — zones/quests/monsters/items/loot/dialogue as JSON. Cross-referenced and
  IP-scanned by `ContentValidationTests`. In-code mirrors: `MonsterLibrary`,
  `SpellLibrary`, `LootLibrary` (tests keep JSON and code aligned by id).
- `game/Assets/Scripts` — runtime (FishNet networking). Server-authoritative: clients
  send intents (`Cmd*` ServerRpcs), server validates via the rules lib, broadcasts
  results (`Rpc*`). `CombatManager` = combat FSM + grid; `GameDirector` = quests/party
  state/saves; `SessionLauncher` = title screen + host/join.
- `game/Assets/Editor` — `ProjectBootstrap` regenerates the ENTIRE scene, prefabs, URP
  config, and materials from code (scene is disposable; never hand-edit it).
  `KenneyArt`/`KayKitArt` integrate the CC0 packs under `game/Assets/Art`.

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
  `/download_url` → GET that page → POST `/file/<upload_id>` → CDN url (expires 60 s;
  run the whole flow in ONE PowerShell invocation — it fails inside functions).
- Kenney kits: one URP material per kit colormap, remapped via
  `ModelImporter.AddRemap`. KayKit free tier has NO melee clip — attack = `Throw`.

## IP rule (non-negotiable)

SRD 5.1 (CC-BY) mechanics only. No WotC names/monsters/settings — `scripts/ip-scan.ps1`
enforces the banned list in `IP-CHECKLIST.md`. New monsters need `srdRef` in their JSON.
Art: Kenney (CC0) environments, KayKit (CC0) characters — credit kept in README.
