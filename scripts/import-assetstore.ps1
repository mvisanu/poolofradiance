<#
.SYNOPSIS
  Imports an Asset Store .unitypackage into game/ headlessly, then re-bootstraps the
  world so the new art is actually used.

.DESCRIPTION
  Asset Store packages cannot be DOWNLOADED without an editor sign-in — but once the
  editor has downloaded one, it is cached on disk as a .unitypackage, and Unity can
  import that with -importPackage in batchmode. So the one-time manual step is:

    Unity Hub -> open game/ -> sign in -> Window > Package Manager -> My Assets
      -> "RPG Poly Pack - Lite" -> Download

  ...and everything after that is this script.

  With no -Package argument it finds the newest .unitypackage in the Unity cache whose
  name matches -Match (default: poly).

.EXAMPLE
  scripts/import-assetstore.ps1
  scripts/import-assetstore.ps1 -Match "RPG Poly Pack"
  scripts/import-assetstore.ps1 -Package "C:\path\to\RPG Poly Pack - Lite.unitypackage"
#>
param(
  [string]$Package,
  [string]$Match = "poly",
  [switch]$SkipBootstrap
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe"
$project = Join-Path $repo "game"

if (-not (Test-Path $unity)) { throw "Unity not found at $unity" }

if (-not $Package) {
  # The editor's Asset Store download cache. Location moved between Unity versions, so
  # check both, plus the Hub's newer per-user cache.
  $caches = @(
    "$env:APPDATA\Unity\Asset Store-5.x",
    "$env:APPDATA\Unity\Asset Store",
    "$env:LOCALAPPDATA\Unity\Asset Store-5.x"
  ) | Where-Object { Test-Path $_ }

  if (-not $caches) {
    Write-Host "No Unity Asset Store cache found." -ForegroundColor Yellow
    Write-Host "Download the pack once from the editor (Package Manager > My Assets), then re-run."
    exit 1
  }

  $found = Get-ChildItem -Path $caches -Recurse -Filter *.unitypackage -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -like "*$Match*" } |
           Sort-Object LastWriteTime -Descending

  if (-not $found) {
    Write-Host "No cached .unitypackage matching '$Match'. Cached packages:" -ForegroundColor Yellow
    Get-ChildItem -Path $caches -Recurse -Filter *.unitypackage -ErrorAction SilentlyContinue |
      ForEach-Object { "  " + $_.FullName }
    exit 1
  }

  $Package = $found[0].FullName
}

if (-not (Test-Path $Package)) { throw "Package not found: $Package" }
Write-Host "Importing: $Package" -ForegroundColor Cyan

# A locked exe fails the later build copy step, and a running editor blocks batchmode.
Get-Process RadiantPool -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$log = Join-Path $repo "import.log"
$p = Start-Process -FilePath $unity -PassThru -Wait -ArgumentList @(
  '-batchmode', '-quit',
  '-projectPath', $project,
  '-importPackage', $Package,
  '-logFile', $log
)
if ($p.ExitCode -ne 0) {
  Write-Host "Import FAILED (exit $($p.ExitCode)). Tail of $log :" -ForegroundColor Red
  Get-Content $log -Tail 25
  exit 1
}
Write-Host "Imported." -ForegroundColor Green

if ($SkipBootstrap) { exit 0 }

# Re-bootstrap: converts the pack's Standard materials to URP (magenta otherwise) and
# regenerates the scene so the new models are actually placed in the world.
Write-Host "Re-bootstrapping the world from the new art..." -ForegroundColor Cyan
$boot = Join-Path $repo "boot.log"
$b = Start-Process -FilePath $unity -PassThru -Wait -ArgumentList @(
  '-batchmode', '-quit',
  '-projectPath', $project,
  '-executeMethod', 'RadiantPool.EditorTools.ProjectBootstrap.Run',
  '-logFile', $boot
)
if ($b.ExitCode -ne 0) {
  Write-Host "Bootstrap FAILED (exit $($b.ExitCode)). Tail of $boot :" -ForegroundColor Red
  Get-Content $boot -Tail 30
  exit 1
}

# What did the pack actually give us? The bootstrap logs the buckets it found.
Select-String -Path $boot -Pattern "\[PolyPack\]" | ForEach-Object { $_.Line }
Write-Host "Done. Now run the build: scripts/build-all.ps1" -ForegroundColor Green
