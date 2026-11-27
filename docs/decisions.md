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
  2026-11-28 in `v0.1.0`; replaced the earlier standing rule requiring seed-deterministic map generation.)*
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

### v0.1.0

- **Fixed hand-built areas:** World areas are fixed and hand-built one at a time instead of generated from a seed;
  `v0.1.0` delivers one self-contained valley (roughly 3–5× default screen width) enclosed by natural perimeter
  features. Reason: places should be intentionally composed rather than procedurally randomized, while behavior
  stays repeatable. Replaces seed-based map generation in **Standing rules** (2026-11-28). See
  [Features](specs/v0.1.0.md#features).
- **Pure natural environment:** No man-made structures or items; the river channel and upper bamboo grove are
  impassable on foot. Reason: the valley is an untamed corner of the world before any sect builds on it. See
  [Design principles](specs/v0.1.0.md#design-principles).
- **Multi-level verticality:** The world uses stacked ground floors plus open-air altitude layers, joined by gentle
  slopes and sheer cliffs, under a fixed-angle overhead camera. Reason: to support future character flight. See
  [Features](specs/v0.1.0.md#features).
- **Exploration controls and presentation:** Free camera with floor stepping, a click-to-move test placeholder with
  `R`/`F` ascend/descend, no persistent HUD, and an `Esc` menu. Reason: the player can inspect and verify the whole
  valley without switching modes. See [Controls](specs/v0.1.0.md#controls).
- **Draft 4 amendments (2026-11-28, owner answers to the spec review):**
  - Left-click targets the ground under the cursor when it is reachable on foot, otherwise the placeholder's own
    floor. Reason: clicking a terrace must send the placeholder to that terrace. See
    [CE-8](specs/v0.1.0.md#controls).
  - Camera floor stepping covers only ground floors and Full View, the same both ways; open-air layers are
    placeholder altitudes only. Reason: an air-layer view looked identical to Full View. See
    [CE-4/CE-5](specs/v0.1.0.md#controls).
  - Each left-click shows a brief auto-fading destination marker that reads differently when unreachable. Reason:
    vision pillar 3 requires clear feedback for every action. See [F-07](specs/v0.1.0.md#features).
  - Accepted defaults: the view follows a slope walk only when the placeholder would otherwise be hidden (`EC-13`);
    `F` over a slope lands on the slope (`EC-14`); a blocked walk stops at the reachable ground closest to the
    clicked spot (`EC-5`); a few open-air layers, the highest clearly above the ridge (`F-03`); ambient water and
    foliage motion keeps playing while the window is unfocused (`EC-4`). See
    [Edge cases](specs/v0.1.0.md#edge-cases).
- **Approved deferrals:** Man-made structures/items, adjacent world areas, NPCs/disciples/AI, full flight gameplay
  mechanics, cultivation/sect/combat/economy systems, persistent HUD/minimap, camera floor stepping into open-air
  layers, audio, day/night/weather cycles, title screen, settings screen, and saving/loading. See
  [Out of scope](specs/v0.1.0.md#out-of-scope).
- **Product spec approval (`v0.1.0`):** Approved by the owner on 2026-11-28 covering `Draft 4` of
  [docs/specs/v0.1.0.md](specs/v0.1.0.md) (`Status: Approved`), with the proposed default for the unfocused
  ambient-motion question accepted. Exact owner approval quote: `"Approved, default OK"`. Supersedes the earlier
  approval of `Draft 3` (quote: `"Approved."`).
