# Tools & Verification

This directory holds build, verification, and helper scripts for the repository.

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

# Implementation code quality + correctness gates
python quality-gates/run_gates.py code-quality correctness-maintainability \
  --base <base-sha> --head <head-sha> \
  --spec docs/specs/vX.Y.Z-technical.md

# Run gate unit tests
python -m unittest discover -s quality-gates/tests -v
```

## Verification runner (`tools/verify-all.sh`)

Once the Unity and .NET Core projects are scaffolded, `tools/verify-all.sh` runs focused and full verification suites:

```bash
# Focused suite run
bash tools/verify-all.sh --suite <core|engine|art|build> [--filter <expr>]

# Tooling self-test
bash tools/verify-all.sh --self-test

# Full verification run (all suites)
bash tools/verify-all.sh
```

See [docs/project-profile.md](../docs/project-profile.md) for the path-to-suite map.
