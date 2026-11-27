"""Deterministic Art Quality Gate (AQ-1 .. AQ-8).

Compares supplied art assets (PNG textures/sprites, binary FBX 3D models,
TTF/OTF fonts, and optional Unity .meta files) against an Art Delivery Contract
using Python 3 standard library only.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import struct
import sys
import zlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Set, Tuple

from .common import (
    GateReport,
    find_section,
    load_waivers,
    parse_sections,
    parse_tables,
    strip_fenced_code_blocks,
)

ART_ID_RE = re.compile(r"\b(ART-\d{3})\b")
ASSET_TOKEN_RE = re.compile(r"`([^`]+?\.(?:png|fbx|ttf|otf))`", re.IGNORECASE)
DIM_RE = re.compile(r"(\d{1,5})\s*[×x]\s*(\d{1,5})", re.IGNORECASE)
HEX_RE = re.compile(r"#([0-9A-Fa-f]{6})\b")
WATERMARK_TEXT_RE = re.compile(
    r"(?:shutterstock|getty\s*images|istockphoto|adobe\s+stock|alamy|depositphotos|dreamstime|watermark)",
    re.IGNORECASE,
)

TEXTURE_EXTS = {".png"}
MODEL_EXTS = {".fbx"}
FONT_EXTS = {".ttf", ".otf"}
ALL_ART_EXTS = TEXTURE_EXTS | MODEL_EXTS | FONT_EXTS


# ---------------------------------------------------------------------------
# Contract Data Model
# ---------------------------------------------------------------------------

@dataclass
class TextureRule:
    width: Optional[int] = None
    height: Optional[int] = None
    channels: Optional[str] = None  # "RGB" | "RGBA"
    bit_depth: Optional[int] = 8
    srgb: Optional[bool] = True
    seamless_4way: bool = False
    seamless_lr: bool = False
    require_alpha_variation: bool = False
    exact_bottom_hex: Optional[str] = None
    exact_top_hex: Optional[str] = None
    top_transparent_pct: Optional[float] = None
    outer_lr_fade_pct: Optional[float] = None
    ring_inner_transparent_pct: Optional[float] = None
    ring_outer_transparent_pct: Optional[float] = None
    import_settings: Dict[str, object] = field(default_factory=dict)

    def needs_pixels(self) -> bool:
        return any(
            (
                self.seamless_4way,
                self.seamless_lr,
                self.require_alpha_variation,
                self.exact_bottom_hex is not None,
                self.exact_top_hex is not None,
                self.top_transparent_pct is not None,
                self.outer_lr_fade_pct is not None,
                self.ring_inner_transparent_pct is not None,
                self.ring_outer_transparent_pct is not None,
            )
        )

    def clone_format(self) -> "TextureRule":
        return TextureRule(
            width=self.width,
            height=self.height,
            channels=self.channels,
            bit_depth=self.bit_depth,
            srgb=self.srgb,
            seamless_4way=self.seamless_4way,
            seamless_lr=self.seamless_lr,
            require_alpha_variation=self.require_alpha_variation,
            exact_bottom_hex=self.exact_bottom_hex,
            exact_top_hex=self.exact_top_hex,
            top_transparent_pct=self.top_transparent_pct,
            outer_lr_fade_pct=self.outer_lr_fade_pct,
            ring_inner_transparent_pct=self.ring_inner_transparent_pct,
            ring_outer_transparent_pct=self.ring_outer_transparent_pct,
            import_settings=dict(self.import_settings),
        )


@dataclass
class ModelRule:
    binary_fbx: bool = True
    y_up: bool = True
    one_mesh: bool = True
    one_material: bool = True
    uv0_in_0_1: bool = True
    require_normals: bool = True
    triangulated: bool = True
    no_rig_anim_cam_light: bool = True
    height_m: Optional[float] = None
    height_tol_pct: float = 10.0
    max_radius_m: Optional[float] = None
    max_triangles: Optional[int] = None
    below_origin_m: Optional[float] = None
    below_origin_pct: Optional[float] = None
    import_settings: Dict[str, object] = field(default_factory=dict)


@dataclass
class FontRule:
    allowed_exts: Tuple[str, ...] = (".ttf", ".otf")
    exact_count: Optional[int] = 1
    static_only: bool = True
    unicode_min: Optional[int] = None
    unicode_max: Optional[int] = None
    license_suffix: Optional[str] = "-LICENSE.txt"


@dataclass
class ArtDeliverable:
    art_id: str
    kind: str  # "texture" | "model" | "font"
    rel_path: str  # POSIX path relative to repo (for font, directory path)
    filename: str
    source_line: int
    texture_rule: Optional[TextureRule] = None
    model_rule: Optional[ModelRule] = None
    font_rule: Optional[FontRule] = None
    companion_of: Optional[str] = None


@dataclass
class ParsedArtContract:
    base_dir: str = ""
    require_art_readme_credits: bool = False
    deliverables: List[ArtDeliverable] = field(default_factory=list)
    skipped_non_file_items: List[str] = field(default_factory=list)


# ---------------------------------------------------------------------------
# Contract Parsing
# ---------------------------------------------------------------------------

def _norm_rel(path_str: str) -> str:
    return path_str.strip().strip("`").replace("\\", "/").rstrip("/")


def _resolve_rel_path(token: str, sub_dest: str, base_dir: str) -> str:
    tok = _norm_rel(token)
    if tok.startswith("Assets/"):
        return tok
    if sub_dest:
        dest_clean = _norm_rel(sub_dest)
        # Avoid doubling trailing segment if token already starts with subfolder name
        dest_tail = dest_clean.split("/")[-1]
        if tok.startswith(dest_tail + "/"):
            parent = dest_clean.rsplit("/", 1)[0] if "/" in dest_clean else ""
            return f"{parent}/{tok}" if parent else tok
        if "/" not in tok:
            return f"{dest_clean}/{tok}"
        if base_dir and not tok.startswith(base_dir + "/"):
            return f"{_norm_rel(base_dir)}/{tok}"
        return f"{dest_clean}/{tok}"
    if base_dir:
        base_clean = _norm_rel(base_dir)
        base_tail = base_clean.split("/")[-1]
        if tok.startswith(base_tail + "/"):
            parent = base_clean.rsplit("/", 1)[0] if "/" in base_clean else ""
            return f"{parent}/{tok}" if parent else tok
        return f"{base_clean}/{tok}"
    return tok


def _parse_import_clause(text: str, base_import: Dict[str, object]) -> Dict[str, object]:
    imp = dict(base_import)
    if re.search(r"\bSprite\s*\(2D\s+and\s+UI\)", text, re.IGNORECASE):
        imp["texture_type"] = "Sprite"
    if re.search(r"\bSingle\b", text, re.IGNORECASE):
        imp["sprite_mode"] = "Single"
    ppu_m = re.search(r"(\d+)\s+pixels\s+per\s+unit\b", text, re.IGNORECASE)
    if ppu_m:
        imp["ppu"] = int(ppu_m.group(1))
    if re.search(r"\bno\s+mipmaps?\b|\bmipmaps?\s+off\b", text, re.IGNORECASE):
        imp["mipmaps"] = False
    elif re.search(r"\bmipmaps?\s+on\b", text, re.IGNORECASE):
        imp["mipmaps"] = True
    if re.search(r"\bBilinear\b", text, re.IGNORECASE):
        imp["filter_mode"] = "Bilinear"
    elif re.search(r"\bTrilinear\b", text, re.IGNORECASE):
        imp["filter_mode"] = "Trilinear"
    elif re.search(r"\bPoint\b", text, re.IGNORECASE):
        imp["filter_mode"] = "Point"
    aniso_m = re.search(r"\bAniso\s+(\d+)\b", text, re.IGNORECASE)
    if aniso_m:
        imp["aniso"] = int(aniso_m.group(1))
    if re.search(r"\bcompression\s+None\b", text, re.IGNORECASE):
        imp["compression"] = "None"
    elif re.search(r"\bcompression\s+High\s+Quality\b", text, re.IGNORECASE):
        imp["compression"] = "HighQuality"
    if re.search(r"\balpha\s+is\s+transparency\b", text, re.IGNORECASE):
        imp["alpha_is_transparency"] = True
    if re.search(r"\bsRGB\s+on\b", text, re.IGNORECASE):
        imp["srgb"] = True
    elif re.search(r"\bsRGB\s+off\b", text, re.IGNORECASE):
        imp["srgb"] = False

    # Wrap mode: handle "wrap Repeat (U) / Clamp (V)" before generic "wrap Repeat" / "wrap Clamp"
    uv_wrap_m = re.search(
        r"\bwrap\s+(Repeat|Clamp)\s*\(U\)\s*/\s*(Repeat|Clamp)\s*\(V\)",
        text,
        re.IGNORECASE,
    )
    if uv_wrap_m:
        imp["wrap_u"] = uv_wrap_m.group(1).capitalize()
        imp["wrap_v"] = uv_wrap_m.group(2).capitalize()
    else:
        w_m = re.search(r"\bwrap\s+(Repeat|Clamp)\b", text, re.IGNORECASE)
        if w_m and not re.search(r"for\s+seamless\s+textures", text, re.IGNORECASE):
            val = w_m.group(1).capitalize()
            imp["wrap_u"] = val
            imp["wrap_v"] = val

    # 9-slice border
    b_all = re.search(r"(\d+)\s*px\s+all\s+sides\b", text, re.IGNORECASE)
    if b_all:
        px = int(b_all.group(1))
        imp["sprite_border"] = (px, px, px, px)
    return imp


def _apply_texture_clauses(rule: TextureRule, text: str, default_seamless_wrap: bool) -> None:
    dm = DIM_RE.search(text)
    if dm:
        rule.width = int(dm.group(1))
        rule.height = int(dm.group(2))

    if re.search(r"\bRGBA\b", text, re.IGNORECASE):
        rule.channels = "RGBA"
    elif re.search(r"\bRGB\b", text, re.IGNORECASE):
        rule.channels = "RGB"

    bd_m = re.search(r"(\d+)\s*bits?\s+per\s+channel\b", text, re.IGNORECASE)
    if bd_m:
        rule.bit_depth = int(bd_m.group(1))

    if re.search(r"\bseamless\s+left[- ]right\b", text, re.IGNORECASE):
        rule.seamless_lr = True
    elif re.search(r"\bseamless\b", text, re.IGNORECASE):
        rule.seamless_4way = True

    # Pixel & alpha rules
    bot_hex_m = re.search(r"bottom\s+row\s+(?:is\s+)?(?:exactly\s+)?`?#([0-9A-Fa-f]{6})`?", text, re.IGNORECASE)
    if bot_hex_m:
        rule.exact_bottom_hex = bot_hex_m.group(1).upper()

    top_hex_m = re.search(r"top\s+row\s+(?:is\s+)?(?:exactly\s+)?`?#([0-9A-Fa-f]{6})`?", text, re.IGNORECASE)
    if top_hex_m:
        rule.exact_top_hex = top_hex_m.group(1).upper()

    top_trans_m = re.search(r"top\s+(\d+(?:\.\d+)?)\s*%\s+fully\s+transparent\b", text, re.IGNORECASE)
    if top_trans_m:
        rule.top_transparent_pct = float(top_trans_m.group(1))

    outer_fade_m = re.search(
        r"outer\s+(\d+(?:\.\d+)?)\s*%\s+on\s+each\s+side\s+fading\s+to\s+alpha\s+0\b",
        text,
        re.IGNORECASE,
    )
    if outer_fade_m:
        rule.outer_lr_fade_pct = float(outer_fade_m.group(1))

    ring_m = re.search(
        r"fully\s+transparent\s+inside\s+(\d+(?:\.\d+)?)\s*%\s+and\s+outside\s+(\d+(?:\.\d+)?)\s*%",
        text,
        re.IGNORECASE,
    )
    if ring_m:
        rule.ring_inner_transparent_pct = float(ring_m.group(1))
        rule.ring_outer_transparent_pct = float(ring_m.group(2))

    if rule.channels == "RGBA" and re.search(
        r"alpha\s*(?:=|is)|transparent|cut-?out|translucent|fading|fade|ring|aura",
        text,
        re.IGNORECASE,
    ):
        rule.require_alpha_variation = True

    rule.import_settings = _parse_import_clause(text, rule.import_settings)

    # Apply common default wrap policy if wrap wasn't explicitly set in this clause/subsection
    if default_seamless_wrap and "wrap_u" not in rule.import_settings:
        if rule.seamless_4way:
            rule.import_settings["wrap_u"] = "Repeat"
            rule.import_settings["wrap_v"] = "Repeat"
        elif rule.seamless_lr:
            rule.import_settings["wrap_u"] = "Repeat"
            rule.import_settings["wrap_v"] = "Clamp"
        else:
            rule.import_settings["wrap_u"] = "Clamp"
            rule.import_settings["wrap_v"] = "Clamp"


def _parse_model_metrics(rule: ModelRule, row_map: Dict[str, str], full_row_text: str) -> None:
    # Height
    h_cell = next((v for k, v in row_map.items() if "height" in k), "")
    if h_cell:
        hm = re.search(r"(\d+(?:\.\d+)?)\s*m\b", h_cell, re.IGNORECASE)
        if hm:
            rule.height_m = float(hm.group(1))
    else:
        hm = re.search(r"\bheight\s+(\d+(?:\.\d+)?)\s*m\b", full_row_text, re.IGNORECASE)
        if hm:
            rule.height_m = float(hm.group(1))

    # Radius (canopy radius or footprint radius)
    r_cell = next((v for k, v in row_map.items() if "radius" in k), "")
    rm = re.search(r"(\d+(?:\.\d+)?)\s*m\b", r_cell) if r_cell else None
    if rm:
        rule.max_radius_m = float(rm.group(1))

    # Triangles
    t_cell = next((v for k, v in row_map.items() if "triangle" in k), "")
    t_text = t_cell or full_row_text
    tm = re.search(r"(?:≤|<=)?\s*([\d,]+)(?:\s*triangles?)?", t_text, re.IGNORECASE)
    if tm and (t_cell or re.search(r"\btriangles?\b", full_row_text, re.IGNORECASE)):
        if t_cell:
            rule.max_triangles = int(tm.group(1).replace(",", ""))
        else:
            tm2 = re.search(r"(?:≤|<=)\s*([\d,]+)\s*triangles?", full_row_text, re.IGNORECASE)
            if tm2:
                rule.max_triangles = int(tm2.group(1).replace(",", ""))


def parse_art_contract(text: str) -> ParsedArtContract:
    """Parse art delivery contracts from a technical spec or standalone contract markdown."""
    sections = parse_sections(text)
    contract_sec = find_section(sections, r"\bart\s+delivery\s+contracts?\b", r"\bart\s+contracts?\b")

    if contract_sec is not None:
        subsecs = [
            s
            for s in sections
            if s.start_line > contract_sec.start_line
            and s.end_line <= contract_sec.end_line
            and ART_ID_RE.search(s.title)
        ]
        first_sub_line = subsecs[0].start_line if subsecs else contract_sec.end_line + 1
        preamble_lines = [
            line
            for idx, line in enumerate(text.splitlines(), start=1)
            if contract_sec.start_line < idx < first_sub_line
        ]
        preamble = "\n".join(preamble_lines)
    else:
        subsecs = [s for s in sections if ART_ID_RE.search(s.title)]
        first_sub_line = subsecs[0].start_line if subsecs else 1
        preamble = "\n".join(text.splitlines()[: max(0, first_sub_line - 1)])

    parsed = ParsedArtContract()

    # Base dir from preamble
    base_m = re.search(r"under\s+`(Assets/[^`]+)`", preamble, re.IGNORECASE)
    if base_m:
        parsed.base_dir = _norm_rel(base_m.group(1))

    parsed.require_art_readme_credits = bool(re.search(r"ART_README", preamble, re.IGNORECASE))

    # Common texture defaults
    common_tex = TextureRule()
    common_tex.bit_depth = 8 if re.search(r"8\s+bits?\s+per\s+channel", preamble, re.IGNORECASE) else 8
    common_tex.srgb = True
    common_tex.import_settings = _parse_import_clause(preamble, {})
    default_seamless_wrap = bool(re.search(r"wrap\s+Repeat\s+for\s+seamless", preamble, re.IGNORECASE))

    # Common model defaults
    common_model = ModelRule()
    if re.search(r"±\s*(\d+(?:\.\d+)?)\s*%", preamble):
        common_model.height_tol_pct = float(re.search(r"±\s*(\d+(?:\.\d+)?)\s*%", preamble).group(1))
    model_imp: Dict[str, object] = {}
    if re.search(r"Read/Write\s+off", preamble, re.IGNORECASE):
        model_imp["is_readable"] = False
    if re.search(r"mesh\s+compression\s+off", preamble, re.IGNORECASE):
        model_imp["mesh_compression"] = 0
    if re.search(r"materials\s+not\s+imported", preamble, re.IGNORECASE):
        model_imp["material_import_mode"] = 0
    if re.search(r"animation\s+and\s+rig\s+off", preamble, re.IGNORECASE):
        model_imp["import_animation"] = False
        model_imp["animation_type"] = 0
    common_model.import_settings = model_imp
    require_companion_albedo = bool(re.search(r"<model>_albedo\.png", preamble, re.IGNORECASE))

    seen_paths: Set[str] = set()

    for sub in subsecs:
        art_m = ART_ID_RE.search(sub.title)
        if not art_m:
            continue
        art_id = art_m.group(1)

        # Subsection destination
        sub_dest = ""
        dest_m = re.search(
            r"^\s*-\s*(?:\*{0,2}(?:Destination(?:\s+path)?|Path)\s*:?\*{0,2}\s*:?\s*)`([^`]+)`",
            sub.body,
            re.IGNORECASE | re.MULTILINE,
        )
        if dest_m:
            cand = _norm_rel(dest_m.group(1))
            if not cand.endswith((".png", ".fbx", ".ttf", ".otf")) and "..." not in cand:
                sub_dest = cand

        # Subsection-level import overrides
        sub_tex_base = common_tex.clone_format()
        imp_line_m = re.search(r"^\s*-\s*\*{0,2}Import[^:]*:\*{0,2}\s*(.+)$", sub.body, re.IGNORECASE | re.MULTILINE)
        if imp_line_m:
            sub_tex_base.import_settings = _parse_import_clause(imp_line_m.group(1), sub_tex_base.import_settings)

        # Subsection-level albedo spec (e.g., "albedo 2048 × 2048 RGBA" or "albedo RGB")
        sub_albedo_rule = sub_tex_base.clone_format()
        alb_line_m = re.search(r"\balbedo\b([^\n.]*)", sub.body, re.IGNORECASE)
        if alb_line_m:
            _apply_texture_clauses(sub_albedo_rule, alb_line_m.group(1), default_seamless_wrap)

        # Subsection-level below-origin model spec
        sub_below_m: Optional[float] = None
        sub_below_pct: Optional[float] = None
        bm_abs = re.search(
            r"(?:extend|extends|continue|continues)\s+(\d+(?:\.\d+)?)\s*m\s+below\s+the\s+origin",
            sub.body,
            re.IGNORECASE,
        )
        if bm_abs:
            sub_below_m = float(bm_abs.group(1))
        bm_pct = re.search(
            r"below\s+the\s+origin\s+by\s+(?:at\s+least\s+)?(\d+(?:\.\d+)?)\s*%",
            sub.body,
            re.IGNORECASE,
        )
        if bm_pct:
            sub_below_pct = float(bm_pct.group(1))

        # Check font typeface subsection
        if re.search(r"\.(?:ttf|otf)\b", sub.body, re.IGNORECASE) and re.search(r"\btypeface\b|\bfont\b", sub.body, re.IGNORECASE):
            f_rule = FontRule()
            f_rule.exact_count = 1 if re.search(r"exactly\s+one\b", sub.body, re.IGNORECASE) else None
            f_rule.static_only = bool(re.search(r"\bstatic\s*\(non-variable\)|\bnon-variable\b", sub.body, re.IGNORECASE))
            uni_m = re.search(r"U\+([0-9A-Fa-f]{4,6})\s*[–\-]\s*U\+([0-9A-Fa-f]{4,6})", sub.body)
            if uni_m:
                f_rule.unicode_min = int(uni_m.group(1), 16)
                f_rule.unicode_max = int(uni_m.group(2), 16)
            lic_m = re.search(r"`<[^>]+>(-LICENSE\.txt)`|(-LICENSE\.txt)", sub.body)
            if lic_m:
                f_rule.license_suffix = lic_m.group(1) or lic_m.group(2)
            font_dir = sub_dest or parsed.base_dir or "Assets/Resources/Art/UI/Fonts"
            if font_dir not in seen_paths:
                seen_paths.add(font_dir)
                parsed.deliverables.append(
                    ArtDeliverable(
                        art_id=art_id,
                        kind="font",
                        rel_path=font_dir,
                        filename="*.ttf|*.otf",
                        source_line=sub.start_line,
                        font_rule=f_rule,
                    )
                )
            continue

        # Parse tables in subsection
        tables = parse_tables(sub.body, start_line_offset=sub.start_line + 1)
        table_lines_used: Set[int] = set()
        emitted_in_sub = 0

        for tbl in tables:
            prev_tex_rule: Optional[TextureRule] = None
            for row in tbl.rows:
                table_lines_used.add(row.line_no)
                full_row_text = " | ".join(row.cells)
                if re.search(r"\bno\s+(?:image|asset)\s+file\b", full_row_text, re.IGNORECASE):
                    parsed.skipped_non_file_items.append(f"{art_id}: line {row.line_no}")
                    continue

                # Find asset file tokens across row
                tok_matches = list(ASSET_TOKEN_RE.finditer(full_row_text))
                if not tok_matches:
                    continue

                row_albedo_cell = next((v for k, v in row.by_header.items() if "albedo" in k), "")
                row_models: List[ArtDeliverable] = []
                row_textures: Set[str] = set()

                for idx, tm in enumerate(tok_matches):
                    raw_tok = tm.group(1)
                    rel_path = _resolve_rel_path(raw_tok, sub_dest, parsed.base_dir)
                    ext = Path(rel_path).suffix.lower()
                    filename = Path(rel_path).name
                    if ext in TEXTURE_EXTS and "/" not in _norm_rel(raw_tok) and row_models:
                        for md in row_models:
                            if filename == f"{Path(md.rel_path).stem}_albedo.png":
                                m_parent = Path(md.rel_path).parent.as_posix()
                                if m_parent != ".":
                                    rel_path = f"{m_parent}/{filename}"
                                break

                    # Determine slice of row text belonging to this token
                    if len(tok_matches) == 1:
                        seg = full_row_text
                    else:
                        start_pos = tm.end()
                        end_pos = tok_matches[idx + 1].start() if idx + 1 < len(tok_matches) else len(full_row_text)
                        seg = full_row_text[start_pos:end_pos]
                        # Include trailing cell columns after the last token if helpful for single-file trailing notes
                        if idx == len(tok_matches) - 1 and "|" in seg:
                            pass

                    if ext in TEXTURE_EXTS:
                        t_rule = sub_tex_base.clone_format()
                        if re.search(r"\bsame(?:\s+format|\s+rules)?\b", seg, re.IGNORECASE) and prev_tex_rule is not None:
                            t_rule = prev_tex_rule.clone_format()
                        _apply_texture_clauses(t_rule, seg, default_seamless_wrap)
                        # If multiple tokens existed and seg lacked dimensions, check row-level Size column
                        if t_rule.width is None and "size" in row.by_header:
                            _apply_texture_clauses(t_rule, row.by_header["size"], default_seamless_wrap)
                        prev_tex_rule = t_rule.clone_format()
                        row_textures.add(rel_path)
                        if rel_path not in seen_paths:
                            seen_paths.add(rel_path)
                            parsed.deliverables.append(
                                ArtDeliverable(
                                    art_id=art_id,
                                    kind="texture",
                                    rel_path=rel_path,
                                    filename=filename,
                                    source_line=row.line_no,
                                    texture_rule=t_rule,
                                )
                            )
                            emitted_in_sub += 1

                    elif ext in MODEL_EXTS:
                        m_rule = ModelRule(
                            binary_fbx=common_model.binary_fbx,
                            y_up=common_model.y_up,
                            one_mesh=common_model.one_mesh,
                            one_material=common_model.one_material,
                            uv0_in_0_1=common_model.uv0_in_0_1,
                            require_normals=common_model.require_normals,
                            triangulated=common_model.triangulated,
                            no_rig_anim_cam_light=common_model.no_rig_anim_cam_light,
                            height_tol_pct=common_model.height_tol_pct,
                            below_origin_m=sub_below_m,
                            below_origin_pct=sub_below_pct,
                            import_settings=dict(common_model.import_settings),
                        )
                        _parse_model_metrics(m_rule, row.by_header, full_row_text)
                        if rel_path not in seen_paths:
                            seen_paths.add(rel_path)
                            d = ArtDeliverable(
                                art_id=art_id,
                                kind="model",
                                rel_path=rel_path,
                                filename=filename,
                                source_line=row.line_no,
                                model_rule=m_rule,
                            )
                            parsed.deliverables.append(d)
                            row_models.append(d)
                            emitted_in_sub += 1

                # Synthesize companion <model>_albedo.png if common model rules require it
                if require_companion_albedo:
                    for md in row_models:
                        stem = Path(md.rel_path).stem
                        parent = Path(md.rel_path).parent.as_posix()
                        alb_rel = f"{parent}/{stem}_albedo.png" if parent != "." else f"{stem}_albedo.png"
                        if alb_rel not in row_textures and alb_rel not in seen_paths:
                            seen_paths.add(alb_rel)
                            alb_rule = sub_albedo_rule.clone_format()
                            if row_albedo_cell:
                                _apply_texture_clauses(alb_rule, row_albedo_cell, default_seamless_wrap)
                            parsed.deliverables.append(
                                ArtDeliverable(
                                    art_id=art_id,
                                    kind="texture",
                                    rel_path=alb_rel,
                                    filename=f"{stem}_albedo.png",
                                    source_line=md.source_line,
                                    texture_rule=alb_rule,
                                    companion_of=md.rel_path,
                                )
                            )
                            emitted_in_sub += 1

        # Non-table bullet format (e.g., ART-001 in v0.1.0-technical.md or VALID_TECH_SPEC)
        if emitted_in_sub == 0:
            non_table_lines = [
                (offset, line)
                for offset, line in enumerate(sub.body.splitlines(), start=sub.start_line + 1)
                if offset not in table_lines_used
            ]
            non_table_text = "\n".join(line for _, line in non_table_lines)
            if re.search(r"\bno\s+(?:image|asset)\s+file\b", non_table_text, re.IGNORECASE) and not ASSET_TOKEN_RE.search(non_table_text):
                parsed.skipped_non_file_items.append(f"{art_id}: line {sub.start_line}")
                continue

            sub_tex = sub_tex_base.clone_format()
            _apply_texture_clauses(sub_tex, non_table_text, default_seamless_wrap)

            for offset, line in non_table_lines:
                if re.search(r"\bno\s+(?:image|asset)\s+file\b", line, re.IGNORECASE):
                    continue
                for tm in ASSET_TOKEN_RE.finditer(line):
                    raw_tok = tm.group(1)
                    if "..." in raw_tok:
                        continue
                    rel_path = _resolve_rel_path(raw_tok, sub_dest, parsed.base_dir)
                    ext = Path(rel_path).suffix.lower()
                    filename = Path(rel_path).name
                    if ext in TEXTURE_EXTS and rel_path not in seen_paths:
                        seen_paths.add(rel_path)
                        t_rule = sub_tex.clone_format()
                        _apply_texture_clauses(t_rule, line, default_seamless_wrap)
                        parsed.deliverables.append(
                            ArtDeliverable(
                                art_id=art_id,
                                kind="texture",
                                rel_path=rel_path,
                                filename=filename,
                                source_line=offset,
                                texture_rule=t_rule,
                            )
                        )
                    elif ext in MODEL_EXTS and rel_path not in seen_paths:
                        seen_paths.add(rel_path)
                        m_rule = ModelRule(
                            binary_fbx=common_model.binary_fbx,
                            y_up=common_model.y_up,
                            one_mesh=common_model.one_mesh,
                            one_material=common_model.one_material,
                            uv0_in_0_1=common_model.uv0_in_0_1,
                            require_normals=common_model.require_normals,
                            triangulated=common_model.triangulated,
                            no_rig_anim_cam_light=common_model.no_rig_anim_cam_light,
                            height_tol_pct=common_model.height_tol_pct,
                            below_origin_m=sub_below_m,
                            below_origin_pct=sub_below_pct,
                            import_settings=dict(common_model.import_settings),
                        )
                        _parse_model_metrics(m_rule, {}, line)
                        parsed.deliverables.append(
                            ArtDeliverable(
                                art_id=art_id,
                                kind="model",
                                rel_path=rel_path,
                                filename=filename,
                                source_line=offset,
                                model_rule=m_rule,
                            )
                        )

    return parsed


# ---------------------------------------------------------------------------
# Pure-Python Binary Parsers (PNG, Binary FBX, SFNT Font, Unity .meta)
# ---------------------------------------------------------------------------

PNG_SIG = b"\x89PNG\r\n\x1a\n"
PNG_COLOR_CHANNELS = {
    0: "Grayscale",
    2: "RGB",
    3: "Indexed",
    4: "GrayscaleA",
    6: "RGBA",
}


@dataclass
class PngInfo:
    valid: bool
    error: str = ""
    width: int = 0
    height: int = 0
    bit_depth: int = 0
    color_type: int = -1
    channels: str = ""
    interlace: int = 0
    has_srgb: bool = False
    gamma: Optional[float] = None
    has_trns: bool = False
    text_chunks: List[Tuple[str, str]] = field(default_factory=list)
    rows: Optional[List[bytearray]] = None


def _paeth_predictor(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa = abs(p - a)
    pb = abs(p - b)
    pc = abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    if pb <= pc:
        return b
    return c


def _unfilter_png(raw: bytes, width: int, height: int, bpp: int) -> List[bytearray]:
    stride = width * bpp
    expected_len = height * (1 + stride)
    if len(raw) < expected_len:
        raise ValueError(f"Decompressed IDAT length {len(raw)} < expected {expected_len}")
    rows: List[bytearray] = []
    prev = bytearray(stride)
    pos = 0
    for _ in range(height):
        ftype = raw[pos]
        pos += 1
        scan = raw[pos : pos + stride]
        pos += stride
        cur = bytearray(stride)
        if ftype == 0:
            cur[:] = scan
        elif ftype == 1:
            for i in range(stride):
                left = cur[i - bpp] if i >= bpp else 0
                cur[i] = (scan[i] + left) & 0xFF
        elif ftype == 2:
            for i in range(stride):
                cur[i] = (scan[i] + prev[i]) & 0xFF
        elif ftype == 3:
            for i in range(stride):
                left = cur[i - bpp] if i >= bpp else 0
                up = prev[i]
                cur[i] = (scan[i] + ((left + up) >> 1)) & 0xFF
        elif ftype == 4:
            for i in range(stride):
                left = cur[i - bpp] if i >= bpp else 0
                up = prev[i]
                up_left = prev[i - bpp] if i >= bpp else 0
                cur[i] = (scan[i] + _paeth_predictor(left, up, up_left)) & 0xFF
        else:
            raise ValueError(f"Unsupported PNG filter type: {ftype}")
        rows.append(cur)
        prev = cur
    return rows


def inspect_png(path: Path, need_pixels: bool = False) -> PngInfo:
    try:
        data = path.read_bytes()
    except OSError as exc:
        return PngInfo(valid=False, error=f"Cannot read file: {exc}")

    if len(data) < 8 or data[:8] != PNG_SIG:
        return PngInfo(valid=False, error="Invalid PNG file signature.")

    pos = 8
    n = len(data)
    ihdr_seen = False
    iend_seen = False
    idat_parts: List[bytes] = []
    info = PngInfo(valid=False)

    while pos + 8 <= n:
        length, ctype = struct.unpack(">I4s", data[pos : pos + 8])
        pos += 8
        if pos + length + 4 > n:
            return PngInfo(valid=False, error=f"Truncated PNG chunk `{ctype.decode('latin-1', 'replace')}`.")
        payload = data[pos : pos + length]
        pos += length
        crc_read = struct.unpack(">I", data[pos : pos + 4])[0]
        pos += 4
        if (zlib.crc32(ctype + payload) & 0xFFFFFFFF) != crc_read:
            return PngInfo(valid=False, error=f"CRC mismatch in PNG chunk `{ctype.decode('latin-1', 'replace')}`.")

        if ctype == b"IHDR":
            if length != 13:
                return PngInfo(valid=False, error="Invalid IHDR length.")
            w, h, bd, ct, comp, filt, interlace = struct.unpack(">IIBBBBB", payload)
            info.width = w
            info.height = h
            info.bit_depth = bd
            info.color_type = ct
            info.interlace = interlace
            info.channels = PNG_COLOR_CHANNELS.get(ct, f"Unknown({ct})")
            ihdr_seen = True
        elif ctype == b"sRGB":
            info.has_srgb = True
        elif ctype == b"gAMA" and length == 4:
            g_int = struct.unpack(">I", payload)[0]
            if g_int > 0:
                info.gamma = g_int / 100000.0
        elif ctype == b"tRNS":
            info.has_trns = True
        elif ctype == b"tEXt":
            if b"\x00" in payload:
                k, v = payload.split(b"\x00", 1)
                info.text_chunks.append((k.decode("latin-1", "replace"), v.decode("latin-1", "replace")))
        elif ctype == b"zTXt":
            if b"\x00" in payload:
                k, rest = payload.split(b"\x00", 1)
                if len(rest) >= 1 and rest[0] == 0:
                    try:
                        val = zlib.decompress(rest[1:]).decode("latin-1", "replace")
                    except zlib.error:
                        val = ""
                    info.text_chunks.append((k.decode("latin-1", "replace"), val))
        elif ctype == b"iTXt":
            parts = payload.split(b"\x00", 5)
            if len(parts) == 6:
                k = parts[0].decode("utf-8", "replace")
                comp_flag = parts[1][:1]
                raw_txt = parts[5]
                if comp_flag == b"\x01":
                    try:
                        val = zlib.decompress(raw_txt).decode("utf-8", "replace")
                    except zlib.error:
                        val = ""
                else:
                    val = raw_txt.decode("utf-8", "replace")
                info.text_chunks.append((k, val))
        elif ctype == b"IDAT":
            idat_parts.append(payload)
        elif ctype == b"IEND":
            iend_seen = True
            break

    if not ihdr_seen or not iend_seen or not idat_parts:
        return PngInfo(valid=False, error="Missing required PNG IHDR, IDAT, or IEND chunk.")

    if info.color_type == 3 and info.has_trns:
        info.channels = "Indexed+tRNS"

    try:
        raw_pixels = zlib.decompress(b"".join(idat_parts))
    except zlib.error as exc:
        return PngInfo(valid=False, error=f"Corrupt PNG IDAT stream: {exc}")

    if need_pixels and info.interlace == 0 and info.bit_depth == 8 and info.color_type in (2, 6):
        bpp = 3 if info.color_type == 2 else 4
        try:
            info.rows = _unfilter_png(raw_pixels, info.width, info.height, bpp)
        except ValueError as exc:
            return PngInfo(valid=False, error=str(exc))

    info.valid = True
    return info


FBX_MAGIC = b"Kaydara FBX Binary  \x00\x1a\x00"


@dataclass
class FbxInfo:
    valid: bool
    is_binary: bool = False
    error: str = ""
    version: int = 0
    unit_scale_factor: float = 100.0
    up_axis: int = 1  # 1 = Y-up
    mesh_count: int = 0
    material_count: int = 0
    triangle_count: int = 0
    non_tri_polygon_count: int = 0
    has_normals: bool = False
    has_uv0: bool = False
    uv_min: Optional[Tuple[float, float]] = None
    uv_max: Optional[Tuple[float, float]] = None
    min_y_m: Optional[float] = None
    max_y_m: Optional[float] = None
    max_radius_m: Optional[float] = None
    has_rig_or_anim: bool = False
    has_camera: bool = False
    has_light: bool = False


def _read_fbx_array(data: bytes, pos: int, fmt_char: str, elem_size: int) -> Tuple[List[float], int]:
    arr_len, encoding, comp_len = struct.unpack_from("<III", data, pos)
    pos += 12
    raw_bytes = data[pos : pos + comp_len]
    pos += comp_len
    if encoding == 1:
        raw_bytes = zlib.decompress(raw_bytes)
    expected = arr_len * elem_size
    if len(raw_bytes) < expected:
        raise ValueError("Truncated FBX array property.")
    if arr_len == 0:
        return [], pos
    vals = list(struct.unpack_from(f"<{arr_len}{fmt_char}", raw_bytes, 0))
    return vals, pos


def _read_fbx_prop(data: bytes, pos: int) -> Tuple[object, int]:
    ptype = chr(data[pos])
    pos += 1
    if ptype == "Y":
        val = struct.unpack_from("<h", data, pos)[0]
        return val, pos + 2
    if ptype == "C":
        val = bool(data[pos] & 1)
        return val, pos + 1
    if ptype == "I":
        val = struct.unpack_from("<i", data, pos)[0]
        return val, pos + 4
    if ptype == "F":
        val = struct.unpack_from("<f", data, pos)[0]
        return val, pos + 4
    if ptype == "D":
        val = struct.unpack_from("<d", data, pos)[0]
        return val, pos + 8
    if ptype == "L":
        val = struct.unpack_from("<q", data, pos)[0]
        return val, pos + 8
    if ptype in ("S", "R"):
        slen = struct.unpack_from("<I", data, pos)[0]
        pos += 4
        raw = data[pos : pos + slen]
        pos += slen
        if ptype == "S":
            # Strip null/sub-separator artifacts for readable name matching
            return raw.decode("utf-8", "replace"), pos
        return raw, pos
    if ptype == "f":
        return _read_fbx_array(data, pos, "f", 4)
    if ptype == "d":
        return _read_fbx_array(data, pos, "d", 8)
    if ptype == "i":
        return _read_fbx_array(data, pos, "i", 4)
    if ptype == "l":
        return _read_fbx_array(data, pos, "q", 8)
    if ptype == "b":
        return _read_fbx_array(data, pos, "B", 1)
    raise ValueError(f"Unsupported FBX property type: {ptype!r}")


@dataclass
class _FbxNode:
    name: str
    props: List[object]
    children: List["_FbxNode"]


def _parse_fbx_nodes(data: bytes, start: int, end: int, version: int) -> List[_FbxNode]:
    nodes: List[_FbxNode] = []
    is_64 = version >= 7500
    hdr_fmt = "<QQQB" if is_64 else "<IIIB"
    hdr_size = 25 if is_64 else 13
    pos = start

    while pos + hdr_size <= end:
        end_offset, num_props, prop_list_len, name_len = struct.unpack_from(hdr_fmt, data, pos)
        if end_offset == 0 and num_props == 0 and prop_list_len == 0 and name_len == 0:
            pos += hdr_size
            break
        pos += hdr_size
        name = data[pos : pos + name_len].decode("utf-8", "replace")
        pos += name_len
        props_end = pos + prop_list_len
        props: List[object] = []
        for _ in range(num_props):
            val, pos = _read_fbx_prop(data, pos)
            props.append(val)
        pos = props_end
        children: List[_FbxNode] = []
        if pos < end_offset:
            children = _parse_fbx_nodes(data, pos, end_offset, version)
        pos = end_offset
        nodes.append(_FbxNode(name=name, props=props, children=children))
    return nodes


def inspect_fbx(path: Path) -> FbxInfo:
    try:
        data = path.read_bytes()
    except OSError as exc:
        return FbxInfo(valid=False, error=f"Cannot read file: {exc}")

    if len(data) < 27:
        return FbxInfo(valid=False, error="File too small to be an FBX asset.")
    if data[:23] != FBX_MAGIC:
        is_ascii = data.lstrip().startswith((b";", b"FBXHeaderExtension"))
        return FbxInfo(
            valid=False,
            is_binary=False,
            error="FBX file is ASCII or invalid format (binary FBX required)." if is_ascii else "Invalid FBX binary header.",
        )

    version = struct.unpack_from("<I", data, 23)[0]
    info = FbxInfo(valid=False, is_binary=True, version=version)

    try:
        top_nodes = _parse_fbx_nodes(data, 27, len(data), version)
    except Exception as exc:
        return FbxInfo(valid=False, is_binary=True, version=version, error=f"Failed parsing FBX binary tree: {exc}")

    # Extract GlobalSettings
    for node in top_nodes:
        if node.name == "GlobalSettings":
            for child in node.children:
                if child.name == "Properties70":
                    for pnode in child.children:
                        if pnode.name == "P" and len(pnode.props) >= 5:
                            pname = str(pnode.props[0])
                            if pname == "UnitScaleFactor":
                                try:
                                    info.unit_scale_factor = float(pnode.props[4])
                                except (TypeError, ValueError):
                                    pass
                            elif pname == "UpAxis":
                                try:
                                    info.up_axis = int(pnode.props[4])
                                except (TypeError, ValueError):
                                    pass

    scale_to_m = info.unit_scale_factor / 100.0 if info.unit_scale_factor > 0 else 1.0
    all_verts: List[Tuple[float, float, float]] = []
    all_uvs: List[Tuple[float, float]] = []

    for node in top_nodes:
        if node.name != "Objects":
            continue
        for obj in node.children:
            if obj.name == "Geometry" and any(isinstance(p, str) and "Mesh" in p for p in obj.props):
                info.mesh_count += 1
                for sub in obj.children:
                    if sub.name == "Vertices" and sub.props and isinstance(sub.props[0], list):
                        raw_v = sub.props[0]
                        for i in range(0, len(raw_v) - 2, 3):
                            x = float(raw_v[i]) * scale_to_m
                            y = float(raw_v[i + 1]) * scale_to_m
                            z = float(raw_v[i + 2]) * scale_to_m
                            all_verts.append((x, y, z))
                    elif sub.name == "PolygonVertexIndex" and sub.props and isinstance(sub.props[0], list):
                        idx_arr = sub.props[0]
                        poly_len = 0
                        for idx in idx_arr:
                            poly_len += 1
                            if int(idx) < 0:
                                if poly_len == 3:
                                    info.triangle_count += 1
                                elif poly_len > 3:
                                    info.non_tri_polygon_count += 1
                                    info.triangle_count += poly_len - 2
                                poly_len = 0
                    elif sub.name == "LayerElementNormal":
                        if any(c.name == "Normals" and c.props and isinstance(c.props[0], list) and len(c.props[0]) >= 3 for c in sub.children):
                            info.has_normals = True
                    elif sub.name == "LayerElementUV":
                        for c in sub.children:
                            if c.name == "UV" and c.props and isinstance(c.props[0], list) and len(c.props[0]) >= 2:
                                info.has_uv0 = True
                                raw_uv = c.props[0]
                                for i in range(0, len(raw_uv) - 1, 2):
                                    all_uvs.append((float(raw_uv[i]), float(raw_uv[i + 1])))
            elif obj.name == "Material":
                info.material_count += 1
            elif obj.name in ("Deformer", "AnimationStack", "AnimationLayer", "AnimationCurve", "AnimationCurveNode"):
                info.has_rig_or_anim = True
            elif obj.name in ("Model", "NodeAttribute"):
                p_joined = " ".join(str(p) for p in obj.props)
                if re.search(r"\bCamera\b", p_joined):
                    info.has_camera = True
                if re.search(r"\bLight\b", p_joined):
                    info.has_light = True
                if re.search(r"\b(?:LimbNode|Skeleton)\b", p_joined):
                    info.has_rig_or_anim = True

    if all_verts:
        ys = [v[1] for v in all_verts]
        radii = [math.hypot(v[0], v[2]) for v in all_verts]
        info.min_y_m = min(ys)
        info.max_y_m = max(ys)
        info.max_radius_m = max(radii)
    if all_uvs:
        us = [uv[0] for uv in all_uvs]
        vs = [uv[1] for uv in all_uvs]
        info.uv_min = (min(us), min(vs))
        info.uv_max = (max(us), max(vs))

    info.valid = True
    return info


@dataclass
class FontInfo:
    valid: bool
    error: str = ""
    is_variable: bool = False
    intervals: List[Tuple[int, int]] = field(default_factory=list)

    def missing_codepoints(self, start_cp: int, end_cp: int) -> List[int]:
        missing: List[int] = []
        for cp in range(start_cp, end_cp + 1):
            if not any(a <= cp <= b for a, b in self.intervals):
                missing.append(cp)
        return missing


def inspect_font(path: Path) -> FontInfo:
    try:
        data = path.read_bytes()
    except OSError as exc:
        return FontInfo(valid=False, error=f"Cannot read font file: {exc}")

    if len(data) < 12:
        return FontInfo(valid=False, error="File too small to be a valid SFNT font.")
    sfnt_ver = data[:4]
    if sfnt_ver not in (b"\x00\x01\x00\x00", b"OTTO", b"true"):
        return FontInfo(valid=False, error="Invalid TTF/OTF SFNT header.")

    num_tables = struct.unpack_from(">H", data, 4)[0]
    if len(data) < 12 + num_tables * 16:
        return FontInfo(valid=False, error="Truncated SFNT table directory.")

    tables: Dict[bytes, Tuple[int, int]] = {}
    for i in range(num_tables):
        tag, _, offset, length = struct.unpack_from(">4sIII", data, 12 + i * 16)
        tables[tag] = (offset, length)

    is_variable = b"fvar" in tables
    intervals: List[Tuple[int, int]] = []

    if b"cmap" in tables:
        cmap_off, cmap_len = tables[b"cmap"]
        if cmap_off + 4 <= len(data):
            _, num_sub = struct.unpack_from(">HH", data, cmap_off)
            for i in range(num_sub):
                rec_off = cmap_off + 4 + i * 8
                if rec_off + 8 > len(data):
                    break
                _, _, sub_rel = struct.unpack_from(">HHI", data, rec_off)
                sub_off = cmap_off + sub_rel
                if sub_off + 4 > len(data):
                    continue
                fmt = struct.unpack_from(">H", data, sub_off)[0]
                if fmt == 4 and sub_off + 14 <= len(data):
                    seg_count_x2 = struct.unpack_from(">H", data, sub_off + 6)[0]
                    seg_count = seg_count_x2 // 2
                    end_pos = sub_off + 14
                    start_pos = end_pos + seg_count * 2 + 2
                    if start_pos + seg_count * 2 <= len(data):
                        ends = struct.unpack_from(f">{seg_count}H", data, end_pos)
                        starts = struct.unpack_from(f">{seg_count}H", data, start_pos)
                        for s, e in zip(starts, ends):
                            if s == 0xFFFF and e == 0xFFFF:
                                continue
                            intervals.append((s, e))
                elif fmt == 12 and sub_off + 16 <= len(data):
                    num_groups = struct.unpack_from(">I", data, sub_off + 12)[0]
                    grp_off = sub_off + 16
                    for g in range(num_groups):
                        if grp_off + 12 > len(data):
                            break
                        s_cp, e_cp, _ = struct.unpack_from(">III", data, grp_off)
                        grp_off += 12
                        intervals.append((s_cp, e_cp))

    return FontInfo(valid=True, is_variable=is_variable, intervals=intervals)


# ---------------------------------------------------------------------------
# Unity .meta Parser
# ---------------------------------------------------------------------------

META_TEXTURE_TYPE_MAP = {"0": "Default", "8": "Sprite"}
META_SPRITE_MODE_MAP = {"0": "None", "1": "Single", "2": "Multiple"}
META_FILTER_MODE_MAP = {"0": "Point", "1": "Bilinear", "2": "Trilinear"}
META_WRAP_MODE_MAP = {"0": "Repeat", "1": "Clamp", "2": "Mirror", "3": "MirrorOnce"}
META_COMPRESSION_MAP = {"0": "None", "1": "LowQuality", "2": "NormalQuality", "3": "HighQuality"}


def parse_unity_meta(meta_path: Path) -> Dict[str, object]:
    text = meta_path.read_text(encoding="utf-8", errors="replace")
    parsed: Dict[str, object] = {}

    def _get(key: str) -> Optional[str]:
        m = re.search(rf"^\s*{re.escape(key)}\s*:\s*(.+?)\s*$", text, re.MULTILINE)
        return m.group(1).strip() if m else None

    for key, dest, mapping in (
        ("textureType", "texture_type", META_TEXTURE_TYPE_MAP),
        ("spriteMode", "sprite_mode", META_SPRITE_MODE_MAP),
        ("filterMode", "filter_mode", META_FILTER_MODE_MAP),
        ("wrapU", "wrap_u", META_WRAP_MODE_MAP),
        ("wrapV", "wrap_v", META_WRAP_MODE_MAP),
        ("textureCompression", "compression", META_COMPRESSION_MAP),
    ):
        val = _get(key)
        if val is not None:
            parsed[dest] = mapping.get(val, val)

    if "wrap_u" not in parsed or "wrap_v" not in parsed:
        wm = _get("wrapMode")
        if wm is not None:
            mapped = META_WRAP_MODE_MAP.get(wm, wm)
            parsed.setdefault("wrap_u", mapped)
            parsed.setdefault("wrap_v", mapped)

    for key, dest in (
        ("spritePixelsToUnits", "ppu"),
        ("aniso", "aniso"),
        ("maxTextureSize", "max_texture_size"),
        ("meshCompression", "mesh_compression"),
        ("materialImportMode", "material_import_mode"),
        ("animationType", "animation_type"),
    ):
        val = _get(key)
        if val is not None:
            try:
                parsed[dest] = int(float(val))
            except ValueError:
                pass

    for key, dest in (
        ("sRGBTexture", "srgb"),
        ("enableMipMap", "mipmaps"),
        ("alphaIsTransparency", "alpha_is_transparency"),
        ("isReadable", "is_readable"),
        ("importAnimation", "import_animation"),
    ):
        val = _get(key)
        if val is not None:
            parsed[dest] = val in ("1", "true", "True")

    bm = re.search(
        r"spriteBorder:\s*\{x:\s*([0-9.]+),\s*y:\s*([0-9.]+),\s*z:\s*([0-9.]+),\s*w:\s*([0-9.]+)\}",
        text,
    )
    if bm:
        parsed["sprite_border"] = (
            int(float(bm.group(1))),
            int(float(bm.group(2))),
            int(float(bm.group(3))),
            int(float(bm.group(4))),
        )

    return parsed


# ---------------------------------------------------------------------------
# Pixel / Alpha / Seam Verification Helpers
# ---------------------------------------------------------------------------

def _mean_edge_diff(rows: List[bytearray], bpp: int, vertical_seam: bool) -> Tuple[float, float]:
    h = len(rows)
    w = len(rows[0]) // bpp
    if vertical_seam:
        # Compare left edge (x=0) vs right edge (x=w-1)
        edge_sum = 0
        for y in range(h):
            row = rows[y]
            r0, g0, b0 = row[0], row[1], row[2]
            rx = (w - 1) * bpp
            r1, g1, b1 = row[rx], row[rx + 1], row[rx + 2]
            edge_sum += abs(r0 - r1) + abs(g0 - g1) + abs(b0 - b1)
        edge_mean = edge_sum / (h * 3.0)

        # Sample interior horizontal neighbor diff
        int_sum = 0
        int_cnt = 0
        step_x = max(1, w // 16)
        for y in range(0, h, max(1, h // 32)):
            row = rows[y]
            for x in range(0, w - 1, step_x):
                p0 = x * bpp
                p1 = (x + 1) * bpp
                int_sum += abs(row[p0] - row[p1]) + abs(row[p0 + 1] - row[p1 + 1]) + abs(row[p0 + 2] - row[p1 + 2])
                int_cnt += 3
        int_mean = (int_sum / int_cnt) if int_cnt else 0.0
        return edge_mean, int_mean
    else:
        # Compare top edge (y=0) vs bottom edge (y=h-1)
        top = rows[0]
        bot = rows[h - 1]
        edge_sum = 0
        for x in range(w):
            p = x * bpp
            edge_sum += abs(top[p] - bot[p]) + abs(top[p + 1] - bot[p + 1]) + abs(top[p + 2] - bot[p + 2])
        edge_mean = edge_sum / (w * 3.0)

        int_sum = 0
        int_cnt = 0
        step_y = max(1, h // 16)
        for y in range(0, h - 1, step_y):
            r0 = rows[y]
            r1 = rows[y + 1]
            for x in range(0, w, max(1, w // 32)):
                p = x * bpp
                int_sum += abs(r0[p] - r1[p]) + abs(r0[p + 1] - r1[p + 1]) + abs(r0[p + 2] - r1[p + 2])
                int_cnt += 3
        int_mean = (int_sum / int_cnt) if int_cnt else 0.0
        return edge_mean, int_mean


def _check_texture_pixels(
    report: GateReport,
    d: ArtDeliverable,
    png: PngInfo,
) -> Tuple[int, int]:
    """Return (aq4_failures, aq4_seam_warnings)."""
    rule = d.texture_rule
    assert rule is not None
    rows = png.rows
    if rows is None or not rows:
        return 0, 0
    w, h = png.width, png.height
    bpp = 4 if png.color_type == 6 else 3
    aq4_fail = 0
    aq4_warn = 0

    # Exact bottom/top row hex check
    if rule.exact_bottom_hex is not None:
        exp_rgb = tuple(int(rule.exact_bottom_hex[i : i + 2], 16) for i in (0, 2, 4))
        bot = rows[h - 1]
        bad_px = 0
        first_got: Optional[str] = None
        for x in range(w):
            p = x * bpp
            rgb = (bot[p], bot[p + 1], bot[p + 2])
            if rgb != exp_rgb:
                bad_px += 1
                if first_got is None:
                    first_got = f"#{rgb[0]:02X}{rgb[1]:02X}{rgb[2]:02X}"
        if bad_px:
            aq4_fail += 1
            report.add_finding(
                "AQ-4",
                "HIGH",
                d.rel_path,
                f"`{d.art_id}` bottom row has {bad_px}/{w} pixel(s) not matching `#{rule.exact_bottom_hex}` (e.g. `{first_got}`).",
                f"Set every pixel on the bottom row of `{d.filename}` to `#{rule.exact_bottom_hex}`.",
            )

    if rule.exact_top_hex is not None:
        exp_rgb = tuple(int(rule.exact_top_hex[i : i + 2], 16) for i in (0, 2, 4))
        top = rows[0]
        bad_px = sum(
            1 for x in range(w) if (top[x * bpp], top[x * bpp + 1], top[x * bpp + 2]) != exp_rgb
        )
        if bad_px:
            aq4_fail += 1
            report.add_finding(
                "AQ-4",
                "HIGH",
                d.rel_path,
                f"`{d.art_id}` top row has {bad_px}/{w} pixel(s) not matching `#{rule.exact_top_hex}`.",
                f"Set every pixel on the top row of `{d.filename}` to `#{rule.exact_top_hex}`.",
            )

    # Alpha checks
    if bpp == 4:
        min_a = 255
        max_a = 0
        for row in rows:
            for x in range(w):
                a = row[x * 4 + 3]
                if a < min_a:
                    min_a = a
                if a > max_a:
                    max_a = a

        if max_a == 0:
            aq4_fail += 1
            report.add_finding(
                "AQ-4",
                "HIGH",
                d.rel_path,
                f"`{d.art_id}` RGBA texture is completely transparent (all alpha values are 0).",
                f"Export `{d.filename}` with visible content in the alpha channel.",
            )
        elif rule.require_alpha_variation and min_a == 255:
            aq4_fail += 1
            report.add_finding(
                "AQ-4",
                "HIGH",
                d.rel_path,
                f"`{d.art_id}` requires alpha transparency/cutout, but every pixel has alpha 255 (fully opaque).",
                f"Author the contracted alpha channel in `{d.filename}`.",
            )

        if rule.top_transparent_pct is not None:
            band_rows = int(math.floor(h * (rule.top_transparent_pct / 100.0)))
            non_zero = 0
            for y in range(band_rows):
                row = rows[y]
                for x in range(w):
                    if row[x * 4 + 3] != 0:
                        non_zero += 1
            if non_zero:
                aq4_fail += 1
                report.add_finding(
                    "AQ-4",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` top {rule.top_transparent_pct:g}% band ({band_rows} rows) contains {non_zero} non-transparent pixel(s).",
                    f"Keep the top {rule.top_transparent_pct:g}% of `{d.filename}` at alpha 0.",
                )

        if rule.outer_lr_fade_pct is not None:
            edge_non_zero = 0
            for y in range(h):
                row = rows[y]
                if row[3] != 0:
                    edge_non_zero += 1
                if row[(w - 1) * 4 + 3] != 0:
                    edge_non_zero += 1
            if edge_non_zero:
                aq4_fail += 1
                report.add_finding(
                    "AQ-4",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` left/right outer edges contain {edge_non_zero} non-zero alpha pixel(s) (must fade to alpha 0).",
                    f"Fade the outer {rule.outer_lr_fade_pct:g}% on both left and right edges to alpha 0.",
                )

        if rule.ring_inner_transparent_pct is not None and rule.ring_outer_transparent_pct is not None:
            cx = w / 2.0
            cy = h / 2.0
            r_in = (rule.ring_inner_transparent_pct / 100.0) * w
            r_out = (rule.ring_outer_transparent_pct / 100.0) * w
            bad_ring = 0
            for y in range(h):
                row = rows[y]
                dy = (y + 0.5) - cy
                for x in range(w):
                    dx = (x + 0.5) - cx
                    dist = math.hypot(dx, dy)
                    if (dist < r_in or dist > r_out) and row[x * 4 + 3] != 0:
                        bad_ring += 1
            if bad_ring:
                aq4_fail += 1
                report.add_finding(
                    "AQ-4",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` radial foam ring has {bad_ring} non-transparent pixel(s) inside {rule.ring_inner_transparent_pct:g}% or outside {rule.ring_outer_transparent_pct:g}% width radius.",
                    "Clear alpha to 0 inside the inner radius and outside the outer radius.",
                )

    # Seamless edge continuity check (MEDIUM warning)
    if rule.seamless_4way or rule.seamless_lr:
        lr_edge, lr_int = _mean_edge_diff(rows, bpp, vertical_seam=True)
        if lr_edge > 64.0 and lr_edge > (2.5 * lr_int + 24.0):
            aq4_warn += 1
            report.add_finding(
                "AQ-4",
                "MEDIUM",
                d.rel_path,
                f"`{d.art_id}` left-right seam discontinuity detected (edge mean diff={lr_edge:.1f} vs interior={lr_int:.1f}).",
                f"Make `{d.filename}` tile cleanly across its left and right edges.",
            )
    if rule.seamless_4way:
        tb_edge, tb_int = _mean_edge_diff(rows, bpp, vertical_seam=False)
        if tb_edge > 64.0 and tb_edge > (2.5 * tb_int + 24.0):
            aq4_warn += 1
            report.add_finding(
                "AQ-4",
                "MEDIUM",
                d.rel_path,
                f"`{d.art_id}` top-bottom seam discontinuity detected (edge mean diff={tb_edge:.1f} vs interior={tb_int:.1f}).",
                f"Make `{d.filename}` tile cleanly across its top and bottom edges.",
            )

    return aq4_fail, aq4_warn


# ---------------------------------------------------------------------------
# Gate Runner
# ---------------------------------------------------------------------------

def _resolve_supplied_path(repo: Path, art_dir: Optional[Path], rel_path: str) -> Path:
    if art_dir is None:
        return repo / rel_path
    # Try direct relative to art_dir, then relative to Assets/Resources/Art strip, then filename match
    cand = art_dir / rel_path
    if cand.exists():
        return cand
    for prefix in ("Assets/Resources/Art/", "Assets/Art/", "Assets/"):
        if rel_path.startswith(prefix):
            sub = rel_path[len(prefix) :]
            c2 = art_dir / sub
            if c2.exists():
                return c2
    return cand


def _check_meta(
    report: GateReport,
    d: ArtDeliverable,
    asset_file: Path,
    require_meta: bool,
) -> Tuple[bool, int]:
    """Return (meta_checked, fail_count)."""
    meta_path = Path(str(asset_file) + ".meta")
    if not meta_path.is_file():
        if require_meta:
            report.add_finding(
                "AQ-7",
                "HIGH",
                d.rel_path + ".meta",
                f"`{d.art_id}` is missing required Unity `.meta` file `{meta_path.name}`.",
                "Generate and commit the Unity `.meta` file matching the contract's import settings.",
            )
            return True, 1
        return False, 0

    meta = parse_unity_meta(meta_path)
    fails = 0
    expected: Dict[str, object] = {}
    if d.kind == "texture" and d.texture_rule is not None:
        expected = dict(d.texture_rule.import_settings)
        if d.texture_rule.srgb is not None and "srgb" not in expected:
            expected["srgb"] = d.texture_rule.srgb
    elif d.kind == "model" and d.model_rule is not None:
        expected = dict(d.model_rule.import_settings)

    for key, exp_val in sorted(expected.items()):
        if key not in meta:
            continue
        actual_val = meta[key]
        if actual_val != exp_val:
            fails += 1
            report.add_finding(
                "AQ-7",
                "HIGH",
                d.rel_path + ".meta",
                f"`{d.art_id}` Unity `.meta` setting `{key}` is `{actual_val}` (contract requires `{exp_val}`).",
                f"Update `{meta_path.name}` import setting `{key}` to `{exp_val}`.",
            )
    return True, fails


def run_art_quality_gate(
    spec_path: Path,
    repo: Path,
    art_dir: Optional[Path] = None,
    paths: Optional[Sequence[str]] = None,
    art_ids: Optional[Sequence[str]] = None,
    decisions_path: Optional[Path] = None,
    require_meta: bool = False,
) -> GateReport:
    report = GateReport(
        gate="Art Quality",
        target=str(spec_path),
        _finding_prefix="AQ",
    )

    if not spec_path.is_file():
        report.add_blocked(f"Art contract spec not found: {spec_path}")
        report.add_check("AQ-1", "Art delivery contract parsed", "BLOCKED", f"Missing file: {spec_path}")
        return report

    if decisions_path is None:
        candidate = repo / "docs" / "decisions.md"
        if candidate.is_file():
            decisions_path = candidate
    waivers = load_waivers(decisions_path)

    text = spec_path.read_text(encoding="utf-8", errors="replace")
    contract = parse_art_contract(text)

    deliverables = list(contract.deliverables)
    if art_ids:
        wanted = {aid.strip().upper() for aid in art_ids if aid.strip()}
        deliverables = [d for d in deliverables if d.art_id.upper() in wanted]

    # Filter deliverables when explicit --paths are supplied
    explicit_files: List[Path] = []
    if paths:
        for p_str in paths:
            p = Path(p_str)
            full_p = p if p.is_absolute() else (repo / p)
            if full_p.is_file():
                explicit_files.append(full_p.resolve())
            elif full_p.is_dir():
                for dirpath, _, filenames in os.walk(full_p):
                    for fn in sorted(filenames):
                        fp = Path(dirpath) / fn
                        if fp.suffix.lower() in ALL_ART_EXTS:
                            explicit_files.append(fp.resolve())

        if explicit_files:
            explicit_rels: Set[str] = set()
            for fp in explicit_files:
                try:
                    explicit_rels.add(fp.relative_to(repo.resolve()).as_posix())
                except ValueError:
                    explicit_rels.add(fp.as_posix())
            deliverables = [
                d
                for d in deliverables
                if d.rel_path in explicit_rels
                or (d.kind == "font" and any(r.startswith(d.rel_path.rstrip("/") + "/") for r in explicit_rels))
            ]

    if not deliverables:
        report.add_blocked("No contracted art deliverables found matching the spec/filter.")
        report.add_check("AQ-1", "Art delivery contract parsed", "BLOCKED", "0 deliverables extracted")
        return report

    report.add_check(
        "AQ-1",
        "Art delivery contract parsed",
        "PASS",
        f"{len(deliverables)} contracted deliverable(s) ({len(contract.skipped_non_file_items)} non-file item(s) skipped)",
    )

    # ------------------------------------------------------------------
    # AQ-2: Supplied file presence, naming, path containment & uncontracted checks
    # ------------------------------------------------------------------
    aq2_fail = 0
    resolved_assets: Dict[str, Path] = {}
    font_files_by_id: Dict[str, List[Path]] = {}

    repo_resolved = repo.resolve()
    art_root_resolved = art_dir.resolve() if art_dir is not None else repo_resolved

    for d in deliverables:
        if d.kind == "font":
            fdir = _resolve_supplied_path(repo, art_dir, d.rel_path)
            if not fdir.is_dir():
                aq2_fail += 1
                report.add_finding(
                    "AQ-2",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` font destination directory is missing: `{d.rel_path}`.",
                    f"Supply the contracted static `.ttf`/`.otf` font inside `{d.rel_path}`.",
                )
                continue
            found_fonts = sorted(
                fp for fp in fdir.iterdir() if fp.is_file() and fp.suffix.lower() in FONT_EXTS
            )
            if not found_fonts:
                aq2_fail += 1
                report.add_finding(
                    "AQ-2",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` has no `.ttf` or `.otf` font file in `{d.rel_path}`.",
                    f"Supply one static `.ttf` or `.otf` file in `{d.rel_path}`.",
                )
                continue
            if d.font_rule and d.font_rule.exact_count is not None and len(found_fonts) != d.font_rule.exact_count:
                aq2_fail += 1
                report.add_finding(
                    "AQ-2",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` requires exactly {d.font_rule.exact_count} font file, found {len(found_fonts)}.",
                    f"Keep only the single contracted typeface file in `{d.rel_path}`.",
                )
            font_files_by_id[d.art_id] = found_fonts
            continue

        fpath = _resolve_supplied_path(repo, art_dir, d.rel_path)
        if not fpath.is_file():
            aq2_fail += 1
            msg = (
                f"`{d.art_id}` missing companion albedo `{d.rel_path}` for `{d.companion_of}`."
                if d.companion_of
                else f"`{d.art_id}` missing contracted deliverable `{d.rel_path}`."
            )
            report.add_finding("AQ-2", "HIGH", d.rel_path, msg, f"Supply `{d.rel_path}` matching the contract.")
            continue

        # Path containment check
        try:
            fpath_res = fpath.resolve()
            if art_dir is not None:
                fpath_res.relative_to(art_root_resolved)
            else:
                fpath_res.relative_to(repo_resolved)
        except ValueError:
            aq2_fail += 1
            report.add_finding(
                "AQ-2",
                "CRITICAL",
                d.rel_path,
                f"`{d.art_id}` resolved path escapes repository/art root.",
                "Keep all supplied art files strictly inside the repository art tree.",
            )
            continue

        if fpath.stat().st_size == 0:
            aq2_fail += 1
            report.add_finding(
                "AQ-2",
                "HIGH",
                d.rel_path,
                f"`{d.art_id}` supplied file `{d.filename}` is empty (0 bytes).",
                f"Replace `{d.filename}` with a valid exported asset.",
            )
            continue

        resolved_assets[d.rel_path] = fpath

    # Flag uncontracted art files when --paths was explicitly given
    if explicit_files:
        contracted_set = {d.rel_path for d in deliverables if d.kind != "font"}
        font_dirs = {d.rel_path.rstrip("/") for d in deliverables if d.kind == "font"}
        for fp in explicit_files:
            try:
                rel = fp.relative_to(repo_resolved).as_posix()
            except ValueError:
                rel = fp.as_posix()
            in_font_dir = fp.suffix.lower() in FONT_EXTS and any(
                rel.startswith(fd + "/") for fd in font_dirs
            )
            if rel not in contracted_set and not in_font_dir:
                aq2_fail += 1
                report.add_finding(
                    "AQ-2",
                    "HIGH",
                    rel,
                    f"Supplied art file `{rel}` does not match any deliverable in the art contract.",
                    "Rename/move the file to its contracted path or remove the uncontracted asset.",
                )

    report.add_check(
        "AQ-2",
        "Supplied file presence, naming, and path containment",
        "PASS" if aq2_fail == 0 else "FAIL",
        f"{len(resolved_assets) + sum(len(v) for v in font_files_by_id.values())} present, {aq2_fail} issue(s)",
    )

    # ------------------------------------------------------------------
    # AQ-3 & AQ-4: Texture format, dimensions, channels, pixels, and seams
    # ------------------------------------------------------------------
    tex_deliverables = [d for d in deliverables if d.kind == "texture" and d.rel_path in resolved_assets]
    aq3_fail = 0
    aq4_fail = 0
    aq4_warn = 0
    png_cache: Dict[str, PngInfo] = {}

    if not tex_deliverables:
        report.add_check("AQ-3", "PNG validity, dimensions, bit depth, and channels", "SKIP", "No supplied textures to check")
        report.add_check("AQ-4", "Pixel, alpha, and seamless texture contract rules", "SKIP", "No supplied textures to check")
    else:
        for d in tex_deliverables:
            rule = d.texture_rule
            assert rule is not None
            png = inspect_png(resolved_assets[d.rel_path], need_pixels=rule.needs_pixels())
            png_cache[d.rel_path] = png
            if not png.valid:
                aq3_fail += 1
                report.add_finding(
                    "AQ-3",
                    "CRITICAL",
                    d.rel_path,
                    f"`{d.art_id}` invalid PNG `{d.filename}`: {png.error}",
                    f"Re-export `{d.filename}` as a valid PNG.",
                )
                continue

            if rule.width is not None and rule.height is not None:
                if (png.width, png.height) != (rule.width, rule.height):
                    aq3_fail += 1
                    report.add_finding(
                        "AQ-3",
                        "HIGH",
                        d.rel_path,
                        f"`{d.art_id}` dimensions are {png.width}x{png.height} (contract requires {rule.width}x{rule.height}).",
                        f"Resize or re-export `{d.filename}` at exactly {rule.width}x{rule.height}.",
                    )

            if rule.bit_depth is not None and png.bit_depth != rule.bit_depth:
                aq3_fail += 1
                report.add_finding(
                    "AQ-3",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` bit depth is {png.bit_depth}-bit (contract requires {rule.bit_depth}-bit per channel).",
                    f"Export `{d.filename}` at {rule.bit_depth} bits per channel.",
                )

            if rule.channels is not None and png.channels != rule.channels:
                aq3_fail += 1
                report.add_finding(
                    "AQ-3",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` channel format is `{png.channels}` (contract requires `{rule.channels}`).",
                    f"Re-export `{d.filename}` as `{rule.channels}` PNG.",
                )

            if rule.srgb and not png.has_srgb and png.gamma is not None and abs(png.gamma - 0.45455) > 0.05:
                report.add_finding(
                    "AQ-3",
                    "MEDIUM",
                    d.rel_path,
                    f"`{d.art_id}` PNG embeds non-sRGB gamma `{png.gamma:.5f}` without an `sRGB` chunk.",
                    f"Export `{d.filename}` in standard sRGB color space.",
                )

            f4, w4 = _check_texture_pixels(report, d, png)
            aq4_fail += f4
            aq4_warn += w4

        report.add_check(
            "AQ-3",
            "PNG validity, dimensions, bit depth, and channels",
            "PASS" if aq3_fail == 0 else "FAIL",
            f"Checked {len(tex_deliverables)} PNG(s); {aq3_fail} error(s)",
        )
        report.add_check(
            "AQ-4",
            "Pixel, alpha, and seamless texture contract rules",
            "PASS" if aq4_fail == 0 else "FAIL",
            f"Checked {len(tex_deliverables)} PNG(s); {aq4_fail} error(s), {aq4_warn} warning(s)",
        )

    # ------------------------------------------------------------------
    # AQ-5: 3D FBX model rules
    # ------------------------------------------------------------------
    model_deliverables = [d for d in deliverables if d.kind == "model" and d.rel_path in resolved_assets]
    aq5_fail = 0
    if not model_deliverables:
        report.add_check("AQ-5", "FBX binary, topology, UVs, bounds, and forbidden elements", "SKIP", "No supplied FBX models to check")
    else:
        for d in model_deliverables:
            m_rule = d.model_rule
            assert m_rule is not None
            fbx = inspect_fbx(resolved_assets[d.rel_path])
            if not fbx.valid:
                aq5_fail += 1
                report.add_finding(
                    "AQ-5",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` invalid FBX `{d.filename}`: {fbx.error}",
                    f"Export `{d.filename}` as a valid binary FBX.",
                )
                continue

            if m_rule.y_up and fbx.up_axis != 1:
                aq5_fail += 1
                report.add_finding(
                    "AQ-5",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` FBX UpAxis is `{fbx.up_axis}` (contract requires Y-up `1`).",
                    f"Re-export `{d.filename}` with Y-up axis orientation.",
                )

            if m_rule.one_mesh and fbx.mesh_count != 1:
                aq5_fail += 1
                report.add_finding(
                    "AQ-5",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` FBX contains {fbx.mesh_count} mesh(es) (contract requires 1).",
                    f"Combine or clean `{d.filename}` to contain a single mesh.",
                )

            if m_rule.one_material and fbx.material_count > 1:
                aq5_fail += 1
                report.add_finding(
                    "AQ-5",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` FBX defines {fbx.material_count} material slots (contract allows at most 1).",
                    f"Assign a single material slot in `{d.filename}`.",
                )

            if m_rule.triangulated and fbx.non_tri_polygon_count > 0:
                aq5_fail += 1
                report.add_finding(
                    "AQ-5",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` FBX contains {fbx.non_tri_polygon_count} non-triangular polygon(s).",
                    f"Triangulate `{d.filename}` before export.",
                )

            if m_rule.max_triangles is not None and fbx.triangle_count > m_rule.max_triangles:
                aq5_fail += 1
                report.add_finding(
                    "AQ-5",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` FBX triangle count is {fbx.triangle_count} (exceeds max {m_rule.max_triangles}).",
                    f"Decimate `{d.filename}` to at most {m_rule.max_triangles} triangles.",
                )

            if m_rule.require_normals and not fbx.has_normals:
                aq5_fail += 1
                report.add_finding(
                    "AQ-5",
                    "HIGH",
                    d.rel_path,
                    f"`{d.art_id}` FBX is missing vertex normals (`LayerElementNormal`).",
                    f"Export vertex normals in `{d.filename}`.",
                )

            if m_rule.uv0_in_0_1:
                if not fbx.has_uv0 or fbx.uv_min is None or fbx.uv_max is None:
                    aq5_fail += 1
                    report.add_finding(
                        "AQ-5",
                        "HIGH",
                        d.rel_path,
                        f"`{d.art_id}` FBX is missing UV0 coordinates.",
                        f"Add UV0 mapping inside [0, 1] to `{d.filename}`.",
                    )
                elif (
                    fbx.uv_min[0] < -1e-4
                    or fbx.uv_min[1] < -1e-4
                    or fbx.uv_max[0] > 1.0 + 1e-4
                    or fbx.uv_max[1] > 1.0 + 1e-4
                ):
                    aq5_fail += 1
                    report.add_finding(
                        "AQ-5",
                        "HIGH",
                        d.rel_path,
                        f"`{d.art_id}` FBX UV0 bounds ({fbx.uv_min[0]:.3f}..{fbx.uv_max[0]:.3f}, {fbx.uv_min[1]:.3f}..{fbx.uv_max[1]:.3f}) exceed [0, 1].",
                        f"Pack UV0 coordinates strictly inside [0, 1] in `{d.filename}`.",
                    )

            if m_rule.no_rig_anim_cam_light:
                if fbx.has_rig_or_anim:
                    aq5_fail += 1
                    report.add_finding(
                        "AQ-5",
                        "HIGH",
                        d.rel_path,
                        f"`{d.art_id}` FBX contains rig/deformer/animation nodes (forbidden by contract).",
                        f"Remove all rigs, bones, and animation data from `{d.filename}`.",
                    )
                if fbx.has_camera or fbx.has_light:
                    aq5_fail += 1
                    report.add_finding(
                        "AQ-5",
                        "HIGH",
                        d.rel_path,
                        f"`{d.art_id}` FBX contains embedded cameras or lights (forbidden by contract).",
                        f"Strip cameras and lights from `{d.filename}`.",
                    )

            if m_rule.height_m is not None and fbx.max_y_m is not None:
                tol = m_rule.height_m * (m_rule.height_tol_pct / 100.0)
                if abs(fbx.max_y_m - m_rule.height_m) > tol + 1e-4:
                    aq5_fail += 1
                    report.add_finding(
                        "AQ-5",
                        "HIGH",
                        d.rel_path,
                        f"`{d.art_id}` FBX height above origin is {fbx.max_y_m:.2f} m (contract requires {m_rule.height_m:.2f} m ±{m_rule.height_tol_pct:g}%).",
                        f"Scale `{d.filename}` so height from origin upward matches {m_rule.height_m:.2f} m ±{m_rule.height_tol_pct:g}%.",
                    )

            if m_rule.max_radius_m is not None and fbx.max_radius_m is not None:
                if fbx.max_radius_m > m_rule.max_radius_m + 1e-4:
                    aq5_fail += 1
                    report.add_finding(
                        "AQ-5",
                        "HIGH",
                        d.rel_path,
                        f"`{d.art_id}` FBX horizontal radius is {fbx.max_radius_m:.2f} m (exceeds max {m_rule.max_radius_m:.2f} m).",
                        f"Keep XZ geometry of `{d.filename}` within radius {m_rule.max_radius_m:.2f} m.",
                    )

            if fbx.min_y_m is not None:
                req_below = m_rule.below_origin_m
                if req_below is None and m_rule.below_origin_pct is not None and m_rule.height_m is not None:
                    req_below = m_rule.height_m * (m_rule.below_origin_pct / 100.0)
                if req_below is not None and (-fbx.min_y_m) + 1e-4 < req_below:
                    aq5_fail += 1
                    report.add_finding(
                        "AQ-5",
                        "HIGH",
                        d.rel_path,
                        f"`{d.art_id}` FBX extends only {-fbx.min_y_m:.2f} m below origin (contract requires at least {req_below:.2f} m).",
                        f"Extend geometry at least {req_below:.2f} m below Y=0 in `{d.filename}`.",
                    )

        report.add_check(
            "AQ-5",
            "FBX binary, topology, UVs, bounds, and forbidden elements",
            "PASS" if aq5_fail == 0 else "FAIL",
            f"Checked {len(model_deliverables)} FBX model(s); {aq5_fail} error(s)",
        )

    # ------------------------------------------------------------------
    # AQ-6: Font rules (static, Unicode coverage, license sidecar)
    # ------------------------------------------------------------------
    font_deliverables = [d for d in deliverables if d.kind == "font" and d.art_id in font_files_by_id]
    aq6_fail = 0
    if not font_deliverables:
        report.add_check("AQ-6", "Font validity, static requirement, Unicode range, and license sidecar", "SKIP", "No supplied fonts to check")
    else:
        for d in font_deliverables:
            f_rule = d.font_rule
            assert f_rule is not None
            for fp in font_files_by_id[d.art_id]:
                rel_fp = f"{d.rel_path.rstrip('/')}/{fp.name}"
                finfo = inspect_font(fp)
                if not finfo.valid:
                    aq6_fail += 1
                    report.add_finding(
                        "AQ-6",
                        "HIGH",
                        rel_fp,
                        f"`{d.art_id}` invalid font `{fp.name}`: {finfo.error}",
                        f"Supply a valid static `.ttf` or `.otf` file for `{d.art_id}`.",
                    )
                    continue

                if f_rule.static_only and finfo.is_variable:
                    aq6_fail += 1
                    report.add_finding(
                        "AQ-6",
                        "HIGH",
                        rel_fp,
                        f"`{d.art_id}` font `{fp.name}` is a variable font (`fvar` table present); contract requires static.",
                        f"Replace `{fp.name}` with a static (non-variable) font file.",
                    )

                if f_rule.unicode_min is not None and f_rule.unicode_max is not None:
                    missing = finfo.missing_codepoints(f_rule.unicode_min, f_rule.unicode_max)
                    if missing:
                        aq6_fail += 1
                        sample = ", ".join(f"U+{cp:04X}" for cp in missing[:5])
                        report.add_finding(
                            "AQ-6",
                            "HIGH",
                            rel_fp,
                            f"`{d.art_id}` font `{fp.name}` is missing {len(missing)} glyph(s) in U+{f_rule.unicode_min:04X}–U+{f_rule.unicode_max:04X} (e.g. {sample}).",
                            "Choose a font covering the full required Unicode range.",
                        )

                if f_rule.license_suffix:
                    lic_path = fp.parent / f"{fp.stem}{f_rule.license_suffix}"
                    if not lic_path.is_file() or lic_path.stat().st_size == 0:
                        aq6_fail += 1
                        report.add_finding(
                            "AQ-6",
                            "HIGH",
                            f"{d.rel_path.rstrip('/')}/{lic_path.name}",
                            f"`{d.art_id}` missing non-empty license file `{lic_path.name}` beside `{fp.name}`.",
                            f"Commit the font's license text as `{lic_path.name}`.",
                        )

        report.add_check(
            "AQ-6",
            "Font validity, static requirement, Unicode range, and license sidecar",
            "PASS" if aq6_fail == 0 else "FAIL",
            f"Checked {sum(len(v) for v in font_files_by_id.values())} font(s); {aq6_fail} error(s)",
        )

    # ------------------------------------------------------------------
    # AQ-7: Unity .meta import settings compliance
    # ------------------------------------------------------------------
    meta_checked = 0
    aq7_fail = 0
    for d in deliverables:
        if d.kind in ("texture", "model") and d.rel_path in resolved_assets:
            checked, fails = _check_meta(report, d, resolved_assets[d.rel_path], require_meta=require_meta)
            if checked:
                meta_checked += 1
            aq7_fail += fails

    if meta_checked == 0:
        report.add_check("AQ-7", "Unity .meta import settings match contract", "SKIP", "No .meta files present (and --require-meta not set)")
    else:
        report.add_check(
            "AQ-7",
            "Unity .meta import settings match contract",
            "PASS" if aq7_fail == 0 else "FAIL",
            f"Checked {meta_checked} .meta file(s); {aq7_fail} mismatch(es)",
        )

    # ------------------------------------------------------------------
    # AQ-8: Sourcing hygiene & ART_README credits
    # ------------------------------------------------------------------
    aq8_fail = 0
    aq8_warn = 0

    for d in deliverables:
        if "docs/references/" in d.rel_path:
            aq8_fail += 1
            report.add_finding(
                "AQ-8",
                "CRITICAL",
                d.rel_path,
                f"`{d.art_id}` attempts to ship from `docs/references/` (forbidden by standing rules).",
                "Move shipping art under `Assets/Resources/Art/`.",
            )

    for rel_path, png in png_cache.items():
        for k, v in png.text_chunks:
            combined = f"{k}: {v}"
            wm = WATERMARK_TEXT_RE.search(combined)
            if wm:
                aq8_fail += 1
                report.add_finding(
                    "AQ-8",
                    "CRITICAL",
                    rel_path,
                    f"PNG metadata chunk `{k}` contains watermarked/stock preview marker (`{wm.group(0)}`).",
                    "Replace with clean licensed/authorized production art.",
                )

    art_readme = repo / "Assets" / "Art" / "ART_README.md"
    if contract.require_art_readme_credits and art_readme.is_file():
        readme_txt = art_readme.read_text(encoding="utf-8", errors="replace")
        supplied_ids = sorted({d.art_id for d in deliverables if d.rel_path in resolved_assets or d.art_id in font_files_by_id})
        for aid in supplied_ids:
            if aid not in readme_txt:
                aq8_warn += 1
                report.add_finding(
                    "AQ-8",
                    "MEDIUM",
                    "Assets/Art/ART_README.md",
                    f"Supplied family `{aid}` has no credit/license entry in `Assets/Art/ART_README.md`.",
                    f"Add a credit and license row for `{aid}` in `Assets/Art/ART_README.md`.",
                )

    report.add_check(
        "AQ-8",
        "Sourcing hygiene and ART_README credit coverage",
        "PASS" if aq8_fail == 0 else "FAIL",
        f"{aq8_fail} critical sourcing issue(s), {aq8_warn} credit warning(s)",
    )

    report.apply_waivers(waivers)
    return report


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Deterministic Art Quality Gate")
    parser.add_argument("--spec", required=True, type=Path, help="Path to technical spec or art contract markdown file")
    parser.add_argument("--repo", default=Path("."), type=Path, help="Repository root path")
    parser.add_argument("--art-dir", default=None, type=Path, help="Optional root directory of supplied art assets")
    parser.add_argument("--paths", nargs="*", default=None, help="Explicit supplied art file or directory paths")
    parser.add_argument("--art-id", nargs="*", dest="art_ids", default=None, help="Filter to specific ART-NNN ID(s)")
    parser.add_argument("--decisions", default=None, type=Path, help="Optional path to docs/decisions.md")
    parser.add_argument("--require-meta", action="store_true", help="Require Unity .meta files to exist and match")
    parser.add_argument("--format", choices=("markdown", "json"), default="markdown")
    args = parser.parse_args(argv)

    report = run_art_quality_gate(
        spec_path=args.spec.resolve(),
        repo=args.repo.resolve(),
        art_dir=args.art_dir.resolve() if args.art_dir else None,
        paths=args.paths,
        art_ids=args.art_ids,
        decisions_path=args.decisions.resolve() if args.decisions else None,
        require_meta=args.require_meta,
    )
    if args.format == "json":
        print(json.dumps(report.to_dict(), indent=2))
    else:
        print(report.to_markdown())
    return report.exit_code


if __name__ == "__main__":
    sys.exit(main())
