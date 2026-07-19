# Task 5 — Graphics realism upgrade implementation (mcp-blender.md Phase 3, scoped from audit)

## Context
Radiant Pool, Unity 6000.0.79f1 URP, repo C:\Users\Bruce\source\repo\poolofradiance.
The scene is REGENERATED FROM CODE by `game/Assets/Editor/ProjectBootstrap.cs` —
never hand-edit the scene; every change goes into bootstrap/runtime code so it
survives regeneration. Desktop (Win64) + WebGL are both shipped; WebGL is the
binding perf budget. Read the audit FIRST — it has exact file/line targets,
current values, and a "don't churn" list you must respect:
`task-4-report.md` in this same scratchpad directory.

## Design direction (binding)
Grounded painted-fantasy realism, not photoreal. Signature = warm golden-hour
key light (sun) against cool teal-blue sky/shadow complements. Restraint
everywhere else: the existing ACES tonemap, bloom, vignette, +22/+20 grade, and
the whole WorldAtmosphere day/night system are deliberate and stay as they are
unless a scoped item below says otherwise. Do not touch UI theme/fonts.

## Scope — implement exactly these items (audit P-numbers)
1. **P3 (S)**: `PolyPackArt.SetupMaterials()` — stop hard-coding `_Smoothness 0.08`
   for RpgPoly/SimpleNature/Dungeon; extend the GraveyardNature
   clamp-and-preserve pattern (`Mathf.Clamp(source, 0.08f, 0.65f)`) to all sources.
2. **P2 (M)**: per-surface-type smoothness in the CC0 kit importers
   (`KenneyArt.cs`, `KayKitArt.cs`, `QuaterniusArt.cs`): replace the flat
   `_Smoothness 0.18` with a small named table by surface kind — metal/weapon
   ~0.5-0.65, skin/fur ~0.2-0.3, cloth/banner ~0.08-0.15, wood/stone ~0.15-0.25,
   water stays its own case. Classify by material/mesh/atlas name words (the
   project's bucket-by-name-words pattern in PolyPackArt is the precedent).
   Do NOT generate Sobel/derived normal maps for the FLAT-COLOR kit atlases
   (Kenney/KayKit palette atlases have no luminance detail — deriving normals
   there produces edge artifacts). Derived normals are for item 4 only.
3. **P1 (M)**: scripted reflection probe(s) for specular plausibility. Add to
   bootstrap scene generation: one large box-projected ReflectionProbe covering
   the hub (water/market), importance-weighted; plus WorldAtmosphere refreshing
   it ONCE at scene load via `RenderProbe()` (and optionally on large
   time-of-day changes at most once per in-game hour — never per frame). If
   scripted realtime refresh proves unreliable in URP builds, fall back to an
   editor-baked custom cubemap generated at bootstrap time and assigned as the
   probe's baked texture. Probes are cheap on WebGL (static cubemap sample) —
   ship on both platforms. NO light-probe groups (useless without baked GI —
   out of scope).
4. **Ground + water normal maps (P5 + ground half of P2)**:
   - Ground: `ProjectBootstrap.GroundTexture()` already builds a value-noise
     heightfield — also emit a normal map PNG derived from that same height
     data, wire `_BumpMap` + `EnableKeyword("_NORMALMAP")` on `M_Ground`
     (modest `_BumpScale`, ~0.6). Same for `HandpaintedGroundArt.cs`: if the
     texture pack ships normal maps, wire them; else derive one from the albedo
     luminance (this pack has real luminance detail, unlike the flat kits).
   - Water: give `Mat()`'s water branch a generated ripple normal map
     (procedural, ~256px, tileable), use URP/Lit `_BumpMap` + `_DetailNormalMap`
     at different tilings, and a tiny runtime scroller (offset animation on the
     two normal slots at different speeds/directions — a small MonoBehaviour or
     a WorldAtmosphere tick; respect Reduced Motion setting if one exists for
     world FX). Slightly raise water smoothness (~0.75) so the new probe
     reflections read. NO planar reflections, NO depth foam (WebGL budget).
5. **P4 (M)**: sky cloud layer, code-native: a generated tileable cloud-noise
   texture on a large slowly-rotating dome/disc high above the world (unlit
   transparent, subtle alpha), OR a second additive pass on the existing sky
   approach — whichever is simpler and WebGL-safe (single texture lookup, no
   raymarching). It must respect WorldAtmosphere's time-of-day tinting (clouds
   warm at dawn/dusk, near-white at noon, dark at night) and Reduced Motion
   (static when reduced). Keep it SUBTLE — painted-fantasy, not overcast.
6. **P6 (S)**: enable SSAO on the web renderer too, at a cheaper tuning
   (downsample true, intensity ~0.4, radius ~0.25) via the existing
   `EnsureSsaoFeature` path (`PipelineVariant` web call flips `ssao:false` →
   cheap-true). Serialized URP field names in that file were verified against
   package source — do NOT guess new ones; reuse the existing helper.
7. **P8 (S)**: explicitly set soft-shadow quality high on the desktop pipeline
   asset (web keeps default/low) while you're in `PipelineVariant()`.
8. **`GeneratedArt.cs` `_hd` preference (S)**: a parallel task is producing
   `Bear_hd.fbx`/`Rat_hd.fbx` next to `game/Assets/Art/Generated/Bear.fbx`/
   `Rat.fbx`. Make GeneratedArt prefer `<Name>_hd.fbx` when it exists, falling
   back to `<Name>.fbx` — so the user can delete the _hd file to revert. The
   files may NOT exist yet when you work — the code must handle both cases and
   you must verify the fallback path still works.

## Explicitly OUT of scope (do not implement)
- Vegetation wind/sway (P7) — deferred, needs Shader Graph work; leave a note.
- Any lightmap/baked-GI workflow, light probe groups.
- Any change to tonemapping/bloom/vignette/color grade/white balance values.
- Runtime pipeline swapping (HARD RULE — see WebQuality.cs comments), the
  WebGL build path in HeadlessBuild.cs (except nothing needed there), UI/theme.

## Hard project rules (violating these has cost hours before)
- Materials/shaders used at runtime must be assets under `Resources/` or
  referenced by scene objects (`Shader.Find` fails in builds for unreferenced
  shaders). Prefer URP/Lit + keywords over new custom shaders; if you must add
  a shader, reference it from a material asset the scene uses.
- `compile-check.ps1` does NOT compile `Assets/Editor` — after editing editor
  files you MUST run the bootstrap to prove they compile.
- Unity batchmode: ABSOLUTE `-projectPath`, `Start-Process -PassThru -Wait`,
  never `&`; close any running RadiantPool.exe first; only one Unity instance.
- Test/capture exe runs never self-quit: bounded sleep + kill pattern, never
  `-Wait` on the game exe; use `-savedir <tmp>` to keep off the real campaign.
- Unity fake-null defeats `??`/`?.` on GetComponent — explicit `== null`.
- PS 5.1 mojibake: use [System.IO.File] APIs or the Write/Edit tools for file
  content; keep git commit messages free of double quotes (or use Bash tool).
- No dingbat glyphs in any UI string; generated textures over font glyphs.

## Verification (required, in order)
1. `scripts/compile-check.ps1` — zero errors.
2. `dotnet test rules/RadiantPool.Rules.sln` — must stay green (you shouldn't
   touch rules; this proves it).
3. Bootstrap: `Start-Process -Wait "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe" @('-batchmode','-quit','-projectPath','C:\Users\Bruce\source\repo\poolofradiance\game','-executeMethod','RadiantPool.EditorTools.ProjectBootstrap.Run','-logFile','C:\Users\Bruce\source\repo\poolofradiance\boot.log')`
   — check boot.log for errors; the log must show the world-map `[WorldMap] PASS`.
4. Win64 build via `RadiantPool.EditorTools.HeadlessBuild.Win64` (same pattern,
   build.log; verify freshness via RadiantPool_Data/Managed/Assembly-CSharp.dll
   timestamp, NOT the exe timestamp).
5. Visual QA captures (bounded-run pattern, -savedir temp):
   `game/Builds/Win64/RadiantPool.exe -name QA -autohost -sitecapture <scratchpad>\hub_after.png -savedir <scratchpad>\qa-save`
   and one remote site: `-sitecapture <scratchpad>\necropolis_after.png -sitezone lanternfall_necropolis`.
   LOOK at the captures (Read tool) and self-review: golden-hour key + teal
   complement present, water ripples/reflection reading, clouds subtle, no
   magenta/black materials, no regression. Never send input to any window.
6. `scripts/smoke-test.ps1` — full gate must pass.
7. Commit ONLY files this task touched, message
   `Graphics realism pass: probes, PBR smoothness, ground/water normals, sky clouds, web SSAO`
   with a short body and trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

## Report
Write the full report to `task-5-report.md` in this scratchpad directory: per-item
what changed (file/function), verification evidence per step (test counts, log
lines, capture assessments), any fallbacks taken, deferred-P7 note, WebGL cost
notes per mcp-blender.md Phase 4. Final message ONLY: STATUS, commit hash(es),
one-line summary of capture self-review, any concerns.
