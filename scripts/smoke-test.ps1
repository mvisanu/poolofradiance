# Two-instance sync smoke test: host + join on one machine, assert via player logs.
# Requires game\Builds\Win64\RadiantPool.exe (scripts\build-all.ps1).

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo "game\Builds\Win64\RadiantPool.exe"
if (-not (Test-Path $exe)) { throw "Build missing: $exe" }

# A playtest left open on 7770 makes the smoke host correctly choose 7771 while the
# special localhost automation path still targets 7770. Close only this game's old
# player before allocating the loopback test port; the refreshed player is launched
# again after the verification workflow.
Get-Process RadiantPool -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

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
# The host drives sale/progression/equipment/atmosphere checks plus a complete monster
# gallery: every rules-library creature must resolve to supported visible renderers, valid
# bounds above ground, and no capsule fallback.
$hostProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Anna","-autohost","-selltest","-leveltest","-weapontest","-atmospheretest","-creaturetest","-questguidancetest","-savedir",$saveDir,"-logFile",$hostLog -PassThru
# Join before the host's delayed self-tests begin mutating scene state. Waiting until all
# gallery/quest checks had run made a late client reconcile against a changed scene.
Start-Sleep -Seconds 4

Write-Host "Starting client instance..."
$clientProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Ben","-autojoin","localhost","-savedir",$saveDir,"-logFile",$clientLog -PassThru
Start-Sleep -Seconds 26

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
Start-Sleep -Seconds 30
Stop-Process -Id $recruitProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$recruitText = Get-Content $recruitLog -Raw

# Restart against the exact campaign written above. Active companions must return with
# the same names, selected classes, level/XP, and individually assigned quest gear.
Write-Host "Starting persistent companion restore instance (-recruitrestoretest)..."
$recruitRestoreLog = Join-Path $logDir "recruit-restore.log"
Remove-Item $recruitRestoreLog -ErrorAction SilentlyContinue
$recruitRestoreProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Dara","-autohost","-recruitrestoretest","-savedir",$recruitSave,"-logFile",$recruitRestoreLog -PassThru
Start-Sleep -Seconds 20
Stop-Process -Id $recruitRestoreProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$recruitRestoreText = Get-Content $recruitRestoreLog -Raw

# A completed pre-Ashen-Ward save must not dead-end on its old finale. Load the exact
# old four-zone shape and prove the appended commission activates automatically.
Write-Host "Starting completed-save migration instance..."
$migrationLog = Join-Path $logDir "migration.log"
Remove-Item $migrationLog -ErrorAction SilentlyContinue
$migrationSave = Join-Path $logDir "migrationsave"
Remove-Item -Recurse -Force $migrationSave -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $migrationSave | Out-Null
$legacy = [ordered]@{
    SchemaVersion = 1; SavedAtUtc = "2026-07-14T00:00:00Z"
    MusterState = 3; PartyGold = 1000
    ZoneStates = @(3, 3, 3, 3); ZoneClearedCounts = @(3, 4, 3, 5)
    CampaignComplete = $true; Stash = @(); ConsumedEncounters = @(); Roster = @()
} | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $migrationSave "campaign.json"), $legacy,
    (New-Object System.Text.UTF8Encoding($false)))
$migrationProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Eira","-autohost","-nextquesttest","-savedir",$migrationSave,"-logFile",$migrationLog -PassThru
Start-Sleep -Seconds 20
Stop-Process -Id $migrationProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$migrationText = Get-Content $migrationLog -Raw

# The Watchers Below gets its own input-path test. It opens the spectral-watch panel
# through CampaignObjectiveInteract.TryInteract (the same method E calls), lets every
# distant objective run Update once, then resolves the authored choice on the server.
Write-Host "Starting site-action input instance (-siteactiontest)..."
$siteActionLog = Join-Path $logDir "site-action.log"
Remove-Item $siteActionLog -ErrorAction SilentlyContinue
$siteActionSave = Join-Path $logDir "siteactionsave"
Remove-Item -Recurse -Force $siteActionSave -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $siteActionSave | Out-Null
$siteActionProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Galen","-autohost","-siteactiontest","-savedir",$siteActionSave,"-logFile",$siteActionLog -PassThru
Start-Sleep -Seconds 14
Stop-Process -Id $siteActionProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$siteActionText = Get-Content $siteActionLog -Raw

# Campaign waystones move the whole party through a server-validated route. Keep this
# isolated because it deliberately teleports the host out of, and back into, the hub.
Write-Host "Starting campaign travel instance (-traveltest)..."
$travelLog = Join-Path $logDir "travel.log"
Remove-Item $travelLog -ErrorAction SilentlyContinue
$travelSave = Join-Path $logDir "travelsave"
Remove-Item -Recurse -Force $travelSave -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $travelSave | Out-Null
$travelProc = Start-Process $exe -ArgumentList "-batchmode","-nographics","-name","Fara","-autohost","-traveltest","-savedir",$travelSave,"-logFile",$travelLog -PassThru
Start-Sleep -Seconds 22
Stop-Process -Id $travelProc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$travelText = Get-Content $travelLog -Raw

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
    @{ Name = "client received the synchronized world clock"; Ok = $clientText -match "\[Atmosphere\] client clock synchronized" },
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
    @{ Name = "day/night lighting, fog, lamps, clock, and ambience transition"; Ok = $hostText -match "\[AtmosphereTest\] PASS" },
    @{ Name = "no failed atmosphere assertion"; Ok = $hostText -notmatch "\[AtmosphereTest\] FAIL" },
    @{ Name = "quest turn-in guidance points to Council Hall"; Ok = $hostText -match "\[QuestGuidanceTest\] PASS" },
    @{ Name = "no failed quest-guidance assertion"; Ok = $hostText -notmatch "\[QuestGuidanceTest\] FAIL" },
    @{ Name = "every creature mapping produces a visible real model"; Ok = $hostText -match "\[CreatureTest\] PASS - all creature visuals 11/11 visible, mappings 11/11" },
    @{ Name = "no failed creature visual or capsule fallback"; Ok = $hostText -notmatch "\[CreatureTest\] FAIL|falling back to a capsule" },
    @{ Name = "armed combat NPC weapons are visible"; Ok = $fightText -match "\[WeaponTest\] PASS - combat NPCs" },
    @{ Name = "no failed combat weapon assertion"; Ok = $fightText -notmatch "\[WeaponTest\] FAIL" },
    @{ Name = "player explicitly chooses tank, healer, and damage companions"; Ok = $recruitText -match "\[RecruitTest\] PASS - chose tank/healer/damage" },
    @{ Name = "all three hires match leader level and equipment tier"; Ok = $recruitText -match "\[RecruitParityTest\] PASS" },
    @{ Name = "quest gear survives companion release and rehire"; Ok = $recruitText -match "\[RecruitPersistenceTest\] PASS" },
    @{ Name = "active named companions restore in a new game process"; Ok = $recruitRestoreText -match "\[RecruitRestoreTest\] PASS" },
    @{ Name = "no failed solo recruitment assertion"; Ok = $recruitText -notmatch "\[RecruitTest\] FAIL" },
    @{ Name = "persistent companion restore has no runtime exception"; Ok = $recruitRestoreText -notmatch "Exception|\[RecruitRestoreTest\] FAIL" },
    @{ Name = "completed four-zone save unlocks the next quest"; Ok = $migrationText -match "\[CampaignMigration\] PASS.+Beyond the Lightwell" },
    @{ Name = "new quest has a live Ashen Ward waypoint"; Ok = $migrationText -match "\[NextQuestWaypointTest\] PASS.+target 'The Ashen Ward'" },
    @{ Name = "save migration has no runtime exception"; Ok = $migrationText -notmatch "Exception|\[CampaignMigration\] FAIL" },
    @{ Name = "Watcher Below E panel survives all distant objective updates"; Ok = $siteActionText -match "\[SiteActionInputTest\] PASS.+E opened the spectral-watch choice.+decision recorded as 'Honor their oath'" },
    @{ Name = "Watcher Below site action has no runtime exception"; Ok = $siteActionText -notmatch "Exception|\[SiteActionInputTest\] FAIL" },
    @{ Name = "campaign reaches every site and pays side/main quest rewards"; Ok = $travelText -match "\[TravelTest\] PASS - 27/27 sites reached; 27/27 encounter sets authored; 27/27 objectives anchored; site objective resolved; side/main rewards paid; 27/27 hub returns" },
    @{ Name = "campaign travel has no runtime exception"; Ok = $travelText -notmatch "Exception|\[TravelTest\] FAIL" },
    @{ Name = "one click on a distant enemy closes in and attacks"; Ok = $fightText -match "\[AttackTest\] PASS" },
    @{ Name = "no failed attack assertion";        Ok = $fightText -notmatch "\[AttackTest\] FAIL" },
    @{ Name = "combat attack produces graphics and sound feedback"; Ok = $fightText -match "presentation FX/SFX" },
    @{ Name = "licensed exploration and battle music are active"; Ok = $fightText -match "\[CombatAudioTest\] PASS.+Action RPG battle track.+Caves and Dungeons 5/5 zones" },
    @{ Name = "realistic weapon sounds are used"; Ok = $fightText -match "\[CombatAudioTest\] PASS.+licensed weapon SFX played" },
    @{ Name = "spell cast and impact sounds are used"; Ok = $fightText -match "\[SpellAudioTest\] PASS.+fire cast \+ impact" },
    @{ Name = "combat light covers every living unit"; Ok = $fightText -match "\[CombatLightTest\] PASS" },
    @{ Name = "no NullReference in combat log";    Ok = $fightText -notmatch "NullReferenceException" }
)

$failed = 0
foreach ($c in $checks) {
    if ($c.Ok) { Write-Host "  PASS  $($c.Name)" -ForegroundColor Green }
    else { Write-Host "  FAIL  $($c.Name)" -ForegroundColor Red; $failed++ }
}
Write-Host "Logs: $hostLog | $clientLog | $recruitLog | $recruitRestoreLog | $migrationLog | $siteActionLog | $travelLog | $fightLog"
if ($failed -gt 0) { exit 1 }
Write-Host "Smoke test passed - two instances hosted, joined, and spawned characters." -ForegroundColor Green
