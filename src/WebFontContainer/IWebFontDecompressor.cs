namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Decompression seam (plans/woff_web_fonts.md): the ONLY boundary through which
    /// WOFF decoding touches a compression codec. Bringup implementation is
    /// <see cref="SystemCompressionWebFontDecompressor"/> (BCL DeflateStream/BrotliDecoder);
    /// a later safe-managed swap replaces one file without touching reconstruction code.
    /// Implementations decompress into an EXACTLY-sized caller-allocated destination and
    /// fail (never throw) on corrupt input, length mismatch, or checksum mismatch.
    /// </summary>
    public interface IWebFontDecompressor
    {
        /// <summary>
        /// Inflates one zlib-framed stream (RFC 1950: 2-byte header, deflate body,
        /// Adler-32 trailer) occupying [sourceOffset, sourceOffset+sourceLength) of
        /// <paramref name="sourceBytes"/>. Must fill <paramref name="destinationBytes"/>
        /// completely and exactly, and verify the Adler-32 trailer.
        /// </summary>
        bool TryInflateZlibFramedStream(byte[] sourceBytes, int sourceOffset, int sourceLength,
            byte[] destinationBytes, out WebFontContainerDecodeFailure inflateFailure);

        /// <summary>
        /// Decodes one raw Brotli stream (RFC 7932, no framing) occupying
        /// [sourceOffset, sourceOffset+sourceLength) of <paramref name="sourceBytes"/>.
        /// Must fill <paramref name="destinationBytes"/> completely and exactly.
        /// On netstandard2.0 the BCL implementation reports
        /// Woff2BrotliUnsupportedOnThisTargetFramework.
        /// </summary>
        bool TryDecodeBrotliStream(byte[] sourceBytes, int sourceOffset, int sourceLength,
            byte[] destinationBytes, out WebFontContainerDecodeFailure brotliFailure);
    }
}