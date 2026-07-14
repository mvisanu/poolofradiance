# Gameplay and Visual Modernization Audit

This audit treats the game as a small-session co-op CRPG with modern stylized MMORPG
presentation. It does not expand the product into a persistent MMO.

## Current feature coverage

| Area | Current strengths | Highest-value next refactor |
|---|---|---|
| Session and networking | Host/join, invite code, replicated party, authoritative combat and campaign state | Replace client-authoritative exploration with prediction and server reconciliation; add reconnect snapshots |
| Character creation and party | Four classes, six abilities, visible equipment, role-aware companions | Split character presentation from network state and cache unit registries instead of repeated scene searches |
| Exploration | Camera-relative movement, jumping, sprinting, camera pan/recenter, wayfinding | Add nav-aware companion formations, interaction targeting, footstep surfaces, and server speed validation |
| Tactical combat | Initiative, movement grid, attacks, spells, AI turns, one-click close-and-attack | Pool grid cells and combat FX; batch overlay meshes; move client snapshots into an immutable combat-view model |
| Quests and world state | Four-zone campaign, optional encounters, gates, repairable persisted progress | Load zone definitions and encounter placement from one data registry instead of mirroring configuration in bootstrap code |
| Progression | Levels, XP, ability points, class resources, equipment upgrades | Present level gains as an event summary and add build previews before a point is committed |
| Economy and inventory | Loot gradient, equipment comparisons, traders, smith, selling, visible purse | Move item-row rendering to a virtualized list when inventories grow; cache icon and comparison view models |
| Saves | Host-owned campaign, derived zone progress, old-save repair | Add explicit save migrations, atomic temp-file replacement, and a save-health report on load |
| UI and wayfinding | Responsive logical canvas, exclusive panels, minimap, quest card, hotbar | Gradually migrate static panels to retained UI while keeping one authoritative input-routing layer |
| Audio and feedback | Adaptive explore/combat music, procedural fallback SFX, hit/spell feedback | Add spatial ambience emitters, surface footsteps, impact variants, and mixer groups with separate sliders |
| Rendering and art | URP, stylized asset discovery, generated beasts, district palettes, x-ray camera | Add LOD groups, GPU instancing validation, reflection probes, light probes, SSAO, decals, and per-zone atmosphere profiles |

## Modernization pass completed

- Exploration now accelerates and decelerates instead of changing velocity instantly.
- Forward sprint is available on Left Shift without changing tactical movement rules.
- Jump buffering and coyote time make traversal input reliable near ledges.
- Companions cache their leader lookup and can catch up with a sprinting player.
- Camera follow is damped and uses a non-allocating sphere cast to stay out of walls.
- The generated URP scene now enables HDR, 4x MSAA, SMAA, longer shadows, ACES
  tonemapping, restrained bloom and vignette, a procedural sunny sky, and subtle ambient
  sun motes.
- Settings now expose Low, Medium, High, and Ultra graphics presets and cap non-VSync
  rendering at 60 fps.

## Prioritized implementation roadmap

### 1. Performance foundation

Create runtime registries for players, encounters, NPCs, and interactables. Objects register
on enable and unregister on disable. This removes the remaining `FindObjectsByType` calls
from hot UI, quest, audio, and world paths. Pool damage popups, spell primitives, movement
range cells, and hover markers. Replace hundreds of tactical grid GameObjects with one or a
few generated meshes.

Target budget at 1080p High: 16.6 ms total frame time, under 8 ms CPU main thread, under
10 ms GPU, no recurring managed allocation during normal exploration, and under 1 KB per
combat interaction after warm-up.

### 2. World rendering

Add automatic LOD groups to imported environment prefabs, validate SRP Batcher and GPU
instancing compatibility, add baked light probes around paths and combat sites, and use one
reflection probe per district. Add a restrained screen-space ambient-occlusion renderer
feature for contact depth. Keep one realtime shadow-casting sun; bake or disable shadows on
small local lights.

### 3. Character and combat presentation

Add directional blend trees, acceleration-aware animation, foot planting, footstep events,
hit reactions, casting anticipation, projectile trails, impact decals, and brief camera
impulse on critical hits. Pool all transient effects. Preserve server events as the sole
source of combat outcomes.

### 4. Environment identity

Give every district a small atmosphere profile: fog colour/density, ambient tint, particles,
music snapshot, and prop palette. Add water with depth tint and shoreline foam, vertex-wind
foliage, terrain-normal blending around roads, and authored landmark silhouettes. These
changes will deliver more visual improvement than increasing polygon counts.

### 5. UI presentation

Keep the existing responsive layout rules, but add animated panel transitions, consistent
item rarity frames, cooldown sweeps, target nameplates, cast feedback, party frames, and
controller focus states. Avoid adding permanent HUD elements; preserve the current clear
battlefield and panel-ownership rules.

## Guardrails

- Rules math remains in the pure C# rules library.
- Clients send intentions; the server validates outcomes and persistence.
- Bootstrap remains the source of generated scene content; do not hand-edit its scene.
- Visual quality settings must have measurable frame-time costs and graceful fallbacks.
- New content and naming must continue to pass the IP scan.
