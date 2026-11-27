# Art Inventory & Credits

This document tracks committed production art assets, their `ART-XXX` inventory IDs, sources, and licensing/attribution credits.

> Visual reference images belong in `docs/references/` and are **never** committed here as shipping art.

## Committed Assets

| ART ID | Asset / Family | Path | Source & License | Version Added |
|---|---|---|---|---|
| ART-001 | Ground surfaces (4) | `Assets/Resources/Art/Terrain/terrain_moss_turf.png; terrain_slate_soil.png; terrain_mossy_stone.png; terrain_pebble_bank.png` | Original built-in image_gen painting; owner-approved production use; no third-party reference pixels copied | v0.1.0 |
| ART-002 | Banks, hillside and lip (4) | `Assets/Resources/Art/Terrain/terrain_bank_grassy.png; terrain_bank_earth_stone.png; terrain_hillside_slope.png; Props/Banks/bank_lip.png` | Original built-in image_gen painting; owner-approved production use; no third-party reference pixels copied | v0.1.0 |
| ART-003 | Water textures (4) | `Assets/Resources/Art/Water/water_stream.png; water_shore.png; water_fall.png; water_foam.png` | Original built-in image_gen painting; owner-approved production use; no third-party reference pixels copied | v0.1.0 |
| ART-004 | Mountain pines (4) | `Assets/Resources/Art/Props/Pines/` | Original built-in image_gen painting; owner-approved production use; no third-party reference pixels copied | v0.1.0 |
| ART-005 | Bamboo groves (3) | `Assets/Resources/Art/Props/Bamboo/` | Original built-in image_gen painting; owner-approved production use; no third-party reference pixels copied | v0.1.0 |
| ART-006 | Rocks (5) | `Assets/Resources/Art/Props/Rocks/` | Original built-in image_gen painting; owner-approved production use; no third-party reference pixels copied | v0.1.0 |
| ART-007 | Ground flora (4) | `Assets/Resources/Art/Props/Flora/` | Original built-in image_gen painting; owner-approved production use; no third-party reference pixels copied | v0.1.0 |
| ART-008 | Ridge ground and soft shadow (2 PNGs) | `Assets/Resources/Art/Terrain/terrain_ridge_ground.png; Props/Shadows/shadow_soft.png` | Original built-in image_gen painting; owner-approved production use; no third-party reference pixels copied | v0.1.0 |
| ART-009 | Panel, divider and button states (5) | `Assets/Resources/Art/UI/overlay_*.png` | Original deterministic raster drawing; owner-approved production use | v0.1.0 |
| ART-010 | Sky and mountain layers (3) | `Assets/Resources/Art/Backdrop/` | Original image_gen mountain paintings and deterministic sky gradient; owner-approved production use | v0.1.0 |
| ART-011 | Libre Baskerville Regular (static) | `Assets/Resources/Art/UI/Fonts/LibreBaskerville-Regular.ttf` | Pablo Impallari and Rodrigo Fuenzalida; SIL Open Font License 1.1 | v0.1.0 |

## Credits & Licenses

- Libre Baskerville Regular: [upstream](https://github.com/impallari/Libre-Baskerville), commit `9852edf75ece3af500a5ec61245f94788c3d4633`, `fonts/ttf/LibreBaskerville-Regular.ttf`. The verbatim SIL OFL 1.1 license is beside the font as `LibreBaskerville-Regular-LICENSE.txt`.
- Generated world paintings and original UI drawings were created for this project using the approved directions. No reference plate pixels are shipping assets. Prompts, processing and source generation IDs are in [delivery evidence](../../docs/art-deliveries/v0.1.0/DELIVERY.txt).
- Unity integration assets are committed and verified by `tools/verify-all.sh --suite art` (`T-ART-01`–`T-ART-05`): `.meta` texture/sprite import settings via `ArtImportPostprocessor.cs`, single-page `Assets/Resources/Art/Props/WorldProps.spriteatlas`, `Assets/Resources/Art/UI/Fonts/OverlaySerif SDF.asset` (static SDFAA, `2048 × 2048`, padding `9`, sampling point size `280`, ASCII `U+0020–U+007E`, set as TMP default font), `ART-008` atmosphere globals (`Assets/Shaders/Atmosphere.shadersubgraph` and `GameBootstrap`), and `ART-011` overlay title/button layout (`Assets/Resources/Prefabs/SystemOverlay.prefab`).
- Some generated source images were upsampled to contract dimensions; visual close/wide zoom acceptance and overview tile-repetition checks are covered by the owner playtests (`PT-2`, `PT-7` in [tests/playtest/v0.1.0.md](../../tests/playtest/v0.1.0.md)).
