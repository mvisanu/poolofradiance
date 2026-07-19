# Task 5 — Graphics realism upgrade report

Status: implementation complete on branch `ui/combat-health-grouping`; changes are left uncommitted as directed.

## Scoped implementation

1. **P3 — authored PolyPack smoothness preserved**
   - `game/Assets/Editor/PolyPackArt.cs`, `SetupMaterials()`
   - All four pack sources now preserve serialized `_Glossiness` and clamp it to `0.08–0.65`. The former `0.08` override for RpgPoly, SimpleNature, and Dungeon is gone. Existing base/normal/metallic/occlusion preservation and Graveyard cutout handling were not changed.

2. **P2 — CC0 per-surface smoothness**
   - `game/Assets/Editor/KenneyArt.cs`
   - Added named water, metal, cloth, skin/fur, and wood/stone classifications. Atlas kits create/reuse surface-specific URP material variants based on model/material/atlas words, so (for example) blades, banners, fountains, and masonry no longer share one flat gloss value. Existing remaps are updated idempotently when the desired surface material changes.
   - `game/Assets/Editor/KayKitArt.cs`
   - Character materials now classify model/folder names: armored/weapon surfaces use `0.58`, cloth/robes `0.11`, exposed skin/fur/leather `0.25`, bone `0.20`, and neutral character surfaces `0.22`.
   - `game/Assets/Editor/QuaterniusArt.cs`
   - Creature materials now classify model/atlas names: metal `0.58`, cloth `0.11`, skin/fur/beasts `0.25`, wood/stone `0.20`, neutral `0.22`.
   - No normals are derived from any Kenney, KayKit, or Quaternius palette atlas.

3. **P1 — hub reflection probe**
   - `game/Assets/Editor/ProjectBootstrap.cs`, `CreateGrayboxScene()`
   - Added one 128px, HDR, box-projected realtime probe covering the hub/market/water volume, with importance 100 and scripting-only refresh.
   - `game/Assets/Scripts/Game/WorldAtmosphere.cs`, `RenderReflectionProbeOnce()`
   - The probe renders once after the first scene frame, after atmosphere application, and is never refreshed per frame. No light-probe groups or baked-GI workflow were added.
   - The editor-baked custom-cubemap fallback was not taken; the scoped scripted realtime path is used and awaits controller build verification.

4. **P5/P2 ground and water normals**
   - `game/Assets/Editor/ProjectBootstrap.cs`, `GroundTexture()` / `NormalTexture()`
   - The procedural grass height values now also produce a wrapped normal PNG. `M_Ground` receives `_BumpMap`, `_NORMALMAP`, matching 48x tiling, and bump scale `0.6`.
   - `game/Assets/Editor/HandpaintedGroundArt.cs`, `NormalFor()`
   - A likely shipped normal map is preferred when present. Otherwise a wrapped luminance-derived normal is generated from the real albedo into the pack's generated folder, imported as `NormalMap`, and assigned at matching tiling with bump scale `0.55`.
   - `game/Assets/Editor/ProjectBootstrap.cs`, `WaterRippleNormal()` / `Mat()`
   - Water receives a generated 256px tileable ripple normal in both base and detail normal slots at different tilings, `_NORMALMAP`/detail keywords, and smoothness `0.75`.
   - `game/Assets/Scripts/Game/WorldAtmosphere.cs`, `UpdateSurfaceMotion()`
   - Base/detail normal offsets move slowly in different directions. Reduced Motion freezes both offsets.

5. **P4 — subtle cloud layer**
   - `game/Assets/Editor/ProjectBootstrap.cs`, `CloudNoiseTexture()` / `CloudMaterial()` / `CreateGrayboxScene()`
   - Added a generated 256px tileable cloud texture on a large double-sided transparent unlit plane, with no collider or shadows. The scene references the material/shader so builds retain it.
   - `game/Assets/Scripts/Game/WorldAtmosphere.cs`, `ApplyAtmosphere()` / `UpdateSurfaceMotion()`
   - Clouds tint dark/cool at night, near-white by day, warm at twilight, and rotate very slowly unless Reduced Motion is enabled.

6. **P6 — cheap WebGL SSAO**
   - `game/Assets/Editor/ProjectBootstrap.cs`, `PipelineVariant()` / `EnsureSsaoFeature()`
   - The web renderer now uses the existing SSAO helper with half-resolution rendering, intensity `0.4`, radius `0.25`, and unchanged direct-light strength `0.25`. Desktop remains half-resolution at `0.6` / `0.3`.

7. **P8 — explicit soft-shadow quality**
   - `game/Assets/Editor/ProjectBootstrap.cs`, `PipelineVariant()`
   - The verified URP serialized field `m_SoftShadowQuality` is explicitly High for desktop and Low for web. No new serialized field names were introduced.

8. **Generated beast `_hd` preference**
   - `game/Assets/Editor/GeneratedArt.cs`, `Setup()`
   - Each canonical beast now selects `Bear_hd.fbx` / `Rat_hd.fbx` when loadable, otherwise `Bear.fbx` / `Rat.fbx`. Both choices build the canonical `Bear.prefab` / `Rat.prefab`, so runtime lookup does not change.
   - Static fallback verification: both base FBXs exist, both HD FBXs currently exist, and the selection branch resolves directly to the base path whenever the HD asset load returns null. Missing either canonical source logs a warning instead of processing unrelated generated models.

## Verification evidence

1. `scripts/compile-check.ps1`
   - Initial invocation was blocked before compilation because the sandbox cannot read `C:\Users\Bruce\AppData\Roaming\NuGet\NuGet.Config`.
   - Required fallback: `dotnet build scripts/compile-check/CompileCheck.csproj --no-restore --nologo -v q`.
   - Final result: **Build succeeded, 0 warnings, 0 errors**.

2. `dotnet test rules/RadiantPool.Rules.sln`
   - Initial restore was blocked by the same inaccessible user NuGet config.
   - Required fallback: `dotnet test rules/RadiantPool.Rules.sln --no-restore --nologo`.
   - Final result: **166 passed, 0 failed, 0 skipped** (`75 ms`).

3. Unity bootstrap — **skipped by controller override**.
4. Win64 build — **skipped by controller override**.
5. Visual captures/self-review — **skipped by controller override; no capture assessment available in this implementer run**.
6. Smoke test — **skipped by controller override**.
7. Commit — **not created by controller override**.

Additional source checks:

- Scoped `git diff --check` passed.
- URP 17 package source was inspected locally to confirm `m_SoftShadowQuality`, `SoftShadowQuality.Low/High`, and the existing SSAO serialized setting names.
- Branch confirmed as `ui/combat-health-grouping`.

## WebGL cost notes

- Reflection probe: one 128px six-face render at scene load, then ordinary static cubemap sampling; no per-frame capture.
- Ground normal: one extra sampled normal texture on ground; 128px source.
- Water: one 256px normal asset sampled twice by the existing Lit material; offset updates only mutate material UV state and stop under Reduced Motion. No planar reflection, refraction, depth foam, or extra camera.
- Clouds: one 256px compressed texture and one transparent unlit plane/single texture lookup; no raymarching and no shadows.
- SSAO: one additional half-resolution screen-space pass at reduced intensity/radius. This is the main WebGL profiling item for the controller's build/capture pass.
- Smoothness and soft-shadow changes add no material textures or draw passes; desktop High soft shadows intentionally spend more filtering samples, while web remains Low.

## Deferred/out of scope and concerns

- P7 vegetation wind/sway remains deferred exactly as required; it needs the planned Shader Graph work.
- Tonemapping, bloom, vignette, color grade, white balance, UI/theme, runtime pipeline swapping, HeadlessBuild, lightmaps, light probes, and baked GI were not changed.
- `compile-check.ps1` does not compile `Assets/Editor`. The controller's required bootstrap is the first true editor-side compile and must validate the reflection-probe APIs, generated texture import paths, materials, and URP asset serialization.
- The controller should visually confirm that the transparent cloud plane stays subtle from all gameplay camera pitches, water normals/reflections read without aliasing, and web SSAO remains within budget.
- Pre-existing unrelated dirty and untracked files were left untouched, including the supplied HD beast assets.
