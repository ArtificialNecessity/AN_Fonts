using System;

namespace StbTrueTypeSharp.TrueTypeHinting.FontFace
{
    /// <summary>Font-unit metrics used to construct one glyph's four TrueType phantom points.</summary>
    internal readonly struct TrueTypeHintingGlyphMetrics
    {
        internal TrueTypeHintingGlyphMetrics(int advanceWidthFontUnits, int leftSideBearingFontUnits,
            int advanceHeightFontUnits, int topSideBearingFontUnits)
        {
            AdvanceWidthFontUnits = advanceWidthFontUnits;
            LeftSideBearingFontUnits = leftSideBearingFontUnits;
            AdvanceHeightFontUnits = advanceHeightFontUnits;
            TopSideBearingFontUnits = topSideBearingFontUnits;
        }

        internal int AdvanceWidthFontUnits { get; }
        internal int LeftSideBearingFontUnits { get; }
        internal int AdvanceHeightFontUnits { get; }
        internal int TopSideBearingFontUnits { get; }
    }

    /// <summary>Immutable, bounds-validated horizontal and optional vertical glyph metric tables.</summary>
    internal sealed class TrueTypeHintingGlyphMetricSource
    {
        private readonly byte[] _horizontalMetricTableBytes;
        private readonly int _horizontalLongMetricCount;
        private readonly byte[] _verticalMetricTableBytes;
        private readonly int _verticalLongMetricCount;
        private readonly int _defaultAscenderFontUnits;
        private readonly int _defaultDescenderFontUnits;

        internal TrueTypeHintingGlyphMetricSource(byte[] horizontalMetricTableBytes, int horizontalLongMetricCount,
            byte[] verticalMetricTableBytes, int verticalLongMetricCount,
            int defaultAscenderFontUnits, int defaultDescenderFontUnits)
        {
            _horizontalMetricTableBytes = horizontalMetricTableBytes == null
                ? throw new ArgumentNullException(nameof(horizontalMetricTableBytes))
                : (byte[])horizontalMetricTableBytes.Clone();
            _horizontalLongMetricCount = horizontalLongMetricCount;
            _verticalMetricTableBytes = verticalMetricTableBytes == null
                ? new byte[0]
                : (byte[])verticalMetricTableBytes.Clone();
            _verticalLongMetricCount = verticalLongMetricCount;
            _defaultAscenderFontUnits = defaultAscenderFontUnits;
            _defaultDescenderFontUnits = defaultDescenderFontUnits;
        }

        internal TrueTypeHintingGlyphMetrics GetGlyphMetrics(TrueTypeGlyphIndex trueTypeGlyphIndex, int glyphMaximumVerticalFontUnits)
        {
            ReadMetric(_horizontalMetricTableBytes, _horizontalLongMetricCount, trueTypeGlyphIndex.Value,
                out int advanceWidthFontUnits, out int leftSideBearingFontUnits);

            int advanceHeightFontUnits;
            int topSideBearingFontUnits;
            if (_verticalLongMetricCount > 0)
            {
                ReadMetric(_verticalMetricTableBytes, _verticalLongMetricCount, trueTypeGlyphIndex.Value,
                    out advanceHeightFontUnits, out topSideBearingFontUnits);
            }
            else
            {
                advanceHeightFontUnits = Math.Abs(_defaultAscenderFontUnits - _defaultDescenderFontUnits);
                topSideBearingFontUnits = _defaultAscenderFontUnits - glyphMaximumVerticalFontUnits;
            }

            return new TrueTypeHintingGlyphMetrics(advanceWidthFontUnits, leftSideBearingFontUnits,
                advanceHeightFontUnits, topSideBearingFontUnits);
        }

        private static void ReadMetric(byte[] metricTableBytes, int longMetricCount, int glyphIndex,
            out int advanceFontUnits, out int sideBearingFontUnits)
        {
            int advanceMetricIndex = Math.Min(glyphIndex, longMetricCount - 1);
            advanceFontUnits = ReadUInt16(metricTableBytes, advanceMetricIndex * 4);
            int sideBearingByteOffset = glyphIndex < longMetricCount
                ? glyphIndex * 4 + 2
                : longMetricCount * 4 + (glyphIndex - longMetricCount) * 2;
            sideBearingFontUnits = ReadInt16(metricTableBytes, sideBearingByteOffset);
        }

        private static ushort ReadUInt16(byte[] dataBytes, int byteOffset)
            => (ushort)((dataBytes[byteOffset] << 8) | dataBytes[byteOffset + 1]);

        private static short ReadInt16(byte[] dataBytes, int byteOffset)
            => unchecked((short)ReadUInt16(dataBytes, byteOffset));
    }
}