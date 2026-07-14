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

# The run gets its OWN campaign file. Without this the test loads - and the self-tests
# below then SAVE OVER - the real campaign in %USERPROFILE%\Saved Games\RadiantPool.
$saveDir = Join-Path $logDir "save"
Remove-Item -Recurse -Force $saveDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $saveDir | Out-Null

Write-Host "Starting host instance..."
# -selltest / -leveltest: the host drives a real sale (bag -> trader -> purse) and a real
# level-up (XP -> level -> ability point spent), and asserts on both below.
$hostProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Anna","-autohost","-selltest","-leveltest","-weapontest","-savedir",$saveDir,"-logFile",$hostLog -PassThru
Start-Sleep -Seconds 12

Write-Host "Starting client instance..."
$clientProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Ben","-autojoin","localhost","-savedir",$saveDir,"-logFile",$clientLog -PassThru
Start-Sleep -Seconds 18

Stop-Process -Id $clientProc.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $hostProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Solo recruitment gets its own fresh session: it reproduces a completed campaign after
# session-owned companions have disappeared, then proves Veresk still offers and spawns 3.
Write-Host "Starting solo recruitment instance (-recruittest)..."
$recruitLog = Join-Path $logDir "recruit.log"
Remove-Item $recruitLog -ErrorAction SilentlyContinue
$recruitSave = Join-Path $logDir "recruitsave"
Remove-Item -Recurse -Force $recruitSave -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $recruitSave | Out-Null
$recruitProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Dara","-autohost","-recruittest","-savedir",$recruitSave,"-logFile",$recruitLog -PassThru
Start-Sleep -Seconds 14
Stop-Process -Id $recruitProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$recruitText = Get-Content $recruitLog -Raw

# -attacktest gets its OWN host: it starts a real encounter, and a fight running underneath
# the sell/level checks would fight them for the turn clock. One click on the FURTHEST enemy
# must both close the distance and land the blow.
Write-Host "Starting combat instance (-attacktest)..."
$fightLog = Join-Path $logDir "fight.log"
Remove-Item $fightLog -ErrorAction SilentlyContinue
$fightSave = Join-Path $logDir "fightsave"
Remove-Item -Recurse -Force $fightSave -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $fightSave | Out-Null
$fightProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Cara","-autohost","-attacktest","-savedir",$fightSave,"-logFile",$fightLog -PassThru
Start-Sleep -Seconds 45
Stop-Process -Id $fightProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$fightText = Get-Content $fightLog -Raw

$hostText = Get-Content $hostLog -Raw
$clientText = Get-Content $clientLog -Raw

$checks = @(
    @{ Name = "host started server + invite code"; Ok = $hostText -match "\[RadiantPool\] server started, invite code" },
    @{ Name = "host spawned its own character";    Ok = $hostText -match "character ready: Anna" },
    @{ Name = "client connected and host spawned it"; Ok = $hostText -match "character ready: Ben" },
    @{ Name = "client attempted autojoin";         Ok = $clientText -match "\[RadiantPool\] autojoin" },
    @{ Name = "no NullReference in host log";      Ok = $hostText -notmatch "NullReferenceException" },
    @{ Name = "no NullReference in client log";    Ok = $clientText -notmatch "NullReferenceException" },
    @{ Name = "selling an item at a trader pays gold"; Ok = $hostText -match "\[SellTest\] PASS - gold" },
    @{ Name = "selling away from a trader is refused"; Ok = $hostText -match "\[SellTest\] PASS - away" },
    @{ Name = "no failed sell assertion";          Ok = $hostText -notmatch "\[SellTest\] FAIL" },
    @{ Name = "earned XP levels the character up"; Ok = $hostText -match "\[LevelTest\] PASS - \d+ XP took" },
    @{ Name = "an ability point can be spent";     Ok = $hostText -match "\[LevelTest\] PASS - spending" },
    @{ Name = "no failed level assertion";         Ok = $hostText -notmatch "\[LevelTest\] FAIL" },
    @{ Name = "player and NPC equipped weapons are visible"; Ok = $hostText -match "\[WeaponTest\] PASS" },
    @{ Name = "no failed weapon visual assertion"; Ok = $hostText -notmatch "\[WeaponTest\] FAIL" },
    @{ Name = "armed combat NPC weapons are visible"; Ok = $fightText -match "\[WeaponTest\] PASS - combat NPCs" },
    @{ Name = "no failed combat weapon assertion"; Ok = $fightText -notmatch "\[WeaponTest\] FAIL" },
    @{ Name = "post-campaign solo player can hire three NPC helpers"; Ok = $recruitText -match "\[RecruitTest\] PASS" },
    @{ Name = "no failed solo recruitment assertion"; Ok = $recruitText -notmatch "\[RecruitTest\] FAIL" },
    @{ Name = "one click on a distant enemy closes in and attacks"; Ok = $fightText -match "\[AttackTest\] PASS" },
    @{ Name = "no failed attack assertion";        Ok = $fightText -notmatch "\[AttackTest\] FAIL" },
    @{ Name = "no NullReference in combat log";    Ok = $fightText -notmatch "NullReferenceException" }
)

$failed = 0
foreach ($c in $checks) {
    if ($c.Ok) { Write-Host "  PASS  $($c.Name)" -ForegroundColor Green }
    else { Write-Host "  FAIL  $($c.Name)" -ForegroundColor Red; $failed++ }
}
Write-Host "Logs: $hostLog | $clientLog | $recruitLog | $fightLog"
if ($failed -gt 0) { exit 1 }
Write-Host "Smoke test passed - two instances hosted, joined, and spawned characters." -ForegroundColor Green
