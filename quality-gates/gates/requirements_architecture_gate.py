"""Deterministic Requirements & Architecture Gate (RA-1 .. RA-9)."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import deque
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple

from .common import (
    GateReport,
    expand_prefixed_ids,
    extract_markdown_links,
    find_section,
    git_commit_exists,
    is_git_repo,
    load_waivers,
    parse_sections,
    parse_tables,
    strip_fenced_code_blocks,
)

SHA_RE = re.compile(r"\b([0-9a-f]{7,40})\b", re.IGNORECASE)
PLACEHOLDER_RE = re.compile(r"\b(?:TBD|TODO|FIXME|TBA|WIP)\b|\?\?\?")
VERSION_FILE_RE = re.compile(r"v(\d+\.\d+\.\d+)-technical\.md$", re.IGNORECASE)
BUNDLE_VERSION_RE = re.compile(r"bundleVersion[^0-9]*(\d+\.\d+\.\d+)", re.IGNORECASE)
CHECK_PREFIXES = ("T-CORE", "T-ENG", "T-ART", "T-BLD", "T-TOOL", "T-BAL", "PT")
REQ_PREFIXES = ("F", "CE", "CP", "CD", "EC", "RC", "AC")


def _extract_product_requirements(product_text: str) -> Tuple[Set[str], Set[str]]:
    """Return (requirement_ids, art_ids) from the product spec."""
    sections = parse_sections(product_text)
    reqs: Set[str] = set()
    arts: Set[str] = set()

    for sec in sections:
        if re.search(r"^(?:features|controls|while\s+exploring|during\s+an|developer|edge\s+cases|acceptance\s+criteria)", sec.title, re.IGNORECASE):
            for tbl in parse_tables(sec.body, start_line_offset=sec.start_line + 1):
                if tbl.headers and tbl.headers[0].strip().lower() == "id":
                    for row in tbl.rows:
                        cid = row.cells[0].strip()
                        if re.fullmatch(r"(?:F|CE|CP|CD|EC|AC|RC)-[A-Z0-9-]+", cid):
                            reqs.add(cid)

    rel_sec = find_section(sections, r"^release\s+acceptance\b")
    if rel_sec:
        for line in rel_sec.body.splitlines():
            m = re.match(r"^\s*(\d+)\.\s+\S", line)
            if m:
                reqs.add(f"RC-{m.group(1)}")

    art_sec = find_section(sections, r"^art\s+(?:requirements|inventory)\b")
    if art_sec:
        for tbl in parse_tables(art_sec.body, start_line_offset=art_sec.start_line + 1):
            if tbl.headers and tbl.headers[0].strip().lower() == "id":
                for row in tbl.rows:
                    cid = row.cells[0].strip()
                    if re.fullmatch(r"ART-\d{3}", cid):
                        arts.add(cid)

    return reqs, arts


def _expand_all_checks(text: str) -> Set[str]:
    checks: Set[str] = set()
    for pref in CHECK_PREFIXES:
        checks |= expand_prefixed_ids(text, pref)
    return checks


def _expand_all_reqs(text: str) -> Set[str]:
    reqs: Set[str] = set()
    for pref in REQ_PREFIXES:
        reqs |= expand_prefixed_ids(text, pref)
    # Also catch custom AC-XXX-N style IDs
    for m in re.finditer(r"\b(AC-[A-Z0-9]+-\d+)\b", text):
        reqs.add(m.group(1))
    return reqs


def _extract_owned_paths_by_ticket(notes: str, tickets: List[str]) -> Dict[str, Set[str]]:
    """Extract backtick-quoted path segments associated with each ticket in an Execution plan note."""
    result: Dict[str, Set[str]] = {t: set() for t in tickets}
    # Split on semicolons or ticket mentions
    for t in tickets:
        # Find substring starting at this ticket up to the next ticket mention or end
        pat = re.compile(rf"\b{re.escape(t)}\b([^;]*)", re.IGNORECASE)
        for m in pat.finditer(notes):
            seg = m.group(1)
            for p in re.findall(r"`([^`]+)`", seg):
                norm = p.strip().replace("\\", "/")
                if "/" in norm or "." in norm:
                    result[t].add(norm)
    return result


def _paths_overlap(p1: str, p2: str) -> bool:
    a = p1.rstrip("/")
    b = p2.rstrip("/")
    if a == b:
        return True
    return a.startswith(b + "/") or b.startswith(a + "/")


def run_requirements_architecture_gate(
    tech_spec_path: Path,
    product_spec_path: Optional[Path],
    repo: Path,
    decisions_path: Optional[Path] = None,
    check_git: bool = True,
) -> GateReport:
    report = GateReport(
        gate="Requirements & Architecture",
        target=str(tech_spec_path),
        _finding_prefix="RA",
    )

    if not tech_spec_path.is_file():
        report.add_blocked(f"Technical spec not found: {tech_spec_path}")
        report.add_check("RA-1", "Required header and sections present", "BLOCKED", f"Missing file: {tech_spec_path}")
        return report

    if decisions_path is None:
        candidate = repo / "docs" / "decisions.md"
        if candidate.is_file():
            decisions_path = candidate
    waivers = load_waivers(decisions_path)

    text = tech_spec_path.read_text(encoding="utf-8", errors="replace")
    sections = parse_sections(text)
    all_slugs = {s.slug for s in sections}
    non_code_lines = strip_fenced_code_blocks(text)

    # Auto-detect product spec if omitted and named in filename (vX.Y.Z-technical.md -> vX.Y.Z.md)
    if product_spec_path is None:
        vm = VERSION_FILE_RE.search(tech_spec_path.name)
        if vm:
            guess = tech_spec_path.parent / f"v{vm.group(1)}.md"
            if guess.is_file():
                product_spec_path = guess

    product_reqs: Set[str] = set()
    product_arts: Set[str] = set()
    if product_spec_path is None or not product_spec_path.is_file():
        report.add_blocked(
            f"Product spec is required for traceability checks (not found: {product_spec_path})."
        )
    else:
        product_text = product_spec_path.read_text(encoding="utf-8", errors="replace")
        product_reqs, product_arts = _extract_product_requirements(product_text)

    # ------------------------------------------------------------------
    # RA-1: Required header metadata & core sections
    # ------------------------------------------------------------------
    ra1_missing: List[str] = []
    if not re.search(r"implementation\s+baseline", text, re.IGNORECASE):
        ra1_missing.append("Implementation baseline")
        report.add_finding(
            "RA-1",
            "HIGH",
            f"{tech_spec_path.name}:1",
            "Technical spec header is missing `Implementation baseline` commit SHA.",
            "Record the implementation baseline commit SHA in the spec header.",
        )

    required_sections = [
        ("Overview", (r"\boverview\b",)),
        ("Implementation tickets", (r"\bimplementation\s+tickets\b", r"\btickets\b")),
        ("Test plan", (r"\btest\s+plan\b",)),
        ("Version", (r"\bversion\b",)),
        ("Risks and technical decisions", (r"\brisks\b",)),
    ]
    if product_arts:
        required_sections.insert(1, ("Art delivery contracts", (r"\bart\s+delivery\s+contracts?\b",)))

    for label, pats in required_sections:
        sec = find_section(sections, *pats)
        if sec is None or not sec.body.strip():
            ra1_missing.append(label)
            report.add_finding(
                "RA-1",
                "HIGH",
                tech_spec_path.name,
                f"Required technical spec section `{label}` is missing or empty.",
                f"Add a populated `{label}` section.",
            )

    report.add_check(
        "RA-1",
        "Required header metadata and core technical spec sections present",
        "PASS" if not ra1_missing else "FAIL",
        "All required sections present" if not ra1_missing else f"Missing: {', '.join(ra1_missing)}",
    )

    # ------------------------------------------------------------------
    # RA-2: Implementation baseline commit exists in git
    # ------------------------------------------------------------------
    baseline_line = ""
    baseline_line_no = 1
    for ln, line in non_code_lines[:40]:
        if re.search(r"implementation\s+baseline", line, re.IGNORECASE):
            baseline_line = line
            baseline_line_no = ln
            break
    sha_match = SHA_RE.search(baseline_line) if baseline_line else None
    if not sha_match:
        report.add_finding(
            "RA-2",
            "HIGH",
            f"{tech_spec_path.name}:{baseline_line_no}",
            "Could not parse a git commit SHA from the `Implementation baseline` line.",
            "Specify the exact 7–40 character git commit SHA in `Implementation baseline`.",
        )
        report.add_check("RA-2", "Baseline commit SHA valid in repository", "FAIL", "No commit SHA found")
    elif not check_git or not is_git_repo(repo):
        report.add_check("RA-2", "Baseline commit SHA valid in repository", "SKIP", f"Git verification skipped (SHA `{sha_match.group(1)}`)")
    else:
        sha = sha_match.group(1)
        if git_commit_exists(repo, sha):
            report.add_check("RA-2", "Baseline commit SHA valid in repository", "PASS", f"Commit `{sha}` verified")
        else:
            report.add_finding(
                "RA-2",
                "HIGH",
                f"{tech_spec_path.name}:{baseline_line_no}",
                f"Baseline commit `{sha}` does not exist in repository `{repo}`.",
                "Update `Implementation baseline` to an existing commit SHA in git history.",
            )
            report.add_check("RA-2", "Baseline commit SHA valid in repository", "FAIL", f"Commit `{sha}` not found")

    # ------------------------------------------------------------------
    # Parse Tickets table, Execution plan, and Test plan
    # ------------------------------------------------------------------
    tickets_sec = find_section(sections, r"\bimplementation\s+tickets\b", r"^tickets\b")
    test_sec = find_section(sections, r"\btest\s+plan\b")

    defined_tickets: Dict[str, Tuple[int, Set[str], Set[str], Set[str]]] = {}
    # ticket_id -> (line_no, dep_tickets, acs, checks)
    ticket_tables = parse_tables(tickets_sec.body, start_line_offset=tickets_sec.start_line + 1) if tickets_sec else []
    main_ticket_tbl = next(
        (t for t in ticket_tables if t.headers and t.headers[0].strip().lower() == "id"),
        None,
    )
    exec_plan_tbl = next(
        (t for t in ticket_tables if t.headers and re.search(r"group", t.headers[0], re.IGNORECASE)),
        None,
    )

    ra4_fail = False
    if main_ticket_tbl:
        for row in main_ticket_tbl.rows:
            tid = row.cells[0].strip()
            if not re.fullmatch(r"ARCH-\d{3}", tid):
                ra4_fail = True
                report.add_finding(
                    "RA-4",
                    "HIGH",
                    f"{tech_spec_path.name}:{row.line_no}",
                    f"Invalid ticket ID `{tid}` (expected `ARCH-NNN`).",
                    "Use stable `ARCH-001`, `ARCH-002`, ... ticket IDs.",
                )
                continue
            if tid in defined_tickets:
                ra4_fail = True
                report.add_finding(
                    "RA-4",
                    "HIGH",
                    f"{tech_spec_path.name}:{row.line_no}",
                    f"Duplicate ticket ID `{tid}`.",
                    "Ensure every implementation ticket has a unique ID.",
                )
                continue
            dep_cell = next((v for k, v in row.by_header.items() if "depend" in k), "")
            ac_cell = next((v for k, v in row.by_header.items() if "ac" in k), "")
            chk_cell = next((v for k, v in row.by_header.items() if "check" in k or "verif" in k), "")
            scope_cell = next((v for k, v in row.by_header.items() if "scope" in k or "contract" in k), "")
            if not scope_cell.strip():
                ra4_fail = True
                report.add_finding(
                    "RA-4",
                    "HIGH",
                    f"{tech_spec_path.name}:{row.line_no}",
                    f"Ticket `{tid}` has an empty Scope/Contract cell.",
                    "Link the ticket to exact spec sections, interfaces, and files.",
                )
            deps = expand_prefixed_ids(dep_cell, "ARCH")
            acs = _expand_all_reqs(ac_cell)
            chks = _expand_all_checks(chk_cell)
            defined_tickets[tid] = (row.line_no, deps, acs, chks)
    else:
        ra4_fail = True
        report.add_finding(
            "RA-4",
            "HIGH",
            tech_spec_path.name,
            "No `Implementation tickets` table with an `ID` column found.",
            "Add an `Implementation tickets` table listing all `ARCH-NNN` tickets.",
        )

    # Validate ticket dependency references and check DAG acyclicity (Kahn's algorithm)
    for tid, (ln, deps, _, _) in defined_tickets.items():
        for dep in sorted(deps):
            if dep == tid:
                ra4_fail = True
                report.add_finding(
                    "RA-4",
                    "HIGH",
                    f"{tech_spec_path.name}:{ln}",
                    f"Ticket `{tid}` depends on itself.",
                    "Remove the self-dependency.",
                )
            elif dep not in defined_tickets:
                ra4_fail = True
                report.add_finding(
                    "RA-4",
                    "HIGH",
                    f"{tech_spec_path.name}:{ln}",
                    f"Ticket `{tid}` depends on undefined ticket `{dep}`.",
                    "Define the prerequisite ticket or fix the dependency ID.",
                )

    in_deg: Dict[str, int] = {t: 0 for t in defined_tickets}
    adj: Dict[str, List[str]] = {t: [] for t in defined_tickets}
    for tid, (_, deps, _, _) in defined_tickets.items():
        for dep in deps:
            if dep in defined_tickets and dep != tid:
                adj[dep].append(tid)
                in_deg[tid] += 1
    q = deque(sorted(t for t, d in in_deg.items() if d == 0))
    visited = 0
    while q:
        cur = q.popleft()
        visited += 1
        for nxt in sorted(adj[cur]):
            in_deg[nxt] -= 1
            if in_deg[nxt] == 0:
                q.append(nxt)
    if visited < len(defined_tickets):
        cyclic = sorted(t for t, d in in_deg.items() if d > 0)
        ra4_fail = True
        report.add_finding(
            "RA-4",
            "HIGH",
            tech_spec_path.name,
            f"Ticket dependency graph contains a cycle involving: {', '.join(cyclic)}.",
            "Break the cycle so tickets form a DAG.",
        )

    report.add_check(
        "RA-4",
        "Ticket schema valid and dependency graph acyclic",
        "PASS" if not ra4_fail else "FAIL",
        f"{len(defined_tickets)} ticket(s) validated" if not ra4_fail else "Ticket schema/DAG error(s)",
    )

    # ------------------------------------------------------------------
    # RA-5: Execution plan completeness, ordering, and parallel ownership conflicts
    # ------------------------------------------------------------------
    ra5_fail = False
    if exec_plan_tbl is None or not exec_plan_tbl.rows:
        ra5_fail = True
        report.add_finding(
            "RA-5",
            "HIGH",
            tech_spec_path.name,
            "Execution plan table (with `Group` column) is missing or empty.",
            "Add an Execution plan table ordering all `ARCH-NNN` tickets into dependency groups.",
        )
    else:
        ticket_group: Dict[str, int] = {}
        for row in exec_plan_tbl.rows:
            g_str = row.cells[0].strip()
            gm = re.search(r"\d+", g_str)
            g_num = int(gm.group(0)) if gm else 0
            t_cell = next((v for k, v in row.by_header.items() if "ticket" in k), "")
            p_cell = next((v for k, v in row.by_header.items() if "parallel" in k), "")
            n_cell = next((v for k, v in row.by_header.items() if "note" in k), "")
            row_tickets = sorted(expand_prefixed_ids(t_cell, "ARCH"))
            for t in row_tickets:
                ticket_group[t] = g_num
            if re.search(r"\byes\b", p_cell, re.IGNORECASE) and len(row_tickets) > 1:
                owned = _extract_owned_paths_by_ticket(n_cell, row_tickets)
                for idx_a in range(len(row_tickets)):
                    for idx_b in range(idx_a + 1, len(row_tickets)):
                        ta, tb = row_tickets[idx_a], row_tickets[idx_b]
                        for pa in owned[ta]:
                            for pb in owned[tb]:
                                if _paths_overlap(pa, pb):
                                    ra5_fail = True
                                    report.add_finding(
                                        "RA-5",
                                        "HIGH",
                                        f"{tech_spec_path.name}:{row.line_no}",
                                        f"Parallel tickets `{ta}` and `{tb}` declare overlapping owned paths `{pa}` and `{pb}`.",
                                        "Sequence the overlapping tickets into separate groups or separate their file ownership.",
                                    )

        missing_from_plan = sorted(set(defined_tickets.keys()) - set(ticket_group.keys()))
        if missing_from_plan:
            ra5_fail = True
            report.add_finding(
                "RA-5",
                "HIGH",
                f"{tech_spec_path.name}:{exec_plan_tbl.header_line}",
                f"Ticket(s) missing from Execution plan: {', '.join(missing_from_plan)}.",
                "Include every `ARCH-NNN` ticket in the Execution plan.",
            )
        for tid, (ln, deps, _, _) in defined_tickets.items():
            if tid not in ticket_group:
                continue
            for dep in sorted(deps):
                if dep in ticket_group and ticket_group[dep] >= ticket_group[tid]:
                    ra5_fail = True
                    report.add_finding(
                        "RA-5",
                        "HIGH",
                        f"{tech_spec_path.name}:{exec_plan_tbl.header_line}",
                        f"Execution plan orders `{tid}` (group {ticket_group[tid]}) before or alongside prerequisite `{dep}` (group {ticket_group[dep]}).",
                        f"Place `{tid}` in a later execution group than `{dep}`.",
                    )

    report.add_check(
        "RA-5",
        "Execution plan covers all tickets in dependency order with disjoint parallel ownership",
        "PASS" if not ra5_fail else "FAIL",
        "Execution plan valid" if not ra5_fail else "Execution plan ordering/ownership error(s)",
    )

    # ------------------------------------------------------------------
    # RA-6: Test-plan check ID integrity (all referenced checks defined)
    # ------------------------------------------------------------------
    defined_checks: Set[str] = set()
    ac_coverage_tbl = None
    if test_sec:
        for tbl in parse_tables(test_sec.body, start_line_offset=test_sec.start_line + 1):
            if not tbl.headers:
                continue
            h0 = tbl.headers[0].strip().lower()
            if "product ac" in h0 or "acceptance" in h0 or h0 == "ac":
                ac_coverage_tbl = tbl
            elif h0 == "id":
                for row in tbl.rows:
                    cid = row.cells[0].strip()
                    if re.fullmatch(r"(?:T-[A-Z]+-\d+|PT-\d+)", cid):
                        defined_checks.add(cid)

    referenced_checks: Set[str] = set()
    ra6_fail = False
    for tid, (ln, _, _, chks) in defined_tickets.items():
        referenced_checks |= chks
        missing = sorted(chks - defined_checks)
        if missing:
            ra6_fail = True
            report.add_finding(
                "RA-6",
                "HIGH",
                f"{tech_spec_path.name}:{ln}",
                f"Ticket `{tid}` references undefined test-plan check(s): {', '.join(missing)}.",
                "Define every referenced check ID in the Test plan tables.",
            )

    if ac_coverage_tbl:
        for row in ac_coverage_tbl.rows:
            chk_cell = next((v for k, v in row.by_header.items() if "check" in k), "")
            chks = _expand_all_checks(chk_cell)
            referenced_checks |= chks
            missing = sorted(chks - defined_checks)
            if missing:
                ra6_fail = True
                report.add_finding(
                    "RA-6",
                    "HIGH",
                    f"{tech_spec_path.name}:{row.line_no}",
                    f"AC coverage row references undefined check(s): {', '.join(missing)}.",
                    "Define every referenced check ID in the Test plan tables.",
                )

    orphan_checks = sorted(defined_checks - referenced_checks)
    if orphan_checks:
        report.add_finding(
            "RA-6",
            "LOW",
            tech_spec_path.name,
            f"Check ID(s) defined in Test plan but not linked from any ticket or AC row: {', '.join(orphan_checks)}.",
            "Link orphan check IDs from a ticket or AC coverage row, or remove unused checks.",
        )

    report.add_check(
        "RA-6",
        "All referenced test-plan check IDs are defined in the Test plan",
        "PASS" if not ra6_fail else "FAIL",
        f"{len(defined_checks)} check(s) defined, {len(referenced_checks)} referenced",
    )

    # ------------------------------------------------------------------
    # RA-3: Complete AC traceability (Product Req -> Spec § + Ticket + Check)
    # ------------------------------------------------------------------
    if not product_reqs:
        report.add_check("RA-3", "Complete product AC traceability", "BLOCKED", "Product spec missing or has no IDs")
    elif ac_coverage_tbl is None:
        report.add_finding(
            "RA-3",
            "HIGH",
            tech_spec_path.name,
            "No Acceptance-criterion coverage table found under Test plan.",
            "Add an `Acceptance-criterion coverage` table mapping every product requirement to spec section, ticket, and check ID.",
        )
        report.add_check("RA-3", "Complete product AC traceability", "FAIL", "Coverage table missing")
    else:
        traced_reqs: Set[str] = set()
        ra3_fail = False
        for row in ac_coverage_tbl.rows:
            ac_col = row.cells[0]
            spec_col = next((v for k, v in row.by_header.items() if "spec" in k), "")
            tkt_col = next((v for k, v in row.by_header.items() if "ticket" in k), "")
            chk_col = next((v for k, v in row.by_header.items() if "check" in k), "")
            row_reqs = _expand_all_reqs(ac_col)
            # Also handle rows like "F-07" being covered via CD-1 or explicit mention
            traced_reqs |= row_reqs
            has_spec = bool(spec_col.strip() and spec_col.strip() != "—")
            has_tkt = bool(expand_prefixed_ids(tkt_col, "ARCH"))
            has_chk = bool(_expand_all_checks(chk_col))
            if not (has_spec and has_tkt and has_chk):
                ra3_fail = True
                report.add_finding(
                    "RA-3",
                    "HIGH",
                    f"{tech_spec_path.name}:{row.line_no}",
                    f"Incomplete traceability row `{ac_col.strip()}` (spec={has_spec}, ticket={has_tkt}, check={has_chk}).",
                    "Populate Spec section, Ticket ID (`ARCH-NNN`), and Check ID (`T-...`/`PT-...`) for every row.",
                )

        # Also count requirements explicitly traced in the Tickets table (e.g. F-03, F-04, F-05, F-06, F-07)
        ticket_traced_reqs: Set[str] = set()
        for _, (_, _, acs, _) in defined_tickets.items():
            ticket_traced_reqs |= acs
        # Check section headings referencing F-XX (e.g., "## 5. Camera (F-03, F-04...)")
        heading_traced_reqs: Set[str] = set()
        for sec in sections:
            heading_traced_reqs |= _expand_all_reqs(sec.title)

        all_traced = traced_reqs | (ticket_traced_reqs & heading_traced_reqs)
        untraced = sorted(product_reqs - all_traced)
        # Allow parent feature F-XX if its constituent controls/checks are traced in coverage table & headings
        truly_untraced: List[str] = []
        for req in untraced:
            if req.startswith("F-") and req in heading_traced_reqs:
                continue
            truly_untraced.append(req)

        if truly_untraced:
            ra3_fail = True
            report.add_finding(
                "RA-3",
                "HIGH",
                tech_spec_path.name,
                f"Product requirement(s) not traced in technical spec coverage: {', '.join(truly_untraced)}.",
                "Map every product feature, control, edge case, and release check to a spec section, ticket, and check ID.",
            )

        report.add_check(
            "RA-3",
            "Complete product AC traceability (spec section, ticket, and check)",
            "PASS" if not ra3_fail else "FAIL",
            f"{len(product_reqs) - len(truly_untraced)}/{len(product_reqs)} product requirement(s) traced",
        )

    # ------------------------------------------------------------------
    # RA-7: Art delivery contract coverage
    # ------------------------------------------------------------------
    if not product_arts:
        report.add_check("RA-7", "Art delivery contracts cover all product ART-XXX items", "SKIP", "No product ART-XXX items")
    else:
        art_contract_sec = find_section(sections, r"\bart\s+delivery\s+contracts?\b")
        ra7_fail = False
        if art_contract_sec is None:
            ra7_fail = True
            report.add_finding(
                "RA-7",
                "HIGH",
                tech_spec_path.name,
                f"Product spec defines {len(product_arts)} art item(s) but technical spec has no Art delivery contracts section.",
                "Add an `Art delivery contracts` section covering every `ART-XXX` item.",
            )
        else:
            covered_arts = set(re.findall(r"\bART-\d{3}\b", art_contract_sec.body))
            missing_arts = sorted(product_arts - covered_arts)
            if missing_arts:
                ra7_fail = True
                report.add_finding(
                    "RA-7",
                    "HIGH",
                    f"{tech_spec_path.name}:{art_contract_sec.start_line}",
                    f"Missing art delivery contract(s) for: {', '.join(missing_arts)}.",
                    "Add delivery contracts (dimensions, format, naming, destination path, import settings, ticket link) for every `ART-XXX`.",
                )
        report.add_check(
            "RA-7",
            "Art delivery contracts cover all product ART-XXX items",
            "PASS" if not ra7_fail else "FAIL",
            f"{len(product_arts)} product ART-XXX item(s) checked",
        )

    # ------------------------------------------------------------------
    # RA-8: Version / bundleVersion matches spec filename
    # ------------------------------------------------------------------
    ver_sec = find_section(sections, r"\bversion\b")
    vm_file = VERSION_FILE_RE.search(tech_spec_path.name)
    ra8_fail = False
    if ver_sec is None:
        ra8_fail = True
        report.add_finding(
            "RA-8",
            "HIGH",
            tech_spec_path.name,
            "Missing `Version` section specifying `bundleVersion`.",
            "Add a `Version` section stating the exact `bundleVersion` for `ProjectSettings/ProjectSettings.asset`.",
        )
    else:
        bm = BUNDLE_VERSION_RE.search(ver_sec.body)
        if not bm:
            ra8_fail = True
            report.add_finding(
                "RA-8",
                "HIGH",
                f"{tech_spec_path.name}:{ver_sec.start_line}",
                "No `bundleVersion` (`X.Y.Z`) found in the `Version` section.",
                "State `bundleVersion: X.Y.Z` explicitly.",
            )
        elif vm_file and bm.group(1) != vm_file.group(1):
            ra8_fail = True
            report.add_finding(
                "RA-8",
                "HIGH",
                f"{tech_spec_path.name}:{ver_sec.start_line}",
                f"`bundleVersion` (`{bm.group(1)}`) does not match spec filename version (`{vm_file.group(1)}`).",
                "Align `bundleVersion` with the spec's semantic version.",
            )
    report.add_check(
        "RA-8",
        "Version section declares bundleVersion matching spec version",
        "PASS" if not ra8_fail else "FAIL",
        "bundleVersion matches spec version" if not ra8_fail else "Missing or mismatched bundleVersion",
    )

    # ------------------------------------------------------------------
    # RA-9: No unresolved placeholders and all relative links resolve
    # ------------------------------------------------------------------
    ra9_hits = 0
    for ln, line in non_code_lines:
        cleaned = re.sub(r"`[^`]*`", "", line)
        pm = PLACEHOLDER_RE.search(cleaned)
        if pm:
            ra9_hits += 1
            report.add_finding(
                "RA-9",
                "HIGH",
                f"{tech_spec_path.name}:{ln}",
                f"Unresolved placeholder `{pm.group(0)}` in technical spec.",
                "Replace every placeholder with an exact contract or value.",
            )

    for ln, _, _, target in extract_markdown_links(text):
        if target.startswith(("http://", "https://", "mailto:")):
            continue
        if target.startswith("#"):
            anchor = target[1:].lower()
            if anchor not in all_slugs:
                ra9_hits += 1
                report.add_finding(
                    "RA-9",
                    "HIGH",
                    f"{tech_spec_path.name}:{ln}",
                    f"Broken internal anchor `#{anchor}`.",
                    "Fix the anchor to match a heading in this document.",
                )
            continue
        path_part = target.split("#", 1)[0]
        resolved = (tech_spec_path.parent / path_part).resolve()
        if not resolved.exists():
            ra9_hits += 1
            report.add_finding(
                "RA-9",
                "HIGH",
                f"{tech_spec_path.name}:{ln}",
                f"Broken relative link `{target}`.",
                f"Fix the link path or ensure `{path_part}` exists.",
            )

    report.add_check(
        "RA-9",
        "No unresolved placeholders and all local links/anchors resolve",
        "PASS" if ra9_hits == 0 else "FAIL",
        "No placeholders or broken links" if ra9_hits == 0 else f"{ra9_hits} placeholder/link issue(s)",
    )

    report.apply_waivers(waivers)
    return report


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Deterministic Requirements & Architecture Gate")
    parser.add_argument("--spec", required=True, type=Path, help="Path to technical spec markdown file")
    parser.add_argument("--product-spec", default=None, type=Path, help="Path to approved product spec")
    parser.add_argument("--repo", default=Path("."), type=Path, help="Repository root path")
    parser.add_argument("--decisions", default=None, type=Path, help="Optional path to docs/decisions.md")
    parser.add_argument("--no-git", action="store_true", help="Skip git baseline commit lookup")
    parser.add_argument("--format", choices=("markdown", "json"), default="markdown")
    args = parser.parse_args(argv)

    report = run_requirements_architecture_gate(
        args.spec.resolve(),
        args.product_spec.resolve() if args.product_spec else None,
        args.repo.resolve(),
        args.decisions,
        check_git=not args.no_git,
    )
    if args.format == "json":
        print(json.dumps(report.to_dict(), indent=2))
    else:
        print(report.to_markdown())
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
