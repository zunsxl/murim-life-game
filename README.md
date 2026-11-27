# Murim Life Game

An offline, single-player Unity game set in a classical Chinese wuxia / Jianghu mountain world.

## Project version

**Project version:** `v0.1.0` (`bundleVersion: 0.1.0` in `ProjectSettings/ProjectSettings.asset`, released 2026-11-27 for Windows x64).

Generates a seed-deterministic three-tier mountain valley presented in strictly 2D painterly oblique art, with damped keyboard and right-mouse camera exploration and a minimal `Esc` pause/quit overlay. See [docs/specs/v0.1.0.md](docs/specs/v0.1.0.md).

## Folder layout

| Path | Purpose |
|---|---|
| `agents/` | Independent role instructions (`product-manager.md`, `designer.md`, `software-architect.md`, `implementer.md`, `code-reviewer.md`, `technical-writer.md`) |
| `quality-gates/` | Deterministic zero-dependency Python 3 quality gates and unit tests |
| `docs/` | Living project documentation (`vision.md`, `scope.md`, `decisions.md`, `project-profile.md`, `art-direction.md`, `game.md`, `backlog.md`) |
| `docs/specs/` | Versioned product and technical specifications (`vX.Y.Z.md`, `vX.Y.Z-technical.md`) and spec templates |
| `docs/design/` | Supporting design notes |
| `docs/reviews/` | Playtest feedback and review records |
| `docs/references/` | Visual reference images and comparison plates (never shipped as runtime assets) |
| `docs/art-deliveries/` | Approved production art delivery manifests, reports, and preview boards (never shipped) |
| `Assets/Scripts/Core/` | Pure C# game rules and deterministic simulation (`Murim.Core`, no UnityEngine references) |
| `Assets/Scripts/View/` | Unity presentation, input, and UI (`Murim.View`) |
| `Assets/StreamingAssets/Data/` | Versioned JSON tunables (`world.json`, `camera.json`; `schemaVersion: 1`, `<= 64 KiB`) |
| `Assets/Resources/Art/` | Production 2D art assets, sprite atlas (`WorldProps.spriteatlas`), font, and TMP asset |
| `Assets/Art/` | Art inventory and licensing credits in [ART_README.md](Assets/Art/ART_README.md) |
| `Assets/Resources/Materials/` | Runtime materials for terrain, water, props, and backdrop |
| `Assets/Resources/Prefabs/` | UI prefabs (`SystemOverlay.prefab`) |
| `Assets/Scenes/` | Unity scenes (`Valley.unity`) |
| `Assets/Settings/` | URP 2D Renderer and Input System settings assets |
| `Assets/Shaders/` | URP Sprite Unlit Shader Graphs and `Atmosphere.shadersubgraph` |
| `Assets/Editor/` | Windows release build script, art manifest/rules, and texture import postprocessor (`Murim.Editor`) |
| `Assets/Tests/Engine/` | Unity EditMode, PlayMode, and Art validation tests |
| `tests/Core/` | Standalone .NET 10 NUnit Core tests |
| `tests/playtest/` | Owner playtest checklists (`v0.1.0.md`) |
| `tools/` | Build and verification scripts (`verify-all.sh`, `dotnet/Murim.Core.csproj`; see [tools/README.md](tools/README.md)) |

## Controls

Launch the Windows build directly (`MurimLifeGame.exe`) in resizable windowed mode (`1600 × 900` default). Optionally pass `-seed <0..4294967295>` on the command line (for example `MurimLifeGame.exe -seed 20261127`) to reproduce a specific valley.

### While exploring

| Input | Behavior |
|---|---|
| `W` / `A` / `S` / `D` or Arrow keys | Pan continuously along screen-aligned horizontal directions (zoom-scaled, damped; diagonal speed normalized; opposing keys cancel) |
| Right-mouse button hold + drag | Drag the camera 1-to-1 across the valley with gentle release damping |
| Mouse scroll wheel | Zoom continuously toward the world point under the cursor between closest detail and widest overview |
| `Home` or `R` | Smoothly ease back to the starting waterfall vista (cancelled immediately by new pan, drag, or scroll input) |
| `Esc` | Open or close the minimal `Paused` system overlay |

Left-clicking the world, middle-mouse dragging, and resting the cursor on window edges have no effect.

### While system overlay is open & window controls

| Input | Behavior |
|---|---|
| `Esc` or left-click `Resume` | Close the system overlay and restore camera control |
| Left-click `Quit` | Exit cleanly to the desktop |
| Window close (`X`) or `Alt+F4` | Exit cleanly to the desktop from exploring or from the overlay |
| Window border drag, maximize, minimize | Resize, maximize, or minimize cleanly while keeping camera bounds intact |

See [docs/game.md](docs/game.md) for full gameplay rules, camera bounds, and active tunable values.

## Configuration

Runtime tunables live in `world.json` and `camera.json` under `Assets/StreamingAssets/Data/` (or `MurimLifeGame_Data/StreamingAssets/Data/` in a built Windows player). Each file requires `"schemaVersion": 1`, is capped at `64 KiB` (`65536` bytes) and JSON nesting depth `4`, and falls back wholesale to built-in defaults on validation or read errors. See [docs/game.md](docs/game.md#configuration) for all fields and valid ranges, and [docs/project-profile.md](docs/project-profile.md) for invariants.

## Dev console

None. `v0.1.0` ships no developer console, debug commands, or debug overlay.

## Building and verifying

The project targets Unity `6000.3.29f1` (pinned in `ProjectSettings/ProjectVersion.txt`) and the .NET 10 SDK (`10.0.100`, pinned in `global.json`). Run `bash tools/verify-all.sh` (or `--suite <core|engine|art|build>`) to execute tests and build the Windows x64 release player. See [tools/README.md](tools/README.md) for usage details.

## Running deterministic quality gates

```bash
# List all gates
python quality-gates/run_gates.py --list

# Run the quality-gates unit test suite
python -m unittest discover -s quality-gates/tests -v
```

See [quality-gates/README.md](quality-gates/README.md) for per-gate usage, exit codes, and waiver rules.
