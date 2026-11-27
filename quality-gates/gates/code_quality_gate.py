"""Deterministic Code Quality & Design Gate (CQ-1 .. CQ-9)."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

from .common import (
    GateReport,
    extract_cs_methods,
    git_changed_files,
    git_rev_parse,
    is_git_repo,
    iter_repo_files,
    load_waivers,
    run_cmd,
    strip_cs_comments_and_strings,
)

CODE_EXTS = {".cs", ".py", ".js", ".ts"}

CORE_FORBIDDEN_USING_RE = re.compile(
    r"^\s*using\s+(?:UnityEngine|UnityEditor|TMPro|Murim\.View|Murim\.Editor)\b",
    re.MULTILINE,
)
VIEW_EDITOR_USING_RE = re.compile(
    r"^\s*using\s+(?:UnityEditor|Murim\.Editor)\b",
    re.MULTILINE,
)
PER_FRAME_METHODS = {"Update", "LateUpdate", "FixedUpdate"}
LINQ_CALL_RE = re.compile(
    r"\.\s*(?:Select|Where|ToList|ToArray|ToDictionary|OrderBy|OrderByDescending|GroupBy|Any|All|First|FirstOrDefault|Last|LastOrDefault|Distinct|Count)\s*\("
)
HOT_ALLOC_RE = re.compile(
    r"\bnew\s+(?:List|Dictionary|HashSet|Queue|Stack|StringBuilder)\s*<|\bnew\s+[A-Za-z_]\w*\s*\[|\b(?:FindObjectOfType|FindObjectsOfType|GameObject\.Find|Camera\.main)\b"
)
CORE_DEBUG_RE = re.compile(r"\b(?:Debug\.Log(?:Warning|Error)?|Console\.Write(?:Line)?)\s*\(")
TODO_MARKER_RE = re.compile(r"(?://|#)\s*(?:TODO|FIXME|HACK|XXX)\b")


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


def _collect_code_files(
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
        return [repo / r for r in changed if (repo / r).is_file() and (repo / r).suffix.lower() in CODE_EXTS]

    if paths:
        return iter_repo_files(repo, paths=paths, exts=CODE_EXTS)

    default_roots = [p for p in ("Assets/Scripts", "Assets/Editor", "tests", "Assets/Tests") if (repo / p).exists()]
    if not default_roots:
        return []
    return iter_repo_files(repo, paths=default_roots, exts=CODE_EXTS)


def _check_unguarded_editor_using(source: str) -> List[int]:
    """Return 1-based line numbers of `using UnityEditor` / `Murim.Editor` outside `#if UNITY_EDITOR`."""
    hits: List[int] = []
    editor_depth = 0
    if_stack: List[bool] = []
    for ln, line in enumerate(source.splitlines(), start=1):
        stripped = line.strip()
        if stripped.startswith("#if"):
            is_ed = "UNITY_EDITOR" in stripped
            if_stack.append(is_ed)
            if is_ed:
                editor_depth += 1
        elif stripped.startswith("#endif"):
            if if_stack:
                if if_stack.pop():
                    editor_depth -= 1
        elif editor_depth == 0 and VIEW_EDITOR_USING_RE.match(line):
            hits.append(ln)
    return hits


def _detect_duplicates(
    files: Sequence[Path],
    repo: Path,
    window: int,
) -> List[Tuple[str, List[Tuple[str, int]]]]:
    """Detect exact normalized blocks of `window` non-trivial code lines across files."""
    buckets: Dict[str, List[Tuple[str, int]]] = {}
    for fp in files:
        rel = _rel_posix(fp, repo)
        raw = fp.read_text(encoding="utf-8", errors="replace")
        cleaned = strip_cs_comments_and_strings(raw) if fp.suffix.lower() == ".cs" else raw
        norm_lines: List[Tuple[int, str]] = []
        for ln, line in enumerate(cleaned.splitlines(), start=1):
            s = re.sub(r"\s+", " ", line).strip()
            # Skip trivial brace-only, import-only, or empty lines
            if not s or s in {"{", "}", "};", "else", "else {", "try {", "finally {"}:
                continue
            if s.startswith(("using ", "namespace ", "import ", "from ")):
                continue
            norm_lines.append((ln, s))

        if len(norm_lines) < window:
            continue
        for i in range(0, len(norm_lines) - window + 1):
            chunk = "\n".join(x[1] for x in norm_lines[i : i + window])
            if len(chunk) < 120:
                continue
            digest = hashlib.sha256(chunk.encode("utf-8")).hexdigest()[:16]
            start_ln = norm_lines[i][0]
            buckets.setdefault(digest, []).append((rel, start_ln))

    # Deduplicate overlapping consecutive windows for the same file
    groups: List[Tuple[str, List[Tuple[str, int]]]] = []
    seen_pairs: set[Tuple[Tuple[str, int], ...]] = set()
    for digest, locs in buckets.items():
        # Filter occurrences that are too close in the same file
        distinct: List[Tuple[str, int]] = []
        for rel, ln in locs:
            if not any(r == rel and abs(l - ln) < window for r, l in distinct):
                distinct.append((rel, ln))
        if len(distinct) >= 2:
            key = tuple(distinct)
            if key not in seen_pairs:
                seen_pairs.add(key)
                groups.append((digest, distinct))
    return groups


def run_code_quality_gate(
    repo: Path,
    base: Optional[str] = None,
    head: Optional[str] = None,
    paths: Optional[Sequence[str]] = None,
    decisions_path: Optional[Path] = None,
    max_fn_lines: int = 60,
    max_nesting: int = 4,
    max_params: int = 5,
    max_complexity: int = 15,
    dup_lines: int = 10,
    run_tools: bool = False,
) -> GateReport:
    target_desc = f"{base}...{head}" if base and head else (", ".join(paths) if paths else str(repo))
    report = GateReport(
        gate="Code Quality & Design",
        target=target_desc,
        _finding_prefix="CQ",
    )

    if decisions_path is None:
        candidate = repo / "docs" / "decisions.md"
        if candidate.is_file():
            decisions_path = candidate
    waivers = load_waivers(decisions_path)

    files = _collect_code_files(repo, base, head, paths, report)
    if report.blocked_reasons:
        report.add_check("CQ-1", "Layer and assembly dependency direction", "BLOCKED", "; ".join(report.blocked_reasons))
        return report

    cs_files = [f for f in files if f.suffix.lower() == ".cs"]

    # ------------------------------------------------------------------
    # CQ-1: Layer & assembly dependency rules
    # ------------------------------------------------------------------
    cq1_hits = 0
    for fp in cs_files:
        rel = _rel_posix(fp, repo)
        raw = fp.read_text(encoding="utf-8", errors="replace")
        cleaned = strip_cs_comments_and_strings(raw)
        if _is_core_file(rel):
            for m in CORE_FORBIDDEN_USING_RE.finditer(cleaned):
                ln = cleaned[: m.start()].count("\n") + 1
                cq1_hits += 1
                report.add_finding(
                    "CQ-1",
                    "HIGH",
                    f"{rel}:{ln}",
                    f"Core file violates pure-C# layer boundary: `{m.group(0).strip()}`.",
                    "Remove Unity/View/Editor references from `Assets/Scripts/Core/`.",
                )
        elif _is_view_file(rel):
            for ln in _check_unguarded_editor_using(cleaned):
                cq1_hits += 1
                report.add_finding(
                    "CQ-1",
                    "HIGH",
                    f"{rel}:{ln}",
                    "View file imports UnityEditor/Murim.Editor outside `#if UNITY_EDITOR`.",
                    "Move editor-only code to `Assets/Editor/` or wrap in `#if UNITY_EDITOR`.",
                )

    # Check Core asmdef if present
    core_dir = repo / "Assets" / "Scripts" / "Core"
    if core_dir.is_dir():
        for asmdef in sorted(core_dir.glob("*.asmdef")):
            rel = _rel_posix(asmdef, repo)
            try:
                data = json.loads(asmdef.read_text(encoding="utf-8"))
                if data.get("noEngineReferences") is not True:
                    cq1_hits += 1
                    report.add_finding(
                        "CQ-1",
                        "HIGH",
                        rel,
                        "`Murim.Core` assembly definition does not set `\"noEngineReferences\": true`.",
                        "Set `\"noEngineReferences\": true` in the Core `.asmdef`.",
                    )
                for ref in data.get("references", []):
                    if "View" in str(ref) or "Editor" in str(ref):
                        cq1_hits += 1
                        report.add_finding(
                            "CQ-1",
                            "HIGH",
                            rel,
                            f"`Murim.Core` `.asmdef` references higher layer `{ref}`.",
                            "Remove View/Editor assembly references from Core.",
                        )
            except json.JSONDecodeError as exc:
                cq1_hits += 1
                report.add_finding("CQ-1", "HIGH", rel, f"Malformed `.asmdef` JSON: {exc}.", "Fix JSON syntax.")

    report.add_check(
        "CQ-1",
        "Layer and assembly dependency direction preserved",
        "SKIP" if not cs_files and not core_dir.is_dir() else ("PASS" if cq1_hits == 0 else "FAIL"),
        f"Checked {len(cs_files)} C# file(s); {cq1_hits} violation(s)",
    )

    # ------------------------------------------------------------------
    # CQ-2: No per-frame allocations or LINQ in Unity Update paths
    # ------------------------------------------------------------------
    cq2_hits = 0
    view_cs = [f for f in cs_files if _is_view_file(f.relative_to(repo).as_posix() if f.is_relative_to(repo) else f.as_posix())]
    for fp in view_cs:
        rel = _rel_posix(fp, repo)
        raw = fp.read_text(encoding="utf-8", errors="replace")
        has_linq_using = bool(re.search(r"^\s*using\s+System\.Linq\s*;", raw, re.MULTILINE))
        for method in extract_cs_methods(raw):
            if method.name not in PER_FRAME_METHODS:
                continue
            if has_linq_using and LINQ_CALL_RE.search(method.body):
                cq2_hits += 1
                report.add_finding(
                    "CQ-2",
                    "HIGH",
                    f"{rel}:{method.start_line}",
                    f"Per-frame method `{method.name}()` uses LINQ allocation(s).",
                    "Replace LINQ queries in Update paths with allocation-free loops.",
                )
            hm = HOT_ALLOC_RE.search(method.body)
            if hm:
                cq2_hits += 1
                report.add_finding(
                    "CQ-2",
                    "HIGH",
                    f"{rel}:{method.start_line}",
                    f"Per-frame method `{method.name}()` performs heap allocation or scene lookup (`{hm.group(0).strip()}`).",
                    "Pre-allocate reusable collections and cache component/camera references in `Awake`/`Start`.",
                )

    report.add_check(
        "CQ-2",
        "No per-frame heap allocations, LINQ, or scene lookups in Update loops",
        "SKIP" if not view_cs else ("PASS" if cq2_hits == 0 else "FAIL"),
        f"Checked {len(view_cs)} View C# file(s); {cq2_hits} issue(s)",
    )

    # ------------------------------------------------------------------
    # CQ-3, CQ-4, CQ-5, CQ-6: Method length, nesting, params, cyclomatic complexity
    # ------------------------------------------------------------------
    cq3_high = cq3_med = 0
    cq4_high = cq4_med = 0
    cq5_med = 0
    cq6_high = cq6_med = 0
    methods_checked = 0

    for fp in cs_files:
        rel = _rel_posix(fp, repo)
        if _is_test_file(rel):
            continue
        raw = fp.read_text(encoding="utf-8", errors="replace")
        for m in extract_cs_methods(raw):
            methods_checked += 1
            length = m.end_line - m.start_line + 1
            if length > max_fn_lines:
                sev = "HIGH" if length > max_fn_lines * 2 else "MEDIUM"
                if sev == "HIGH":
                    cq3_high += 1
                else:
                    cq3_med += 1
                report.add_finding(
                    "CQ-3",
                    sev,
                    f"{rel}:{m.start_line}",
                    f"Method `{m.name}` is {length} lines long (threshold: {max_fn_lines}).",
                    "Extract cohesive helper methods to keep each unit focused.",
                )
            if m.max_nesting > max_nesting:
                sev = "HIGH" if m.max_nesting > max_nesting + 2 else "MEDIUM"
                if sev == "HIGH":
                    cq4_high += 1
                else:
                    cq4_med += 1
                report.add_finding(
                    "CQ-4",
                    sev,
                    f"{rel}:{m.start_line}",
                    f"Method `{m.name}` has nesting depth {m.max_nesting} (threshold: {max_nesting}).",
                    "Use early returns or extract nested branches into helper methods.",
                )
            if m.param_count > max_params:
                cq5_med += 1
                report.add_finding(
                    "CQ-5",
                    "MEDIUM",
                    f"{rel}:{m.start_line}",
                    f"Method `{m.name}` takes {m.param_count} parameters (threshold: {max_params}).",
                    "Group related parameters into an existing or focused readonly struct/record.",
                )
            if m.complexity > max_complexity:
                sev = "HIGH" if m.complexity > max_complexity + 10 else "MEDIUM"
                if sev == "HIGH":
                    cq6_high += 1
                else:
                    cq6_med += 1
                report.add_finding(
                    "CQ-6",
                    sev,
                    f"{rel}:{m.start_line}",
                    f"Method `{m.name}` has cyclomatic complexity {m.complexity} (threshold: {max_complexity}).",
                    "Simplify control flow via early returns, lookup tables, or helper extraction.",
                )

    report.add_check(
        "CQ-3",
        f"Method length within threshold (<= {max_fn_lines} lines)",
        "SKIP" if methods_checked == 0 else ("PASS" if cq3_high == 0 else "FAIL"),
        f"Checked {methods_checked} method(s); high={cq3_high}, medium={cq3_med}",
    )
    report.add_check(
        "CQ-4",
        f"Nesting depth within threshold (<= {max_nesting})",
        "SKIP" if methods_checked == 0 else ("PASS" if cq4_high == 0 else "FAIL"),
        f"Checked {methods_checked} method(s); high={cq4_high}, medium={cq4_med}",
    )
    report.add_check(
        "CQ-5",
        f"Method parameter count within threshold (<= {max_params})",
        "SKIP" if methods_checked == 0 else "PASS",
        f"Checked {methods_checked} method(s); medium={cq5_med}",
    )
    report.add_check(
        "CQ-6",
        f"Cyclomatic complexity within threshold (<= {max_complexity})",
        "SKIP" if methods_checked == 0 else ("PASS" if cq6_high == 0 else "FAIL"),
        f"Checked {methods_checked} method(s); high={cq6_high}, medium={cq6_med}",
    )

    # ------------------------------------------------------------------
    # CQ-7: Normalized code duplication detection
    # ------------------------------------------------------------------
    non_test_code = [f for f in files if not _is_test_file(_rel_posix(f, repo))]
    dup_groups = _detect_duplicates(non_test_code, repo, window=dup_lines)
    cq7_high = 0
    cq7_med = 0
    for digest, locs in dup_groups:
        sev = "HIGH" if len(locs) >= 3 else "MEDIUM"
        if sev == "HIGH":
            cq7_high += 1
        else:
            cq7_med += 1
        loc_str = ", ".join(f"{r}:{ln}" for r, ln in locs[:5])
        report.add_finding(
            "CQ-7",
            sev,
            f"{locs[0][0]}:{locs[0][1]}",
            f"Duplicated block (>={dup_lines} non-trivial lines, hash `{digest}`) appears in {len(locs)} places: {loc_str}.",
            "Extract the repeated logic into a single shared helper.",
        )

    report.add_check(
        "CQ-7",
        f"No duplicated non-trivial blocks (>={dup_lines} lines in 3+ places)",
        "SKIP" if not non_test_code else ("PASS" if cq7_high == 0 else "FAIL"),
        f"Checked {len(non_test_code)} file(s); high={cq7_high} (3+ copies), medium={cq7_med} (2 copies)",
    )

    # ------------------------------------------------------------------
    # CQ-8: Debug leftovers & markers
    # ------------------------------------------------------------------
    cq8_high = 0
    cq8_low = 0
    for fp in files:
        rel = _rel_posix(fp, repo)
        raw = fp.read_text(encoding="utf-8", errors="replace")
        if fp.suffix.lower() == ".cs" and _is_core_file(rel):
            cleaned = strip_cs_comments_and_strings(raw)
            for ln, line in enumerate(cleaned.splitlines(), start=1):
                if CORE_DEBUG_RE.search(line):
                    cq8_high += 1
                    report.add_finding(
                        "CQ-8",
                        "HIGH",
                        f"{rel}:{ln}",
                        "Debug/console output call in pure Core code.",
                        "Remove `Debug.Log`/`Console.WriteLine` from Core; return structured warnings/results instead.",
                    )
        for ln, line in enumerate(raw.splitlines(), start=1):
            if TODO_MARKER_RE.search(line):
                cq8_low += 1
                report.add_finding(
                    "CQ-8",
                    "LOW",
                    f"{rel}:{ln}",
                    "Leftover TODO/FIXME/HACK marker in code.",
                    "Resolve the marker or remove stale comments before shipping.",
                )

    report.add_check(
        "CQ-8",
        "No debug console leftovers in Core or stale markers",
        "SKIP" if not files else ("PASS" if cq8_high == 0 else "FAIL"),
        f"Core debug calls={cq8_high}, TODO/FIXME markers={cq8_low}",
    )

    # ------------------------------------------------------------------
    # CQ-9: Compiler warnings-as-errors & optional dotnet format check
    # ------------------------------------------------------------------
    core_csproj = repo / "tools" / "dotnet" / "Murim.Core.csproj"
    cq9_fail = False
    cq9_evidence: List[str] = []
    if core_csproj.is_file():
        csproj_txt = core_csproj.read_text(encoding="utf-8", errors="replace")
        if "<TreatWarningsAsErrors>true</TreatWarningsAsErrors>" not in csproj_txt:
            cq9_fail = True
            report.add_finding(
                "CQ-9",
                "HIGH",
                _rel_posix(core_csproj, repo),
                "`Murim.Core.csproj` does not enable `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.",
                "Enable `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `tools/dotnet/Murim.Core.csproj`.",
            )
        else:
            cq9_evidence.append("Core warnings-as-errors enabled")

    if run_tools and core_csproj.is_file():
        code, out, err = run_cmd(["dotnet", "format", str(core_csproj), "--verify-no-changes", "--no-restore"], cwd=repo)
        if code == 127:
            report.add_blocked("`dotnet` CLI not found while `--run-tools` was requested.")
            report.add_check("CQ-9", "Static checks & formatter verification", "BLOCKED", err.strip())
            report.apply_waivers(waivers)
            return report
        if code != 0:
            cq9_fail = True
            report.add_finding(
                "CQ-9",
                "HIGH",
                _rel_posix(core_csproj, repo),
                f"`dotnet format --verify-no-changes` failed (exit {code}).",
                "Run `dotnet format` to match `.editorconfig` style rules.",
            )
        else:
            cq9_evidence.append("dotnet format passed")

    if not core_csproj.is_file():
        report.add_check("CQ-9", "Compiler warnings-as-errors and configured static checks", "SKIP", "No Core .csproj present")
    else:
        report.add_check(
            "CQ-9",
            "Compiler warnings-as-errors and configured static checks",
            "PASS" if not cq9_fail else "FAIL",
            "; ".join(cq9_evidence) if cq9_evidence else "Failed",
        )

    report.apply_waivers(waivers)
    return report


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Deterministic Code Quality & Design Gate")
    parser.add_argument("--repo", default=Path("."), type=Path, help="Repository root path")
    parser.add_argument("--base", default=None, help="Base git revision for diff scan")
    parser.add_argument("--head", default=None, help="Head git revision for diff scan")
    parser.add_argument("--paths", nargs="*", default=None, help="Explicit file/directory paths to scan")
    parser.add_argument("--decisions", default=None, type=Path, help="Optional path to docs/decisions.md")
    parser.add_argument("--max-fn-lines", type=int, default=60)
    parser.add_argument("--max-nesting", type=int, default=4)
    parser.add_argument("--max-params", type=int, default=5)
    parser.add_argument("--max-complexity", type=int, default=15)
    parser.add_argument("--dup-lines", type=int, default=10)
    parser.add_argument("--run-tools", action="store_true", help="Run dotnet format --verify-no-changes if available")
    parser.add_argument("--format", choices=("markdown", "json"), default="markdown")
    args = parser.parse_args(argv)

    report = run_code_quality_gate(
        repo=args.repo.resolve(),
        base=args.base,
        head=args.head,
        paths=args.paths,
        decisions_path=args.decisions,
        max_fn_lines=args.max_fn_lines,
        max_nesting=args.max_nesting,
        max_params=args.max_params,
        max_complexity=args.max_complexity,
        dup_lines=args.dup_lines,
        run_tools=args.run_tools,
    )
    if args.format == "json":
        print(json.dumps(report.to_dict(), indent=2))
    else:
        print(report.to_markdown())
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
