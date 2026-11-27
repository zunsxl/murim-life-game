# Deterministic Quality Gates

Zero-dependency Python 3 quality gates for Murim Universe. Every gate is a pure deterministic program: the same
inputs always produce the exact same checks, findings, decision, and exit code.

## Exit codes and decisions

| Decision | Exit code | Meaning |
|---|---|---|
| `PASS` | `0` | No unwaived `CRITICAL` or `HIGH` findings and no blocked prerequisites |
| `FAIL` | `1` | At least one unwaived `CRITICAL` or `HIGH` finding |
| `BLOCKED` | `2` | Missing required input file, invalid git revision, or unavailable required tool |

## Available gates

| Gate | Command | What it verifies |
|---|---|---|
| `product-spec` | `python quality-gates/run_gates.py product-spec --spec <path>` | Required structure/Status/Goal, no unresolved placeholders, unique IDs, no untestable vague quantifiers, no code/implementation leakage, valid local links/anchors, art inventory cross-references, and decision-log presence |
| `art-inventory` | `python quality-gates/run_gates.py art-inventory --spec <path>` | Inventory table presence, unique `ART-NNN` IDs, exact numeric counts (no `etc.`/`as needed`), valid `New`/`Reuse`/`Modify` dispositions + existing asset paths, resolved directions, valid `F-XX` linkage + acceptance criteria, no-art justifications, and no reference/watermarked shipping sources |
| `art-quality` | `python quality-gates/run_gates.py art-quality --spec <tech-spec>` | Compares supplied PNG textures/sprites, binary FBX 3D models, TTF/OTF fonts, and Unity `.meta` import settings against the spec's Art delivery contracts (dimensions, bit depth, RGB/RGBA, alpha/pixel rules, seamless edge continuity, FBX topology/UV0/bounds/no-rig, font static & ASCII coverage + license, sourcing hygiene, and `ART_README.md` credits) |
| `requirements-architecture` | `python quality-gates/run_gates.py requirements-architecture --spec <tech-spec> --product-spec <product-spec>` | Required header & sections, baseline git commit existence, full product AC-to-section/ticket/check traceability, acyclic `ARCH-NNN` ticket DAG, dependency-ordered execution plan with disjoint parallel ownership, test-plan check ID integrity, `ART-NNN` delivery contract coverage, `bundleVersion` match, and link/placeholder hygiene |
| `security-privacy` | `python quality-gates/run_gates.py security-privacy --spec <tech-spec>` or `--base <sha> --head <sha>` | Secret scanning (redacted output), hardcoded local user paths/PII, unsafe deserialization and shell injection sinks, release-build debug flag gating, JSON config size/schema bounds, exact dependency version pinning, and tooling script `set -euo pipefail` / failure propagation |
| `code-quality` | `python quality-gates/run_gates.py code-quality --base <sha> --head <sha>` (or `--paths ...`) | `Murim.Core` pure-C# layer boundary & `.asmdef`, no LINQ/allocations/scene lookups in `Update` loops, method length/nesting/parameter/complexity thresholds, normalized duplicate-block detection, Core debug leftovers, `<TreatWarningsAsErrors>`, and optional `dotnet format --verify-no-changes` (`--run-tools`) |
| `correctness-maintainability` | `python quality-gates/run_gates.py correctness-maintainability --base <sha> --head <sha> --spec <tech-spec>` | Core determinism invariants (no unseeded RNG, clocks, threads, or CPU-dependent trig), no `LayerMask.NameToLayer`/`GetMask`, `CultureInfo.InvariantCulture` parsing, no empty `catch` blocks, `StreamingAssets/Data/*.json` validation, `MonoBehaviour` event unsubscribe symmetry, no `[Ignore]`/`[Explicit]` tests, test-plan check ID traceability, and optional focused test execution (`--run-tests`) |

## Examples

```bash
# List gates
python quality-gates/run_gates.py --list

# Validate a product spec and its art inventory together
python quality-gates/run_gates.py product-spec art-inventory --spec docs/specs/v0.1.0.md

# Validate a technical spec for architecture traceability and security
python quality-gates/run_gates.py requirements-architecture security-privacy \
  --spec docs/specs/v0.1.0-technical.md \
  --product-spec docs/specs/v0.1.0.md

# Validate supplied art assets against Art delivery contracts
python quality-gates/run_gates.py art-quality \
  --spec docs/specs/v0.1.0-technical.md \
  --require-meta

# Validate an implementation diff (and emit JSON for CI)
python quality-gates/run_gates.py code-quality correctness-maintainability \
  --base origin/v0.1.0-spec --head HEAD \
  --spec docs/specs/v0.1.0-technical.md \
  --format json
```

## Explicit waivers

To waive a specific deterministic finding in `docs/decisions.md`, record `WAIVED: <FINDING-ID>` (for example,
`WAIVED: CQ-F-001`) alongside the owner's reason. The gate still lists the finding in its report marked
`(WAIVED)` and does not fail on it.

## Running the gate test suite

```bash
python -m unittest discover -s quality-gates/tests -v
```
