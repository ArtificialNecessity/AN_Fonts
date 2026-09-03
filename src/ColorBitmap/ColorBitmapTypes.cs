namespace StbTrueTypeSharp.ColorBitmap
{
    /// <summary>Index of a strike (BitmapSize record) inside a CBLC table. -1 = none.</summary>
    public readonly struct ColorBitmapStrikeIndex
    {
        public int Value { get; }
        public ColorBitmapStrikeIndex(int value) { Value = value; }
        public static ColorBitmapStrikeIndex None => new ColorBitmapStrikeIndex(-1);
        public bool IsNone => Value < 0;
        public override string ToString() => IsNone ? "strike:none" : "strike:" + Value;
    }

    /// <summary>
    /// EBDT/CBDT glyph image data format (OpenType 1.9.1 EBDT §"Glyph bitmap data formats" + CBDT 17/18/19).
    /// Raw formats carry bitDepth-32 premultiplied BGRA when the strike's bitDepth is 32.
    /// </summary>
    public enum ColorBitmapImageFormat : ushort
    {
        Unknown = 0,
        /// <summary>SmallGlyphMetrics + byte-aligned bitmap.</summary>
        SmallMetricsByteAligned = 1,
        /// <summary>SmallGlyphMetrics + bit-aligned bitmap.</summary>
        SmallMetricsBitAligned = 2,
        /// <summary>(obsolete, not defined) — reserved.</summary>
        Reserved3 = 3,
        /// <summary>(obsolete, not defined) — reserved.</summary>
        Reserved4 = 4,
        /// <summary>Bit-aligned bitmap only; metrics live in the IndexSubtable (formats 2/5).</summary>
        MetricsInIndexBitAligned = 5,
        /// <summary>BigGlyphMetrics + byte-aligned bitmap.</summary>
        BigMetricsByteAligned = 6,
        /// <summary>BigGlyphMetrics + bit-aligned bitmap.</summary>
        BigMetricsBitAligned = 7,
        /// <summary>SmallGlyphMetrics + pad + composite component list.</summary>
        SmallMetricsComposite = 8,
        /// <summary>BigGlyphMetrics + composite component list.</summary>
        BigMetricsComposite = 9,
        /// <summary>SmallGlyphMetrics + uint32 dataLen + PNG.</summary>
        SmallMetricsPng = 17,
        /// <summary>BigGlyphMetrics + uint32 dataLen + PNG.</summary>
        BigMetricsPng = 18,
        /// <summary>uint32 dataLen + PNG; metrics live in the IndexSubtable (formats 2/5).</summary>
        MetricsInIndexPng = 19,
    }

    /// <summary>Where a glyph's metrics were read from.</summary>
    public enum ColorBitmapGlyphMetricsSource : byte
    {
        /// <summary>SmallGlyphMetrics in the glyph data (one direction; per BitmapSize.flags).</summary>
        SmallGlyphMetrics = 1,
        /// <summary>BigGlyphMetrics in the glyph data (both directions).</summary>
        BigGlyphMetrics = 2,
        /// <summary>BigGlyphMetrics shared by the whole range, stored in IndexSubtable format 2/5.</summary>
        IndexSubtable = 3,
    }

    /// <summary>
    /// Normalised glyph metrics in STRIKE PIXELS (at the strike's ppemX/ppemY). Bearings are relative to the
    /// glyph origin; <see cref="HoriBearingY"/> is the distance from the baseline UP to the bitmap's top edge
    /// (y-up, per spec). Vertical fields are valid only when <see cref="HasVerticalMetrics"/>.
    /// </summary>
    public readonly struct ColorBitmapGlyphMetrics
    {
        public byte Width { get; }
        public byte Height { get; }
        public sbyte HoriBearingX { get; }
        public sbyte HoriBearingY { get; }
        public byte HoriAdvance { get; }
        public bool HasVerticalMetrics { get; }
        public sbyte VertBearingX { get; }
        public sbyte VertBearingY { get; }
        public byte VertAdvance { get; }
        public ColorBitmapGlyphMetricsSource Source { get; }

        public ColorBitmapGlyphMetrics(byte width, byte height, sbyte horiBearingX, sbyte horiBearingY, byte horiAdvance,
            bool hasVerticalMetrics, sbyte vertBearingX, sbyte vertBearingY, byte vertAdvance, ColorBitmapGlyphMetricsSource source)
        {
            Width = width; Height = height;
            HoriBearingX = horiBearingX; HoriBearingY = horiBearingY; HoriAdvance = horiAdvance;
            HasVerticalMetrics = hasVerticalMetrics;
            VertBearingX = vertBearingX; VertBearingY = vertBearingY; VertAdvance = vertAdvance;
            Source = source;
        }

        public int PixelCount => Width * Height;
    }

    /// <summary>SbitLineMetrics (12 bytes) — strike-level line metrics for one layout direction.</summary>
    public readonly struct ColorBitmapStrikeLineMetrics
    {
        public sbyte Ascender { get; }
        public sbyte Descender { get; }
        public byte WidthMax { get; }
        public sbyte CaretSlopeNumerator { get; }
        public sbyte CaretSlopeDenominator { get; }
        public sbyte CaretOffset { get; }
        public sbyte MinOriginSB { get; }
        public sbyte MinAdvanceSB { get; }
        public sbyte MaxBeforeBL { get; }
        public sbyte MinAfterBL { get; }

        public ColorBitmapStrikeLineMetrics(sbyte ascender, sbyte descender, byte widthMax, sbyte caretSlopeNumerator,
            sbyte caretSlopeDenominator, sbyte caretOffset, sbyte minOriginSB, sbyte minAdvanceSB, sbyte maxBeforeBL, sbyte minAfterBL)
        {
            Ascender = ascender; Descender = descender; WidthMax = widthMax;
            CaretSlopeNumerator = caretSlopeNumerator; CaretSlopeDenominator = caretSlopeDenominator; CaretOffset = caretOffset;
            MinOriginSB = minOriginSB; MinAdvanceSB = minAdvanceSB; MaxBeforeBL = maxBeforeBL; MinAfterBL = minAfterBL;
        }
    }

    /// <summary>One located glyph image inside CBDT: where the bytes are, which format, and its metrics.</summary>
    public readonly struct ColorBitmapGlyphRecord
    {
        public ColorBitmapStrikeIndex Strike { get; }
        public int GlyphId { get; }
        public ColorBitmapImageFormat ImageFormat { get; }
        public ColorBitmapGlyphMetrics Metrics { get; }
        /// <summary>Absolute offset (in the font byte array) of the glyph's data block (metrics-prefixed where the format says so).</summary>
        public int DataOffset { get; }
        /// <summary>Length in bytes of the glyph's data block as given by the index subtable.</summary>
        public int DataLength { get; }
        /// <summary>Absolute offset of the PNG bytes (PNG formats only), else -1.</summary>
        public int PngOffset { get; }
        /// <summary>Declared PNG byte length (PNG formats only), else 0.</summary>
        public int PngLength { get; }

        public ColorBitmapGlyphRecord(ColorBitmapStrikeIndex strike, int glyphId, ColorBitmapImageFormat imageFormat,
            ColorBitmapGlyphMetrics metrics, int dataOffset, int dataLength, int pngOffset, int pngLength)
        {
            Strike = strike; GlyphId = glyphId; ImageFormat = imageFormat; Metrics = metrics;
            DataOffset = dataOffset; DataLength = dataLength; PngOffset = pngOffset; PngLength = pngLength;
        }

        public bool IsPng => ImageFormat == ColorBitmapImageFormat.SmallMetricsPng
                          || ImageFormat == ColorBitmapImageFormat.BigMetricsPng
                          || ImageFormat == ColorBitmapImageFormat.MetricsInIndexPng;

        public bool IsComposite => ImageFormat == ColorBitmapImageFormat.SmallMetricsComposite
                                || ImageFormat == ColorBitmapImageFormat.BigMetricsComposite;
    }

    /// <summary>A decoded raw-format glyph: premultiplied RGBA8 (converted from the table's premultiplied BGRA), row-major, tightly packed.</summary>
    public readonly struct ColorBitmapDecodedPixels
    {
        public int Width { get; }
        public int Height { get; }
        public byte[] PremultipliedRgba { get; }

        public ColorBitmapDecodedPixels(int width, int height, byte[] premultipliedRgba)
        {
            Width = width; Height = height; PremultipliedRgba = premultipliedRgba;
        }
    }
}