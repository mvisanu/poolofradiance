# Design & Build a Traditional Turn-Based RPG Combat System

## GOAL

Build a polished traditional turn-based RPG combat system in which the player and enemies alternate turns, select actions from a menu, play synchronized animations and visual effects, track health through UI elements, and end the battle with a victory or defeat screen.

## SUCCESS CRITERIA

The system is successful when:

- The player can select a physical attack or magic attack from an on-screen combat menu.
- The selected action targets an enemy and applies the correct amount of damage.
- Enemies automatically select and perform an action during their turns.
- Turns occur in a clear and predictable order.
- Player and enemy animations play for idle, attack, movement, damage, and defeat states.
- Visual effects and damage application are synchronized with animation events.
- Health bars update accurately after damage is applied.
- Characters cannot act after their health reaches zero.
- The battle ends when all enemies are defeated or the player party is defeated.
- A modal victory or defeat panel appears at the end of combat.
- The combat system is modular and can support additional characters, enemies, abilities, and status effects later.
- The project runs without console errors.

## USERS

Primary users:

- Players who enjoy traditional turn-based fantasy RPG combat.
- Game developers who need an expandable combat framework.

Expected player skill level:

- Beginner to experienced RPG players.

Input requirements:

- Mouse and keyboard support.
- Design the input layer so controller or touch support can be added later.

## CORE GAMEPLAY WORKFLOWS

Ranked by importance:

### 1. Start a Battle

Player action:

- The player enters or starts a combat encounter.

Expected result:

- Player and enemy characters are spawned or initialized.
- Health, magic points, stats, and available abilities are loaded.
- The turn order is calculated.
- The battle UI appears.
- The first eligible character begins its turn.

### 2. Select a Player Action

Player action:

- The player either clicks an enemy for the default attack or selects a named hotbar ability.

Required actions:

- Direct default attack by left-clicking an enemy
- Named spell abilities on the hotbar

Expected result:

- The system validates that the character is alive and allowed to act.
- The player selects a valid target.
- The action is queued or executed.
- The combat menu is disabled while the action resolves.

### 3. Execute a Physical Attack

Player action:

- The player left-clicks an enemy in the world.
- If needed, the character automatically moves next to the target before attacking.

Expected result:

- The player character moves or animates toward the target if required.
- The physical attack animation plays.
- The hit event is triggered at the correct animation frame.
- Damage is calculated and applied.
- A hit visual effect plays.
- The target plays a damage reaction.
- The target health bar updates.
- If the target reaches zero health, the defeat animation plays.
- Control returns to the turn manager.

### 4. Execute a Magic Attack

Player action:

- The player selects a named spell ability and chooses a legal target.

Expected result:

- The system verifies that the player has enough magic points or resources.
- The casting animation plays.
- The magic visual effect appears at the correct time.
- Damage is applied when the effect reaches or activates on the target.
- The target health bar updates.
- The target plays a damage or defeat animation.
- The magic cost is deducted.
- Control returns to the turn manager.

### 5. Execute an Enemy Turn

Enemy action:

- An enemy automatically selects an action and target.

Expected result:

- The enemy chooses from its available abilities.
- The enemy targets a living player character.
- The selected animation and visual effects play.
- Damage is applied.
- Player health updates.
- The system continues to the next eligible character.

### 6. Resolve Character Defeat

System action:

- A character's health reaches zero.

Expected result:

- The character is marked as defeated.
- The defeat animation plays.
- The character is removed from the active turn order.
- The character can no longer be selected as an active target unless a future revive ability supports it.

### 7. Resolve the Battle

System action:

- All enemies or all player characters are defeated.

Expected result:

- Input and combat actions stop.
- Remaining animations finish safely.
- A victory panel appears when all enemies are defeated.
- A defeat panel appears when all player characters are defeated.
- The panel provides options such as Continue, Retry, or Exit.

## COMBAT RULES

Implement the following initial rules:

- Combat is turn based.
- Each living combatant receives one action per turn.
- Turn order is based on a configurable Speed or Initiative stat.
- Physical attacks consume no magic points.
- Magic attacks consume a configurable magic-point cost.
- Damage cannot reduce health below zero.
- Healing cannot raise health above maximum health.
- Defeated characters are skipped in the turn order.
- UI input is disabled while an attack, animation, or effect is resolving.
- A character cannot target itself unless the selected ability explicitly allows it.
- Offensive abilities cannot target defeated characters.
- Battle completion is checked after every resolved action.

## DAMAGE MODEL

Start with a simple configurable damage model.

Example physical damage calculation:

```text
Physical Damage = max(1, Attacker Attack - Target Defense)
```

Example magic damage calculation:

```text
Magic Damage = max(1, Ability Power + Attacker Magic - Target Magic Defense)
```

Include support for:

- Minimum damage of 1.
- Optional random damage variation.
- Critical hits.
- Configurable critical-hit chance.
- Configurable critical-hit multiplier.
- Future elemental resistance and weakness support.

Keep damage calculations in a dedicated service or class so the formulas can be changed without modifying animation, UI, or turn-management code.

## CHARACTER STATS

Each player and enemy should support:

- Character ID
- Display name
- Maximum health
- Current health
- Maximum magic points
- Current magic points
- Attack
- Defense
- Magic
- Magic defense
- Speed or initiative
- Critical-hit chance
- Character model or prefab
- Animator reference
- Available abilities
- Team affiliation
- Current combat state
- Alive or defeated state

## ABILITY DATA

Each combat ability should be data driven and contain:

- Ability ID
- Display name
- Description
- Ability type
- Damage type
- Base power
- Resource cost
- Target type
- Animation trigger
- Visual-effect prefab
- Sound-effect reference
- Impact delay or animation-event name
- Whether movement toward the target is required
- Critical-hit eligibility
- Cooldown support for future use
- Status-effect support for future use

Prefer ScriptableObjects or an equivalent data-driven configuration system.

## COMBAT STATES

Use an explicit battle-state system.

Recommended states:

- Initializing
- StartingBattle
- CalculatingTurnOrder
- WaitingForPlayerInput
- SelectingAction
- SelectingTarget
- ExecutingPlayerAction
- ExecutingEnemyAction
- ApplyingDamage
- UpdatingUI
- CheckingBattleResult
- Victory
- Defeat
- Paused

Prevent multiple states from processing simultaneously.

## TURN MANAGER

Create a dedicated Turn Manager responsible for:

- Registering all combatants.
- Building the initial turn order.
- Selecting the current combatant.
- Skipping defeated combatants.
- Starting player or enemy turns.
- Waiting until an action fully resolves.
- Advancing to the next combatant.
- Recalculating turn order when necessary.
- Checking victory and defeat conditions.
- Preventing duplicate turns.

The Turn Manager must not directly control animation details or UI presentation.

## ANIMATION REQUIREMENTS

Each character should support the following animation states where applicable:

- Idle
- Walk or run
- Physical attack
- Magic cast
- Damage reaction
- Defeat
- Victory
- Return to starting position

Use animation events or a controlled timeline to trigger:

- Weapon impact.
- Spell release.
- Visual effects.
- Sound effects.
- Damage application.
- Return movement.
- Action completion.

Damage should occur at the visual moment of impact rather than immediately when the menu option is selected.

Include timeout or fallback handling so combat does not become permanently stuck if an animation event is missing.

## VISUAL EFFECT REQUIREMENTS

Visual effects should support:

- Physical hit effects.
- Magic casting effects.
- Magic projectile or target effects.
- Critical-hit effects.
- Defeat effects.
- Optional floating damage numbers.

VFX timing must remain synchronized with the animation and damage event.

Allow VFX prefabs to be configured per ability.

## USER INTERFACE

Create the following UI components:

### Combat Action Menu

- No generic Physical Attack or Magic Attack buttons
- Left-clicking an enemy performs the default attack and automatically closes distance
- Named spell abilities remain directly available on the hotbar
- Back or Cancel button when applicable
- Disabled state while an action is resolving
- Keyboard navigation support

### Monster Nameplates

- Exact current/max health bar above every living monster's rendered head
- Deterministic generated target shapes such as triangle, square, circle, and diamond
- Shape and health presentation must remain readable at supported UI scales

### Target Selection UI

- Highlight valid targets.
- Prevent invalid or defeated targets from being selected.
- Clearly show the currently selected target.
- Allow cancellation before the action begins.

### Player Status Panel

Display:

- Character name
- Current and maximum health
- Health bar
- Current and maximum magic points
- Magic bar
- Current-turn indicator
- Defeated status

### Enemy Status Panel or Nameplate

Display:

- Enemy name
- Current and maximum health
- Health bar
- Selected-target indicator
- Defeated status

### Battle Message Area

Display messages such as:

- Character attacks Enemy.
- Enemy takes 25 damage.
- Critical hit.
- Not enough magic points.
- Enemy is defeated.

### Victory Modal

Include:

- Victory title
- Optional experience gained
- Optional rewards
- Continue button

### Defeat Modal

Include:

- Defeat title
- Retry button
- Exit button

Health bars must update their fill values based on:

```text
Current Health / Maximum Health
```

Animate health-bar changes smoothly while ensuring the final displayed value is accurate.

## AUDIO

Support configurable audio for:

- Menu navigation
- Button selection
- Physical attack
- Magic cast
- Impact
- Damage reaction
- Defeat
- Victory
- Defeat screen
- Background battle music

Audio references should be configurable and not hard coded.

## ASSET INTEGRATION

The design should allow use of external asset packs, including:

- Heroic fantasy creature assets for enemy variety.
- RPG MMO UI 7 or similar UI assets for menus, health bars, nameplates, and modal windows.

Do not tightly couple the combat logic to a specific purchased asset pack.

Create adapter components or prefab configurations so models, animators, UI sprites, and visual effects can be replaced without changing core combat code.

## TECHNICAL STACK

Preferred stack:

- Unity
- C#
- Unity Animator
- ScriptableObjects for characters and abilities
- Unity UI or UI Toolkit
- Prefabs for combatants and visual effects
- Coroutines, async workflows, or a controlled action queue for sequencing

Propose and justify any additional packages or architectural patterns before using them.

Avoid unnecessary third-party dependencies.

## RECOMMENDED ARCHITECTURE

Separate the project into the following responsibilities:

### Core Combat

- Battle manager
- Turn manager
- Combatant model
- Ability model
- Damage calculator
- Targeting service
- Battle-result evaluator
- Action queue

### Presentation

- Character animation controller
- Visual-effect controller
- Audio controller
- Camera controller
- Floating-text controller

### User Interface

- Combat menu controller
- Target-selection controller
- Player status panel
- Enemy nameplate
- Battle message panel
- Victory modal
- Defeat modal

### Data

- Character definitions
- Enemy definitions
- Ability definitions
- Encounter definitions
- Reward definitions

Core combat logic should be testable without requiring animations or UI.

## ERROR HANDLING

Handle the following safely:

- Missing character prefab
- Missing animator
- Missing animation event
- Missing VFX prefab
- Missing target
- Invalid target
- Character defeated before its queued action executes
- Insufficient magic points
- Empty turn order
- Duplicate combatant registration
- Health values outside valid ranges
- Battle ending while another action is resolving
- UI button pressed more than once
- Scene reloaded during combat

Log actionable error messages without flooding the console.

## TESTING REQUIREMENTS

Create tests for:

- Physical damage calculation
- Magic damage calculation
- Health clamping
- Resource-cost validation
- Turn-order calculation
- Defeated-character skipping
- Target validation
- Player victory
- Player defeat
- Duplicate input prevention
- Missing animation-event fallback
- Action queue completion
- Multiple enemies
- Multiple player characters

Create a playable test encounter containing:

- One player character
- Two enemy characters
- One physical attack
- One magic attack
- Working health bars
- Attack, damage, idle, and defeat animations
- Victory and defeat modals

## PERFORMANCE REQUIREMENTS

- Avoid repeated scene searches during combat.
- Cache frequently used components.
- Pool reusable visual effects and floating damage numbers where appropriate.
- Avoid allocating unnecessary objects every frame.
- Use events to update UI rather than polling every frame.
- Ensure only one combat action resolves at a time.

## ACCESSIBILITY AND USABILITY

Include:

- Readable UI text.
- Clear selected-target indicators.
- Distinguishable health and magic values.
- Button labels in addition to icons.
- Adjustable animation speed as a future extension.
- Optional reduced-screen-shake setting.
- Input lock indicators while actions are resolving.

## OUT OF SCOPE FOR THE FIRST VERSION

Do not implement these until the core system is complete:

- Open-world exploration
- Inventory management
- Equipment system
- Save and load system
- Complex status effects
- Elemental weaknesses
- Summoning
- Multiplayer
- Procedural encounters
- Advanced enemy AI
- Full progression system
- Dialogue system

Design the architecture so these features can be added later.

## DELIVERABLES

Provide:

- Architecture overview
- Folder structure
- Core class diagram
- Battle-state diagram
- Turn-flow diagram
- Data-model definitions
- ScriptableObject definitions
- Complete C# implementation
- Prefab setup instructions
- Animator setup instructions
- UI hierarchy
- Sample encounter configuration
- Unit tests
- Manual test checklist
- Known limitations
- Extension recommendations

## PROCESS

Complete the project using separate checkpoints. Stop after each phase and wait for approval before continuing.

### PHASE 1 — DESIGN

Produce:

- Architecture overview
- Component responsibilities
- Battle-state design
- Turn-flow design
- Data model
- Event flow
- Folder structure
- Proposed Unity scene hierarchy
- Technical decisions and tradeoffs
- Risks and mitigations

Do not write the full implementation yet.

### PHASE 2 — CORE COMBAT IMPLEMENTATION

Produce:

- Combatant model
- Character statistics
- Ability definitions
- Damage calculator
- Targeting rules
- Turn manager
- Battle manager
- Action queue
- Victory and defeat evaluation
- Unit tests for core combat logic

Use placeholders for animations and UI where necessary.

### PHASE 3 — UI IMPLEMENTATION

Produce:

- Combat action menu
- Target-selection interface
- Player status panels
- Enemy health nameplates
- Battle messages
- Victory modal
- Defeat modal
- Health and magic bar updates

### PHASE 4 — ANIMATION, VFX, AND AUDIO

Produce:

- Animation controller
- Animation-event integration
- Physical attack sequence
- Magic attack sequence
- Damage and defeat reactions
- VFX synchronization
- Audio integration
- Missing-event timeout handling

### PHASE 5 — ENEMY AI AND ENCOUNTERS

Produce:

- Basic enemy action selection
- Target selection
- Encounter definitions
- Multiple-enemy support
- Multiple-player support
- Configurable enemy abilities

### PHASE 6 — TESTING AND POLISH

Produce:

- Automated test results
- Manual test checklist
- Bug fixes
- Performance review
- Input-lock validation
- UI polish
- Animation timing review
- Final setup documentation

## FINAL ACCEPTANCE TEST

The completed sample must demonstrate this sequence:

1. A battle begins with one player and at least two enemies.
2. Every living enemy shows an overhead health bar and a distinct generated target shape.
3. The player left-clicks a distant enemy once.
4. The character automatically closes distance and the attack animation plays.
5. The impact VFX appears at the correct animation frame.
6. Damage is applied.
7. The enemy health bar updates.
8. The enemy performs its turn.
9. The player selects a named spell ability from the hotbar.
10. The appropriate spell slot is deducted.
11. The casting animation and VFX play.
12. The enemy is defeated when its health reaches zero.
13. Defeated enemies no longer receive turns.
14. The victory modal appears after all enemies are defeated.
15. The defeat modal appears when all player characters are defeated.
16. No console errors occur during the complete battle.
