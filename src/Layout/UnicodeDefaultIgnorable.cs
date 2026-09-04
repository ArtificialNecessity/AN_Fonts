namespace StbTrueTypeSharp.Layout
{
	/// <summary>
	/// Unicode Default_Ignorable_Code_Point test (HarfBuzz _hb_glyph_info_is_default_ignorable law,
	/// plans/color_glyph_fonts.md §5.4). Default-ignorables are MAYBE-skipped during GSUB sequence matching
	/// (skipped unless they match the expected element — HarfBuzz matcher_t::may_skip) and, when they survive
	/// shaping, are deleted by the text iterator (zero-width, no glyph). Ranges mirror HarfBuzz's
	/// hb-unicode.hh is_default_ignorable (Unicode 15 DI list minus the HB-specific exclusions).
	/// </summary>
	public static class UnicodeDefaultIgnorable
	{
		public static bool IsDefaultIgnorable(int codepoint)
		{
			int plane = codepoint >> 16;
			if (plane == 0)
			{
				// BMP
				switch (codepoint >> 8)
				{
					case 0x00: return codepoint == 0x00AD;                             // SOFT HYPHEN
					case 0x03: return codepoint == 0x034F;                             // CGJ
					case 0x06: return codepoint == 0x061C;                             // ARABIC LETTER MARK
					case 0x11: return codepoint == 0x115F || codepoint == 0x1160;      // HANGUL FILLERs
					case 0x17: return codepoint == 0x17B4 || codepoint == 0x17B5;      // KHMER VOWEL INHERENT
					case 0x18: return codepoint >= 0x180B && codepoint <= 0x180E;      // MONGOLIAN FVS + MVS
					case 0x20:
						return (codepoint >= 0x200B && codepoint <= 0x200F)             // ZWSP..RLM (incl. ZWJ/ZWNJ)
							|| (codepoint >= 0x202A && codepoint <= 0x202E)             // bidi embeddings
							|| (codepoint >= 0x2060 && codepoint <= 0x206F);            // WJ..invisible operators + deprecated
					case 0x31: return codepoint == 0x3164;                             // HANGUL FILLER
					case 0xFE: return (codepoint >= 0xFE00 && codepoint <= 0xFE0F)     // variation selectors (VS15/VS16!)
						|| codepoint == 0xFEFF;                                         // ZWNBSP/BOM
					case 0xFF: return codepoint == 0xFFA0                              // HALFWIDTH HANGUL FILLER
						|| (codepoint >= 0xFFF0 && codepoint <= 0xFFF8);                // specials
					default: return false;
				}
			}
			if (plane == 1)
				return (codepoint >= 0x1BCA0 && codepoint <= 0x1BCA3)               // shorthand format controls
					|| (codepoint >= 0x1D173 && codepoint <= 0x1D17A);              // musical format controls
			if (plane == 14)
				return codepoint <= 0xE0FFF;                                        // tags + VS supplement (whole block is DI)
			return false;
		}
	}
}