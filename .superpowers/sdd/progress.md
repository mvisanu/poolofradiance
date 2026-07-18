# SDD Progress — mcp-blender.md (graphics realism + MCP setup)

Goal: complete mcp-blender.md via subagents (goal hook active, autonomous run).

Plan tasks:
1. Blender MCP bridge: install blender-mcp addon into Blender 5.1, run persistent server on 9876, verify from Claude (create cube).
2. Unity MCP research: pick most active Unity-6-compatible MCP connector, produce install/registration/verify procedure.
3. Unity MCP install: UPM package + Claude Code registration + connection verify (per Task 2 report).
4. Realism audit (read-only): lighting/post/materials/meshes inventory from ProjectBootstrap/WorldAtmosphere/settings; prioritized gap list.
5. Graphics upgrade implementation (code-driven via ProjectBootstrap/WorldAtmosphere, never hand-edit scene): lighting, post stack, PBR materials; keep WebGL budget.
6. WebGL reality check + final whole-branch review.

Ledger:
- Baseline checkpoint commit 92e5f7a (prior session work: regen, camera fix, relight) — task diffs start here.
- Design direction (frontend-design + ui-ux-pro-max): warm adventure-gold key light vs cool teal-blue complement, ACES filmic grade, restrained bloom/vignette; signature = golden-hour sun. UI stays Academia/Gilded Quest theme.
- Task 1 (Blender bridge): COMPLETE. blender-mcp addon installed in Blender 5.1, server on 9876. Controller verified MCP round-trip: get_scene_info OK + created MCP_Test_Cube via execute_blender_code. Relaunch: Start-Process blender.exe --python scratchpad\start_mcp_server.py -WindowStyle Minimized.
- Task 2 (Unity MCP research): COMPLETE. Pick = CoplayDev/unity-mcp (MCP for Unity), batchmode-capable (UNITY_MCP_ALLOW_BATCH=1 / McpCiBoot.StartStdioForCi). UPM: https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main. Full report: scratchpad task-2-report.md.
- Task 3 (Unity MCP install): IN FLIGHT, self-paused behind a live external Unity WebGL build (PID 71076, started 5:43p, holds project lock; do NOT kill — it exits on completion). Agent has a poller and resumes itself.
- Task 4 (realism audit): COMPLETE. Report: scratchpad task-4-report.md. Top gaps P1 probes/ambient, P2 flat kit materials, P3 smoothness flattening, P4 sky, P5 water, P6 web SSAO, P7 wind (deferred), P8 shadow quality. "Already good": ACES/post stack/day-night/web build path — don't churn.
- Task 5 (graphics implementation): brief ready (scratchpad task-5-brief.md); DO NOT DISPATCH until Unity lock free (Task 3 done) — bootstrap/build needs exclusive Unity.
- Task 6 (Blender beast _hd meshes): COMPLETE, controller-verified. Bear_hd.fbx (3808→13872 tris), Rat_hd.fbx (2194→9488), previews confirm smooth vs faceted originals; originals untouched; no armature existed. Files uncommitted — commit with Task 5 (GeneratedArt _hd wiring is in Task 5 scope). Report: scratchpad task-6-report.md.
