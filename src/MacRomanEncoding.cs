using System.Collections.Generic;

namespace StbTrueTypeSharp
{
	/// <summary>
	/// Mac OS Roman code page (Apple ROMAN.TXT, Mac OS 8.5+ semantics) — the key space of a cmap (platformID 1,
	/// encodingID 0) subtable. Such a subtable maps Mac Roman BYTES (not Unicode) to glyph IDs, so
	/// <see cref="FontInfo.stbtt_FindGlyphIndex"/> must translate the caller's Unicode scalar to a byte first
	/// (plans/BUGFIX_cmap_subtable_selection_mac_platform.md §3.2). Only the reverse direction is on the hot path.
	/// </summary>
	public static class MacRomanEncoding
	{
		/// <summary>Returned by <see cref="UnicodeToMacRomanByte"/> when the codepoint has no Mac Roman byte.</summary>
		public const int NoMacRomanByte = -1;

		/// <summary>
		/// Upper half of the code page: <c>HighHalfToUnicode[b - 0x80]</c> is the Unicode scalar for byte <c>b</c>
		/// (0x80..0xFF). Bytes 0x00..0x7F are ASCII identity. Generated from Python's <c>mac_roman</c> codec, which
		/// carries Apple's ROMAN.TXT: 0xDB = U+20AC EURO SIGN (post Mac OS 8.5), 0xF0 = U+F8FF Apple logo (PUA).
		/// </summary>
		public static readonly int[] HighHalfToUnicode =
		{
			0x00C4, 0x00C5, 0x00C7, 0x00C9, 0x00D1, 0x00D6, 0x00DC, 0x00E1,  // 0x80-0x87
			0x00E0, 0x00E2, 0x00E4, 0x00E3, 0x00E5, 0x00E7, 0x00E9, 0x00E8,  // 0x88-0x8F
			0x00EA, 0x00EB, 0x00ED, 0x00EC, 0x00EE, 0x00EF, 0x00F1, 0x00F3,  // 0x90-0x97
			0x00F2, 0x00F4, 0x00F6, 0x00F5, 0x00FA, 0x00F9, 0x00FB, 0x00FC,  // 0x98-0x9F
			0x2020, 0x00B0, 0x00A2, 0x00A3, 0x00A7, 0x2022, 0x00B6, 0x00DF,  // 0xA0-0xA7
			0x00AE, 0x00A9, 0x2122, 0x00B4, 0x00A8, 0x2260, 0x00C6, 0x00D8,  // 0xA8-0xAF
			0x221E, 0x00B1, 0x2264, 0x2265, 0x00A5, 0x00B5, 0x2202, 0x2211,  // 0xB0-0xB7
			0x220F, 0x03C0, 0x222B, 0x00AA, 0x00BA, 0x03A9, 0x00E6, 0x00F8,  // 0xB8-0xBF
			0x00BF, 0x00A1, 0x00AC, 0x221A, 0x0192, 0x2248, 0x2206, 0x00AB,  // 0xC0-0xC7
			0x00BB, 0x2026, 0x00A0, 0x00C0, 0x00C3, 0x00D5, 0x0152, 0x0153,  // 0xC8-0xCF
			0x2013, 0x2014, 0x201C, 0x201D, 0x2018, 0x2019, 0x00F7, 0x25CA,  // 0xD0-0xD7
			0x00FF, 0x0178, 0x2044, 0x20AC, 0x2039, 0x203A, 0xFB01, 0xFB02,  // 0xD8-0xDF
			0x2021, 0x00B7, 0x201A, 0x201E, 0x2030, 0x00C2, 0x00CA, 0x00C1,  // 0xE0-0xE7
			0x00CB, 0x00C8, 0x00CD, 0x00CE, 0x00CF, 0x00CC, 0x00D3, 0x00D4,  // 0xE8-0xEF
			0xF8FF, 0x00D2, 0x00DA, 0x00DB, 0x00D9, 0x0131, 0x02C6, 0x02DC,  // 0xF0-0xF7
			0x00AF, 0x02D8, 0x02D9, 0x02DA, 0x00B8, 0x02DD, 0x02DB, 0x02C7,  // 0xF8-0xFF
		};

		/// <summary>
		/// Byte 0xDB was U+00A4 CURRENCY SIGN before Mac OS 8.5 and is still named <c>currency</c> by PDF's
		/// MacRomanEncoding (PDF 32000 Annex D) and fontTools. A (1,0) cmap carries no character semantics — both
		/// codepoints name the same slot — so both resolve to 0xDB (spec §3.5, option A).
		/// </summary>
		public const int LegacyCurrencySignCodepoint = 0x00A4;
		public const byte EuroOrCurrencySignByte = 0xDB;

		private static readonly Dictionary<int, byte> UnicodeToHighHalfByte = BuildReverseMap();

		private static Dictionary<int, byte> BuildReverseMap()
		{
			var map = new Dictionary<int, byte>(HighHalfToUnicode.Length + 1);
			for (int i = 0; i < HighHalfToUnicode.Length; i++)
				map[HighHalfToUnicode[i]] = (byte)(0x80 + i);
			map[LegacyCurrencySignCodepoint] = EuroOrCurrencySignByte;
			return map;
		}

		/// <summary>
		/// Unicode scalar → Mac Roman byte (0x00..0xFF), or <see cref="NoMacRomanByte"/> (-1) when the character
		/// is not in the code page. ASCII (&lt; 0x80) is identity and never touches the dictionary.
		/// </summary>
		public static int UnicodeToMacRomanByte(int unicodeCodepoint)
		{
			if (unicodeCodepoint < 0)
				return NoMacRomanByte;
			if (unicodeCodepoint < 0x80)
				return unicodeCodepoint;
			return UnicodeToHighHalfByte.TryGetValue(unicodeCodepoint, out byte macRomanByte) ? macRomanByte : NoMacRomanByte;
		}

		/// <summary>Mac Roman byte → Unicode scalar (ASCII identity below 0x80). Diagnostic/test use.</summary>
		public static int MacRomanByteToUnicode(byte macRomanByte)
		{
			return macRomanByte < 0x80 ? macRomanByte : HighHalfToUnicode[macRomanByte - 0x80];
		}
	}
}