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
| `Fraunces-VariableFont_SOFT,WONK,opsz,wght.ttf` | SOURCE VF (V8, Silky spec §10). upem 2000, 683 glyphs. `opsz 9/9/144` (avar **10 segments**, slope ≈4.3 between 0.5185 and 0.5704), `wght 100/900/900` (**default == MAX**; avar 7 segments all on the negative side), `SOFT 0/0/100`, `WONK 0/1/1`. 438 composites, **424 with gvar deltas on component offsets**, 22 with a component SCALE (`bullet` = period @1.5 — the glyph that exposed the stb composite-transform bug, see below). `HVAR` (advances only), `GDEF GPOS GSUB STAT`; **no MVAR, no cvar**. GSUB 1.1 `rvrn` FeatureVariations, 2 records → lookup 3 (`h m n s &` + accented → `*.alt`): record 1 `SOFT∈[0,1] ∧ opsz∈[0,0.0667] ∧ wght∈[−1,0]` **fires at the DEFAULT instance**; record 2 `WONK∈[−1,−0.51]`. Named instances Thin/Light/Regular/SemiBold/Bold/Black (Black == default). |
| `SourceSerif4Variable-Roman.otf` | SOURCE VF (V8 / Part B). **CFF2 with SIX FontDicts + FDSelect** (AdobeVFPrototype has one), one VarData over 8 regions, `wght 200/400/900` (avar 6 segments), `opsz 8/20/60`, 30 named instances, `HVAR` (AdvWidthMap), `MVAR stro xhgt`, no FeatureVariations. Copied from `../3P_source-serif/VAR/`. |
| `SourceSerif4Variable-Roman.ttf` | SOURCE VF — the SAME design as glyf+gvar (813 composites, 16 with component scale): the second `Cff2GlyfCrossOracle` pair. |
| `SourceSans3VF-Upright.otf` | SOURCE VF (V8 / Part B). CFF2, ONE FD, `wght 200/200/900` (default == min), avar 8 segments, VarStore with **two VarData subtables** and **672 charstrings that execute `vsindex`** — the only font in reach where `vsindex` is USED (Source Serif: 0; Adobe: one VarData). Copied from `../3P_source-sans-vf/VF/`. |
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
| `Fraunces…default` | opsz 9 / wght 900 / SOFT 0 / WONK 1 — no-op proof on a default-at-max font; `rvrn` record 1 fires at default (`h m n s` substituted). |
| `Fraunces…wght100` | Axis min on a default-at-MAX axis: norm −1 exactly (sign gate for the negative-only branch). |
| `Fraunces…wght400` | Named `Regular`; norm −0.625 → avar −0.60 (ON a knee, negative side). Named-instance round-trip target (spec §10.3). |
| `Fraunces…wght450` | BETWEEN knees on the negative side (norm −0.5625): a `(v−min)/(max−min)` bug yields +0.4375, a wrong-branch bug a positive value. |
| `Fraunces…opsz72` | norm 0.4667 → a knee whose OUT value is identity (0.4667→0.4667): "knee found, wrong segment interpolated" catcher. |
| `Fraunces…opsz84` | norm 0.5556 INSIDE the steepest avar segment (remap ≈0.83): a 2.14-before-avar ordering bug moves the result by several ulps. |
| `Fraunces…opsz144` | Axis max, norm +1. |
| `Fraunces…SOFT100` | Third axis at max, identity avar (control); `rvrn` record 1 still fires. |
| `Fraunces…WONK0` | norm −1 on a default-at-max axis; `rvrn` record 2 fires. |
| `Fraunces…opsz84_wght450_SOFT50_WONK0.4` | Four axes off-default; FeatureVariations record 1 REJECTED (opsz), record 2 fires — first-match-with-rejection on a real font. |
| `SourceSerif4.cff2…` / `SourceSerif4.glyf…` (`default wght200 wght300 wght600 wght900 opsz8 opsz60 wght700_opsz8`) | Multi-FD CFF2 vs its glyf build: `wght300` (norm −0.5 → avar −0.632) and `wght600` (0.4 → 0.330) sit ON knees; `wght700_opsz8` = named `Caption Bold` (round-trip target). |
| `SourceSans3VF.cff2…` (`default wght400 wght900`) | `vsindex` operator path (672 charstrings), default == min. |

## Findings from the V8 fixtures (2026-09-03)

- **ENGINE BUG (static path, fixed in `src/FontInfo.cs` composite branch):** upstream stb_truetype transforms a component as
  `m * (a*x + c*y + dx)` with `m = |(a, b)|`, i.e. a component carrying `WE_HAVE_A_SCALE`/`WE_HAVE_AN_X_AND_Y_SCALE`/`2x2` is
  scaled TWICE and its offset is always scaled. Fraunces `bullet` (period @ 1.5, offset (0,194)): stb xMin 202 vs the font's
  own header / fontTools / FreeType 135. Found by `VariationMetricsTests` lsb (fontTools' instanced hmtx is the first oracle
  independent of stb on this path — VF-vs-twin outline identity passes either way because both halves run the same code).
  Now `x' = a*x + c*y + dx` with the offset scaled only under `SCALED_COMPONENT_OFFSET` (bit 11), rounded not truncated.
  Static fonts in `tests/graphical/_TestFonts` with scaled components (all UNSCALED-offset): Roboto-Regular (`– — ≤`, 9),
  Miama (`¡ ’ ”`, 4), BungeeSpice (sup/sub digits, 14), AdobeVFPrototype.ttf (21). No shipped gold contained any of them
  (`--validate` sweep 2026-09-04: every text gold 0 px).
- **Font self-inconsistency (Fraunces):** `gcircumflex` (539) and `gmacron` (544) carry an HVAR advance delta (−117.6 at wght
  400) but NO gvar phantom advance delta; fontTools' phantom-derived twin hmtx stays at 1225. Same class as Inter `.notdef`;
  production (HVAR wins) matches Chrome. `ProductionAdvance_HvarWins…` derives the expected set from the HVAR delta.
- **Test-oracle correction (CFF2 at the DEFAULT instance):** `stbtt_GetGlyphBox` on a CFF font is a CONTROL-POINT bbox and can
  undershoot a cubic's true extremum (Source Sans `uni002E002D0029`: exact xMin 29.92 vs control 28); at default our lsb is the
  hmtx value verbatim, so the lsb test compares hmtx there and the control bbox only at varied positions.

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