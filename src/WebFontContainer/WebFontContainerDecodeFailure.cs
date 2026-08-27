namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Machine-readable reason a WOFF1/WOFF2 container failed to decode to sfnt
    /// (plans/woff_web_fonts.md). Same pattern as TrueTypeYHintingFailure: hostile
    /// or malformed input NEVER throws — it surfaces one of these codes.
    /// </summary>
    public enum WebFontContainerDecodeFailureCode
    {
        None = 0,

        // ── Container-agnostic ──
        TruncatedContainerHeader,
        ReconstructedSfntSizeExceedsCap,
        TotalSfntSizeMismatch,

        // ── WOFF1 header / directory ──
        Woff1ReservedFieldNonZero,
        Woff1DeclaredLengthMismatch,
        Woff1TableDirectoryTruncated,
        Woff1TableOffsetOutOfBounds,
        Woff1TableCompressedLargerThanOriginal,
        Woff1TableDirectoryNotAscendingByTag,
        Woff1TableRangesOverlapHeaderOrDirectory,

        // ── zlib framing / inflate (per WOFF1 table) ──
        ZlibHeaderInvalid,
        ZlibDictionaryFlagUnsupported,
        ZlibInflateTruncatedOrCorrupt,
        ZlibInflatedLengthMismatch,
        ZlibAdler32Mismatch,

        // ── WOFF2 (Phase 2/3 — reserved now so codes stay stable) ──
        Woff2BrotliStreamCorrupt,
        Woff2BrotliDecodedLengthMismatch,
        Woff2BrotliUnsupportedOnThisTargetFramework,
        Woff2VariableIntegerOverflow,
        Woff2TableDirectoryInvalid,
        Woff2TransformedLocaMustBeZeroLength,
        Woff2GlyfTripletStreamOverrun,
        Woff2GlyfSubStreamBoundsInvalid,
        Woff2CompositeGlyphStreamOverrun,
        Woff2HmtxReconstructionInvalid,
        Woff2CollectionDirectoryInvalid,

        // ── WOFF2 strictness (plans/WOFF_decoder_strictness.md) ──
        Woff2BlockLayoutInvalid,
        Woff2GlyfOrigLengthMismatch,
        // ── StrictWoff parsing mode ──
        NotAWoffContainerInStrictMode,
    }

    /// <summary>
    /// Failure detail for a container decode attempt: the code plus a short
    /// human-readable context string (offsets, table tag, expected/actual sizes).
    /// </summary>
    public readonly struct WebFontContainerDecodeFailure
    {
        public WebFontContainerDecodeFailureCode FailureCode { get; }
        public string FailureDetail { get; }

        public WebFontContainerDecodeFailure(WebFontContainerDecodeFailureCode failureCode, string failureDetail)
        {
            FailureCode = failureCode;
            FailureDetail = failureDetail;
        }

        public static WebFontContainerDecodeFailure None
            => new WebFontContainerDecodeFailure(WebFontContainerDecodeFailureCode.None, string.Empty);

        public override string ToString()
            => FailureCode == WebFontContainerDecodeFailureCode.None
                ? "None"
                : FailureCode + ": " + FailureDetail;
    }
}