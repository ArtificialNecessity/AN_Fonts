using StbTrueTypeSharp.Layout;
using StbTrueTypeSharp.Variations;

namespace StbTrueTypeSharp
{
	/// <summary>
	/// Part D (SilkyNvg plans/font_variable_support_fvar_avar_gvar_hvar.md §9): GSUB 'rvrn' (Required Variation Alternates)
	/// + FeatureVariations = variation-dependent GLYPH SELECTION. This is the one place glyph selection happens; the
	/// substituted index then flows through every existing outline/metrics/kerning/raster path unchanged. Applied to static
	/// fonts too ('rvrn' is a required feature; a fontTools-instanced twin is a static font with the winner baked in).
	/// GSUB is located at InitFont and PARSED LAZILY; a malformed GSUB makes substitution the identity for this face with
	/// the reason in <see cref="stbtt_GsubParseFailure"/> — never a throw at draw time.
	/// </summary>
	public partial class FontInfo
	{
		/// <summary>GSUB table offset (0 = absent), resolved in stbtt_InitFont_internal alongside GPOS.</summary>
		public int gsub;

		private OpenTypeGsubTable _gsubTable;
		private bool _gsubResolved;
		private FontVariationParseFailure _gsubParseFailure = FontVariationParseFailure.None;

		// Last-coords memo: Fontstash resolves one instance per text run, so consecutive calls repeat the same coords.
		private FontVariationNormalizedCoordinates _gsubMemoCoords;
		private GsubResolvedLookupSet _gsubMemoLookups;
		private bool _gsubMemoValid;

		/// <summary>True when GSUB parsed AND the default LangSys reaches an 'rvrn' feature or names a required feature.</summary>
		public bool stbtt_HasRequiredVariationAlternates
		{
			get { var t = stbtt__GetGsubTable(); return t != null && t.HasRequiredVariationAlternates; }
		}

		/// <summary>Why GSUB was rejected (None when fine or when the font simply has no GSUB).</summary>
		public FontVariationParseFailure stbtt_GsubParseFailure
		{
			get { stbtt__GetGsubTable(); return _gsubParseFailure; }
		}

		/// <summary>
		/// Diagnostic (spec §9.8): lookups reachable through 'rvrn'/required at the DEFAULT instance whose type is not
		/// SingleSubst — this milestone skips them. Per-instance counts come from <see cref="stbtt_ResolveRequiredVariationLookups"/>.
		/// </summary>
		public int stbtt_GsubUnsupportedLookupCount => stbtt_ResolveRequiredVariationLookups(FontVariationNormalizedCoordinates.Default).UnsupportedLookupCount;

		/// <summary>
		/// Diagnostic (spec §9.9): true when GPOS is version 1.1 with a non-NULL featureVariationsOffset — i.e. the font
		/// wants region-dependent KERNING lookups, which this milestone does not apply (the FeatureVariations reader is
		/// table-agnostic and tested; the GPOS hook waits for a fixture — see the block comment at stbtt__ResolveGposKernLookups).
		/// </summary>
		public bool stbtt_HasUnappliedGposFeatureVariations
		{
			get
			{
				if (this.gpos == 0) return false;
				int gposLength = stbtt__find_table_length("GPOS");
				if (gposLength < 14) return false;
				var g = this.data + this.gpos;
				return Common.ttUSHORT(g) == 1 && Common.ttUSHORT(g + 2) >= 1 && Common.ttULONG(g + 10) != 0;
			}
		}

		internal OpenTypeGsubTable stbtt__GetGsubTable()
		{
			if (_gsubResolved) return _gsubTable;
			_gsubResolved = true;
			_gsubTable = null;
			if (this.gsub == 0) return null;
			// fvar axis count bounds ConditionFormat1.axisIndex (spec: invalid index => record ignored). Static font => 0 axes,
			// so any axis-range condition is ignored and only universal records can apply — exactly the spec's reading.
			int axisCount = stbtt__GetVariationTables()?.Fvar.AxisCount ?? 0;
			if (!OpenTypeGsubTable.TryParse(this.data, this.gsub, stbtt__find_table_length("GSUB"), axisCount, out _gsubTable, out _gsubParseFailure))
				_gsubTable = null;
			return _gsubTable;
		}

		/// <summary>The GSUB lookups in force at the instance (spec §9.4 resolution). Empty when nothing applies.</summary>
		public GsubResolvedLookupSet stbtt_ResolveRequiredVariationLookups(in FontVariationNormalizedCoordinates coords)
		{
			var table = stbtt__GetGsubTable();
			if (table == null || !table.HasRequiredVariationAlternates) return GsubResolvedLookupSet.Empty;
			if (_gsubMemoValid && _gsubMemoCoords.Equals(coords)) return _gsubMemoLookups;
			_gsubMemoLookups = table.ResolveRequiredVariationLookups(coords);
			_gsubMemoCoords = coords;
			_gsubMemoValid = true;
			return _gsubMemoLookups;
		}

		/// <summary>
		/// cmap glyph -> glyph after 'rvrn' (+ required feature) at this instance. Identity when nothing applies. NOTE the
		/// default instance is NOT a no-op for a font whose FeatureVariations has a record matching coordinate 0 (WPT
		/// FontStyleTest: f, r -> .mono at slnt 0) — the no-op guarantee is for OUTLINES/METRICS, not for glyph selection.
		/// Glyph 0 (.notdef) is never substituted.
		/// </summary>
		public int stbtt_SubstituteGlyphVar(int glyph_index, in FontVariationNormalizedCoordinates coords)
		{
			if (glyph_index <= 0) return glyph_index;
			var lookups = stbtt_ResolveRequiredVariationLookups(coords);
			if (lookups.IsEmpty) return glyph_index;
			int substituted = _gsubTable.ApplySingleSubstitutionLookups(lookups, glyph_index);
			return substituted > 0 && substituted < this.numGlyphs ? substituted : glyph_index; // never produce .notdef or an out-of-range id from a real glyph
		}

		/// <summary>stbtt_FindGlyphIndex then stbtt_SubstituteGlyphVar. 0 (no cmap entry) stays 0.</summary>
		public int stbtt_FindGlyphIndexVar(int unicode_codepoint, in FontVariationNormalizedCoordinates coords)
		{
			int glyph = stbtt_FindGlyphIndex(unicode_codepoint);
			return glyph == 0 ? 0 : stbtt_SubstituteGlyphVar(glyph, coords);
		}
	}
}