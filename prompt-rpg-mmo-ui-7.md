# Transform My Unity Game with RPG & MMO UI 7

## ROLE

Act as a senior Unity UI engineer and technical artist working inside my existing Unity project.

The project already contains a licensed Unity Asset Store package:

- **Package:** RPG & MMO UI 7
- **Publisher:** Evil
- **Reference:** https://assetstore.unity.com/packages/2d/gui/rpg-mmo-ui-7-114435

Your job is to redesign the complete player-facing interface so the game looks like a cohesive fantasy RPG built around this asset pack.

Do not create a separate demo project. Apply the new design to the existing game while preserving its gameplay.

---

# PRIMARY GOAL

Make every normal game screen use the visual language of **RPG & MMO UI 7**, including its available:

- Window frames
- Panels
- Buttons
- Tabs
- Health bars
- Mana bars
- Nameplates
- Icons
- Tooltips
- Slots
- Decorative borders
- Backgrounds
- Typography
- Hover, pressed, selected, and disabled states

This must be a complete UI transformation, not a partial reskin.

Restyle or rebuild all existing screens, including:

- Main menu
- Pause menu
- Settings
- Combat HUD
- Player status panels
- Enemy nameplates
- Health and mana bars
- Physical attack controls
- Magic attack controls
- Ability selection
- Target selection
- Turn indicators
- Battle messages
- Damage text
- Victory screen
- Defeat screen
- Inventory, equipment, character, quest, and dialogue screens if they exist
- Loading screens
- Confirmation dialogs
- Notifications
- Any remaining prototype or placeholder UI

---

# CRITICAL RULES

## Preserve the Imported Package

Treat the original RPG & MMO UI 7 package folder as read-only.

Do not:

- Delete vendor assets.
- Rename or move vendor assets.
- Overwrite original sprites.
- Modify original package prefabs directly.
- Modify demo scenes as the final game screens.
- Edit vendor scripts unless absolutely necessary and documented.

Instead:

- Create prefab variants.
- Create game-owned prefabs that reference vendor sprites.
- Create wrappers and adapter components.
- Store all custom work outside the package folder.

## Preserve Gameplay

Do not change gameplay rules merely to simplify the UI migration.

Preserve:

- Combat formulas
- Turn order
- Character statistics
- Enemy AI
- Input behavior
- Health and mana logic
- Ability behavior
- Scene transitions
- Save data
- Victory and defeat logic
- Animation and VFX timing
- Existing public APIs
- Existing UnityEvents
- Existing serialized references whenever possible

When replacing a UI object, reconnect every required event and script reference.

## Avoid Destructive Changes

- Confirm source control is available before broad edits.
- Do not delete the old UI until the new UI is tested.
- Keep a migration-safe backup of replaced prefabs and scenes.
- Make changes in small, reviewable groups.
- Do not hand-edit complex Unity YAML when a safe editor script can perform the migration.

## Do Not Guess Asset Names

Search the project for the actual imported files. Do not assume folder, prefab, sprite, font, or scene names.

Search for likely identifiers such as:

```text
RPG & MMO UI 7
RPG MMO UI 7
UI 7
RPG
MMO
Evil
```

Use the locally imported package and its demo scenes as the source of truth.

---

# WORKFLOW

Complete the work in phases.

On the first run, complete **Phase 1 only**, provide the audit, and wait for approval before broad changes.

When explicitly told `execute all phases`, continue through all phases and report after each one.

---

# PHASE 1 — AUDIT THE PROJECT

## 1. Identify the Unity Environment

Report:

- Unity editor version
- Render pipeline
- Runtime UI system: uGUI, UI Toolkit, or custom
- Text system: TextMeshPro or legacy Text
- Input system
- Main target platform
- Canvas Scaler configuration
- Reference resolution
- Scene list
- Existing UI-related compile errors or warnings

Do not upgrade Unity or install packages without approval.

## 2. Locate RPG & MMO UI 7

Find and list the actual paths for:

- Package root
- Demo scenes
- Prefabs
- Sprite sheets
- Individual sprites
- Sprite atlases
- Fonts
- Materials
- Buttons
- Panels and windows
- Tabs
- Health and mana bars
- Nameplates
- Inventory or ability slots
- Tooltips
- Icons
- Decorative assets
- Sample scripts
- Documentation

## 3. Inspect the Package Demonstrations

Inspect demo scenes and prefabs to understand:

- Intended panel construction
- Sprite slicing
- Border sizes
- Button state setup
- Text hierarchy
- Spacing and padding
- Color palette
- Icon sizing
- Health and mana bar construction
- Tooltip construction
- Modal composition
- Hover, pressed, selected, and disabled behavior

Do not simply copy a demo Canvas. Build game-specific screens from reusable package elements.

## 4. Inventory the Existing Game UI

Search all scenes, prefabs, and scripts for:

```text
Canvas
Button
Image
RawImage
Slider
Scrollbar
Dropdown
Toggle
TMP_Text
TextMeshProUGUI
EventSystem
GraphicRaycaster
UIDocument
VisualElement
OnGUI
GUILayout
```

Identify every player-facing screen and record:

- Scene or prefab path
- GameObject hierarchy
- Controller script
- Data source
- UnityEvents
- Input dependencies
- Animation dependencies
- Whether it can be reskinned in place
- Whether it should be rebuilt as a reusable prefab
- Migration risk

## 5. Create a Migration Matrix

Use this format:

| Existing UI | Current Path | Existing Logic | RPG & MMO UI 7 Replacement | Migration Method | Risk | Validation |
|---|---|---|---|---|---|---|

## Phase 1 Deliverable

Provide:

- Package inventory
- Existing UI inventory
- Migration matrix
- Recommended implementation order
- Risks and blockers
- Exact files expected to change

Stop and wait for approval.

---

# PHASE 2 — BUILD A REUSABLE THEME SYSTEM

## 1. Game-Owned Folder Structure

Create a structure similar to:

```text
Assets/
  Game/
    UI/
      RPGMMO7/
        Theme/
        Prefabs/
          Common/
          Combat/
          HUD/
          Menus/
          Modals/
          Inventory/
          Tooltips/
        Scripts/
          Runtime/
          Editor/
        Animations/
        Materials/
        Audio/
        Tests/
        Documentation/
        Backup/
```

Adjust the root to match the project’s current organization.

## 2. Theme ScriptableObject

Create a reusable theme asset named something like:

```text
RpgMmoUi7Theme
```

Store serialized references to available package assets, including:

- Main background
- Primary and secondary windows
- Modal frame
- Header and divider
- Primary button states
- Secondary button states
- Tab states
- Icon button states
- Health frame and fill
- Mana frame and fill
- Experience frame and fill
- Player nameplate
- Enemy nameplate
- Tooltip frame
- Item slot
- Ability slot
- Selected slot
- Disabled overlay
- Target marker
- Turn marker
- Fonts
- Text colors
- Damage, healing, warning, and critical colors
- Standard spacing and padding
- UI sound references when available

Use serialized asset references. Do not load theme assets with fragile runtime string paths.

## 3. Reusable Prefabs

Create reusable prefabs or prefab variants for:

- Primary button
- Secondary button
- Icon button
- Tab
- Standard window
- Modal window
- Header
- Divider
- Tooltip
- Health bar
- Mana bar
- Character portrait frame
- Player status panel
- Enemy nameplate
- Ability slot
- Inventory slot
- Target indicator
- Turn indicator
- Confirmation dialog
- Notification
- Loading overlay

Do not duplicate styling independently in every scene.

## 4. Sprite Configuration

For each sprite, determine the correct usage:

- Simple
- Sliced
- Tiled
- Filled

Preserve decorative corners and borders. Do not stretch artwork that is not designed to stretch.

Use nine-slicing where appropriate and preserve aspect ratio for icons and ornaments.

## 5. Typography

Use TextMeshPro where practical.

Create a consistent hierarchy for:

- Screen title
- Window title
- Section heading
- Body text
- Button label
- Small metadata
- Tooltip title
- Tooltip body
- Damage text

Maintain readability and add fallback fonts when needed.

---

# PHASE 3 — RESPONSIVE LAYOUT FOUNDATION

Use a consistent Canvas Scaler for screen-space UI.

Recommended starting point when compatible with the existing project:

```text
UI Scale Mode: Scale With Screen Size
Reference Resolution: 1920 x 1080
Screen Match Mode: Match Width Or Height
Match: 0.5
```

Validate at minimum:

- 1366×768
- 1920×1080
- 2560×1440
- 3840×2160
- 1920×1200
- Resizable windowed mode

Use:

- Anchors
- Layout Groups
- Consistent margins
- Consistent spacing
- Safe-area support when applicable

Avoid:

- Hard-coded screen coordinates
- Layouts that work at only one resolution
- Excessive nested layout rebuilds
- Repeated runtime scene searches

Establish a clear UI layer order:

1. Background
2. World HUD
3. Main HUD
4. Menus
5. Tooltips
6. Modals
7. Loading overlay
8. Debug overlay

---

# PHASE 4 — RESTYLE THE COMBAT UI

The combat UI is the highest-priority gameplay interface.

## Combat HUD

Restyle:

- Player name and portrait
- Player health and mana
- Player status effects
- Enemy nameplates
- Enemy health bars
- Current-turn indicator
- Target indicator
- Battle message area

## Health and Mana Bars

Preserve existing data bindings.

Health fill must display:

```text
currentHealth / maximumHealth
```

Mana fill must display:

```text
currentMana / maximumMana
```

Requirements:

- Clamp values safely.
- Preserve delayed-damage behavior if it already exists.
- Ensure the final displayed value exactly matches gameplay state.
- Do not calculate gameplay health in the UI.
- Support zero and maximum values.

## Action Menu

Restyle the existing controls for:

- Physical Attack
- Magic Attack
- Back or Cancel
- Disabled actions
- Selected actions

Preserve all click handlers and keyboard, controller, mouse, or touch behavior already supported.

Prevent double submission while actions resolve.

## Ability Selection

Use themed slots, frames, icons, and tooltips.

Display existing ability information such as:

- Name
- Icon
- Cost
- Description
- Cooldown
- Disabled state
- Target type

Do not invent gameplay properties that do not exist.

## Target Selection

Create a clear themed target-selection state.

Requirements:

- Highlight valid targets.
- Dim or reject invalid targets.
- Show the current target.
- Allow cancel or back.
- Preserve existing input methods.
- Do not target defeated characters unless the ability permits it.

## Battle Messages and Floating Text

Restyle:

- Damage
- Healing
- Critical hits
- Misses
- Status messages
- Insufficient-resource messages
- Defeat messages

Do not alter combat results.

---

# PHASE 5 — VICTORY AND DEFEAT

Create themed victory and defeat modals using package elements.

## Victory

Support existing content such as:

- Victory title
- Rewards
- Experience
- Continue button
- Return button

## Defeat

Support:

- Defeat title
- Retry
- Exit or return
- Confirmation when needed

## Modal Requirements

- Block gameplay input behind the modal.
- Preserve time-scale behavior.
- Restore UI focus correctly.
- Select a safe default control for controller or keyboard navigation.
- Prevent duplicate button execution.

---

# PHASE 6 — RESTYLE ALL OTHER SCREENS

Apply the same design system to all existing interfaces.

## Main Menu

Restyle existing controls such as:

- New Game
- Continue
- Load Game
- Settings
- Credits
- Quit

Preserve save checks and scene-loading logic.

## Pause Menu

Restyle:

- Resume
- Settings
- Restart
- Main Menu
- Quit

Preserve pause and time-scale behavior.

## Settings

Restyle existing:

- Sliders
- Toggles
- Dropdowns
- Tabs
- Apply
- Cancel
- Defaults

When the package lacks an exact control, compose a matching control from its visual elements.

## Inventory and Equipment

When these systems exist, restyle:

- Slots
- Selection states
- Tabs
- Item icons
- Quantities
- Tooltips
- Comparison panels
- Equip and use actions

Preserve drag-and-drop and item logic.

## Character, Quest, and Dialogue

When present, restyle:

- Character statistics
- Portraits
- Equipment slots
- Quest lists and details
- Objectives and rewards
- Speaker nameplates
- Dialogue panels
- Dialogue choices

Do not alter sequencing or game data.

## Loading and Notifications

Restyle:

- Loading background
- Progress indicator
- Tips
- Toast notifications
- Errors
- Save confirmations

---

# PHASE 7 — INTERACTION POLISH

Every interactive control must support appropriate states:

- Normal
- Highlighted
- Pressed
- Selected
- Disabled
- Keyboard/controller focus
- Mouse hover
- Active tab
- Invalid action

Use package state sprites when available.

When no exact state exists, create a visually consistent state using tint, overlay, scale, or a restrained animation.

Add subtle reusable transitions where appropriate:

- Window fade or scale-in
- Button hover
- Tab selection
- Tooltip fade
- Modal appearance
- Health-bar interpolation
- Target pulse

Do not add excessive motion or delay gameplay.

---

# PHASE 8 — SAFE EDITOR MIGRATION TOOL

When many scenes or prefabs share legacy UI, create an editor tool such as:

```text
RpgMmoUi7MigrationWindow
```

Possible capabilities:

- Scan selected scene or prefab.
- List legacy UI elements.
- Preview replacements.
- Assign theme assets.
- Convert buttons, panels, and bars.
- Preserve UnityEvents.
- Preserve object names and serialized references.
- Save a backup.
- Apply changes with Undo support.
- Generate a migration report.

Use safe Unity editor APIs such as:

- `SerializedObject`
- `SerializedProperty`
- `PrefabUtility`
- `AssetDatabase`
- `Undo`
- `EditorSceneManager`

Keep editor-only code out of runtime assemblies.

Do not run destructive batch migration without a preview.

---

# PHASE 9 — VALIDATION

## Compile Validation

After each phase:

- Allow Unity to recompile.
- Fix newly introduced errors.
- Check assembly definition boundaries.
- Keep editor and runtime code separated.
- Do not suppress errors to claim completion.

## Reference Validation

Check for:

- Missing sprites
- Missing fonts
- Missing materials
- Missing prefabs
- Broken UnityEvents
- Null serialized references
- Duplicate EventSystems
- Incorrect raycast targets
- Lost scripts

## Functional Validation

Confirm:

- Main-menu controls work.
- Pause and resume work.
- Settings still save.
- Physical attacks work.
- Magic attacks work.
- Target selection works.
- Health updates correctly.
- Mana updates correctly.
- Enemy turns work.
- Turn order remains correct.
- Victory works.
- Defeat works.
- Retry works.
- Scene transitions work.
- Modals block background input.
- Existing keyboard/controller navigation still works.

## Visual Validation

Check each major screen at all required resolutions for:

- Stretched borders
- Clipped text
- Overlapping controls
- Off-screen panels
- Unreadable labels
- Old placeholder visuals
- Inconsistent fonts
- Missing button states
- Missing icons
- Important content hidden by decoration

## Performance Validation

Check for:

- Excessive Canvas rebuilds
- Excessive layout rebuilds
- Unnecessary material instances
- Too many raycast targets
- Per-frame allocations
- Repeated scene searches
- Unpooled high-frequency floating text

---

# TESTS

Create or update tests where practical for:

- Health-bar value mapping
- Mana-bar value mapping
- Value clamping
- Input locking
- Double-click prevention
- Modal input blocking
- Victory activation
- Defeat activation
- Required theme references
- Missing-reference detection
- Target-selection states

Create a manual test checklist covering:

1. Launch the game.
2. Navigate the main menu.
3. Start a battle.
4. Select Physical Attack.
5. Select a valid target.
6. Confirm the attack executes.
7. Confirm enemy health updates.
8. Select Magic Attack.
9. Confirm mana is deducted.
10. Confirm target selection.
11. Confirm enemy turn.
12. Win the battle.
13. Confirm the victory modal.
14. Lose a battle.
15. Confirm the defeat modal.
16. Pause and resume.
17. Open settings.
18. Test required resolutions.
19. Test supported keyboard/controller navigation.
20. Check the Unity Console.

---

# DEFINITION OF DONE

The work is complete only when:

1. RPG & MMO UI 7 has been audited locally.
2. Original vendor files remain unchanged.
3. A reusable game-owned theme exists.
4. Common themed prefabs exist.
5. Every major player-facing screen uses the same visual language.
6. Combat remains functional.
7. Health and mana displays remain accurate.
8. Victory and defeat remain functional.
9. Buttons have complete states.
10. UI works at supported resolutions.
11. No normal screen visibly mixes the old prototype style with the new theme.
12. No new compile errors exist.
13. No new runtime exceptions exist.
14. No missing serialized references remain.
15. Documentation and a changed-file report are provided.

---

# REPORT AFTER EACH PHASE

Provide these sections:

## Completed

Describe what was finished.

## Files Added

List every new file.

## Files Modified

List every modified file.

## Vendor Assets Referenced

List package files used without changing them.

## Validation Performed

List compilation, Play Mode, resolution, and functional checks.

## Remaining Work

List unfinished screens or issues.

## Risks

Describe compatibility, layout, font, package, or reference concerns.

---

# FINAL QUALITY STANDARD

The result must look intentionally designed, not like random package sprites were placed over the old interface.

Match the package’s:

- Visual hierarchy
- Window framing
- Color balance
- Decorative density
- Button construction
- Typography treatment
- Spacing
- Icon presentation
- Panel composition
- Fantasy RPG identity

Favor consistency, readability, and correct gameplay over using every image in the package.

Do not claim completion until every normal player-facing screen has been inspected and validated.
