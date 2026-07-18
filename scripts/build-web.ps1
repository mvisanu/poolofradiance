# Builds the browser edition: bootstrap (scene/prefabs/materials from code), then a
# WebGL player into webbase/game/ using the RadiantPool web template.
# Usage: scripts/build-web.ps1   (run from anywhere; paths are absolute)
$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe"
$project = Join-Path $repo "game"

& (Join-Path $repo "scripts\compile-check.ps1")
if ($LASTEXITCODE -ne 0) { throw "compile-check failed" }

# Unity is a GUI app: Start-Process -Wait or two invocations race each other.
Write-Host "[build-web] Bootstrap (scene regen)..."
$p = Start-Process -FilePath $unity -PassThru -Wait -ArgumentList @(
    "-batchmode", "-quit", "-projectPath", "`"$project`"",
    "-executeMethod", "RadiantPool.EditorTools.ProjectBootstrap.Run",
    "-logFile", "`"$repo\boot-web.log`"")
if ($p.ExitCode -ne 0) { throw "Bootstrap failed (see boot-web.log)" }

Write-Host "[build-web] WebGL build (first run includes a full target-switch reimport)..."
$p = Start-Process -FilePath $unity -PassThru -Wait -ArgumentList @(
    "-batchmode", "-quit", "-projectPath", "`"$project`"",
    "-buildTarget", "WebGL",
    "-executeMethod", "RadiantPool.EditorTools.HeadlessBuild.WebGL",
    "-logFile", "`"$repo\webgl.log`"")
if ($p.ExitCode -ne 0) { throw "WebGL build failed (see webgl.log)" }

$out = Join-Path $repo "webbase\game"
$size = (Get-ChildItem $out -Recurse -File | Measure-Object -Sum Length).Sum / 1MB
Write-Host ("[build-web] OK -> {0}  ({1:N0} MB). Test: webbase\serve.ps1" -f $out, $size)
