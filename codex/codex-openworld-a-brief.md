# Task A — Open World: geography, terrain, demolition, safety

## 1. Goal

Turn the campaign world into one continuous walkable open world: sites repositioned to
match the campaign atlas, rolling wilderness terrain connecting everything, the town and
site cages demolished, a world rim, a walkability validator, and a fall-safety warp —
with zero changes to rules, quests, encounters, saves, or waystone travel behavior.

## 2. Design decisions already made (follow exactly)

### 2a. Site relayout — CampaignExpansionContent.cs

Replace ONLY the `Vector3 center` literal of each of the 34 sites in
`game/Assets/Scripts/Game/CampaignExpansionContent.cs` (Sites array, ~lines 166-467)
with these values (projected from the MiniMap campaign atlas; Council Hall stays at
world origin; atlas-north = +Z). Touch nothing else in each entry.

```
drowned_bastion                  new Vector3(-315f, 0f, 256f)
cinderwell_yard                  new Vector3(-238f, 0f, 288f)
cinderwell_undercroft            new Vector3(-162f, 0f, 284f)
ember_archive                    new Vector3(-126f, 0f, 220f)
loomhouse_enclave                new Vector3(-297f, 0f, 180f)
blackbriar_manor                 new Vector3(-220f, 0f, 158f)
gilded_quarter                   new Vector3(-144f, 0f, 148f)
emberwild_expanse                new Vector3(-306f, 0f, -180f)
wild_lairs                       new Vector3(-238f, 0f, -261f)
reedwind_encampment              new Vector3(-171f, 0f, -167f)
goblin_delves                    new Vector3(-135f, 0f, -275f)
drowned_observatory_approach     new Vector3(18f, 0f, 270f)
drowned_observatory_underworks   new Vector3(81f, 0f, 230f)
drowned_observatory_crown        new Vector3(135f, 0f, 297f)
mirewatch_citadel                new Vector3(-9f, 0f, -243f)
tidebreaker_anchorage            new Vector3(58f, 0f, -184f)
iron_concord_redoubt             new Vector3(139f, 0f, -234f)
lanternfall_necropolis           new Vector3(90f, 0f, -328f)
cinder_gate                      new Vector3(256f, 0f, -27f)
crownless_citadel                new Vector3(320f, 0f, 76f)
thornmaze                        new Vector3(387f, 0f, -63f)
ember_crown_spire                new Vector3(364f, 0f, 148f)
duskmire_crossing                new Vector3(212f, 0f, 306f)
whispervault                     new Vector3(266f, 0f, 238f)
stormglass_foundry               new Vector3(315f, 0f, 324f)
frostvein_pass                   new Vector3(396f, 0f, 310f)
hoarfire_halls                   new Vector3(446f, 0f, 238f)
winter_crown_vault               new Vector3(432f, 0f, 168f)
shattered_coast                  new Vector3(207f, 0f, -252f)
colossus_road                    new Vector3(256f, 0f, -346f)
titan_foundry                    new Vector3(315f, 0f, -256f)
veil_threshold                   new Vector3(387f, 0f, -234f)
hollow_star_depths               new Vector3(437f, 0f, -315f)
dawnspire_nexus                  new Vector3(468f, 0f, -392f)
```

(Validated: min pairwise distance 70+ units vs the 48-unit arena; nearest site is
~193 from origin. winter_crown_vault is deliberately offset from its raw atlas
projection to clear ember_crown_spire.)

### 2b. New editor file: game/Assets/Editor/OpenWorld.cs

Namespace `RadiantPool.EditorTools`, static class `OpenWorld`. Public surface:

- `public static void Build(Transform worldRoot, Material groundMat)` — terrain +
  world rim. Called from `ProjectBootstrap.CreateGrayboxScene` AFTER all remote sites
  are built (so site positions are final) and before/near DressWorld.
- `public static List<List<Vector3>> RoadPolylines()` — deterministic road graph
  (Task B will consume the same data for visual road strips and dressing corridors;
  make it callable independently).
- `public static void Validate()` — logs exactly one line starting `[OpenWorld] PASS`
  or `[OpenWorld] FAIL` (see 2f). Called at the end of scene creation.

World bounds constants: X in [-400, 560], Z in [-480, 410].

### 2c. Wilderness terrain (inside OpenWorld.Build)

- Chunked generated meshes under a parent GameObject `Wilderness`: chunk size 120x120
  world units, vertex spacing 4 (31x31 verts per chunk), covering the world bounds.
- Height function: two Perlin octaves, e.g.
  `h = Mathf.PerlinNoise(x*0.013f+7.3f, z*0.013f+2.1f)*3.4f + Mathf.PerlinNoise(x*0.045f+11.7f, z*0.045f+5.9f)*0.9f - 1.6f`
  (gentle rolling hills roughly -1.6..+2.7). Deterministic — no Random.
- FLATTEN to exactly y = -0.04 (4 cm below the hub plane and site slabs, so no
  z-fighting and the lip is far under the CharacterController step offset) via
  smoothstep blend in these zones:
  - Town: fully flat where max(|x|,|z|) <= 66, blended back to hills by 100.
  - Each remote site (all 34 centers above): flat within 34 of center (Chebyshev
    distance, the arena is square 48), blended by 55.
  - Road corridors: flat within 7 units of any polyline from RoadPolylines(),
    blended by 15. (Compute distance to polyline segments; chunks are small enough
    for a brute-force per-vertex pass at bootstrap time.)
- Each chunk: MeshFilter + MeshRenderer (`groundMat`, the same grass material the hub
  ground uses; UVs = world position / 8 so tiling matches), MeshCollider (sharedMesh),
  static, RecalculateNormals. Name `Wilderness_<cx>_<cz>`.
- Terrain is plain scene geometry, NOT network-spawned, no NetworkObject.

### 2d. Road graph (RoadPolylines)

- Per atlas region, sites group as: CINDERWELL {drowned_bastion, cinderwell_yard,
  cinderwell_undercroft, ember_archive, loomhouse_enclave, blackbriar_manor,
  gilded_quarter}; EMBERWILD {emberwild_expanse, wild_lairs, reedwind_encampment,
  goblin_delves}; OBSERVATORY {drowned_observatory_approach, drowned_observatory_underworks,
  drowned_observatory_crown}; MIREWATCH {mirewatch_citadel, tidebreaker_anchorage,
  iron_concord_redoubt, lanternfall_necropolis}; EMBER_CROWN {cinder_gate,
  crownless_citadel, thornmaze, ember_crown_spire}; STORMGLASS {duskmire_crossing,
  whispervault, stormglass_foundry}; FROSTVEIN {frostvein_pass, hoarfire_halls,
  winter_crown_vault}; TITAN {shattered_coast, colossus_road, titan_foundry};
  DAWNSPIRE {veil_threshold, hollow_star_depths, dawnspire_nexus}.
- Edges: per region, a minimum spanning tree over the region's site centers
  (deterministic: Prim's from the alphabetically-first site) PLUS one edge from the
  town to the region's closest-to-origin site. The town endpoint of that edge is
  `entry.normalized * 58` (on the old town boundary, pointing toward the region).
- Every road endpoint that lands on a site attaches at the site's SOUTH arena edge:
  `center + new Vector3(0, 0, -24)` (the waystone arrival side).
- Winding: subdivide each edge every ~24 units and offset each interior point
  perpendicular by `(Mathf.PerlinNoise(t*1.7f + seedFromEndpoints, 0.5f) - 0.5f) * 12f`,
  tapering to 0 within 30 units of either endpoint. Deterministic (seed from endpoint
  coordinates, not Random).
- Roads in Task A are DATA ONLY (used for terrain flattening + validation). Visual
  road strips come in Task B.

### 2e. Demolition + rim + safety

In `ProjectBootstrap.CreateGrayboxScene`:
- DELETE creation of: `Wall_N/S/E/W` (~lines 915-918), `MarketWall_W`, `MarketWall_E_A`,
  `MarketWall_E_B` (~923-927), `TempleWall_S`, `TempleWall_N` (~951-952), all three
  ZoneGate objects `Gate_DrownedMarket` / `Gate_GlasslitTemple` / `Gate_AshenWard`
  (~928-962), and the per-site `SiteEdgeN/S/E/W` walls (~1684-1688). Keep the water
  strip, buildings, district signs, and the necropolis decorative wall ring.
- KEEP `ZoneGate.cs` (the class) — verify every runtime reference to `ZoneGate`
  (MiniMap gate markers, anything else) tolerates zero instances; report what you find.
- World rim: four invisible Box colliders (renderer disabled), 8 tall, 2 thick, along
  the world bounds edges, named `WorldEdge_N/S/E/W`, parented under `Wilderness`.
- Camera: ensure the bootstrap-configured camera far clip plane is >= 800.
- Fall safety: in `game/Assets/Scripts/Player/PlayerMotor.cs`, when
  `transform.position.y < -25f`, teleport to `new Vector3(-9f, 0.5f, -14f)` (the town
  shrine) using the mandatory pattern: disable the CharacterController, set position,
  re-enable (see `GameDirector.Warp` / CLAUDE.md gotcha — a direct position write is
  eaten by the CharacterController).

### 2f. Validator (OpenWorld.Validate)

After the scene is built:
1. All 34 site centers: pairwise distance >= 50 AND distance from origin >= 150.
2. Sample every road polyline at 4-unit steps. At each sample:
   `Physics.Raycast(new Vector3(x, 30, z), Vector3.down, out hit, 60)` must hit with
   `hit.point.y` in [-1.5, 3]; and `Physics.CheckSphere(hit.point + Vector3.up * 1.1f, 0.8f)`
   (non-trigger colliders) must, after ignoring the ground hit itself, find no blocking
   collider. Use QueryTriggerInteraction.Ignore. If a sample is blocked, count it.
3. Log ONE line: `[OpenWorld] PASS walkable samples <n>/<n>, sites 34` when everything
   holds, else `[OpenWorld] FAIL <specifics>`. The controller greps boot.log for this.

## 3. Context

- Repo: C:\Users\Bruce\source\repo\poolofradiance, branch ui/combat-health-grouping.
- The scene is FULLY regenerated by `RadiantPool.EditorTools.ProjectBootstrap.Run`
  (batchmode); never hand-edit the scene. Your code must be deterministic — the same
  bootstrap must produce the same world every run.
- `ProjectBootstrap.CreateGrayboxScene` starts ~line 679; the nested local function
  `RemoteSite` (~1638) builds each site relative to `site.Center` — reposition is safe
  because everything derives from Center (waystones at center+(0,0,-14), encounter
  triggers, dressing).
- Hub ground: `Plane` "Ground" scale (12,1,12) at origin (~line 887). Reuse its
  material for the wilderness terrain.
- Player movement: `PlayerMotor.cs` — CharacterController, WASD, moveSpeed 6,
  sprint 8.5, gravity -20. No NavMesh anywhere; do not add one.
- Prior session gotcha: `compile-check.ps1` does NOT compile Assets/Editor — the
  bootstrap run is the compile gate for OpenWorld.cs. Write it carefully.

## 4. Constraints

- Do NOT touch: rules/ (the rules library), CombatManager.cs, GameDirector.cs,
  CampaignTravel.cs, any content/*.json, MiniMap.cs (atlas stays as-is), Theme/UI files.
- Do NOT gate encounter triggers on zone state (approved design: open danger).
- No new packages/dependencies. No Random.* in generation (determinism). ASCII only in
  code and strings (no unicode glyphs). Do not commit — the controller commits.
- dotnet/NuGet may fail in your sandbox: use --no-restore fallbacks and report.
- Write your report to codex/codex-openworld-a-report.md: files changed, how each
  design decision was implemented, every ZoneGate reference found and how zero
  instances behave, self-review notes, anything you could not verify.

## 5. Acceptance criteria (controller runs these; state in your report what you ran)

1. `scripts/compile-check.ps1` -> 0 errors.
2. `dotnet test rules/RadiantPool.Rules.sln` -> all tests pass (no rules changes).
3. Bootstrap (controller, absolute -projectPath, -logFile logs\boot-openworld.log)
   -> exit 0, log contains `[OpenWorld] PASS`.
4. Win64 build -> exit 0.
