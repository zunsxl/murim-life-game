"""Deterministic Security & Privacy Gate (SP-1 .. SP-8)."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Tuple

from .common import (
    GateReport,
    git_changed_files,
    git_rev_parse,
    is_git_repo,
    iter_repo_files,
    load_waivers,
    strip_cs_comments_and_strings,
    strip_fenced_code_blocks,
)

# High-confidence secret patterns (matched values are ALWAYS redacted to first 4 chars)
SECRET_PATTERNS: List[Tuple[re.Pattern[str], str]] = [
    (re.compile(r"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----"), "private key block"),
    (re.compile(r"\bAKIA[0-9A-Z]{16}\b"), "AWS access key ID"),
    (re.compile(r"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{36,}\b"), "GitHub token"),
    (re.compile(r"\bgithub_pat_[A-Za-z0-9_]{20,}\b"), "GitHub fine-grained PAT"),
    (re.compile(r"\bsk-[A-Za-z0-9]{20,}\b"), "API secret key"),
    (re.compile(r"\bAIza[0-9A-Za-z\-_]{35}\b"), "Google API key"),
    (re.compile(r"\bxox[baprs]-[0-9A-Za-z\-]{10,}\b"), "Slack token"),
    (
        re.compile(
            r"""(?i)\b(?:api[_-]?key|secret[_-]?key|access[_-]?token|auth[_-]?token|password|passwd|client[_-]?secret)\s*[:=]\s*["']([^"'\s]{12,})["']"""
        ),
        "hardcoded secret literal",
    ),
]

# Local user path / PII patterns (skip angle-bracket placeholders like C:\Users\<name>)
LOCAL_PATH_RE = re.compile(
    r"""(?:[A-Za-z]:\\{1,2}Users\\{1,2}(?![<\[])[^\s\\"'`<>]+|/(?:Users|home)/(?![<\[])[^\s/"'`<>]+)"""
)
PII_CODE_RE = re.compile(
    r"\b(?:Environment\.UserName|Environment\.MachineName|Environment\.UserDomainName|SystemInfo\.deviceUniqueIdentifier)\b"
)

# Unsafe deserialization & shell injection patterns
UNSAFE_CS_PATTERNS: List[Tuple[re.Pattern[str], str, str]] = [
    (re.compile(r"\b(?:BinaryFormatter|SoapFormatter|NetDataContractSerializer|LosFormatter)\b"), "CRITICAL", "unsafe .NET formatter deserialization"),
    (re.compile(r"\bTypeNameHandling\.(?:All|Auto|Objects|Arrays)\b"), "CRITICAL", "unsafe polymorphic JSON deserialization"),
    (re.compile(r"\bUseShellExecute\s*=\s*true\b"), "HIGH", "process execution with UseShellExecute=true"),
    (re.compile(r"""\bProcess\.Start\s*\(\s*(?:"(?:cmd|powershell|bash|sh)"|\$")"""), "HIGH", "shell command execution via Process.Start"),
]

UNSAFE_SHELL_PATTERNS: List[Tuple[re.Pattern[str], str, str]] = [
    (re.compile(r"(?:^|[;&|]\s*)eval\s+"), "CRITICAL", "shell eval execution"),
    (re.compile(r"\b(?:Invoke-Expression|iex)\s+"), "CRITICAL", "PowerShell dynamic code execution"),
    (re.compile(r"\b(?:curl|wget)\b[^|\n]*\|\s*(?:ba)?sh\b"), "CRITICAL", "piping remote content to shell"),
]

DEBUG_BUILD_FLAG_RE = re.compile(
    r"\bBuildOptions\.(?:Development|AllowDebugging|ConnectWithProfiler|EnableDeepProfilingSupport)\b"
)

TEXT_SCAN_EXTS = {
    ".md", ".cs", ".json", ".xml", ".csproj", ".props", ".targets",
    ".sh", ".ps1", ".bat", ".cmd", ".py", ".js", ".ts", ".yml", ".yaml",
    ".txt", ".cfg", ".ini", ".shader", ".hlsl", ".asmdef",
}


def _redact(secret: str) -> str:
    prefix = secret[:4] if len(secret) >= 4 else secret
    return f"{prefix}****"


def _collect_target_files(
    repo: Path,
    spec_path: Optional[Path],
    base: Optional[str],
    head: Optional[str],
    paths: Optional[Sequence[str]],
    report: GateReport,
) -> List[Path]:
    files: List[Path] = []
    if base or head:
        if not (base and head):
            report.add_blocked("Both --base and --head are required for git diff scanning.")
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
        for rel in changed:
            fp = repo / rel
            if fp.is_file() and fp.suffix.lower() in TEXT_SCAN_EXTS:
                files.append(fp)
    elif paths:
        files.extend(iter_repo_files(repo, paths=paths, exts=TEXT_SCAN_EXTS))
    elif spec_path is None:
        files.extend(iter_repo_files(repo, exts=TEXT_SCAN_EXTS))

    if spec_path and spec_path.is_file() and spec_path not in files:
        files.insert(0, spec_path)
    return files


def run_security_privacy_gate(
    repo: Path,
    spec_path: Optional[Path] = None,
    base: Optional[str] = None,
    head: Optional[str] = None,
    paths: Optional[Sequence[str]] = None,
    decisions_path: Optional[Path] = None,
) -> GateReport:
    target_desc = str(spec_path) if spec_path else (f"{base}...{head}" if base and head else str(repo))
    report = GateReport(
        gate="Security & Privacy",
        target=target_desc,
        _finding_prefix="SP",
    )

    if spec_path is not None and not spec_path.is_file():
        report.add_blocked(f"Spec file not found: {spec_path}")
        report.add_check("SP-1", "Secret scan", "BLOCKED", f"Missing file: {spec_path}")
        return report

    if decisions_path is None:
        candidate = repo / "docs" / "decisions.md"
        if candidate.is_file():
            decisions_path = candidate
    waivers = load_waivers(decisions_path)

    scan_files = _collect_target_files(repo, spec_path, base, head, paths, report)
    if report.blocked_reasons:
        report.add_check("SP-1", "Secret scan", "BLOCKED", "; ".join(report.blocked_reasons))
        return report

    # ------------------------------------------------------------------
    # SP-1: Secret scanning
    # ------------------------------------------------------------------
    sp1_hits = 0
    for fp in scan_files:
        rel = str(fp.relative_to(repo)) if fp.is_relative_to(repo) else str(fp)
        text = fp.read_text(encoding="utf-8", errors="replace")
        for ln, line in enumerate(text.splitlines(), start=1):
            for pat, desc in SECRET_PATTERNS:
                m = pat.search(line)
                if m:
                    val = m.group(1) if m.lastindex else m.group(0)
                    # Ignore obvious dummy/example placeholders
                    if re.fullmatch(r"(?:example|dummy|placeholder|changeme|your[_-].+|xxx+|000+)", val, re.IGNORECASE):
                        continue
                    sp1_hits += 1
                    report.add_finding(
                        "SP-1",
                        "CRITICAL",
                        f"{rel}:{ln}",
                        f"Potential {desc} detected (`{_redact(val)}`).",
                        "Remove the secret from tracked files, rotate it if live, and inject credentials outside the repo.",
                    )

    report.add_check(
        "SP-1",
        "No live secrets, tokens, or private keys in scanned files",
        "PASS" if sp1_hits == 0 else "FAIL",
        f"Scanned {len(scan_files)} file(s); {sp1_hits} match(es)",
    )

    # ------------------------------------------------------------------
    # SP-2: Local user paths & personal data leaks
    # ------------------------------------------------------------------
    sp2_hits = 0
    for fp in scan_files:
        rel = str(fp.relative_to(repo)) if fp.is_relative_to(repo) else str(fp)
        text = fp.read_text(encoding="utf-8", errors="replace")
        for ln, line in enumerate(text.splitlines(), start=1):
            pm = LOCAL_PATH_RE.search(line)
            if pm:
                matched = pm.group(0)
                # Ignore obvious generic documentation examples like /Users/alice or C:\Users\example
                if re.search(r"[/\\](?:example|user|username|name|alice|bob|player)(?:[/\\]|$)", matched, re.IGNORECASE):
                    continue
                sp2_hits += 1
                report.add_finding(
                    "SP-2",
                    "HIGH",
                    f"{rel}:{ln}",
                    f"Hardcoded local user path `{matched}` leaks machine/user identity.",
                    "Replace local user paths with repository-relative paths or placeholders like `<user>`.",
                )
            if fp.suffix.lower() == ".cs" and "tests" not in rel.lower().replace("\\", "/").split("/"):
                cm = PII_CODE_RE.search(line)
                if cm:
                    sp2_hits += 1
                    report.add_finding(
                        "SP-2",
                        "HIGH",
                        f"{rel}:{ln}",
                        f"Personal/device identifier API `{cm.group(0)}` referenced in runtime code.",
                        "Do not collect or log usernames, machine names, or device IDs.",
                    )

    report.add_check(
        "SP-2",
        "No hardcoded local machine/user paths or personal data collection",
        "PASS" if sp2_hits == 0 else "FAIL",
        f"Scanned {len(scan_files)} file(s); {sp2_hits} match(es)",
    )

    # ------------------------------------------------------------------
    # SP-3: Unsafe deserialization & command/shell injection sinks
    # ------------------------------------------------------------------
    sp3_hits = 0
    code_files = [f for f in scan_files if f.suffix.lower() in {".cs", ".sh", ".ps1", ".bat", ".cmd"}]
    for fp in code_files:
        rel = str(fp.relative_to(repo)) if fp.is_relative_to(repo) else str(fp)
        raw = fp.read_text(encoding="utf-8", errors="replace")
        if fp.suffix.lower() == ".cs":
            cleaned = strip_cs_comments_and_strings(raw)
            for ln, (raw_line, clean_line) in enumerate(zip(raw.splitlines(), cleaned.splitlines()), start=1):
                for pat, sev, desc in UNSAFE_CS_PATTERNS:
                    target_line = raw_line if "Process.Start" in desc else clean_line
                    if pat.search(target_line):
                        sp3_hits += 1
                        report.add_finding(
                            "SP-3",
                            sev,
                            f"{rel}:{ln}",
                            f"Detected {desc}.",
                            "Use safe typed serialization (`JsonUtility`) and argument-array process invocation (`UseShellExecute = false`).",
                        )
        else:
            for ln, line in enumerate(raw.splitlines(), start=1):
                stripped = line.lstrip()
                if stripped.startswith("#"):
                    continue
                for pat, sev, desc in UNSAFE_SHELL_PATTERNS:
                    if pat.search(line):
                        sp3_hits += 1
                        report.add_finding(
                            "SP-3",
                            sev,
                            f"{rel}:{ln}",
                            f"Detected {desc} in script.",
                            "Avoid `eval`/`Invoke-Expression` and invoke commands with explicit argument arrays.",
                        )

    if code_files:
        report.add_check(
            "SP-3",
            "No unsafe deserialization or shell injection sinks in code/scripts",
            "PASS" if sp3_hits == 0 else "FAIL",
            f"Checked {len(code_files)} code/script file(s); {sp3_hits} issue(s)",
        )
    else:
        report.add_check("SP-3", "No unsafe deserialization or shell injection sinks in code/scripts", "SKIP", "No code/script files in target")

    # ------------------------------------------------------------------
    # SP-4: Release build & debug feature gating
    # ------------------------------------------------------------------
    sp4_hits = 0
    sp4_checked = False
    if spec_path and spec_path.is_file():
        sp4_checked = True
        spec_text = spec_path.read_text(encoding="utf-8", errors="replace")
        mentions_dev = bool(re.search(r"\b(?:dev\s+console|seed\s+panel|-devlog|debug\s+command|cheat)\b", spec_text, re.IGNORECASE))
        if mentions_dev:
            has_gate = bool(
                re.search(
                    r"(?:non-development|gated|release\s+build|UNITY_EDITOR|-devlog|disabled\s+in\s+release|no\s+debugger)",
                    spec_text,
                    re.IGNORECASE,
                )
            )
            if not has_gate:
                sp4_hits += 1
                report.add_finding(
                    "SP-4",
                    "HIGH",
                    spec_path.name,
                    "Technical spec defines developer/debug controls without specifying release-build gating.",
                    "Document explicitly how debug/developer controls are gated or disabled in release builds.",
                )

    build_scripts = [
        f for f in scan_files
        if f.suffix.lower() == ".cs"
        and "buildscript" in f.name.lower()
        and "tests" not in str(f).lower().replace("\\", "/").split("/")
    ]
    for fp in build_scripts:
        sp4_checked = True
        rel = str(fp.relative_to(repo)) if fp.is_relative_to(repo) else str(fp)
        cleaned = strip_cs_comments_and_strings(fp.read_text(encoding="utf-8", errors="replace"))
        for ln, line in enumerate(cleaned.splitlines(), start=1):
            m = DEBUG_BUILD_FLAG_RE.search(line)
            if m:
                sp4_hits += 1
                report.add_finding(
                    "SP-4",
                    "HIGH",
                    f"{rel}:{ln}",
                    f"Release build script references debug flag `{m.group(0)}`.",
                    "Keep Development, AllowDebugging, and Profiler flags out of release player builds.",
                )

    report.add_check(
        "SP-4",
        "Debug/developer features gated out of release builds",
        "SKIP" if not sp4_checked else ("PASS" if sp4_hits == 0 else "FAIL"),
        "No spec or build script in target" if not sp4_checked else f"{sp4_hits} gating issue(s)",
    )

    # ------------------------------------------------------------------
    # SP-5: Untrusted input / JSON configuration bounds
    # ------------------------------------------------------------------
    sp5_hits = 0
    sp5_checked = False
    if spec_path and spec_path.is_file():
        sp5_checked = True
        spec_text = spec_path.read_text(encoding="utf-8", errors="replace")
        mentions_json = bool(re.search(r"\b(?:StreamingAssets/Data|\.json|save\s+file|import\s+file|mod\s+file)\b", spec_text, re.IGNORECASE))
        if mentions_json:
            has_limit = bool(re.search(r"\b\d+\s*(?:KiB|KB|MiB|MB|bytes?)\b", spec_text, re.IGNORECASE))
            has_schema = bool(re.search(r"\b(?:schemaVersion|schema\s+version|format\s+version)\b", spec_text, re.IGNORECASE))
            has_fallback = bool(re.search(r"\b(?:fall\s*back|default|reject|malformed)\b", spec_text, re.IGNORECASE))
            if not (has_limit and has_schema and has_fallback):
                sp5_hits += 1
                report.add_finding(
                    "SP-5",
                    "HIGH",
                    spec_path.name,
                    f"Spec loads external/JSON data but lacks complete bounds (size_limit={has_limit}, schema_version={has_schema}, fallback={has_fallback}).",
                    "Specify explicit file size limits, `schemaVersion` validation, and fail-safe fallback/rejection behavior.",
                )

    data_jsons = [
        f for f in scan_files
        if f.suffix.lower() == ".json" and "streamingassets/data" in str(f).lower().replace("\\", "/")
    ]
    for fp in data_jsons:
        sp5_checked = True
        rel = str(fp.relative_to(repo)) if fp.is_relative_to(repo) else str(fp)
        size = fp.stat().st_size
        if size > 65536:
            sp5_hits += 1
            report.add_finding(
                "SP-5",
                "HIGH",
                rel,
                f"Data JSON file exceeds 64 KiB cap ({size} bytes).",
                "Keep tunable JSON files at or below 65536 bytes.",
            )
        try:
            obj = json.loads(fp.read_text(encoding="utf-8"))
            if not isinstance(obj, dict) or not isinstance(obj.get("schemaVersion"), int):
                sp5_hits += 1
                report.add_finding(
                    "SP-5",
                    "HIGH",
                    rel,
                    "Data JSON file is missing an integer `schemaVersion` field.",
                    "Include an integer `schemaVersion` key in every StreamingAssets/Data JSON file.",
                )
        except json.JSONDecodeError as exc:
            sp5_hits += 1
            report.add_finding(
                "SP-5",
                "HIGH",
                rel,
                f"Malformed JSON file: {exc}.",
                "Fix the JSON syntax error.",
            )

    report.add_check(
        "SP-5",
        "Untrusted input & JSON config bounds (size cap, schemaVersion, fallback)",
        "SKIP" if not sp5_checked else ("PASS" if sp5_hits == 0 else "FAIL"),
        "No spec or data JSON in target" if not sp5_checked else f"{sp5_hits} bound/schema issue(s)",
    )

    # ------------------------------------------------------------------
    # SP-6: Dependency version pinning
    # ------------------------------------------------------------------
    sp6_hits = 0
    dep_files = [
        f for f in scan_files
        if f.name.lower() in {"manifest.json", "global.json", "package.json", "requirements.txt"}
        or f.suffix.lower() == ".csproj"
    ]
    for fp in dep_files:
        rel = str(fp.relative_to(repo)) if fp.is_relative_to(repo) else str(fp)
        text = fp.read_text(encoding="utf-8", errors="replace")
        if fp.name.lower() == "manifest.json" and "packages" in rel.lower().replace("\\", "/"):
            try:
                data = json.loads(text)
                for pkg, ver in data.get("dependencies", {}).items():
                    if not isinstance(ver, str) or ver.strip() in {"*", "latest", ""} or ver.startswith(("^", "~")):
                        sp6_hits += 1
                        report.add_finding(
                            "SP-6",
                            "HIGH",
                            rel,
                            f"Unity package `{pkg}` has unpinned version `{ver}`.",
                            "Pin every Unity package to an exact version.",
                        )
            except json.JSONDecodeError:
                sp6_hits += 1
                report.add_finding("SP-6", "HIGH", rel, "Malformed `Packages/manifest.json`.", "Fix JSON syntax.")
        elif fp.suffix.lower() == ".csproj":
            for ln, line in enumerate(text.splitlines(), start=1):
                m = re.search(r'<PackageReference\s+[^>]*Include="([^"]+)"[^>]*Version="([^"]+)"', line)
                if m:
                    pkg, ver = m.group(1), m.group(2)
                    if "*" in ver or ver.startswith(("[", "(")) and "," in ver:
                        sp6_hits += 1
                        report.add_finding(
                            "SP-6",
                            "HIGH",
                            f"{rel}:{ln}",
                            f"NuGet package `{pkg}` uses floating version `{ver}`.",
                            "Pin NuGet package versions exactly and commit `packages.lock.json`.",
                        )
        elif fp.name.lower() == "requirements.txt":
            for ln, line in enumerate(text.splitlines(), start=1):
                s = line.strip()
                if not s or s.startswith("#") or s.startswith("-"):
                    continue
                if "==" not in s:
                    sp6_hits += 1
                    report.add_finding(
                        "SP-6",
                        "HIGH",
                        f"{rel}:{ln}",
                        f"Unpinned Python requirement `{s}`.",
                        "Pin exact versions using `==`.",
                    )

    report.add_check(
        "SP-6",
        "Dependencies are pinned to exact versions",
        "SKIP" if not dep_files else ("PASS" if sp6_hits == 0 else "FAIL"),
        f"Checked {len(dep_files)} manifest(s); {sp6_hits} unpinned",
    )

    # ------------------------------------------------------------------
    # SP-7: Verification & tooling script integrity
    # ------------------------------------------------------------------
    sp7_hits = 0
    sh_scripts = [
        f for f in scan_files
        if f.suffix.lower() == ".sh" and "tools" in str(f).lower().replace("\\", "/").split("/")
    ]
    for fp in sh_scripts:
        rel = str(fp.relative_to(repo)) if fp.is_relative_to(repo) else str(fp)
        text = fp.read_text(encoding="utf-8", errors="replace")
        if not re.search(r"set\s+-[a-z]*e[a-z]*u[a-z]*o\s+pipefail|set\s+-euo\s+pipefail", text):
            sp7_hits += 1
            report.add_finding(
                "SP-7",
                "HIGH",
                f"{rel}:1",
                "Tooling shell script does not enable `set -euo pipefail`.",
                "Add `set -euo pipefail` near the top of the script so failures cannot be silently ignored.",
            )
        for ln, line in enumerate(text.splitlines(), start=1):
            if re.search(r"\b(?:dotnet\s+test|pytest|unity)\b.*\|\|\s*true\b", line, re.IGNORECASE):
                sp7_hits += 1
                report.add_finding(
                    "SP-7",
                    "CRITICAL",
                    f"{rel}:{ln}",
                    "Verification script masks test/tool failure with `|| true`.",
                    "Propagate non-zero exit codes; never turn a failure into a pass.",
                )

    report.add_check(
        "SP-7",
        "Verification/tooling scripts enforce strict error propagation",
        "SKIP" if not sh_scripts else ("PASS" if sp7_hits == 0 else "FAIL"),
        f"Checked {len(sh_scripts)} tooling shell script(s); {sp7_hits} issue(s)",
    )

    # ------------------------------------------------------------------
    # SP-8: Spec trust-boundary & privacy coverage
    # ------------------------------------------------------------------
    if spec_path and spec_path.is_file():
        spec_text = spec_path.read_text(encoding="utf-8", errors="replace")
        has_surface_stmt = bool(
            re.search(r"\b(?:networking|save\s+data|mod\s+surface|trust\s+boundar|untrusted|StreamingAssets/Data)\b", spec_text, re.IGNORECASE)
        )
        has_log_privacy = bool(
            re.search(r"\b(?:log|Player\.log|personal\s+data|local\s+paths|user\s+or\s+machine\s+names)\b", spec_text, re.IGNORECASE)
        )
        if not (has_surface_stmt and has_log_privacy):
            report.add_finding(
                "SP-8",
                "MEDIUM",
                spec_path.name,
                f"Technical spec does not explicitly document both external surfaces ({has_surface_stmt}) and log/privacy rules ({has_log_privacy}).",
                "State external input surfaces and confirm logs/artifacts omit local paths and personal data.",
            )
            report.add_check("SP-8", "Spec documents trust boundaries and log privacy", "FAIL", "Missing surface or log-privacy statement")
        else:
            report.add_check("SP-8", "Spec documents trust boundaries and log privacy", "PASS", "External surfaces and log privacy documented")
    else:
        report.add_check("SP-8", "Spec documents trust boundaries and log privacy", "SKIP", "No technical spec provided")

    report.apply_waivers(waivers)
    return report


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Deterministic Security & Privacy Gate")
    parser.add_argument("--spec", default=None, type=Path, help="Optional path to technical spec")
    parser.add_argument("--repo", default=Path("."), type=Path, help="Repository root path")
    parser.add_argument("--base", default=None, help="Base git revision for diff scan")
    parser.add_argument("--head", default=None, help="Head git revision for diff scan")
    parser.add_argument("--paths", nargs="*", default=None, help="Explicit file/directory paths to scan")
    parser.add_argument("--decisions", default=None, type=Path, help="Optional path to docs/decisions.md")
    parser.add_argument("--format", choices=("markdown", "json"), default="markdown")
    args = parser.parse_args(argv)

    report = run_security_privacy_gate(
        repo=args.repo.resolve(),
        spec_path=args.spec.resolve() if args.spec else None,
        base=args.base,
        head=args.head,
        paths=args.paths,
        decisions_path=args.decisions,
    )
    if args.format == "json":
        print(json.dumps(report.to_dict(), indent=2))
    else:
        print(report.to_markdown())
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
