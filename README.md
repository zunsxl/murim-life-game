# Murim Life Game

A Unity game set in the martial world of Murim.

## Project version

**Project version:** Pre-release (not yet initialized in `ProjectSettings/ProjectSettings.asset`).

## Folder layout

| Path | Purpose |
|---|---|
| `agents/` | Independent role instructions (`product-designer.md`, `software-architect.md`, `implementer.md`, `technical-writer.md`) |
| `quality-gates/` | Deterministic zero-dependency Python 3 quality gates and unit tests |
| `docs/` | Living project documentation (`vision.md`, `scope.md`, `decisions.md`, `project-profile.md`, `art-direction.md`, `game.md`, `backlog.md`) |
| `docs/specs/` | Versioned product and technical specifications (`vX.Y.Z.md`, `vX.Y.Z-technical.md`) and spec templates |
| `docs/design/` | Supporting design notes |
| `docs/reviews/` | Playtest feedback and review records |
| `docs/references/` | Visual reference images (never shipped as runtime assets) |
| `Assets/Scripts/Core/` | Pure C# game rules and deterministic simulation (`Murim.Core`, no UnityEngine references) |
| `Assets/Scripts/View/` | Unity presentation, input, and UI (`Murim.View`) |
| `Assets/StreamingAssets/Data/` | Versioned JSON tunables (`schemaVersion`, `<= 64 KiB`) |
| `Assets/Art/` | Production art assets and [ART_README.md](Assets/Art/ART_README.md) |
| `Assets/Tests/Engine/` | Unity EditMode and PlayMode tests |
| `tests/Core/` | Standalone .NET NUnit Core tests |
| `tests/playtest/` | Owner playtest scripts |
| `tools/` | Build and verification scripts (see [tools/README.md](tools/README.md)) |

## Controls

To be documented when `v0.1.0` is implemented.

## Configuration

Runtime tunables live in JSON files under `Assets/StreamingAssets/Data/`. See [docs/project-profile.md](docs/project-profile.md) for schema and fallback rules.

## Dev console

To be documented when developer controls are implemented.

## Running deterministic quality gates

```bash
# List all gates
python quality-gates/run_gates.py --list

# Run the quality-gates unit test suite
python -m unittest discover -s quality-gates/tests -v
```

See [quality-gates/README.md](quality-gates/README.md) for per-gate usage, exit codes, and waiver rules.
