"""Comprehensive deterministic tests for all seven Murim Universe quality gates and CLI runner."""

from __future__ import annotations

import json
import math
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from gates.art_inventory_gate import run_art_inventory_gate
from gates.art_quality_gate import parse_art_contract, run_art_quality_gate
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


def _png_chunk(ctype: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + ctype
        + payload
        + struct.pack(">I", zlib.crc32(ctype + payload) & 0xFFFFFFFF)
    )


def _make_png(
    width: int,
    height: int,
    color_type: int = 6,
    bit_depth: int = 8,
    pixel_fn=None,
    srgb: bool = True,
    text_chunks: list[tuple[str, str]] | None = None,
) -> bytes:
    ihdr = struct.pack(">IIBBBBB", width, height, bit_depth, color_type, 0, 0, 0)
    chunks = [b"\x89PNG\r\n\x1a\n", _png_chunk(b"IHDR", ihdr)]
    if srgb:
        chunks.append(_png_chunk(b"sRGB", b"\x00"))
    if text_chunks:
        for k, v in text_chunks:
            chunks.append(_png_chunk(b"tEXt", k.encode("latin-1") + b"\x00" + v.encode("latin-1")))

    bpp = (4 if color_type == 6 else 3) * (bit_depth // 8)
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # Filter type 0 (None)
        for x in range(width):
            vals = pixel_fn(x, y) if pixel_fn else ((100, 120, 90, 255) if color_type == 6 else (100, 120, 90))
            if bit_depth == 8:
                raw.extend(vals[: (4 if color_type == 6 else 3)])
            else:
                for ch in vals[: (4 if color_type == 6 else 3)]:
                    raw.extend(struct.pack(">H", ch * 257))
    chunks.append(_png_chunk(b"IDAT", zlib.compress(bytes(raw))))
    chunks.append(_png_chunk(b"IEND", b""))
    return b"".join(chunks)


def _fbx_encode_prop(ptype: str, val) -> bytes:
    if ptype == "I":
        return b"I" + struct.pack("<i", val)
    if ptype == "D":
        return b"D" + struct.pack("<d", val)
    if ptype == "S":
        raw = val.encode("utf-8")
        return b"S" + struct.pack("<I", len(raw)) + raw
    if ptype == "d":
        packed = struct.pack(f"<{len(val)}d", *val)
        return b"d" + struct.pack("<III", len(val), 0, len(packed)) + packed
    if ptype == "i":
        packed = struct.pack(f"<{len(val)}i", *val)
        return b"i" + struct.pack("<III", len(val), 0, len(packed)) + packed
    raise ValueError(ptype)


def _fbx_node(name: str, props: list[tuple[str, object]], children: list[tuple], start_off: int) -> bytes:
    name_b = name.encode("utf-8")
    prop_bytes = b"".join(_fbx_encode_prop(t, v) for t, v in props)
    hdr_size = 13 + len(name_b)
    cur = start_off + hdr_size + len(prop_bytes)
    child_bytes = bytearray()
    for ch in children:
        cb = _fbx_node(ch[0], ch[1], ch[2], cur)
        child_bytes.extend(cb)
        cur += len(cb)
    if children:
        child_bytes.extend(b"\x00" * 13)
        cur += 13
    header = struct.pack("<IIIB", cur, len(props), len(prop_bytes), len(name_b)) + name_b
    return header + prop_bytes + bytes(child_bytes)


def _make_fbx(
    height_m: float = 1.8,
    min_y_m: float = -0.5,
    radius_m: float = 1.0,
    num_tris: int = 2,
    quad: bool = False,
    up_axis: int = 1,
    has_normals: bool = True,
    uv_range: tuple[float, float] = (0.0, 1.0),
    mesh_count: int = 1,
    material_count: int = 1,
    has_rig: bool = False,
    has_camera: bool = False,
) -> bytes:
    # UnitScaleFactor 100.0 -> 1 FBX unit = 1 cm -> 1 m = 100 units
    verts = []
    indices = []
    uvs = []
    normals = []
    if quad:
        verts = [0.0, min_y_m * 100, 0.0, radius_m * 100, min_y_m * 100, 0.0, radius_m * 100, height_m * 100, 0.0, 0.0, height_m * 100, 0.0]
        indices = [0, 1, 2, ~3]
        uvs = [uv_range[0], uv_range[0], uv_range[1], uv_range[0], uv_range[1], uv_range[1], uv_range[0], uv_range[1]]
        normals = [0.0, 0.0, 1.0] * 4
    else:
        for t in range(num_tris):
            base = len(verts) // 3
            y_top = height_m * 100 if t == 0 else height_m * 50
            verts.extend([
                0.0, min_y_m * 100, 0.0,
                radius_m * 100, 0.0, 0.0,
                0.0, y_top, 0.0,
            ])
            indices.extend([base, base + 1, ~(base + 2)])
            uvs.extend([uv_range[0], uv_range[0], uv_range[1], uv_range[0], uv_range[0], uv_range[1]])
            normals.extend([0.0, 0.0, 1.0] * 3)

    geom_children = [
        ("Vertices", [("d", verts)], []),
        ("PolygonVertexIndex", [("i", indices)], []),
    ]
    if has_normals:
        geom_children.append(("LayerElementNormal", [("I", 0)], [("Normals", [("d", normals)], [])]))
    if uv_range is not None:
        geom_children.append(("LayerElementUV", [("I", 0)], [("UV", [("d", uvs)], [])]))

    obj_children = []
    for m_idx in range(mesh_count):
        obj_children.append(("Geometry", [("S", f"Mesh{m_idx}"), ("S", "Mesh")], geom_children))
    for mat_idx in range(material_count):
        obj_children.append(("Material", [("S", f"Mat{mat_idx}"), ("S", "")], []))
    if has_rig:
        obj_children.append(("Deformer", [("S", "Skin"), ("S", "Skin")], []))
    if has_camera:
        obj_children.append(("NodeAttribute", [("S", "Cam"), ("S", "Camera")], []))

    gs = (
        "GlobalSettings",
        [],
        [
            (
                "Properties70",
                [],
                [
                    ("P", [("S", "UnitScaleFactor"), ("S", "double"), ("S", "Number"), ("S", ""), ("D", 1.0)], []),
                    ("P", [("S", "UpAxis"), ("S", "int"), ("S", "Integer"), ("S", ""), ("I", up_axis)], []),
                ],
            )
        ],
    )
    objs = ("Objects", [], obj_children)

    header = b"Kaydara FBX Binary  \x00\x1a\x00" + struct.pack("<I", 7400)
    pos = len(header)
    gs_b = _fbx_node(gs[0], gs[1], gs[2], pos)
    pos += len(gs_b)
    objs_b = _fbx_node(objs[0], objs[1], objs[2], pos)
    return header + gs_b + objs_b + (b"\x00" * 13)


def _make_ttf(intervals: list[tuple[int, int]] | None = None, variable: bool = False) -> bytes:
    if intervals is None:
        intervals = [(0x0020, 0x007E)]
    # Build cmap format 4 subtable
    segs = list(intervals) + [(0xFFFF, 0xFFFF)]
    seg_count = len(segs)
    end_codes = struct.pack(f">{seg_count}H", *[e for _, e in segs])
    start_codes = struct.pack(f">{seg_count}H", *[s for s, _ in segs])
    id_delta = struct.pack(f">{seg_count}h", *([0] * seg_count))
    id_range = struct.pack(f">{seg_count}H", *([0] * seg_count))
    sub_len = 16 + len(end_codes) + len(start_codes) + len(id_delta) + len(id_range)
    sub_hdr = struct.pack(">HHHHHHH", 4, sub_len, 0, seg_count * 2, 2, 0, 0)
    cmap_sub = sub_hdr + end_codes + b"\x00\x00" + start_codes + id_delta + id_range
    cmap_tbl = struct.pack(">HH", 0, 1) + struct.pack(">HHI", 3, 1, 12) + cmap_sub

    tables = [(b"cmap", cmap_tbl)]
    if variable:
        tables.append((b"fvar", b"\x00\x01\x00\x00" + b"\x00" * 12))

    num_tables = len(tables)
    dir_end = 12 + num_tables * 16
    records = bytearray()
    body = bytearray()
    cur_off = dir_end
    for tag, tbl_bytes in tables:
        records.extend(struct.pack(">4sIII", tag, 0, cur_off, len(tbl_bytes)))
        body.extend(tbl_bytes)
        cur_off += len(tbl_bytes)
    sfnt_hdr = struct.pack(">4sHHHH", b"\x00\x01\x00\x00", num_tables, 16, 0, 0)
    return sfnt_hdr + bytes(records) + bytes(body)


ART_CONTRACT_SPEC = """# Murim Universe v0.2.0 · Technical spec

## 4. Art delivery contracts

Files go under `Assets/Resources/Art/` with exact names. Credit each family in `Assets/Art/ART_README.md`.

**Common texture rules.** PNG, 8 bits per channel, sRGB, exact pixel sizes as listed. Import: sRGB on, mipmaps on, Trilinear, Aniso 8, compression High Quality, wrap Repeat for seamless textures and Clamp otherwise.

**Common model rules.** FBX (binary), Y-up, one mesh, one material slot, UV0 inside 0–1, vertex normals, triangulated, no rig, animation, cameras, or lights. Each model has one albedo PNG `<model>_albedo.png` beside it. Import: Read/Write off, mesh compression off, materials not imported, animation and rig off. Heights within ±10 %.

### 4.1 ART-001 Ground Turf

- **Destination:** `Assets/Resources/Art/Terrain/`
- **Files (1):** `turf.png`
- **Format:** 16 × 16 RGB, seamless

### 4.2 ART-002 Waterfall & Foam

- **Destination:** `Assets/Resources/Art/Water/`

| Variant | Deliverable |
|---|---|
| Cascade | `water_fall.png`: 16 × 16 RGBA, outer 10 % on each side fading to alpha 0, wrap Clamp |
| Foam | `water_foam.png`: 32 × 32 RGBA, ring centred in the image, fully transparent inside 35 % and outside 48 % of the image width from the centre, wrap Clamp |

### 4.3 ART-003 Horizon Sky

- **Destination:** `Assets/Resources/Art/Backdrop/`

| File | Format | Composition |
|---|---|---|
| `backdrop_sky.png` | 8 × 16 RGB, wrap Clamp | bottom row exactly `#EFD3A0` |
| `backdrop_peaks_far.png` | 16 × 16 RGBA, seamless left-right, wrap Repeat (U) / Clamp (V) | top 10 % fully transparent |
| `backdrop_peaks_near.png` | same | same |

### 4.4 ART-004 UI Frame

- **Destination:** `Assets/Resources/Art/UI/`
- **Import (all):** Sprite (2D and UI), Single, 100 pixels per unit, no mipmaps, wrap Clamp, Bilinear, compression None, alpha is transparency.

| File | Size | 9-slice border | Notes |
|---|---|---|---|
| `overlay_panel.png` | 16 × 16 RGBA | 4 px all sides | centre translucent (alpha 0.85–0.92) |

### 4.5 ART-005 Boulders

- **Destination:** `Assets/Resources/Art/Props/Rocks/`; albedo RGB.

| File | Height | Footprint radius | Triangles | Albedo |
|---|---|---|---|---|
| `rock_boulder.fbx` | 1.8 m | ≤ 1.5 m | ≤ 500 | 16 × 16 |

Geometry continues below the origin by at least 25 % of the height.

### 4.6 ART-006 Overlay Typography

- **Destination:** `Assets/Resources/Art/UI/Fonts/`
- **Typeface:** exactly one static (non-variable) `.ttf` or `.otf` classical serif covering at least U+0020–U+007E, with `<font>-LICENSE.txt` beside it.

### 4.7 ART-007 Lighting Mood

| Variant | Deliverable |
|---|---|
| Golden mood | No image file. Delivered as scene lighting values. |
"""


def _write_valid_art_set(repo: Path) -> None:
    art_root = repo / "Assets" / "Resources" / "Art"
    (art_root / "Terrain").mkdir(parents=True, exist_ok=True)
    (art_root / "Water").mkdir(parents=True, exist_ok=True)
    (art_root / "Backdrop").mkdir(parents=True, exist_ok=True)
    (art_root / "UI" / "Fonts").mkdir(parents=True, exist_ok=True)
    (art_root / "Props" / "Rocks").mkdir(parents=True, exist_ok=True)
    (repo / "Assets" / "Art").mkdir(parents=True, exist_ok=True)
    (repo / "Assets" / "Art" / "ART_README.md").write_text(
        "# Credits\nART-001 ART-002 ART-003 ART-004 ART-005 ART-006\n",
        encoding="utf-8",
    )

    # ART-001 turf.png (16x16 RGB seamless)
    (art_root / "Terrain" / "turf.png").write_bytes(
        _make_png(16, 16, color_type=2, pixel_fn=lambda x, y: (60, 110, 55))
    )
    # ART-002 water_fall.png (16x16 RGBA, outer edges alpha 0)
    (art_root / "Water" / "water_fall.png").write_bytes(
        _make_png(16, 16, color_type=6, pixel_fn=lambda x, y: (200, 220, 240, 0 if x in (0, 1, 14, 15) else 200))
    )
    # ART-002 water_foam.png (32x32 RGBA, ring between 35% and 48% of width=32)
    def foam_px(x: int, y: int):
        d = math.hypot(x + 0.5 - 16.0, y + 0.5 - 16.0)
        a = 220 if (0.35 * 32 <= d <= 0.48 * 32) else 0
        return (240, 245, 250, a)
    (art_root / "Water" / "water_foam.png").write_bytes(_make_png(32, 32, color_type=6, pixel_fn=foam_px))

    # ART-003 backdrop_sky.png (8x16 RGB, bottom row #EFD3A0)
    (art_root / "Backdrop" / "backdrop_sky.png").write_bytes(
        _make_png(8, 16, color_type=2, pixel_fn=lambda x, y: (0xEF, 0xD3, 0xA0) if y == 15 else (180, 140, 90))
    )
    # ART-003 peaks (16x16 RGBA, top 10% = row 0 alpha 0)
    for fn in ("backdrop_peaks_far.png", "backdrop_peaks_near.png"):
        (art_root / "Backdrop" / fn).write_bytes(
            _make_png(16, 16, color_type=6, pixel_fn=lambda x, y: (80, 90, 100, 0 if y < 2 else 220))
        )

    # ART-004 overlay_panel.png + .meta
    panel = art_root / "UI" / "overlay_panel.png"
    panel.write_bytes(_make_png(16, 16, color_type=6, pixel_fn=lambda x, y: (30, 50, 45, 225)))
    Path(str(panel) + ".meta").write_text(
        "fileFormatVersion: 2\n"
        "TextureImporter:\n"
        "  textureType: 8\n"
        "  spriteMode: 1\n"
        "  spritePixelsToUnits: 100\n"
        "  enableMipMap: 0\n"
        "  filterMode: 1\n"
        "  wrapU: 1\n"
        "  wrapV: 1\n"
        "  textureCompression: 0\n"
        "  alphaIsTransparency: 1\n"
        "  sRGBTexture: 1\n"
        "  spriteBorder: {x: 4, y: 4, z: 4, w: 4}\n",
        encoding="utf-8",
    )

    # ART-005 rock_boulder.fbx + rock_boulder_albedo.png + .meta
    fbx_p = art_root / "Props" / "Rocks" / "rock_boulder.fbx"
    fbx_p.write_bytes(_make_fbx(height_m=1.8, min_y_m=-0.5, radius_m=1.0, num_tris=4))
    Path(str(fbx_p) + ".meta").write_text(
        "fileFormatVersion: 2\n"
        "ModelImporter:\n"
        "  isReadable: 0\n"
        "  meshCompression: 0\n"
        "  materialImportMode: 0\n"
        "  importAnimation: 0\n"
        "  animationType: 0\n",
        encoding="utf-8",
    )
    (art_root / "Props" / "Rocks" / "rock_boulder_albedo.png").write_bytes(
        _make_png(16, 16, color_type=2, pixel_fn=lambda x, y: (110, 105, 95))
    )

    # ART-006 font + license
    (art_root / "UI" / "Fonts" / "MurimSerif.ttf").write_bytes(_make_ttf())
    (art_root / "UI" / "Fonts" / "MurimSerif-LICENSE.txt").write_text("SIL Open Font License 1.1\n", encoding="utf-8")


class TestArtQualityGate(unittest.TestCase):
    def test_valid_art_delivery_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "docs" / "specs" / "v0.2.0-technical.md"
            spec.parent.mkdir(parents=True)
            spec.write_text(ART_CONTRACT_SPEC, encoding="utf-8")
            _write_valid_art_set(repo)

            parsed = parse_art_contract(ART_CONTRACT_SPEC)
            # 1 (turf) + 2 (water) + 3 (backdrop) + 1 (ui) + 2 (rock fbx + albedo) + 1 (font) = 10; ART-007 skipped
            self.assertEqual(len(parsed.deliverables), 10)
            self.assertEqual(len(parsed.skipped_non_file_items), 1)

            report = run_art_quality_gate(spec, repo)
            self.assertEqual(report.decision, "PASS", report.to_markdown())
            self.assertEqual(report.exit_code, EXIT_PASS)

    def test_detects_png_dimension_channel_bitdepth_corrupt_and_pixel_failures(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "docs" / "specs" / "v0.2.0-technical.md"
            spec.parent.mkdir(parents=True)
            spec.write_text(ART_CONTRACT_SPEC, encoding="utf-8")
            _write_valid_art_set(repo)

            art_root = repo / "Assets" / "Resources" / "Art"
            # Wrong dimensions + wrong channel (RGBA instead of RGB) + watermarked metadata
            (art_root / "Terrain" / "turf.png").write_bytes(
                _make_png(8, 8, color_type=6, text_chunks=[("Copyright", "Shutterstock Preview")])
            )
            # Wrong bit depth (16-bit) on albedo
            (art_root / "Props" / "Rocks" / "rock_boulder_albedo.png").write_bytes(
                _make_png(16, 16, color_type=2, bit_depth=16)
            )
            # Corrupt PNG on water_fall
            (art_root / "Water" / "water_fall.png").write_bytes(b"not a png")
            # Wrong bottom row hex color on sky
            (art_root / "Backdrop" / "backdrop_sky.png").write_bytes(
                _make_png(8, 16, color_type=2, pixel_fn=lambda x, y: (10, 20, 30))
            )
            # Non-transparent top band on peaks_far
            (art_root / "Backdrop" / "backdrop_peaks_far.png").write_bytes(
                _make_png(16, 16, color_type=6, pixel_fn=lambda x, y: (80, 90, 100, 255))
            )
            # Opaque outside ring on water_foam
            (art_root / "Water" / "water_foam.png").write_bytes(
                _make_png(32, 32, color_type=6, pixel_fn=lambda x, y: (255, 255, 255, 255))
            )

            report = run_art_quality_gate(spec, repo)
            self.assertEqual(report.decision, "FAIL")
            checks = {f.check_id for f in report.findings}
            self.assertTrue({"AQ-3", "AQ-4", "AQ-8"}.issubset(checks), checks)
            self.assertTrue(any(f.severity == "CRITICAL" for f in report.findings))

    def test_detects_fbx_and_font_and_meta_failures(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "docs" / "specs" / "v0.2.0-technical.md"
            spec.parent.mkdir(parents=True)
            spec.write_text(ART_CONTRACT_SPEC, encoding="utf-8")
            _write_valid_art_set(repo)

            art_root = repo / "Assets" / "Resources" / "Art"
            # Bad FBX: Z-up, quad, >500 tris, height wrong, UV > 1, rig + camera, shallow embed
            (art_root / "Props" / "Rocks" / "rock_boulder.fbx").write_bytes(
                _make_fbx(
                    height_m=4.0,
                    min_y_m=-0.05,
                    radius_m=3.0,
                    quad=True,
                    up_axis=2,
                    has_normals=False,
                    uv_range=(0.0, 1.5),
                    mesh_count=2,
                    material_count=2,
                    has_rig=True,
                    has_camera=True,
                )
            )
            # Missing companion albedo
            (art_root / "Props" / "Rocks" / "rock_boulder_albedo.png").unlink()

            # Variable font missing ASCII glyphs + missing license
            (art_root / "UI" / "Fonts" / "MurimSerif.ttf").write_bytes(
                _make_ttf(intervals=[(0x0041, 0x0042)], variable=True)
            )
            (art_root / "UI" / "Fonts" / "MurimSerif-LICENSE.txt").unlink()

            # Wrong .meta on overlay_panel
            (art_root / "UI" / "overlay_panel.png.meta").write_text(
                "TextureImporter:\n"
                "  spritePixelsToUnits: 64\n"
                "  filterMode: 2\n"
                "  wrapU: 0\n"
                "  spriteBorder: {x: 8, y: 8, z: 8, w: 8}\n",
                encoding="utf-8",
            )

            report = run_art_quality_gate(spec, repo, require_meta=True)
            self.assertEqual(report.decision, "FAIL")
            checks = {f.check_id for f in report.findings}
            self.assertTrue({"AQ-2", "AQ-5", "AQ-6", "AQ-7"}.issubset(checks), checks)

    def test_ascii_fbx_and_scoping_and_waiver(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            spec = repo / "docs" / "specs" / "v0.2.0-technical.md"
            spec.parent.mkdir(parents=True)
            spec.write_text(ART_CONTRACT_SPEC, encoding="utf-8")
            _write_valid_art_set(repo)

            # Replace FBX with ASCII FBX
            fbx_p = repo / "Assets" / "Resources" / "Art" / "Props" / "Rocks" / "rock_boulder.fbx"
            fbx_p.write_text("; FBX 7.4.0 project file\nFBXHeaderExtension: {}\n", encoding="utf-8")

            # Scope to ART-005 only -> fails AQ-5
            rep_fail = run_art_quality_gate(spec, repo, art_ids=["ART-005"])
            self.assertEqual(rep_fail.decision, "FAIL")
            self.assertEqual({f.check_id for f in rep_fail.findings}, {"AQ-5"})

            # Waive AQ-F-001
            dec = repo / "docs" / "decisions.md"
            dec.write_text("WAIVED: AQ-F-001 (temporary test waiver)\n", encoding="utf-8")
            rep_waived = run_art_quality_gate(spec, repo, art_ids=["ART-005"], decisions_path=dec)
            self.assertEqual(rep_waived.decision, "PASS", rep_waived.to_markdown())

            # Uncontracted file via --paths (remove temporary waiver first)
            dec.unlink()
            stray = repo / "Assets" / "Resources" / "Art" / "Terrain" / "extra.png"
            stray.write_bytes(_make_png(16, 16, color_type=2))
            rep_stray = run_art_quality_gate(spec, repo, paths=["Assets/Resources/Art/Terrain"])
            self.assertEqual(rep_stray.decision, "FAIL")
            self.assertTrue(any(f.check_id == "AQ-2" and "extra.png" in f.message for f in rep_stray.findings))

    def test_missing_spec_is_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            report = run_art_quality_gate(Path(tmp) / "missing.md", Path(tmp))
            self.assertEqual(report.decision, "BLOCKED")
            self.assertEqual(report.exit_code, EXIT_BLOCKED)

    def test_parses_single_digit_dimensions_inline_albedo_and_height_phrase(self) -> None:
        snippet = """# Technical spec
## 4. Art delivery contracts
Files go under `Assets/Resources/Art/`.
**Common model rules.** FBX (binary), each model has `<model>_albedo.png` beside it.

### 4.1 ART-002 Bank Lip
| Variant | Deliverable |
|---|---|
| Lip | `Props/Banks/bank_lip.fbx` + `bank_lip_albedo.png` (1024 × 1024 RGBA): 3.0 m long (X), height 0.6 m, ≤ 300 triangles |

### 4.2 ART-009 Divider
- **Destination:** `Assets/Resources/Art/UI/`

| File | Size |
|---|---|
| `overlay_divider.png` | 512 × 8 RGBA |
"""
        parsed = parse_art_contract(snippet)
        by_path = {d.rel_path: d for d in parsed.deliverables}
        self.assertIn("Assets/Resources/Art/Props/Banks/bank_lip.fbx", by_path)
        self.assertIn("Assets/Resources/Art/Props/Banks/bank_lip_albedo.png", by_path)
        self.assertNotIn("Assets/Resources/Art/bank_lip_albedo.png", by_path)
        self.assertEqual(by_path["Assets/Resources/Art/Props/Banks/bank_lip.fbx"].model_rule.height_m, 0.6)
        self.assertEqual(by_path["Assets/Resources/Art/Props/Banks/bank_lip_albedo.png"].texture_rule.width, 1024)
        self.assertEqual(by_path["Assets/Resources/Art/UI/overlay_divider.png"].texture_rule.width, 512)
        self.assertEqual(by_path["Assets/Resources/Art/UI/overlay_divider.png"].texture_rule.height, 8)


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
        self.assertIn("art-quality", proc.stdout)
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

            tech_spec = repo / "docs" / "specs" / "v0.2.0-technical.md"
            tech_spec.parent.mkdir(parents=True, exist_ok=True)
            tech_spec.write_text(ART_CONTRACT_SPEC, encoding="utf-8")
            _write_valid_art_set(repo)
            proc3 = subprocess.run(
                [sys.executable, str(runner), "art-quality", "--spec", str(tech_spec), "--repo", str(repo), "--format", "json"],
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(proc3.returncode, 0, proc3.stdout + proc3.stderr)
            payload3 = json.loads(proc3.stdout)
            self.assertEqual(payload3["overall_decision"], "PASS")
            self.assertEqual(payload3["gates"][0]["gate"], "Art Quality")


if __name__ == "__main__":
    unittest.main()
