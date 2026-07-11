# Two-instance sync smoke test: host + join on one machine, assert via player logs.
# Requires game\Builds\Win64\RadiantPool.exe (scripts\build-all.ps1).

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo "game\Builds\Win64\RadiantPool.exe"
if (-not (Test-Path $exe)) { throw "Build missing: $exe" }

$logDir = Join-Path $env:TEMP "radiantpool-smoke"
New-Item -ItemType Directory -Force $logDir | Out-Null
$hostLog = Join-Path $logDir "host.log"
$clientLog = Join-Path $logDir "client.log"
Remove-Item $hostLog, $clientLog -ErrorAction SilentlyContinue

Write-Host "Starting host instance..."
$hostProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Anna","-autohost","-logFile",$hostLog -PassThru
Start-Sleep -Seconds 12

Write-Host "Starting client instance..."
$clientProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Ben","-autojoin","localhost","-logFile",$clientLog -PassThru
Start-Sleep -Seconds 18

Stop-Process -Id $clientProc.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $hostProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$hostText = Get-Content $hostLog -Raw
$clientText = Get-Content $clientLog -Raw

$checks = @(
    @{ Name = "host started server + invite code"; Ok = $hostText -match "\[RadiantPool\] server started, invite code" },
    @{ Name = "host spawned its own character";    Ok = $hostText -match "character ready: Anna" },
    @{ Name = "client connected and host spawned it"; Ok = $hostText -match "character ready: Ben" },
    @{ Name = "client attempted autojoin";         Ok = $clientText -match "\[RadiantPool\] autojoin" },
    @{ Name = "no NullReference in host log";      Ok = $hostText -notmatch "NullReferenceException" },
    @{ Name = "no NullReference in client log";    Ok = $clientText -notmatch "NullReferenceException" }
)

$failed = 0
foreach ($c in $checks) {
    if ($c.Ok) { Write-Host "  PASS  $($c.Name)" -ForegroundColor Green }
    else { Write-Host "  FAIL  $($c.Name)" -ForegroundColor Red; $failed++ }
}
Write-Host "Logs: $hostLog | $clientLog"
if ($failed -gt 0) { exit 1 }
Write-Host "Smoke test passed - two instances hosted, joined, and spawned characters." -ForegroundColor Green
