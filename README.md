# Radiant Pool

A 3D online co-op party CRPG for 2–4 friends: create characters, take quests from the
hub town of **Aldenmere**, clear the ruined quarters block-by-block (turn-based tactical
combat, SRD 5.1 rules), level 1→5, and free the city from the Hollow Flame. Structure
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

## Rules tests only (no Unity required)
```powershell
dotnet test rules/RadiantPool.Rules.sln
```

## Art credits
Environment models from the CC0 **Kenney** asset kits ([kenney.nl](https://kenney.nl)):
Fantasy Town Kit 2.0 and Pirate Kit — Creative Commons Zero, thank you Kenney!
Characters from the CC0 **KayKit** packs by Kay Lousberg
([kaylousberg.itch.io](https://kaylousberg.itch.io)): Adventurers 2.0 and
Skeletons 1.1 — thank you Kay!
Spell/action icons from [game-icons.net](https://game-icons.net) by **Lorc** and
**Delapouite**, licensed [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)
(`game/Assets/Resources/SpellIcons`).

## License / attribution
This work includes material taken from the System Reference Document 5.1 ("SRD 5.1") by
Wizards of the Coast LLC and available at
https://dnd.wizards.com/resources/systems-reference-document. The SRD 5.1 is licensed
under the Creative Commons Attribution 4.0 International License available at
https://creativecommons.org/licenses/by/4.0/legalcode.
