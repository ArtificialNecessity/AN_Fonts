namespace StbTrueTypeSharp.Variations
{
	/// <summary>
	/// Why a variation table was rejected. Same contract as the WOFF decoder and TrueType hinting
	/// failures: malformed variation data NEVER throws at draw time — the font simply reports
	/// stbtt_HasVariations == false (renders the default instance) and this code says why.
	/// </summary>
	public enum FontVariationParseFailureCode
	{
		None = 0,
		FvarTruncatedHeader = 1,
		FvarUnsupportedVersion = 2,
		FvarAxisCountZero = 3,
		FvarAxisRecordOutOfBounds = 4,
		FvarAxisRangeInvalid = 5,          // not min <= default <= max
		FvarInstanceRecordOutOfBounds = 6,
		AvarTruncatedHeader = 7,
		AvarAxisCountMismatch = 8,
		AvarSegmentMapOutOfBounds = 9,
		GvarTruncatedHeader = 10,
		GvarUnsupportedVersion = 11,
		GvarAxisCountMismatch = 12,
		GvarGlyphCountMismatch = 13,
		GvarOffsetArrayOutOfBounds = 14,
		GvarSharedTuplesOutOfBounds = 15,
		GvarGlyphVariationDataOutOfBounds = 16,
		GvarTupleHeaderOutOfBounds = 17,
		GvarSharedTupleIndexOutOfRange = 18,
		GvarPointNumbersOutOfBounds = 19,
		GvarPointNumberOutOfRange = 20,
		GvarPackedDeltasOutOfBounds = 21,
		ItemVariationStoreTruncated = 22,
		ItemVariationStoreUnsupportedFormat = 23,
		ItemVariationStoreRegionListOutOfBounds = 24,
		ItemVariationStoreAxisCountMismatch = 25,
		ItemVariationStoreDataOutOfBounds = 26,
		DeltaSetIndexMapOutOfBounds = 27,
		HvarTruncatedHeader = 28,
		MvarTruncatedHeader = 29,
		MvarValueRecordsOutOfBounds = 30,
	}

	/// <summary>Rich failure record: which code, which table, and a human-readable detail string.</summary>
	public readonly struct FontVariationParseFailure
	{
		public FontVariationParseFailureCode Code { get; }
		public string TableTag { get; }
		public string Detail { get; }

		public FontVariationParseFailure(FontVariationParseFailureCode code, string tableTag, string detail)
		{
			Code = code;
			TableTag = tableTag;
			Detail = detail;
		}

		public static readonly FontVariationParseFailure None = new FontVariationParseFailure(FontVariationParseFailureCode.None, null, null);
		public bool IsFailure => Code != FontVariationParseFailureCode.None;

		public override string ToString() => IsFailure ? "'" + TableTag + "': " + Code + " — " + Detail : "(no failure)";
	}
}