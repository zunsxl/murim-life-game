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
