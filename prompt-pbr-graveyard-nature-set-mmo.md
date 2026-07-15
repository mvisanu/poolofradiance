# Integrate “PBR Graveyard and Nature Set 2.0” Into an Existing Unity MMO

## ROLE

Act as a senior Unity environment artist, technical artist, level designer, performance engineer, and multiplayer gameplay engineer working directly inside an existing Unity MMO project.

The project already contains or will contain the licensed Unity Asset Store package:

- **Package:** PBR Graveyard and Nature Set 2.0
- **Publisher:** NatureManufacture
- **Asset Store reference:** https://assetstore.unity.com/packages/3d/environments/fantasy/pbr-graveyard-and-nature-set-2-0-58915

Your job is to use the imported package to create a polished, playable graveyard and haunted-nature region inside the existing MMO world.

Do not create a disconnected asset showcase. Build a production-quality game area that matches the existing game’s architecture, art direction, terrain, networking, quest system, AI, combat, navigation, minimap, persistence, and performance requirements.

---

# PRIMARY OBJECTIVE

Create a complete fantasy graveyard region using the package’s available assets, including appropriate combinations of:

- Grave markers
- Tombstones
- Crypts
- Mausoleums
- Coffins
- Bone and skeleton props
- Ruins
- Walls
- Gates
- Fences
- Dead or haunted vegetation
- Trees
- Rocks
- Ground materials
- Paths
- Nature props
- Decorative objects
- Interior or catacomb pieces when available
- Lighting and atmospheric elements
- Decals and surface details
- Package-provided demo configurations when useful

The finished area must:

- Feel like a real part of the MMO world.
- Support exploration and combat.
- Provide clear player navigation.
- Include meaningful gameplay locations.
- Maintain stable multiplayer behavior.
- Meet acceptable performance targets.
- Preserve the original package files.
- Work with the project’s current render pipeline.

---

# SOURCE OF TRUTH

Use the imported package files inside the Unity project as the definitive source of truth.

Before implementation:

1. Search the entire `Assets` directory for the package.
2. Locate its documentation, demo scenes, prefabs, terrain layers, materials, shaders, textures, meshes, LOD groups, colliders, vegetation, particle effects, decals, and sample lighting.
3. Inspect the package’s demo scenes.
4. Determine which assets are compatible with the project’s Unity version and render pipeline.
5. Identify the intended scale, material setup, and prefab structure.
6. Do not guess asset names or folder paths.
7. Do not assume every item shown on the Asset Store page is present in the locally imported package version.

The Asset Store listing indicates broad compatibility with Built-in, URP, and HDRP, but the local project configuration and imported asset version determine the exact implementation.

---

# CRITICAL SAFETY RULES

## Preserve Vendor Assets

Treat the original package directory as read-only.

Do not:

- Delete vendor files.
- Rename vendor files.
- Move vendor files.
- Modify vendor prefabs directly.
- Overwrite vendor materials or textures.
- Change source meshes.
- Edit package demo scenes as the final game scene.
- Add game-specific scripts directly to vendor prefabs unless using prefab variants.

Instead:

- Create prefab variants.
- Create game-owned materials when pipeline conversion or customization is required.
- Create game-owned terrain layers.
- Create wrapper prefabs.
- Create game-owned scene instances.
- Keep all project-specific files outside the vendor folder.

## Preserve Existing MMO Systems

Do not replace or rewrite existing systems merely to place environment assets.

Preserve:

- Networking framework
- Server authority
- Character controller
- Combat logic
- AI framework
- Spawn system
- Quest system
- Loot system
- Persistence
- Save data
- World streaming
- Additive scene loading
- Terrain system
- Minimap
- Fast travel
- Day/night cycle
- Weather system
- Audio manager
- Existing render pipeline
- Existing input system

Integrate through adapters and existing interfaces.

## Multiplayer Safety

Static environment art normally belongs in shared scenes and must not be network-spawned individually unless the project specifically requires it.

Do not:

- Add a network identity to every tombstone, tree, rock, or fence.
- Synchronize static decoration over the network.
- let clients authoritatively spawn enemies or loot.
- run server-only gameplay logic on decorative props.
- introduce client-only collision differences that affect gameplay.

Only interactive or stateful objects should use the project’s networking system.

---

# WORKFLOW

Complete the work in phases.

On the first run, complete **Phase 1 only**, provide the audit and implementation proposal, and wait for approval.

When explicitly instructed to `execute all phases`, continue through every phase and provide a report after each phase.

---

# PHASE 1 — AUDIT THE PROJECT AND PACKAGE

## 1.1 Identify the Existing Project

Report:

- Unity version
- Target platform
- Render pipeline:
  - Built-in
  - URP
  - HDRP
- Color space
- Lighting workflow
- Terrain system
- Vegetation system
- Networking framework
- AI and navigation system
- World streaming or additive scene system
- Addressables usage
- Occlusion-culling usage
- Existing day/night system
- Existing weather system
- Existing minimap system
- Existing quest and spawn systems
- Current target frame rate
- Current scene organization

Do not upgrade or convert the project without approval.

## 1.2 Locate the Imported Package

Search for likely identifiers:

```text
PBR Graveyard
Graveyard and Nature
NatureManufacture
Graveyard
Cemetery
Crypt
Mausoleum
Catacomb
Tombstone
```

List the actual paths for:

- Package root
- Demo scenes
- Prefabs
- Meshes
- Materials
- Textures
- Terrain layers
- Trees
- Vegetation
- Rocks
- Tombstones
- Crypts
- Walls
- Fences
- Gates
- Bones
- Coffins
- Ruins
- Interior pieces
- Particle effects
- Decals
- Shaders
- LOD assets
- Collision meshes
- Documentation

## 1.3 Inspect Technical Quality

For representative prefabs, record:

- Triangle count
- Number of materials
- Texture resolution
- Texture format
- Shader
- LODGroup configuration
- Collider type
- Lightmap UV availability
- Static-batching suitability
- GPU-instancing compatibility
- Shadow settings
- Bounds accuracy
- Scale consistency
- Pivot quality

Identify any assets that are unsuitable for frequent placement without optimization.

## 1.4 Inspect Demo Scenes

Study the package demo scenes for:

- Intended scale
- Asset combinations
- Terrain blending
- Path construction
- Vegetation density
- Lighting
- Fog
- Post-processing
- Color palette
- Prop spacing
- Graveyard composition
- Interior construction
- Decal use
- Particle use
- LOD distances

Use the demo as reference, not as the final MMO zone.

## 1.5 Audit the Existing World

Identify the best integration point in the current MMO.

Record:

- Candidate scenes
- Terrain coordinates
- Nearby towns, roads, forests, or dungeons
- Existing quest hubs
- Nearby player levels
- Existing enemy factions
- Fast-travel locations
- World-streaming boundaries
- Minimap coverage
- Spawn volumes
- NavMesh surfaces
- Audio zones
- Lighting volumes
- Weather volumes
- Safe zones and PvP zones

## 1.6 Produce a Migration and Integration Plan

Create a table:

| System or Area | Current Implementation | Package Assets Proposed | Integration Method | Risk | Validation |
|---|---|---|---|---|---|

Also provide:

- Proposed graveyard location
- Proposed zone dimensions
- Recommended player-level range
- Proposed gameplay purpose
- Scene and prefab files expected to change
- New files expected
- Performance risks
- Shader or material risks
- Networking risks
- Terrain risks
- Navigation risks

## Phase 1 Deliverable

Provide:

- Project audit
- Package inventory
- Technical asset review
- Demo-scene findings
- Proposed zone concept
- Integration matrix
- Implementation order
- Risks and blockers

Stop and wait for approval.

---

# PHASE 2 — CREATE A GAME-OWNED ENVIRONMENT LIBRARY

Create a safe project-owned structure similar to:

```text
Assets/
  Game/
    Environment/
      Graveyard/
        Scenes/
        Prefabs/
          Architecture/
          Graves/
          Nature/
          Props/
          Interactive/
          Encounters/
          Lighting/
          Audio/
        Materials/
        Terrain/
        Decals/
        VFX/
        Scripts/
          Runtime/
          Editor/
        Navigation/
        Tests/
        Documentation/
        Backup/
```

Adapt the root folder to the project’s existing conventions.

## 2.1 Prefab Variants

Create prefab variants or wrapper prefabs for frequently used assets.

Possible categories:

- Grave clusters
- Tombstone rows
- Broken tombstone groups
- Cemetery gate
- Fence segments
- Fence corners
- Crypt entrance
- Mausoleum
- Ritual area
- Bone pile
- Coffin scene
- Dead-tree cluster
- Rock cluster
- Path-edge cluster
- Ruin cluster
- Catacomb room
- Catacomb corridor
- Graveyard encounter arena

Prefab variants may add:

- Correct static flags
- Correct layer
- Correct collider
- LOD tuning
- Occlusion settings
- NavMesh modifiers
- Audio emitters
- Interaction scripts
- Quest markers
- Spawn-point markers
- Minimap markers
- Addressable labels

Do not modify the original vendor prefab.

## 2.2 Material Strategy

Create game-owned material variants only where needed.

Verify:

- Render-pipeline compatibility
- Normal maps
- Mask maps
- Metallic and smoothness values
- Alpha clipping
- Double-sided foliage behavior
- Wind support
- GPU instancing
- Terrain compatibility
- Decal compatibility
- Fog compatibility
- Lightmap compatibility

Do not run a broad automatic material conversion without first backing up and reviewing the result.

## 2.3 Asset Validation Tool

When useful, create an editor validator that checks:

- Missing materials
- Missing shaders
- Missing textures
- Invalid colliders
- Missing LODGroup
- Excessive material slots
- Non-uniform scale
- Incorrect layers
- Incorrect static flags
- Excessive shadow distance
- Missing lightmap UVs
- Large texture memory
- Prefabs modified inside the vendor folder

---

# PHASE 3 — DESIGN THE GRAVEYARD ZONE

## 3.1 Zone Concept

Build a coherent environment with distinct subareas.

Recommended structure:

1. **Approach Road**
   - Transition from the existing biome
   - Directional landmarks
   - Sparse graves and dead vegetation
   - First view of the cemetery gate

2. **Outer Cemetery**
   - Lower-threat enemies
   - Broken fences
   - Scattered graves
   - Introductory quest objects
   - Clear paths

3. **Central Graveyard**
   - Dense tombstone layout
   - Major mausoleum or chapel landmark
   - Elite encounters
   - Ritual or boss arena
   - Strong visual storytelling

4. **Crypt District**
   - Larger structures
   - Vertical composition
   - Entrances to underground content
   - Stronger enemies
   - Better loot

5. **Catacomb or Dungeon Entrance**
   - Transition point to an additive dungeon scene
   - Loading, portal, door, or instance trigger
   - Clear multiplayer entry behavior

6. **Hidden Area**
   - Optional exploration reward
   - Rare resource
   - Secret quest
   - Lore object
   - Treasure or mini-boss

## 3.2 Layout Principles

The zone must have:

- Clear primary path
- Optional secondary routes
- Recognizable landmarks
- Sightline control
- Combat spaces
- Rest or safe areas where appropriate
- Escape paths
- Ranged-combat visibility
- Melee-combat clearance
- No collision traps
- No unavoidable dead ends
- No decorative clutter blocking movement
- Clear entrances and exits

Avoid uniform rows of copied assets unless deliberately designed.

Vary:

- Rotation
- Scale within realistic limits
- Damage state
- Clustering
- Elevation
- Vegetation coverage
- Burial density
- Prop composition

Do not use random placement without artistic review.

## 3.3 Environmental Storytelling

Communicate history through placement.

Possible themes:

- Abandoned royal cemetery
- Necromancer occupation
- Battlefield burial ground
- Plague cemetery
- Corrupted nature
- Haunted family crypt
- Desecrated holy ground

Use visual clues such as:

- Open coffins
- Broken gates
- Disturbed graves
- Ritual circles
- Bones
- Abandoned tools
- Damaged statues
- Barricades
- Burn marks
- Blood or magical residue when appropriate
- Faction banners when available from the game

Do not introduce lore that contradicts the existing game.

---

# PHASE 4 — TERRAIN AND BIOME INTEGRATION

## 4.1 Terrain Blending

Blend the graveyard naturally into the surrounding terrain.

Use appropriate:

- Terrain layers
- Ground textures
- Mud
- Dirt
- Grass
- Dead grass
- Rock
- Moss
- Path materials
- Decals
- Vertex painting when available

Avoid hard visible seams at the edge of the zone.

## 4.2 Paths

Create readable paths connecting:

- World entrance
- Cemetery gate
- Central landmark
- Crypts
- Boss area
- Dungeon entrance
- Exit route

Paths should remain visible under different lighting and weather.

## 4.3 Vegetation

Use vegetation to frame movement rather than obstruct it.

Requirements:

- Keep large trees away from critical camera paths.
- Avoid foliage that hides enemies unfairly.
- Avoid dense collision on small plants.
- Use GPU instancing when supported.
- Use LODs.
- Use billboards or impostors for distant vegetation when the project supports them.
- Match the game’s wind system.
- Keep vegetation density within performance targets.

## 4.4 Terrain Holes and Underground Areas

If building crypts or catacombs under terrain:

- Use terrain holes correctly.
- Prevent light leaks.
- Prevent players from seeing the underside of terrain.
- Ensure collision continuity.
- Ensure navigation continuity.
- Use additive scenes when appropriate.
- Preserve multiplayer transition logic.

---

# PHASE 5 — COLLISION, NAVIGATION, AND PLAYER MOVEMENT

## 5.1 Collision

Use the lowest-cost collider that preserves gameplay.

Prefer:

- Box colliders
- Capsule colliders
- Compound primitive colliders
- Simplified mesh colliders for static architecture

Avoid:

- Convex mesh colliders on complex static structures unless required
- High-poly mesh colliders
- Colliders on tiny decorative vegetation
- Invisible collision extending far beyond visible geometry

Validate:

- Cemetery gates
- Fences
- Tombstones
- Crypt stairs
- Mausoleum entrances
- Coffins
- Rocks
- Trees
- Interior floors
- Doorways
- Dungeon transitions

## 5.2 NavMesh

Integrate with the project’s current AI navigation system.

Configure:

- Walkable paths
- Stairs
- Ramps
- Crypt interiors
- Combat arenas
- Grave clusters
- Fence boundaries
- Off-mesh links when needed
- NavMesh obstacles for movable objects
- NavMesh modifiers for non-walkable decoration

Test multiple enemy sizes if the MMO supports them.

Do not let enemies:

- Spawn outside navigation
- Become trapped between graves
- Walk through crypt walls
- Fall through terrain
- Block narrow paths permanently
- Chase players infinitely outside the encounter area

## 5.3 Player Movement Validation

Test:

- Walking
- Running
- Sprinting
- Jumping
- Dodging
- Mounts
- Large player characters
- Camera collision
- First-person camera if supported
- Third-person camera
- Controller navigation

Prevent:

- Getting stuck behind tombstones
- Camera clipping through walls
- Falling through crypt stairs
- Standing on unintended decorative geometry
- Jumping outside world boundaries

---

# PHASE 6 — LIGHTING, WEATHER, AND ATMOSPHERE

## 6.1 Lighting

Match the project’s lighting strategy.

Support:

- Day/night cycle
- Moonlight
- Directional light
- Baked lighting
- Mixed lighting
- Realtime lights only where justified
- Light probes
- Reflection probes
- Shadow settings

Use lighting to emphasize:

- Cemetery entrance
- Major mausoleum
- Ritual site
- Boss arena
- Dungeon entrance
- Safe path

Do not add large numbers of shadow-casting realtime lights.

## 6.2 Fog and Post-Processing

Use the existing project systems.

Possible adjustments:

- Local fog
- Color grading
- Vignette
- Bloom
- Ambient occlusion
- Volumetric effects
- Exposure
- Desaturation

Keep:

- Enemies visible
- Paths readable
- UI readable
- Multiplayer combat fair
- Performance stable

Do not make the environment so dark or foggy that gameplay becomes frustrating.

## 6.3 Weather

Integrate with existing weather when available:

- Rain
- Mist
- Wind
- Thunder
- Falling leaves
- Dust
- Fireflies
- Magical particles

Do not create a second competing weather manager.

## 6.4 VFX

Use restrained effects for:

- Ghostly mist
- Ritual energy
- Grave disturbance
- Dungeon entrance
- Magical corruption
- Ambient insects
- Dust
- Leaves

Pool frequently spawned effects.

Static ambient effects should not require network synchronization.

---

# PHASE 7 — AUDIO

Create or configure audio zones using existing licensed audio.

Possible layers:

- Wind
- Distant crows
- Rustling trees
- Insects
- Creaking gates
- Whispering ambience
- Crypt reverb
- Distant undead
- Ritual hum
- Thunder when supported

Requirements:

- Use the existing audio manager.
- Use spatial audio appropriately.
- Add reverb zones for interiors when supported.
- Avoid placing too many looping AudioSources.
- Use grouped ambient emitters.
- Respect master, music, ambience, and effects volume settings.
- Do not import unlicensed external audio.

---

# PHASE 8 — MMO GAMEPLAY INTEGRATION

## 8.1 Enemy Encounters

Use the existing enemy and spawn systems.

Possible encounter tiers:

- Outer graveyard: low-level undead
- Central cemetery: standard groups
- Crypt district: elite enemies
- Ritual site: mini-boss
- Mausoleum or catacomb: boss or dungeon entry

All gameplay-critical spawning must be server-authoritative.

Spawn points must include:

- Valid navigation
- Safe minimum distance from players
- Leash area
- Respawn timing
- Group composition
- Elite designation
- Population cap
- Server ownership

Do not place active enemies directly in player arrival points.

## 8.2 Quest Integration

Connect the region to the current quest system.

Possible quest types:

- Investigate disturbed graves
- Defeat undead
- Recover relics
- Close ritual sites
- Escort an NPC
- Find missing travelers
- Enter the crypt
- Defeat a necromancer
- Collect grave-soil or bone fragments
- Discover lore markers

Do not build a new quest framework.

## 8.3 Interactable Objects

Possible server-authoritative interactions:

- Crypt doors
- Coffins
- Ritual altars
- Quest markers
- Lore tablets
- Treasure chests
- Gathering nodes
- Dungeon entrances
- Fast-travel points

Interactive state must follow the project’s persistence and replication rules.

Decorative objects must remain non-networked.

## 8.4 Loot and Rewards

Use existing loot tables and server authority.

Do not store trusted rewards on clients.

## 8.5 PvE and PvP Considerations

Check:

- Line of sight
- Choke points
- Spawn camping
- Safe-zone boundaries
- PvP exploit positions
- Ranged high-ground advantage
- Invisible collision abuse
- Hiding inside geometry
- Logout locations
- Mount restrictions
- Fast-travel safety

## 8.6 Minimap and World Map

Update:

- Zone name
- Minimap bounds
- Roads and paths
- Graveyard landmark
- Dungeon entrance
- Quest markers
- Fast-travel point
- Boss or event markers when appropriate
- Fog-of-war behavior

Do not expose hidden areas prematurely.

---

# PHASE 9 — STREAMING, ADDRESSABLES, AND SCENE ORGANIZATION

Follow the project’s existing world architecture.

Recommended separation when compatible:

```text
Graveyard_Terrain
Graveyard_Environment
Graveyard_Gameplay
Graveyard_Lighting
Graveyard_Audio
Graveyard_Navigation
Graveyard_Interior
```

Possible loading approach:

- Additive scenes
- Addressables
- World-streaming cells
- Terrain tiles
- Dungeon instance scene

Requirements:

- Static decoration loads together efficiently.
- Gameplay data remains server-authoritative.
- Unloading the zone releases memory correctly.
- Lightmaps and reflection probes load correctly.
- NavMesh data loads before AI activation.
- Players cannot fall through unloaded terrain.
- Spawn systems wait for scene readiness.
- Clients joining late receive correct interactive state.

Do not introduce a new streaming framework unless approved.

---

# PHASE 10 — PERFORMANCE OPTIMIZATION

## 10.1 Performance Budget

Use the project’s existing budgets when available.

Otherwise propose measurable targets for:

- Frame rate
- Main-thread time
- GPU frame time
- Draw calls
- SetPass calls
- Visible triangles
- Texture memory
- Scene memory
- Shadow casters
- Realtime lights
- Networked objects
- Active AI
- Particle count

Test using a representative player camera and realistic combat load.

## 10.2 Rendering Optimization

Review:

- LODGroups
- LOD distances
- GPU instancing
- Static batching
- SRP Batcher compatibility
- Occlusion culling
- Shadow distance
- Shadow cascade count
- Reflection probes
- Light probes
- Material count
- Texture atlases
- Overdraw
- Alpha-clipped vegetation
- Decals
- Particle effects

Do not merge meshes in a way that breaks occlusion, streaming, or editing.

## 10.3 Texture Optimization

Review:

- Maximum texture size
- Compression
- Mipmaps
- Streaming mipmaps
- Normal-map import
- Alpha usage
- Platform overrides
- Read/Write Enabled
- Duplicate textures

Do not reduce quality blindly. Use distance and importance.

## 10.4 CPU Optimization

Avoid:

- Per-frame `Find` calls
- Per-frame terrain searches
- Excessive physics colliders
- Excessive MonoBehaviours on decoration
- One Update method per decorative prop
- Frequent dynamic NavMesh rebuilding
- Excessive realtime probes
- Unpooled VFX
- Network components on static scenery

## 10.5 MMO Network Optimization

Static environment content should create nearly zero ongoing network traffic.

Measure:

- Networked object count
- Spawn messages
- State-sync frequency
- Enemy replication
- Interactable replication
- Late-join synchronization
- Server CPU for zone AI
- Player density impact

---

# PHASE 11 — EDITOR TOOLS

When useful, create game-owned editor tools.

Possible tools:

## Graveyard Placement Tool

Features:

- Select package prefab variants
- Brush placement
- Align to terrain
- Randomize rotation
- Controlled scale variation
- Minimum spacing
- Slope restrictions
- Exclusion zones
- Parent under correct scene root
- Mark static correctly
- Undo support
- Preview mode

## Graveyard Validation Tool

Check:

- Vendor files modified
- Missing materials
- Missing shaders
- Missing colliders
- Invalid LODGroups
- Incorrect layers
- Incorrect static flags
- Duplicate NetworkIdentity components
- Decorative objects with networking
- Spawn points off NavMesh
- Blocking colliders
- Missing minimap registration
- Excessive realtime lights
- Excessive shadow casters
- Missing terrain blending
- Scene objects outside streaming bounds

Do not create editor-only code in runtime assemblies.

---

# PHASE 12 — TESTING AND VALIDATION

## 12.1 Functional Testing

Validate:

- Player can enter and leave the region.
- World streaming loads correctly.
- Terrain and collision are present before player movement.
- Enemies spawn correctly.
- Enemies navigate correctly.
- Combat works among graves and structures.
- Quest objectives update.
- Interactions work.
- Loot is server-authoritative.
- Dungeon entry works.
- Minimap updates.
- Fast travel works when included.
- Respawning works.
- Multiple players can enter together.
- Late-joining clients see correct state.

## 12.2 Visual Testing

Test:

- Day
- Night
- Rain
- Fog
- Low and high graphics settings
- Multiple resolutions
- Different camera distances
- Interior and exterior transitions

Check:

- Material errors
- Pink shaders
- Texture seams
- Terrain seams
- Light leaks
- Floating props
- Buried props
- Repeating patterns
- Stretched textures
- Missing vegetation
- LOD popping
- Shadow popping
- Reflection errors

## 12.3 Navigation Testing

Test:

- Player paths
- Enemy paths
- Boss arena
- Crypt entrance
- Stairs
- Narrow gates
- Fence boundaries
- Mount movement
- Large enemies
- Flee and leash behavior

## 12.4 Multiplayer Testing

Test with:

- Host and one client
- Dedicated server and multiple clients when supported
- Late join
- Disconnect and reconnect
- Multiple simultaneous encounters
- Multiple quest states
- Loot ownership
- Shared interactables
- Respawn
- Scene transition
- Zone unload and reload

## 12.5 Performance Testing

Profile:

- Empty zone
- Normal exploration
- Heavy combat
- Maximum expected player density
- Night lighting
- Weather
- Boss encounter
- Dungeon entrance
- Scene loading
- Scene unloading

Use Unity Profiler, Frame Debugger, Memory Profiler, and the render-pipeline-specific analysis tools available in the project.

---

# AUTOMATED TESTS

Create tests where practical for:

- Graveyard scene loads
- Required scene roots exist
- Required materials resolve
- No vendor assets were modified
- No missing scripts
- No missing prefab references
- No static decoration has network identities
- Spawn points are registered
- Spawn points are inside allowed bounds
- Interactables have required server authority
- Dungeon entrance references a valid destination
- Minimap registration exists
- Quest markers resolve
- Terrain and collision roots exist
- Addressable labels resolve when used
- Validation tool reports no blocking errors

---

# MANUAL TEST CHECKLIST

Create a checklist with at least:

1. Load the MMO world.
2. Travel to the graveyard entrance.
3. Verify terrain loads before arrival.
4. Walk the main road.
5. Explore the outer cemetery.
6. Enter the central graveyard.
7. Fight a normal enemy group.
8. Fight an elite encounter.
9. Navigate around graves and fences.
10. Enter a crypt or mausoleum.
11. Use a quest interactable.
12. Confirm server-authoritative quest progress.
13. Collect loot.
14. Verify minimap and world-map markers.
15. Enter the dungeon or catacomb transition.
16. Test with another player.
17. Test late join.
18. Test day and night.
19. Test weather.
20. Test low and high quality settings.
21. Check Unity Console.
22. Profile frame rate and memory.
23. Exit the zone.
24. Confirm the scene unloads correctly.
25. Return and confirm persistent state.

---

# DEFINITION OF DONE

The integration is complete only when:

1. The imported package has been audited.
2. Original vendor files remain unchanged.
3. A game-owned graveyard environment library exists.
4. The graveyard is integrated into the existing MMO world.
5. Terrain transitions are natural.
6. Paths and landmarks clearly guide players.
7. Collision is reliable.
8. Player and enemy navigation work.
9. Multiplayer authority is preserved.
10. Static decoration creates no unnecessary network traffic.
11. Enemies, quests, loot, and interactions use existing game systems.
12. Lighting and atmosphere match the project.
13. The area works during day, night, and supported weather.
14. The minimap and world map are updated.
15. Streaming and scene loading work.
16. Performance meets the agreed budget.
17. No new compile errors exist.
18. No new runtime exceptions exist.
19. No missing materials, shaders, scripts, or references remain.
20. Documentation and validation reports are complete.

---

# REPORT AFTER EACH PHASE

Use this format:

## Completed

Describe finished work.

## Files Added

List each new file.

## Files Modified

List each modified file.

## Vendor Assets Referenced

List package assets used without modifying them.

## Scenes Affected

List scenes created or changed.

## Validation Performed

List compile, play-mode, multiplayer, navigation, and performance checks.

## Performance Results

Report measurable profiling results.

## Remaining Work

List unfinished items.

## Risks and Caveats

Document render-pipeline, networking, shader, lighting, navigation, and performance concerns.

---

# FINAL DELIVERABLES

Provide:

- Completed graveyard region
- Game-owned prefab variants
- Material variants
- Terrain layers
- Lighting and atmosphere setup
- Navigation data
- Enemy spawn configuration
- Quest and interaction integration
- Minimap and map integration
- Streaming configuration
- Optional placement and validation tools
- Automated test results
- Manual test checklist
- Performance report
- Changed-file report
- Known limitations
- Instructions for extending the graveyard later

---

# QUALITY STANDARD

The final result must look like an intentionally designed MMO zone, not a random collection of purchased assets.

Prioritize:

- Strong composition
- Clear navigation
- Environmental storytelling
- Consistent scale
- Believable asset placement
- Terrain blending
- Fair combat spaces
- Multiplayer reliability
- Efficient rendering
- Low network overhead
- Maintainability

Do not claim completion based only on a visually attractive screenshot. The zone must be playable, network-safe, optimized, and fully integrated with the existing game systems.
