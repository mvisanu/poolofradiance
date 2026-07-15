# Importing the Asset Store packs

Asset Store packages can only be downloaded through the Unity **editor** while
signed in to a Unity account — they can't be fetched by the build scripts. The
game is wired so each pack **drops in with zero code changes** once imported.

One-time flow, per pack:

1. Open the page in a browser, sign in, click **Add to My Assets**.
2. Open the project in the Unity editor (`game/`), sign in.
3. `Window > Package Manager` → dropdown **My Assets** → select the pack →
   **Download** → **Import**.
4. Copy/rename the files as listed below (the imported pack itself can live
   anywhere under `Assets/`; only the names in `Resources/` matter).
5. Run the normal build (`scripts/build-all.ps1`). Never commit Asset Store source files.

For the audio packs already downloaded on this workstation, the selective installer is
the fastest path. It preserves Unity's importer metadata and writes only the clips used by
the game into gitignored `Resources` folders:

```powershell
python scripts/install-audio-assets.py
```

For the owned environment collection, install every compatible low-poly pack plus the
small set of hand-painted ground textures in one command:

```powershell
python scripts/install-environment-assets.py
python scripts/install-graveyard-assets.py
```

The installers read Unity's local Asset Store cache and install RPG Poly Pack Lite,
Low-Poly Simple Nature Pack, Low Poly Dungeons Lite, PBR Graveyard and Nature Set 2.0,
plus eight theme-selected grass and dirt textures. The Graveyard installer follows the
dependency closure of 29 authored prefabs instead of importing its entire 3.3 GB package.
All source art and generated local materials are gitignored. Run the bootstrap afterward;
the prefab libraries become deterministic site dressing and the ground helper selects
clean, swamp, civic, or corrupted terrain for each site theme.

## Where each pack goes

| Pack | Wire-up |
|---|---|
| [RPG & MMO UI 7](https://assetstore.unity.com/packages/2d/gui/rpg-mmo-ui-7-114435) | In Package Manager > My Assets click **Download only**, close this project's editor, then run `scripts/import-rpg-mmo-ui7.ps1`. The selective installer preserves all 286 image assets locally, excludes the package's legacy scripts/prefabs/demo scenes, and `RpgMmoUi7Art` bakes 19 semantic IMGUI roles into ignored `Assets/Resources/UI/RpgMmoUi7`. `Theme.Apply()` carries the skin across title, character creation, inventory, journal, settings, level-up, session, vendor/smith/NPC/travel/objective panels, quest/minimap HUD, hotbar, and combat UI. The package has no live font files; all UI7 controls use the bundled OFL stack: MedievalSharp titles, Source Serif controls, Inter body/fields. Verify the built player with `RadiantPool.exe -uiskincapture <png>`; it logs `[UiSkinTest] PASS - 19/19 roles` plus the typography assertion. |
| PBR Graveyard and Nature Set 2.0 | In Package Manager > My Assets click **Download only**, close this project's editor, then run `python scripts/install-graveyard-assets.py`. It installs 29 authored architecture, grave, rock, foliage, root, and tree prefabs plus only their dependencies. `PolyPackArt` uses authored prefabs (not internal FBX submeshes), recovers PBR maps from serialized material slots, converts them to URP Lit, and makes this pack the dominant remote-site perimeter. Crypt and necropolis sites also receive scaled grave rings. |
| [FREE RPG Fantasy Spell Icons](https://assetstore.unity.com/packages/2d/gui/icons/free-rpg-fantasy-spell-icons-200511) | Overwrite the same-named PNGs in `Assets/Resources/SpellIcons/` (`fire_bolt.png`, `magic_missile.png`, `burning_hands.png`, `sleep.png`, `sacred_flame.png`, `guiding_bolt.png`, `cure_wounds.png`, `healing_word.png`, `bless.png`, `attack.png`, `dodge.png`, `cast.png`, `end_turn.png`). Current icons are CC-BY game-icons.net placeholders. |
| [Caves and Dungeons music](https://assetstore.unity.com/packages/audio/music/caves-and-dungeons-292342) | `install-audio-assets.py` selects an exploration loop plus distinct Old Docks, Drowned Market, Sunken Warcamp, Glasslit Temple, and Ashen Ward loops. |
| Action RPG Battle Music | `install-audio-assets.py` installs four looped encounter tracks. `GameAudio` chooses a new track per fight without immediately repeating the last one. |
| AnyMMORPG | The installer selects its weapon swings, sword/blunt impacts, misses, critical metal hits, bow releases, and fire/arcane/radiant/healing/control/shield spell recordings. These replace procedural combat cues only; no AnyMMORPG code or prefabs are imported. |
| [Orc Warrior](https://assetstore.unity.com/packages/3d/characters/orc-warrior-orc-character-200207) | Save a prefab of the character as `Assets/Resources/Characters/Orc.prefab`. Used by the Orc Raiders and the Karg Splitjaw boss fight (green-tinted KayKit Barbarian until then). If it has its own Animator, leave it on the prefab; otherwise combat still poses/positions it. |
| [Spiders](https://assetstore.unity.com/packages/package/236418) | Prefab as `Assets/Resources/Characters/Spider.prefab` — used by Giant Spiders in the wilds. |
| [Bears](https://assetstore.unity.com/packages/package/228910) | Prefab as `Assets/Resources/Characters/Bear.prefab` — used by Brown Bears in the wilds. |
| [Goblins](https://assetstore.unity.com/packages/package/293432) | Prefab as `Assets/Resources/Characters/Goblin.prefab` — used by Goblin Ambushers (small green KayKit Rogue until then). |
| [Low Poly Medieval Props](https://assetstore.unity.com/packages/3d/props/low-poly-simple-medieval-props-258397) | Scene dressing is generated by `ProjectBootstrap` from Kenney kits — imported props need bootstrap placements. After importing, ask Claude to wire specific props (say which: barrels, fences, tents for the warcamp, etc.). |
| [RPG Poly Pack - Lite](https://assetstore.unity.com/packages/3d/environments/landscapes/rpg-poly-pack-lite-148410) | **Already wired — zero code changes.** Import it anywhere under `Assets/` and re-bootstrap: `PolyPackArt` finds it, converts its materials to URP, sorts every prefab into buckets by name (tree / pine / rock / cliff / bush / grass / flower / mushroom / log / house / ruin / fence / tent / prop) and the world dresses itself out of those buckets — trees, wilds sites, warehouses, town houses, temple ruins. No pack ⇒ falls back to the Kenney kits, so the build never breaks. |

## Procedural environment pack coverage

- **RPG Poly Pack Lite:** buildings, tents, fences, paths, props, and abandoned clutter.
- **Low-Poly Simple Nature Pack:** trees, bushes, rocks, branches, stumps, grass, flowers,
  and mushrooms in seeded perimeter clusters at every remote campaign site.
- **PBR Graveyard and Nature Set 2.0:** authored PBR trees, ivy, fern, roots, rocks, grave
  markers, church/ruin pieces, and props across all 22 remote sites; cemetery rings at
  crypt and necropolis destinations. Non-tree dressing is normalized to human scale.
- **Low Poly Dungeons Lite:** broken walls, columns, floors, lights, books, pottery,
  furniture, and debris at keeps, crypts, caves, observatories, gates, citadels, and spires.
- **Handpainted Grass Ground Textures:** tiled normal, dark, swamp, blue-tinted, civic dirt,
  corrupted, or over-corrupted ground selected by campaign theme.

## The fast path (RPG Poly Pack, and any Asset Store pack)

Downloading needs the editor **once**; everything after that is headless:

1. Unity Hub → open `game/` → sign in → `Window > Package Manager` → **My Assets** →
   find the pack → **Download** (you do NOT need to click Import).
2. Close the editor, then:

   ```powershell
   scripts/import-assetstore.ps1              # newest cached pack matching "poly"
   scripts/import-assetstore.ps1 -Match "RPG Poly Pack"
   scripts/import-rpg-mmo-ui7.ps1            # selective UI7 image-only installer
   python scripts/install-graveyard-assets.py # selective PBR Graveyard/Nature installer
   ```

   That imports the cached `.unitypackage` in batchmode, converts materials to URP,
   re-bootstraps the scene, and prints what the pack actually contained
   (`[PolyPack] Assets/…: Tree x12, Rock x8, House x5, …`).
3. `scripts/build-all.ps1`

Why the manual download: the Asset Store requires a signed-in editor session, so the
build scripts can't fetch it — but the editor leaves the `.unitypackage` in
`%APPDATA%\Unity\Asset Store-5.x\`, and `-importPackage` takes it from there.

Notes:
- The scene is regenerated by the bootstrap — never hand-place things in the
  scene; put art under `Resources/` (or ask for bootstrap placements).
- Character prefabs are looked up by exact name via `CharacterVisuals.Attach`;
  a missing prefab falls back to the KayKit stand-in, then a capsule.
- **Asset Store EULA — do NOT commit the packs.** They are licensed to *your Unity
  account*; redistributing the raw assets is not allowed, and **this repo is public
  (github.com/mvisanu/poolofradiance)**, so pushing them there would be redistribution.
  They are gitignored. Each machine imports them itself — which costs nothing, because
  every pack here is a drop-in: import + re-bootstrap and the game picks it up, and with
  no pack installed the world falls back to the CC0 Kenney kits and still builds.
- Audio WAVs and their `.meta` files under `Resources/Music` and `Resources/Sfx` are
  intentionally gitignored. They are included in local Unity builds but are never
  redistributed as raw files through the public repository.
