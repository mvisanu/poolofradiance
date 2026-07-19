# Task 8 — WoW-style HUD layout restyle

## The user's directive (binding)
"Change the HUD display like World of Warcraft." Use the WoW LAYOUT GRAMMAR with
the project's existing Gilded Quest / Academia visual skin (RPG & MMO UI 7 roles,
MedievalSharp/Source Serif/Inter type) — layout like WoW, look like Radiant Pool.
This is a rearrangement + restyle of the existing IMGUI HUD, NOT a rewrite: all
drawing stays IMGUI through `Ui.Begin()` → `Theme.Apply()`; tune styles in Theme,
never inline.

## Repo context
C:\Users\Bruce\source\repo\poolofradiance. Read CLAUDE.md's HUD sections first —
they document the current invariants; several are updated BY this task (listed
below), the rest are law. Key files: `HotBar.cs`, `CombatClientUI.cs`,
`QuestTracker.cs`, `MiniMap.cs`, `ProgressUI.cs`, `InventoryUI.cs`, `Theme.cs`,
`Ui.cs`, `SessionPanel.cs`, `PlayerCharacterHolder.cs` (synced HP/abilities),
`CombatManager` client HP sync (`RpcHpSync`).

## Target layout (WoW grammar, logical canvas Ui.W x Ui.H)
1. **Player unit frame — top-left**: portrait tile + name/level + health bar
   (`hp/max` + %), and for caster classes a second thin resource bar showing
   remaining spell-slot fraction (label "slots L1 2/3" style, words not glyphs).
   Portrait: a GENERATED class emblem texture (code-drawn, like MiniMap markers —
   never a font glyph, never a bitmap). In combat the frame shows the combat
   unit's live HP (`RpcHpSync`), out of combat `CurrentHpSynced` — reuse the ONE
   data path that HotBar.DrawHealth uses today, then REMOVE DrawHealth's above-
   the-bar strip (the unit frame replaces it).
2. **Party frames** — stacked under the player frame, one compact frame per
   companion/other player (name + health bar). Same sync sources CompanionAct/
   PlayerCharacterHolder already replicate. Cap the stack; shed detail gracefully.
3. **Target frame — top-centre-left, combat only**: when the player has a picked/
   remembered attack target or an action is resolving on a target, show that
   enemy's name + hp/max bar (data already drives the over-head monster bars).
   Hidden when no target. This does NOT replace the over-head bars — keep those.
4. **Action bar — bottom-centre (restyled HotBar)**: keep every existing slot,
   behavior, wrap rule, stow handle, and `BarRect` contract; restyle slots as
   uniform WoW-like squares with the keybind letter in a corner tag
   (Theme.SlotStyle), gold readout `{PartyGold:N0}g` in a compact pouch chip at
   the bar's right end. **Thin XP strip directly above the bar** (WoW-style):
   reuse `ProgressUI.XpBlock`'s data (level/xp/bar — keep ONE definition by
   extracting a shared draw helper, not by duplicating), full-width of the bar,
   ~6-8 units tall, tooltip-free, MAX at cap. The character sheet keeps its
   fuller XpBlock.
5. **Quest tracker — right side, under the minimap** (WoW objectives tracker):
   move the QuestTracker card from top-left to right-aligned under
   `MiniMap.MapRect.yMax` (+8). Keep collapse-to-pill, checklist `[x]/[ ]`,
   banner, steering arrow, beacon exactly as they are. In combat the initiative
   panel already docks below the minimap — dock the tracker BELOW the initiative
   panel then (initiative wins the anchor), both height-capped with scroll.
   The top-left corner now belongs to the unit frames.
6. **Combat log — bottom-LEFT above the bar** (WoW chat position): left-aligned,
   ~40% of Ui.W wide. ALL existing geometry law stays: `LogRect` is the one
   definition, docks ≥8 above the COMPLETE `HotBar.BarRect`, publishes `default`
   + draws nothing while any attack/spell target picker is open, and
   `IsMouseOverHud` gates with the same published rects.

## Invariants that stay LAW (do not weaken)
- One-definition rects: every new frame (player, party, target, xp strip) is a
  `Rect` PROPERTY consumed by both its drawer and `IsMouseOverHud`. Never retype.
- `Ui.PanelOpen` ⇒ every HUD drawer (including all new frames) draws NOTHING.
- `Ui.Fit`/logical canvas only — no raw Screen.width math; shed text detail
  rather than overflow; `Ui.UserScale` respected automatically via Ui.
- Hotkeys guarded by `!Ui.Typing`; H stow keeps working; Esc/panel exclusivity
  untouched; no dingbat glyphs anywhere (ASCII/words/generated textures only);
  gold always `:N0`; contrast ≥4.5:1 for text on its panel (Theme palette).
- Reduced Motion: any new animation (xp bar fill pulse etc.) must respect it —
  prefer no animation; WoW-ness is layout, not motion.
- Server-authoritative data flow untouched — this task is client presentation only.

## Documented rules this task DELIBERATELY updates (update CLAUDE.md accordingly)
- "Your HEALTH rides above the bar (HotBar.DrawHealth)" → replaced by the
  top-left player unit frame (same data path).
- "level and XP live on the CHARACTER SHEET, never the main screen" → XP also
  appears as the thin action-bar strip (sheet keeps the full block). Keep the
  one-definition principle by sharing the drawing/data helper.
- Quest card position "top-left" → right side under minimap/initiative.
- Combat log centred → bottom-left (geometry law unchanged).
Update the affected CLAUDE.md sentences precisely; keep the historical "why"
where one exists.

## Self-tests to keep green / update
- `[CombatUiTest]` (via -combatuicapture and -attacktest): log fully above
  `HotBar.BarRect`, log stowed while pickers open — must still pass with the new
  left-aligned LogRect. Update any literal rect expectations in the test, not
  the law itself.
- `-attacktest` asserts picker/hotbar fit the logical canvas + monster HUD —
  keep passing; the new target frame must not overlap the picker or the bar
  (it lives top-centre-left, they live at the bottom — assert non-overlap if a
  cheap assert fits the existing pattern).
- `-uiskincapture` (title screen, 26 roles) should be untouched — verify no
  Theme role was renamed/removed. If you ADD a Theme helper consuming an
  existing baked role (statbar_overlay, xpbar, xpbar_fill, currency_gold,
  divider), that's fine; do NOT add new bake roles in this task.
- `smoke-test.ps1` expected strings: update only if an assertion message you
  changed requires it.

## Verification
1. `scripts/compile-check.ps1` — zero errors.
2. `dotnet test rules/RadiantPool.Rules.sln` — green (no rules changes).
3. Unity steps — ONLY when no other Unity.exe/RadiantPool.exe process is running
   (another agent may hold the project; poll `Get-Process Unity,RadiantPool
   -ErrorAction SilentlyContinue` and wait, bounded ~15 min, before starting):
   a. Bootstrap batchmode (absolute -projectPath, Start-Process -Wait, boot.log
      clean).
   b. `HeadlessBuild.Win64` (build.log clean; freshness via Assembly-CSharp.dll
      timestamp).
   c. Captures (bounded run + kill, `-savedir` temp, never -Wait, NEVER send
      input to any window):
      - `-combatuicapture <scratchpad>\hud_combat.png` (runs [CombatUiTest])
      - `-warpsmith` + screenshot for the out-of-combat HUD, or an equivalent
        capture flag if one fits better
      - `-uiskincapture <scratchpad>\hud_title.png`
      READ the PNGs and self-review against the WoW layout spec above: unit
      frames top-left, target frame only in combat, xp strip above bar, tracker
      right, log bottom-left, nothing overlapping, skin coherent.
   d. `scripts/smoke-test.ps1` — full gate green.
4. Commit ONLY files this task touched:
   `HUD: WoW-style layout - unit frames, target frame, xp strip, right-side tracker`
   short body + trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
   (Bash tool; no double quotes inside the message).

## Report
Full report to `task-8-report.md` in this scratchpad directory: per-area changes,
capture self-review with the PNG paths, CLAUDE.md edits, test updates,
verification evidence per step. Final message ONLY: STATUS, commit hash,
one-line capture verdict, concerns.
