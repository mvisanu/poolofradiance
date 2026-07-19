# Task 8 fix batch — WoW HUD review findings

Branch ui/combat-health-grouping, base commit 8f58f28 (the Task 8 HUD commit).
These are review findings from the controller's diff read and capture review of
that commit. Fix exactly these three; touch nothing else.

## 1. Restore the player's DOWN / death-save indicator (Important)
The old `CombatClientUI.DrawMyCard` showed `DOWN — rolling death saves` when the
local combat unit was down, and the old `HotBar.DrawHealth` readout showed
`DOWN` at 0 hp. Both were removed with those methods; the new top-left player
frame only shows `0/max 0%`. Restore the state in the player unit frame
(`CombatClientUI.DrawPlayerFrame`): when the local combat unit `.Down` is true,
replace the hp readout text with a crimson `DOWN - death saves` (words + colour,
`Theme.Crimson` for the label colour like the old code; ASCII hyphen, NO em dash
glyph risk in UI: use "DOWN - death saves"). Out of combat at 0 hp show `DOWN`
in crimson. Party rows already show `down` — leave them.

## 2. Cache the out-of-combat party holder scan (Important, perf)
`CombatClientUI.DrawUnitFrames` calls
`FindObjectsByType<PlayerCharacterHolder>` every OnGUI pass out of combat.
Cache the result and refresh on a slow poll (~1 s), mirroring the existing
slow-poll pattern used for `PlayerCharacterHolder` mirrors (see its SyncVar
poll comment). A simple `float _nextPartyScan` + cached list field on
CombatClientUI is fine. Invalidate-safe: filter destroyed/fake-null entries
with explicit `== null` checks when drawing (Unity fake-null defeats `?.`).

## 3. XP strip label is vertically clipped to illegibility (Important, visual)
In `ProgressUI.DrawXpTrack` compact mode, the fontSize-8 label inside the
8-unit-high strip is clipped by `TextClipping.Clip` — the capture shows the top
half of the glyphs only. Fix by drawing the label in a taller centred rect that
OVERFLOWS the strip vertically (e.g. a rect 14 units high centred on the strip,
`clipping = TextClipping.Overflow`, keep the dark backing chip sized to the
text) — the strip graphic stays 8 units; only the text rides over it. Keep
contrast >= 4.5:1 (dark backing behind light text stays).

## Constraints
- Runtime scripts only (`CombatClientUI.cs`, `ProgressUI.cs`); no Theme role
  changes; no new rects (reuse published ones); keep all Task 8 invariants
  (one-definition rects, Ui.PanelOpen, no glyphs, gold :N0).
- Verify: `scripts/compile-check.ps1` zero errors (use --no-restore fallback if
  the sandbox blocks NuGet, and say so); `dotnet test rules/RadiantPool.Rules.sln
  --no-restore` 166/166. Unity steps are the controller's.
- Do NOT git commit (sandbox cannot); leave changes in the working tree.
- Append a short fix report to codex/codex-task-8-report.md (## Fix batch).

Final stdout: STATUS, files touched, one-line test evidence.
