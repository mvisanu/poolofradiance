I have an existing Unity game that I want you to upgrade and convert to a 
web-based (WebGL) game. You must retain ALL current gameplay functionality 
and keep using my existing assets — do not replace, delete, or substitute 
any of my assets, scripts, scenes, or prefabs.

Project details (fill these in):
- Current Unity version: [e.g., 2021.3]
- Target Unity version: Unity 6 (latest LTS)
- Render pipeline: [Built-in / URP / HDRP]
- Game type: [e.g., 3D platformer, top-down shooter, puzzle game]
- Current platform target: [PC / Android / iOS]
- Plugins/packages in use: [list them, e.g., Cinemachine, DOTween, 
  Mirror, Firebase]

TASK 1 — Upgrade to latest Unity:
1. Guide me through safely upgrading the project to Unity 6 
   (backup first, then open in new version).
2. Fix any API deprecations, obsolete script calls, or package 
   incompatibilities that appear after the upgrade.
3. If I'm on HDRP, migrate me to URP (WebGL requirement) while keeping 
   my materials and lighting looking as close as possible to current.
4. Update all packages to versions compatible with Unity 6.

TASK 2 — Convert to WebGL while retaining functionality:
1. Audit my scripts for WebGL-incompatible code and fix each one while 
   preserving behavior:
   - System.Threading / multithreading → convert to coroutines or 
     async patterns that work in WebGL
   - System.IO file read/write → convert to PlayerPrefs or IndexedDB-safe 
     persistence, keeping the same save/load behavior
   - Raw sockets/TCP → convert to WebSockets or UnityWebRequest, 
     keeping the same networking behavior
   - Native plugins/DLLs → identify web-compatible replacements
2. Keep all my input working: if I use old Input Manager, either keep it 
   or migrate cleanly to the new Input System, and add touch/browser 
   input support without breaking existing controls.
3. Preserve all game logic, UI flows, audio, and scene transitions exactly 
   as they work now.

TASK 3 — Optimize for web:
1. Configure WebGL Player Settings: Brotli compression, appropriate 
   memory size, code stripping level that doesn't break my scripts 
   (check for reflection usage first).
2. Optimize my existing assets for web WITHOUT replacing them: 
   texture compression settings, audio compression, mesh compression, 
   mipmaps — while keeping visual quality close to current.
3. Set up Addressables or asset bundles if the build exceeds ~200MB 
   so content streams instead of one giant download.
4. Ensure post-processing and quality settings are tuned so the game 
   runs at a stable framerate in Chrome/Firefox/Safari.

TASK 4 — Build and verify:
1. Produce a working WebGL build.
2. Give me a checklist to verify every feature works in-browser 
   (saves, audio, input, networking, UI).
3. Tell me how to host/test it locally and options to deploy 
   (itch.io, Unity Play, Netlify).

Rules:
- Never delete or replace my assets, scenes, or prefabs.
- Any script you change: preserve its exact gameplay behavior; explain 
  each change and why WebGL requires it.
- Work incrementally: after each task, stop and let me test before 
  continuing.
- If something CANNOT work on WebGL (e.g., a native-only plugin), 
  do not silently remove it — flag it and propose the closest 
  web-compatible alternative for my approval.