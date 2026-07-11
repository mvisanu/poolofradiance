# Playtest checklists — 3c character creation, Phase-4 save/rejoin

## 3c — creation + point buy
1. Launch: pick class (presets load), race, and adjust abilities with +/− — points meter
   caps at 27/27, scores clamp to 8–15; Host/Join disabled while the build is invalid.
2. Host with a custom build → in combat your HP/AC/spells match the build (e.g. Wizard
   Int 15 → spell DC 13); a second player joins with a different class and sees their own
   loadout.
3. Try to cheat: the server re-validates — an out-of-budget build (edited client) falls
   back to the class default, never crashes the host.

## Phase 4 slice — save / load / rejoin
1. Clear an encounter → autosave notice; `%USERPROFILE%\Saved Games\RadiantPool\campaign.json`
   exists and lists quest state, gold, stash, roster. F5 (host) saves on demand.
2. Quit both clients. Host again with the same display name → campaign state, XP/level,
   current HP, spent spell slots, and cleared (still-pacified) encounters all restored.
3. Joiner drops mid-session and rejoins with the same name → same character comes back
   ("X rejoins the party"), not a fresh level-1.
