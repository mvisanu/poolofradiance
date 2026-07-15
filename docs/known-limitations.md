# Known limitations — v1 vertical slice

Gameplay
- All 27 campaign destinations share one streamed/generated world scene; separate scene
  loading per destination is not implemented.
- Rogue Sneak Attack, Fighter Action Surge/Second Wind, and Cleric Channel Divinity are
  listed as features but not yet usable actions in combat.
- Bless approximates concentration as a fixed 10-round duration; only 10 spells total.
- Area spells start from one selected unit; there is no free-position area cursor or
  multi-select target picker yet.
- Monster AI greedily chooses its strongest authored in-range attack and moves only when
  necessary. It has no coordinated tactics, retreat behavior, or support abilities yet.

Multiplayer
- Explore movement is client-authoritative (combat is fully server-validated); a modified
  client could teleport while exploring. Acceptable for the friends-only trust model.
- Host quit = session over (host migration out of scope v1). Autosave protects progress.
- Invite codes encode LAN IPs — internet play needs a VPN or port forward (docs/hosting.md).
- Late joiners during an active combat spectate until the next encounter.

Presentation
- Licensed music/SFX are installed per workstation from the owner's Unity Asset Store
  cache and cannot be redistributed in the public repository. A clean clone uses the
  procedural AudioSynth fallback until `scripts/install-audio-assets.py` is run.
- Combat sound is currently a polished 2D mix; positional attenuation, obstruction, and
  reverb zones are future spatial-audio work.
- No keybind remapping (combat also fixes A/C/Backspace/Space for its menu flow).
