# Hosting guide — Radiant Pool

## Hosting a campaign (the easy path)
1. Launch `RadiantPool.exe`, enter your display name, build your character, click
   **Host a campaign**.
2. Your invite code appears at the top ("Hosting — invite code: XXXXX-XXXXX"). Click
   **Copy code** and send it to your friends (2–4 players total).
3. Friends launch the game, build a character, paste the code, click **Join**.

The host's machine runs the authoritative game. **If the host quits, the session ends**
(everyone else is disconnected) — progress is safe: the campaign autosaves on the host at
every cleared encounter and quest turn-in, and F5 saves on demand. Host again later with
the same display names and everyone gets their characters back.

## Same house / same LAN
Codes work out of the box — they encode your LAN address.

## Playing over the internet (v1 reality)
The v1 invite code encodes your **local** IP, so internet friends need one of:
- **Easiest — a free mesh VPN** (Tailscale/ZeroTier): everyone installs it, host reads the
  code from the game while on the VPN, done. No router changes.
- **Port forwarding**: forward **UDP 7770** on the host's router to the host PC, then give
  friends a code built from your public IP (shown at whatismyip.com) — the in-game code
  currently shows the LAN address only, so VPN is the smoother option.

Unity Relay (true one-click internet joins with no VPN or router steps) is wired in the
architecture but requires linking the project to Unity Gaming Services — planned alongside
the Steam transport; see ARCHITECTURE.md §1.

## Save files
`%USERPROFILE%\Saved Games\RadiantPool\campaign.json` on the host. Back it up or delete it
to start a fresh campaign.

## Firewall
First launch, Windows asks to allow the game through the firewall — allow on
private networks (host only needs it).
