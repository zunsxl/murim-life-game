# Art Direction

Authoritative visual direction and art rules for the project. When this document and a newer owner decision in
[docs/decisions.md](decisions.md) disagree, `docs/decisions.md` wins.

## Visual Principles

- **Readability at camera distance:** Terrain, structures, characters, UI, and effects must read cleanly at the
  game's working camera angle and zoom range.
- **Grounded Murim / Wuxia aesthetic:** Prefer cohesive, natural materials, clear silhouettes, and restrained
  visual accents over noisy generic fantasy tropes.
- **State legibility:** Interactive states (hover, preview, selected, disabled, active, damaged) must be
  distinguishable at a glance against all expected backgrounds.

## v0.1.0 visual direction

- Use detailed painted naturalism with readable large forms and restrained surface detail (1A).
- Use stable neutral daylight, fresh natural greens, cool gray granite and clear green water (2B); individual warm comparison panels do not override this palette.
- Use irregular broken granite cliffs and boulders, broad natural slopes, mountain pines, dense ridge bamboo, low shrubs, grass and restrained white/yellow flowers (3A, 4A).
- Water uses clear green rocky flow, submerged stones and narrow cascades (5B).
- The test subject is a matte ivory capsule with a dark base (6A). Its airborne projection uses a thin vertical guide and surface ring (7A), distinct from a cast shadow or destination marker.
- Upper obstructions briefly fade to complete removal (8A). Cut surfaces remain visually consistent with natural terrain.
- Reachable destinations use a pale ring; unreachable destinations use an amber broken ring with an X (9A). Shape, not color alone, conveys the difference.
- The menu and brief floor label use restrained slate-and-bronze framing with legible light text (10C).
- Preserve a purely natural, unbuilt Murim/Wuxia valley. No sect insignia, invented lore, supernatural scenery or man-made landscape elements are introduced.
- Exact families, counts, states and acceptance criteria belong in the version's Art requirements inventory. The choices above do not establish technical delivery contracts or implementation architecture.

## Art Inventory & Delivery Conventions

- Every in-scope art asset or asset family is assigned a stable `ART-NNN` identifier in the version's product spec
  (`docs/specs/vX.Y.Z.md`).
- Exact counts, dispositions (`New`, `Reuse`, `Modify`), visual requirements, chosen direction options, and
  per-item acceptance criteria live in the product spec.
- File dimensions, formats, naming, destination paths, and Unity import settings live in the technical spec's
  **Art delivery contracts** (`docs/specs/vX.Y.Z-technical.md`).

## References, Mockups, and Sourcing

- Reference images live in `docs/references/` and are **never** shipped as runtime assets.
- Sourcing and mockup permissions are governed by [docs/decisions.md](decisions.md#art-sourcing-rules).
- Committed production art assets and credits are tracked in [Assets/Art/ART_README.md](../Assets/Art/ART_README.md).
