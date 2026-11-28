# Decisions Log

Authoritative record of owner decisions, spec approvals, art direction choices, architectural/balance decisions,
and explicit quality-gate waivers. Update an existing entry rather than recording the same decision twice.

## Standing rules

- **Owner authority:** Only the owner approves product specs, approves technical specs, chooses art directions,
  authorizes merges to `main`, and grants quality-gate waivers.
- **Art changes shown before build:** Visual direction changes and new art items are reviewed via text options and
  illustrative comparison mockups before spec approval.
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

### v0.1.0 *(Spec Draft 4 — awaiting owner approval)*

| Topic | Decision | Reason | Link |
|---|---|---|---|
| Fresh-start baseline & version | `v0.1.0` is the fresh-start baseline with a single fixed, authored 1:1 map (no seeded/procedural variation), superseding earlier seeded-world and `v0.1.0` roadmap wording in `docs/vision.md` and `docs/project-profile.md`. | Authoritative reference recreation requires exact fixed layout; owner instructed `1A` and `Q3B` ("v0.1.0 will be the fresh start and the new baseline"). | [v0.1.0.md · Design principles](specs/v0.1.0.md#design-principles) |
| Camera & exploration bounds | Launch at exact reference framing; allow clamped pan (`WASD`/Arrows, right/middle-mouse drag, no edge pan), zoom (scroll wheel toward cursor; `=`, `+`, Numpad `+`, `-`, Numpad `-` toward viewport center), and reset (`R`/`Home`), with camera angle locked and exploration clamped strictly inside reference borders (no off-frame terrain). Opposing pan keys cancel; mouse drag overrides keyboard pan while held. | Owner selected `2C`, `Q1B`, `Q2A`, and accepted `D5` plus proposed defaults 1, 3, and 4. | [v0.1.0.md · Controls](specs/v0.1.0.md#controls) |
| Central water pool | Preserve the central water pool as a natural spring/rock pool around the central tree; remove only the built stone enclosure and replace its rim with natural rocks and mossy bank. | Owner selected `3B` to preserve natural water feature while removing masonry. | [v0.1.0.md · Design principles](specs/v0.1.0.md#design-principles) |
| Retaining wall reconstruction | Reconstruct removed stone retaining walls (upper terrace and near riverbank) as natural earthen/grassy slopes with scattered boulders and vegetation, preserving exact elevation steps. | Owner selected `4A` to match surrounding valley hillside naturally. | [v0.1.0.md · Design principles](specs/v0.1.0.md#design-principles) |
| Paths & UI-occluded corners | Replace all constructed/worn paths and stone steps with continuous natural groundcover; reconstruct UI-occluded corners seamlessly to match adjacent natural terrain, gated by pre-implementation masks. | Owner accepted `D1` and `D2`. | [v0.1.0.md · Features](specs/v0.1.0.md#features) |
| Presentation & lighting | Completely static visual scene, silent, and locked to the reference scene's warm golden-hour lighting and clear atmosphere. | Owner selected `5C` and accepted `D3`. | [v0.1.0.md · Out of scope](specs/v0.1.0.md#out-of-scope) |
| Direct launch, display mode & lifecycle | Launch directly into a resizable widescreen window (defaulting to a reference-matching size) with no main menu, HUD, or gameplay UI; support clean exit via `Esc` or standard window close; keep the view intact and silent on focus loss, minimize, or resize without stuck keys. | Owner accepted `D4` and proposed default 2. | [v0.1.0.md · Edge cases](specs/v0.1.0.md#edge-cases) |
| Reference asset non-shipping & verification | Ship only the recreated 1:1 environment; never ship the reference image inside the player build. Verify externally at reference resolution/framing via side-by-side and opacity overlay. | Owner clarified in Q6 (`Build the copy and ship the copy, not the reference art`). | [v0.1.0.md · Release acceptance](specs/v0.1.0.md#release-acceptance) |
| Aspect ratio handling | Preserve exact reference framing across displays using letterboxing/pillarboxing on non-matching aspect ratios and clean scaling on matching widescreen resolutions. | Owner delegated Q7 and accepted `D6`. | [v0.1.0.md · Edge cases](specs/v0.1.0.md#edge-cases) |
| Superseded doc cleanup & scope sync | Owner authorized direct removal of conflicting seeded-world and old `v0.1.0` roadmap wording from `docs/vision.md` and `docs/project-profile.md`, and syncing `docs/scope.md` line 20 on the `v0.1.0-spec` branch. | Owner selected `Q3B` and `6A`. | [scope.md · Milestone Status](scope.md#milestone-status) |
