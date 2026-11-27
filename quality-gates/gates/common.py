"""Shared data structures, parsers, git helpers, and report formatters for deterministic quality gates."""

from __future__ import annotations

import json
import os
import re
import subprocess
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Set, Tuple

EXIT_PASS = 0
EXIT_FAIL = 1
EXIT_BLOCKED = 2

SEVERITY_ORDER = {"CRITICAL": 0, "HIGH": 1, "MEDIUM": 2, "LOW": 3}
BLOCKING_SEVERITIES = {"CRITICAL", "HIGH"}


@dataclass
class Finding:
    id: str
    check_id: str
    severity: str
    location: str
    message: str
    remediation: str
    waived: bool = False
    waiver_ref: str = ""

    def to_dict(self) -> Dict[str, object]:
        return {
            "id": self.id,
            "check_id": self.check_id,
            "severity": self.severity,
            "location": self.location,
            "message": self.message,
            "remediation": self.remediation,
            "waived": self.waived,
            "waiver_ref": self.waiver_ref,
        }


@dataclass
class CheckResult:
    check_id: str
    description: str
    status: str  # PASS | FAIL | SKIP | BLOCKED
    evidence: str = ""

    def to_dict(self) -> Dict[str, object]:
        return {
            "check_id": self.check_id,
            "description": self.description,
            "status": self.status,
            "evidence": self.evidence,
        }


@dataclass
class GateReport:
    gate: str
    target: str
    checks: List[CheckResult] = field(default_factory=list)
    findings: List[Finding] = field(default_factory=list)
    blocked_reasons: List[str] = field(default_factory=list)
    notes: List[str] = field(default_factory=list)
    _finding_prefix: str = "F"
    _finding_seq: int = 0

    def next_finding_id(self) -> str:
        self._finding_seq += 1
        return f"{self._finding_prefix}-F-{self._finding_seq:03d}"

    def add_finding(
        self,
        check_id: str,
        severity: str,
        location: str,
        message: str,
        remediation: str,
        waivers: Optional[Dict[str, str]] = None,
    ) -> Finding:
        sev = severity.upper()
        if sev not in SEVERITY_ORDER:
            raise ValueError(f"Unknown severity: {severity}")
        fid = self.next_finding_id()
        waived = False
        waiver_ref = ""
        if waivers and fid in waivers:
            waived = True
            waiver_ref = waivers[fid]
        finding = Finding(
            id=fid,
            check_id=check_id,
            severity=sev,
            location=location,
            message=message,
            remediation=remediation,
            waived=waived,
            waiver_ref=waiver_ref,
        )
        self.findings.append(finding)
        return finding

    def add_check(self, check_id: str, description: str, status: str, evidence: str = "") -> None:
        self.checks.append(CheckResult(check_id=check_id, description=description, status=status, evidence=evidence))

    def add_blocked(self, reason: str) -> None:
        self.blocked_reasons.append(reason)

    def apply_waivers(self, waivers: Dict[str, str]) -> None:
        if not waivers:
            return
        for f in self.findings:
            if f.id in waivers:
                f.waived = True
                f.waiver_ref = waivers[f.id]

    @property
    def decision(self) -> str:
        has_blocking_finding = any(
            (f.severity in BLOCKING_SEVERITIES and not f.waived) for f in self.findings
        )
        if has_blocking_finding:
            return "FAIL"
        if self.blocked_reasons or any(c.status == "BLOCKED" for c in self.checks):
            return "BLOCKED"
        return "PASS"

    @property
    def exit_code(self) -> int:
        dec = self.decision
        if dec == "PASS":
            return EXIT_PASS
        if dec == "FAIL":
            return EXIT_FAIL
        return EXIT_BLOCKED

    def to_dict(self) -> Dict[str, object]:
        return {
            "gate": self.gate,
            "target": self.target,
            "decision": self.decision,
            "exit_code": self.exit_code,
            "counts": {
                sev: sum(1 for f in self.findings if f.severity == sev and not f.waived)
                for sev in ("CRITICAL", "HIGH", "MEDIUM", "LOW")
            },
            "waived_count": sum(1 for f in self.findings if f.waived),
            "blocked_reasons": list(self.blocked_reasons),
            "notes": list(self.notes),
            "checks": [c.to_dict() for c in self.checks],
            "findings": [f.to_dict() for f in self.findings],
        }

    def to_markdown(self) -> str:
        lines: List[str] = []
        lines.append(f"## Gate: {self.gate}")
        lines.append(f"- **Target:** `{self.target}`")
        lines.append(f"- **Decision:** **{self.decision}** (exit code `{self.exit_code}`)")
        counts = {
            sev: sum(1 for f in self.findings if f.severity == sev and not f.waived)
            for sev in ("CRITICAL", "HIGH", "MEDIUM", "LOW")
        }
        lines.append(
            "- **Findings:** "
            f"Critical={counts['CRITICAL']}, High={counts['HIGH']}, "
            f"Medium={counts['MEDIUM']}, Low={counts['LOW']}"
            + (
                f" (waived={sum(1 for f in self.findings if f.waived)})"
                if any(f.waived for f in self.findings)
                else ""
            )
        )
        if self.blocked_reasons:
            lines.append("")
            lines.append("### Blocked reasons")
            for reason in self.blocked_reasons:
                lines.append(f"- {reason}")
        if self.notes:
            lines.append("")
            lines.append("### Notes")
            for note in self.notes:
                lines.append(f"- {note}")
        if self.checks:
            lines.append("")
            lines.append("### Checks")
            lines.append("| ID | Status | Check | Evidence |")
            lines.append("|---|---|---|---|")
            for c in self.checks:
                ev = c.evidence.replace("|", "\\|").replace("\n", " ")
                desc = c.description.replace("|", "\\|")
                lines.append(f"| {c.check_id} | {c.status} | {desc} | {ev} |")
        if self.findings:
            lines.append("")
            lines.append("### Findings")
            lines.append("| ID | Check | Severity | Location | Problem | Remediation |")
            lines.append("|---|---|---|---|---|---|")
            sorted_findings = sorted(
                self.findings, key=lambda f: (SEVERITY_ORDER.get(f.severity, 99), f.id)
            )
            for f in sorted_findings:
                sev = f"{f.severity} (WAIVED)" if f.waived else f.severity
                loc = f.location.replace("|", "\\|")
                msg = f.message.replace("|", "\\|").replace("\n", " ")
                rem = f.remediation.replace("|", "\\|").replace("\n", " ")
                lines.append(f"| {f.id} | {f.check_id} | {sev} | `{loc}` | {msg} | {rem} |")
        return "\n".join(lines)


# ---------------------------------------------------------------------------
# Waiver loader
# ---------------------------------------------------------------------------

WAIVER_RE = re.compile(r"\bWAIVED\s*:\s*([A-Z]{2}-F-\d{3})\b")


def load_waivers(decisions_path: Optional[Path]) -> Dict[str, str]:
    """Load explicit deterministic waivers (`WAIVED: XX-F-NNN`) from docs/decisions.md."""
    waivers: Dict[str, str] = {}
    if not decisions_path or not decisions_path.is_file():
        return waivers
    text = decisions_path.read_text(encoding="utf-8", errors="replace")
    for idx, line in enumerate(text.splitlines(), start=1):
        for m in WAIVER_RE.finditer(line):
            waivers[m.group(1)] = f"{decisions_path.name}:{idx}"
    return waivers


# ---------------------------------------------------------------------------
# Markdown parsing utilities
# ---------------------------------------------------------------------------

@dataclass
class Section:
    level: int
    title: str
    slug: str
    start_line: int  # 1-based line number of heading
    end_line: int  # 1-based inclusive line number of section body
    body: str  # text after heading line up to next heading of same or higher level


@dataclass
class TableRow:
    line_no: int
    cells: List[str]
    by_header: Dict[str, str]


@dataclass
class MarkdownTable:
    header_line: int
    headers: List[str]
    rows: List[TableRow]


HEADING_RE = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")
FENCE_RE = re.compile(r"^\s*(`{3,}|~{3,})")


def slugify_heading(title: str) -> str:
    """Produce a GitHub-style anchor slug for a Markdown heading."""
    s = title.strip().lower()
    # Strip inline markdown formatting
    s = re.sub(r"`([^`]*)`", r"\1", s)
    s = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", s)
    s = re.sub(r"[*_~]", "", s)
    # Replace spaces with hyphens, strip punctuation except word chars and hyphens
    out_chars: List[str] = []
    for ch in s:
        if ch.isalnum() or ch in ("-", "_", " "):
            out_chars.append("-" if ch == " " else ch)
    return "".join(out_chars)


def strip_fenced_code_blocks(text: str) -> List[Tuple[int, str]]:
    """Return list of (1-based line_no, line) excluding lines inside fenced code blocks."""
    result: List[Tuple[int, str]] = []
    in_fence = False
    fence_char = ""
    fence_len = 0
    for idx, line in enumerate(text.splitlines(), start=1):
        m = FENCE_RE.match(line)
        if m:
            marker = m.group(1)
            if not in_fence:
                in_fence = True
                fence_char = marker[0]
                fence_len = len(marker)
            elif marker[0] == fence_char and len(marker) >= fence_len:
                in_fence = False
            continue
        if not in_fence:
            result.append((idx, line))
    return result


def parse_sections(text: str) -> List[Section]:
    """Parse Markdown headings and their full section bodies (including subsections)."""
    if text.startswith("\ufeff"):
        text = text.lstrip("\ufeff")
    lines = text.splitlines()
    headings: List[Tuple[int, int, str]] = []  # (line_idx_0, level, title)
    in_fence = False
    fence_char = ""
    fence_len = 0
    for idx, line in enumerate(lines):
        m = FENCE_RE.match(line)
        if m:
            marker = m.group(1)
            if not in_fence:
                in_fence = True
                fence_char = marker[0]
                fence_len = len(marker)
            elif marker[0] == fence_char and len(marker) >= fence_len:
                in_fence = False
            continue
        if in_fence:
            continue
        hm = HEADING_RE.match(line)
        if hm:
            headings.append((idx, len(hm.group(1)), hm.group(2).strip()))

    sections: List[Section] = []
    slug_counts: Dict[str, int] = {}
    for i, (start_idx, level, title) in enumerate(headings):
        end_idx = len(lines) - 1
        for j in range(i + 1, len(headings)):
            if headings[j][1] <= level:
                end_idx = headings[j][0] - 1
                break
        base_slug = slugify_heading(title)
        if base_slug in slug_counts:
            slug_counts[base_slug] += 1
            slug = f"{base_slug}-{slug_counts[base_slug]}"
        else:
            slug_counts[base_slug] = 0
            slug = base_slug
        body = "\n".join(lines[start_idx + 1 : end_idx + 1])
        sections.append(
            Section(
                level=level,
                title=title,
                slug=slug,
                start_line=start_idx + 1,
                end_line=end_idx + 1,
                body=body,
            )
        )
    return sections


def find_section(sections: Sequence[Section], *patterns: str) -> Optional[Section]:
    """Return the first section whose title matches any regex pattern (case-insensitive)."""
    for sec in sections:
        for pat in patterns:
            if re.search(pat, sec.title, re.IGNORECASE):
                return sec
    return None


def _split_table_row(line: str) -> List[str]:
    stripped = line.strip()
    if stripped.startswith("|"):
        stripped = stripped[1:]
    if stripped.endswith("|"):
        stripped = stripped[:-1]
    # Split on unescaped pipes
    cells = re.split(r"(?<!\\)\|", stripped)
    return [c.strip().replace("\\|", "|") for c in cells]


TABLE_SEP_RE = re.compile(r"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)+\|?\s*$")


def parse_tables(text: str, start_line_offset: int = 1) -> List[MarkdownTable]:
    """Parse GitHub-flavored Markdown tables outside fenced code blocks."""
    raw_lines = text.splitlines()
    tables: List[MarkdownTable] = []
    in_fence = False
    fence_char = ""
    fence_len = 0
    i = 0
    n = len(raw_lines)
    while i < n:
        line = raw_lines[i]
        m = FENCE_RE.match(line)
        if m:
            marker = m.group(1)
            if not in_fence:
                in_fence = True
                fence_char = marker[0]
                fence_len = len(marker)
            elif marker[0] == fence_char and len(marker) >= fence_len:
                in_fence = False
            i += 1
            continue
        if in_fence:
            i += 1
            continue
        if "|" in line and i + 1 < n and TABLE_SEP_RE.match(raw_lines[i + 1]):
            headers = _split_table_row(line)
            norm_headers = [h.strip().lower() for h in headers]
            header_line = start_line_offset + i
            i += 2
            rows: List[TableRow] = []
            while i < n and "|" in raw_lines[i] and raw_lines[i].strip():
                if FENCE_RE.match(raw_lines[i]):
                    break
                cells = _split_table_row(raw_lines[i])
                # Pad/trim to header length
                while len(cells) < len(norm_headers):
                    cells.append("")
                by_header = {norm_headers[k]: cells[k] for k in range(len(norm_headers))}
                rows.append(TableRow(line_no=start_line_offset + i, cells=cells, by_header=by_header))
                i += 1
            tables.append(MarkdownTable(header_line=header_line, headers=headers, rows=rows))
            continue
        i += 1
    return tables


MD_LINK_RE = re.compile(r"(!?)\[([^\]]*)\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")


def extract_markdown_links(text: str) -> List[Tuple[int, bool, str, str]]:
    """Extract (line_no, is_image, label, target) from Markdown outside code blocks/spans."""
    links: List[Tuple[int, bool, str, str]] = []
    for line_no, line in strip_fenced_code_blocks(text):
        # Remove inline code spans so code examples don't trigger link checks
        cleaned = re.sub(r"`[^`]*`", "", line)
        for m in MD_LINK_RE.finditer(cleaned):
            links.append((line_no, m.group(1) == "!", m.group(2), m.group(3)))
    return links


# ---------------------------------------------------------------------------
# ID expansion utilities (handles CE-1–CE-5, T-CORE-01–06, 09, ARCH-001–003, etc.)
# ---------------------------------------------------------------------------

def expand_prefixed_ids(text: str, prefix: str) -> Set[str]:
    """Expand IDs and ranges with a fixed prefix such as 'CE', 'T-CORE', 'ARCH', 'ART', 'PT'."""
    results: Set[str] = set()
    # Pattern matches:
    #   PREFIX-01
    #   PREFIX-01–PREFIX-05  or  PREFIX-01–05  or  PREFIX-01 ... PREFIX-05
    #   followed optionally by comma-separated shorthand numbers: , 09, 14 (notes), 15
    esc = re.escape(prefix)
    token_re = re.compile(
        rf"\b{esc}-(\d+)"
        rf"(?:\s*(?:[\u2013\u2014\-]|(?:\.\.\.|\u2026))\s*(?:{esc}-)?(\d+))?"
        rf"((?:\s*,\s*(?:\d+(?:\s*[\u2013\u2014\-]\s*\d+)?)(?:\s*\([^)]*\))?)*)"
    )
    for m in token_re.finditer(text):
        start_str = m.group(1)
        end_str = m.group(2)
        tail = m.group(3) or ""
        width = len(start_str)
        start_num = int(start_str)
        if end_str is not None:
            end_num = int(end_str)
            if end_num >= start_num and (end_num - start_num) <= 200:
                for k in range(start_num, end_num + 1):
                    results.add(f"{prefix}-{k:0{width}d}" if width > 1 else f"{prefix}-{k}")
            else:
                results.add(f"{prefix}-{start_str}")
                results.add(f"{prefix}-{end_str}")
        else:
            results.add(f"{prefix}-{start_str}")
        # Parse comma-separated shorthand tail numbers (e.g. ", 09, 14 (camera), 15")
        if tail:
            for item in re.finditer(r"(\d+)(?:\s*[\u2013\u2014\-]\s*(\d+))?", tail):
                a_str = item.group(1)
                b_str = item.group(2)
                a_num = int(a_str)
                w = max(width, len(a_str)) if width > 1 else len(a_str)
                if b_str is not None:
                    b_num = int(b_str)
                    if b_num >= a_num and (b_num - a_num) <= 200:
                        for k in range(a_num, b_num + 1):
                            results.add(f"{prefix}-{k:0{w}d}" if w > 1 else f"{prefix}-{k}")
                else:
                    results.add(f"{prefix}-{a_num:0{w}d}" if w > 1 else f"{prefix}-{a_num}")
    return results


# ---------------------------------------------------------------------------
# Git utilities (argument-array only, never shell=True)
# ---------------------------------------------------------------------------

def run_cmd(argv: Sequence[str], cwd: Path) -> Tuple[int, str, str]:
    """Run a subprocess safely using an argument array."""
    try:
        proc = subprocess.run(
            list(argv),
            cwd=str(cwd),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            shell=False,
        )
        return proc.returncode, proc.stdout, proc.stderr
    except FileNotFoundError:
        return 127, "", f"Command not found: {argv[0]}"


def is_git_repo(repo: Path) -> bool:
    code, out, _ = run_cmd(["git", "rev-parse", "--is-inside-work-tree"], cwd=repo)
    return code == 0 and out.strip() == "true"


def git_commit_exists(repo: Path, sha: str) -> bool:
    code, _, _ = run_cmd(["git", "cat-file", "-e", f"{sha}^{{commit}}"], cwd=repo)
    return code == 0


def git_rev_parse(repo: Path, ref: str) -> Optional[str]:
    code, out, _ = run_cmd(["git", "rev-parse", "--verify", f"{ref}^{{commit}}"], cwd=repo)
    return out.strip() if code == 0 else None


def git_changed_files(repo: Path, base: str, head: str) -> Optional[List[str]]:
    code, out, _ = run_cmd(["git", "diff", "--name-only", "--diff-filter=ACMR", f"{base}...{head}"], cwd=repo)
    if code != 0:
        return None
    return [line.strip() for line in out.splitlines() if line.strip()]


def git_show_file(repo: Path, ref: str, rel_path: str) -> Optional[str]:
    norm = rel_path.replace("\\", "/")
    code, out, _ = run_cmd(["git", "show", f"{ref}:{norm}"], cwd=repo)
    return out if code == 0 else None


# ---------------------------------------------------------------------------
# C# comment/string stripping and method parsing
# ---------------------------------------------------------------------------

def strip_cs_comments_and_strings(source: str) -> str:
    """Replace C# comments and string/char literals with spaces while preserving line numbers."""
    out: List[str] = []
    i = 0
    n = len(source)
    while i < n:
        ch = source[i]
        nxt = source[i + 1] if i + 1 < n else ""
        # Line comment
        if ch == "/" and nxt == "/":
            while i < n and source[i] != "\n":
                out.append(" ")
                i += 1
            continue
        # Block comment
        if ch == "/" and nxt == "*":
            out.append("  ")
            i += 2
            while i < n:
                if source[i] == "*" and i + 1 < n and source[i + 1] == "/":
                    out.append("  ")
                    i += 2
                    break
                out.append("\n" if source[i] == "\n" else " ")
                i += 1
            continue
        # Verbatim string @"..." or $@"..." or @$"..."
        if (ch == "@" and nxt == '"') or (
            ch in ("$", "@") and nxt in ("$", "@") and i + 2 < n and source[i + 2] == '"'
        ):
            start_quote = i + 1 if ch == "@" and nxt == '"' else i + 2
            while i <= start_quote:
                out.append(" ")
                i += 1
            while i < n:
                if source[i] == '"':
                    if i + 1 < n and source[i + 1] == '"':
                        out.append("  ")
                        i += 2
                        continue
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if source[i] == "\n" else " ")
                i += 1
            continue
        # Regular string "..."
        if ch == '"':
            out.append(" ")
            i += 1
            while i < n:
                if source[i] == "\\" and i + 1 < n:
                    out.append("\n" if source[i + 1] == "\n" else "  ")
                    i += 2
                    continue
                if source[i] == '"':
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if source[i] == "\n" else " ")
                i += 1
            continue
        # Char literal '...'
        if ch == "'":
            out.append(" ")
            i += 1
            while i < n:
                if source[i] == "\\" and i + 1 < n:
                    out.append("  ")
                    i += 2
                    continue
                if source[i] == "'":
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if source[i] == "\n" else " ")
                i += 1
            continue
        out.append(ch)
        i += 1
    return "".join(out)


@dataclass
class ParsedMethod:
    name: str
    start_line: int
    end_line: int
    param_count: int
    max_nesting: int
    complexity: int
    body: str


CS_METHOD_HEADER_RE = re.compile(
    r"(?:^|\n)[ \t]*"
    r"(?:(?:public|private|protected|internal|static|virtual|override|sealed|async|extern|unsafe|new|partial)\s+)*"
    r"(?:[\w<>\[\],.?]+\s+)"
    r"([A-Za-z_]\w*)\s*"
    r"(?:<[^>{;]+>)?\s*"
    r"\(([^)]*)\)\s*"
    r"(?:where\s+[^{;]+)?"
    r"\{",
    re.MULTILINE,
)

CS_CONTROL_KEYWORDS = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "else"}


def _count_params(param_str: str) -> int:
    s = param_str.strip()
    if not s:
        return 0
    # Split on commas outside generic angle brackets
    depth = 0
    count = 1
    for ch in s:
        if ch in ("<", "(", "["):
            depth += 1
        elif ch in (">", ")", "]"):
            depth = max(0, depth - 1)
        elif ch == "," and depth == 0:
            count += 1
    return count


def _measure_brace_body(cleaned: str, open_brace_idx: int) -> Tuple[int, int]:
    """Return (close_brace_idx, max_inner_nesting_depth)."""
    depth = 0
    max_depth = 0
    i = open_brace_idx
    n = len(cleaned)
    while i < n:
        ch = cleaned[i]
        if ch == "{":
            depth += 1
            if depth - 1 > max_depth:
                max_depth = depth - 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return i, max_depth
        i += 1
    return n - 1, max_depth


CS_BRANCH_RE = re.compile(
    r"\b(?:if|for|foreach|while|case|catch)\b|&&|\|\||\?\?|\?(?!\.)"
)


def extract_cs_methods(source: str) -> List[ParsedMethod]:
    """Deterministically extract C# methods with line count, params, nesting, and complexity."""
    cleaned = strip_cs_comments_and_strings(source)
    methods: List[ParsedMethod] = []
    for m in CS_METHOD_HEADER_RE.finditer(cleaned):
        name = m.group(1)
        if name in CS_CONTROL_KEYWORDS:
            continue
        open_idx = m.end() - 1
        close_idx, max_nesting = _measure_brace_body(cleaned, open_idx)
        start_line = cleaned[: m.start()].count("\n") + (2 if cleaned[m.start() : m.start() + 1] == "\n" else 1)
        end_line = cleaned[:close_idx].count("\n") + 1
        body = cleaned[open_idx : close_idx + 1]
        complexity = 1 + len(CS_BRANCH_RE.findall(body))
        param_count = _count_params(m.group(2))
        methods.append(
            ParsedMethod(
                name=name,
                start_line=start_line,
                end_line=end_line,
                param_count=param_count,
                max_nesting=max_nesting,
                complexity=complexity,
                body=body,
            )
        )
    return methods


def iter_repo_files(repo: Path, paths: Optional[Sequence[str]] = None, exts: Optional[Set[str]] = None) -> List[Path]:
    """Collect deterministic sorted list of files from explicit paths or the repo."""
    ignore_dirs = {".git", "Library", "Temp", "Obj", "Logs", "UserSettings", "Build", "artifacts", "bin", "obj", "node_modules", "__pycache__", ".venv"}
    found: List[Path] = []
    roots = [repo / p if not Path(p).is_absolute() else Path(p) for p in paths] if paths else [repo]
    for root in roots:
        if root.is_file():
            if exts is None or root.suffix.lower() in exts:
                found.append(root)
        elif root.is_dir():
            for dirpath, dirnames, filenames in os.walk(root):
                dirnames[:] = sorted(d for d in dirnames if d not in ignore_dirs)
                for fn in sorted(filenames):
                    fp = Path(dirpath) / fn
                    if exts is None or fp.suffix.lower() in exts:
                        found.append(fp)
    return sorted(set(found))
