namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Branded wrapper for a plain sfnt (TTF/OTF/TTC) byte stream — the OUTPUT of
    /// container decoding, safe to hand to stbtt_InitFont and the hinting engine.
    /// Never wrap raw user input in this type without going through
    /// <see cref="WebFontContainerDecoder.TryDecodeToSfnt"/> (pass-through inputs
    /// are wrapped there after the magic sniff).
    /// </summary>
    public readonly struct SfntFontFileBytes
    {
        public byte[] FontFileBytes { get; }

        internal SfntFontFileBytes(byte[] fontFileBytes)
        {
            FontFileBytes = fontFileBytes;
        }
    }
}