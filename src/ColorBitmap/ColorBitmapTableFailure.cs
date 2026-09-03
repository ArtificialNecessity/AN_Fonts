namespace StbTrueTypeSharp.ColorBitmap
{
    /// <summary>
    /// Machine-readable reason a CBLC/CBDT (or sbix) colour bitmap lookup/decode failed
    /// (plans/color_glyph_fonts.md §4.1). Same pattern as WebFontContainerDecodeFailure:
    /// hostile or malformed input NEVER throws — it surfaces one of these codes.
    /// </summary>
    public enum ColorBitmapTableFailureCode
    {
        None = 0,

        // ── table presence / header ──
        TablesAbsent,
        CblcTruncatedHeader,
        CblcUnsupportedVersion,
        CbdtTruncatedHeader,
        CbdtUnsupportedVersion,
        CblcBitmapSizeRecordOutOfBounds,
        CblcStrikeCountUnreasonable,

        // ── IndexSubtableList / IndexSubtable ──
        CblcIndexSubtableListOutOfBounds,
        CblcIndexSubtableRecordsNotAscending,
        CblcIndexSubtableRecordsOverlap,
        CblcIndexSubtableOutOfBounds,
        CblcIndexSubtableFormatUnknown,
        CblcIndexSubtableArrayOutOfBounds,
        CblcIndexSubtableGlyphIdsNotSorted,

        // ── glyph lookup ──
        StrikeIndexOutOfRange,
        GlyphNotInStrike,
        GlyphImageDataOutOfBounds,
        GlyphImageDataTruncated,

        // ── glyph data formats ──
        ImageFormatUnknown,
        ImageFormatIsPngNotRaw,
        ImageFormatIsRawNotPng,
        GreyscaleStrikeUnsupported,
        RawBitmapSizeMismatch,
        CompositeRecursionTooDeep,
        CompositeComponentOutOfBounds,
        PngDeclaredLengthExceedsData,
    }

    /// <summary>Failure detail for a colour bitmap operation: code + short human-readable context.</summary>
    public readonly struct ColorBitmapTableFailure
    {
        public ColorBitmapTableFailureCode FailureCode { get; }
        public string FailureDetail { get; }

        public ColorBitmapTableFailure(ColorBitmapTableFailureCode failureCode, string failureDetail)
        {
            FailureCode = failureCode;
            FailureDetail = failureDetail;
        }

        public static ColorBitmapTableFailure None
            => new ColorBitmapTableFailure(ColorBitmapTableFailureCode.None, string.Empty);

        public bool IsFailure => FailureCode != ColorBitmapTableFailureCode.None;

        public override string ToString()
            => FailureCode == ColorBitmapTableFailureCode.None ? "None" : FailureCode + ": " + FailureDetail;
    }
}