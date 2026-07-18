I want you to help me set up MCP tools and then upgrade my Unity game's 
graphics to look more realistic while keeping all my existing assets and 
gameplay functionality intact.

Project details (fill these in):
- Unity version: [e.g., Unity 6]
- Render pipeline: [Built-in / URP / HDRP]
- OS: [Windows / Mac]
- Blender installed: [yes/no — version]
- Game type and current art style: [e.g., 3D adventure, currently 
  flat/simple lighting]
- Target platform: WebGL (browser) — keep this in mind for all 
  performance decisions

PHASE 1 — Set up the MCP tools:
1. Find and install a well-maintained open-source Unity MCP connector 
   (search GitHub for current options, pick the most active one 
   compatible with my Unity version). Walk me through:
   - Installing the Unity package/plugin side
   - Registering the MCP server in my Claude Code config
   - Verifying the connection works with a simple test command
2. Install Blender MCP (the open-source GitHub project):
   - Install the Blender addon side
   - Register it in my Claude Code MCP config
   - Verify with a simple test (e.g., create a cube in Blender)
3. If either tool fails to connect, diagnose and fix before moving on.

PHASE 2 — Audit my project for realism gaps:
1. Open my main scene(s) via Unity MCP and inventory:
   - Current lighting setup (light types, shadows, GI, skybox)
   - Post-processing (if any)
   - Materials (which are flat colors vs PBR with texture maps)
   - Which meshes are lowest quality / most dated looking
2. Give me a prioritized list of what will most improve realism, 
   with effort estimates. Wait for my approval before changing anything.

PHASE 3 — Upgrade toward realistic graphics (using my assets):
1. Lighting first (biggest realism win):
   - Physically plausible sun/directional light with proper shadows
   - Realistic skybox with matching ambient lighting
   - Baked global illumination + light probes where appropriate
   - Reflection probes for shiny surfaces
2. Post-processing stack:
   - ACES tonemapping, subtle bloom, ambient occlusion, 
     color grading toward filmic/neutral, slight vignette
   - Nothing exaggerated — aim for grounded, photographic look
3. Materials: convert flat/simple materials to proper PBR:
   - Correct metallic/smoothness values per surface type
   - Add normal maps and detail where missing (generate tileable 
     textures if needed)
   - Keep my original textures as the base — enhance, don't replace
4. For meshes that need more detail, use Blender MCP:
   - Import my existing model, improve topology/add detail, 
     re-export as FBX with materials intact
   - Never overwrite my source files — save upgraded versions 
     alongside originals so I can compare and revert

PHASE 4 — WebGL reality check:
1. After each major change, verify the scene still performs well 
   for browser deployment (URP-compatible shaders only, texture 
   sizes reasonable, baked lighting preferred over realtime).
2. Flag any realism feature that is too expensive for WebGL and 
   offer a cheaper alternative that looks similar.

Rules:
- Never delete or replace my assets, scenes, prefabs, or scripts.
- Back up / duplicate any scene before modifying it.
- Work incrementally: one phase at a time, stop for my review 
  after each phase.
- Explain each change in plain language so I learn what's happening.