# Open World Task B report

## Outcome

Implemented Task B without committing. The implementation adds persisted visual road ribbons,
deterministic region-flavored wilderness dressing with a hard 1,600-prop cap, collider-free
world-rim mountains, and removes the two superseded hub-centric backdrop rings.

Task A's Chebyshev-66 town road endpoints are unchanged.

## Files changed

- `game/Assets/Editor/OpenWorldDressing.cs` (new)
  - Public `OpenWorldDressing.Dress()` entry point.
  - Road mesh/material/texture generation.
  - Wilderness candidate generation, masking, cap convergence, placement, grounding, and
    collider stripping.
  - Region flavor selection from Task A's region groups.
  - World-rim and corner crag placement.
- `game/Assets/Editor/OpenWorldDressing.cs.meta` (new)
  - Stable Unity asset GUID.
- `game/Assets/Editor/OpenWorld.cs`
  - Changed only the `Regions` table visibility from private to public so Task B consumes the
    exact Task A grouping instead of duplicating it.
  - No road construction, persistence, terrain, validation, world-bound, or endpoint logic was
    changed.
- `game/Assets/Editor/ProjectBootstrap.cs`
  - Calls `OpenWorldDressing.Dress()` immediately after `DressWorld()`.
  - Removes the 26-crag/conifer highlands ring at radius 64-72.
  - Removes the eight-cliff horizon ring at approximately +/-78-86.
  - Retains the hub-edge forest bands, accent trees, ground scatter, authored encounter-site
    dressing, hub road strips, farm/warcamp dressing, and all other `DressWorld` content.
- `codex/codex-openworld-b-report.md` (new)
  - This report.

No Constraints-listed file was changed by Task B. Existing unrelated and Task A working-tree
changes were preserved.

## Implementation details

### Road ribbons

- Uses every polyline returned by `OpenWorld.RoadPolylines()`; the current graph produces 34
  ribbon objects.
- Each polyline becomes one mesh with two vertices per point, a 5 m total width, miter-like
  averaged point tangents, and upward-facing triangle winding.
- All ribbon vertices use y = -0.02 exactly.
- UV `u` spans 0..1 across the ribbon and `v` accumulates road length divided by 6.
- Meshes are recreated in `Assets/Settings/OpenWorldRoadMeshes.asset`; the first mesh is the
  main asset and the remaining meshes use `AssetDatabase.AddObjectToAsset`, matching Task A's
  terrain persistence pattern.
- `WildRoads` is a static child of `World`; every ribbon has only `MeshFilter` and
  `MeshRenderer`, with no collider and no runtime/network component.
- `M_WildRoad` is one persisted URP Lit material. Its generated 256x256 repeating dirt albedo
  is centered on RGB (0.42, 0.33, 0.24), uses two deterministic Perlin octaves, and has
  smoothness 0.14.

### Wilderness forest and meadow scatter

- Uses `System.Random(481516)` only; no `UnityEngine.Random` usage.
- Evaluates an 8 m grid over all Task A world bounds and applies deterministic +/-3 m jitter.
- Excludes:
  - Chebyshev radius below 62 around town;
  - Chebyshev radius below 40 around every campaign site;
  - points below 9 m from any road segment;
  - the 14 m world-rim band.
- Uses the required forest Perlin mask coordinates and base thresholds:
  - above 0.56: forest candidate;
  - below 0.35 and a 0.30 probability roll: meadow scatter;
  - between them: open transition.
- If accepted candidates exceed 1,600, the forest threshold rises in 0.01 steps (meadow
  acceptance remains unchanged) until the count is within the hard cap. Failure to converge
  throws and emits the required `[OpenWorldDressing] FAIL ...` line. A reduction is separately
  logged under `[OpenWorldScatter]` with the initial/final counts and chosen threshold.
- Region flavor is selected by the nearest site among the exact shared `OpenWorld.Regions`
  groups, measured as distance to each region's nearest site:
  - Frostvein and Stormglass: 75% pine preference and rock-weighted ground scatter.
  - Mirewatch: 70% tree acceptance and bush/log-weighted scatter.
  - Ember Crown, Titan, and Dawnspire: 55% tree acceptance and rock/cliff-weighted scatter.
  - Cinderwell, Observatory, Emberwild, and all points within 160 m of origin: lush mixed
    forest and the default meadow mix.
- Within 120 m of `lanternfall_necropolis`, meadow choices include occasional GraveyardNature
  `Grave` and `Bush` (fern) props before falling back to the general buckets.
- Trees use 3.6-6.4 m target height. Ground scatter uses 0.8-2.0 m target size.
- Placements use `PolyPackArt` discovery buckets. Missing buckets fall through to the same
  Kenney Nature models used by `DressWorld`, so the scene still dresses without licensed
  packs.
- Every accepted instance is raycast-grounded from y = 30 onto the hilly terrain. Every
  collider in every placed hierarchy is destroyed immediately, then the complete hierarchy
  is marked static.
- Runtime category counts are emitted as one `[OpenWorldScatter] categories trees <n>, scatter
  <n>, graves <n>` line. The required final line remains exactly
  `[OpenWorldDressing] PASS props <n>` and counts only trees, scatter, and graves.

### Prop counts

- Road ribbon meshes: 34.
- Wilderness trees + meadow scatter + graves: hard maximum 1,600 total. Exact generated
  category counts require the Unity bootstrap to run; this environment could not reach the
  execute method because Unity Licensing Client IPC was denied (see Verification). The code
  logs exact tree/scatter/grave counts during the controller's bootstrap.
- World-rim regular placements: 140 candidates (two long edges plus two short edges at 26 m
  spacing).
- World-rim corner placements: 12-16 candidates (3-4 at each corner).
- World-rim props are deliberately separate from the 1,600 wilderness cap, whose specification
  covers trees, scatter, and graves.

### World rim

- `WorldRim` is a static child of `World`.
- Cliff/Rock bucket props are laid just inside all four world edges at 26 m spacing, with
  deterministic tangential +/-8 m jitter, 2.2-3.4 target footprint, and inward-facing yaw with
  deterministic +/-28 degree variation.
- Each corner receives 3-4 extra crags with deterministic local offsets.
- Every rim prop is terrain-grounded and recursively stripped of colliders. Task A's invisible
  `WorldEdge_*` colliders remain the only world barrier.

## Verification

### Passed

- `scripts/compile-check.ps1` was run. Its restore phase was blocked before compilation by
  sandbox denial reading `%APPDATA%/NuGet/NuGet.Config`.
- Requested fallback run:
  - `dotnet build scripts/compile-check/CompileCheck.csproj --no-restore`
  - Result: success, 0 warnings, 0 errors.
- Focused source checks:
  - new editor source contains no non-ASCII characters;
  - no `UnityEngine.Random` usage;
  - braces balance;
  - focused `git diff --check` is clean for Task B tracked edits;
  - the two obsolete `DressWorld` blocks are absent;
  - `OpenWorldDressing.Dress()` is directly after `DressWorld()`;
  - `OpenWorld` still contains `entry * (66f / maxComponent)` unchanged.

### Environment-blocked

Unity bootstrap was launched with the required absolute project path and remained attached.
Unity imported packages and requested script compilation, but then repeatedly lost its Unity
Licensing Client connection. The licensing client IPC channel was inaccessible in this sandbox,
and Unity entered repeated 60-second reconnect attempts before the run was stopped. It produced
no C# compiler diagnostic and never reached `ProjectBootstrap.Run`, so this session could not
produce the generated road assets/scene, exact prop category counts, or the two PASS markers.

The controller should run the normal bootstrap to complete editor compilation, asset generation,
scene validation, and visual review.

## Self-review

- Determinism: all discrete choices use fixed-seed `System.Random`; all spatial masks and road
  texture mottling use fixed-coordinate Perlin noise.
- Persistence: road meshes follow Task A's delete/recreate plus sub-asset pattern; material and
  texture use stable project asset paths.
- Traversal safety: roads have no colliders; every wilderness and rim hierarchy has all colliders
  destroyed; site, town, road, and rim exclusions are applied before placement.
- Performance: dressing is baked editor content with no per-frame scripts; wilderness placement
  cannot exceed 1,600 instances.
- Scope: no package, runtime, content JSON, UI, combat, travel, rules, or test-script changes;
  no commit; unrelated working-tree edits were neither reverted nor reformatted.
- Remaining verification risk: only Unity's editor compiler/bootstrap can validate Unity API
  integration and report exact scene counts. That run was blocked solely by licensing IPC in
  this environment and remains required before acceptance.

## Validator fix

`ValidateRoadSample` now accepts a raycast hit as ground only when its GameObject name starts
with `Wilderness_` or `SiteGround_`, or is exactly `Ground`. Other first hits are reported as
non-ground blockers so low road obstacles can no longer certify themselves as walkable ground;
the existing clearance checks remain unchanged for accepted ground hits.

`scripts/compile-check.ps1` was blocked during restore by denied access to the user NuGet config.
The required `dotnet build scripts/compile-check/CompileCheck.csproj --no-restore` fallback
succeeded with 0 errors and 1 existing FishNet unreachable-code warning.
