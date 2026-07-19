# Task 8 fix batch 2 — second-pass review findings

Branch ui/combat-health-grouping, base 9f35be4. Independent review findings,
adjudicated by the controller. Fix exactly these four; touch nothing else.
Files in scope: CombatClientUI.cs, ProgressUI.cs, QuestTracker.cs,
PlayerCharacterHolder.cs. Do NOT touch anything under Assets/Editor or the
art-importer files (another task owns them right now).

## 1. Quest card must dock from HotBar.BarRect, never Ui.H (Important)
QuestTracker.DrawObjectives caps the panel with `Ui.H - CardTop - 12f`.
CLAUDE.md law: bottom HUD panels dock from `HotBar.BarRect`, never `Ui.H`.
Cap the card's yMax at `HotBar.BarRect.yMin - 8f` when BarRect is published
(`height > 0`), falling back to `Ui.H - 12f` on the first frame (mirror the
fallback comment pattern in CombatClientUI.LogRect). Apply to the expanded
panel; the 30-unit collapsed pill can keep its current height but must use the
same capped top-anchor space if it would ever reach the bar.

## 2. Caster resource bar must show PERSISTED remaining slots out of combat (Important)
Today DrawPlayerFrame shows ClassData capacity out of combat — false full bar
after spending slots (slots persist: CharacterSheet.SlotsRemaining serializes).
Fix with the file's own established pattern (PlayerCharacterHolder mirrors
server-only derived stats as SyncVars on a slow poll — see its existing poll):
- Add `SlotsRemainingTotalSynced` (int SyncVar) to PlayerCharacterHolder,
  refreshed in the same slow poll from the server-side sheet's
  `SlotsRemaining.Sum()`. Server-side only writes; explicit `== null` guards.
- Unit frame resource bar becomes TOTAL remaining / TOTAL capacity
  (`ClassData.SpellSlots(cls, level).Sum()` client-side for capacity), label
  `slots N/M` (words, ASCII). In combat keep live `CombatManager.MySlots.Sum()`
  as the remaining source; out of combat use the SyncVar.
- Hide the bar when total capacity is 0 (non-casters), as today.

## 3. Compact XP label must stay inside the strip's width (Minor)
ProgressUI.DrawXpTrack compact mode: chip + label use CalcSize with Overflow
clipping and can exceed the strip rect on the 124-unit collapsed handle.
Clamp: if `textSize.x + 10f > rect.width`, draw the bar only (no chip, no
label); otherwise keep the current fitted chip. Vertical overflow (14 units on
an 8-unit strip) stays — that was fix batch 1 and is intended.

## 4. Cache IMGUI styles and trim per-event allocations (Minor)
OnGUI runs multiple times per frame. Cache as fields (create-once, mutate
cheap properties per draw): the `centered` GUIStyle in DrawPlayerFrame and
DrawTargetFrame, and the compact label GUIStyle in ProgressUI.DrawXpTrack
(setting `.normal.textColor`/fontSize per draw on the cached instance is
fine). Sort the cached out-of-combat party list once at scan time (inside the
1s poll) instead of OrderBy per event. Do not over-engineer: GUIContent and
small string interpolations may stay.

## Constraints & verification
- Keep every Task 8 invariant (one-definition rects, Ui.PanelOpen, glyph ban,
  gold :N0, contrast). Item 2 touches replication: SyncVar mirror only, no new
  RPCs, no rules-lib changes.
- Verify: `scripts/compile-check.ps1` (or --no-restore fallback, say so) zero
  errors; `dotnet test rules/RadiantPool.Rules.sln --no-restore` 166/166.
- Do NOT git commit. Append '## Fix batch 2' to codex/codex-task-8-report.md.
Final stdout: STATUS, files touched, one-line test evidence.
