"""Comprehensive deterministic tests for all six Murim Universe quality gates and CLI runner."""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from gates.art_inventory_gate import run_art_inventory_gate
from gates.code_quality_gate import run_code_quality_gate
from gates.common import EXIT_BLOCKED, EXIT_FAIL, EXIT_PASS, expand_prefixed_ids
from gates.correctness_maintainability_gate import run_correctness_maintainability_gate
from gates.product_spec_gate import run_product_spec_gate
from gates.requirements_architecture_gate import run_requirements_architecture_gate
from gates.security_privacy_gate import run_security_privacy_gate


VALID_PRODUCT_SPEC = """# Murim Universe v0.2.0 · Sample spec

**Status:** Approved

**Release goal.** A player can meditate at a shrine and observe a visual aura change.

## Features

| ID | Feature |
|---|---|
| F-01 | Shrine meditation |

## Controls

| ID | Input | Expected behavior |
|---|---|---|
| CE-1 | Press E near shrine | Start meditating and display the aura. |

## Edge cases

| ID | Situation | What the player experiences |
|---|---|---|
| EC-1 | Moving while meditating | Meditation cancels immediately and the aura hides. |

## Art requirements

| ID | Item | Purpose · features | Variants (count) | Disposition | Direction |
|---|---|---|---|---|---|
| ART-001 | Shrine aura | Visual feedback · F-01 | 2 states (idle, active) | New | A · pale jade glow |

### ART-001 · Shrine aura

- **Variants (2):** idle ring, active pulse.
- **Visual requirements:** readable over stone and grass at default zoom.
- **Acceptance:** release check 1 passes.

### Areas that need no art

| Area | Reason |
|---|---|
| Characters | Reuses existing disciple models. |

## Release acceptance

1. **Meditate at shrine.** Stand near the shrine, press E, observe the aura, then move to cancel it.

## Out of scope

- Combat interruptions

## Open questions

None.
"""


VALID_TECH_SPEC = """# Murim Universe v0.2.0 · Technical spec

| | |
|---|---|
| Product spec | [v0.2.0](v0.2.0.md) |
| Implementation baseline | `1234567` |

## 1. Overview

Pure Core meditation state machine and View aura presenter. No networking or save files.
Logs record only event names and no local paths or personal data.
Tunable data lives in `Assets/StreamingAssets/Data/shrine.json` (max 64 KiB, `schemaVersion` 1, fallback to defaults on malformed files).
Developer controls are gated behind `-devlog` and excluded from non-Development release builds.

## 2. Art delivery contracts

### 2.1 ART-001 Shrine aura (integration ARCH-002)

- Path: `Assets/Resources/Art/Shrine/ART-001_aura.png`, 512x512 RGBA PNG.

## 3. Implementation tickets

| ID | Title | Scope and contract | Depends on | ACs | Checks |
|---|---|---|---|---|---|
| ARCH-001 | Core meditation rules | `Core/Shrine/` | None | F-01, CE-1, EC-1 | T-CORE-01 |
| ARCH-002 | View aura presenter | `View/Shrine/`, §2.1 | ARCH-001 | F-01, RC-1 | T-ENG-01, T-ART-01 |

**Execution plan.**

| Group | Tickets | Parallel | Notes |
|---|---|---|---|
| 1 | ARCH-001 | — | Foundation in `Core/Shrine/`. |
| 2 | ARCH-002 | — | Depends on ARCH-001 in `View/Shrine/`. |

## 4. Test plan

### 4.1 Acceptance-criterion coverage

| Product AC | Spec | Ticket | Checks |
|---|---|---|---|
| F-01, CE-1, EC-1 | §1 | ARCH-001 | T-CORE-01 |
| RC-1 | §2.1 | ARCH-002 | T-ENG-01, T-ART-01, PT-1 |

### 4.2 Core tests

| ID | Setup | Expected |
|---|---|---|
| T-CORE-01 | Start and cancel meditation | State toggles cleanly. |

### 4.3 Engine tests

| ID | Setup | Expected |
|---|---|---|
| T-ENG-01 | Press E and move | Aura toggles. |
| T-ART-01 | Check ART-001 | File and import settings valid. |

### 4.4 Owner playtests

| ID | Release check | Pass |
|---|---|---|
| PT-1 | RC-1 | Aura readable over stone and grass. |

## 5. Version

`bundleVersion: 0.2.0`

## 6. Risks and technical decisions

| Topic | Decision |
|---|---|
| Logs | Omit user paths and machine names. |
"""


class TestIdExpansion(unittest.TestCase):
    def test_expand_ranges_and_shorthand(self) -> None:
        self.assertEqual(
            expand_prefixed_ids("T-CORE-01–03, 09, 14 (camera), 15", "T-CORE"),
            {"T-CORE-01", "T-CORE-02", "T-CORE-03", "T-CORE-09", "T-CORE-14", "T-CORE-15"},
        )
        self.assertEqual(
            expand_prefixed_ids("CE-1–CE-4", "CE"),
            {"CE-1", "CE-2", "CE-3", "CE-4"},
        )
        self.assertEqual(
            expand_prefixed_ids("ARCH-001–003", "ARCH"),
            {"ARCH-001", "ARCH-002", "ARCH-003"},
        )


class TestProductSpecGate(unittest.TestCase):
    def test_valid_spec_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "docs" / "specs" / "v0.2.0.md"
            spec.parent.mkdir(parents=True)
            spec.write_text(VALID_PRODUCT_SPEC, encoding="utf-8")
            (repo / "docs" / "decisions.md").write_text("# Decisions\n## v0.2.0\nApproved.\n", encoding="utf-8")

            report = run_product_spec_gate(spec, repo)
            self.assertEqual(report.decision, "PASS", report.to_markdown())
            self.assertEqual(report.exit_code, EXIT_PASS)

    def test_detects_placeholders_dups_impl_leaks_and_dangling_art(self) -> None:
        bad_spec = VALID_PRODUCT_SPEC.replace("Press E near shrine", "Press E etc. TBD").replace(
            "Meditation cancels immediately", "Uses MonoBehaviour in Assets/Scripts/Core/Shrine.cs and ART-999"
        )
        # Add duplicate ID
        bad_spec = bad_spec.replace("| F-01 | Shrine meditation |", "| F-01 | Shrine meditation |\n| F-01 | Duplicate |")
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "v0.2.0.md"
            spec.write_text(bad_spec, encoding="utf-8")

            report = run_product_spec_gate(spec, repo)
            self.assertEqual(report.decision, "FAIL")
            self.assertEqual(report.exit_code, EXIT_FAIL)
            checks = {f.check_id for f in report.findings}
            self.assertTrue({"PS-2", "PS-3", "PS-4", "PS-5", "PS-7"}.issubset(checks), checks)

    def test_missing_file_is_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            report = run_product_spec_gate(Path(tmp) / "missing.md", Path(tmp))
            self.assertEqual(report.decision, "BLOCKED")
            self.assertEqual(report.exit_code, EXIT_BLOCKED)


class TestArtInventoryGate(unittest.TestCase):
    def test_valid_inventory_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "v0.2.0.md"
            spec.write_text(VALID_PRODUCT_SPEC, encoding="utf-8")
            report = run_art_inventory_gate(spec, repo)
            self.assertEqual(report.decision, "PASS", report.to_markdown())

    def test_detects_vague_counts_unresolved_direction_and_forbidden_source(self) -> None:
        bad = VALID_PRODUCT_SPEC.replace("2 states (idle, active)", "several states, etc.").replace(
            "A · pale jade glow", "TBD"
        ).replace(
            "readable over stone and grass at default zoom.", "ship directly from docs/references/ref.png"
        )
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "v0.2.0.md"
            spec.write_text(bad, encoding="utf-8")
            report = run_art_inventory_gate(spec, repo)
            self.assertEqual(report.decision, "FAIL")
            checks = {f.check_id for f in report.findings}
            self.assertTrue({"AI-3", "AI-5", "AI-7"}.issubset(checks), checks)
            self.assertTrue(any(f.severity == "CRITICAL" for f in report.findings))


class TestRequirementsArchitectureGate(unittest.TestCase):
    def test_valid_tech_spec_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            specs_dir = repo / "docs" / "specs"
            specs_dir.mkdir(parents=True)
            prod = specs_dir / "v0.2.0.md"
            tech = specs_dir / "v0.2.0-technical.md"
            prod.write_text(VALID_PRODUCT_SPEC, encoding="utf-8")
            tech.write_text(VALID_TECH_SPEC, encoding="utf-8")

            report = run_requirements_architecture_gate(tech, prod, repo, check_git=False)
            self.assertEqual(report.decision, "PASS", report.to_markdown())

    def test_detects_cycle_untraced_ac_missing_check_and_parallel_overlap(self) -> None:
        bad_tech = (
            VALID_TECH_SPEC
            .replace("| ARCH-001 | Core meditation rules | `Core/Shrine/` | None | F-01, CE-1, EC-1 | T-CORE-01 |",
                     "| ARCH-001 | Core meditation rules | `Core/Shrine/` | ARCH-002 | F-01, CE-1 | T-CORE-01, T-CORE-99 |")
            .replace("| 1 | ARCH-001 | — | Foundation in `Core/Shrine/`. |\n| 2 | ARCH-002 | — | Depends on ARCH-001 in `View/Shrine/`. |",
                     "| 1 | ARCH-001, ARCH-002 | yes | ARCH-001 owns `Core/Shrine/`; ARCH-002 owns `Core/Shrine/Sub.cs`. |")
            .replace("| F-01, CE-1, EC-1 | §1 | ARCH-001 | T-CORE-01 |",
                     "| F-01, CE-1 | §1 | ARCH-001 | T-CORE-01 |")
            .replace("`bundleVersion: 0.2.0`", "`bundleVersion: 0.9.9`")
        )
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            specs_dir = repo / "docs" / "specs"
            specs_dir.mkdir(parents=True)
            prod = specs_dir / "v0.2.0.md"
            tech = specs_dir / "v0.2.0-technical.md"
            prod.write_text(VALID_PRODUCT_SPEC, encoding="utf-8")
            tech.write_text(bad_tech, encoding="utf-8")

            report = run_requirements_architecture_gate(tech, prod, repo, check_git=False)
            self.assertEqual(report.decision, "FAIL")
            checks = {f.check_id for f in report.findings}
            self.assertTrue({"RA-3", "RA-4", "RA-5", "RA-6", "RA-8"}.issubset(checks), checks)


class TestSecurityPrivacyGate(unittest.TestCase):
    def test_detects_secret_local_path_unsafe_deser_and_debug_build(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            src = repo / "Assets" / "Editor" / "BuildScript.cs"
            src.parent.mkdir(parents=True)
            src.write_text(
                'using System;\n'
                'class BuildScript {\n'
                '  string token = "ghp_1234567890abcdefghijklmnopqrstuvwxyz1234";\n'
                '  string log = "C:\\\\Users\\\\johndoe\\\\secret.txt";\n'
                '  void Run() {\n'
                '    var f = new BinaryFormatter();\n'
                '    var opt = BuildOptions.Development;\n'
                '  }\n'
                '}\n',
                encoding="utf-8",
            )
            sh = repo / "tools" / "verify-all.sh"
            sh.parent.mkdir(parents=True)
            sh.write_text("#!/usr/bin/env bash\ndotnet test || true\n", encoding="utf-8")

            report = run_security_privacy_gate(repo, paths=["Assets", "tools"])
            self.assertEqual(report.decision, "FAIL")
            checks = {f.check_id for f in report.findings}
            self.assertTrue({"SP-1", "SP-2", "SP-3", "SP-4", "SP-7"}.issubset(checks), checks)
            # Ensure full secret is NEVER echoed in markdown report
            self.assertNotIn("1234567890abcdefghijklmnopqrstuvwxyz", report.to_markdown())

    def test_waiver_in_decisions_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            p = repo / "notes.md"
            p.write_text("Path: /home/jdoe/file.txt\n", encoding="utf-8")
            dec = repo / "docs" / "decisions.md"
            dec.parent.mkdir(parents=True)
            dec.write_text("Owner waiver: WAIVED: SP-F-001 (intentional fixture).\n", encoding="utf-8")

            report = run_security_privacy_gate(repo, paths=["notes.md"], decisions_path=dec)
            self.assertEqual(report.decision, "PASS", report.to_markdown())
            self.assertTrue(report.findings[0].waived)


class TestCodeQualityGate(unittest.TestCase):
    def test_clean_core_and_view_pass(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            core = repo / "Assets" / "Scripts" / "Core" / "MathUtil.cs"
            core.parent.mkdir(parents=True)
            core.write_text(
                "namespace Murim.Core {\n"
                "  public static class MathUtil {\n"
                "    public static double Clamp(double v, double a, double b) {\n"
                "      if (v < a) return a;\n"
                "      if (v > b) return b;\n"
                "      return v;\n"
                "    }\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            (core.parent / "Murim.Core.asmdef").write_text(
                json.dumps({"name": "Murim.Core", "noEngineReferences": True, "references": []}),
                encoding="utf-8",
            )
            report = run_code_quality_gate(repo, paths=["Assets/Scripts"])
            self.assertEqual(report.decision, "PASS", report.to_markdown())

    def test_detects_layer_violation_hot_linq_and_duplication(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            core = repo / "Assets" / "Scripts" / "Core" / "BadCore.cs"
            core.parent.mkdir(parents=True)
            core.write_text(
                "using UnityEngine;\n"
                "namespace Murim.Core {\n"
                "  public class BadCore {\n"
                "    public void LogIt() { Debug.Log(\"hi\"); }\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            view = repo / "Assets" / "Scripts" / "View" / "BadView.cs"
            view.parent.mkdir(parents=True)
            view.write_text(
                "using System.Linq;\n"
                "using UnityEngine;\n"
                "public class BadView : MonoBehaviour {\n"
                "  int[] arr = new int[4];\n"
                "  void Update() {\n"
                "    var list = arr.Where(x => x > 0).ToList();\n"
                "    var cam = Camera.main;\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            report = run_code_quality_gate(repo, paths=["Assets/Scripts"])
            self.assertEqual(report.decision, "FAIL")
            checks = {f.check_id for f in report.findings}
            self.assertTrue({"CQ-1", "CQ-2", "CQ-8"}.issubset(checks), checks)


class TestCorrectnessMaintainabilityGate(unittest.TestCase):
    def test_detects_nondeterminism_layer_name_locale_catch_json_and_disabled_tests(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            core = repo / "Assets" / "Scripts" / "Core" / "Gen.cs"
            core.parent.mkdir(parents=True)
            core.write_text(
                "using System;\n"
                "namespace Murim.Core {\n"
                "  public class Gen {\n"
                "    public double Next() {\n"
                "      var r = new Random();\n"
                "      var t = DateTime.UtcNow;\n"
                "      var x = Math.Sin(1.0);\n"
                "      var f = float.Parse(\"1.5\");\n"
                "      try { return x + f; } catch (Exception) { }\n"
                "      return 0;\n"
                "    }\n"
                "  }\n"
                "}\n",
                encoding="utf-8",
            )
            view = repo / "Assets" / "Scripts" / "View" / "Picker.cs"
            view.parent.mkdir(parents=True)
            view.write_text(
                "using UnityEngine;\n"
                "public class Picker : MonoBehaviour {\n"
                "  void Start() { int w = LayerMask.NameToLayer(\"Water\"); }\n"
                "}\n",
                encoding="utf-8",
            )
            bad_json = repo / "Assets" / "StreamingAssets" / "Data" / "water.json"
            bad_json.parent.mkdir(parents=True)
            bad_json.write_text('{"flowSpeed": 1.0}', encoding="utf-8")
            test_cs = repo / "tests" / "Core" / "GenTests.cs"
            test_cs.parent.mkdir(parents=True)
            test_cs.write_text(
                "public class GenTests {\n"
                "  [Ignore(\"broken\")]\n"
                "  public void TestOne() {}\n"
                "}\n",
                encoding="utf-8",
            )

            report = run_correctness_maintainability_gate(repo)
            self.assertEqual(report.decision, "FAIL")
            checks = {f.check_id for f in report.findings}
            self.assertTrue({"RC-1", "RC-2", "RC-3", "RC-4", "RC-5", "RC-7"}.issubset(checks), checks)
            self.assertTrue(any(f.check_id == "RC-1" and f.severity == "CRITICAL" for f in report.findings))


class TestCliRunner(unittest.TestCase):
    def test_cli_list_and_json_output(self) -> None:
        runner = ROOT / "run_gates.py"
        proc = subprocess.run(
            [sys.executable, str(runner), "--list"],
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(proc.returncode, 0)
        self.assertIn("product-spec", proc.stdout)
        self.assertIn("correctness-maintainability", proc.stdout)

        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "v0.2.0.md"
            spec.write_text(VALID_PRODUCT_SPEC, encoding="utf-8")
            proc2 = subprocess.run(
                [sys.executable, str(runner), "product-spec", "art-inventory", "--spec", str(spec), "--repo", str(repo), "--format", "json"],
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(proc2.returncode, 0, proc2.stdout + proc2.stderr)
            payload = json.loads(proc2.stdout)
            self.assertEqual(payload["overall_decision"], "PASS")
            self.assertEqual(len(payload["gates"]), 2)


if __name__ == "__main__":
    unittest.main()
