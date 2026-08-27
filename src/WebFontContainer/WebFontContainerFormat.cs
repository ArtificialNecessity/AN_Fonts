namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Result of sniffing the first four bytes of a candidate font file
    /// (plans/woff_web_fonts.md). Plain sfnt (TTF/OTF/TTC) inputs report
    /// <see cref="NotAWebFontContainer"/> and pass through undecoded.
    /// </summary>
    public enum WebFontContainerFormat
    {
        /// <summary>Magic is not 'wOFF' or 'wOF2' — treat as ordinary sfnt bytes.</summary>
        NotAWebFontContainer = 0,

        /// <summary>W3C WOFF 1.0 container ('wOFF'): per-table zlib compression.</summary>
        Woff1 = 1,

        /// <summary>W3C WOFF2 container ('wOF2'): single Brotli stream + table transforms.</summary>
        Woff2 = 2,
    }
}