# Fix — OpenWorld validator ground check (review Important 2)

## Goal

Stop `ValidateRoadSample` in `game/Assets/Editor/OpenWorld.cs` from certifying a
sub-3m obstacle as walkable ground.

## Defect (from second-pass review)

The downward raycast treats whatever collider it hits FIRST as ground, and then
unconditionally excludes that same collider from the clearance check. A crate, low
wall, or rock standing on a road therefore passes: its top is below y=3, and the
clearance sphere ignores it because it was the raycast hit.

## Fix (exactly this, nothing else)

After the raycast hit passes the height check, require the hit collider to actually
BE ground before treating it as such:

- Accept the hit only if the hit GameObject's name starts with `Wilderness_` or
  `SiteGround_`, or is exactly `Ground` (the hub plane).
- Otherwise: increment `blocked`, and if `firstFailure` is null set it to
  `non-ground {collider name} at ({x:F1},{z:F1})`, then return.
- Keep the existing clearance CheckSphere/OverlapSphere logic unchanged for accepted
  ground hits.

## Constraints

- Touch ONLY the `ValidateRoadSample` method in `game/Assets/Editor/OpenWorld.cs`.
- ASCII only. Do not commit.
- Append a short note to `codex/codex-openworld-b-report.md` under a heading
  `## Validator fix`: what changed and why, plus the result of running
  `scripts/compile-check.ps1` (use the --no-restore fallback if restore is blocked;
  it confirms no accidental runtime damage).
