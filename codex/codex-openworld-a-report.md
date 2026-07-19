# Open World Task A Report

## Outcome

Implemented Task A without committing. The campaign sites now use the supplied atlas
coordinates, the bootstrap creates deterministic chunked wilderness and road data, the old
town/site cages are removed, the world has collider-only rim walls, the camera reaches the
new bounds, and the player has a CharacterController-safe fall recovery.

## Files changed

- `game/Assets/Scripts/Game/CampaignExpansionContent.cs`
  - Replaced only the `Vector3 center` literal for each of the 34 `Sites` entries with the
    coordinate table from the brief.
- `game/Assets/Editor/OpenWorld.cs`
  - Added the Task A terrain builder, deterministic road graph, world rim, and validator.
- `game/Assets/Editor/OpenWorld.cs.meta`
  - Added a stable Unity meta GUID for the new editor source.
- `game/Assets/Editor/ProjectBootstrap.cs`
  - Integrated `OpenWorld.Build` and `OpenWorld.Validate`, removed the specified barriers,
    moved `DressWorld` beside the new wilderness build, and set the camera far clip plane.
- `game/Assets/Scripts/Player/PlayerMotor.cs`
  - Added shrine recovery below y = -25.
- `codex/codex-openworld-a-report.md`
  - This report.

The bootstrap will generate/update `Assets/Settings/OpenWorldMeshes.asset` as one persistent
mesh asset containing all chunk meshes. It is not present in this source-only change because
the full bootstrap was intentionally left for the controller acceptance run.

## Design implementation

### Site relayout

- Applied all 34 supplied coordinates verbatim.
- A source audit parsed the resulting table and confirmed 34 exact matches.
- Computed nearest distance from origin: 192.925 units.
- Computed minimum pairwise distance: 70.342 units, between
  `iron_concord_redoubt` and `shattered_coast`.

### Wilderness terrain

- `OpenWorld.Build(Transform worldRoot, Material groundMat)` creates a `Wilderness` parent
  below the supplied world root.
- Bounds are X [-400, 560] and Z [-480, 410].
- Creates 8 by 8 full 120x120 chunks. Every chunk has 31x31 vertices at 4-unit spacing and
  is named `Wilderness_<cx>_<cz>`.
- Every chunk receives a `MeshFilter`, `MeshRenderer` using the hub `groundMat`, a
  `MeshCollider` sharing the generated mesh, world-position/8 UVs, recalculated normals, and
  static flags. There are no `NetworkObject` components.
- Height uses the specified two deterministic Perlin octaves and no random generator.
- Smoothstep flattening blends to exactly y = -0.04 for:
  - the town, fully flat through Chebyshev radius 66 and blended through 100;
  - every remote site, fully flat through Chebyshev radius 34 and blended through 55;
  - every road, fully flat through distance 7 and blended through 15.
- Distance to roads is calculated against every polyline segment.
- The mesh asset is rebuilt deterministically on each bootstrap so saved scenes and player
  builds retain the generated meshes.

### Road graph

- `OpenWorld.RoadPolylines()` is public and independently callable.
- The nine region lists contain all 34 sites exactly once.
- Each region uses deterministic Prim MST construction starting at its alphabetically first
  site, with stable lexical tie-breaking.
- Each region also has one town connection to its closest-to-origin site, beginning at
  `entry.normalized * 58`.
- Every endpoint on a site uses `center + (0, 0, -24)`.
- The graph contains 34 edges total: 25 regional MST edges plus 9 town entries.
- Edges are subdivided at no more than about 24 units. Interior points receive the specified
  perpendicular Perlin offset, capped at 12 units and smoothly tapered to zero inside 30
  units of each endpoint. Seeds derive only from endpoint coordinates; no `Random` API is
  used.
- Task A creates no visual strips for these new roads. The data is used only for terrain
  flattening and validation.

### Demolition and bootstrap integration

- Deleted creation of `Wall_N`, `Wall_S`, `Wall_E`, and `Wall_W`.
- Deleted creation of `MarketWall_W`, `MarketWall_E_A`, and `MarketWall_E_B`.
- Deleted creation of `TempleWall_S` and `TempleWall_N`.
- Deleted all creation and visual parenting for `Gate_DrownedMarket`,
  `Gate_GlasslitTemple`, and `Gate_AshenWard`.
- Deleted creation of every per-site `SiteEdgeN/S/E/W` collider.
- Kept the water strip, buildings, district signs, and the authored necropolis decorative
  wall ring.
- Remote sites are all built first. The bootstrap then creates a `World` root, calls
  `OpenWorld.Build(worldRoot, groundMat)`, and calls `DressWorld()` beside it.
- `OpenWorld.Validate()` runs at the end of scene creation, immediately before saving.
- Main camera `farClipPlane` is set to 800.

### World rim

- `Wilderness` owns four collider-only cubes named `WorldEdge_N/S/E/W`.
- They are 8 units tall, 2 units thick, static, renderer-disabled, and aligned to the exact
  world bounds.

### Fall safety

- The owning `PlayerMotor` checks fall safety before the combat movement early return, so a
  falling player can recover even if combat is active.
- Below y = -25 it disables the `CharacterController`, moves to the town shrine at
  `(-9, 0.5, -14)`, and re-enables the controller.
- Planar and vertical velocity are cleared and grounded time is refreshed so stale falling
  momentum cannot immediately pull the recovered player downward again.

### Validator

- Checks for exactly 34 sites, origin distance >= 150, and every pair distance >= 50.
- Samples every road segment at steps no larger than 4 units, including each polyline end.
- Each sample raycasts down from y = 30 for 60 units with triggers ignored and requires a hit
  height in [-1.5, 3].
- Each valid ground point uses `Physics.CheckSphere` at y + 1.1 with radius 0.8, triggers
  ignored. When occupied, `Physics.OverlapSphere` identifies colliders so the ground hit can
  be ignored and a real blocker can be reported.
- Emits exactly one `[OpenWorld] PASS ...` or `[OpenWorld] FAIL ...` aggregate line per call.
  Failure output includes specific site geometry issues and/or the first road failure.

## Every ZoneGate reference found

A full C# source scan of `game/Assets/Scripts` and `game/Assets/Editor` found these references:

1. `game/Assets/Scripts/Game/ZoneGate.cs`
   - This is the retained class definition required by the brief.
   - With zero instances, Unity invokes none of its `Update` methods. The class itself already
     tolerates a missing `GameDirector` and remains closed while a zone is locked.
2. `game/Assets/Scripts/Game/MiniMap.cs`, in `MiniMap.Rescan`
   - Enumerates `FindObjectsByType<ZoneGate>(FindObjectsSortMode.None)` in a `foreach`.
   - With zero instances the returned array is empty, the loop executes zero times, and no
     gate marker is added. There is no indexing, singleton assumption, or null dereference.
3. Previous `ProjectBootstrap.cs` references (removed by this task)
   - Three object constructions named `Gate_DrownedMarket`, `Gate_AshenWard`, and
     `Gate_GlasslitTemple`.
   - Three matching `AddComponent<ZoneGate>()` calls and their visual children.
   - After demolition, `ProjectBootstrap.cs` contains no `ZoneGate` reference.

No other runtime or editor C# references were found. Zero instantiated gates are therefore
safe for the current runtime callers.

## Verification performed

1. `scripts/compile-check.ps1`
   - The normal invocation was blocked before compilation because the sandbox cannot read
     `C:\Users\Bruce\AppData\Roaming\NuGet\NuGet.Config`.
   - Required fallback run:
     `dotnet build scripts/compile-check/CompileCheck.csproj --no-restore`
   - Result: success, 0 errors. One pre-existing FishNet unreachable-code warning was emitted.
2. Offline editor-class compile
   - Temporarily included `game/Assets/Editor/OpenWorld.cs` in the already-restored compile
     project, ran the same `dotnet build ... --no-restore`, and restored the project file.
   - Result: success, 0 errors. `scripts/compile-check/CompileCheck.csproj` has no final diff.
3. `dotnet test rules/RadiantPool.Rules.sln --no-restore`
   - Result: 166 passed, 0 failed, 0 skipped.
4. Coordinate and structure audit
   - Result: exact 34/34 coordinate match; nearest-origin and pairwise constraints pass;
     all nine region lists cover 34 unique sites; graph edge count is 34; all prohibited
     bootstrap creation names are absent; build/validate ordering and far clip setting pass.
5. `ZoneGate` source scan
   - Result: only the retained class and empty-safe minimap enumeration remain.

## Not run here

- Full `ProjectBootstrap.Run`, `[OpenWorld] PASS` log assertion, and Win64 build were not run.
  The brief assigns these acceptance gates to the controller, and this workspace already had
  unrelated modifications to the generated scene/prefabs that a bootstrap would overwrite.
- A compile-only Unity batch launch was attempted, but this environment rejected the process
  before Unity produced a log. The new `OpenWorld.cs` was therefore compiled offline against
  the installed Unity and UnityEditor assemblies as described above. `ProjectBootstrap.cs`
  still requires the controller's actual Unity compile/bootstrap gate.

## Constraints and self-review

- No commit was created.
- No task edit was made under `rules/`, to `CombatManager.cs`, `GameDirector.cs`,
  `CampaignTravel.cs`, `content/*.json`, `MiniMap.cs`, or Theme/UI files.
- The worktree already contained unrelated edits before Task A, including a modification to
  constrained `GameDirector.cs`; those changes were preserved and not touched.
- No quest, encounter, save, reward, travel, or zone-state logic was changed.
- Encounter triggers were not gated on zone state.
- No package or dependency was added.
- New code and strings are ASCII-only and the new generator contains no `Random` reference.
- The terrain extends in full 120-unit chunks, so the final northern chunk reaches beyond
  Z = 410; the exact Z = 410 world-edge collider bounds the playable world as specified.
- The main remaining risk is scene-level clearance from imported authored prefab colliders.
  The end-of-bootstrap validator is intentionally the authority for that check and will name
  the first blocker if a locally installed art pack introduces one.
- The generated mesh asset strategy was chosen so the saved scene/build cannot retain
  transient in-memory mesh references. It uses one asset with 64 mesh sub-assets to avoid
  creating 64 separate tracked asset files.
