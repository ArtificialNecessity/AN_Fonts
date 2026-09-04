using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Layout
{
	/// <summary>OpenType glyph classes from GDEF GlyphClassDef (spec: 1 base, 2 ligature, 3 mark, 4 component; 0 = unclassified).</summary>
	public static class OpenTypeGlyphClass
	{
		public const int Unclassified = 0;
		public const int Base = 1;
		public const int Ligature = 2;
		public const int Mark = 3;
		public const int Component = 4;
	}

	/// <summary>
	/// GDEF glyph-classification reader for LookupFlag skipping (SilkyNvg plans/color_glyph_fonts.md §5.2):
	/// GlyphClassDef, MarkAttachClassDef, MarkGlyphSetsDef. The GDEF v1.3 ItemVariationStore is consumed elsewhere
	/// (FontInfoVariations.stbtt__TryParseGdefItemVariationStore) — this type covers the classification tables the
	/// variations path does not touch. Header (OpenType 1.9.1): majorVersion(u16)=1, minorVersion(u16),
	/// glyphClassDefOffset(u16)@4, attachListOffset@6, ligCaretListOffset@8, markAttachClassDefOffset@10,
	/// [v1.2+] markGlyphSetsDefOffset@12. All offsets bounds-validated against the table extent; a malformed or
	/// absent subtable yields class 0 / attach class 0 / not-in-set (fail-soft, never throws).
	/// </summary>
	public sealed class OpenTypeGdefTable
	{
		private readonly FakePtr<byte> _table;   // GDEF table start
		private readonly int _tableLength;
		private readonly int _glyphClassDefOffset;      // 0 = absent
		private readonly int _markAttachClassDefOffset; // 0 = absent
		private readonly int _markGlyphSetsDefOffset;   // 0 = absent (v1.2+ only)

		public bool HasGlyphClassDef => _glyphClassDefOffset != 0;

		private OpenTypeGdefTable(FakePtr<byte> table, int tableLength, int glyphClassDefOffset, int markAttachClassDefOffset, int markGlyphSetsDefOffset)
		{
			_table = table;
			_tableLength = tableLength;
			_glyphClassDefOffset = glyphClassDefOffset;
			_markAttachClassDefOffset = markAttachClassDefOffset;
			_markGlyphSetsDefOffset = markGlyphSetsDefOffset;
		}

		/// <summary>False when the table is absent, truncated, or not version 1.x. Never throws.</summary>
		public static bool TryParse(FakePtr<byte> fontData, int tableOffset, int tableLength, out OpenTypeGdefTable table)
		{
			table = null;
			if (tableOffset <= 0 || tableLength < 12 || (long)tableOffset + tableLength > fontData.RemainingLength) return false;
			var g = fontData + tableOffset;
			int majorVersion = ttUSHORT(g);
			int minorVersion = ttUSHORT(g + 2);
			if (majorVersion != 1) return false;
			int glyphClassDefOffset = ttUSHORT(g + 4);
			int markAttachClassDefOffset = ttUSHORT(g + 10);
			int markGlyphSetsDefOffset = minorVersion >= 2 && tableLength >= 14 ? ttUSHORT(g + 12) : 0;
			// Bounds: a subtable needs at least its 4-byte header inside the table; otherwise treat as absent.
			if (glyphClassDefOffset != 0 && glyphClassDefOffset + 4 > tableLength) glyphClassDefOffset = 0;
			if (markAttachClassDefOffset != 0 && markAttachClassDefOffset + 4 > tableLength) markAttachClassDefOffset = 0;
			if (markGlyphSetsDefOffset != 0 && markGlyphSetsDefOffset + 4 > tableLength) markGlyphSetsDefOffset = 0;
			table = new OpenTypeGdefTable(g, tableLength, glyphClassDefOffset, markAttachClassDefOffset, markGlyphSetsDefOffset);
			return true;
		}

		/// <summary>OpenTypeGlyphClass of the glyph (0 when GlyphClassDef is absent or the glyph is uncovered).</summary>
		public int GetGlyphClass(int glyph)
		{
			if (_glyphClassDefOffset == 0) return OpenTypeGlyphClass.Unclassified;
			int c = stbtt__GetGlyphClass(_table + _glyphClassDefOffset, glyph);
			return c < 0 ? OpenTypeGlyphClass.Unclassified : c;
		}

		/// <summary>Mark attachment class from MarkAttachClassDef (0 when absent/uncovered).</summary>
		public int GetMarkAttachClass(int glyph)
		{
			if (_markAttachClassDefOffset == 0) return 0;
			int c = stbtt__GetGlyphClass(_table + _markAttachClassDefOffset, glyph);
			return c < 0 ? 0 : c;
		}

		/// <summary>True when MarkGlyphSetsDef set <paramref name="setIndex"/> covers the glyph. MarkGlyphSets table:
		/// format(u16)=1, markGlyphSetCount(u16), Offset32 coverageOffsets[count] (from MarkGlyphSets table start).</summary>
		public bool IsInMarkGlyphSet(int setIndex, int glyph)
		{
			if (_markGlyphSetsDefOffset == 0 || setIndex < 0) return false;
			var sets = _table + _markGlyphSetsDefOffset;
			if (ttUSHORT(sets) != 1) return false;
			int count = ttUSHORT(sets + 2);
			if (setIndex >= count || _markGlyphSetsDefOffset + 4 + count * 4L > _tableLength) return false;
			long coverageOffset = ttULONG(sets + 4 + 4 * setIndex);
			if (coverageOffset == 0 || _markGlyphSetsDefOffset + coverageOffset + 4 > _tableLength) return false;
			return stbtt__GetCoverageIndex(sets + (int)coverageOffset, glyph) >= 0;
		}
	}
}