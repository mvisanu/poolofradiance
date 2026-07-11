# Phase 2 — two-machine join test script

Goal: prove invite-code join + movement sync between two real clients.

## Build
1. Open `game/` in Unity 6000.0.79f1 (or run the CI build):
   `Unity.exe -batchmode -quit -projectPath game -executeMethod RadiantPool.EditorTools.HeadlessBuild.Win64 -logFile build.log`
2. Output lands in `game/Builds/Win64/RadiantPool.exe`. Copy the whole folder to machine B (or run two instances on one machine for a smoke test).

## Test (two machines, same LAN)
| # | Machine A (host) | Machine B (joiner) | Expect |
|---|---|---|---|
| 1 | Launch exe, type name "Anna", click **Host a campaign** | — | A sees "Hosting — invite code: XXXXX-XXXXX" and spawns as a blue capsule in the gray-box dockyard |
| 2 | Read code aloud | Launch exe, type name "Ben", enter code, click **Join** | B spawns near A within ~2 s; each sees the other's capsule and floating name |
| 3 | Run around (WASD), rotate camera (hold RMB), jump (Space) | Watch A | B sees A's capsule move/rotate/jump smoothly (interpolated, no teleporting) |
| 4 | Watch B | Run behind a warehouse, then return | A sees B disappear behind geometry and come back — positions stay consistent |
| 5 | — | Enter a wrong code (e.g. AAAAA-AAAAA) then Join | On-screen error "Could not reach host…" — no freeze, no silent failure |
| 6 | Quit the host | — | B shows "Disconnected from host" within ~5 s |

Pass = all six rows. WAN note: v1 invite codes encode the host's LAN IP; over the
internet the host must port-forward UDP 7770 (or both players use a VPN like Tailscale) —
Unity Relay integration replaces this when a UGS project is linked, see ARCHITECTURE.md §1.
