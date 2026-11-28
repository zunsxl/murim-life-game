"""Deterministic Art Inventory Gate (AI-1 .. AI-7)."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Optional, Set

from .common import (
    GateReport,
    find_section,
    load_waivers,
    parse_sections,
    parse_tables,
)

ART_ID_RE = re.compile(r"^ART-\d{3}$")
FEATURE_ID_RE = re.compile(r"\bF-\d+\b")
VAGUE_COUNT_RE = re.compile(r"\b(?:etc\.?|as\s+needed|tbd|todo|several|various|some)\b", re.IGNORECASE)
UNRESOLVED_DIR_RE = re.compile(r"\b(?:tbd|todo|unresolved|pending|choose|option\s*\?|none\s+chosen)\b|\?\?\?", re.IGNORECASE)
FORBIDDEN_SOURCE_RE = re.compile(
    r"(?:docs/references/|shutterstock|gettyimages|istockphoto|adobe\s+stock\s+preview|watermarked?\s+preview|extracted\s+from)",
    re.IGNORECASE,
)
ASSET_PATH_RE = re.compile(r"(?:Assets/[^\s,)`]+)")


def _lookup_col(row_map: Dict[str, str], *aliases: str) -> str:
    for header, val in row_map.items():
        for alias in aliases:
            if re.search(alias, header, re.IGNORECASE):
                return val.strip()
    return ""


def run_art_inventory_gate(
    spec_path: Path,
    repo: Path,
    decisions_path: Optional[Path] = None,
) -> GateReport:
    report = GateReport(
        gate="Art Inventory",
        target=str(spec_path),
        _finding_prefix="AI",
    )

    if not spec_path.is_file():
        report.add_blocked(f"Product spec not found: {spec_path}")
        report.add_check("AI-1", "Art requirements inventory table present", "BLOCKED", f"Missing file: {spec_path}")
        return report

    if decisions_path is None:
        candidate = repo / "docs" / "decisions.md"
        if candidate.is_file():
            decisions_path = candidate
    waivers = load_waivers(decisions_path)

    text = spec_path.read_text(encoding="utf-8", errors="replace")
    sections = parse_sections(text)

    # Collect declared feature IDs from ## Features
    declared_features: Set[str] = set()
    feat_sec = find_section(sections, r"^features\b")
    if feat_sec:
        for tbl in parse_tables(feat_sec.body, start_line_offset=feat_sec.start_line + 1):
            if tbl.headers and tbl.headers[0].strip().lower() == "id":
                for row in tbl.rows:
                    if re.fullmatch(r"F-\d+", row.cells[0].strip()):
                        declared_features.add(row.cells[0].strip())

    # ------------------------------------------------------------------
    # AI-1: Art requirements section and inventory table present
    # ------------------------------------------------------------------
    art_sec = find_section(sections, r"^art\s+(?:requirements|inventory)\b")
    if art_sec is None:
        report.add_finding(
            "AI-1",
            "HIGH",
            spec_path.name,
            "No `## Art requirements` (or `## Art inventory`) section found.",
            "Add an `## Art requirements` section with the art inventory table (or state explicitly that no art is needed).",
        )
        report.add_check("AI-1", "Art requirements inventory table present", "FAIL", "Section missing")
        report.apply_waivers(waivers)
        return report

    # Check if spec explicitly declares no art needed
    if re.search(r"no\s+art(?:\s+changes?)?\s+(?:is\s+|are\s+)?needed", art_sec.body, re.IGNORECASE) and not re.search(r"\bART-\d{3}\b", art_sec.body):
        report.add_check("AI-1", "Art requirements inventory table present", "PASS", "Explicitly states no art needed")
        for cid, desc in (
            ("AI-2", "Stable and unique ART-NNN IDs"),
            ("AI-3", "Exact numeric variant counts without vague quantifiers"),
            ("AI-4", "Valid disposition and existing reused/modified assets"),
            ("AI-5", "Direction/option chosen for every item"),
            ("AI-6", "Feature linkage and per-item acceptance criteria"),
            ("AI-7", "Areas needing no art justified and no forbidden/reference shipping sources"),
        ):
            report.add_check(cid, desc, "SKIP", "No art items in scope")
        report.apply_waivers(waivers)
        return report

    # Find the main inventory table (first table in art_sec whose first column is ID)
    art_tables = parse_tables(art_sec.body, start_line_offset=art_sec.start_line + 1)
    inv_table = None
    for tbl in art_tables:
        if tbl.headers and tbl.headers[0].strip().lower() == "id":
            inv_table = tbl
            break

    if inv_table is None or not inv_table.rows:
        report.add_finding(
            "AI-1",
            "HIGH",
            f"{spec_path.name}:{art_sec.start_line}",
            "No populated Art inventory table with an `ID` column found under `## Art requirements`.",
            "Add a Markdown table with columns: ID, Item, Purpose/Features, Variants (count), Disposition, Direction.",
        )
        report.add_check("AI-1", "Art requirements inventory table present", "FAIL", "Inventory table missing or empty")
        report.apply_waivers(waivers)
        return report

    report.add_check(
        "AI-1",
        "Art requirements inventory table present",
        "PASS",
        f"{len(inv_table.rows)} inventory row(s) found",
    )

    # Map detail subsections `### ART-NNN ...`
    detail_sections: Dict[str, tuple[int, str]] = {}
    for sec in sections:
        if sec.start_line > art_sec.start_line and sec.end_line <= art_sec.end_line:
            m = re.match(r"^(ART-\d{3})\b", sec.title.strip())
            if m:
                detail_sections[m.group(1)] = (sec.start_line, sec.body)

    # ------------------------------------------------------------------
    # AI-2: Stable & unique ART-NNN IDs
    # ------------------------------------------------------------------
    seen_ids: Dict[str, int] = {}
    ai2_fail = False
    for row in inv_table.rows:
        art_id = row.cells[0].strip()
        if not ART_ID_RE.fullmatch(art_id):
            ai2_fail = True
            report.add_finding(
                "AI-2",
                "HIGH",
                f"{spec_path.name}:{row.line_no}",
                f"Invalid art inventory ID `{art_id}` (expected `ART-NNN`, e.g. `ART-001`).",
                "Use stable three-digit `ART-NNN` identifiers.",
            )
            continue
        if art_id in seen_ids:
            ai2_fail = True
            report.add_finding(
                "AI-2",
                "HIGH",
                f"{spec_path.name}:{row.line_no}",
                f"Duplicate art inventory ID `{art_id}` (first defined on line {seen_ids[art_id]}).",
                "Give each art item or family a unique `ART-NNN` ID.",
            )
        else:
            seen_ids[art_id] = row.line_no

    report.add_check(
        "AI-2",
        "Stable and unique ART-NNN IDs",
        "PASS" if not ai2_fail else "FAIL",
        f"{len(seen_ids)} unique ART-NNN ID(s)",
    )

    # ------------------------------------------------------------------
    # AI-3: Exact numeric variant counts without vague quantifiers
    # ------------------------------------------------------------------
    ai3_fail = False
    for row in inv_table.rows:
        art_id = row.cells[0].strip()
        variants = _lookup_col(row.by_header, r"variant", r"count")
        if not variants or not re.search(r"\d+", variants):
            ai3_fail = True
            report.add_finding(
                "AI-3",
                "HIGH",
                f"{spec_path.name}:{row.line_no}",
                f"`{art_id}` is missing an explicit numeric variant count (found: {variants!r}).",
                "State exact integer counts for all variants and states.",
            )
        vm = VAGUE_COUNT_RE.search(variants)
        if vm:
            ai3_fail = True
            report.add_finding(
                "AI-3",
                "HIGH",
                f"{spec_path.name}:{row.line_no}",
                f"`{art_id}` uses vague quantifier `{vm.group(0)}` in Variants column.",
                "Replace vague quantifiers with an exact enumeration and integer count.",
            )
        if art_id in detail_sections:
            dline, dbody = detail_sections[art_id]
            for offset, line in enumerate(dbody.splitlines(), start=dline + 1):
                if re.search(r"variants?|states?|parts?", line, re.IGNORECASE):
                    dm = VAGUE_COUNT_RE.search(line)
                    if dm:
                        ai3_fail = True
                        report.add_finding(
                            "AI-3",
                            "HIGH",
                            f"{spec_path.name}:{offset}",
                            f"`{art_id}` detail uses vague quantifier `{dm.group(0)}`.",
                            "Enumerate every variant/state explicitly with an exact count.",
                        )

    report.add_check(
        "AI-3",
        "Exact numeric variant counts without vague quantifiers",
        "PASS" if not ai3_fail else "FAIL",
        "All items have explicit numeric counts" if not ai3_fail else "Missing/vague variant count(s)",
    )

    # ------------------------------------------------------------------
    # AI-4: Valid disposition & existing asset verification for Reuse/Modify
    # ------------------------------------------------------------------
    ai4_fail = False
    for row in inv_table.rows:
        art_id = row.cells[0].strip()
        disp = _lookup_col(row.by_header, r"disposition")
        if not disp or not re.search(r"\b(new|reuse|modify)\b", disp, re.IGNORECASE):
            ai4_fail = True
            report.add_finding(
                "AI-4",
                "HIGH",
                f"{spec_path.name}:{row.line_no}",
                f"`{art_id}` has invalid or missing Disposition `{disp}` (expected `New`, `Reuse`, or `Modify`).",
                "Set Disposition to `New`, `Reuse`, or `Modify` (naming the existing asset when reusing or modifying).",
            )
        elif re.search(r"\b(reuse|modify)\b", disp, re.IGNORECASE):
            combined = disp + " " + (detail_sections[art_id][1] if art_id in detail_sections else "")
            paths = ASSET_PATH_RE.findall(combined)
            refs_art = re.findall(r"\bART-\d{3}\b", disp)
            if not paths and not refs_art:
                ai4_fail = True
                report.add_finding(
                    "AI-4",
                    "HIGH",
                    f"{spec_path.name}:{row.line_no}",
                    f"`{art_id}` has disposition `{disp}` but does not name the existing asset path or prior `ART-NNN`.",
                    "Name the existing asset path or `ART-NNN` being reused or modified.",
                )
            for p in paths:
                if not (repo / p).exists():
                    ai4_fail = True
                    report.add_finding(
                        "AI-4",
                        "HIGH",
                        f"{spec_path.name}:{row.line_no}",
                        f"`{art_id}` references existing asset `{p}` which does not exist in `{repo}`.",
                        "Verify the asset path or change disposition to `New`.",
                    )

    report.add_check(
        "AI-4",
        "Valid disposition and existing asset verification for Reuse/Modify",
        "PASS" if not ai4_fail else "FAIL",
        "All dispositions valid" if not ai4_fail else "Disposition or existing-asset error(s)",
    )

    # ------------------------------------------------------------------
    # AI-5: Direction / option chosen for every in-scope item
    # ------------------------------------------------------------------
    ai5_fail = False
    for row in inv_table.rows:
        art_id = row.cells[0].strip()
        direction = _lookup_col(row.by_header, r"direction", r"option", r"choice")
        if not direction or UNRESOLVED_DIR_RE.search(direction):
            ai5_fail = True
            report.add_finding(
                "AI-5",
                "HIGH",
                f"{spec_path.name}:{row.line_no}",
                f"`{art_id}` has no resolved direction/option chosen (found: {direction!r}).",
                "Record the owner's chosen direction/option in the inventory table before approval.",
            )

    report.add_check(
        "AI-5",
        "Direction/option chosen for every in-scope item",
        "PASS" if not ai5_fail else "FAIL",
        "All items have resolved direction choices" if not ai5_fail else "Unresolved direction/option(s)",
    )

    # ------------------------------------------------------------------
    # AI-6: Feature linkage & per-item acceptance criteria
    # ------------------------------------------------------------------
    ai6_fail = False
    for row in inv_table.rows:
        art_id = row.cells[0].strip()
        purpose = _lookup_col(row.by_header, r"purpose", r"feature")
        linked_feats = set(FEATURE_ID_RE.findall(purpose))
        if not linked_feats:
            ai6_fail = True
            report.add_finding(
                "AI-6",
                "HIGH",
                f"{spec_path.name}:{row.line_no}",
                f"`{art_id}` does not link to any Feature ID (`F-XX`) in its Purpose/Features column.",
                "Link each art item to the `F-XX` feature ID(s) it serves.",
            )
        elif declared_features:
            unknown = sorted(linked_feats - declared_features)
            if unknown:
                ai6_fail = True
                report.add_finding(
                    "AI-6",
                    "HIGH",
                    f"{spec_path.name}:{row.line_no}",
                    f"`{art_id}` links to undefined feature ID(s): {', '.join(unknown)}.",
                    "Fix the feature ID reference to match `## Features`.",
                )

        # Check acceptance criteria either in inline column or detail subsection
        inline_ac = _lookup_col(row.by_header, r"acceptance")
        if art_id in detail_sections:
            dline, dbody = detail_sections[art_id]
            has_vis = bool(re.search(r"visual\s+requirements?", dbody, re.IGNORECASE))
            has_ac = bool(re.search(r"acceptance", dbody, re.IGNORECASE))
            if not has_vis:
                ai6_fail = True
                report.add_finding(
                    "AI-6",
                    "MEDIUM",
                    f"{spec_path.name}:{dline}",
                    f"`{art_id}` subsection is missing visual requirements.",
                    "Add a `Visual requirements:` bullet for this item.",
                )
            if not has_ac and not inline_ac:
                ai6_fail = True
                report.add_finding(
                    "AI-6",
                    "HIGH",
                    f"{spec_path.name}:{dline}",
                    f"`{art_id}` is missing per-item acceptance criteria.",
                    "Add an `Acceptance:` bullet or Acceptance column entry for this item.",
                )
        elif not inline_ac:
            ai6_fail = True
            report.add_finding(
                "AI-6",
                "HIGH",
                f"{spec_path.name}:{row.line_no}",
                f"`{art_id}` has neither a `### {art_id}` detail subsection nor an inline Acceptance column.",
                "Add a detail subsection (with Visual requirements and Acceptance) or an Acceptance column.",
            )

    report.add_check(
        "AI-6",
        "Valid feature linkage and per-item acceptance criteria",
        "PASS" if not ai6_fail else "FAIL",
        "All items linked to declared features with acceptance criteria" if not ai6_fail else "Feature-link or acceptance gap(s)",
    )

    # ------------------------------------------------------------------
    # AI-7: Areas needing no art justified & no forbidden/reference shipping sources
    # ------------------------------------------------------------------
    ai7_fail = False
    no_art_sec = find_section(sections, r"areas?\s+that\s+need\s+no\s+art", r"no\s+art\s+needed")
    if no_art_sec is None or not no_art_sec.body.strip():
        ai7_fail = True
        report.add_finding(
            "AI-7",
            "HIGH",
            f"{spec_path.name}:{art_sec.start_line}",
            "Missing `### Areas that need no art` justification section/table.",
            "Document visual areas needing no art with a brief reason for each.",
        )

    for offset, line in enumerate(art_sec.body.splitlines(), start=art_sec.start_line + 1):
        # Allow explicit disclaimers like "not shipping" / "reference only, never shipping"
        if re.search(r"(?:never|not)\s+(?:a\s+)?shipping|inspiration\s+only|reference\s+only", line, re.IGNORECASE):
            continue
        fm = FORBIDDEN_SOURCE_RE.search(line)
        if fm:
            ai7_fail = True
            report.add_finding(
                "AI-7",
                "CRITICAL",
                f"{spec_path.name}:{offset}",
                f"Art inventory references a disallowed/unlicensed shipping source (`{fm.group(0)}`).",
                "Remove reference images or watermarked/extracted sources from shipping plans.",
            )

    report.add_check(
        "AI-7",
        "Areas needing no art justified and no reference/unlicensed shipping sources",
        "PASS" if not ai7_fail else "FAIL",
        "No-art areas documented and sourcing checks clean" if not ai7_fail else "Missing no-art justification or disallowed source",
    )

    report.apply_waivers(waivers)
    return report


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Deterministic Art Inventory Gate")
    parser.add_argument("--spec", required=True, type=Path, help="Path to product spec markdown file")
    parser.add_argument("--repo", default=Path("."), type=Path, help="Repository root path")
    parser.add_argument("--decisions", default=None, type=Path, help="Optional path to docs/decisions.md")
    parser.add_argument("--format", choices=("markdown", "json"), default="markdown")
    args = parser.parse_args(argv)

    report = run_art_inventory_gate(args.spec.resolve(), args.repo.resolve(), args.decisions)
    if args.format == "json":
        print(json.dumps(report.to_dict(), indent=2))
    else:
        print(report.to_markdown())
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
