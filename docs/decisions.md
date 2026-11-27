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

- **Product spec approval:** Amended and approved by owner on 2026-11-27 (Draft 6, strictly 2D amendment; supersedes Draft 5) — exact quote: `"Approved."` → [docs/specs/v0.1.0.md](specs/v0.1.0.md)
- **Scope & deferrals:** Focus strictly on procedurally generated multi-level valley world, fixed oblique 2D camera movement, and minimal `Esc` pause/quit overlay. 3D models/meshes/dynamic 3D lighting, playable/movable units, NPCs, buildings, bridges, roads/stairs, natural footpaths, gameplay HUD/minimap, time/cultivation mechanics, environmental animations, camera rotation, screen-edge scrolling, middle-mouse drag, custom cursor, and audio are deferred → [Out of scope](specs/v0.1.0.md#out-of-scope).
- **Visual medium & verticality:** Strictly 2D painterly oblique 3/4 art (zero 3D models) with front-facing painted embankments, baked upper-left lighting, soft 2D cast shadows, and front-to-back depth sorting while retaining three logical elevation tiers, one primary waterfall, and gentle continuous slopes linking tiers for future player-built structures, bridges, and XYZ flying/selection *(replaced prior Stylized 3D painterly decision from Draft 5 on 2026-11-27)* → [F-01, F-03](specs/v0.1.0.md#features), [EC-5](specs/v0.1.0.md#edge-cases).
- **Seed & determinism:** Normal launches use a fresh random seed; external seed override enables deterministic reproduction across launches → [F-01](specs/v0.1.0.md#features), [EC-10](specs/v0.1.0.md#edge-cases).
- **Camera & window defaults:** `WASD`/Arrow pan + right-mouse-drag only (zoom-scaled pan travel), cursor-anchored wheel zoom, `Home`/`R` reset to central valley overview (interrupted by new input), crisp closest-zoom 2D sprite layering and front-to-back depth sorting (`EC-5`), resizable windowed launch with clean `X`/`Alt+F4` close → [Controls](specs/v0.1.0.md#controls), [Edge cases](specs/v0.1.0.md#edge-cases).
- **Cultural setting:** Classical Chinese wuxia / Jianghu natural landscape exclusively; Korean Murim permanently excluded → [Design principles](specs/v0.1.0.md#design-principles).
- **AI image generation policy update:** Owner removed the standing ban on AI image generation for both pre-approval illustrative mockups and production art (see *Art sourcing rules* above). `v0.1.0` reference art and illustrative comparison mockups (Plates 1–2 AI-generated; Plates 3–4 UI/vector composites) are archived in `docs/references/` for reference only and never ship as runtime assets.
- **Art direction choices (`ART-001`–`ART-011`, strictly 2D):** Reviewed via text options and illustrative comparison plates attached in `docs/references/` → [Art requirements](specs/v0.1.0.md#art-requirements):
  - `ART-001` (Valley Ground Terrain Surfaces): **Option C** — Rich Verdant Moss & Wet Slate Earth (2D)
  - `ART-002` (Multi-Level Embankments & Natural Slopes): **Option C** — Rounded Terraced Earth-and-Stone Banks (2D)
  - `ART-003` (Static Mountain Stream & Waterfall Surfaces): **Option C** — Dark Mirror Pool & Crisp White Waterfall (2D)
  - `ART-004` (Mountain Pine Trees): **Option A** — Layered Painterly Cloud-Canopy Pines (2D)
  - `ART-005` (Bamboo Grove Clusters): **Option A** — Sunlit Olive-Jade Vertical Groves (2D)
  - `ART-006` (Mossy Boulders & Rock Outcrops): **Option A** — Moss-Capped Warm Slate Boulders (2D)
  - `ART-007` (Wildflowers, Shrubs & Ground Foliage): **Option A** — Delicate White Wildflowers & Muted Mountain Brush (2D)
  - `ART-008` (Valley Border Framing, Ground Shadows & Atmospheric Mood): **Option C** — Golden Late-Afternoon Valley Glow, Ground Shadows & Close Ridge Framing (2D; 3 elements including reusable soft 2D ground shadow sprite)
  - `ART-009` (Minimal System Overlay Frame & Buttons): **Option A** — Dark Slate-Jade Frame with Muted Brass Trim
  - `ART-010` (Valley Sky & Distant Mountain Horizon): **Option A** — Golden-Amber Haze & Layered Misty Peaks (2D)
  - `ART-011` (System Overlay Typography & Paused Title): **Option A** — Clean Classical Serif Title & Buttons

- **Technical decisions (`v0.1.0`):** recorded with the technical spec, revised on 2026-11-27 for the strictly 2D amendment (Draft 6) → [docs/specs/v0.1.0-technical.md](specs/v0.1.0-technical.md):
  - **Engine and toolchain:** Unity 6.3 LTS (`6000.3` stream, exact patch frozen in `ProjectSettings/ProjectVersion.txt` by `ARCH-001`), URP with the 2D Renderer, Input System, Mono x64; standalone Core tests on the .NET 10 SDK. Reason: current LTS lines; Mono avoids an IL2CPP toolchain for contributors *(replaced URP Forward from the 3D draft)* → [§3.6](specs/v0.1.0-technical.md#36-project-setup-build-and-verification-tooling-supports-f-01f-05).
  - **Logical 3D world, strictly 2D picture:** Core keeps real positions and heights (three tiers, slopes, water levels, prop heights); View only projects them and draws flat painted layers and sprites with one orthographic camera. No model assets, lights, or depth buffer exist in the project. Reason: satisfies the strictly 2D amendment while keeping the elevation foundation for future structures, bridges, and XYZ flight → [§3.3.1](specs/v0.1.0-technical.md#331-rendering-setup-and-the-strictly-2d-rule).
  - **Fixed oblique projection (3-4-5):** screen `u = x`, `v = 0.6 × z + 0.8 × height` (a view from 36.87° above the horizon), integer-exact in Core and constant. Reason: one projection shared by generation, drawing, camera, and art; all art is painted for it → [§2.1](specs/v0.1.0-technical.md#21-units-and-coordinates).
  - **Integer-only, constructive generation:** the valley is generated with integer arithmetic only on a 512 m × 512 m, 1 m heightfield, and every invariant (three tiers, one waterfall and stream, one gentle ramp per tier pair, viewer-facing embankments) holds by construction. Reason: identical results in the test host, Editor, and player with one golden hash; no retry loops → [§3.1](specs/v0.1.0-technical.md#31-valley-generation-f-01).
  - **World scale and tier heights (balance):** valley floor 368 m wide (2.35 screens at widest zoom); mid tier 12 m and upper tier 26 m above the basin; gentle ramps 5× their rise (average gradient 0.20); one waterfall of about 11 m from the mid tier into the basin head. Unchanged by the 2D amendment. Reason: a 12 m bank is 22 % of the default view height, so tiers read as separate levels while ramps stay gentle enough for future movement → [§3.1.9](specs/v0.1.0-technical.md#319-worldjson).
  - **Prop density (balance):** about 7,300–8,400 instances per valley at defaults (pines 400–530, bamboo 290–530, rocks 770–900, flora 5,500–6,500) with open plateaus kept below 12 % canopy cover and hard caps per family. Reason: dense ridges and banks, calm buildable meadows → [§3.2.4](specs/v0.1.0-technical.md#324-density-rationale-and-measured-ranges).
  - **Depth sorting and draw order:** fixed at launch with no depth buffer: backdrop, terrain (north rows first), water, bank lips, ground shadows, then one sprite per prop ordered by ground depth (farther north first) on any tier. Reason: exact for upright sprites, deterministic, and cannot flicker or pop (`EC-5`) → [§2.4](specs/v0.1.0-technical.md#24-layers-scene-and-draw-order), [§3.2.5](specs/v0.1.0-technical.md#325-back-to-front-order-f-03-ec-5).
  - **Baked light:** the upper-left sun is painted into all art; generated slopes get a fixed relief shade and pines, bamboo, and rocks a soft shadow sprite to their lower right; sprites are never rotated or mirrored, and copies vary by scale and a 92–100 % tone only → [§3.1.7](specs/v0.1.0-technical.md#317-view-frame-and-relief-shade), [§3.3.4](specs/v0.1.0-technical.md#334-props-lips-and-ground-shadows).
  - **Camera model and feel (balance):** an orthographic view rectangle on the screen plane; view height 12–88 m (default 44 m; 21–156 m wide at 16:9); key pan `0.9 × view height` per second (one screen width in 2.0 s at any zoom); damping times 0.10 s / 0.22 s (pan), 0.12 s (zoom), 0.35 s (reset). Motion model lives in `Murim.Core` for deterministic testing *(replaced the 3D draft's pitched perspective rig, 12–120 m distance, and terrain-following clearance)* → [§3.4](specs/v0.1.0-technical.md#34-camera-exploration-f-04).
  - **Crisp sprites:** world art is authored at 128 px per metre and the closest zoom never shows it above 1.25× native size, whatever the window height → [§3.4.6](specs/v0.1.0-technical.md#346-crisp-sprite-layering-across-zoom-ec-5).
  - **Camera bounds:** the view rectangle is clamped to the screen-plane rectangle the terrain always covers; the only other thing ever visible is the horizon backdrop above the north ridge, limited to 30 % of the screen above the lowest crest point; very wide windows limit zoom-out instead of exposing the sides → [§3.4.5](specs/v0.1.0-technical.md#345-bounds-and-the-no-void-rule-ec-3-ec-4-ec-8).
  - **Horizon as a screen-anchored backdrop:** `ART-010` is composed in screen space behind the world (the camera never rotates). Reason: no far scenery needed and no void possible → [§3.3.5](specs/v0.1.0-technical.md#335-horizon-backdrop).
  - **Seed override and inputs:** `-seed <0…4294967295>` on the command line; tunables in `world.json` and `camera.json` with whole-file fallback, 64 KiB and depth limits; no dev controls, telemetry, or crash upload in `v0.1.0` → [§2.5](specs/v0.1.0-technical.md#25-configuration-loading), [§3.1.1](specs/v0.1.0-technical.md#311-seed-ec-10).
  - **Input interpretation (EC-7):** the pointer leaving the window, focus loss, minimise, opening the overlay, and a reset all cancel held pan keys and drags until they are pressed again → [§3.4.3](specs/v0.1.0-technical.md#343-suppression-and-cancel).
  - **Art delivery:** all world art is flat 2D painting: 8 seamless ground tiles, 7 water and backdrop paintings, and 18 transparent sprites (17 props plus the shared ground shadow) packed into one atlas; the atmosphere tint and haze (`ART-008`) and the `Paused` title treatment (`ART-011`) are engine values rather than image files *(replaced the 3D draft's 17 FBX models and real-time lighting mood)* → [§4](specs/v0.1.0-technical.md#4-art-delivery-contracts).
  - **Technical spec approval:** Approved by owner on 2026-11-27 at commit `31dba70` (strictly 2D revision), conditional on the `requirements-architecture` and `security-privacy` gates passing — exact quote: `"Run the gates. If they pass, push it to the PR."` Both gates passed with 0 Critical, 0 High, 0 Medium, and 0 Low findings (`RA-1`–`RA-9`; `SP-1`–`SP-8`, with `SP-3`, `SP-6`, and `SP-7` skipped because no code, manifests, or tooling scripts exist yet) → [docs/specs/v0.1.0-technical.md](specs/v0.1.0-technical.md)

- **Production art delivery approval (`v0.1.0`):** Owner approved the 38 PNG assets and licensed static serif font on 2026-11-27 and authorized publication to the existing PR — exact quote: `"Approved.\n\nPush this to the pull-request."` Assets follow the approved `ART-001`–`ART-011` choices and technical art contract. The file-level audit passed 143 checks; Unity import, atlas/TMP integration and in-game acceptance remain implementation work → [delivery evidence](art-deliveries/v0.1.0/DELIVERY.txt).
