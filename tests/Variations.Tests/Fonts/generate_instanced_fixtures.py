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
    # ── Part D (GSUB rvrn + FeatureVariations): WPT css-fonts/variations FontStyleTest-slnt-VF (Stephen Nixon /
    # ArrowType vf-slnt-test, OFL). slnt -15..0 (default 0), NO avar. GSUB 1.1 with an `rvrn` feature whose DEFAULT
    # lookup list is EMPTY and a FeatureVariations table:
    #   slnt normalized in [-1.0, -0.5]     -> lookup 0: a..z (21 glyphs) -> *.italic
    #   slnt normalized in [-0.4999, 0.0]   -> lookup 1: f g i l r        -> *.mono
    # so even the DEFAULT instance is not a substitution no-op. fontTools' instancer evaluates the ConditionSets
    # at the pinned location and bakes the winning lookups into the twin's plain `rvrn` feature (FeatureVariations
    # dropped) -- the twin's GSUB is the substitution oracle, exactly like its glyf is the outline oracle.
    # The .woff2 is WPT's file verbatim; the .ttf is the same sfnt decompressed (fontTools, flavor=None).
    (
        "FontStyleTest-slnt-VF.ttf",
        "FontStyleTest-slnt-VF",
        [
            {},                              # slnt 0: norm 0.0 -> record 2 (mono) applies
            {"slnt": -14},                   # the WPT slnt-variable.html value (italic/oblique = 14deg): norm -0.9333 -> italic
            {"slnt": -15},                   # axis min: norm -1.0
            {"slnt": -7.5},                  # EXACTLY the knee: norm -0.5 is INCLUSIVE in record 1 -> italic
            {"slnt": -7},                    # just above the knee: norm -0.4667 -> record 2 -> mono
        ],
    ),
    # ── V8 coverage pass (spec s10, 2026-09-03). Fraunces Roman (Undercase Type, OFL 1.1; Google Fonts build).
    # upem 2000, 683 glyphs, fvar/avar/gvar/HVAR/GDEF/GPOS/GSUB/STAT, NO MVAR, NO cvar.
    #   opsz  9 /   9 / 144   (default == min)   avar: 10 SEGMENTS, steep (0.5185->0.6741, 0.5704->0.8963)
    #   wght 100 / 900 / 900  (default == MAX)   avar: 7 segments, all on the NEGATIVE side
    #   SOFT   0 /   0 / 100                     avar identity
    #   WONK   0 /   1 /   1  (default == max)   avar identity
    # 438 composites, 424 with gvar deltas on component offsets. GSUB 1.1 FeatureVariations, 2 records -> rvrn lookup 3
    # (h m n s & + accented -> *.alt): record 1 = SOFT in [0,1] AND opsz in [0,0.0667] AND wght in [-1,0] -- FIRES AT THE
    # DEFAULT INSTANCE; record 2 = WONK in [-1,-0.51]. Named instances Thin/Light/Regular/SemiBold/Bold/Black at
    # opsz 9 SOFT 0 WONK 1 (Black == the fvar default).
    (
        "Fraunces-VariableFont_SOFT,WONK,opsz,wght.ttf",
        "Fraunces",
        [
            {},                              # default = opsz 9 wght 900 SOFT 0 WONK 1; rvrn record 1 fires at default
            {"wght": 100},                   # axis min on a default-at-max axis: norm -1 exactly (sign gate)
            {"wght": 400},                   # named 'Regular'; norm -0.625 -> avar -0.60 (knee, negative side); s10.3 round-trip target
            {"wght": 450},                   # BETWEEN knees on the negative side (norm -0.5625): (v-min)/(max-min) bug gives +0.4375
            {"opsz": 72},                    # norm 0.4667 -> knee whose OUT value is identity (0.4667 -> 0.4667)
            {"opsz": 84},                    # norm 0.5556 INSIDE the steepest avar segment (remap ~0.83); ordering bug moves several ulps
            {"opsz": 144},                   # axis max, norm +1
            {"SOFT": 100},                   # third axis at max, identity avar (control); rvrn record 1 still fires
            {"WONK": 0},                     # norm -1 on a default-at-max axis; rvrn record 2 fires
            {"opsz": 84, "wght": 450, "SOFT": 50, "WONK": 0.4},  # 4 axes off-default; record 1 REJECTED (opsz), record 2 fires
        ],
    ),
    # ── V8 / Part B follow-up (spec s10.4). Source Serif 4 Variable Roman (Adobe, OFL 1.1), from ../3P_source-serif/VAR/.
    # The .otf is CFF2 with SIX FontDicts + FDSelect (AdobeVFPrototype has ONE FD), one VarData over 8 regions, HVAR with
    # AdvWidthMap, MVAR stro/xhgt, 30 named instances. The .ttf is the SAME design as glyf+gvar (813 composites): the
    # second Cff2GlyfCrossOracle pair. wght 200/400/900 (avar 6 segments: -0.5->-0.632, 0.4->0.330, 0.6->0.708),
    # opsz 8/20/60 (avar identity). No FeatureVariations in GSUB or GPOS.
    (
        "SourceSerif4Variable-Roman.otf",
        "SourceSerif4.cff2",
        [
            {},                              # default (wght 400, opsz 20)
            {"wght": 200},                   # axis min
            {"wght": 300},                   # norm -0.5 -> avar -0.632 (ON a knee)
            {"wght": 600},                   # norm 0.4 -> avar 0.330 (ON a knee)
            {"wght": 900},                   # axis max
            {"opsz": 8},                     # second axis min
            {"opsz": 60},                    # second axis max
            {"wght": 700, "opsz": 8},        # = named instance 'Caption Bold'; s10.3 round-trip target
        ],
    ),
    (
        "SourceSerif4Variable-Roman.ttf",
        "SourceSerif4.glyf",
        [
            {},
            {"wght": 200},
            {"wght": 300},
            {"wght": 600},
            {"wght": 900},
            {"opsz": 8},
            {"opsz": 60},
            {"wght": 700, "opsz": 8},
        ],
    ),
    # ── V8 (spec s10.4). Source Sans 3 VF Upright (Adobe, OFL 1.1), from ../3P_source-sans-vf/VF/. CFF2, ONE FD,
    # wght 200/200/900 (default == MIN), avar 8 segments, VarStore with TWO VarData subtables and 672 charstrings that
    # execute the `vsindex` operator -- the only font in reach where vsindex is USED (Source Serif: 0, Adobe: one VarData).
    (
        "SourceSans3VF-Upright.otf",
        "SourceSans3VF.cff2",
        [
            {},                              # default == min: all requests normalize to [0, 1]
            {"wght": 400},                   # between avar knees
            {"wght": 900},                   # axis max
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