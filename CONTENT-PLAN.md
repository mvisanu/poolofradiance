# CONTENT-PLAN.md — Radiant Pool

First region layout (hub + 4 zones), quest structure, and the asset-pack shopping list.
All names below are original — see IP-CHECKLIST.md for the audit.

---

## 1. Setting premise (original lore, Pool of Radiance *structure*)

**Region:** The Sundered Coast.
**Hub town:** **Aldenmere** — once the richest port on the coast, shattered a generation
ago when the **Lightwell** beneath its temple district erupted. The surviving population
holds a single walled quarter (Havenrock) while the rest of the city rots, quarter by
quarter, under monsters drawn to the Lightwell's glow. The **Reclamation Council** hires
adventuring companies to retake the city block-by-block.

**Villain:** **The Hollow Flame** — a bodiless will of living radiance that seeped from
the Lightwell and possesses a chain of hosts (its current host: **Warden Sorrel**, the
city's long-missing lord-protector). It wants the city empty so nothing interrupts its
slow claim on the Lightwell. The campaign uses a classic commission-driven reclamation
arc (possessing entity, corrupted seat of power, Lightwell as final site) with original
names, monsters, dialogue, and lore.

Tone: hopeful reclamation, not grimdark. Every cleared zone visibly relights —
lanterns lit, council banners raised, a merchant or guard appears. The "permanently
pacified" mechanic is also the story's emotional payoff.

---

## 2. First region: hub + 4 zones

### Hub — Havenrock Quarter (safe zone, no combat)
- **Council Hall** — quest-giver: **Councilor Veresk** (main quest chain), quest turn-ins, story beats.
- **The Salvage Exchange** — vendor: buy/sell arms, armor, potions (workflow 4).
- **Temple of the Dawnmother** — healing services, cleric flavor, long-rest anchor.
- **The Leaky Compass** (inn) — party gathering point, long rest, rumor dialogue.
- Spawn plaza with practice dummy (tutorializes attacking without a manual).

### Zone 1 — The Old Docks (levels 1–2, tutorial pace)
- Fiction: smuggler gangs and marsh creatures squat in the drowned warehouses.
- Enemies (SRD-derived stats, original names): **Marsh Skulkers** (bandit-statted),
  **Dock Rats** (giant rat stats), boss: **Skulker Boss Grell** (bandit captain-lite).
- 3 required encounters + 1 optional (locked warehouse, better loot — teaches that
  exploring pays). Clear → docks relight, fisherman NPC returns, Zone 2 unlocks.

### Zone 2 — The Drowned Market (levels 2–3)
- Fiction: the flooded merchant quarter; the dead of the eruption never left.
- Enemies: **Risen Drowned** (zombie stats), **Bonewalkers** (skeleton stats),
  boss: **The Toll-Keeper** (skeleton with a class level).
- 4 required encounters + optional sunken vault (water hazard cells in combat — first
  taste of grid terrain). Clear → market stalls return, second vendor inventory tier.

### Zone 3 — The Sunken Warcamp (levels 3–4)
- Fiction: an army gathering beyond the walls threatens to join the force occupying the
  temple district. The Council commissions the party to break its two pickets and command
  tent before the alliance is sealed.
- 3 required encounters. Clear → the south road is reclaimed and the final assault opens.

### Zone 4 — The Glasslit Temple (levels 4–5, region climax)
- Fiction: the ruined temple district around the Lightwell; the Hollow Flame's cult
  ("the Kindled") prepare Warden Sorrel's final rite.
- Enemies: **Kindled Zealots** (cultist stats), **Kindled Adepts** (cult fanatic stats),
  **Emberlings** (fire-flavored small elemental, magma-mephit-statted).
- 4 required encounters + climax: **Warden Sorrel, Hollow-Flame Host** (knight stats +
  fire rider + 2 adds; two-phase: at 0 HP the Flame abandons him — Sorrel is *saved*,
  not killed, and the Flame recedes into the Lightwell, sequel hook).
- Clear → region complete = **v1 campaign complete** (~6–8 hours for a party of 2).

### Quest chain (workflow 3)
1. `q_muster` — report to Councilor Veresk (teaches journal + turn-in). 
2. `q_clear_docks` → 3. `q_clear_market` → 4. `q_clear_warcamp` →
   5. `q_clear_temple` (each: clear all required
   encounters, return for gold/XP/story beat; journal tracks per-objective counters,
   shared party progress, per-player rewards).
6. Side quests (one per zone, optional): lost cargo (docks), the toll ledger (market),
   evacuate the acolyte (temple) — each exercises a system (loot, dialogue choice, escort-lite).

XP budget tuned so a 2-player party finishing required content hits level 5 at the
temple climax; 3–4 player parties hit ~4–5 (SRD encounter math, tested in 3d).

---

## 3. Asset acquisition plan (no custom art)

Prices are current list prices as of writing — verify at purchase; Synty runs frequent
50–70% sales and everything below is commonly bundled.

| Need | Pack (store) | Price (list) | Notes |
|---|---|---|---|
| Town + ruins modular set | **POLYGON Fantasy Kingdom** — Synty (Unity Asset Store) | $149.99 | Covers Havenrock, docks, market ruins, temple exteriors/interiors. The single most load-bearing purchase. |
| Dungeon/interior fill | **POLYGON Dungeon** — Synty | $59.99 | Sunken vault, temple undercroft. Optional if Kingdom interiors stretch. |
| Player characters (modular, 4 classes × races) | **POLYGON Modular Fantasy Hero Characters** — Synty | $59.99 | Modular = character-creator appearance options for free. |
| Monsters batch 1 | **POLYGON Fantasy Rivals** — Synty | $59.99 | Covers skulkers/cultist bodies; reskin via material swaps. |
| Undead | **POLYGON Skeletons** (or reuse Rivals + materials) | ~$30 | Cut candidate — Rivals + emissive material passes for Risen Drowned. |
| Animations | **Mixamo** (Adobe) | Free | Locomotion, attacks, cast, hit, death. Retarget via Unity humanoid rig. |
| RPG UI kit | **GUI PRO Kit - Fantasy RPG** — Layer Lab (Asset Store) | $45 | Character sheet, inventory grids, journal, vendor UI theme. |
| VFX (spells/hits) | **Polygon Arsenal** — Archanor VFX | $30 | Stylized-matching spell/impact effects for all 10 spells. |
| Music + ambience | **Fantasy Music Bundle** (Asset Store, various ~$20) + freesound.org CC0 | ~$20 | Nice-to-have tier; ship silent-with-SFX if cut. |
| SFX | **Sonniss GDC Game Audio bundles** | Free | Royalty-free, huge coverage. |

**Total: ~$455 list, realistically ~$200–250 on sale.** Minimum viable subset if budget
is tight: Fantasy Kingdom + Modular Heroes + Mixamo + GUI PRO ≈ $255 list.

Gray-box phases (2–3e) use Unity primitives + ProBuilder (free) exclusively; packs are
bought at 3f so a sale window can be picked.

**License check:** all Asset Store/Synty licenses permit shipped commercial games
(no redistribution of source assets — we ship builds, fine). Mixamo license permits
game use. CC0 audio unrestricted.

---

## 4. Content production order

| Phase | Content built |
|---|---|
| 3a–3b | 1 monster (Marsh Skulker), 10 spells, weapon/armor basics — as JSON fixtures for tests |
| 3c | 4 classes × levels 1–5, 4 races (human/dwarf/elf/halfling, SRD), point-buy data |
| 3d | Havenrock hub (gray-box), Veresk dialogue, q_muster + q_clear_docks, Old Docks zone + 4 encounters |
| 3e | ~30 items (SRD weapons/armor/potions), 6 loot tables, vendor inventories, rest rules |
| 3f | Zones 2–3 built out with purchased packs, remaining monsters, side quests, boss fight |

Zones 2–3 intentionally land *after* the art pass begins: by then the pipeline
(JSON → zone scene → encounters → quest) is proven on Zone 1 and content production is
mechanical.
