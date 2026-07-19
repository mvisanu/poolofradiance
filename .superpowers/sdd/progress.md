# SDD Progress — mcp-blender.md (graphics realism + MCP setup)

## NEW GOAL (Jul 19 ~8am): OPEN WORLD
User goal: build an open world — walk overland from town to town; take down walls/fences
where appropriate; environment more wooded and open; DO NOT break existing functionality
(waystones, quests, encounters, saves, smoke gate must stay green).
Pipeline: /handoff (Claude researches/designs, Codex implements, controller verifies).
- Phase 1 Research: two Explore agents dispatched (world layout/barriers; travel/zone-gating runtime).
- Research report A (travel/zone runtime) COMPLETE. Key facts: world = isolated 48x48 arena cells
  on ~50u grid, coords (-150..-150) to (100,260); town = 120x120 slab walled at +-60
  (ProjectBootstrap.cs:915-918); every remote site fenced by invisible SiteEdgeN/S/E/W colliders
  (PB:1682-1688); VOID between cells (no ground). No current-zone concept — everything is radius
  checks (TownRegenRadius 32 around Council Hall, TraderNear, minimap nearest-destination-45m).
  ZoneGate physical walls only for town zones 1/3/4. Remote zone locks enforced ONLY by CmdTravelTo
  refusal (GameDirector.cs:799-808). EncounterTrigger ignores zone lock state (deliberate:
  fight-before-accept supported, GameDirector.cs:488-505). NO kill-Z/fall recovery
  (PlayerMotor.cs:61-88, unbounded fall). Defeat respawn = town shrine (-9,0.15,-14). Save stores
  NO player position (safe). CmdTravelTo teleports the whole party (party-split risk if mixed
  walk/teleport). Risks list: connective ground needed, edge colliders removal, kill-plane needed,
  locked-zone overworld access = overleveled fights, wayfinding points across void (cosmetic).
- Research report B (world gen) COMPLETE: hub ground 120x120 plane +-60; site Centers are
  HAND-AUTHORED literals in CampaignExpansionContent.Sites (not derived) so relayout = edit
  the Vector3s; walls built by Box() helper keep colliders; necropolis ring + all fences are
  collider-free decoration; DressWorld is hub-centric (bands +-52, highlands 64-72, horizon
  cliffs 78-86); no NavMesh; PlayerMotor WASD speed 6/8.5 gravity -20.
- DESIGN v1 presented; user chose REWORK: bigger/more organic world, remove MORE walls
  (town dividers + ZoneGates too), organic winding roads/forests. Danger zones: OPEN DANGER
  approved (no trigger gating).
- DESIGN v2 APPROVED by user: "the atlas is the world" — site centers projected from
  MiniMap atlas coords (scale 900, Council Hall origin, atlas-north=+Z); rolling Perlin
  terrain flattened at town/sites/roads (flat y=-0.04 vs slabs at 0 to avoid z-fighting);
  fully open town (hub walls, market/temple dividers, all 3 ZoneGates, all SiteEdge cages
  removed); winding roads per region MST + town spur; world rim cliffs + invisible
  WorldEdge colliders as only barrier; kill-Z warp to town shrine; [OpenWorld] PASS
  bootstrap validator. Waystones/rules/saves untouched.
- Task A (geography/terrain/demolition/safety) brief: codex/codex-openworld-a-brief.md.
  Coordinate table computed from atlas (min pair 70+, nearest site 193 from origin,
  extent X -315..468 Z -392..324; winter_crown_vault nudged to 432,168). DISPATCHED to
  Codex (background, workspace-write, no-commit). Task B (visual roads + region-flavored
  wilderness dressing + rim cliffs + hub backdrop rework) briefed AFTER A returns.
- Task A RETURNED (report codex/codex-openworld-a-report.md): spec-faithful, no constraint
  violations. NOTE: a PARALLEL session is working this tree concurrently (TargetFrameTest in
  GameDirector/CombatClientUI/QuestTracker/smoke-test + regenerated scene/controllers) —
  Codex preserved it; MY COMMITS MUST SCOPE to open-world files only.
  Controller gates: compile-check 0 errors, rules 166/166. Bootstrap run 1: exit 0 but
  [OpenWorld] FAIL 1242/1244 — town roads started at Euclidean radius 58 = inside the town
  square; Stormglass road crossed an Ashen Ward rooftop at (52.2,46.4). Controller applied
  the allowed micro-fix (town road start = Chebyshev-66 boundary projection in
  OpenWorld.RoadPolylines). Bootstrap run 2 in flight (logs/boot-openworld2.log).
- Task A bootstrap run 2: [OpenWorld] PASS walkable samples 1207/1207, sites 34. Win64 build
  dispatched (logs/build-openworld.log). Task B DISPATCHED to Codex concurrently (source-only,
  no Unity lock conflict; logs/codex-openworld-b.log).
- Task A ACCEPTED: build exit 0 (Assembly-CSharp.dll 8:42:54). Interim visual check
  codex/openworld_a_necropolis.png vs baseline necropolis_after.png: site renders
  identically (violet corrupted theme is INTENDED); background now open green wilderness
  (undressed until Task B). NOTE: new capture's player frame shows only hp bar (no
  level/100% text) — likely the PARALLEL session's in-flight CombatClientUI edits, not
  open-world scope; do not touch.
- Task B RETURNED (codex/codex-openworld-b-report.md): OpenWorldDressing.cs (roads/scatter/
  rim), ProjectBootstrap hook + ring removals, OpenWorld.Regions made public (permitted +
  reported). Controller diff review clean; determinism verified (RNG draws before exclusion
  checks). Codex sandbox blocked its Unity run (licensing IPC) — bootstrap gate is the
  controller's (boot-openworld3.log, in flight). Then: build, 3x sitecapture review, smoke.
- Task B GATES: bootstrap exit 0 with [OpenWorldDressing] PASS props 1522 (trees 925,
  scatter 596, graves 1; cap raised threshold 0.56->0.65 from 2659) AND [OpenWorld] PASS
  1207/1207 post-dressing. Build exit 0 (level0 refreshed 8:58:29; Assembly-CSharp
  unchanged = editor-only diff, expected). Sitecaptures reviewed: spire = burnt sparse +
  visible dirt roads; blackbriar = lush woods horizon + road + ambush encounter fires
  correctly; necropolis unregressed. Smoke gate running (logs/smoke-openworld.log).
  NOTE smoke-test.ps1 carries the parallel session's +2 lines (TargetFrameTest?) — if smoke
  fails ONLY on that assertion it is the parallel workstream's, not open-world's.
- COMMIT cf6b04b: full open world (sources + generated scene/assets + codex records),
  scoped AWAY from parallel session's uncommitted HUD files. Smoke gate on open world:
  68/68 PASS, 0 FAIL.
- Codex second-pass review verdict NEEDS-FIXES, adjudicated: Imp1 (town endpoints deviate
  from brief) = controller's validated micro-fix, brief REVISED instead (documented in
  codex-openworld-a-brief.md); Imp2 (validator accepts non-ground raycast hit as ground)
  = REAL, fix delegated to Codex (codex/codex-openworld-fix-brief.md — note: codex exec
  prompts with embedded double quotes or angle brackets get mangled by the PS5.1 npm shim
  and fail exit 2 with arg errors; use brief FILES + quote-free prompts); Min1 (terrain
  chunks overhang past north rim) = ACCEPTED as horizon backdrop, negligible cost;
  Min2 (em dash in comment) = fixed by controller.
- Task B brief written: codex/codex-openworld-b-brief.md (road ribbon meshes y=-0.02 +
  M_WildRoad dirt mat, Perlin forest/meadow scatter with region flavor + collider stripping,
  1600 prop cap + [OpenWorldDressing] PASS log, rim mountains, remove old horizon/highlands
  rings from DressWorld). Dispatch after Task A bootstrap+build pass.

Goal: complete mcp-blender.md via subagents (goal hook active, autonomous run).

Plan tasks:
1. Blender MCP bridge: install blender-mcp addon into Blender 5.1, run persistent server on 9876, verify from Claude (create cube).
2. Unity MCP research: pick most active Unity-6-compatible MCP connector, produce install/registration/verify procedure.
3. Unity MCP install: UPM package + Claude Code registration + connection verify (per Task 2 report).
4. Realism audit (read-only): lighting/post/materials/meshes inventory from ProjectBootstrap/WorldAtmosphere/settings; prioritized gap list.
5. Graphics upgrade implementation (code-driven via ProjectBootstrap/WorldAtmosphere, never hand-edit scene): lighting, post stack, PBR materials; keep WebGL budget.
6. WebGL reality check + final whole-branch review.

NEW USER GOAL (5:5x pm): camera never auto-adjusts (no combat assist, no collision pull-in), manual pan/zoom everywhere incl. combat, blockers fade see-through. = Task 7.

Ledger:
- Task 7 (manual camera + see-through blockers): implemented, commit fcb26d6 (base 7ab4bd8), compile-check clean + 166/166 rules tests. Task 7: complete (commits fcb26d6+5f099c7, base 7ab4bd8, re-review clean: spec ✅ / quality Approved). Live -attacktest/smoke confirmation pending on the shared build+smoke gate after Task 5.
- NOTE: a PARALLEL session works this repo — commit 7ab4bd8 (settle combat camera on gliders, regen scene, thin haze) + the 5:43p WebGL build were its work. WebGL build exited ~6:0x pm; Unity lock FREE. Check for foreign Unity/game processes before every Unity run.
- Baseline checkpoint commit 92e5f7a (prior session work: regen, camera fix, relight) — task diffs start here.
- Design direction (frontend-design + ui-ux-pro-max): warm adventure-gold key light vs cool teal-blue complement, ACES filmic grade, restrained bloom/vignette; signature = golden-hour sun. UI stays Academia/Gilded Quest theme.
- Task 1 (Blender bridge): COMPLETE. blender-mcp addon installed in Blender 5.1, server on 9876. Controller verified MCP round-trip: get_scene_info OK + created MCP_Test_Cube via execute_blender_code. Relaunch: Start-Process blender.exe --python scratchpad\start_mcp_server.py -WindowStyle Minimized.
- Task 2 (Unity MCP research): COMPLETE. Pick = CoplayDev/unity-mcp (MCP for Unity), batchmode-capable (UNITY_MCP_ALLOW_BATCH=1 / McpCiBoot.StartStdioForCi). UPM: https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main. Full report: scratchpad task-2-report.md.
- Task 3 (Unity MCP install): IN FLIGHT, self-paused behind a live external Unity WebGL build (PID 71076, started 5:43p, holds project lock; do NOT kill — it exits on completion). Agent has a poller and resumes itself.
- Task 4 (realism audit): COMPLETE. Report: scratchpad task-4-report.md. Top gaps P1 probes/ambient, P2 flat kit materials, P3 smoothness flattening, P4 sky, P5 water, P6 web SSAO, P7 wind (deferred), P8 shadow quality. "Already good": ACES/post stack/day-night/web build path — don't churn.
- Task 5 (graphics implementation): brief ready (scratchpad task-5-brief.md); DO NOT DISPATCH until Unity lock free (Task 3 done) — bootstrap/build needs exclusive Unity.
- NEW USER GOAL (Jul 19 ~3:3x am): install Codex plugin for Claude Code. DONE: marketplace openai-codex added, plugin codex@openai-codex v1.0.6 enabled, codex-cli 0.144.6 installed via npm, already logged in (ChatGPT). /reload-plugins + /codex:setup need the user's session reload; CLI verified headless (codex exec, model gpt-5.6-sol). New division of labor per updated CLAUDE.md: Claude plans/reviews, Codex codes.
- Task 3 (Unity MCP install): agent LOST at compaction. State: manifest.json has com.coplaydev.unity-mcp (+packages-lock resolve), both UNCOMMITTED; NO .mcp.json yet. Registration needs the in-editor Setup Wizard or resident batchmode editor (McpCiBoot.StartStdioForCi + UNITY_MCP_ALLOW_BATCH=1, per task-2-report) — deferred until after Task 8/5 Unity gates (lock conflict).
- Task 8 (WoW HUD): DELEGATED TO CODEX (codex exec background, brief copied to repo root codex-task-8-brief.md, report codex-task-8-report.md, Unity verification steps reserved for controller). Task 5 dispatch waits for Task 8 (serial implementers + Unity lock).
- WORKFLOW SWITCH (user, Jul 19): all delegation now follows .claude/commands/handoff.md — Claude researches/designs/verifies, Codex implements via /codex:rescue; /codex:review second pass after each accepted task. Plugins reloaded, /codex:* commands live.
- Task 5: design APPROVED by user (as designed). Brief now at codex/codex-task-5-brief.md + audit codex/codex-task-4-audit.md (user goal moved codex-*.* to codex/; *.log live in logs/). Dispatch to Codex AFTER Task 8 smoke gate (serial: working-tree contamination + Unity lock).
- FINAL STATE (Jul 19 ~5am): ALL TASKS COMPLETE. Smoke 66/66 green on the graphics build. Final whole-branch Codex review (92e5f7a..HEAD) returned 1 Important (quest card zero-height overflow — fixed c281add) + 2 Minor docs (both fixed in CLAUDE.md, c281add). Task 3 DONE: .mcp.json (uv run sparse clone at ..\unity-mcp-server, entry mcp-for-unity), bridge verified batchmode (StdioBridgeHost port 6400), commit ebf0e0f; unity-mcp tools appear after Claude Code restart. Regen assets + delegation records committed 178bc50. Final rebuild dispatched so the shipped exe matches HEAD.
- Task 5: implemented by Codex, reviewed, committed 03f57f7 (incl. Task 6 _hd assets). REGRESSION found in capture review: salmon-pink arena grounds — ForTheme's FindAssets substring match picked the generated N_*.png normal as albedo; controller fixed (exact-name + Generated-excluded, commit 60313ff). Re-bootstrap healed the .mats (SetTexture re-binds); ember + necropolis captures verified CORRECT (authored corrupted-grass textures, PBR props, no pink/magenta). GOTCHA: -sitecapture with no -sitezone defaults to ember_crown_spire (GameDirector.cs:2305) and works on a fresh -savedir. Task 8 fixes-2 committed 07e73b8. Final smoke gate running.
- Task 8: COMPLETE. Commits 9ab5e15/8f58f28/9f35be4. Smoke re-run 100% green (run-1 attacktest stall = 13-instance load flake). Rebuild 4:24:31 + recapture: XP strip label legible, all combat assertions PASS. Codex second-pass review dispatched (read-only, background). Task 5 DISPATCHED to Codex (background; Unity steps reserved for controller).
- Task 8 VERIFICATION: bootstrap clean, build fresh (3:54:40), [UiSkinTest] PASS 26/26, [CombatUiTest]/[CombatCameraTest]/[MonsterHudTest]/[AttackTest] all PASS standalone; hud_combat.png + hud_title.png reviewed = WoW layout confirmed. GOTCHA: -combatuicapture requires -attacktest on the SAME command line (flag is read inside AttackSelfTest; alone the game idles forever). Smoke run 1: 12 FAILs, all one stalled -attacktest instance ('fight never reached my turn' under 13-instance load; standalone passed) — re-running to test flake. Fix batch (DOWN indicator, party-scan cache, XP label unclip) implemented by Codex, reviewed, committed 9f35be4; needs rebuild before recapture. Task 8 commits: 9ab5e15, 8f58f28, 9f35be4.
- Task 8 CODEX RESULT: code complete, sandbox blocked its git commit (read-only .git) — controller committed: 9ab5e15 (/handoff command) + 8f58f28 (Task 8 HUD + CLAUDE.md incl. division-of-labor section). Controller verified compile-check 0 errors + rules 166/166. Diff review findings for fix loop: (a) player DOWN/death-save indicator lost with old DrawMyCard/DrawHealth — new frame shows only 0/max 0%; (b) out-of-combat party frames call FindObjectsByType every OnGUI — cache on slow poll. Unity gate in progress: bootstrap → build → -combatuicapture/-uiskincapture (visual review) → smoke-test. Fix delegation to Codex = (a)+(b)+capture findings in ONE batch.
- Task 6 (Blender beast _hd meshes): COMPLETE, controller-verified. Bear_hd.fbx (3808→13872 tris), Rat_hd.fbx (2194→9488), previews confirm smooth vs faceted originals; originals untouched; no armature existed. Files uncommitted — commit with Task 5 (GeneratedArt _hd wiring is in Task 5 scope). Report: scratchpad task-6-report.md.
