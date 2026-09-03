using System;
using System.Collections.Generic;

namespace StbTrueTypeSharp.ColorBitmap
{
    /// <summary>
    /// Parsed CBLC (Color Bitmap Location) + CBDT (Color Bitmap Data) tables — OpenType 1.9.1, structures
    /// transcribed in _EXTERNAL_APIS/opentype_color_glyph_tables.md. Safe managed code, every read
    /// bounds-validated, malformed input surfaces a <see cref="ColorBitmapTableFailure"/> and never throws
    /// (plans/color_glyph_fonts.md §4.1). Parsing is eager for the strike directory and the IndexSubtable
    /// headers (small); glyph image bytes are located on demand.
    /// </summary>
    public sealed class ColorBitmapStrikeTable
    {
        /// <summary>One BitmapSize record (a strike) with its parsed IndexSubtable directory.</summary>
        public sealed class Strike
        {
            public ColorBitmapStrikeIndex Index { get; internal set; }
            public byte PpemX { get; internal set; }
            public byte PpemY { get; internal set; }
            /// <summary>1/2/4/8 greyscale (out of scope for colour) or 32 = premultiplied BGRA / PNG colour.</summary>
            public byte BitDepth { get; internal set; }
            /// <summary>Bitmap flags: 0x01 HORIZONTAL_METRICS, 0x02 VERTICAL_METRICS (direction of SmallGlyphMetrics).</summary>
            public sbyte Flags { get; internal set; }
            public ushort StartGlyphIndex { get; internal set; }
            public ushort EndGlyphIndex { get; internal set; }
            public ColorBitmapStrikeLineMetrics HorizontalLineMetrics { get; internal set; }
            public ColorBitmapStrikeLineMetrics VerticalLineMetrics { get; internal set; }
            internal IndexSubtableEntry[] IndexSubtables = Array.Empty<IndexSubtableEntry>();

            public bool IsColour => BitDepth == 32;
            public bool SmallMetricsAreVertical => (Flags & 0x02) != 0 && (Flags & 0x01) == 0;
            public int IndexSubtableCount => IndexSubtables.Length;
        }

        /// <summary>One IndexSubtableRecord + its IndexSubHeader, with the format-specific payload located (not copied).</summary>
        internal sealed class IndexSubtableEntry
        {
            public ushort FirstGlyphIndex;
            public ushort LastGlyphIndex;
            public ushort IndexFormat;               // 1..5
            public ColorBitmapImageFormat ImageFormat;
            public int ImageDataOffsetInCbdt;        // from start of CBDT
            public int SubtableAbsoluteOffset;       // absolute offset of the IndexSubHeader in the font
            public int SubtableEndAbsoluteOffset;    // exclusive; bounded by the IndexSubtableList size
            // Formats 2/5: constant image size + shared metrics.
            public int ConstantImageSize;
            public ColorBitmapGlyphMetrics SharedBigMetrics;
            // Format 4/5: explicit glyph id arrays (absolute offsets into the font; counts validated).
            public int GlyphArrayAbsoluteOffset;
            public int GlyphArrayCount;
        }

        private const int CblcHeaderByteCount = 8;
        private const int BitmapSizeRecordByteCount = 48;
        private const int IndexSubtableRecordByteCount = 8;
        private const int IndexSubHeaderByteCount = 8;
        private const int BigGlyphMetricsByteCount = 8;
        private const int SmallGlyphMetricsByteCount = 5;
        /// <summary>Sanity ceiling on numSizes: real fonts have 1–12 strikes; a hostile count is rejected, not iterated.</summary>
        private const int MaxReasonableStrikeCount = 256;
        /// <summary>Composite (format 8/9) nesting guard.</summary>
        private const int MaxCompositeRecursionDepth = 8;

        private readonly byte[] _fontData;
        private readonly int _cbdtOffset;
        private readonly int _cbdtLength;
        private readonly Strike[] _strikes;

        public int StrikeCount => _strikes.Length;
        public Strike GetStrike(ColorBitmapStrikeIndex index) => _strikes[index.Value];
        public IReadOnlyList<Strike> Strikes => _strikes;

        private ColorBitmapStrikeTable(byte[] fontData, int cbdtOffset, int cbdtLength, Strike[] strikes)
        {
            _fontData = fontData; _cbdtOffset = cbdtOffset; _cbdtLength = cbdtLength; _strikes = strikes;
        }

        // ── bounds-checked big-endian readers over the raw font bytes ──
        private static bool InBounds(byte[] data, int offset, int length)
            => offset >= 0 && length >= 0 && offset <= data.Length - length;
        private static ushort U16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);
        private static short S16(byte[] d, int o) => (short)((d[o] << 8) | d[o + 1]);
        private static uint U32(byte[] d, int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);

        private static ColorBitmapStrikeLineMetrics ReadLineMetrics(byte[] d, int o)
            => new ColorBitmapStrikeLineMetrics((sbyte)d[o], (sbyte)d[o + 1], d[o + 2], (sbyte)d[o + 3], (sbyte)d[o + 4],
                (sbyte)d[o + 5], (sbyte)d[o + 6], (sbyte)d[o + 7], (sbyte)d[o + 8], (sbyte)d[o + 9]);

        private static ColorBitmapGlyphMetrics ReadBigMetrics(byte[] d, int o, ColorBitmapGlyphMetricsSource source)
            => new ColorBitmapGlyphMetrics(width: d[o + 1], height: d[o], horiBearingX: (sbyte)d[o + 2], horiBearingY: (sbyte)d[o + 3],
                horiAdvance: d[o + 4], hasVerticalMetrics: true, vertBearingX: (sbyte)d[o + 5], vertBearingY: (sbyte)d[o + 6],
                vertAdvance: d[o + 7], source);

        private static ColorBitmapGlyphMetrics ReadSmallMetrics(byte[] d, int o, bool metricsAreVertical)
        {
            byte height = d[o], width = d[o + 1]; sbyte bearingX = (sbyte)d[o + 2], bearingY = (sbyte)d[o + 3]; byte advance = d[o + 4];
            return metricsAreVertical
                ? new ColorBitmapGlyphMetrics(width, height, 0, 0, 0, true, bearingX, bearingY, advance, ColorBitmapGlyphMetricsSource.SmallGlyphMetrics)
                : new ColorBitmapGlyphMetrics(width, height, bearingX, bearingY, advance, false, 0, 0, 0, ColorBitmapGlyphMetricsSource.SmallGlyphMetrics);
        }

        /// <summary>
        /// Parses CBLC + CBDT located at the given absolute offsets/lengths (from the sfnt directory).
        /// </summary>
        public static bool TryParse(byte[] fontData, int cblcOffset, int cblcLength, int cbdtOffset, int cbdtLength,
            out ColorBitmapStrikeTable table, out ColorBitmapTableFailure failure)
        {
            table = null;
            if (fontData == null || cblcOffset == 0 || cbdtOffset == 0)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.TablesAbsent, "CBLC and CBDT are both required"); return false; }
            if (!InBounds(fontData, cblcOffset, CblcHeaderByteCount) || cblcLength < CblcHeaderByteCount || !InBounds(fontData, cblcOffset, cblcLength))
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcTruncatedHeader, $"CBLC @{cblcOffset} len {cblcLength}"); return false; }
            if (!InBounds(fontData, cbdtOffset, 4) || cbdtLength < 4 || !InBounds(fontData, cbdtOffset, cbdtLength))
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CbdtTruncatedHeader, $"CBDT @{cbdtOffset} len {cbdtLength}"); return false; }

            ushort cblcMajor = U16(fontData, cblcOffset), cblcMinor = U16(fontData, cblcOffset + 2);
            if (cblcMajor != 3 || cblcMinor != 0)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcUnsupportedVersion, $"CBLC {cblcMajor}.{cblcMinor} (expected 3.0)"); return false; }
            ushort cbdtMajor = U16(fontData, cbdtOffset), cbdtMinor = U16(fontData, cbdtOffset + 2);
            if (cbdtMajor != 3 || cbdtMinor != 0)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CbdtUnsupportedVersion, $"CBDT {cbdtMajor}.{cbdtMinor} (expected 3.0)"); return false; }

            uint numSizes = U32(fontData, cblcOffset + 4);
            if (numSizes > MaxReasonableStrikeCount)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcStrikeCountUnreasonable, $"numSizes {numSizes}"); return false; }
            int cblcEnd = cblcOffset + cblcLength;
            if ((long)CblcHeaderByteCount + (long)numSizes * BitmapSizeRecordByteCount > cblcLength)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcBitmapSizeRecordOutOfBounds, $"{numSizes} BitmapSize records exceed CBLC length {cblcLength}"); return false; }

            var strikes = new Strike[numSizes];
            for (int s = 0; s < numSizes; s++)
            {
                int rec = cblcOffset + CblcHeaderByteCount + s * BitmapSizeRecordByteCount;
                uint indexSubtableListOffset = U32(fontData, rec);
                uint indexSubtableListSize = U32(fontData, rec + 4);
                uint numberOfIndexSubtables = U32(fontData, rec + 8);
                var strike = new Strike
                {
                    Index = new ColorBitmapStrikeIndex(s),
                    HorizontalLineMetrics = ReadLineMetrics(fontData, rec + 16),
                    VerticalLineMetrics = ReadLineMetrics(fontData, rec + 28),
                    StartGlyphIndex = U16(fontData, rec + 40),
                    EndGlyphIndex = U16(fontData, rec + 42),
                    PpemX = fontData[rec + 44],
                    PpemY = fontData[rec + 45],
                    BitDepth = fontData[rec + 46],
                    Flags = (sbyte)fontData[rec + 47],
                };

                // IndexSubtableList: must lie inside CBLC.
                long listStart = (long)cblcOffset + indexSubtableListOffset;
                long listEnd = listStart + indexSubtableListSize;
                if (indexSubtableListOffset < CblcHeaderByteCount || listEnd > cblcEnd || (long)numberOfIndexSubtables * IndexSubtableRecordByteCount > indexSubtableListSize)
                { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableListOutOfBounds, $"strike {s}: list @{indexSubtableListOffset} size {indexSubtableListSize} count {numberOfIndexSubtables}"); return false; }

                var entries = new IndexSubtableEntry[numberOfIndexSubtables];
                int previousLast = -1;
                for (int r = 0; r < numberOfIndexSubtables; r++)
                {
                    int recOff = (int)listStart + r * IndexSubtableRecordByteCount;
                    var entry = new IndexSubtableEntry
                    {
                        FirstGlyphIndex = U16(fontData, recOff),
                        LastGlyphIndex = U16(fontData, recOff + 2),
                    };
                    uint subtableOffset = U32(fontData, recOff + 4);
                    if (entry.LastGlyphIndex < entry.FirstGlyphIndex || entry.FirstGlyphIndex <= previousLast && previousLast >= 0)
                    {
                        failure = new ColorBitmapTableFailure(
                            entry.LastGlyphIndex < entry.FirstGlyphIndex || entry.FirstGlyphIndex < previousLast
                                ? ColorBitmapTableFailureCode.CblcIndexSubtableRecordsNotAscending
                                : ColorBitmapTableFailureCode.CblcIndexSubtableRecordsOverlap,
                            $"strike {s} record {r}: [{entry.FirstGlyphIndex},{entry.LastGlyphIndex}] after {previousLast}");
                        return false;
                    }
                    previousLast = entry.LastGlyphIndex;

                    long sub = listStart + subtableOffset;
                    if (sub + IndexSubHeaderByteCount > listEnd)
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableOutOfBounds, $"strike {s} record {r}: subtable @{subtableOffset}"); return false; }
                    entry.SubtableAbsoluteOffset = (int)sub;
                    entry.SubtableEndAbsoluteOffset = (int)listEnd;
                    entry.IndexFormat = U16(fontData, (int)sub);
                    entry.ImageFormat = (ColorBitmapImageFormat)U16(fontData, (int)sub + 2);
                    entry.ImageDataOffsetInCbdt = (int)Math.Min(U32(fontData, (int)sub + 4), int.MaxValue);
                    if (!TryValidateIndexSubtablePayload(fontData, entry, out failure))
                    { failure = new ColorBitmapTableFailure(failure.FailureCode, $"strike {s} record {r}: {failure.FailureDetail}"); return false; }
                    entries[r] = entry;
                }
                strike.IndexSubtables = entries;
                strikes[s] = strike;
            }

            table = new ColorBitmapStrikeTable(fontData, cbdtOffset, cbdtLength, strikes);
            failure = ColorBitmapTableFailure.None;
            return true;
        }

        /// <summary>Validates and locates the format-specific IndexSubtable payload (arrays / shared metrics). No CBDT reads.</summary>
        private static bool TryValidateIndexSubtablePayload(byte[] d, IndexSubtableEntry e, out ColorBitmapTableFailure failure)
        {
            int payload = e.SubtableAbsoluteOffset + IndexSubHeaderByteCount;
            int end = e.SubtableEndAbsoluteOffset;
            int rangeCount = e.LastGlyphIndex - e.FirstGlyphIndex + 1;
            switch (e.IndexFormat)
            {
                case 1: // Offset32 sbitOffsets[rangeCount + 1]
                    if ((long)payload + (long)(rangeCount + 1) * 4 > end)
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableArrayOutOfBounds, $"format 1 needs {(rangeCount + 1) * 4} bytes"); return false; }
                    e.GlyphArrayAbsoluteOffset = payload; e.GlyphArrayCount = rangeCount + 1;
                    break;
                case 3: // Offset16 sbitOffsets[rangeCount + 1]
                    if ((long)payload + (long)(rangeCount + 1) * 2 > end)
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableArrayOutOfBounds, $"format 3 needs {(rangeCount + 1) * 2} bytes"); return false; }
                    e.GlyphArrayAbsoluteOffset = payload; e.GlyphArrayCount = rangeCount + 1;
                    break;
                case 2: // uint32 imageSize + BigGlyphMetrics
                    if (payload + 4 + BigGlyphMetricsByteCount > end)
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableArrayOutOfBounds, "format 2 header truncated"); return false; }
                    e.ConstantImageSize = (int)Math.Min(U32(d, payload), int.MaxValue);
                    e.SharedBigMetrics = ReadBigMetrics(d, payload + 4, ColorBitmapGlyphMetricsSource.IndexSubtable);
                    break;
                case 4: // uint32 numGlyphs + GlyphIdOffsetPair[numGlyphs + 1]
                {
                    if (payload + 4 > end)
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableArrayOutOfBounds, "format 4 header truncated"); return false; }
                    uint numGlyphs = U32(d, payload);
                    if ((long)payload + 4 + ((long)numGlyphs + 1) * 4 > end)
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableArrayOutOfBounds, $"format 4 numGlyphs {numGlyphs}"); return false; }
                    e.GlyphArrayAbsoluteOffset = payload + 4; e.GlyphArrayCount = (int)numGlyphs;
                    for (int i = 1; i < numGlyphs; i++)
                        if (U16(d, e.GlyphArrayAbsoluteOffset + i * 4) <= U16(d, e.GlyphArrayAbsoluteOffset + (i - 1) * 4))
                        { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableGlyphIdsNotSorted, $"format 4 pair {i}"); return false; }
                    break;
                }
                case 5: // uint32 imageSize + BigGlyphMetrics + uint32 numGlyphs + uint16 glyphIdArray[numGlyphs]
                {
                    if (payload + 4 + BigGlyphMetricsByteCount + 4 > end)
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableArrayOutOfBounds, "format 5 header truncated"); return false; }
                    e.ConstantImageSize = (int)Math.Min(U32(d, payload), int.MaxValue);
                    e.SharedBigMetrics = ReadBigMetrics(d, payload + 4, ColorBitmapGlyphMetricsSource.IndexSubtable);
                    uint numGlyphs = U32(d, payload + 4 + BigGlyphMetricsByteCount);
                    int arr = payload + 4 + BigGlyphMetricsByteCount + 4;
                    if ((long)arr + (long)numGlyphs * 2 > end)
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableArrayOutOfBounds, $"format 5 numGlyphs {numGlyphs}"); return false; }
                    e.GlyphArrayAbsoluteOffset = arr; e.GlyphArrayCount = (int)numGlyphs;
                    for (int i = 1; i < numGlyphs; i++)
                        if (U16(d, arr + i * 2) <= U16(d, arr + (i - 1) * 2))
                        { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableGlyphIdsNotSorted, $"format 5 id {i}"); return false; }
                    break;
                }
                default:
                    failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableFormatUnknown, $"indexFormat {e.IndexFormat}");
                    return false;
            }
            failure = ColorBitmapTableFailure.None;
            return true;
        }

        /// <summary>
        /// Strike selection (spec CBDT §Scaling behavior + FreeType parity): the smallest strike whose ppemY is
        /// ≥ the requested size (downscale from the closest larger); if none is larger, the largest strike.
        /// Greyscale strikes are skipped. Returns None when the table has no colour strike.
        /// </summary>
        public ColorBitmapStrikeIndex SelectStrikeIndexForPpem(float requestedPpem)
        {
            int best = -1, largest = -1;
            for (int s = 0; s < _strikes.Length; s++)
            {
                Strike strike = _strikes[s];
                if (!strike.IsColour) continue;
                if (largest < 0 || strike.PpemY > _strikes[largest].PpemY) largest = s;
                if (strike.PpemY >= requestedPpem && (best < 0 || strike.PpemY < _strikes[best].PpemY)) best = s;
            }
            return new ColorBitmapStrikeIndex(best >= 0 ? best : largest);
        }

        /// <summary>
        /// Locates <paramref name="glyphId"/> in the given strike. Returns false with
        /// <see cref="ColorBitmapTableFailureCode.GlyphNotInStrike"/> when the strike simply has no image for the
        /// glyph (the normal "fall back to outline" signal), or a structural code when the data is malformed.
        /// </summary>
        public bool TryLocateGlyph(ColorBitmapStrikeIndex strikeIndex, int glyphId, out ColorBitmapGlyphRecord record, out ColorBitmapTableFailure failure)
        {
            record = default;
            if (strikeIndex.IsNone || strikeIndex.Value >= _strikes.Length)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.StrikeIndexOutOfRange, strikeIndex.ToString()); return false; }
            Strike strike = _strikes[strikeIndex.Value];
            if (glyphId < 0 || glyphId > ushort.MaxValue || glyphId < strike.StartGlyphIndex || glyphId > strike.EndGlyphIndex)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphNotInStrike, $"glyph {glyphId} outside strike range [{strike.StartGlyphIndex},{strike.EndGlyphIndex}]"); return false; }

            // Records are sorted by firstGlyphIndex and non-overlapping (validated at parse) → binary search.
            IndexSubtableEntry[] entries = strike.IndexSubtables;
            int lo = 0, hi = entries.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (glyphId < entries[mid].FirstGlyphIndex) hi = mid - 1;
                else if (glyphId > entries[mid].LastGlyphIndex) lo = mid + 1;
                else { found = mid; break; }
            }
            if (found < 0)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphNotInStrike, $"glyph {glyphId}: no index subtable covers it"); return false; }
            IndexSubtableEntry e = entries[found];
            byte[] d = _fontData;

            int dataOffsetInCbdt, dataLength;
            bool metricsFromIndex = false;
            switch (e.IndexFormat)
            {
                case 1:
                {
                    int i = glyphId - e.FirstGlyphIndex;
                    uint a = U32(d, e.GlyphArrayAbsoluteOffset + i * 4), b = U32(d, e.GlyphArrayAbsoluteOffset + (i + 1) * 4);
                    if (b < a) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphImageDataOutOfBounds, $"glyph {glyphId}: format 1 offsets descend"); return false; }
                    if (a == b) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphNotInStrike, $"glyph {glyphId}: zero-length image (absent)"); return false; }
                    dataOffsetInCbdt = (int)Math.Min((uint)e.ImageDataOffsetInCbdt + a, int.MaxValue); dataLength = (int)Math.Min(b - a, int.MaxValue);
                    break;
                }
                case 3:
                {
                    int i = glyphId - e.FirstGlyphIndex;
                    ushort a = U16(d, e.GlyphArrayAbsoluteOffset + i * 2), b = U16(d, e.GlyphArrayAbsoluteOffset + (i + 1) * 2);
                    if (b < a) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphImageDataOutOfBounds, $"glyph {glyphId}: format 3 offsets descend"); return false; }
                    if (a == b) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphNotInStrike, $"glyph {glyphId}: zero-length image (absent)"); return false; }
                    dataOffsetInCbdt = e.ImageDataOffsetInCbdt + a; dataLength = b - a;
                    break;
                }
                case 2:
                {
                    int i = glyphId - e.FirstGlyphIndex;
                    dataOffsetInCbdt = (int)Math.Min((long)e.ImageDataOffsetInCbdt + (long)i * e.ConstantImageSize, int.MaxValue);
                    dataLength = e.ConstantImageSize; metricsFromIndex = true;
                    break;
                }
                case 4:
                {
                    int pos = BinarySearchGlyphArray(d, e.GlyphArrayAbsoluteOffset, e.GlyphArrayCount, stride: 4, glyphId);
                    if (pos < 0) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphNotInStrike, $"glyph {glyphId}: not in format 4 array"); return false; }
                    ushort a = U16(d, e.GlyphArrayAbsoluteOffset + pos * 4 + 2), b = U16(d, e.GlyphArrayAbsoluteOffset + (pos + 1) * 4 + 2);
                    if (b < a) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphImageDataOutOfBounds, $"glyph {glyphId}: format 4 offsets descend"); return false; }
                    if (a == b) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphNotInStrike, $"glyph {glyphId}: zero-length image (absent)"); return false; }
                    dataOffsetInCbdt = e.ImageDataOffsetInCbdt + a; dataLength = b - a;
                    break;
                }
                case 5:
                {
                    int pos = BinarySearchGlyphArray(d, e.GlyphArrayAbsoluteOffset, e.GlyphArrayCount, stride: 2, glyphId);
                    if (pos < 0) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphNotInStrike, $"glyph {glyphId}: not in format 5 array"); return false; }
                    dataOffsetInCbdt = (int)Math.Min((long)e.ImageDataOffsetInCbdt + (long)pos * e.ConstantImageSize, int.MaxValue);
                    dataLength = e.ConstantImageSize; metricsFromIndex = true;
                    break;
                }
                default:
                    failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CblcIndexSubtableFormatUnknown, $"indexFormat {e.IndexFormat}"); return false;
            }

            if (dataLength <= 0 || dataOffsetInCbdt < 0 || (long)dataOffsetInCbdt + dataLength > _cbdtLength)
            { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphImageDataOutOfBounds, $"glyph {glyphId}: CBDT [{dataOffsetInCbdt},+{dataLength}) exceeds table length {_cbdtLength}"); return false; }
            int abs = _cbdtOffset + dataOffsetInCbdt;

            // Metrics + PNG span per image format.
            ColorBitmapGlyphMetrics metrics;
            int cursor = abs, pngOffset = -1, pngLength = 0;
            bool smallVertical = strike.SmallMetricsAreVertical;
            switch (e.ImageFormat)
            {
                case ColorBitmapImageFormat.SmallMetricsByteAligned:
                case ColorBitmapImageFormat.SmallMetricsBitAligned:
                case ColorBitmapImageFormat.SmallMetricsComposite:
                case ColorBitmapImageFormat.SmallMetricsPng:
                    if (dataLength < SmallGlyphMetricsByteCount) { failure = Truncated(glyphId, "SmallGlyphMetrics"); return false; }
                    metrics = ReadSmallMetrics(d, cursor, smallVertical); cursor += SmallGlyphMetricsByteCount;
                    break;
                case ColorBitmapImageFormat.BigMetricsByteAligned:
                case ColorBitmapImageFormat.BigMetricsBitAligned:
                case ColorBitmapImageFormat.BigMetricsComposite:
                case ColorBitmapImageFormat.BigMetricsPng:
                    if (dataLength < BigGlyphMetricsByteCount) { failure = Truncated(glyphId, "BigGlyphMetrics"); return false; }
                    metrics = ReadBigMetrics(d, cursor, ColorBitmapGlyphMetricsSource.BigGlyphMetrics); cursor += BigGlyphMetricsByteCount;
                    break;
                case ColorBitmapImageFormat.MetricsInIndexBitAligned:
                case ColorBitmapImageFormat.MetricsInIndexPng:
                    if (!metricsFromIndex) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.ImageFormatUnknown, $"glyph {glyphId}: image format {(int)e.ImageFormat} requires index format 2/5 (got {e.IndexFormat})"); return false; }
                    metrics = e.SharedBigMetrics;
                    break;
                default:
                    failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.ImageFormatUnknown, $"glyph {glyphId}: imageFormat {(int)e.ImageFormat}"); return false;
            }
            if (e.ImageFormat == ColorBitmapImageFormat.SmallMetricsPng || e.ImageFormat == ColorBitmapImageFormat.BigMetricsPng || e.ImageFormat == ColorBitmapImageFormat.MetricsInIndexPng)
            {
                if (cursor + 4 > abs + dataLength) { failure = Truncated(glyphId, "PNG dataLen"); return false; }
                uint declared = U32(d, cursor); cursor += 4;
                if (declared > (uint)(abs + dataLength - cursor))
                { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.PngDeclaredLengthExceedsData, $"glyph {glyphId}: dataLen {declared} > {abs + dataLength - cursor} available"); return false; }
                pngOffset = cursor; pngLength = (int)declared;
            }

            record = new ColorBitmapGlyphRecord(strikeIndex, glyphId, e.ImageFormat, metrics, abs, dataLength, pngOffset, pngLength);
            failure = ColorBitmapTableFailure.None;
            return true;
        }

        private static ColorBitmapTableFailure Truncated(int glyphId, string what)
            => new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GlyphImageDataTruncated, $"glyph {glyphId}: {what} truncated");

        private static int BinarySearchGlyphArray(byte[] d, int arrayOffset, int count, int stride, int glyphId)
        {
            int lo = 0, hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int id = U16(d, arrayOffset + mid * stride);
                if (glyphId < id) hi = mid - 1; else if (glyphId > id) lo = mid + 1; else return mid;
            }
            return -1;
        }

        /// <summary>The raw PNG bytes of a PNG-format record (empty span for non-PNG records).</summary>
        public ReadOnlySpan<byte> GetPngBytes(in ColorBitmapGlyphRecord record)
            => record.IsPng ? new ReadOnlySpan<byte>(_fontData, record.PngOffset, record.PngLength) : ReadOnlySpan<byte>.Empty;

        /// <summary>
        /// Decodes a RAW (non-PNG) bitDepth-32 record into premultiplied RGBA8. Formats 1/2/5/6/7 are pixel blocks
        /// (at 32 bpp "bit-aligned" and "byte-aligned" have identical layout: rows are whole bytes); formats 8/9 are
        /// composites whose components are decoded recursively and source-over blended at their offsets.
        /// </summary>
        public bool TryDecodeRawPixels(in ColorBitmapGlyphRecord record, out ColorBitmapDecodedPixels pixels, out ColorBitmapTableFailure failure)
            => TryDecodeRawPixels(record, 0, out pixels, out failure);

        private bool TryDecodeRawPixels(in ColorBitmapGlyphRecord record, int depth, out ColorBitmapDecodedPixels pixels, out ColorBitmapTableFailure failure)
        {
            pixels = default;
            if (record.IsPng) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.ImageFormatIsPngNotRaw, $"glyph {record.GlyphId}"); return false; }
            Strike strike = _strikes[record.Strike.Value];
            if (!strike.IsColour) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.GreyscaleStrikeUnsupported, $"strike bitDepth {strike.BitDepth}"); return false; }
            int width = record.Metrics.Width, height = record.Metrics.Height;
            byte[] d = _fontData;
            int headerBytes = record.Metrics.Source == ColorBitmapGlyphMetricsSource.SmallGlyphMetrics ? SmallGlyphMetricsByteCount
                            : record.Metrics.Source == ColorBitmapGlyphMetricsSource.BigGlyphMetrics ? BigGlyphMetricsByteCount : 0;
            int payload = record.DataOffset + headerBytes;
            int payloadEnd = record.DataOffset + record.DataLength;

            if (record.IsComposite)
            {
                if (depth >= MaxCompositeRecursionDepth) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CompositeRecursionTooDeep, $"glyph {record.GlyphId}"); return false; }
                // Format 8: SmallGlyphMetrics + uint8 pad + uint16 numComponents; Format 9: BigGlyphMetrics + uint16 numComponents.
                if (record.ImageFormat == ColorBitmapImageFormat.SmallMetricsComposite) payload += 1;
                if (payload + 2 > payloadEnd) { failure = Truncated(record.GlyphId, "composite numComponents"); return false; }
                int numComponents = U16(d, payload); payload += 2;
                if ((long)payload + (long)numComponents * 4 > payloadEnd) { failure = Truncated(record.GlyphId, "composite component array"); return false; }
                var canvas = new byte[width * height * 4];
                for (int c = 0; c < numComponents; c++)
                {
                    int comp = payload + c * 4;
                    int componentGlyphId = U16(d, comp); sbyte xOffset = (sbyte)d[comp + 2], yOffset = (sbyte)d[comp + 3];
                    if (!TryLocateGlyph(record.Strike, componentGlyphId, out ColorBitmapGlyphRecord componentRecord, out failure))
                    { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.CompositeComponentOutOfBounds, $"glyph {record.GlyphId} component {c} -> glyph {componentGlyphId}: {failure.FailureDetail}"); return false; }
                    if (componentRecord.IsPng) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.ImageFormatIsPngNotRaw, $"glyph {record.GlyphId}: composite component {componentGlyphId} is PNG"); return false; }
                    if (!TryDecodeRawPixels(componentRecord, depth + 1, out ColorBitmapDecodedPixels componentPixels, out failure)) return false;
                    // Component placement: xOffset/yOffset are in pixels, y-DOWN from the composite's top-left (EBDT convention).
                    BlendSourceOverPremultiplied(canvas, width, height, componentPixels, xOffset, yOffset);
                }
                pixels = new ColorBitmapDecodedPixels(width, height, canvas);
                failure = ColorBitmapTableFailure.None;
                return true;
            }

            // Pixel block formats 1/2/5/6/7 at 32 bpp: width*height*4 bytes of premultiplied BGRA.
            long needed = (long)width * height * 4;
            if (payload + needed > payloadEnd) { failure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.RawBitmapSizeMismatch, $"glyph {record.GlyphId}: {width}x{height}x4 = {needed} bytes, {payloadEnd - payload} available"); return false; }
            var rgba = new byte[width * height * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                // CBDT bitDepth-32 pixel order is B,G,R,A (spec: "encoded in that order"), premultiplied, sRGB.
                rgba[i + 0] = d[payload + i + 2];
                rgba[i + 1] = d[payload + i + 1];
                rgba[i + 2] = d[payload + i + 0];
                rgba[i + 3] = d[payload + i + 3];
            }
            pixels = new ColorBitmapDecodedPixels(width, height, rgba);
            failure = ColorBitmapTableFailure.None;
            return true;
        }

        /// <summary>Premultiplied source-over of <paramref name="source"/> onto <paramref name="canvas"/> at (dx, dy), clipped.</summary>
        private static void BlendSourceOverPremultiplied(byte[] canvas, int canvasWidth, int canvasHeight, ColorBitmapDecodedPixels source, int dx, int dy)
        {
            for (int sy = 0; sy < source.Height; sy++)
            {
                int cy = dy + sy; if (cy < 0 || cy >= canvasHeight) continue;
                for (int sx = 0; sx < source.Width; sx++)
                {
                    int cx = dx + sx; if (cx < 0 || cx >= canvasWidth) continue;
                    int si = (sy * source.Width + sx) * 4, ci = (cy * canvasWidth + cx) * 4;
                    int sa = source.PremultipliedRgba[si + 3];
                    if (sa == 0) continue;
                    int inv = 255 - sa;
                    canvas[ci + 0] = (byte)(source.PremultipliedRgba[si + 0] + (canvas[ci + 0] * inv + 127) / 255);
                    canvas[ci + 1] = (byte)(source.PremultipliedRgba[si + 1] + (canvas[ci + 1] * inv + 127) / 255);
                    canvas[ci + 2] = (byte)(source.PremultipliedRgba[si + 2] + (canvas[ci + 2] * inv + 127) / 255);
                    canvas[ci + 3] = (byte)(sa + (canvas[ci + 3] * inv + 127) / 255);
                }
            }
        }
    }
}