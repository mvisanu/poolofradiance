# Task B — Open World: visual roads, wilderness dressing, world rim, backdrop rework

## 1. Goal

Make the open world read as a living wooded overland: visible winding dirt roads along the
Task A road graph, dense region-flavored forests broken by open meadows, a mountain rim at
the world edge, and removal of the old hub-centric backdrop rings that now sit mid-world.

## 2. Design decisions already made (follow exactly)

### 2a. New editor file: game/Assets/Editor/OpenWorldDressing.cs

Namespace `RadiantPool.EditorTools`, static class `OpenWorldDressing`, public entry
`public static void Dress()`. Call it from `ProjectBootstrap.CreateGrayboxScene`
immediately AFTER the existing `DressWorld();` call (which follows `OpenWorld.Build`).
`OpenWorld.Validate()` already runs at scene end and must still PASS after dressing.

### 2b. Visual road ribbons

- For every polyline from `OpenWorld.RoadPolylines()`: build one generated ribbon mesh —
  width 5, vertices at each polyline point extruded perpendicular in XZ, y = -0.02
  (2 cm above the flattened corridor at -0.04, below hub plane and site slabs at 0).
- UVs: v along road length / 6, u across 0..1. Persist meshes the same way OpenWorld.cs
  persists chunk meshes, in a separate asset `Assets/Settings/OpenWorldRoadMeshes.asset`
  (delete + recreate each bootstrap, AddObjectToAsset for the rest).
- Material: ONE URP Lit material `M_WildRoad` with a generated 256x256 dirt albedo
  (mid-brown ~ (0.42, 0.33, 0.24) with deterministic Perlin mottling, saved as an asset
  under Assets/Settings or the existing generated-texture location the bootstrap uses),
  smoothness 0.14. Mirror how ProjectBootstrap creates other generated materials.
- Ribbons: MeshRenderer only, NO collider, static, parented under a `WildRoads` object
  under the `World` root. Not network-spawned.

### 2c. Wilderness forest + meadow scatter

- Deterministic: a `System.Random` with a FIXED literal seed is allowed (UnityEngine.Random
  remains forbidden); Perlin-based masks preferred where natural.
- Candidate grid: every 8 units over OpenWorld.MinX/MaxX/MinZ/MaxZ. At each candidate
  (jittered up to +-3 deterministically):
  - SKIP if max(|x|,|z|) < 62 (town + its existing dressing);
  - SKIP if within Chebyshev 40 of any `CampaignExpansionContent.Sites` center (sites keep
    their own authored dressing);
  - SKIP if within 9 units of any road polyline segment;
  - SKIP if within 14 units of the world bounds (rim band, see 2d).
- Forest mask: `Mathf.PerlinNoise(x*0.008f+3.7f, z*0.008f+9.2f)`. Value > 0.56 => place a
  tree via `PolyPackArt` buckets. Value < 0.35 => meadow: with probability ~0.3 place
  ground scatter (Grass/Flower/Bush/Rock/Mushroom/Log mix). Between => nothing (open
  transitional ground).
- Region flavor — pick the mix by the NEAREST region (region = the site groups already in
  OpenWorld.Regions; use distance to the region's nearest site center):
  - FROSTVEIN, STORMGLASS: pine-weighted trees + extra rocks.
  - MIREWATCH: bush/log-weighted, sparser trees; within 120 of lanternfall_necropolis mix
    in occasional Grave/dead-fern props (PolyPackArt Grave bucket / graveyard set).
  - EMBER_CROWN, TITAN, DAWNSPIRE: sparse burnt look — rock/cliff-weighted, fewer trees
    (multiply tree acceptance by 0.55).
  - CINDERWELL, OBSERVATORY, EMBERWILD and anywhere within 160 of origin: lush mixed
    forest (default Tree() mix).
- Scale variation: trees 3.6-6.4, scatter 0.8-2.0, deterministic per-instance.
- Place items with the existing `PolyPackArt` placement helpers (Kenney fallback applies
  automatically when the pack is absent). After instantiating each wilderness dressing
  object, DESTROY every Collider in its hierarchy — wilderness dressing must be
  guaranteed collider-free so roads and off-road walking stay clear.
- Ground each instance on the terrain: raycast down from y=30 and sit the prop on the hit
  point (terrain is hilly; do not assume y=0).
- HARD CAP: 1600 total wilderness dressing instances (trees + scatter + graves). If the
  masks would exceed it, raise the tree threshold slightly and log what was reduced.
  Log exactly one line `[OpenWorldDressing] PASS props <n>` (or `FAIL <reason>` if the
  cap logic cannot converge).

### 2d. World rim mountains

- Along the inside of each world edge (OpenWorld.Min/Max bounds), place Cliff/Rock bucket
  props every ~26 units with +-8 deterministic jitter, scale 2.2-3.4, rotated to face
  inward-ish (deterministic yaw), grounded by raycast, colliders DESTROYED (the invisible
  WorldEdge colliders remain the actual barrier). Parent under `WorldRim` under `World`.
- Corner clusters: 3-4 extra crags at each of the four corners.

### 2e. Hub backdrop rework (in DressWorld, ProjectBootstrap.cs)

- REMOVE the horizon-cliffs ring (~8 cliffs at +-78-86, ~lines 612-618) and the highlands
  ring (~26 crags + conifers at radius 64-72, ~lines 518-527): they were designed as a
  closed-world backdrop and now sit as a wall of crags in the middle of the open world.
- KEEP the hub-edge forest bands, mid-map accent trees, ground scatter, farm/warcamp
  dressing — everything else in DressWorld stays.

## 3. Context

- Repo: C:\Users\Bruce\source\repo\poolofradiance, branch ui/combat-health-grouping.
- Task A (already in the working tree, uncommitted): game/Assets/Editor/OpenWorld.cs
  (terrain chunks at y hills / flat -0.04 zones, `RoadPolylines()`, `Validate()`,
  world bounds constants, WorldEdge colliders), site relayout in
  CampaignExpansionContent.cs, demolished town/site walls in ProjectBootstrap.cs.
  Read OpenWorld.cs first and reuse its patterns (mesh asset persistence, determinism).
- `PolyPackArt` (game/Assets/Editor/PolyPackArt.cs): discovery-first buckets
  (Tree/Pine/Bush/Rock/Cliff/Grass/Flower/Mushroom/Log/Grave/...); `Available == false`
  => Kenney fallback. Mirror how DressWorld uses it.
- The scene is fully regenerated by bootstrap; determinism is mandatory.
- NOTE: another workstream has uncommitted edits in this tree (CombatClientUI.cs,
  GameDirector.cs, QuestTracker.cs, smoke-test.ps1, regenerated scene/controllers).
  Do NOT touch or revert them.

## 4. Constraints

- Do NOT touch: rules/, CombatManager.cs, GameDirector.cs, CampaignTravel.cs,
  content/*.json, MiniMap.cs, Theme/UI files, CombatClientUI.cs, QuestTracker.cs,
  smoke-test.ps1, PlayerMotor.cs, CampaignExpansionContent.cs.
- OpenWorld.cs: only touch if a small helper must become public; report any such change.
- No new packages. ASCII only. UnityEngine.Random forbidden; System.Random only with a
  fixed literal seed. Do not commit — the controller commits.
- WebGL perf matters: respect the 1600 cap; no per-frame runtime scripts — everything is
  baked static scene content.
- Write your report to codex/codex-openworld-b-report.md: files changed, prop counts by
  category, how each decision was implemented, self-review.

## 5. Acceptance criteria (controller runs these)

1. `scripts/compile-check.ps1` -> 0 errors (runtime untouched, should be trivially green).
2. Bootstrap -> exit 0; log contains BOTH `[OpenWorld] PASS` and
   `[OpenWorldDressing] PASS props <n>` with n <= 1600.
3. Win64 build -> exit 0.
4. Controller visual review: `-sitecapture` at lanternfall_necropolis, ember_crown_spire,
   blackbriar_manor shows wilderness/roads reading correctly around the sites.
5. Full `scripts/smoke-test.ps1` green.
