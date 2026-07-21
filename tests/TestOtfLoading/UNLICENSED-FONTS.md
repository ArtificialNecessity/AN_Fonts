# UNLICENSED TEST FONTS — DO NOT USE

## Legal Notice

The font files in this directory are **unlicensed** and may NOT be used for any
display, rendering, embedding, or distribution purposes of any kind.

These files were extracted from random test-case PDFs that exhibited parsing
failures. They are included here **solely** for automated parser debugging and
regression testing of the SafeStbTrueTypeSharp CFF/OTF parsing code.

## Specifically:

- **Lato-Regular.otf** — Used as a known-good reference OTF/CFF font for parser
  validation. The Lato font family is licensed under the SIL Open Font License,
  but this copy is used here only for parser testing, not for any rendered output.

- **debug_frutiger_roman.otf** — A subset of Frutiger-Roman extracted from a PDF
  test document. This is a commercial typeface. This file exists only to reproduce
  a specific CFF subroutine parsing bug and must not be used for any other purpose.

- **debug_generated_cff.otf** — A generated OTF wrapper around raw CFF data
  extracted from a PDF. Used only for parser testing.

## Rules

1. Do NOT render, display, or present glyphs from these fonts to any user.
2. Do NOT distribute these fonts outside this test directory.
3. Do NOT use these fonts in any product, sample, demo, or documentation.
4. These fonts are consumed ONLY by automated tests that verify byte-level parsing.
5. If a font is no longer needed for debugging, DELETE it.