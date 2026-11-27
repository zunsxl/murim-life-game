# Tools & Verification

This directory holds build, verification, and helper scripts for the repository, including the standalone `.NET 10` project for `Murim.Core` (`tools/dotnet/Murim.Core.csproj`).

## Deterministic quality gates

Deterministic quality gates live in [quality-gates/](../quality-gates/README.md) and require only Python 3 (standard library):

```bash
# List available gates
python quality-gates/run_gates.py --list

# Product spec + art inventory gates
python quality-gates/run_gates.py product-spec art-inventory --spec docs/specs/vX.Y.Z.md

# Technical spec + security gates
python quality-gates/run_gates.py requirements-architecture security-privacy \
  --spec docs/specs/vX.Y.Z-technical.md \
  --product-spec docs/specs/vX.Y.Z.md

# Art quality gate (compare supplied art against art delivery contracts)
python quality-gates/run_gates.py art-quality \
  --spec docs/specs/vX.Y.Z-technical.md

# Implementation code quality + correctness gates
python quality-gates/run_gates.py code-quality correctness-maintainability \
  --base <base-sha> --head <head-sha> \
  --spec docs/specs/vX.Y.Z-technical.md

# Run gate unit tests
python -m unittest discover -s quality-gates/tests -v
```

## Verification runner (`tools/verify-all.sh`)

Run `tools/verify-all.sh` from the repository root (`bash tools/verify-all.sh ...`). Unity suites (`engine`, `art`, `build`) read the Unity `6000.3.29f1` editor executable path from the `UNITY_EDITOR` environment variable; `core` requires the .NET 10 SDK (`10.0.100`, pinned in `global.json`). Outputs and logs are written under `artifacts/`.

```bash
# Focused suite run (--filter is supported for core and engine only)
bash tools/verify-all.sh --suite <core|engine|art|build> [--filter <expr>]

# Tooling self-test (T-TOOL-01)
bash tools/verify-all.sh --self-test

# Full verification run (all suites: core, engine, art, build)
bash tools/verify-all.sh
```

| Suite | What it runs |
|---|---|
| `core` | `dotnet test tests/Core/Murim.Core.Tests.csproj -c Release` (`T-CORE-*`, `T-BAL-*`) |
| `engine` | Unity batchmode EditMode and PlayMode tests excluding category `Art` (`T-ENG-*`) |
| `art` | Unity batchmode EditMode tests with category `Art` (`T-ART-*`) |
| `build` | Clean Windows x64 release build (`Builds/Windows/MurimLifeGame.exe` via `Murim.Editor.BuildScript.BuildWindowsRelease`, `BuildOptions.None`) followed by the `-seed 1` player smoke check (`T-BLD-01`, `T-BLD-02`) |

Exit codes: `0` pass, `1` test/build failure (including missing or zero-test Unity results XML), `2` invalid arguments, missing tool (`dotnet` / `UNITY_EDITOR`), or `MURIM_VERIFY_STUB_MODE` set outside `--self-test`.

See [docs/project-profile.md](../docs/project-profile.md) for the path-to-suite map.
