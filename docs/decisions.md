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
- **Deterministic simulation:** World areas are fixed and hand-built (retiring seed-based map layout generation),
  while core simulation rules and state in `Assets/Scripts/Core/` remain pure C# and deterministic. *(Amended
  2026-11-28 in `v0.1.0` per owner decision `2A`; replaced the earlier standing rule requiring seed-deterministic
  map generation.)*
- **Branch naming:** Use `vX.Y.Z-spec`, `vX.Y.Z-impl`, `vX.Y.Z-impl-ARCH-NNN`, and `vX.Y.Z-docs`. Never name a
  branch `vX.Y.Z` (reserved for release tags).

## Art sourcing rules

| Rule | Policy |
|---|---|
| Production art sourcing | Owner obtains and commits production art; sourcing and licensing are the owner's decision. |
| Illustrative mockups | Allowed before approval using any method, including AI image generation, existing project art, composites, or manual edits. Mockups are illustrative only and are not committed as runtime assets. |
| Shipping exclusions | Never ship watermarked previews, unverified third-party extracts, or reference images from `docs/references/`. |

## Quality-gate waivers

To waive a specific blocking deterministic gate finding, record an entry here containing `WAIVED: <FINDING-ID>`
(for example, `WAIVED: CQ-F-001`), the owner's reason, and the version.

| Finding ID | Status | Version | Reason |
|---|---|---|---|
| — | *(No active waivers)* | — | — |

## Version decisions

### v0.1.0 *(Draft 2 — product spec approval pending)*

- **Fixed hand-built areas, deterministic core simulation (`2A`, `3A`, `5B`):** World areas are fixed and hand-built
  one area at a time rather than procedurally generated from a seed; `v0.1.0` delivers the first self-contained
  spacious mountain valley (roughly 3–5× default screen width) enclosed by natural perimeter features, while core
  world and movement rules remain deterministic. Replaces seed-based map generation in **Standing rules**
  (2026-11-28). See [v0.1.0 Features](specs/v0.1.0.md#features).
- **Pure natural environment (`4A`):** `v0.1.0` focuses purely on natural landscape features (tiered grassy
  clearings, rocky cliffs, gentle natural slopes, upper bamboo grove, scattered mountain trees, boulders, shrubs,
  wildflowers, cascading waterfall, and flowing rocky river) with zero man-made structures or items. See
  [v0.1.0 Features](specs/v0.1.0.md#features) and [Out of scope](specs/v0.1.0.md#out-of-scope).
- **Multi-level verticality for future character flight (`6A`, `8A`, `13A`, `17A`):** Approved project direction:
  the world uses stacked multi-level ground tiers plus open-air altitude layers to support future character flight.
  In `v0.1.0`, ground tiers connect via a mix of gentle walkable slopes and sheer cliffs, viewed from a fixed
  angled overhead camera (`10A`) with manual floor-level stepping/slicing; full flight gameplay mechanics are
  deferred to future versions. See [v0.1.0 Features](specs/v0.1.0.md#features) and
  [Out of scope](specs/v0.1.0.md#out-of-scope).
- **Exploration, placeholder, presentation, and atmosphere (`9A + 9C`, `11C`, `12A`, `14A`, `15A`, `16A`, `D1–D4`):**
  Direct launch into the valley with zero persistent HUD, a brief auto-fading floor label on floor changes, an
  `Esc` system menu (`Resume`, `Reset View`, `Quit`), steady daytime / golden-hour lighting with subtle ambient
  water/foliage motion (no audio), free camera controls anytime, and a controllable test placeholder supporting
  ground walking on slopes plus vertical open-air ascent/descent. See [v0.1.0 Controls](specs/v0.1.0.md#controls)
  and [Edge cases](specs/v0.1.0.md#edge-cases).
- **Approved deferrals:** Man-made structures/items, adjacent world areas, NPCs/disciples/AI, full flight gameplay
  mechanics, cultivation/sect/combat/economy systems, persistent HUD/minimap, audio, day/night/weather cycles,
  title screen, and saving/loading. See [v0.1.0 Out of scope](specs/v0.1.0.md#out-of-scope).
- **Product spec approval:** *Pending explicit owner approval of Draft 2 (and resolution of Open questions 1–6).*
