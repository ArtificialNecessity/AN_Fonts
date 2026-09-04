namespace StbTrueTypeSharp.Layout
{
	/// <summary>
	/// LookupFlag-driven glyph skipping for GSUB matching (SilkyNvg plans/color_glyph_fonts.md §5.2 — the shaping
	/// plan's "skippy iterator"). A skipped glyph is invisible to sequence matching (ligature components, context
	/// input/backtrack/lookahead) but stays in the buffer. Noto emoji has LookupFlag 0 everywhere — the no-op
	/// fast path (<see cref="GsubNoSkipGlyphSkipper"/>) covers it; the GDEF-backed filter exists for spec compliance.
	/// </summary>
	public interface IGsubGlyphSkipper
	{
		bool ShouldSkip(ushort glyphId);
	}

	/// <summary>LookupFlag == 0 (or no usable GDEF): nothing is ever skipped. Allocation-free singleton.</summary>
	public sealed class GsubNoSkipGlyphSkipper : IGsubGlyphSkipper
	{
		public static readonly GsubNoSkipGlyphSkipper Instance = new GsubNoSkipGlyphSkipper();
		private GsubNoSkipGlyphSkipper() { }
		public bool ShouldSkip(ushort glyphId) => false;
	}

	/// <summary>
	/// GDEF-backed LookupFlag filter (OpenType 1.9.1 §LookupFlag, HarfBuzz skipping semantics):
	/// IgnoreBaseGlyphs 0x0002 (class 1), IgnoreLigatures 0x0004 (class 2), IgnoreMarks 0x0008 (class 3),
	/// UseMarkFilteringSet 0x0010 (marks NOT in the set are skipped), MarkAttachmentTypeMask 0xFF00 (marks whose
	/// MarkAttachClass differs are skipped). RightToLeft 0x0001 affects cursive attachment only — ignored here.
	/// </summary>
	public sealed class GsubGdefGlyphSkipper : IGsubGlyphSkipper
	{
		public const int FlagIgnoreBaseGlyphs = 0x0002;
		public const int FlagIgnoreLigatures = 0x0004;
		public const int FlagIgnoreMarks = 0x0008;
		public const int FlagUseMarkFilteringSet = 0x0010;
		public const int MarkAttachmentTypeMask = 0xFF00;

		private readonly OpenTypeGdefTable _gdef;
		private readonly int _lookupFlag;
		private readonly int _markFilteringSet;   // valid only when FlagUseMarkFilteringSet is set

		/// <summary>True when this flag needs GDEF-backed filtering at all (use the no-skip singleton otherwise).</summary>
		public static bool FlagNeedsSkipping(int lookupFlag)
			=> (lookupFlag & (FlagIgnoreBaseGlyphs | FlagIgnoreLigatures | FlagIgnoreMarks | FlagUseMarkFilteringSet | MarkAttachmentTypeMask)) != 0;

		public GsubGdefGlyphSkipper(OpenTypeGdefTable gdef, int lookupFlag, int markFilteringSet)
		{
			_gdef = gdef;
			_lookupFlag = lookupFlag;
			_markFilteringSet = markFilteringSet;
		}

		public bool ShouldSkip(ushort glyphId)
		{
			int glyphClass = _gdef.GetGlyphClass(glyphId);
			switch (glyphClass)
			{
				case OpenTypeGlyphClass.Base:
					return (_lookupFlag & FlagIgnoreBaseGlyphs) != 0;
				case OpenTypeGlyphClass.Ligature:
					return (_lookupFlag & FlagIgnoreLigatures) != 0;
				case OpenTypeGlyphClass.Mark:
					if ((_lookupFlag & FlagIgnoreMarks) != 0) return true;
					if ((_lookupFlag & FlagUseMarkFilteringSet) != 0) return !_gdef.IsInMarkGlyphSet(_markFilteringSet, glyphId);
					int attachmentType = (_lookupFlag & MarkAttachmentTypeMask) >> 8;
					if (attachmentType != 0) return _gdef.GetMarkAttachClass(glyphId) != attachmentType;
					return false;
				default:
					return false; // class 0 (unclassified) and 4 (component) are never skipped by LookupFlag
			}
		}
	}
}