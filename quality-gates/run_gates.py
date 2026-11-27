#!/usr/bin/env python3
"""Unified CLI runner for Murim Universe deterministic quality gates.

Exit codes:
  0 = PASS (all requested gates passed)
  1 = FAIL (one or more gates reported blocking Critical/High findings)
  2 = BLOCKED (missing required inputs/tools, invalid CLI usage, or blocked gate)
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import List, Optional

# Ensure local package import works regardless of current working directory
SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from gates.art_inventory_gate import run_art_inventory_gate
from gates.code_quality_gate import run_code_quality_gate
from gates.common import EXIT_BLOCKED, EXIT_FAIL, EXIT_PASS, GateReport
from gates.correctness_maintainability_gate import run_correctness_maintainability_gate
from gates.product_spec_gate import run_product_spec_gate
from gates.requirements_architecture_gate import run_requirements_architecture_gate
from gates.security_privacy_gate import run_security_privacy_gate

AVAILABLE_GATES = {
    "product-spec": "Validate a product spec's structure, IDs, testability, links, and product-level boundaries.",
    "art-inventory": "Validate a product spec's ART-XXX inventory, exact counts, dispositions, options, and sourcing.",
    "requirements-architecture": "Validate a technical spec's baseline SHA, AC traceability, ticket DAG, execution plan, and art contracts.",
    "security-privacy": "Scan specs, configs, scripts, and code for secrets, PII/local paths, unsafe sinks, debug gating, and unpinned deps.",
    "code-quality": "Check layer boundaries, per-frame allocations/LINQ, complexity/size thresholds, duplication, and warnings-as-errors.",
    "correctness-maintainability": "Check Core determinism, layer-index safety, invariant parsing, JSON schemas, test hygiene, and optional focused tests.",
}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="run_gates.py",
        description="Run one or more deterministic quality gates for Murim Universe.",
    )
    parser.add_argument(
        "gates",
        nargs="*",
        help=f"Gate name(s) to run: {', '.join(AVAILABLE_GATES.keys())}",
    )
    parser.add_argument("--list", action="store_true", help="List available deterministic quality gates and exit")
    parser.add_argument("--repo", default=Path("."), type=Path, help="Repository root path (default: current directory)")
    parser.add_argument("--spec", default=None, type=Path, help="Path to product or technical spec markdown file")
    parser.add_argument("--product-spec", default=None, type=Path, help="Path to approved product spec (for requirements-architecture)")
    parser.add_argument("--decisions", default=None, type=Path, help="Optional path to docs/decisions.md (for approvals & waivers)")
    parser.add_argument("--base", default=None, help="Base git revision for diff-scoped code/security gates")
    parser.add_argument("--head", default=None, help="Head git revision for diff-scoped code/security gates")
    parser.add_argument("--paths", nargs="*", default=None, help="Explicit file or directory paths to scan")
    parser.add_argument("--no-git", action="store_true", help="Skip git baseline commit lookup in requirements-architecture")
    parser.add_argument("--run-tools", action="store_true", help="Run external formatter/linter tools in code-quality")
    parser.add_argument("--run-tests", action="store_true", help="Run focused test runner in correctness-maintainability")
    parser.add_argument("--suite", default=None, help="Focused suite name when using --run-tests with tools/verify-all.sh")
    parser.add_argument("--filter", dest="test_filter", default=None, help="Test filter expression when using --run-tests")
    parser.add_argument("--max-fn-lines", type=int, default=60, help="Code quality method line threshold (default: 60)")
    parser.add_argument("--max-nesting", type=int, default=4, help="Code quality nesting depth threshold (default: 4)")
    parser.add_argument("--max-params", type=int, default=5, help="Code quality parameter count threshold (default: 5)")
    parser.add_argument("--max-complexity", type=int, default=15, help="Code quality cyclomatic complexity threshold (default: 15)")
    parser.add_argument("--dup-lines", type=int, default=10, help="Code quality duplicate block window in lines (default: 10)")
    parser.add_argument("--format", choices=("markdown", "json"), default="markdown", help="Output format (default: markdown)")
    return parser


def _resolve_opt(path: Optional[Path]) -> Optional[Path]:
    return path.resolve() if path is not None else None


def main(argv: Optional[List[str]] = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.list:
        for name, desc in AVAILABLE_GATES.items():
            print(f"{name:28s}  {desc}")
        return EXIT_PASS

    if not args.gates:
        parser.print_help(sys.stderr)
        return EXIT_BLOCKED

    unknown = [g for g in args.gates if g not in AVAILABLE_GATES]
    if unknown:
        print(f"Error: unknown gate(s): {', '.join(unknown)}", file=sys.stderr)
        print(f"Available gates: {', '.join(AVAILABLE_GATES.keys())}", file=sys.stderr)
        return EXIT_BLOCKED

    repo = args.repo.resolve()
    spec = _resolve_opt(args.spec)
    product_spec = _resolve_opt(args.product_spec)
    decisions = _resolve_opt(args.decisions)

    reports: List[GateReport] = []
    for gate_name in args.gates:
        if gate_name == "product-spec":
            if spec is None:
                r = GateReport(gate="Product Spec", target="(none)", _finding_prefix="PS")
                r.add_blocked("`--spec <path>` is required for `product-spec`.")
                reports.append(r)
            else:
                reports.append(run_product_spec_gate(spec, repo, decisions))

        elif gate_name == "art-inventory":
            if spec is None:
                r = GateReport(gate="Art Inventory", target="(none)", _finding_prefix="AI")
                r.add_blocked("`--spec <path>` is required for `art-inventory`.")
                reports.append(r)
            else:
                reports.append(run_art_inventory_gate(spec, repo, decisions))

        elif gate_name == "requirements-architecture":
            if spec is None:
                r = GateReport(gate="Requirements & Architecture", target="(none)", _finding_prefix="RA")
                r.add_blocked("`--spec <tech-spec-path>` is required for `requirements-architecture`.")
                reports.append(r)
            else:
                reports.append(
                    run_requirements_architecture_gate(
                        tech_spec_path=spec,
                        product_spec_path=product_spec,
                        repo=repo,
                        decisions_path=decisions,
                        check_git=not args.no_git,
                    )
                )

        elif gate_name == "security-privacy":
            reports.append(
                run_security_privacy_gate(
                    repo=repo,
                    spec_path=spec,
                    base=args.base,
                    head=args.head,
                    paths=args.paths,
                    decisions_path=decisions,
                )
            )

        elif gate_name == "code-quality":
            reports.append(
                run_code_quality_gate(
                    repo=repo,
                    base=args.base,
                    head=args.head,
                    paths=args.paths,
                    decisions_path=decisions,
                    max_fn_lines=args.max_fn_lines,
                    max_nesting=args.max_nesting,
                    max_params=args.max_params,
                    max_complexity=args.max_complexity,
                    dup_lines=args.dup_lines,
                    run_tools=args.run_tools,
                )
            )

        elif gate_name == "correctness-maintainability":
            reports.append(
                run_correctness_maintainability_gate(
                    repo=repo,
                    base=args.base,
                    head=args.head,
                    paths=args.paths,
                    spec_path=spec,
                    decisions_path=decisions,
                    run_tests=args.run_tests,
                    suite=args.suite,
                    test_filter=args.test_filter,
                )
            )

    # Aggregate decision: FAIL overrides BLOCKED; BLOCKED overrides PASS
    if any(r.decision == "FAIL" for r in reports):
        overall_code = EXIT_FAIL
        overall_decision = "FAIL"
    elif any(r.decision == "BLOCKED" for r in reports):
        overall_code = EXIT_BLOCKED
        overall_decision = "BLOCKED"
    else:
        overall_code = EXIT_PASS
        overall_decision = "PASS"

    if args.format == "json":
        payload = {
            "overall_decision": overall_decision,
            "exit_code": overall_code,
            "gates": [r.to_dict() for r in reports],
        }
        print(json.dumps(payload, indent=2))
    else:
        print("\n\n---\n\n".join(r.to_markdown() for r in reports))

    return overall_code


if __name__ == "__main__":
    sys.exit(main())
