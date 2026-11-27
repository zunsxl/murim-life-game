# Decisions Log

Authoritative record of owner decisions, spec approvals, art direction choices, architectural/balance decisions,
and explicit quality-gate waivers. Update an existing entry rather than recording the same decision twice.

## Standing rules

- **Owner authority:** Only the owner approves product specs, approves technical specs, chooses art directions,
  authorizes merges to `main`, and grants quality-gate waivers.
- **Art changes shown before build:** Visual direction changes and new art items are reviewed via text options and
  illustrative comparison mockups before spec approval.
- **Reference images never ship:** Files in `docs/references/` are inspiration/reference only and are never
  committed as runtime assets.
- **Cultural setting (Chinese wuxia / Jianghu only):** The game exclusively follows classical Chinese wuxia /
  Jianghu (and Chinese cultivation where later approved) aesthetics and never uses Korean Murim influences.
  *(Supersedes Korean Murim references in `docs/vision.md`; `docs/vision.md` flagged for doc update.)*
- **Deterministic simulation:** Core simulation and map generation in `Assets/Scripts/Core/` remain pure C# and
  seed-deterministic.
- **Branch naming:** Use `vX.Y.Z-spec`, `vX.Y.Z-impl`, `vX.Y.Z-impl-ARCH-NNN`, and `vX.Y.Z-docs`. Never name a
  branch `vX.Y.Z` (reserved for release tags).

## Art sourcing rules

| Rule | Policy |
|---|---|
| Production art sourcing | Owner obtains, approves, and commits production art; AI image generation is permitted for production art alongside commissioned/licensed sources (replaced prior disallowance in `v0.1.0`). |
| Illustrative mockups | Allowed before spec approval using existing project art, composites, manual edits, or AI image generation (replaced prior disallowance in `v0.1.0`). Mockups are illustrative only and never ship as runtime assets. |
| Shipping exclusions | Never ship watermarked previews, unverified third-party extracts, illustrative mockups, or reference images from `docs/references/`. |

## Quality-gate waivers

To waive a specific blocking deterministic gate finding, record an entry here containing `WAIVED: <FINDING-ID>`
(for example, `WAIVED: CQ-F-001`), the owner's reason, and the version.

| Finding ID | Status | Version | Reason |
|---|---|---|---|
| — | *(No active waivers)* | — | — |

## Version decisions

### v0.1.0

- **Product spec approval:** Approved by owner on 2026-11-27 (Draft 5) — exact quote: `"1. Approved. 2. Open the PR spec."` → [docs/specs/v0.1.0.md](specs/v0.1.0.md)
- **Scope & deferrals:** Focus strictly on procedurally generated multi-level valley world, fixed 3/4 camera movement, and minimal `Esc` pause/quit overlay. Playable/movable units, NPCs, buildings, bridges, roads/stairs, natural footpaths, gameplay HUD/minimap, time/cultivation mechanics, environmental animations, camera rotation, screen-edge scrolling, middle-mouse drag, custom cursor, and audio are deferred → [Out of scope](specs/v0.1.0.md#out-of-scope).
- **Visual medium & verticality:** Stylized 3D painterly presentation at a fixed elevated 3/4 angle to support true 3D multi-level verticality (three elevation tiers, one primary waterfall, gentle continuous slopes linking tiers) for future player-built structures, bridges, and XYZ flying/selection → [F-01, F-03](specs/v0.1.0.md#features).
- **Seed & determinism:** Normal launches use a fresh random seed; external seed override enables deterministic reproduction across launches → [F-01](specs/v0.1.0.md#features), [EC-10](specs/v0.1.0.md#edge-cases).
- **Camera & window defaults:** `WASD`/Arrow pan + right-mouse-drag only (zoom-scaled pan travel), cursor-anchored wheel zoom, `Home`/`R` reset to central valley overview (interrupted by new input), terrain-following overhead height clearance (`EC-5`), resizable windowed launch with clean `X`/`Alt+F4` close → [Controls](specs/v0.1.0.md#controls), [Edge cases](specs/v0.1.0.md#edge-cases).
- **Cultural setting:** Classical Chinese wuxia / Jianghu natural landscape exclusively; Korean Murim permanently excluded → [Design principles](specs/v0.1.0.md#design-principles).
- **AI image generation policy update:** Owner removed the standing ban on AI image generation for both pre-approval illustrative mockups and production art (see *Art sourcing rules* above). `v0.1.0` illustrative comparison mockups used AI image generation (Plates 1–2) and UI/vector composites (Plates 3–4); none are shipped as runtime assets.
- **Art direction choices (`ART-001`–`ART-011`):** Reviewed via text options and illustrative comparison plates → [Art requirements](specs/v0.1.0.md#art-requirements):
  - `ART-001` (Valley Ground Terrain Surfaces): **Option C** — Rich Verdant Moss & Wet Slate Earth
  - `ART-002` (Multi-Level Embankments & Natural Slopes): **Option C** — Rounded Terraced Earth-and-Stone Banks
  - `ART-003` (Static Mountain Stream & Waterfall Surfaces): **Option C** — Dark Mirror Pool & Crisp White Waterfall
  - `ART-004` (Mountain Pine Trees): **Option A** — Layered Painterly Cloud-Canopy Pines
  - `ART-005` (Bamboo Grove Clusters): **Option A** — Sunlit Olive-Jade Vertical Groves
  - `ART-006` (Mossy Boulders & Rock Outcrops): **Option A** — Moss-Capped Warm Slate Boulders
  - `ART-007` (Wildflowers, Shrubs & Ground Foliage): **Option A** — Delicate White Wildflowers & Muted Mountain Brush
  - `ART-008` (Valley Border Framing & Atmospheric Lighting Mood): **Option C** — Golden Late-Afternoon Valley Glow & Close Ridge Framing
  - `ART-009` (Minimal System Overlay Frame & Buttons): **Option A** — Dark Slate-Jade Frame with Muted Brass Trim
  - `ART-010` (Valley Sky & Distant Mountain Horizon): **Option A** — Golden-Amber Haze & Layered Misty Peaks
  - `ART-011` (System Overlay Typography & Paused Title): **Option A** — Clean Classical Serif Title & Buttons

- **Technical decisions (`v0.1.0`):** recorded with the technical spec → [docs/specs/v0.1.0-technical.md](specs/v0.1.0-technical.md):
  - **Engine and toolchain:** Unity 6.3 LTS (`6000.3` stream, exact patch frozen in `ProjectSettings/ProjectVersion.txt` by `ARCH-001`), URP Forward, Input System, Mono x64; standalone Core tests on the .NET 10 SDK. Reason: current LTS lines; Mono avoids an IL2CPP toolchain for contributors → [§3.6](specs/v0.1.0-technical.md#36-project-setup-build-and-verification-tooling-supports-f-01f-05).
  - **Integer-only, constructive generation:** the valley is generated with integer arithmetic only on a 512 m × 512 m, 1 m heightfield, and every invariant (three tiers, one waterfall and stream, one gentle ramp per tier pair, camera-facing embankments) holds by construction. Reason: identical results in the test host, Editor, and player with one golden hash; no retry loops → [§3.1](specs/v0.1.0-technical.md#31-valley-generation-f-01).
  - **World scale and tier heights (balance):** valley floor 368 m wide (2.4 screens at widest zoom); mid tier 12 m and upper tier 26 m above the basin; gentle ramps 5× their rise (average gradient 0.20); one waterfall of about 11 m from the mid tier into the basin head. Reason: tiers read as real height from the fixed camera while ramps stay gentle enough for future movement → [§3.1.9](specs/v0.1.0-technical.md#319-worldjson).
  - **Prop density (balance):** about 7,300–8,400 instances per valley at defaults (pines 400–530, bamboo 290–530, rocks 770–900, flora 5,500–6,500) with open plateaus kept below 12 % canopy cover and hard caps per family. Reason: dense ridges and banks, calm buildable meadows → [§3.2.4](specs/v0.1.0-technical.md#324-density-rationale-and-measured-ranges).
  - **Camera model and feel (balance):** fixed pitch 40° and vertical FOV 40°; zoom distance 12–120 m (default 60 m); key pan `0.65 × distance` m/s (one screen width in 2.0 s at any zoom); damping times 0.10 s / 0.22 s (pan), 0.12 s (zoom), 0.35 s (reset); 3 m camera clearance. Motion model lives in `Murim.Core` for deterministic testing → [§3.4](specs/v0.1.0-technical.md#34-camera-exploration-f-04).
  - **Camera bounds:** the pivot is clamped from the view frustum's ground footprint so no ray can leave the terrain except over the north ridge onto the horizon backdrop; very wide windows limit zoom-out instead of exposing the sides → [§3.4.7](specs/v0.1.0-technical.md#347-bounds-and-the-no-void-rule-ec-3-ec-4-ec-8).
  - **Horizon as a screen-anchored backdrop:** `ART-010` is composed in screen space behind the terrain (the camera never rotates). Reason: no far geometry needed and no void possible → [§3.3.5](specs/v0.1.0-technical.md#335-horizon-backdrop).
  - **Seed override and inputs:** `-seed <0…4294967295>` on the command line; tunables in `world.json` and `camera.json` with whole-file fallback, 64 KiB and depth limits; no dev controls, telemetry, or crash upload in `v0.1.0` → [§2.5](specs/v0.1.0-technical.md#25-configuration-loading), [§3.1.1](specs/v0.1.0-technical.md#311-seed-ec-10).
  - **Input interpretation (EC-7):** the pointer leaving the window, focus loss, minimise, opening the overlay, and a reset all cancel held pan keys and drags until they are pressed again → [§3.4.3](specs/v0.1.0-technical.md#343-suppression-and-cancel).
  - **Art delivery:** props are 3D FBX models with one albedo each (17 models), terrain and water are seamless textures, the lighting mood (`ART-008`) and `Paused` title treatment (`ART-011`) are engine settings rather than image files → [§4](specs/v0.1.0-technical.md#4-art-delivery-contracts).
  - **Technical spec approval:** pending owner approval (draft committed locally on `v0.1.0-spec`).
