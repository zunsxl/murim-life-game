# Changelog

All notable changes to this project are documented in this file.

The format follows semantic versioning (`vMAJOR.MINOR.PATCH`). Merging a version into `main` does not establish a
release; unreleased versions are marked as candidates until the owner confirms a release.

## [Unreleased]

- *(No unreleased changes.)*

## [v0.1.0] - 2026-11-27

Confirmed Windows (x64) release ([v0.1.0](https://github.com/zunsxl/murim-life-game/releases/tag/v0.1.0)): *Mountain Valley World & Camera Exploration* ([product spec](docs/specs/v0.1.0.md)).

### Added
- Bootstrapped Unity `6000.3.29f1` URP 2D Renderer project (`Murim.Core`, `Murim.View`, `Murim.Editor`), standalone `.NET 10` Core project (`tools/dotnet/Murim.Core.csproj`, `tests/Core/`), Unity EditMode/PlayMode/Art tests (`Assets/Tests/Engine/`), `tools/verify-all.sh`, and `tests/playtest/v0.1.0.md`.
- Implemented seed-deterministic integer-only procedural mountain valley generation (`ValleyGenerator`, `512 m × 512 m` world, three elevation tiers, front-facing rounded embankments, gentle connecting slopes, spring pool, brook, single waterfall, plunge pool, meandering stream, 8 blended ground layers, and 17 static prop variants with soft ground shadows), fixed 3-4-5 oblique projection (`Projection`), back-to-front prop sorting (`PropOrder`), and `-seed <0..4294967295>` launch override (`SeedArgument`, `SeedSource`).
- Delivered and integrated strictly 2D painterly oblique art (`ART-001`–`ART-010`, 38 PNGs under `Assets/Resources/Art/`), `WorldProps.spriteatlas`, licensed static `LibreBaskerville-Regular.ttf` + `OverlaySerif SDF.asset` (`ART-011`), URP Sprite Unlit Shader Graphs (`Terrain`, `Water`, `WorldSprite`, `Backdrop`, and `Atmosphere.shadersubgraph`), and runtime materials.
- Implemented damped `WASD`/Arrow and 1:1 right-mouse drag panning, cursor-anchored wheel zoom, `Home`/`R` vista reset, boundary clamping with no exposed void, focus/minimize/pointer-exit input cancellation (`CameraRig`, `CameraRigDriver`), and `world.json` / `camera.json` config loading with whole-file fallback (`JsonConfigLoader`, `JsonGuard`).
- Implemented resizable windowed launch (`1600 × 900` default) and minimal `Esc` pause/quit system overlay (`SystemOverlay.prefab`, `Paused`, `Resume`, `Quit`).
- Initialized repository agent profiles (`agents/`), deterministic Python 3 quality gates (`quality-gates/`), and living documentation (`docs/`).
