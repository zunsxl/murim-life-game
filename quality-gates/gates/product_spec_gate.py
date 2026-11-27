"""Deterministic Product Spec Gate (PS-1 .. PS-8)."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple

from .common import (
    GateReport,
    extract_markdown_links,
    find_section,
    load_waivers,
    parse_sections,
    parse_tables,
    strip_fenced_code_blocks,
)

STATUS_RE = re.compile(r"^\s*\*{0,2}Status\s*:?\*{0,2}\s*:?\s*(.+?)\s*$", re.IGNORECASE | re.MULTILINE)
GOAL_RE = re.compile(r"(\*{2}(?:Release\s+)?Goal\.?\*{2}|^#{1,3}\s+(?:Release\s+)?Goal\b)", re.IGNORECASE | re.MULTILINE)

PLACEHOLDER_PATTERNS = [
    (re.compile(r"\b(?:TBD|TODO|FIXME|TBA|WIP)\b"), "unresolved placeholder token"),
    (re.compile(r"\?\?\?"), "unresolved question marks"),
    (re.compile(r"\[(?:insert|fill\s+in|todo|tbd)[^\]]*\]", re.IGNORECASE), "placeholder bracket text"),
]

# Vague terms in ACs/controls/edge cases that are untestable without a measurable condition
HARD_VAGUE_RE = re.compile(r"\b(?:etc\.?|as\s+needed)\b", re.IGNORECASE)
SOFT_VAGUE_RE = re.compile(
    r"\b(?:intuitive|feels\s+good|appropriate|reasonable|properly|fast|quick|smoothly|various)\b",
    re.IGNORECASE,
)

# Implementation-detail leakage forbidden in product specs
IMPL_LEAK_PATTERNS = [
    (re.compile(r"\bAssets/Scripts/[^\s)`]+"), "source code path"),
    (re.compile(r"\b[\w/\\-]+\.(?:cs|csproj|asmdef|shader|hlsl|unity)\b"), "code or Unity asset file reference"),
    (re.compile(r"\b(?:MonoBehaviour|ScriptableObject|UnityEngine\.|JsonUtility|GameObject\.Find|Physics\.Raycast)\b"), "Unity/C# API identifier"),
    (re.compile(r"\b(?:schemaVersion|struct\s+\w+|namespace\s+\w+|public\s+(?:class|void|int|float|bool))\b"), "code/schema implementation detail"),
]

ID_TOKEN_RE = re.compile(r"\b((?:F|AC|CE|CP|CD|EC|RC)-[A-Z0-9-]+)\b")
ART_ID_RE = re.compile(r"\b(ART-\d{3})\b")
VERSION_RE = re.compile(r"\bv(\d+\.\d+\.\d+)\b")


def run_product_spec_gate(
    spec_path: Path,
    repo: Path,
    decisions_path: Optional[Path] = None,
) -> GateReport:
    report = GateReport(
        gate="Product Spec",
        target=str(spec_path),
        _finding_prefix="PS",
    )

    if not spec_path.is_file():
        report.add_blocked(f"Product spec not found: {spec_path}")
        report.add_check("PS-1", "Required structure and metadata", "BLOCKED", f"Missing file: {spec_path}")
        return report

    if decisions_path is None:
        candidate = repo / "docs" / "decisions.md"
        if candidate.is_file():
            decisions_path = candidate
    waivers = load_waivers(decisions_path)

    text = spec_path.read_text(encoding="utf-8", errors="replace")
    sections = parse_sections(text)
    all_slugs = {s.slug for s in sections}
    non_code_lines = strip_fenced_code_blocks(text)

    # ------------------------------------------------------------------
    # PS-1: Required structure & metadata
    # ------------------------------------------------------------------
    ps1_issues: List[str] = []
    has_h1 = any(s.level == 1 for s in sections)
    if not has_h1:
        ps1_issues.append("missing H1 title")
        report.add_finding(
            "PS-1",
            "HIGH",
            f"{spec_path.name}:1",
            "Product spec is missing a top-level `#` title.",
            "Add an H1 title naming the product, version, and spec subject.",
        )

    status_match = STATUS_RE.search(text)
    status_val = status_match.group(1).strip() if status_match else ""
    is_approved = bool(re.search(r"\bapproved\b", status_val, re.IGNORECASE))
    is_draft = bool(re.search(r"\bdraft\s+\d+\b", status_val, re.IGNORECASE))
    if not status_match or not (is_approved or is_draft):
        ps1_issues.append("invalid or missing Status line")
        report.add_finding(
            "PS-1",
            "HIGH",
            f"{spec_path.name}:1",
            f"Missing or invalid Status line (found: {status_val!r}; expected `Draft N` or `Approved`).",
            "Add `**Status:** Draft N` or `**Status:** Approved` near the top of the spec.",
        )

    if not GOAL_RE.search(text):
        ps1_issues.append("missing goal")
        report.add_finding(
            "PS-1",
            "HIGH",
            f"{spec_path.name}:1",
            "Product spec is missing a Goal / Release goal statement.",
            "Add a `**Release goal.**` statement or `## Goal` section stating what the player can now do.",
        )

    required_sections = [
        ("Features", (r"^features\b",)),
        ("Edge cases", (r"^edge\s+cases\b",)),
        ("Art requirements", (r"^art\s+(?:requirements|inventory)\b",)),
        ("Acceptance criteria / Release acceptance", (r"^(?:release\s+acceptance|acceptance\s+criteria)\b",)),
        ("Out of scope", (r"^out\s+of\s+scope\b",)),
        ("Open questions", (r"^open\s+questions\b",)),
    ]
    for label, pats in required_sections:
        sec = find_section(sections, *pats)
        if sec is None or not sec.body.strip():
            ps1_issues.append(f"missing/empty {label}")
            report.add_finding(
                "PS-1",
                "HIGH",
                spec_path.name,
                f"Required section `{label}` is missing or empty.",
                f"Add a populated `## {label}` section.",
            )

    report.add_check(
        "PS-1",
        "Required structure, Status line, Goal, and core sections present",
        "PASS" if not ps1_issues else "FAIL",
        "All required metadata and sections present" if not ps1_issues else "; ".join(ps1_issues),
    )

    # ------------------------------------------------------------------
    # PS-2: No unresolved placeholders (and no open questions if Approved)
    # ------------------------------------------------------------------
    open_q_sec = find_section(sections, r"^open\s+questions\b")
    open_q_range = (open_q_sec.start_line, open_q_sec.end_line) if open_q_sec else (-1, -1)
    ps2_hits = 0

    for line_no, line in non_code_lines:
        in_open_q = open_q_range[0] <= line_no <= open_q_range[1]
        if in_open_q and not is_approved:
            continue
        cleaned_line = re.sub(r"`[^`]*`", "", line)
        for pat, desc in PLACEHOLDER_PATTERNS:
            m = pat.search(cleaned_line)
            if m:
                ps2_hits += 1
                report.add_finding(
                    "PS-2",
                    "HIGH",
                    f"{spec_path.name}:{line_no}",
                    f"Found {desc} (`{m.group(0)}`) in spec body.",
                    "Resolve the placeholder or move unresolved owner questions into `## Open questions`.",
                )

    if is_approved and open_q_sec is not None:
        oq_body = open_q_sec.body.strip()
        if oq_body and not re.match(r"^(?:none\b|no\s+open\s+questions\b)", oq_body, re.IGNORECASE):
            ps2_hits += 1
            report.add_finding(
                "PS-2",
                "HIGH",
                f"{spec_path.name}:{open_q_sec.start_line}",
                "Spec is marked `Approved` but `## Open questions` is non-empty.",
                "Resolve all open questions with the owner and record `None.` under `## Open questions`.",
            )

    report.add_check(
        "PS-2",
        "No unresolved placeholders in spec body (and no open questions when Approved)",
        "PASS" if ps2_hits == 0 else "FAIL",
        "No placeholders found" if ps2_hits == 0 else f"{ps2_hits} placeholder/open-question issue(s)",
    )

    # ------------------------------------------------------------------
    # PS-3: Stable IDs & uniqueness across Features, Controls, Edge cases, ACs
    # ------------------------------------------------------------------
    id_locations: Dict[str, List[int]] = {}
    seen_row_lines: Set[int] = set()
    feature_ids: Set[str] = set()
    criteria_ids: Set[str] = set()

    for sec in sections:
        if re.search(r"^(?:features|controls|while\s+exploring|during\s+an|developer|edge\s+cases|acceptance\s+criteria)", sec.title, re.IGNORECASE):
            for tbl in parse_tables(sec.body, start_line_offset=sec.start_line + 1):
                if not tbl.headers:
                    continue
                if tbl.headers[0].strip().lower() == "id":
                    for row in tbl.rows:
                        if row.line_no in seen_row_lines:
                            continue
                        seen_row_lines.add(row.line_no)
                        cell_id = row.cells[0].strip()
                        if not cell_id:
                            continue
                        id_locations.setdefault(cell_id, []).append(row.line_no)
                        if re.match(r"^F-\d+$", cell_id):
                            feature_ids.add(cell_id)
                        else:
                            criteria_ids.add(cell_id)

    rel_sec = find_section(sections, r"^(?:release\s+acceptance|acceptance\s+criteria)\b")
    numbered_rc_count = 0
    if rel_sec:
        for line in rel_sec.body.splitlines():
            if re.match(r"^\s*\d+\.\s+\S", line):
                numbered_rc_count += 1

    ps3_fail = False
    if not feature_ids:
        ps3_fail = True
        report.add_finding(
            "PS-3",
            "HIGH",
            spec_path.name,
            "No Feature IDs (`F-01`, `F-02`, ...) found in a `Features` table.",
            "Give every feature a stable `F-XX` ID in the `## Features` table.",
        )
    if not criteria_ids and numbered_rc_count == 0:
        ps3_fail = True
        report.add_finding(
            "PS-3",
            "HIGH",
            spec_path.name,
            "No identifiable acceptance criteria, controls, or edge-case IDs found.",
            "Assign stable IDs (`AC-XXX`, `CE-N`, `EC-N`) or numbered release acceptance checks.",
        )
    for item_id, lines in sorted(id_locations.items()):
        if len(lines) > 1:
            ps3_fail = True
            line_list = ", ".join(str(x) for x in lines)
            report.add_finding(
                "PS-3",
                "HIGH",
                f"{spec_path.name}:{lines[1]}",
                f"Duplicate requirement ID `{item_id}` appears on lines {line_list}.",
                f"Make every requirement ID unique across the product spec.",
            )

    report.add_check(
        "PS-3",
        "Stable and unique feature/requirement IDs",
        "PASS" if not ps3_fail else "FAIL",
        f"{len(feature_ids)} feature(s), {len(criteria_ids)} table criterion ID(s), {numbered_rc_count} release check(s)",
    )

    # ------------------------------------------------------------------
    # PS-4: Testability — flag untestable/vague wording in ACs, Controls, Edge cases
    # ------------------------------------------------------------------
    ps4_hard = 0
    ps4_soft = 0
    check_sections = [
        s
        for s in sections
        if re.search(
            r"^(?:controls|edge\s+cases|release\s+acceptance|acceptance\s+criteria)\b",
            s.title,
            re.IGNORECASE,
        )
    ]
    # Avoid double-counting parent + child sections by scanning non-overlapping top matches
    checked_lines: Set[int] = set()
    for sec in check_sections:
        for offset, line in enumerate(sec.body.splitlines(), start=sec.start_line + 1):
            if offset in checked_lines:
                continue
            checked_lines.add(offset)
            cleaned_line = re.sub(r"`[^`]*`", "", line)
            hm = HARD_VAGUE_RE.search(cleaned_line)
            if hm:
                ps4_hard += 1
                report.add_finding(
                    "PS-4",
                    "HIGH",
                    f"{spec_path.name}:{offset}",
                    f"Open-ended vague quantifier `{hm.group(0)}` in `{sec.title}`.",
                    "Enumerate exact cases or replace with an explicit observable condition.",
                )
            sm = SOFT_VAGUE_RE.search(cleaned_line)
            if sm:
                ps4_soft += 1
                report.add_finding(
                    "PS-4",
                    "MEDIUM",
                    f"{spec_path.name}:{offset}",
                    f"Potentially untestable qualitative word `{sm.group(0)}` in `{sec.title}`.",
                    "Pair the wording with a concrete observable pass/fail condition.",
                )

    report.add_check(
        "PS-4",
        "No open-ended or vague untestable wording in controls, edge cases, or acceptance criteria",
        "PASS" if ps4_hard == 0 else "FAIL",
        f"Hard vague hits={ps4_hard}, soft warnings={ps4_soft}",
    )

    # ------------------------------------------------------------------
    # PS-5: Product-level boundary (no code/implementation leakage)
    # ------------------------------------------------------------------
    ps5_hits = 0
    for line_no, line in non_code_lines:
        for pat, desc in IMPL_LEAK_PATTERNS:
            m = pat.search(line)
            if m:
                ps5_hits += 1
                report.add_finding(
                    "PS-5",
                    "HIGH",
                    f"{spec_path.name}:{line_no}",
                    f"Implementation detail ({desc}: `{m.group(0)}`) found in product spec.",
                    "Keep product specs focused on player experience; move code/schema/path details to the technical spec.",
                )

    report.add_check(
        "PS-5",
        "Product-level boundary preserved (no code/schema/source-path leakage)",
        "PASS" if ps5_hits == 0 else "FAIL",
        "No implementation leakage detected" if ps5_hits == 0 else f"{ps5_hits} implementation reference(s)",
    )

    # ------------------------------------------------------------------
    # PS-6: Local relative links and internal anchors resolve
    # ------------------------------------------------------------------
    ps6_broken = 0
    links = extract_markdown_links(text)
    for line_no, is_img, label, target in links:
        if target.startswith(("http://", "https://", "mailto:")):
            continue
        if target.startswith("#"):
            anchor = target[1:].lower()
            if anchor not in all_slugs:
                ps6_broken += 1
                report.add_finding(
                    "PS-6",
                    "HIGH",
                    f"{spec_path.name}:{line_no}",
                    f"Broken internal anchor link `#{anchor}`.",
                    "Update the anchor to match an existing heading in this document.",
                )
            continue
        path_part = target.split("#", 1)[0]
        resolved = (spec_path.parent / path_part).resolve()
        if not resolved.exists():
            ps6_broken += 1
            kind = "image" if is_img else "link"
            report.add_finding(
                "PS-6",
                "HIGH",
                f"{spec_path.name}:{line_no}",
                f"Broken relative {kind} target `{target}` (resolved path does not exist).",
                f"Fix the relative path or commit the missing referenced file `{path_part}`.",
            )

    report.add_check(
        "PS-6",
        "All local relative links, images, and internal anchors resolve",
        "PASS" if ps6_broken == 0 else "FAIL",
        f"Checked {len(links)} link(s); {ps6_broken} broken",
    )

    # ------------------------------------------------------------------
    # PS-7: Art requirements inventory presence & cross-reference integrity
    # ------------------------------------------------------------------
    art_sec = find_section(sections, r"^art\s+(?:requirements|inventory)\b")
    declared_art_ids: Set[str] = set()
    if art_sec:
        for tbl in parse_tables(art_sec.body, start_line_offset=art_sec.start_line + 1):
            if tbl.headers and tbl.headers[0].strip().lower() == "id":
                for row in tbl.rows:
                    if ART_ID_RE.fullmatch(row.cells[0].strip()):
                        declared_art_ids.add(row.cells[0].strip())

    referenced_art_ids: Set[str] = set()
    for _, line in non_code_lines:
        for m in ART_ID_RE.finditer(line):
            referenced_art_ids.add(m.group(1))

    dangling_art = sorted(referenced_art_ids - declared_art_ids)
    ps7_fail = False
    if dangling_art:
        ps7_fail = True
        report.add_finding(
            "PS-7",
            "HIGH",
            spec_path.name,
            f"Art ID(s) referenced in spec body but missing from the Art requirements table: {', '.join(dangling_art)}.",
            "Add each referenced `ART-XXX` item to the Art requirements inventory table.",
        )
    elif art_sec and not declared_art_ids:
        if not re.search(r"no\s+art(?:\s+changes?)?\s+(?:is\s+|are\s+)?needed", art_sec.body, re.IGNORECASE):
            ps7_fail = True
            report.add_finding(
                "PS-7",
                "HIGH",
                f"{spec_path.name}:{art_sec.start_line}",
                "Art requirements section has no `ART-XXX` table entries and no explicit `no art needed` statement.",
                "List all `ART-XXX` items in a table or state explicitly why no art is needed.",
            )

    report.add_check(
        "PS-7",
        "Art inventory declared and all referenced ART-XXX IDs exist in inventory",
        "PASS" if not ps7_fail else "FAIL",
        f"{len(declared_art_ids)} declared, {len(referenced_art_ids)} referenced, {len(dangling_art)} dangling",
    )

    # ------------------------------------------------------------------
    # PS-8: Approved spec recorded in docs/decisions.md (when present)
    # ------------------------------------------------------------------
    vm = VERSION_RE.search(spec_path.name) or VERSION_RE.search(text)
    version_str = f"v{vm.group(1)}" if vm else ""
    if is_approved and decisions_path and decisions_path.is_file() and version_str:
        dec_text = decisions_path.read_text(encoding="utf-8", errors="replace")
        if version_str not in dec_text:
            report.add_finding(
                "PS-8",
                "MEDIUM",
                str(decisions_path.name),
                f"Spec is marked `Approved` for `{version_str}`, but `{version_str}` was not found in `{decisions_path.name}`.",
                f"Record the owner's approval and decisions for `{version_str}` in `{decisions_path.name}`.",
            )
            report.add_check("PS-8", "Approved spec version recorded in decisions log", "FAIL", f"{version_str} missing from {decisions_path.name}")
        else:
            report.add_check("PS-8", "Approved spec version recorded in decisions log", "PASS", f"{version_str} present in {decisions_path.name}")
    else:
        reason = "Draft spec" if not is_approved else "No decisions.md provided/found"
        report.add_check("PS-8", "Approved spec version recorded in decisions log", "SKIP", reason)

    report.apply_waivers(waivers)
    return report


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Deterministic Product Spec Gate")
    parser.add_argument("--spec", required=True, type=Path, help="Path to product spec markdown file")
    parser.add_argument("--repo", default=Path("."), type=Path, help="Repository root path")
    parser.add_argument("--decisions", default=None, type=Path, help="Optional path to docs/decisions.md")
    parser.add_argument("--format", choices=("markdown", "json"), default="markdown")
    args = parser.parse_args(argv)

    report = run_product_spec_gate(args.spec.resolve(), args.repo.resolve(), args.decisions)
    if args.format == "json":
        print(json.dumps(report.to_dict(), indent=2))
    else:
        print(report.to_markdown())
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
