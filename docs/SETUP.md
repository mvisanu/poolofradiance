# Developer setup — Radiant Pool

## Installed by automation (already done on this machine)
- .NET 8 SDK (`dotnet` — rules library + tests)
- Unity Hub 3.x and **Unity 6000.0.79f1** (`C:\Program Files\Unity\Hub\Editor\6000.0.79f1`)

## ⚠️ One-time manual step: Unity license (blocks everything Unity-related)

Unity requires a license tied to a Unity account and the activation flow is
interactive — automation cannot do it for you. It's free and takes ~2 minutes:

1. Open **Unity Hub** (Start menu).
2. Sign in (or create a free Unity ID).
3. Hub auto-activates a **Personal** license on sign-in
   (if not: Hub → Settings (gear) → Licenses → **Add** → *Get a free personal license*).

That's it — no project changes needed.

## Then: bootstrap + build (automated)

```powershell
# 1. Import packages, create URP settings, player prefab, gray-box scene (~5-15 min first run):
& "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe" -batchmode -quit `
  -projectPath "C:\Users\Bruce\source\repo\poolofradiance\game" `
  -executeMethod RadiantPool.EditorTools.ProjectBootstrap.Run -logFile bootstrap.log

# 2. Build the playable Windows client:
& "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe" -batchmode -quit `
  -projectPath "C:\Users\Bruce\source\repo\poolofradiance\game" `
  -executeMethod RadiantPool.EditorTools.HeadlessBuild.Win64 -logFile build.log
```

Output: `game/Builds/Win64/RadiantPool.exe`. Test flow: `docs/playtest-phase2.md`.

## Rules library (no Unity needed)

```powershell
dotnet test rules/RadiantPool.Rules.sln
```
