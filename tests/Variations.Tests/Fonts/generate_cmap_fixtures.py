#!/usr/bin/env python
r"""
Generate the SYNTHETIC cmap edge-case fixtures for CmapSubtableSelectionTests
(plans/BUGFIX_cmap_subtable_selection_mac_platform.md s3.5 / s4).

All four are derived from Inter.no-var.subset.ttf (OFL 1.1; 7 glyphs: .notdef a l n s t glyph00006)
so the submodule's test suite stays self-contained and free of Apple system-font bytes. Each fixture
keeps glyf/hmtx untouched and REPLACES the cmap encoding records:

  Synthetic.Inter.uvs-plus-bmp.ttf      (0,5) format 14 + (3,1) format 4
        -> the (0,5) record must be SKIPPED (fmt 14 is a supplement); lookup goes through (3,1).
  Synthetic.Inter.cmap-fmt10-only.ttf   (3,10) format 10 only  (trimmed 32-bit array)
  Synthetic.Inter.cmap-fmt8-only.ttf    (3,10) format 8 only   (mixed 16/32, deprecated)
  Synthetic.Inter.maccmap-only.ttf      (1,0) format 6 only, ASCII identity + 0xDE -> glyph 'glyph00006'
        -> OFL twin of the Quartz fixtures: 0xDE is Mac Roman 'fi' (U+FB01), so U+FB01 must resolve
           and U+00DE (the naive identity read) must NOT.

Run ONCE, offline; outputs are checked in. The build never depends on Python or fontTools.

Usage (from this directory):
    python generate_cmap_fixtures.py

fontTools used for the checked-in fixtures: 4.60.0 (pip) / 3P_fonttools Lib.
"""
import os
import struct

from fontTools.ttLib import TTFont
from fontTools.ttLib.tables._c_m_a_p import cmap_format_4, cmap_format_6, cmap_format_14, cmap_format_unknown

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "Inter.no-var.subset.ttf")


def load_source():
    f = TTFont(SOURCE, recalcTimestamp=False)
    return f, dict(f["cmap"].getBestCmap())   # {97:'a',108:'l',110:'n',115:'s',116:'t'}


def sub(fmt_cls, fmt, platform, encoding, mapping, language=0):
    t = fmt_cls(fmt)
    t.platformID, t.platEncID, t.language = platform, encoding, language
    t.cmap = dict(mapping)
    return t


def raw_sub(fmt, platform, encoding, data):
    """fontTools cannot WRITE formats 8/10; carry hand-packed bytes through cmap_format_unknown."""
    t = cmap_format_unknown(fmt)
    t.platformID, t.platEncID, t.language = platform, encoding, 0
    t.data = data
    t.cmap = {}
    return t


def pack_format_10(gid_by_code):
    """OpenType cmap Format 10 (_EXTERNAL_APIS/OpenType/cmap.md):
       u16 format=10 | u16 reserved | u32 length | u32 language | u32 startCharCode | u32 numChars | u16 glyphIdArray[numChars]"""
    first, last = min(gid_by_code), max(gid_by_code)
    gids = [gid_by_code.get(c, 0) for c in range(first, last + 1)]
    length = 20 + 2 * len(gids)
    return struct.pack(">HHIIII", 10, 0, length, 0, first, len(gids)) + struct.pack(f">{len(gids)}H", *gids)


def sequential_groups(gid_by_code):
    """Maximal runs where glyph id advances with the character code -> (startCharCode, endCharCode, startGlyphID)."""
    groups = []
    for code in sorted(gid_by_code):
        gid = gid_by_code[code]
        if groups and groups[-1][1] + 1 == code and groups[-1][2] + (code - groups[-1][0]) == gid:
            groups[-1] = (groups[-1][0], code, groups[-1][2])
        else:
            groups.append((code, code, gid))
    return groups


def pack_format_8(gid_by_code):
    """OpenType cmap Format 8 (deprecated mixed 16/32-bit):
       u16 format=8 | u16 reserved | u32 length | u32 language | u8 is32[8192] | u32 numGroups | SequentialMapGroup[numGroups]
       All codes here are BMP, so every is32 bit is 0."""
    groups = sequential_groups(gid_by_code)
    length = 12 + 8192 + 4 + 12 * len(groups)
    body = b"".join(struct.pack(">III", s, e, g) for s, e, g in groups)
    return struct.pack(">HHII", 8, 0, length, 0) + bytes(8192) + struct.pack(">I", len(groups)) + body


def gid_map(font, uni):
    order = font.getGlyphOrder()
    return {code: order.index(name) for code, name in uni.items()}


def save(font, name):
    path = os.path.join(HERE, name)
    font.save(path)
    check = TTFont(path)
    recs = "; ".join(f"({t.platformID},{t.platEncID}) fmt{t.format}" for t in check["cmap"].tables)
    print(f"  wrote {name}  ({os.path.getsize(path):,} bytes)  {recs}")


def main():
    # 1. (0,5) fmt14 + (3,1) fmt4 -- the fmt14 record sorts FIRST and must be skipped by the ranking.
    f, uni = load_source()
    uvs = cmap_format_14(14)
    uvs.platformID, uvs.platEncID, uvs.language = 0, 5, 0
    uvs.cmap = {}
    # One default UVS (a + VS1 -> default glyph) so the table is non-empty and well-formed.
    uvs.uvsDict = {0xFE00: [(97, None)]}
    f["cmap"].tables = [uvs, sub(cmap_format_4, 4, 3, 1, uni)]
    save(f, "Synthetic.Inter.uvs-plus-bmp.ttf")

    # 2. (3,10) format 10 only.
    f, uni = load_source()
    f["cmap"].tables = [raw_sub(10, 3, 10, pack_format_10(gid_map(f, uni)))]
    save(f, "Synthetic.Inter.cmap-fmt10-only.ttf")

    # 3. (3,10) format 8 only.
    f, uni = load_source()
    f["cmap"].tables = [raw_sub(8, 3, 10, pack_format_8(gid_map(f, uni)))]
    save(f, "Synthetic.Inter.cmap-fmt8-only.ttf")

    # 4. (1,0) format 6 only, Mac Roman keyed: ASCII identity + 0xDE ('fi' in Mac Roman) -> glyph00006.
    f, uni = load_source()
    mac = dict(uni)
    mac[0xDE] = "glyph00006"
    f["cmap"].tables = [sub(cmap_format_6, 6, 1, 0, mac)]
    save(f, "Synthetic.Inter.maccmap-only.ttf")


if __name__ == "__main__":
    main()