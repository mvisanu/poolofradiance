# Radiant Pool — Realism Audit (read-only)

Scope: Unity 6000.0.79f1, URP. Everything below is read from the current repo
state (`ProjectBootstrap.cs` is the single source of truth — the scene is
regenerated from it, never hand-edited). Goal per `mcp-blender.md`/brief:
grounded, more *realistic painted-fantasy* look (Warcraft-ish), not
photoreal — keep existing assets/gameplay, WebGL is the binding budget.

---

## 1. CURRENT STATE INVENTORY

### Lighting
- **Sun**: one `Directional Light`, intensity `1.25`, color `(1, 0.956, 0.87)`,
  rotation `Euler(48, -35, 0)`, `LightShadows.Soft`, `shadowStrength 0.75`.
  (`ProjectBootstrap.cs` ~L564-571). At runtime `WorldAtmosphere.cs`
  (`ApplyAtmosphere`, ~L309-330) drives sun intensity/color/rotation by
  time-of-day: day peak `1.45 + 0.12` twilight bonus, color lerps
  `(1,0.46,0.25)` dawn/dusk to `(1,0.95,0.82)` noon.
- **Moon**: procedurally created if missing (`WorldAtmosphere.RefreshSceneReferences`,
  ~L156-163), directional, soft shadows, color `(0.36,0.48,0.72)`, night
  intensity up to `~0.42` in combat / `0.34` otherwise.
- **No baked lightmaps, no light probes, no reflection probes anywhere.**
  Grepped `ProjectBootstrap.cs` for ReflectionProbe/LightProbe/Lightmap/
  GIWorkflowMode/bakedGI - zero matches. `RenderSettings.ambientMode =
  AmbientMode.Trilight` (sky/equator/ground colors set directly, both at
  bootstrap and every frame in `WorldAtmosphere.ApplyAtmosphere`) is the
  entire GI story - this is realtime-only flat ambient, no GI bounce, no
  probes for indoor/shadowed reads.
- Many hand-placed emissive **point lights** for lamps/braziers/shrine/lightwell/
  breach seals (`ProjectBootstrap.cs` ~L965-1099, ~L1854), plus a runtime
  **party torch** and **combat fill light** (`WorldAtmosphere.cs` `CreatePartyTorch`
  ~L182-195, `CreateCombatLight` ~L197-211 - combat light is explicitly
  `LightShadows.None`, a flat non-shadowed fill).
- Shadows: soft, `shadowStrength 0.75` on the sun; desktop pipeline shadow
  distance `70`, resolution `2048`, `4` cascades; web pipeline `40`/`1024`/`2`
  cascades (`SetupUrp` -> `PipelineVariant` calls, ~L81-91).
- `QualitySettings.asset`: per-platform default quality level is `Standalone:5`
  (Ultra) and `WebGL:3` (High) - legacy quality-level shadow/AA fields exist
  there too, but URP's own pipeline-asset fields (above) are what actually
  govern shadow distance/resolution/cascades/MSAA at runtime; the legacy
  fields are mostly vestigial once a custom SRP is assigned.

### Post-processing (`Assets/Settings/PostFX.asset`)
Six components, all embedded as real sub-assets (the file comment in
`ProjectBootstrap.cs` L617-621 notes a historical bug where `VolumeProfile.Add`
left ghost `{fileID: 0}` entries - that bug is fixed, all 6 are present):
- **Tonemapping**: `mode: 2` = **ACES** (PostFX.asset L79-84). Confirmed.
- **Bloom**: `intensity 0.6`, `threshold 0.9`, `scatter 0.65`,
  `tint (1, 0.97, 0.88)` - warm highlight bloom (L147-167).
- **Vignette**: `intensity 0.17`, `smoothness 0.8` (L13-27).
- **Color Adjustments**: `postExposure 0.05`, `contrast 22`, `saturation 20`
  - a meaningfully punchy/saturated painted-fantasy grade, not neutral
  (L119-136).
- **White Balance**: `temperature 8`, `tint 0` - slight warm push (L41-49).
- **Film Grain**: `active: 0` (off by design - "painted worlds are clean") and
  additionally force-disabled on WebGL at runtime (`WebQuality.cs` L38-43).
- **No SSAO in the VolumeProfile itself** - SSAO is a **renderer feature**
  (`EnsureSsaoFeature`, `ProjectBootstrap.cs` L178-207), added only to the
  desktop renderer (`ssao: true`) and explicitly **omitted on the web
  renderer** (`ssao: false`, `PipelineVariant` call L88-91). Desktop AO
  settings: `Downsample = true` (half-res), `Intensity 0.6`, `Radius 0.3`,
  `DirectLightingStrength 0.25`.
- **Anti-aliasing**: desktop uses `SubpixelMorphologicalAntiAliasing` at
  `AntialiasingQuality.High` (`ProjectBootstrap.cs` L606-607); `WebQuality.cs`
  swaps this to `FastApproximateAntialiasing` at runtime for WebGL builds
  (cheaper, correct call given SMAA's cost on web GPUs).
- Camera: `allowHDR = true`, `renderPostProcessing = true` on both platforms.

### Skybox
- `Skybox/Procedural` material `M_SunnySky.mat`: `_SunSize 0.05`,
  `_SunSizeConvergence 5`, `_AtmosphereThickness 1.05`, `_SkyTint
  (0.42,0.55,0.78)`, `_GroundColor (0.4,0.44,0.5)`, `_Exposure 1.25`
  (`M_SunnySky.mat` L28-36; matches bootstrap L590-596). Runtime
  `WorldAtmosphere` clones this per-instance and re-drives `_SkyTint`,
  `_GroundColor`, `_Exposure`, `_AtmosphereThickness` by time of day
  (`ApplyAtmosphere` L379-390).
- This is Unity's built-in **legacy procedural skybox shader** (`Skybox/
  Procedural`, not a custom/HDRI sky) - cheap and WebGL-safe, but it caps
  realism: no clouds, no physically-based Rayleigh/Mie scattering, single
  flat sun disc.

### Fog / aerial perspective
- Bootstrap default: `FogMode.Exponential`, color `(0.62,0.72,0.86)`,
  density `0.009` (`ProjectBootstrap.cs` L576-579).
- Runtime (`WorldAtmosphere.ApplyAtmosphere` L370-377): fog density is
  time-of-day driven, `Mathf.Lerp(0.022, 0.0035, daylight)` clamped to a
  `0.0035` floor, i.e. **noon fog is much thinner than the bootstrap
  default** and night fog is much denser (`0.022`+). A comment on L373-374
  explicitly frames this as "aerial perspective, not weather." Combat
  darkens fog color slightly and reduces density a touch.
- Recent observation history (harness log) notes daytime fog density was
  just reduced further for mid-ground visibility - consistent with what's
  in the file now.

### Materials - flat vs PBR, by source
- **Procedural/primitive materials** (`ProjectBootstrap.Mat()`, L253-267):
  every wall/crate/gate/road/district material is `URP/Lit` with only
  `_BaseColor` + a single flat `_Smoothness` (`0.62` for `M_Water`, `0.16`
  for everything else - walls, crates, gates, roads, docks/market/temple/
  council dressing, NPC/player capsules). **No metallic maps, no normal
  maps, no occlusion maps, no detail maps on any of these** - pure flat
  color + single smoothness scalar.
- **Ground** (`M_Ground.mat` + `GroundTexture()` in `ProjectBootstrap.cs`
  L216-251): a **procedurally generated 128x128 value-noise PNG** (two
  octaves: broad patch + fine speckle), tiled `48x48` over the 120 m plane,
  `_Smoothness 0.16`, no normal map, no metallic map (`M_Ground.mat`
  L28-121 - every non-base texture slot is `{fileID: 0}`). Reads as flat,
  slightly noisy green, not a lit/shaded PBR ground.
- **Alt hand-painted ground textures** (`HandpaintedGroundArt.cs`): pulls
  real seamless hand-painted grass/dirt textures from
  `Assets/Handpainted_Grass_and_Ground_Textures` when present, tiled
  `4.5x4.5`, `_Smoothness 0.02`. **Still no normal map wired in** even though
  this is a real texture pack - only `_BaseMap` + `_BaseColor` tint are set
  (L60-63); the pack likely ships normal/roughness variants that go unused.
  This path is keyed by `CampaignSiteTheme`, not obviously wired into the
  main hub/graybox ground (which uses the procedural noise texture instead).
- **PolyPackArt** (`SetupMaterials`, L187-253) - the only pipeline that
  does carry PBR maps forward: converts Asset Store Standard-shader
  materials to `URP/Lit`, preserving `_BaseMap`, `_BumpMap` (normal, enables
  `_NORMALMAP`), `_MetallicGlossMap`, `_OcclusionMap` where the source had
  them. **But then flattens smoothness**: for the `GraveyardNature` source it
  clamps `_Glossiness` into `[0.08, 0.65]`; for every other source
  (`RpgPoly`, `SimpleNature`, `Dungeon`) it **hard-codes `_Smoothness = 0.08`
  regardless of the source value** (L230-231), discarding authored PBR
  roughness data. Alpha-cutout leaves/grass get `_Cutoff 0.35`, double-sided,
  for `GraveyardNature` only.
- **KenneyArt / KayKitArt / QuaterniusArt** (all CC0 kits - buildings, props,
  nature scatter, player/NPC characters, monsters): every material is
  **flat `_BaseColor` (or a single shared atlas texture) + flat
  `_Smoothness = 0.18`, zero metallic, zero normal maps, zero occlusion
  maps** (`KenneyArt.cs` L36-41/81-89, `KayKitArt.cs` L75-80, `QuaterniusArt.cs`
  L50-55). This covers essentially **all characters, monsters, buildings,
  and most set-dressing props** in the game - the single largest "flat
  material" surface area in the project.
- **Emissive accents**: lamp flames, lightwell, ashen breach seals all use
  `EnableKeyword("_EMISSION")` + HDR emission colors (values up to `~5.2`)
  so they feed Bloom correctly (`ProjectBootstrap.cs` L901-909, L1066-1081).
  This part is already correctly built for the post stack.
- **Water** (`M_Water`, `_Smoothness 0.62`, flat dark teal `(0.07,0.15,0.20)`):
  no normal map, no reflection, no refraction, no vertex/shader animation
  - a static flat-shaded box.

### WebGL-specific trims
- Build-time (`HeadlessBuild.WebGL`, `Assets/Editor/HeadlessBuild.cs`
  L42-141): forces `enableRenderCompatibilityMode = true` (Render Graph
  path is broken on WebGL2, per comment L65-67), swaps the **active**
  render pipeline to `URP_Web` for the duration of the build only (shader
  variant stripping must match the pipeline that's active at build time -
  runtime swap is explicitly forbidden per the comment in `WebQuality.cs`
  L21-24, because it causes invisible meshes). Brotli compression,
  `ManagedStrippingLevel.Minimal`.
- `URP_Web` pipeline asset differences vs `URP_Desktop`
  (`ProjectBootstrap.cs` `SetupUrp` L81-91):
  | Setting | Desktop | Web |
  |---|---|---|
  | MSAA | 4x | 2x |
  | HDR | on | on (kept for bloom/ACES) |
  | Shadow distance | 70 | 40 |
  | Shadow map res | 2048 | 1024 |
  | Cascades | 4 | 2 |
  | Max additional lights | 8 | 4 |
  | Additional-light shadows | on | off |
  | SSAO renderer feature | on | off |
- Runtime (`WebQuality.cs`): swaps camera AA from SMAA-High to FXAA, force-
  disables Film Grain (already off by default anyway).
- Net effect: web loses SSAO entirely, loses shadows from all non-sun lights
  (torch/lamps/combat fill won't cast shadows), and gets half the shadow
  cascades/resolution/distance of desktop.

---

## 2. GAP LIST - prioritized by realism-impact-per-effort

### P1 - Ambient/GI is flat trilight, no probes, no bounce light (highest impact)
- **What's wrong**: `RenderSettings.ambientMode = AmbientMode.Trilight` with
  hand-tuned sky/equator/ground colors is the entire indirect-lighting
  model. There is no baked GI, no realtime GI, no light probes, no
  reflection probes anywhere in the codebase. Every occluded/indoor surface
  gets the same flat ambient regardless of what's actually nearby (a wall
  under a roof reads identically to open sky). This is the single biggest
  gap between "flat game lighting" and "grounded" per the brief.
- **Fix**: Add a `ReflectionProbeGroup`/single large baked reflection probe
  centered near the hub (cheap, one bake) for specular plausibility on
  water/metal/glass, and consider `Light Probe Group`s seeded near
  buildings/dense foliage in `ProjectBootstrap.CreateGrayboxScene`/`DressWorld`
  if any baked lightmapping is introduced later. Since the world regenerates
  procedurally every run, a **full lightmap bake is impractical** (no fixed
  static geometry contract) - the realistic path is a light probe volume
  auto-generated procedurally to match placed props, or accept ambient-only
  and instead improve the trilight tuning (see P4/skybox) plus SSAO
  (already desktop-only) as the affordable substitute for bounce/occlusion.
- **Effort**: M (probe placement can be scripted in bootstrap alongside
  `DressWorld`; a full GI workflow change is L).
- **Desktop cost**: reflection probe bake is one-time editor cost, ~free at
  runtime (static cubemap sample). Light probes: negligible runtime cost.
- **WebGL cost**: same - probes are just texture/SH data, cheap on web too.
  Safe to ship on both platforms.

### P2 - Characters, monsters, buildings, and most props are 100% flat material (no normal/metallic maps)
- **What's wrong**: `KenneyArt.cs`, `KayKitArt.cs`, `QuaterniusArt.cs` - i.e.
  every player/NPC/monster body, every building, and most CC0 set dressing -
  set only `_BaseColor`/atlas + a single flat `_Smoothness 0.18`. No normal
  maps, no metallic maps, no occlusion maps anywhere in these three files.
  This is the largest surface area of "flat-shaded plastic" look in the game.
- **Fix**: Generate/bake simple normal maps for the highest-visibility
  assets (player, common monster models, hub buildings) - even a cheap
  height-from-luminance-derived normal map (Sobel filter over the existing
  albedo/atlas, bakeable at import time in each `*Art.cs` `SetupMaterials`/
  `Setup()`) would add real shading definition cheaply. At minimum, split
  `_Smoothness` per material by surface type (cloth ~0.1-0.2, metal/weapon
  ~0.5-0.7, skin/leather ~0.25-0.35) instead of one constant `0.18` for
  everything from banners to swords.
  Concretely: `KenneyArt.ColorMat`/atlas material L74-92, `KayKitArt.
  SetupMaterialsAndImport` L52-104, `QuaterniusArt.SetupModel` L37-56.
- **Effort**: M (per-kind smoothness table is S; generated normal maps is
  M-L depending on whether you accept a cheap derived-normal approach vs.
  sourcing/painting real ones).
- **Desktop cost**: negligible (same shader, same texture count if maps are
  small/compressed).
- **WebGL cost**: negligible if normal maps stay small (e.g. 256-512px,
  BC5/ETC2 compressed) - this is one of the cheapest realism levers
  available for WebGL specifically because it doesn't touch lighting passes.

### P3 - PolyPackArt smoothness is force-flattened, discarding authored PBR data
- **What's wrong**: `PolyPackArt.SetupMaterials()` (`PolyPackArt.cs`
  L230-231) DOES preserve normal/metallic/occlusion maps from Asset Store
  packs when present - but then overwrites `_Smoothness` to a hard `0.08`
  for `RpgPoly`/`SimpleNature`/`Dungeon` sources, discarding the source
  pack's `_Glossiness` entirely. Only `GraveyardNature` respects (clamped)
  authored smoothness.
- **Fix**: Extend the `GraveyardNature` clamp-and-preserve pattern
  (`Mathf.Clamp(sourceSmoothness, 0.08f, 0.65f)`) to all sources instead of
  hard-coding `0.08f` for three of the four.
- **Effort**: S (one-line change in `PolyPackArt.cs` L230-231).
- **Desktop/WebGL cost**: zero - pure data value change, no new passes.

### P4 - Skybox is the legacy flat procedural shader, no clouds/scattering nuance
- **What's wrong**: `Skybox/Procedural` (Unity's old built-in sky shader,
  `M_SunnySky.mat`) gives a single flat gradient + sun disc; no clouds, no
  real atmospheric scattering, caps how "grounded" the sky can read
  regardless of how good the ground looks.
- **Fix**: Either (a) author a cheap custom gradient+cloud-noise skybox
  shader (fullscreen, 1 texture lookup, WebGL-safe), or (b) keep the
  procedural sky but add a lightweight cloud layer via a second additive
  pass / static cloud texture rotated slowly - low cost, meaningfully
  improves the "painted fantasy" sky read. Wire changes into
  `ProjectBootstrap.cs` L583-596 and `WorldAtmosphere.ApplyAtmosphere`
  L379-390 (already re-drives `_SkyTint`/`_Exposure` per hour, so a custom
  shader just needs the same properties or a small adapter).
- **Effort**: M.
- **Desktop cost**: low (one shader, no extra render targets).
- **WebGL cost**: low if kept to a single fullscreen pass with baked noise
  texture rather than raymarched volumetric clouds (avoid - too expensive
  for WebGL).

### P5 - Water is a flat, unshaded, unreflective box
- **What's wrong**: `M_Water` is `URP/Lit`, flat dark teal, `_Smoothness
  0.62`, no normal map, no scrolling/ripple, no reflection/refraction, no
  foam at shoreline. It's built from a plain scaled cube (`ProjectBootstrap.cs`
  L757, L766).
  Note: with no reflection probes anywhere in the scene (see P1), even
  raising water smoothness further would only pick up the default skybox
  reflection - still an improvement over the current near-featureless
  surface, but reflections will stay a flat sky gradient, not real
  environment reflections, until probes exist.
- **Fix**: Cheapest realism win: add a simple scrolling normal map (two
  layers at different scale/speed, single extra texture ~256px) to `Mat()`'s
  water branch and a slightly higher smoothness/fresnel push - this is a
  classic cheap "watershader" trick and is WebGL-safe. Real planar
  reflection or depth-based foam would be higher cost and is not
  recommended for the WebGL budget.
- **Effort**: S (shader/material tweak) to M (if adding a dedicated water
  shader graph with depth-fade foam).
- **Desktop cost**: low.
- **WebGL cost**: low if using two static normal-map samples (avoid planar
  reflection cameras - expensive on web, doubles draw calls for the
  reflected view).

### P6 - SSAO is desktop-only; WebGL gets zero ambient occlusion
- **What's wrong**: `EnsureSsaoFeature` is only invoked when `ssao: true`,
  and the web pipeline variant passes `ssao: false` (`ProjectBootstrap.cs`
  `SetupUrp` L88-91). WebGL surfaces lose all contact/crevice shading that
  desktop gets, widening the desktop/WebGL visual gap the brief flags as a
  concern.
- **Fix**: Consider enabling SSAO on web at a cheaper setting (e.g. lower
  radius/sample count via the same `EnsureSsaoFeature` with a `downsample:
  true` + reduced intensity), rather than omitting entirely - modern
  desktop browsers with WebGL2/RGBA16F support (already assumed elsewhere
  in this project, per the `SetupUrp` HDR comment) can likely absorb a
  half-res single-pass SSAO. If it's genuinely too costly, a cheap
  alternative is baked-in AO via vertex color or a simple screen-space
  approximation limited to opaque geometry.
- **Effort**: S (flip the flag / tune one parameter set) to test the actual
  WebGL frame cost.
- **Desktop cost**: n/a (already on).
- **WebGL cost**: needs profiling - this is exactly the kind of "flag it as
  too expensive, offer cheaper equivalent" item the brief asks for; a
  half-res, low-radius SSAO pass is the recommended cheaper equivalent if a
  full-quality pass profiles poorly.

### P7 - No vegetation wind/sway shading
- **What's wrong**: Grepped the project for Wind/SpeedTree - no wind
  shader or per-vertex sway animation is applied to any tree/grass/bush
  asset placed by `DressWorld()`/`KenneyArt.Place`/`PolyPackArt.Place`.
  Foliage is fully static, which reads noticeably "gamey" against the
  otherwise-animated atmosphere (flickering lamps, drifting motes, day/night
  cycle).
  Also PolyPackArt's alpha-cutout leaf/grass materials only apply to the
  `GraveyardNature` source pack (`PolyPackArt.cs` L234-237) - other packs'
  foliage that should be cutout may be rendered fully opaque instead
  (worth verifying per-pack in a follow-up, not confirmed from code alone).
- **Fix**: URP Lit shader supports vertex-displacement wind via a small
  custom Shader Graph subgraph; apply it to `Kind.Tree/Bush/Grass/Flower`
  materials specifically in the relevant `*Art.cs` setup passes.
- **Effort**: M.
- **Desktop cost**: low (vertex-only shader work).
- **WebGL cost**: low - vertex shader cost, not fragment/fullscreen, cheap
  on WebGL2.

### P8 - Directional-light shadow softness/quality only tunable via one global shadowStrength
- **What's wrong**: sun shadow uses `LightShadows.Soft` with a single
  `shadowStrength 0.75` - reasonable defaults, but there's no cascade blend
  tuning, no contact-shadow/short-range detail shadow layer, so close-up
  character shadows can look low-res relative to the 2048/70m desktop
  budget. Minor relative to P1/P2 but easy to include while already tuning
  URP settings.
- **Fix**: Consider enabling Soft Shadow Quality: High explicitly per
  pipeline asset (currently relies on defaults from
  `UniversalRenderPipelineAsset.Create`) and check cascade border/blend
  values if close-range shadow aliasing is visible in-game.
- **Effort**: S.
- **Desktop/WebGL cost**: negligible to low.

---

## 3. ALREADY GOOD - don't churn these
- **ACES tonemapping is correctly set** (`Tonemapping.mode = 2` / ACES,
  `PostFX.asset` L79-84) - this was an explicit ask in the brief and is
  done.
- **Bloom/Vignette/ColorAdjustments/WhiteBalance are all properly embedded
  sub-assets** with sensible, deliberate painted-fantasy values (warm bloom
  tint, moderate vignette, +22 contrast/+20 saturation grade) - the ghost
  `{fileID: 0}` bug that used to silently drop these was already fixed
  (see comment at `ProjectBootstrap.cs` L617-621).
- **Desktop SSAO renderer feature is present and reasonably tuned**
  (half-res, intensity 0.6, radius 0.3) - a real win already in place for
  desktop.
- **Day/night atmosphere system (`WorldAtmosphere.cs`) is comprehensive**:
  time-driven sun/moon color+intensity, fog density/color, sky tint/exposure,
  lamp flicker, party torch, and a shadowless combat fill light are all
  already wired and self-tested (`AtmosphereSelfTest` coroutine). This is a
  lot of atmospheric groundwork already correctly built.
- **WebGL build pipeline correctly avoids the runtime pipeline-swap trap**
  (baking `URP_Web` in at build time, not swapping live) - this was clearly
  a hard-won fix (see comments in both `HeadlessBuild.cs` and `WebQuality.cs`)
  and should not be touched carelessly.
- **PolyPackArt does carry forward normal/metallic/occlusion maps** from
  Asset Store packs where the source materials have them (the smoothness
  flattening in P3 is the only regression there - the map-preservation
  logic itself, `SavedTexture`/`SavedColor`/`SavedFloat`, L206-229, is solid
  and handles Unity's "error shader loses HasProperty" gotcha correctly).
- **Emissive materials (lamps, lightwell, breach seals) are correctly set
  up with HDR emission colors** that will bloom properly under ACES - no
  churn needed there.
- **Fog is already time-of-day-aware and framed as aerial perspective**, not
  static weather - the density curve (`ApplyAtmosphere` L370-377) is a
  reasonable foundation; only the skybox rendering itself needs more
  visual richness (P4), not the fog logic.
