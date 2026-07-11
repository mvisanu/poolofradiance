# Playtest checklists — 3b combat, 3d quest loop, 3e loot/vendor/rest

Prereq: build per `docs/SETUP.md`, host + join two instances (`docs/playtest-phase2.md`).

## 3b — turn-based grid combat (synced)
1. Host and joiner both walk into a warehouse-yard encounter volume → both screens freeze
   explore movement, a checkerboard grid appears, all party members snap to cells,
   initiative list shows every unit in the same order on both machines.
2. On your turn: move with the compass buttons (5 ft per step, budget enforced), Attack an
   adjacent Marsh Skulker, or as the Cleric/Wizard Cast (Sacred Flame / Fire Bolt / Sleep…)
   → both clients see identical rolls in the combat log; illegal actions (attack out of
   reach, second action) show an amber rejection only to the actor.
3. Kill all monsters → "Victory!" + XP in both logs, grid disappears, movement unfreezes.
   Expect: identical HP everywhere, no desync after 3+ encounters.

## 3d — quest loop
1. Press E at Councilor Veresk (gold capsule) → accept "Retake the Old Docks"; both
   players' journals (J) show 0/3.
2. Clear the three required encounters → journal ticks to 3/3 on both machines, notice
   banner "Return to Councilor Veresk".
3. Turn in at Veresk → +300 XP each (level-ups announced), +100 party gold, journal marks
   quest ✔, zone shows "pacified" — walk back through cleared yards: no re-trigger, ever.

## 3e — loot, vendor, rest
1. Win any encounter → loot banner (gold + items) on both clients; journal shows shared
   gold/potions/salvage.
2. At the Salvage Exchange (green capsule): Sell all salvage → gold increases by the
   listed sum; Buy Potion (50g) → potion count +1 on both clients.
3. Take damage, then journal → Drink potion (+2d4+2 HP) and Long rest (full HP + slots,
   Wizard/Cleric slots visibly restored next combat). The optional warehouse encounter
   drops the bonus cache table.
