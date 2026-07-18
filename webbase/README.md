# Radiant Pool — web version (`webbase/`)

The browser edition of Radiant Pool: the full campaign, playable solo in Chrome,
Edge, Firefox, or Safari, wrapped in a parchment-and-brass web shell that matches
the in-game Academia design system. The Unity WebGL player lands in `webbase/game/`
(gitignored build artifact); this folder holds everything needed to build, test,
and ship it.

## Build

```powershell
scripts/build-web.ps1        # from the repo root: bootstrap → WebGL build → webbase/game
```

Or by hand (target switch must happen via `-buildTarget`, before the method runs):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Unity.exe" -batchmode -quit `
  -projectPath <repo>\game -buildTarget WebGL `
  -executeMethod RadiantPool.EditorTools.HeadlessBuild.WebGL -logFile webgl.log
```

The build uses Brotli compression **with decompression fallback**, so it loads from
any static host with zero header configuration, and the custom `RadiantPool`
WebGL template (`game/Assets/WebGLTemplates/RadiantPool/`) — the charter screen,
pool loader, and field notes.

## Test locally

```powershell
cd webbase
.\serve.ps1            # http://localhost:8080/game/ opens in the browser
```

`serve.py` also sends proper `Content-Encoding: br` headers so the browser skips
the JS decompression fallback (faster first load).

## What differs from the desktop build (by design, not omission)

| Area | Desktop | Web |
|------|---------|-----|
| Multiplayer | Host + invite codes (Tugboat/UDP) | **Solo campaign only.** Browsers cannot open or accept UDP/TCP sockets; the build swaps in an in-process loopback transport (`LoopbackTransport`). The title screen says so. True web multiplayer would need a WebSocket relay server (Bayou + hosted WSS) — flagged as future work. |
| Saves | `%USERPROFILE%\Saved Games\RadiantPool\campaign.json` | Browser storage (IndexedDB via PlayerPrefs), same JSON, per-browser/per-site. Clearing site data deletes the campaign. |
| Self-test flags (`-attacktest`, …) | Command line | Not available (browsers pass no args). QA rides the desktop build. |
| Graphics tier | Full: MSAA 4x, HDR, SSAO, 4 shadow cascades | Web profile **baked in at build time** (never a runtime pipeline swap — see CLAUDE.md gotcha): MSAA 2x, HDR on, full render scale, 1024 shadowmap / 2 cascades, FXAA, no film grain, no SSAO, URP compatibility mode (Render Graph off). |

Everything else — rules, campaign, content, UI, saves format — is the same code
and the same assets.

## In-browser verification checklist

1. Title screen renders with the RPG & MMO UI 7 skin and all three fonts.
2. Create a character → **BEGIN A SOLO CAMPAIGN** → world loads, WASD pans.
3. Fight one encounter: click-to-attack walks and strikes; turn hands off after impact.
4. Open I / L / M panels; sell an item at a trader; gold total moves on the hotbar.
5. Reload the tab → **campaign resumes** (browser-storage save round-trip).
6. Audio plays after the first click (browsers gate sound behind a user gesture —
   the Enter-the-Pool button is that gesture).
7. Fullscreen button (bottom-right) works; Esc leaves fullscreen without wedging input.

## Deploy

Any static host works — no headers, no server code:

- **itch.io**: zip the *contents* of `webbase/game/` (index.html at the zip root),
  upload as HTML project, set viewport to 1280×720 + "Mobile friendly" off.
- **Netlify / GitHub Pages / Cloudflare Pages**: publish the `webbase/game/` folder
  as-is. With decompression fallback enabled nothing else is required; optionally
  add `Content-Encoding: br` headers for `*.br` files to skip the JS fallback.
- **Self-hosted**: any web server; `serve.py` shows the ideal header set.
