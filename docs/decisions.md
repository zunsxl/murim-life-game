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

- **Art option selections (v0.1.0):** The owner chose 1A, 2B, 3A, 4A, 5B, 6A, 7A, 8A, 9A and 10C in chat. The initial selection was followed by the clarification `"8A, 10C."`. Reason: these selections establish the natural valley treatment, readable test feedback and restrained interface for v0.1.0. See [Art requirements](specs/v0.1.0.md#art-requirements) and [Art direction](art-direction.md#v010-visual-direction).
- **Art inventory approval and publication (v0.1.0):** The owner approved Art Draft 1, covering ART-001 through ART-021, the exact variant counts, defaults and version-scoped art direction, and authorized publication to the existing PR #6. Exact approval quote: `"Approved. Push it to the PR."` No additional art deferrals. Reason: complete the approved valley specification with the selected visual direction and a complete art inventory. See [Art requirements](specs/v0.1.0.md#art-requirements) and [Art direction](art-direction.md#v010-visual-direction).

- **Technical and balance decisions (v0.1.0, draft, pending owner approval of the technical spec):**
  - The valley layout is hand-authored constant data in `Murim.Core`, not a JSON file. Reason: it is fixed
    content rather than a tunable, so it is identical on every launch and needs no fallback copy. See
    [Valley layout data](specs/v0.1.0-technical.md#31-valley-layout-data-f-01-f-02-f-03-f-04).
  - Terrain is a heightfield mesh and every prop is a painted 2D sprite on an upright quad; no 3D model files.
    Reason: matches the approved painted direction and 2D art sourcing under a fixed 45° camera. See
    [Art delivery contracts](specs/v0.1.0-technical.md#4-art-delivery-contracts).
  - Four ground floors 3 m apart and three air layers at 5, 11 and 17 m. Reason: the fewest layers that clear
    each cliff tier and sit clearly above the 13 m ridge and 15 m bamboo. See
    [Core vertical model](specs/v0.1.0-technical.md#21-core-vertical-model).
  - Starting speeds: walk 5 m/s, fly 8 m/s, layer change 8 m/s; default view 40 m wide in a 160 m valley. Reason:
    unhurried exploration with a valley 4 default screens across. See
    [Tunables](specs/v0.1.0-technical.md#39-tunables).
  - `R`/`F` cancel the current destination, and opening the menu or losing focus completes any layer change in
    progress. Reason: nothing is left pending, so nothing resumes without fresh input. See
    [Altitude layers and airborne movement](specs/v0.1.0-technical.md#33-altitude-layers-and-airborne-movement-f-03-f-06-ce-9-ce-10-ec-6-ec-7-ec-8-ec-14).
  - Lighting follows neutral daylight (art option 2B), which supersedes the "golden-hour" wording in `F-08` and
    release check 10. See [World rendering and atmosphere](specs/v0.1.0-technical.md#37-world-rendering-and-atmosphere-f-02-f-08).
