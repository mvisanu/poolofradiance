# IP-CHECKLIST.md — Radiant Pool

Legal position: we use **SRD 5.1 only**, released by Wizards of the Coast under
**Creative Commons Attribution 4.0 (CC-BY-4.0)**. That grant is irrevocable and covers
the rules text and the specific creature/spell/item names *printed in the SRD* — and
nothing else. Everything WotC calls "Product Identity" stays out.

## Required attribution (ship this verbatim in credits + README)

> This work includes material taken from the System Reference Document 5.1 ("SRD 5.1")
> by Wizards of the Coast LLC and available at
> https://dnd.wizards.com/resources/systems-reference-document. The SRD 5.1 is licensed
> under the Creative Commons Attribution 4.0 International License available at
> https://creativecommons.org/licenses/by/4.0/legalcode.

## Hard bans (never appears in code, content, marketing, or repo docs outside this file)

| Banned | Why | Our replacement |
|---|---|---|
| "Dungeons & Dragons", "D&D", "DND" | Trademark | "SRD 5.1-based rules" in all user-facing text |
| "Pool of Radiance" as product name | Trademark (game + novel) | Working title **Radiant Pool**; see borderline item #1 |
| Forgotten Realms, Faerûn, Moonsea | Product Identity (PI) setting | **The Sundered Coast** (original) |
| Phlan, New Phlan, Sokal Keep, Valjevo Castle | PI locations | **Aldenmere / Havenrock / the Glasslit Temple** |
| Tyranthraxus, "the Flamed One" | PI villain | **The Hollow Flame** (original possessing entity) |
| Beholder, mind flayer/illithid, yuan-ti, githyanki, displacer beast, carrion crawler, umber hulk, slaad, kuo-toa | PI monsters (not in SRD) | SRD or original monsters only (see CONTENT-PLAN enemy list) |
| Named characters/deities of the Realms (Bane, Tyr as *Realms* depictions, etc.) | PI | Original **Dawnmother** faith |
| WotC trade dress: ampersand logo, red-box styling, official fonts/layouts | Trademark/trade dress | Original UI theme (GUI PRO kit) |

## Allowed and used (SRD 5.1, CC-BY)

- The 6 ability scores, proficiency bonus, AC/HP, advantage/disadvantage, initiative,
  action/bonus-action/reaction economy, saving throws, death saves, conditions, rest rules.
- Classes: Fighter, Wizard, Cleric, Rogue (SRD subclass each: Champion, Evoker, Life,
  Thief). Races: Human, Dwarf (Hill), Elf (High), Halfling (Lightfoot).
- Spells by SRD name: Fire Bolt, Sacred Flame, Magic Missile, Burning Hands, Cure Wounds,
  Healing Word, Bless, Shield, Sleep, Guiding Bolt.
- Monster *stat blocks*: bandit, bandit captain, giant rat, skeleton, zombie, cultist,
  cult fanatic, magma mephit, knight — **reskinned with original names** in-game
  (Marsh Skulker, Risen Drowned, Kindled Zealot, …). Using SRD names would also be
  legal; we reskin for flavor, not necessity.

## Borderline items — flagged for explicit decision (per prompt requirement)

1. **Working title "Radiant Pool."** Legally distinct from the "Pool of Radiance"
   trademark, but it invites the comparison and a trademark examiner could see confusing
   similarity in the same goods class (video games). **Recommendation: treat as internal
   codename; rename before any public/Steam listing** (candidates: *Lightwell*,
   *The Sundered Coast*, *Havenrock*). Decision owner: you, before Phase 4.
2. **Marketing the inspiration.** Saying "inspired by classic Gold Box CRPGs" is fine;
   "a Pool of Radiance remake" in store copy is not (nominative use gets murky in
   store SEO). Rule: name the *genre*, never the WotC product, in anything public.
3. **"Gold Box" itself** is also a WotC-associated mark for these games — don't use it
   in store copy either; "classic late-80s party CRPGs" is safe.
4. **SRD monster names in save files/JSON ids** (e.g. `zombie`): legal under CC-BY since
   they're SRD text, but we already attribute; no action needed. Kept flagged so nobody
   "fixes" attribution away.
5. **Asset packs**: Synty/Asset Store licenses are commercial-use; confirm at purchase
   that no pack embeds third-party trademarks (some packs include recognizable homages).
   Audit at 3f.

## Process guardrails

- CI grep (Phase 4): banned-term list above run against `/content`, `/game/Assets`,
  UI strings, and README on every build; build fails on hit (this file is exempt).
- Any new monster/spell/item must cite its SRD source page in a `"srdRef"` field or be
  marked `"original": true` in its JSON.
- This checklist is re-audited at every phase gate.
