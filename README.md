# Radiant Pool

A 3D online co-op party CRPG for 2–4 friends: create characters, take quests from the
hub town of **Aldenmere**, clear the ruined quarters block-by-block (turn-based tactical
combat, SRD 5.1 rules), progress from level 1→20 across 39 playable locations, and free
the city and its surrounding realms from the Hollow Flame and Hollow Star. A bestiary of
37 SRD-based creatures fills those fights, and encounters draw a themed, level-matched mix
onto a freshly scattered battlefield each time, so no two clears feel identical. Structure
inspired by classic late-80s party CRPGs; all names, setting, and lore are original.
Explicitly **not** an MMO — think Baldur's Gate 3 / Solasta co-op sessions with a
WoW-style camera.

## Repo map
| Path | What |
|---|---|
| `ARCHITECTURE.md` / `CONTENT-PLAN.md` / `IP-CHECKLIST.md` | Phase 1 design docs |
| `rules/` | Pure-C# SRD 5.1 rules library + xUnit tests (no Unity) |
| `content/` | Zones, quests, monsters, items, loot, dialogue as versioned JSON |
| `game/` | Unity 6 (URP) client/server — FishNet networking |
| `docs/` | Setup, playtest checklists, hosting guide |

## Quick start
1. **One-time**: open Unity Hub → sign in → free Personal license (see `docs/SETUP.md`).
2. `scripts/build-all.ps1` — runs rules tests, bootstraps the Unity project, builds
   `game/Builds/Win64/RadiantPool.exe`.
3. Launch the exe → **Host a campaign** → share the invite code; friends enter it and
   **Join**. Playtest script: `docs/playtest-phase2.md`.

To make a single friend-ready Windows installer after building the player, install
[Inno Setup](https://jrsoftware.org/isdl.php) and run `scripts/build-installer.ps1`.
Share `game/Builds/Installer/RadiantPool-Setup-1.0.1.exe`; the recipient does not need
Unity and can install, launch, and uninstall Radiant Pool like a normal Windows app.

## Rules tests only (no Unity required)
```powershell
dotnet test rules/RadiantPool.Rules.sln
```

## Art credits
Environment models from the CC0 **Kenney** asset kits ([kenney.nl](https://kenney.nl)):
Fantasy Town Kit 2.0, Pirate Kit, Nature Kit, and Survival Kit — Creative Commons
Zero, thank you Kenney!
Creature models from the CC0 **Quaternius** packs ([quaternius.com](https://quaternius.com)):
Ultimate Monsters and Easy Animated Enemy Pack — Creative Commons Zero, thank you
Quaternius!
Characters from the CC0 **KayKit** packs by Kay Lousberg
([kaylousberg.itch.io](https://kaylousberg.itch.io)): Adventurers 2.0 and
Skeletons 1.1 — thank you Kay!
Spell/action icons from [game-icons.net](https://game-icons.net) by **Lorc** and
**Delapouite**, licensed [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
(`game/Assets/Resources/SpellIcons`).
UI fonts: **MedievalSharp** (wmk69), **Source Serif 4** (Adobe), and **Inter**
(Rasmus Andersson), all under the
[SIL Open Font License 1.1](https://openfontlicense.org)
(`game/Assets/Resources/Fonts`, licenses included).
Local builds also use **Caves and Dungeons** (Sonus Novum), **Action RPG Battle
Music** (Chimera Forge Productions), and selected **AnyMMORPG** combat SFX under
the Unity Asset Store EULA. Those licensed source clips are installed from the owner's
Unity cache by `scripts/install-audio-assets.py` and are intentionally not in this repo.
Local owner builds may also use **RPG & MMO UI 7** (Evil) under the Unity Asset Store
EULA. Its source textures and generated runtime skin crops stay gitignored and are not
redistributed in this repository.

## License / attribution
This work includes material taken from the System Reference Document 5.1 ("SRD 5.1") by
Wizards of the Coast LLC and available at
https://dnd.wizards.com/resources/systems-reference-document. The SRD 5.1 is licensed
under the Creative Commons Attribution 4.0 International License available at
https://creativecommons.org/licenses/by/4.0/legalcode.
