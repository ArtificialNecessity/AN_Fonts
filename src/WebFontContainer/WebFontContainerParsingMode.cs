namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Controls how <see cref="WebFontContainerDecoder.TryDecodeToSfnt(byte[], WebFontContainerParsingMode, out SfntFontFileBytes, out WebFontContainerDecodeFailure)"/>
    /// treats input whose leading magic is NOT a WOFF1/WOFF2 signature
    /// (plans/WOFF_decoder_strictness.md §3.4 header-signature-001).
    /// </summary>
    public enum WebFontContainerParsingMode
    {
        /// <summary>
        /// Default: unknown magic is assumed to be plain sfnt (.ttf/.otf/.ttc) bytes and
        /// passed through zero-copy. Rejection then falls to the downstream sfnt parser.
        /// </summary>
        LenientSfntPassThrough = 0,

        /// <summary>
        /// The caller KNOWS the bytes are supposed to be a WOFF container (e.g. a CSS
        /// @font-face source with format("woff2")): any magic other than 'wOFF'/'wOF2'
        /// is rejected outright with
        /// <see cref="WebFontContainerDecodeFailureCode.NotAWoffContainerInStrictMode"/>.
        /// </summary>
        StrictWoff = 1,
    }
}