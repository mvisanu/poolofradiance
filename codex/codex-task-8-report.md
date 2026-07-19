# Task 8 — WoW-style HUD layout restyle

## Status

Implementation complete on `ui/combat-health-grouping`. Unity bootstrap, build, captures, and smoke tests were intentionally skipped per the controller override. Commit creation is blocked because the sandbox exposes `.git` read-only and Git cannot create `.git/index.lock`.

## Changes by area

### Player, party, and target frames

- Moved the local player presentation to a persistent top-left unit frame.
- Reused the existing live combat `UnitView.Hp`/`RpcHpSync` path in combat and `PlayerCharacterHolder.CurrentHpSynced` out of combat.
- Added name, synced level, exact `hp/max`, percentage, and a code-generated class emblem.
- Added a thin caster resource bar using `CombatManager.MySlots` against the rules library's `ClassData.SpellSlots` capacity, with an exact `slots L1 2/3`-style label.
- Added up to three compact party frames below the player frame, sourced from combat unit views in combat and replicated character holders outside combat.
- Added a top-centre-left hostile target frame driven by the picked/remembered attack or spell target; overhead monster bars remain unchanged.
- Published player, party, and target rect properties and added them to `IsMouseOverHud`.

### Action bar and XP

- Removed the old health strip from `HotBar`; health now remains visible independently in the player unit frame when the bar is stowed.
- Added an 8-unit XP strip directly above the action bar and stowed handle.
- Extracted shared XP calculation/drawing through `ProgressUI.DrawXpTrack`, preserving one definition for level, XP fraction, and MAX behavior while keeping the full character-sheet block.
- Kept every existing slot, wrap rule, behavior, hotbar stow behavior, and complete `BarRect` contract.
- Added corner key tags for actual bound controls (`A`, `I`, `J`, `SPC`, `ESC`) without inventing bindings for unbound slots.
- Moved the compact `{PartyGold:N0}g` purse chip to the right end of the bar.

### Quest tracker and combat log

- Moved the quest tracker to the right below `MiniMap.MapRect` outside combat and below the capped initiative panel in combat.
- Kept the collapsible pill, checklist, banner, steering arrow, beacon, and waypoint data flow.
- Added a capped scroll view for objective rows and published `QuestTracker.CardRect` for battlefield click gating.
- Capped initiative height to reserve tracker space while retaining its existing scroll view.
- Kept the combat log bottom-left, widened it toward 40% of the logical canvas, retained the 8-unit dock above complete `HotBar.BarRect`, and preserved picker-time stowing.

### Documentation and tests

- Updated only the affected CLAUDE.md HUD sentences: player health location, XP strip, right-side quest tracker, bottom-left combat log, published rect list, and initiative/tracker docking.
- No smoke-test expected strings required changes.
- No Theme roles were added, renamed, or removed; existing bar, XP, currency, panel, and slot roles are reused.

## Verification evidence

1. `scripts/compile-check.ps1`
   - Attempted exactly as required.
   - Restore was blocked before compilation by sandbox denial reading `C:\Users\Bruce\AppData\Roaming\NuGet\NuGet.Config`.
   - Follow-up source compile using the already-restored graph, `dotnet build scripts/compile-check/CompileCheck.csproj --no-restore --nologo -v q`, succeeded with 0 errors and one pre-existing FishNet unreachable-code warning.
2. `dotnet test rules/RadiantPool.Rules.sln`
   - Attempted exactly as required.
   - Restore was blocked before tests by the same sandbox denial.
   - Follow-up `dotnet test rules/RadiantPool.Rules.sln --no-restore` passed: 166 passed, 0 failed, 0 skipped.
3. Unity bootstrap/build/captures/smoke
   - Skipped entirely per controller override. No PNG capture self-review was performed.
4. Static checks
   - `git diff --check` passed.
   - Runtime compile has zero errors through the no-restore compile path.
5. Commit
   - Attempted scoped staging of only the four runtime files and the Task 8 CLAUDE.md HUD hunks.
   - Blocked by `fatal: Unable to create '.git/index.lock': Permission denied`.
   - No Task 8 files were staged or committed. `codex-task-8-brief.md`, `codex-task-8-report.md`, and `handoff.md` remain untracked and were never staged.

## Concerns for controller verification

- Visual capture review is still required to tune density at the narrowest supported logical canvas, especially the 8-unit XP text and right-side tracker below a normal-size combat minimap.
- Out of combat, the current client API does not expose a separate replicated remaining-slot array; the caster frame therefore shows the rules-library slot capacity outside combat and the live remaining count during combat.
- The restore-enabled verification commands must be rerun outside this restricted sandbox to satisfy their exact invocation forms.
- The controller must stage the four runtime files plus only the Task 8 CLAUDE.md HUD hunks, then create the requested commit because this sandbox cannot write the Git index.

## Fix batch

- Restored the local player frame's crimson `DOWN - death saves` combat readout and crimson `DOWN` out-of-combat readout at zero HP.
- Cached out-of-combat party-holder discovery on a one-second poll and explicitly filtered Unity fake-null entries on each draw.
- Let the compact XP label and its text-sized dark chip use a centred 14-unit rect over the unchanged 8-unit strip, with overflow clipping.
- Verification: the restore-enabled compile script was blocked by sandbox access to the user NuGet.Config; `dotnet build scripts/compile-check/CompileCheck.csproj --no-restore` passed with 0 errors, and `dotnet test rules/RadiantPool.Rules.sln --no-restore` passed 166/166.

## Fix batch 2

- Docked both quest-card states above the authoritative `HotBar.BarRect`, with a first-frame logical-canvas fallback.
- Mirrored persisted total spell slots remaining to clients and made the caster frame show total remaining/capacity in and out of combat.
- Hid the compact XP chip and label when they cannot fit the strip, while retaining the XP bar.
- Cached the player, target, and compact-XP styles and moved out-of-combat party sorting into the one-second scan.
- Verification: `scripts/compile-check.ps1` was blocked before compilation by sandbox access to the user NuGet.Config; the allowed `dotnet build scripts/compile-check/CompileCheck.csproj --no-restore --nologo -v q` fallback passed with 0 errors (one pre-existing FishNet warning), and `dotnet test rules/RadiantPool.Rules.sln --no-restore --nologo -v q` passed 166/166.
