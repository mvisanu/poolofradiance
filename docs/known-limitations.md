# Known limitations — v1 vertical slice

Gameplay
- One zone (Old Docks) is playable in-engine; Drowned Market and Glasslit Temple exist as
  content JSON but have no scenes yet (they arrive with the 3f art pass).
- Level-ups apply automatically (HP/slots/features per SRD); no choice UI (e.g. ASI
  allocation is deferred — flagged for 3c-full).
- Rogue Sneak Attack, Fighter Action Surge/Second Wind, and Cleric Channel Divinity are
  listed as features but not yet usable actions in combat.
- Bless approximates concentration as a fixed 10-round duration; only 10 spells total.
- Potions are party-stash items usable out of combat only; no per-character inventory or
  equipment swapping yet (3e-full).
- Monster AI is a simple "close and swing" melee routine; ranged monster attacks unused.

Multiplayer
- Explore movement is client-authoritative (combat is fully server-validated); a modified
  client could teleport while exploring. Acceptable for the friends-only trust model.
- Host quit = session over (host migration out of scope v1). Autosave protects progress.
- Invite codes encode LAN IPs — internet play needs a VPN or port forward (docs/hosting.md).
- Late joiners during an active combat spectate until the next encounter.

Presentation
- Gray-box art, IMGUI interface, no audio yet (3f art pass pending asset-pack purchase).
- No keybind remapping (fixed WASD/E/J/F5/Esc).
