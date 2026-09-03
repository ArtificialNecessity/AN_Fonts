# Variations.Tests fixtures — variable fonts + fontTools-instanced static twins

Spec: `plans/font_variable_support_fvar_avar_gvar_hvar.md` §6 (Silky repo).

## What is here

| File | Role |
|---|---|
| `Inter.var.subset.ttf` | SOURCE VF. WPT `css/css-fonts/variations/resources/` fixture: 7 glyphs, `wght` 100–900 (default 400), `slnt` −10–0, **no `avar`**, `HVAR`, `STAT`. The `font-variation-settings-descriptor-01` font. |
| `Inter.no-var.subset.ttf` | WPT static twin of the above (default instance, shipped by WPT). Cross-check for the `.instanced.default` fixture. |
| `NotoSansSymbols-VariableFont_wght.ttf` | SOURCE VF. `wght` 100–900 with a **9-segment `avar` map**, 171 composite glyphs, `HVAR` + `MVAR` + `VVAR`. |
| `RobotoFlex-VariableFont_….ttf` | SOURCE VF. **13 axes** (`opsz wght GRAD wdth slnt XOPQ YOPQ XTRA YTUC YTLC YTAS YTDE YTFI`), 487 composite glyphs, `HVAR` + `MVAR`, `avar` on every axis. GSUB 1.1 with **7 `rvrn` FeatureVariations records** (Part D; fires at `wght700/1000`, `wdth25`, the 4-axis position). |
| `FontStyleTest-slnt-VF.woff2` / `.ttf` | SOURCE VF (Part D, spec §9). WPT `css/css-fonts/variations/resources/FontStyleTest-slnt-VF.woff2` verbatim (the `slnt-variable.html` font) + the same sfnt decompressed with fontTools (`flavor=None`) because the catalog loads raw sfnt bytes. `slnt` −15–0 (default 0), no `avar`, `gvar HVAR GDEF GPOS STAT`. **GSUB 1.1, `rvrn` with an EMPTY default lookup list and 2 FeatureVariations records:** slnt∈[−1,−0.5] → lookup 0 (`a…z` → `*.italic`, 21 glyphs); slnt∈[−0.4999,0] → lookup 1 (`f g i l r` → `*.mono`). The DEFAULT instance substitutes. 133 glyphs. |
| `*.instanced.*.ttf` | GENERATED static fonts — one per design position (below). NO `fvar/gvar/avar/HVAR/MVAR/VVAR/cvar/STAT`. |
| `generate_instanced_fixtures.py` | The generator. The FIXTURE_PLAN in it is the authoritative list of positions. |

## How the fixtures are used

The unit tests load the VARIABLE font and ask our engine for the outline at a design
position, then load the matching INSTANCED fixture and read its plain `glyf` outline:

```
stbtt_GetGlyphShapeVar(g, coords)      // ours, on the VF
  == stbtt_GetGlyphShape(g)            // fontTools' answer, baked into the fixture
for EVERY glyph, ±1 font unit per point (rounding).
HVAR advances  vs the fixture's hmtx;   MVAR metrics vs the fixture's hhea/OS/2.
```

fontTools `varLib.instancer` is the font industry's reference implementation of the
OpenType variations math (fvar+avar normalization, gvar deltas, IUP inference,
phantom points, composite offsets, item variation stores). Matching it point-for-point
is the definitive correctness gate; pixel comparisons against Skia (in
`tests/graphical/VariableFontTest`) are the parity band.

## Design positions and WHY each one

| Fixture | Exercises |
|---|---|
| `Inter…default` | All-zero normalized coords → must be BYTE-IDENTICAL to the un-varied path (the no-op proof). Also equals `Inter.no-var.subset.ttf`. |
| `Inter…wght100` / `wght900` | Axis extremes (norm −1 / +1): peak tuples at full scalar. |
| `Inter…wght350` | Intermediate weight, no avar: pure tent-scalar interpolation + IUP. |
| `Inter…slnt-10` | Second axis at its extreme; slant deltas are x-only. |
| `Inter…wght100_slnt-10` | Two axes at once (tuple scalars multiply). The WPT descriptor-01 case after clamping `wght 50` → 100. |
| `Noto…wght300` | EXACTLY on an `avar` knee (norm −0.3333 → −0.5). Catches the round-before-avar ordering bug (spec §1.1). |
| `Noto…wght250` | BETWEEN two `avar` knees: piecewise-linear remap interpolation in 2.14 fixed point. |
| `Noto…wght100/700/900` | Extremes + a knee that maps almost identity (0.6 → 0.61); 171 composites. |
| `RobotoFlex…wght100/700/1000` | Big multi-axis font at weight extremes/mid; 487 composites; HVAR advances. |
| `RobotoFlex…wdth25/151` | Width axis extremes (advance changes dominate → HVAR test). |
| `RobotoFlex…opsz36` | EXACTLY on an `avar` knee (norm 0.1693 → 0.492) on a non-wght axis. |
| `RobotoFlex…opsz144` / `slnt-10` / `GRAD150` | Remaining axis extremes; GRAD changes weight WITHOUT changing advances (HVAR must NOT move). |
| `RobotoFlex…wght700_wdth75_opsz36_slnt-5` | Four axes simultaneously incl. one intermediate-region knee: product of scalars, intermediate tuples. |
| `FontStyleTest…default` | slnt 0 → norm 0.0 → FeatureVariations record 2 → twin `rvrn → [1]` (`.mono`). Proves the default instance is NOT a substitution no-op. |
| `FontStyleTest…slnt-14` | The WPT `slnt-variable.html` value (`font-style: italic`/`oblique` = 14°): norm −0.9333 → record 1 → twin `rvrn → [0]` (`.italic`). |
| `FontStyleTest…slnt-15` | Axis min, norm −1.0: lower bound of record 1 inclusive. |
| `FontStyleTest…slnt-7.5` | EXACTLY norm −0.5: the record-1 upper bound is INCLUSIVE (spec: `min <= coord <= max`) → `.italic`. Off-by-one-ULP catcher. |
| `FontStyleTest…slnt-7` | norm −0.4667: just above the knee → record 2 → `.mono`. |

## Regeneration (offline, ONE time; never part of the build)

```
cd SafeStbTrueTypeSharp\tests\Variations.Tests\Fonts
set PYTHONPATH=C:\PROJECTS\3P_fonttools\Lib
python generate_instanced_fixtures.py
```

Generated 2026-09-02 with `3P_fonttools` @ `e7e00f1` (fontTools 4.63.1.dev0), Python 3.13.
Every location is FULLY pinned (unspecified axes → fvar default) so the output is static;
`updateFontNames=False` (outline/metric oracle only — name/STAT rewriting is irrelevant and
needs STAT names the WPT subset lacks). Post-generation check performed: no variation
tables remain, glyph order identical to the source VF, `Inter…default` glyf == source glyf.

## Licenses

- **Inter** — SIL Open Font License 1.1 (© The Inter Project Authors, https://github.com/rsms/inter). Subset files taken verbatim from WPT (`w3c/web-platform-tests`, `css/css-fonts/variations/resources/`), itself BSD-3-Clause; the font remains OFL.
- **Noto Sans Symbols** — SIL OFL 1.1 (Google, https://fonts.google.com/noto).
- **Roboto Flex** — SIL OFL 1.1 (Google Fonts / Font Bureau / David Berlow et al., https://github.com/googlefonts/roboto-flex).
- **FontStyleTest-slnt-VF** — SIL OFL 1.1 (Stephen Nixon / ArrowType, "vf-slnt-test", https://arrowtype.github.io/vf-slnt-test/). Taken verbatim from WPT `css/css-fonts/variations/resources/` (2026-09-02).
- Instanced derivatives inherit the OFL (OFL permits derivative works; Reserved Font Names are not used since `updateFontNames=False` leaves the name table as the source's — these files are TEST FIXTURES, never shipped).