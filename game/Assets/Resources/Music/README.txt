Licensed music overrides live here locally and are ignored by Git. Run:

  python scripts/install-audio-assets.py

after downloading the three required packages once through Unity Package Manager >
My Assets. The installer selects and renames the tracks below while preserving their
Unity importer metadata.

Caves and Dungeons:
  explore                 hub / wilderness exploration
  zone_old_docks          The Old Docks
  zone_drowned_market     The Drowned Market
  zone_sunken_warcamp     The Sunken Warcamp
  zone_glasslit_temple    The Glasslit Temple
  zone_ashen_ward         The Ashen Ward

Action RPG Battle Music (randomized per encounter without immediate repeats):
  combat_01               Horns Of War
  combat_02               The Ambush
  combat_03               Enemy Approaches
  combat_04               Swords At Midnight

Missing licensed files are safe: GameAudio falls back to the procedural AudioSynth
music and SFX so a clean public clone remains playable.
