<#
.SYNOPSIS
  Installs only the licensed image/sprite art from RPG & MMO UI 7, then bootstraps the
  discovery-first runtime skin. The Unity editor download is the only manual step.

.DESCRIPTION
  Unity Asset Store .unitypackage files contain one folder per asset with `pathname`,
  `asset`, and `asset.meta`. This script preserves image assets and their sprite slicing
  metadata while intentionally excluding the package's 2020-era scripts and prefabs.
#>
param([string]$Package)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "game"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe"

if (-not $Package) {
  $caches = @(
    "$env:APPDATA\Unity\Asset Store-5.x",
    "$env:APPDATA\Unity\Asset Store",
    "$env:LOCALAPPDATA\Unity\Asset Store-5.x"
  ) | Where-Object { Test-Path -LiteralPath $_ }

  $found = $null
  if (@($caches).Count -gt 0) {
    $found = Get-ChildItem -Path $caches -Recurse -Filter *.unitypackage -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -match '(?i)RPG.*MMO.*UI.*7' } |
      Sort-Object LastWriteTime -Descending |
      Select-Object -First 1
  }
  if (-not $found) {
    throw "RPG & MMO UI 7 is not downloaded. In Unity: Package Manager > My Assets > RPG & MMO UI 7 > Download."
  }
  $Package = $found.FullName
}

if (-not (Test-Path -LiteralPath $Package)) { throw "Package not found: $Package" }
if (-not (Test-Path -LiteralPath $unity)) { throw "Unity not found: $unity" }

# Close only this project's editor, never somebody else's Unity project.
Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($project,
      [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-Process RadiantPool -ErrorAction SilentlyContinue |
  Stop-Process -Force -ErrorAction SilentlyContinue

$tempRoot = Join-Path $env:TEMP ("rpg_mmo_ui7_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$tempFull = [IO.Path]::GetFullPath($tempRoot)
$safeTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
if (-not $tempFull.StartsWith($safeTemp, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Refusing unsafe extraction path: $tempFull"
}

try {
  Write-Host "Extracting licensed sprite art from: $Package" -ForegroundColor Cyan
  $stagedPackage = Join-Path $tempRoot 'package.unitypackage'
  Copy-Item -LiteralPath $Package -Destination $stagedPackage -Force
  $tar = Start-Process -FilePath "tar.exe" -ArgumentList @('-xf', $stagedPackage, '-C', $tempRoot) -PassThru -Wait -WindowStyle Hidden
  if ($tar.ExitCode -ne 0) { throw "tar extraction failed with exit code $($tar.ExitCode)" }

  $extensions = @('.png', '.tga', '.psd', '.jpg', '.jpeg', '.tif', '.tiff')
  $projectFull = [IO.Path]::GetFullPath($project).TrimEnd('\') + '\'
  $licensedRoot = Join-Path $project 'Assets\LocalLicensed\RpgMmoUi7'
  $installed = 0
  foreach ($pathFile in (Get-ChildItem -LiteralPath $tempRoot -Recurse -Filter pathname)) {
    $pathname = [IO.File]::ReadAllText($pathFile.FullName).Trim()
    # Some older Asset Store archives append a newline plus an octal mode marker (`00`)
    # to every pathname record. Strip only that terminal archive marker.
    $pathname = [Text.RegularExpressions.Regex]::Replace($pathname, '[\r\n]+[0-7]{2}$', '')
    $pathname = $pathname.Replace('/', '\')
    if (-not $pathname.StartsWith('Assets\', [StringComparison]::OrdinalIgnoreCase)) { continue }
    if ($extensions -notcontains [IO.Path]::GetExtension($pathname).ToLowerInvariant()) { continue }

    $relative = $pathname.Substring('Assets\'.Length)
    $target = [IO.Path]::GetFullPath((Join-Path $licensedRoot $relative))
    if (-not $target.StartsWith($projectFull, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Package path escapes the Unity project: $pathname"
    }
    $source = Join-Path $pathFile.Directory.FullName 'asset'
    $meta = Join-Path $pathFile.Directory.FullName 'asset.meta'
    if (-not (Test-Path -LiteralPath $source)) { continue }

    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    if (Test-Path -LiteralPath $meta) { Copy-Item -LiteralPath $meta -Destination ($target + '.meta') -Force }
    $installed++
  }
  if ($installed -eq 0) { throw "The package contained no supported UI image assets." }
  Write-Host "Installed $installed licensed image assets; legacy scripts/prefabs excluded." -ForegroundColor Green
}
finally {
  if (Test-Path -LiteralPath $tempFull) { Remove-Item -LiteralPath $tempFull -Recurse -Force }
}

$bootLog = Join-Path $repo "boot-ui7.log"
$boot = Start-Process -FilePath $unity -ArgumentList @(
  '-batchmode', '-quit', '-projectPath', $project,
  '-executeMethod', 'RadiantPool.EditorTools.ProjectBootstrap.Run',
  '-logFile', $bootLog
) -PassThru -Wait -WindowStyle Hidden
if ($boot.ExitCode -ne 0) {
  Get-Content -LiteralPath $bootLog -Tail 120
  throw "RPG & MMO UI 7 bootstrap failed with exit code $($boot.ExitCode)."
}

$ready = Select-String -LiteralPath $bootLog -Pattern '\[RpgMmoUi7\] READY' |
  Select-Object -Last 1
if (-not $ready) {
  Get-Content -LiteralPath $bootLog -Tail 120
  throw "Bootstrap did not produce a ready RPG & MMO UI 7 skin."
}
$ready.Line
