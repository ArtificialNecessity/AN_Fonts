namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Entry point for web-font container handling (plans/woff_web_fonts.md).
    /// Sniffs the 4-byte magic and either passes plain sfnt bytes through unchanged
    /// or decodes the WOFF1/WOFF2 container into a reconstructed sfnt stream.
    /// Malformed containers surface a <see cref="WebFontContainerDecodeFailure"/>;
    /// this class never throws on hostile input.
    /// </summary>
    public static class WebFontContainerDecoder
    {
        /// <summary>
        /// 30 MiB reconstructed-sfnt ceiling, matching Google woff2's kDefaultMaxSize
        /// (Chrome behavior) — decompression-bomb defense. Real-world worst case
        /// (full CJK Noto) reconstructs to ~20-25 MB.
        /// </summary>
        public const int ReconstructedSfntByteCountCap = 30 * 1024 * 1024;

        private const uint Woff1Magic = 0x774F4646; // 'wOFF'
        private const uint Woff2Magic = 0x774F4632; // 'wOF2'

        /// <summary>Sniffs the leading magic. Inputs shorter than 4 bytes are NotAWebFontContainer.</summary>
        public static WebFontContainerFormat SniffContainerFormat(byte[] fontFileBytes)
        {
            if (fontFileBytes == null || fontFileBytes.Length < 4)
            {
                return WebFontContainerFormat.NotAWebFontContainer;
            }
            uint leadingMagic = SystemCompressionWebFontDecompressor.ReadBigEndianUInt32(fontFileBytes, 0);
            switch (leadingMagic)
            {
                case Woff1Magic: return WebFontContainerFormat.Woff1;
                case Woff2Magic: return WebFontContainerFormat.Woff2;
                default: return WebFontContainerFormat.NotAWebFontContainer;
            }
        }

        /// <summary>
        /// Pass-through for plain sfnt inputs (zero-copy); container decode for WOFF1/WOFF2.
        /// On success <paramref name="sfntFontFileBytes"/> wraps bytes safe for stbtt_InitFont.
        /// </summary>
        public static bool TryDecodeToSfnt(byte[] fontFileBytes,
            out SfntFontFileBytes sfntFontFileBytes, out WebFontContainerDecodeFailure decodeFailure)
            => TryDecodeToSfnt(fontFileBytes, SystemCompressionWebFontDecompressor.Shared,
                out sfntFontFileBytes, out decodeFailure);

        /// <summary>Seam-injectable overload for tests and the later safe-managed decompressor swap.</summary>
        public static bool TryDecodeToSfnt(byte[] fontFileBytes, IWebFontDecompressor webFontDecompressor,
            out SfntFontFileBytes sfntFontFileBytes, out WebFontContainerDecodeFailure decodeFailure)
        {
            switch (SniffContainerFormat(fontFileBytes))
            {
                case WebFontContainerFormat.Woff1:
                {
                    if (!Woff1ContainerDecoder.TryDecode(fontFileBytes, webFontDecompressor,
                        ReconstructedSfntByteCountCap, out byte[] reconstructedSfntBytes, out decodeFailure))
                    {
                        sfntFontFileBytes = default;
                        return false;
                    }
                    sfntFontFileBytes = new SfntFontFileBytes(reconstructedSfntBytes);
                    return true;
                }

                case WebFontContainerFormat.Woff2:
                {
                    if (!Woff2ContainerDecoder.TryDecode(fontFileBytes, webFontDecompressor,
                        ReconstructedSfntByteCountCap, out byte[] reconstructedSfntBytes, out decodeFailure))
                    {
                        sfntFontFileBytes = default;
                        return false;
                    }
                    sfntFontFileBytes = new SfntFontFileBytes(reconstructedSfntBytes);
                    return true;
                }

                default:
                    // Ordinary sfnt bytes: pass through unchanged (zero cost).
                    sfntFontFileBytes = new SfntFontFileBytes(fontFileBytes);
                    decodeFailure = WebFontContainerDecodeFailure.None;
                    return true;
            }
        }
    }
}