# Game Guide

This document describes the game **as currently implemented on `main`** (`v0.1.0`, commit `6a8a7ba672ad51a0635ba730b9363a0b4616955c`). Unimplemented spec requirements are not documented here.

## Current Status

**Version:** `v0.1.0` — *Mountain Valley World & Camera Exploration* (Windows x64, released 2026-11-27).

Launching `MurimLifeGame.exe` opens directly into a procedurally generated Chinese-wuxia / Jianghu mountain valley in resizable windowed mode (`1600 × 900` default). There is no title screen, gameplay HUD, playable unit, building, ambient animation, or audio in `v0.1.0`.

## Valley Generation & World Layout

Every launch generates an untouched `512 m × 512 m` valley (`513 × 513` vertex heightfield at `1 m` resolution) using pure integer-centimetre arithmetic (`Murim.Core.World.ValleyGenerator`):

- **Three elevation tiers:** A lower stream basin, mid-level meadows (default `12 m` above basin), and upper shelves (default `26 m` above basin), bordered by west/east ridges (default `30 m` rise) and a north ridge (default `26 m` rise). The valley floor spans `368 m` (`x = 72 m` to `x = 440 m`).
- **Embankments & slopes:** Front-facing rounded earth-and-stone embankments separate the tiers, linked by one gentle continuous ramp between basin and mid tier and one between mid and upper tier on opposite sides of the valley axis (average gradient `~0.20`).
- **Water features:** A mid-tier spring pool and brook feed a single vertical waterfall (`3 m` wide, `~11.3–11.5 m` drop at defaults) into a plunge pool at the basin head and a meandering stream exiting south.
- **Ground layers & props:** Eight blended terrain layers (moss turf, dark-slate soil, mossy stone, pebble bank, grassy embankment, earth-and-boulder bank, hillside slope, ridge forest floor — zero footpaths or paving) and 17 static prop variants across 5 families (`Pine`, `Bamboo`, `Rock`, `Lip`, `Flora`). Pines, bamboo, and rocks cast a reusable lower-right soft ground shadow (`shadow_soft`). Typical valleys contain ~7,300–8,400 prop instances (caps: Pine `1,200`, Bamboo `800`, Rock `1,600`, Lip `500`, Flora `10,000` with variant caps `flora_wildflower` `4,000`, `flora_shrub` `1,200`, `flora_fern` `800`, `flora_tussock` `5,000`).

### Seeds & Determinism

- **Normal launch:** Uses 4 cryptographic random bytes (`System.Security.Cryptography.RandomNumberGenerator`) as a 32-bit unsigned seed.
- **Seed override:** Pass `-seed <0..4294967295>` on the command line (for example, `MurimLifeGame.exe -seed 20261127`) to reproduce the exact same valley layout, water, and prop placement. Invalid `-seed` values log a warning and fall back to a random seed.
- **Verification log:** On boot, `GameBootstrap` writes `[Murim] World ready seed=<uint> source=<cli|random> hash=<16 hex> props=<int> ms=<int>` using a 64-bit FNV-1a `ValleyWorld.Hash`.

## Strictly 2D Presentation

- **Oblique 3-4-5 projection:** Logical world coordinates `(x, z, y)` project onto the 2D screen plane via `u = x`, `v = 0.6 * z + 0.8 * y` (a fixed `36.87°` view looking north). One orthographic camera renders with rotation locked to `(0, 0, 0)`.
- **Draw order (`Murim.View.DrawOrder`):** Fixed at launch with no depth buffer or 3D lights:
  1. `Backdrop` (`-32000`): screen-anchored golden-amber sky and two parallax misty mountain layers (`ART-010`)
  2. `Terrain` (`-31000` to `-31007`): 64 flat chunk meshes (`8 × 8` chunks of `64 × 64` cells), drawn north row first, sampling fine (`worldXY / 16`) and coarse (`worldXY / 73`) tile detail (coarse blend rises from `0` at `H <= 30 m` to `0.5` at `H >= 70 m`)
  3. `Water` (`-30000` stream/brook/pools, `-29999` waterfall, `-29998` plunge-pool foam collar)
  4. `Lips` (`-29000 + lipRank`): mossy overhang lip sprites sorted north-to-south
  5. `Shadows` (`-28000`): soft lower-right cast ground shadows
  6. `Props` (`-16000 + propRank`): remaining sprites sorted back-to-front (`ZCm` descending, then `XCm` ascending)
- **Baked lighting & atmosphere:** Upper-left sunlight is baked into all art; terrain slopes receive a static vertex relief shade (`Projection.ReliefQ`, `0.824–1.176`); world shaders apply a warm tint (`#FFF2D9`) and north-crest haze (`#EFD3A0`, max `0.45`) over the upper `110 m` below the crest.
- **Flora zoom fade:** Ground flora (`ART-007`) stays at full opacity below view height `50 m`, fades linearly between `50 m` and `64 m`, and deactivates at `>= 64 m` so overview zoom remains calm.

## Controls

| Context | Input | Behavior |
|---|---|---|
| Exploring | `W` / `A` / `S` / `D` or Arrow keys | Continuous damped pan along screen axes (zoom-scaled speed; diagonal speed normalized; opposing keys cancel) |
| Exploring | Right-mouse button hold + drag | Direct 1-to-1 camera drag across all zoom levels with damped release glide |
| Exploring | Mouse scroll wheel | Cursor-anchored continuous zoom toward/away from the point beneath the cursor |
| Exploring | `Home` or `R` | Smoothly eases camera back to the starting waterfall vista (`H = 44 m`); cancelled immediately by any new pan, drag, or scroll |
| Exploring | `Esc` | Opens the `Paused` overlay and blocks camera input |
| Overlay open | `Esc` or left-click `Resume` | Closes the overlay and restores camera control |
| Overlay open | Left-click `Quit` | Exits cleanly to desktop |
| Anytime | Window close (`X`) or `Alt+F4` | Exits cleanly to desktop |
| Anytime | Window resize, maximize, minimize | Resizes/maximizes/minimizes cleanly while keeping bounds intact |

- **Inactive inputs:** Left-clicking the world, middle-mouse dragging, and resting the cursor on window edges do nothing.
- **Input cancellation:** Opening the overlay, losing focus (`Alt-Tab`), minimizing, or moving the pointer outside the window during an active drag/pan immediately cancels active motion so no stuck input or drift occurs on return.

## Camera Behavior & Active Values

- **Starting vista:** Opens centered on the waterfall (`wfX`, `4.4 m` below the top of the fall) at default view height `44 m` (`78.2 m` wide at 16:9).
- **Zoom & crispness:** Default view height bounds are `12 m`–`88 m` (15 scroll detents at `15%` per detent). Closest zoom enforces `MaxPixelsPerMetre = 160` (`1.25×` the `128 px/m` authored sprite resolution), raising the effective minimum view height on windows taller than `1920 px`.
- **No-void clamping:** Camera bounds clamp horizontally within `[0, 512] m` and vertically between the south terrain edge (`PlaneBottomCm`) and `SkyFraction = 0.30` above the lowest north crest (`PlaneCrestCm`). Very wide windows cap zoom-out instead of exposing side void (aspects wider than `5.5:1` are unsupported).
- **Damped motion:** Key pan speed is `0.9 × viewHeight` per second (`~1.98 s` across a 16:9 screen at any zoom), with `0.10 s` pan acceleration, `0.22 s` pan stop damping, `0.12 s` zoom smoothing, and `0.35 s` reset smoothing.

## Configuration

Runtime tunables live in `Assets/StreamingAssets/Data/` (or `MurimLifeGame_Data/StreamingAssets/Data/` in the built player). Both files require `"schemaVersion": 1`, valid UTF-8, size `<= 65536` bytes (`64 KiB`), and JSON nesting depth `<= 4`. Any failure logs `[Murim] Config <fileName> ignored (<reason>); using defaults.` (`missing`, `unreadable`, `oversized`, `malformed`, `schema`, `range:<fieldName>`, or `invalid` for disallowed file names) and applies built-in defaults for the **entire file**.

### `world.json` (`WorldConfig`)

| Field | Type | Default | Valid range | Effect |
|---|---|---|---|---|
| `schemaVersion` | int | `1` | `1` | Config schema version |
| `midTierHeightM` | int | `12` | `8`–`16` | Mid tier height above basin floor (m) |
| `upperTierHeightM` | int | `26` | `midTierHeightM + 10`–`midTierHeightM + 18` | Upper tier height above basin floor (m) |
| `sideRidgeHeightM` | int | `30` | `20`–`40` | West/east ridge rise at terrain edge (m) |
| `northRidgeHeightM` | int | `26` | `22`–`32` | North ridge rise at terrain edge (m) |
| `treeDensityPct` | int | `100` | `25`–`150` | Scales pine and bamboo placement thresholds (%) |
| `rockDensityPct` | int | `100` | `25`–`150` | Scales rock and bank-lip placement thresholds (%) |
| `floraDensityPct` | int | `100` | `25`–`150` | Scales wildflower, shrub, fern, and tussock thresholds (%) |

### `camera.json` (`CameraConfig`)

| Field | Type | Default | Valid range | Effect |
|---|---|---|---|---|
| `schemaVersion` | int | `1` | `1` | Config schema version |
| `minViewHeightM` | float | `12` | `8`–`24` | Closest view height (m) |
| `maxViewHeightM` | float | `88` | `70`–`100` | Widest view height (m) |
| `defaultViewHeightM` | float | `44` | `minViewHeightM`–`maxViewHeightM` | Initial and reset view height (m) |
| `panSpeedPerViewHeight` | float | `0.9` | `0.3`–`3.0` | Key pan speed per metre of view height (m/s per m) |
| `panAccelTimeS` | float | `0.10` | `0.01`–`1.0` | Pan acceleration damping time (s) |
| `panStopTimeS` | float | `0.22` | `0.01`–`1.0` | Pan release damping time (s) |
| `zoomStepPct` | float | `15` | `5`–`50` | Zoom step per scroll wheel detent (%) |
| `zoomSmoothTimeS` | float | `0.12` | `0.01`–`1.0` | Zoom smoothing time (s) |
| `resetSmoothTimeS` | float | `0.35` | `0.05`–`2.0` | `Home`/`R` vista reset smoothing time (s) |

## System Overlay & Exit

Pressing `Esc` toggles `Assets/Resources/Prefabs/SystemOverlay.prefab` (Screen Space Overlay canvas, `1920 × 1080` *Shrink* scaler, `Paused` title in `OverlaySerif SDF`, `Resume`, and `Quit` buttons using `Navigation.None`). `Time.timeScale` remains `1`.

## Developer Controls

None exist in `v0.1.0`.
