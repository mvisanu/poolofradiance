# Playtest checklist — full first-region campaign (2 players)

1. Host + join (docs/playtest-phase2.md rows 1–2). Accept the muster quest at Veresk →
   "Retake the Old Docks" activates; the Market gate (north) and Temple gate (east) are
   solid walls — you cannot enter either zone.
2. Clear the docks' 3 required encounters (west side) → turn in at Veresk → +300 XP each
   (level 2), +100 gold, **the Market gate sinks open on both clients**. Repeat for the
   Market (4 encounters, undead) → Temple gate opens; party should reach ~level 3–4.
3. Temple (5 encounters vs the Kindled): the finale at the Lightwell pits the party
   against **Warden Sorrel, Hollow-Flame Host** (AC 18, ~39 HP) plus a zealot.
   Victory → turn in → +3400 XP each (level 5), campaign-complete banner, journal shows
   "★ Aldenmere stands free." Save persists all of it — quit, re-host, verify the world
   comes back complete.

XP tuning note: encounter XP is awarded in full to every party member; the curve is
machine-verified by `CampaignSimulationTests` (five seeded 2-player runs of all 12
required encounters must finish at level 5 without excessive party wipes).
