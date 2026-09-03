#!/usr/bin/env python
r"""
Generate INSTANCED (static) fixture fonts from the variable fonts in this directory.

Purpose (plans/font_variable_support_fvar_avar_gvar_hvar.md s6): fontTools'
varLib.instancer is the independent reference implementation of fvar/avar/gvar/
HVAR/MVAR interpolation. For every design position below it bakes a plain static
TTF whose glyf/hmtx/hhea contain the interpolated values. The Variations.Tests
unit tests then assert

    stbtt_GetGlyphShapeVar(g, coords)   on the VARIABLE font
        == stbtt_GetGlyphShape(g)      on the INSTANCED fixture   (+-1 font unit)

for every glyph -- an exact outline oracle that pixel tests cannot provide.

Run ONCE, offline; outputs are checked in. The build never depends on Python or
fontTools. Re-run only to add positions or after upgrading the source VFs.

Usage (from this directory):
    set PYTHONPATH=C:\PROJECTS\3P_fonttools\Lib
    python generate_instanced_fixtures.py

fontTools source used for the checked-in fixtures: 3P_fonttools @ e7e00f1 (4.63.1.dev0).
"""
import os
import sys

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

HERE = os.path.dirname(os.path.abspath(__file__))

# (source VF file, fixture stem, [ {axisTag: userValue, ...}, ... ])
# Every location is FULLY PINNED (unspecified axes -> their fvar default), so the
# output is a static font with no fvar/gvar/avar/HVAR/MVAR.
FIXTURE_PLAN = [
    (
        "Inter.var.subset.ttf",  # WPT css-fonts/variations fixture: 7 glyphs, wght 100..900, slnt -10..0, NO avar
        "Inter.var.subset",
        [
            {},                              # default instance -> byte-identical no-op proof
            {"wght": 100},
            {"wght": 900},
            {"wght": 350},                   # intermediate (non-knee) weight
            {"slnt": -10},
            {"wght": 100, "slnt": -10},      # WPT font-variation-settings-descriptor-01 (wght 50 clamps to 100)
        ],
    ),
    (
        "NotoSansSymbols-VariableFont_wght.ttf",  # wght 100..900 with a 9-segment avar map; 171 composite glyphs
        "NotoSansSymbols-VariableFont_wght",
        [
            {"wght": 100},
            {"wght": 900},
            {"wght": 300},                   # EXACTLY on an avar knee (norm -0.3333 -> -0.5)
            {"wght": 250},                   # BETWEEN knees: exercises avar piecewise-linear interpolation
            {"wght": 700},                   # knee 0.6 -> 0.61
        ],
    ),
    (
        "RobotoFlex-VariableFont_GRAD,XOPQ,XTRA,YOPQ,YTAS,YTDE,YTFI,YTLC,YTUC,opsz,slnt,wdth,wght.ttf",
        "RobotoFlex",  # 13 axes, 487 composite glyphs, HVAR + MVAR, avar on every axis
        [
            {"wght": 100},
            {"wght": 1000},
            {"wght": 700},
            {"wdth": 25},
            {"wdth": 151},
            {"opsz": 36},                    # EXACTLY on an avar knee (norm 0.1693 -> 0.492)
            {"opsz": 144},
            {"slnt": -10},
            {"GRAD": 150},
            {"wght": 700, "wdth": 75, "opsz": 36, "slnt": -5},  # multi-axis: tuple scalars multiply across axes
        ],
    ),
    # ── Part B (CFF2): Adobe Variable Font Prototype, wght 200..389.34..900, CNTR 0..0..100 (OFL 1.1) ──
    # The .otf is CFF2 (blend/vsindex charstrings, one FD, one ItemVariationData over 5 regions, HVAR for
    # advances since CFF2 has no phantom points). fontTools instances a fully-pinned CFF2 into a plain
    # 'CFF ' (OTTO) font -- the fixture keeps the .ttf suffix of this generator but is CFF-flavoured
    # (our loader sniffs by content). The .ttf is the SAME design built as glyf+gvar: a second, independent
    # oracle -- the CFF2 outline at P must agree with the glyf outline at P within cubic<->quadratic
    # authoring tolerance (compared as rasterized masks/bboxes, not point lists).
    (
        "AdobeVFPrototype.otf",
        "AdobeVFPrototype.cff2",
        [
            {},                              # default instance (wght 389.34, CNTR 0): blend with all-zero scalars == static reader
            {"wght": 200},                   # axis min
            {"wght": 900},                   # axis max
            {"wght": 600},                   # intermediate weight
            {"CNTR": 100},                   # second axis at its max (default wght)
            {"wght": 900, "CNTR": 50},       # named instance; two axes at once
            {"wght": 900, "CNTR": 100},      # named instance
        ],
    ),
    (
        "AdobeVFPrototype.ttf",
        "AdobeVFPrototype.glyf",
        [
            {},
            {"wght": 200},
            {"wght": 900},
            {"wght": 600},
            {"CNTR": 100},
            {"wght": 900, "CNTR": 50},
            {"wght": 900, "CNTR": 100},
        ],
    ),
]


def fixture_name(stem, location):
    if not location:
        return f"{stem}.instanced.default.ttf"
    parts = "_".join(f"{tag}{fmt(v)}" for tag, v in location.items())
    return f"{stem}.instanced.{parts}.ttf"


def fmt(v):
    return str(int(v)) if float(v).is_integer() else str(v)


def main():
    generated = []
    for source_file, stem, locations in FIXTURE_PLAN:
        source_path = os.path.join(HERE, source_file)
        axis_defaults = {a.axisTag: a.defaultValue for a in TTFont(source_path)["fvar"].axes}
        for location in locations:
            unknown = set(location) - set(axis_defaults)
            if unknown:
                sys.exit(f"{source_file}: unknown axis tags {unknown}")
            full_location = dict(axis_defaults)
            full_location.update(location)
            font = TTFont(source_path)
            # updateFontNames=False: keep name/STAT untouched -- we compare outlines and
            # metrics only, and name rewriting needs STAT axis-value names that subsets lack.
            instancer.instantiateVariableFont(font, full_location, inplace=True, updateFontNames=False)
            for tag in ("fvar", "gvar", "avar", "HVAR", "MVAR", "VVAR", "cvar", "STAT"):
                if tag in font:
                    del font[tag]   # belt and braces: the fixture must be unmistakably static
            out_name = fixture_name(stem, location)
            out_path = os.path.join(HERE, out_name)
            font.save(out_path)
            generated.append((out_name, os.path.getsize(out_path), full_location))
            print(f"  wrote {out_name}  ({os.path.getsize(out_path):,} bytes)")
    print(f"\n{len(generated)} fixtures generated.")


if __name__ == "__main__":
    main()