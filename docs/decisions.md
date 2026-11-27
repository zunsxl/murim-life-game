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
