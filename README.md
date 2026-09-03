# ArtificialNecessity.Fonts

**A C# Memory-Safe Font Engine — TTF, OTF, CPU Glyph Raster + simplified Kerning, Glyph-Shaping, and Advance.**

This library descends from [SafeStbTrueTypeSharp](https://github.com/rds1983/SafeStbTrueTypeSharp) (rds1983's fully-safe C# port of stb_truetype 1.24). It contains zero `unsafe` code — all font data access goes through bounds-checked `FakePtr<byte>` views over managed arrays.

## License

MIT / Public-Domain, following stb_truetype and SafeStbTrueTypeSharp upstream.

## What we changed since the fork

Everything after upstream commit `2f76ecb` (2020) is ours:

| Area | Summary |
|---|---|
| **OTF/CFF fixes** | Corrected CFF/CharString parsing bugs and multi-component (composite) glyph handling; added OTF test fonts. |
| **Full GPOS pair kerning** | Replaced stb_truetype's minimal GPOS stub with a proper pair-kerning kernel: script/feature selection, Extension lookups, full ValueRecord layout, class-0 handling. Validated against a numeric HarfBuzz oracle (`tests/GposKerning.Tests`). |
| **Y-only autofit raster** | `stbtt_YPixelGridFitRemap` entry points: a monotonic y-remap applied during rasterization for crisp horizontal stems (see SilkyNvg `plans/crisp_text_yonly_autofit_gridfit.md`) without touching x-spacing. This is custom autofit, not TrueType instruction execution. |
| **Synthetic styles** | Outline-preprocessing synthesis: fake-bold via stroke-union embolden, oblique shear, and horizontal scaling (`FontInfoSynth.cs`). |
| **OpenType Font Variations** | `fvar/avar/gvar/HVAR/MVAR` (glyf), `CFF2` `blend`/`vsindex`, GDEF ItemVariationStore → varied GPOS kerning (`src/Variations/`, `FontInfoVariations.cs`). Oracle: fontTools-instanced static twins, every glyph ±1 unit (`tests/Variations.Tests`). |
| **Variation-dependent glyph selection** | GSUB `rvrn` (Required Variation Alternates) + `FeatureVariations` (`src/Layout/`): `stbtt_FindGlyphIndexVar(codepoint, coords)` picks the font's `.italic`/`.mono`/simplified alternates for a region of design space — single-glyph replacement (SingleSubst, Extension) only. Applied to static fonts too (`rvrn` is a required feature). Oracles: fontTools twins (every cmap glyph) + HarfBuzz glyph ids. |
| **Hygiene** | Removed the dead upstream MonoGame sample; restructured `tests/` into per-project subdirectories. |

## Scope of our attentions

We care about **CPU glyph rasterization and simplified horizontal text layout** for UI-grade Latin-script text: glyph loading (TrueType `glyf`, OTF/CFF and CFF2 outlines, variable-font instances), advance widths, pair kerning, and high-quality anti-aliased raster output. We are deliberately **not** a full text-shaping engine (no HarfBuzz-style contextual shaping, ligatures, vertical layout, or complex-script support). The ONE piece of GSUB we implement is per-glyph replacement through `rvrn`/required features, because without it a variable font renders the wrong glyph at some instances; anything sequence-aware is a follow-up analysis (SilkyNvg `plans/font_variable_support_fvar_avar_gvar_hvar.md` §9.10).

## The GPOS kernel: capabilities and limitations

`stbtt_GetGlyphKernAdvance` makes a **font-level** decision (never mixed per-pair): if the font's GPOS carries a usable `kern` feature it is used; otherwise we fall back to the legacy `kern` table (this also covers fonts whose GPOS exists but is mark/mkmk-only).

### Capabilities

- **Script/feature selection**: prefers the `latn` script, falls back to `DFLT`, uses the default LangSys. Marks the lookups of *every* `kern` feature in the LangSys, applies them in LookupList index order (OpenType application order), and **accumulates adjustments across lookups**. Resolution is cached once per font.
- **Lookup types**: PairAdjustment (type 2), including type 9 Extension-wrapped subtables (32-bit offsets, as emitted by large fonts).
- **Both PairPos formats**: format 1 (per-glyph pair lists, binary-searched) and format 2 (class matrices).
- **Full ValueRecord layout**: record size computed from `popcount(valueFormat)`, including the device-table offset bits (0x0010–0x0080) that contribute to record *size*; `xAdvance` located correctly after XPlacement/YPlacement.
- **HarfBuzz “applied” semantics**: within a lookup the *first subtable that applies* wins. Format 1 applies only when a pair record is found; format 2 applies whenever coverage hits and both classes resolve — *even when the adjustment is zero* (the class-0 fix). A coverage hit without a pair record correctly falls through to the next subtable.
- **Oracle-tested**: `tests/GposKerning.Tests` compares our numeric results against HarfBuzz output for real fonts.

### Limitations (deliberate)

- **xAdvance of glyph 1 only.** We return a single horizontal advance adjustment; YAdvance, placements, and the second glyph's ValueRecord are ignored.
- **Pair kerning only.** No mark attachment (types 4–6), cursive attachment (type 3), single adjustment (type 1), or contextual positioning (types 7–8).
- **No ppem Device tables.** Device-table (0x0001–0x0003) adjustments are skipped (unhinted rendering); **VariationIndex** deltas ARE applied at a variation instance via the GDEF ItemVariationStore (`stbtt_GetGlyphKernAdvanceVar`). Values are unscaled font units.
- **GPOS FeatureVariations not yet wired.** The table-agnostic reader exists (`src/Layout/OpenTypeLayoutFeatureVariations.cs`, used by GSUB); the `kern`-feature hook waits for a fixture — `stbtt_HasUnappliedGposFeatureVariations` flags a font that needs it. See the block comment at `stbtt__ResolveGposKernLookups`.
- **LookupFlags ignored.** For base-glyph pair kerning the only flag observed in target fonts is `IgnoreMarks` (0x0008), which is a no-op when neither glyph is a mark.
- **Latin-centric**: only `latn`/`DFLT` with the default LangSys; no per-language systems, no complex scripts.
- **GSUB: `rvrn` + required feature, SingleSubst only.** Lookup types 2–6/8 reached through them are skipped and counted (`stbtt_GsubUnsupportedLookupCount`); no other GSUB feature is applied.