"""Deterministic Code Correctness & Maintainability Gate (RC-1 .. RC-9)."""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
from pathlib import Path
from typing import List, Optional, Sequence, Set, Tuple

from .common import (
    GateReport,
    extract_cs_methods,
    git_changed_files,
    git_rev_parse,
    is_git_repo,
    iter_repo_files,
    load_waivers,
    parse_sections,
    parse_tables,
    run_cmd,
    strip_cs_comments_and_strings,
)

# Forbidden non-deterministic APIs in Assets/Scripts/Core/
NONDET_PATTERNS: List[Tuple[re.Pattern[str], str]] = [
    (re.compile(r"\b(?:System\.)?Random\b|\bnew\s+Random\s*\("), "unseeded System.Random"),
    (re.compile(r"\bUnityEngine\.Random\b"), "UnityEngine.Random"),
    (re.compile(r"\b(?:DateTime|DateTimeOffset)\.(?:Now|UtcNow|Today)\b"), "wall-clock time"),
    (re.compile(r"\b(?:Environment\.TickCount|Environment\.TickCount64|Stopwatch\.GetTimestamp)\b"), "non-deterministic clock/tick"),
    (re.compile(r"\bGuid\.NewGuid\s*\("), "random GUID generation"),
    (re.compile(r"\.\s*GetHashCode\s*\("), "runtime GetHashCode()"),
    (re.compile(r"\b(?:Task\.Run|ThreadPool\.QueueUserWorkItem|new\s+Thread\s*\(|Parallel\.(?:For|ForEach|Invoke))\b"), "concurrency/threading inside Core"),
    (
        re.compile(r"\b(?:Math|MathF)\.(?:Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Pow|Exp|Log|Log10|Log2)\s*\("),
        "CPU-dependent transcendental math in deterministic Core",
    ),
]

LAYER_NAME_RE = re.compile(r"\bLayerMask\.(?:NameToLayer|GetMask)\s*\(")

# Locale-sensitive float/double/decimal parsing without InvariantCulture
LOCALE_PARSE_RE = re.compile(
    r"\b(?:float|double|decimal|Single|Double|Decimal)\.(?:Parse|TryParse)\s*\(([^;]*)\)"
)

EMPTY_CATCH_RE = re.compile(r"\bcatch\b(?:\s*\([^)]*\))?\s*\{\s*\}")

DISABLED_TEST_RE = re.compile(r"\[\s*(?:Ignore|Explicit)\b|\bAssert\.(?:Inconclusive)\s*\(")

ALL_SCAN_EXTS = {".cs", ".json", ".py", ".sh"}


def _rel_posix(fp: Path, repo: Path) -> str:
    try:
        return fp.relative_to(repo).as_posix()
    except ValueError:
        return fp.as_posix()


def _is_core_file(rel: str) -> bool:
    low = rel.lower()
    return low.startswith("assets/scripts/core/") or "/scripts/core/" in low


def _is_view_file(rel: str) -> bool:
    low = rel.lower()
    return low.startswith("assets/scripts/view/") or "/scripts/view/" in low


def _is_test_file(rel: str) -> bool:
    low = rel.lower()
    return low.startswith("tests/") or low.startswith("assets/tests/") or "/tests/" in low


def _collect_files(
    repo: Path,
    base: Optional[str],
    head: Optional[str],
    paths: Optional[Sequence[str]],
    report: GateReport,
) -> List[Path]:
    if base or head:
        if not (base and head):
            report.add_blocked("Both --base and --head are required for git diff mode.")
            return []
        if not is_git_repo(repo):
            report.add_blocked(f"Not a git repository: {repo}")
            return []
        if not git_rev_parse(repo, base) or not git_rev_parse(repo, head):
            report.add_blocked(f"Invalid git revision(s): base={base}, head={head}")
            return []
        changed = git_changed_files(repo, base, head)
        if changed is None:
            report.add_blocked(f"Failed to compute git diff {base}...{head}")
            return []
        return [repo / r for r in changed if (repo / r).is_file() and (repo / r).suffix.lower() in ALL_SCAN_EXTS]

    if paths:
        return iter_repo_files(repo, paths=paths, exts=ALL_SCAN_EXTS)

    default_roots = [
        p
        for p in ("Assets/Scripts", "Assets/StreamingAssets/Data", "Assets/Tests", "tests")
        if (repo / p).exists()
    ]
    if not default_roots:
        return []
    return iter_repo_files(repo, paths=default_roots, exts=ALL_SCAN_EXTS)


def _extract_automated_check_ids(spec_path: Path) -> Set[str]:
    text = spec_path.read_text(encoding="utf-8", errors="replace")
    sections = parse_sections(text)
    ids: Set[str] = set()
    for sec in sections:
        if re.search(r"\btest\s+plan\b|\bcore\s+tests\b|\bengine\s+tests\b", sec.title, re.IGNORECASE):
            for tbl in parse_tables(sec.body, start_line_offset=sec.start_line + 1):
                if tbl.headers and tbl.headers[0].strip().lower() == "id":
                    for row in tbl.rows:
                        cid = row.cells[0].strip()
                        if re.fullmatch(r"T-(?:CORE|ENG|ART|BAL)-\d+", cid):
                            ids.add(cid)
    return ids


def run_correctness_maintainability_gate(
    repo: Path,
    base: Optional[str] = None,
    head: Optional[str] = None,
    paths: Optional[Sequence[str]] = None,
    spec_path: Optional[Path] = None,
    decisions_path: Optional[Path] = None,
    run_tests: bool = False,
    suite: Optional[str] = None,
    test_filter: Optional[str] = None,
) -> GateReport:
    target_desc = f"{base}...{head}" if base and head else (", ".join(paths) if paths else str(repo))
    report = GateReport(
        gate="Code Correctness & Maintainability",
        target=target_desc,
        _finding_prefix="RC",
    )

    if decisions_path is None:
        candidate = repo / "docs" / "decisions.md"
        if candidate.is_file():
            decisions_path = candidate
    waivers = load_waivers(decisions_path)

    files = _collect_files(repo, base, head, paths, report)
    if report.blocked_reasons:
        report.add_check("RC-1", "Core determinism invariants", "BLOCKED", "; ".join(report.blocked_reasons))
        return report

    cs_files = [f for f in files if f.suffix.lower() == ".cs"]
    core_cs = [f for f in cs_files if _is_core_file(_rel_posix(f, repo))]
    view_cs = [f for f in cs_files if _is_view_file(_rel_posix(f, repo))]
    prod_cs = [f for f in cs_files if not _is_test_file(_rel_posix(f, repo))]
    test_cs = [f for f in cs_files if _is_test_file(_rel_posix(f, repo))]

    # ------------------------------------------------------------------
    # RC-1: Core determinism invariants
    # ------------------------------------------------------------------
    rc1_hits = 0
    for fp in core_cs:
        rel = _rel_posix(fp, repo)
        cleaned = strip_cs_comments_and_strings(fp.read_text(encoding="utf-8", errors="replace"))
        for ln, line in enumerate(cleaned.splitlines(), start=1):
            for pat, desc in NONDET_PATTERNS:
                if pat.search(line):
                    # Allow explicit custom namespace Murim.Core.Random
                    if desc == "unseeded System.Random" and re.search(r"\bMurim\.Core\.Random\b", line):
                        continue
                    rc1_hits += 1
                    report.add_finding(
                        "RC-1",
                        "CRITICAL",
                        f"{rel}:{ln}",
                        f"Determinism violation in Core: {desc}.",
                        "Use seeded `Hash32`/`Pcg32` and IEEE-exact deterministic arithmetic only inside `Murim.Core`.",
                    )

    report.add_check(
        "RC-1",
        "Core determinism invariants (no unseeded RNG, clocks, threads, or CPU-dependent trig)",
        "SKIP" if not core_cs else ("PASS" if rc1_hits == 0 else "FAIL"),
        f"Checked {len(core_cs)} Core C# file(s); {rc1_hits} violation(s)",
    )

    # ------------------------------------------------------------------
    # RC-2: Unity layer index safety (no LayerMask.NameToLayer / GetMask)
    # ------------------------------------------------------------------
    rc2_hits = 0
    for fp in prod_cs:
        rel = _rel_posix(fp, repo)
        cleaned = strip_cs_comments_and_strings(fp.read_text(encoding="utf-8", errors="replace"))
        for ln, line in enumerate(cleaned.splitlines(), start=1):
            m = LAYER_NAME_RE.search(line)
            if m:
                rc2_hits += 1
                report.add_finding(
                    "RC-2",
                    "HIGH",
                    f"{rel}:{ln}",
                    f"Layer lookup by name (`{m.group(0).strip()}`) risks Unity built-in layer name collision (e.g. `Water` = 4 vs user layer 9).",
                    "Reference Unity layers via explicit integer index constants.",
                )

    report.add_check(
        "RC-2",
        "No fragile Unity layer lookups by name (NameToLayer / GetMask)",
        "SKIP" if not prod_cs else ("PASS" if rc2_hits == 0 else "FAIL"),
        f"Checked {len(prod_cs)} C# file(s); {rc2_hits} violation(s)",
    )

    # ------------------------------------------------------------------
    # RC-3: Locale-independent numeric parsing
    # ------------------------------------------------------------------
    rc3_hits = 0
    for fp in prod_cs:
        rel = _rel_posix(fp, repo)
        cleaned = strip_cs_comments_and_strings(fp.read_text(encoding="utf-8", errors="replace"))
        for ln, line in enumerate(cleaned.splitlines(), start=1):
            for m in LOCALE_PARSE_RE.finditer(line):
                args_str = m.group(1)
                if "InvariantCulture" not in args_str:
                    rc3_hits += 1
                    report.add_finding(
                        "RC-3",
                        "HIGH",
                        f"{rel}:{ln}",
                        f"Locale-sensitive numeric parse without `CultureInfo.InvariantCulture`: `{m.group(0).strip()}`.",
                        "Pass `NumberStyles.Float, CultureInfo.InvariantCulture` (or `CultureInfo.InvariantCulture`).",
                    )

    report.add_check(
        "RC-3",
        "Locale-independent floating-point/decimal parsing",
        "SKIP" if not prod_cs else ("PASS" if rc3_hits == 0 else "FAIL"),
        f"Checked {len(prod_cs)} C# file(s); {rc3_hits} locale-sensitive parse(s)",
    )

    # ------------------------------------------------------------------
    # RC-4: No silently swallowed exceptions
    # ------------------------------------------------------------------
    rc4_hits = 0
    for fp in prod_cs:
        rel = _rel_posix(fp, repo)
        cleaned = strip_cs_comments_and_strings(fp.read_text(encoding="utf-8", errors="replace"))
        for m in EMPTY_CATCH_RE.finditer(cleaned):
            ln = cleaned[: m.start()].count("\n") + 1
            rc4_hits += 1
            report.add_finding(
                "RC-4",
                "HIGH",
                f"{rel}:{ln}",
                "Empty `catch` block silently swallows exceptions.",
                "Handle the error explicitly, log the specified warning/fallback, or rethrow.",
            )

    report.add_check(
        "RC-4",
        "No silently swallowed exceptions (empty catch blocks)",
        "SKIP" if not prod_cs else ("PASS" if rc4_hits == 0 else "FAIL"),
        f"Checked {len(prod_cs)} C# file(s); {rc4_hits} empty catch block(s)",
    )

    # ------------------------------------------------------------------
    # RC-5: JSON configuration files valid, bounded, and schema-versioned
    # ------------------------------------------------------------------
    data_jsons = [
        f for f in files
        if f.suffix.lower() == ".json" and "streamingassets/data" in _rel_posix(f, repo).lower()
    ]
    rc5_hits = 0
    for fp in data_jsons:
        rel = _rel_posix(fp, repo)
        if fp.stat().st_size > 65536:
            rc5_hits += 1
            report.add_finding("RC-5", "HIGH", rel, f"Config JSON exceeds 64 KiB ({fp.stat().st_size} bytes).", "Reduce JSON config size to <= 65536 bytes.")
            continue
        try:
            data = json.loads(
                fp.read_text(encoding="utf-8"),
                parse_constant=lambda c: (_ for _ in ()).throw(ValueError(f"Non-finite JSON constant: {c}")),
            )
            if not isinstance(data, dict) or not isinstance(data.get("schemaVersion"), int):
                rc5_hits += 1
                report.add_finding("RC-5", "HIGH", rel, "Config JSON missing integer `schemaVersion`.", "Add integer `schemaVersion` key.")
            else:
                # Verify all numeric fields are finite
                def _check_finite(obj: object, prefix: str = "") -> None:
                    nonlocal rc5_hits
                    if isinstance(obj, float) and not math.isfinite(obj):
                        rc5_hits += 1
                        report.add_finding("RC-5", "HIGH", rel, f"Non-finite number at `{prefix}`.", "Use finite numeric values.")
                    elif isinstance(obj, dict):
                        for k, v in obj.items():
                            _check_finite(v, f"{prefix}.{k}" if prefix else str(k))
                    elif isinstance(obj, list):
                        for idx, v in enumerate(obj):
                            _check_finite(v, f"{prefix}[{idx}]")
                _check_finite(data)
        except (json.JSONDecodeError, ValueError) as exc:
            rc5_hits += 1
            report.add_finding("RC-5", "HIGH", rel, f"Invalid JSON config: {exc}.", "Fix malformed JSON syntax/values.")

    report.add_check(
        "RC-5",
        "StreamingAssets/Data JSON configs valid, finite, <= 64 KiB, with schemaVersion",
        "SKIP" if not data_jsons else ("PASS" if rc5_hits == 0 else "FAIL"),
        f"Checked {len(data_jsons)} JSON config(s); {rc5_hits} issue(s)",
    )

    # ------------------------------------------------------------------
    # RC-6: MonoBehaviour event subscription symmetry
    # ------------------------------------------------------------------
    rc6_med = 0
    for fp in view_cs:
        rel = _rel_posix(fp, repo)
        raw = fp.read_text(encoding="utf-8", errors="replace")
        if not re.search(r":\s*MonoBehaviour\b", raw):
            continue
        methods = {m.name: m for m in extract_cs_methods(raw)}
        sub_events: Set[str] = set()
        for mname in ("OnEnable", "Awake", "Start"):
            if mname in methods:
                for em in re.finditer(r"([\w.]+)\s*\+=\s*", methods[mname].body):
                    sub_events.add(em.group(1))
        unsub_events: Set[str] = set()
        for mname in ("OnDisable", "OnDestroy"):
            if mname in methods:
                for em in re.finditer(r"([\w.]+)\s*-=\s*", methods[mname].body):
                    unsub_events.add(em.group(1))
        leaked = sorted(sub_events - unsub_events)
        if leaked:
            rc6_med += 1
            report.add_finding(
                "RC-6",
                "MEDIUM",
                rel,
                f"MonoBehaviour subscribes to `{', '.join(leaked)}` without matching `-=` in `OnDisable`/`OnDestroy`.",
                "Unsubscribe all event handlers in `OnDisable` or `OnDestroy` to prevent lifecycle leaks.",
            )

    report.add_check(
        "RC-6",
        "MonoBehaviour event subscriptions released on teardown",
        "SKIP" if not view_cs else "PASS",
        f"Checked {len(view_cs)} View file(s); potential leaks={rc6_med}",
    )

    # ------------------------------------------------------------------
    # RC-7: No disabled or weakened tests
    # ------------------------------------------------------------------
    rc7_hits = 0
    for fp in test_cs:
        rel = _rel_posix(fp, repo)
        cleaned = strip_cs_comments_and_strings(fp.read_text(encoding="utf-8", errors="replace"))
        for ln, line in enumerate(cleaned.splitlines(), start=1):
            m = DISABLED_TEST_RE.search(line)
            if m:
                rc7_hits += 1
                report.add_finding(
                    "RC-7",
                    "HIGH",
                    f"{rel}:{ln}",
                    f"Disabled or weakened test construct detected (`{m.group(0).strip()}`).",
                    "Do not ignore, disable, or weaken required tests.",
                )

    report.add_check(
        "RC-7",
        "No ignored, explicit-only, or weakened tests",
        "SKIP" if not test_cs else ("PASS" if rc7_hits == 0 else "FAIL"),
        f"Checked {len(test_cs)} test file(s); {rc7_hits} issue(s)",
    )

    # ------------------------------------------------------------------
    # RC-8: Test-plan check ID traceability in test files
    # ------------------------------------------------------------------
    all_test_files = iter_repo_files(
        repo,
        paths=[p for p in ("tests", "Assets/Tests") if (repo / p).exists()],
        exts={".cs"},
    )
    if spec_path and spec_path.is_file() and all_test_files:
        expected_ids = _extract_automated_check_ids(spec_path)
        corpus = "\n".join(f.read_text(encoding="utf-8", errors="replace") for f in all_test_files)
        missing_ids = sorted(cid for cid in expected_ids if cid not in corpus)
        if missing_ids:
            report.add_finding(
                "RC-8",
                "MEDIUM",
                spec_path.name,
                f"Test-plan check ID(s) not referenced in test files: {', '.join(missing_ids)}.",
                "Reference each test-plan check ID (e.g. `T-CORE-01`) in its corresponding test name or comment.",
            )
            report.add_check(
                "RC-8",
                "Test-plan check IDs traceable in test source",
                "PASS",
                f"{len(expected_ids) - len(missing_ids)}/{len(expected_ids)} referenced (missing reported as Medium)",
            )
        else:
            report.add_check(
                "RC-8",
                "Test-plan check IDs traceable in test source",
                "PASS",
                f"All {len(expected_ids)} automated check ID(s) referenced",
            )
    else:
        reason = "No --spec provided" if not spec_path else "No test files found in tests/ or Assets/Tests/"
        report.add_check("RC-8", "Test-plan check IDs traceable in test source", "SKIP", reason)

    # ------------------------------------------------------------------
    # RC-9: Optional focused test execution
    # ------------------------------------------------------------------
    if not run_tests:
        report.add_check("RC-9", "Focused test execution", "SKIP", "Not requested (pass --run-tests to execute)")
    else:
        verify_script = repo / "tools" / "verify-all.sh"
        core_tests = repo / "tests" / "Core"
        if suite and verify_script.is_file():
            cmd = ["bash", str(verify_script), "--suite", suite]
            if test_filter:
                cmd.extend(["--filter", test_filter])
        elif core_tests.is_dir():
            cmd = ["dotnet", "test", str(core_tests), "-c", "Release"]
            if test_filter:
                cmd.extend(["--filter", test_filter])
        else:
            report.add_blocked("No test runner (`tools/verify-all.sh` or `tests/Core/`) found for `--run-tests`.")
            report.add_check("RC-9", "Focused test execution", "BLOCKED", "Test runner not found")
            report.apply_waivers(waivers)
            return report

        code, out, err = run_cmd(cmd, cwd=repo)
        if code == 127:
            report.add_blocked(f"Test runner binary unavailable: {err.strip()}")
            report.add_check("RC-9", "Focused test execution", "BLOCKED", err.strip())
        elif code != 0:
            snippet = (out + "\n" + err).strip().splitlines()[-5:]
            report.add_finding(
                "RC-9",
                "HIGH",
                " ".join(cmd),
                f"Focused test command failed with exit code {code}: {' | '.join(snippet)}",
                "Fix the failing test(s) and re-run focused verification.",
            )
            report.add_check("RC-9", "Focused test execution", "FAIL", f"Exit code {code}: `{' '.join(cmd)}`")
        else:
            report.add_check("RC-9", "Focused test execution", "PASS", f"Exit code 0: `{' '.join(cmd)}`")

    report.apply_waivers(waivers)
    return report


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Deterministic Code Correctness & Maintainability Gate")
    parser.add_argument("--repo", default=Path("."), type=Path, help="Repository root path")
    parser.add_argument("--base", default=None, help="Base git revision for diff scan")
    parser.add_argument("--head", default=None, help="Head git revision for diff scan")
    parser.add_argument("--paths", nargs="*", default=None, help="Explicit file/directory paths to scan")
    parser.add_argument("--spec", default=None, type=Path, help="Optional technical spec for test-plan check traceability")
    parser.add_argument("--decisions", default=None, type=Path, help="Optional path to docs/decisions.md")
    parser.add_argument("--run-tests", action="store_true", help="Execute focused tests")
    parser.add_argument("--suite", default=None, help="Focused suite name for tools/verify-all.sh")
    parser.add_argument("--filter", dest="test_filter", default=None, help="Test filter expression")
    parser.add_argument("--format", choices=("markdown", "json"), default="markdown")
    args = parser.parse_args(argv)

    report = run_correctness_maintainability_gate(
        repo=args.repo.resolve(),
        base=args.base,
        head=args.head,
        paths=args.paths,
        spec_path=args.spec.resolve() if args.spec else None,
        decisions_path=args.decisions,
        run_tests=args.run_tests,
        suite=args.suite,
        test_filter=args.test_filter,
    )
    if args.format == "json":
        print(json.dumps(report.to_dict(), indent=2))
    else:
        print(report.to_markdown())
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
