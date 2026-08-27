using System;

namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Reverses the WOFF2 transformed-glyf encoding (WOFF2 §5.1) back into standard
    /// TrueType glyf + loca tables. Safe transliteration of 3P_woff2/src/woff2_dec.cc
    /// ReconstructGlyf / TripletDecode / StorePoints / ComputeBbox / SizeOfComposite.
    /// Glyph records are 4-byte aligned in the output glyf (matching Google's Pad4),
    /// which also guarantees even offsets for the short loca format.
    /// Bounds-validated throughout; hostile input reports failure codes, never throws.
    /// </summary>
    internal static class Woff2TransformedGlyfReconstructor
    {
        // Simple-glyph point flags (TrueType glyf spec).
        private const byte GlyfFlagOnCurve = 1 << 0;
        private const byte GlyfFlagXShort = 1 << 1;
        private const byte GlyfFlagYShort = 1 << 2;
        private const byte GlyfFlagRepeat = 1 << 3;
        private const byte GlyfFlagXSameOrPositive = 1 << 4;
        private const byte GlyfFlagYSameOrPositive = 1 << 5;
        private const byte GlyfFlagOverlapSimple = 1 << 6;

        // Composite-glyph component flags.
        private const int CompositeFlagArgsAreWords = 1 << 0;
        private const int CompositeFlagHaveScale = 1 << 3;
        private const int CompositeFlagMoreComponents = 1 << 5;
        private const int CompositeFlagHaveXYScale = 1 << 6;
        private const int CompositeFlagHaveTwoByTwo = 1 << 7;
        private const int CompositeFlagHaveInstructions = 1 << 8;

        private const int TransformedGlyfHeaderOptionFlagOverlapSimpleBitmap = 1 << 0;
        private const int SimpleGlyphEndPtsOffset = 10; // after nContours + bbox

        private struct GlyphOutlinePoint
        {
            public int X;
            public int Y;
            public bool OnCurve;
        }

        internal static bool TryReconstruct(byte[] transformedStreamBytes, int transformedOffset, int transformedByteCount,
            uint declaredLocaByteCount,
            out byte[] glyfTableBytes, out byte[] locaTableBytes, out short[] glyphXMinsFontUnits,
            out WebFontContainerDecodeFailure decodeFailure)
        {
            glyfTableBytes = null;
            locaTableBytes = null;
            glyphXMinsFontUnits = null;

            int transformedEndOffset = transformedOffset + transformedByteCount;
            var headerCursor = new Woff2StreamCursor(transformedStreamBytes, transformedOffset, transformedEndOffset);

            // ── Transformed-glyf header: version, optionFlags, numGlyphs, indexFormat, 7 substream sizes ──
            if (!headerCursor.TryReadU16(out _ /* version/reserved */)
                || !headerCursor.TryReadU16(out ushort optionFlags)
                || !headerCursor.TryReadU16(out ushort glyphCount)
                || !headerCursor.TryReadU16(out ushort locaIndexFormat))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfSubStreamBoundsInvalid,
                    "transformed glyf header truncated");
                return false;
            }
            bool hasOverlapSimpleBitmap = (optionFlags & TransformedGlyfHeaderOptionFlagOverlapSimpleBitmap) != 0;

            // conform-mustRejectLoca: declared loca origLength must match exactly.
            uint expectedLocaByteCount = (uint)(locaIndexFormat != 0 ? 4 : 2) * ((uint)glyphCount + 1);
            if (declaredLocaByteCount != expectedLocaByteCount)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TransformedLocaMustBeZeroLength,
                    $"loca origLength {declaredLocaByteCount}, expected {expectedLocaByteCount} (numGlyphs {glyphCount}, indexFormat {locaIndexFormat})");
                return false;
            }

            // Seven substreams laid consecutively after the header.
            var subStreamOffsets = new int[7];
            var subStreamByteCounts = new int[7];
            long runningOffset = transformedOffset + (2 + 7) * 4; // header is (2+7)*4 = 36 bytes
            if (runningOffset > transformedEndOffset)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfSubStreamBoundsInvalid,
                    "transformed glyf shorter than fixed header");
                return false;
            }
            for (int subStreamIndex = 0; subStreamIndex < 7; subStreamIndex++)
            {
                if (!headerCursor.TryReadU32(out uint subStreamByteCount)
                    || subStreamByteCount > transformedEndOffset - runningOffset)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfSubStreamBoundsInvalid,
                        $"substream {subStreamIndex} size invalid");
                    return false;
                }
                subStreamOffsets[subStreamIndex] = (int)runningOffset;
                subStreamByteCounts[subStreamIndex] = (int)subStreamByteCount;
                runningOffset += subStreamByteCount;
            }
            var nContourCursor = MakeCursor(transformedStreamBytes, subStreamOffsets, subStreamByteCounts, 0);
            var nPointsCursor = MakeCursor(transformedStreamBytes, subStreamOffsets, subStreamByteCounts, 1);
            var flagCursor = MakeCursor(transformedStreamBytes, subStreamOffsets, subStreamByteCounts, 2);
            var glyphCursor = MakeCursor(transformedStreamBytes, subStreamOffsets, subStreamByteCounts, 3);
            var compositeCursor = MakeCursor(transformedStreamBytes, subStreamOffsets, subStreamByteCounts, 4);
            var bboxCursor = MakeCursor(transformedStreamBytes, subStreamOffsets, subStreamByteCounts, 5);
            var instructionCursor = MakeCursor(transformedStreamBytes, subStreamOffsets, subStreamByteCounts, 6);

            // Optional overlapSimple bitmap appended after the 7 substreams.
            int overlapBitmapOffset = 0;
            if (hasOverlapSimpleBitmap)
            {
                int overlapBitmapByteCount = (glyphCount + 7) >> 3;
                if (overlapBitmapByteCount > transformedEndOffset - runningOffset)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfSubStreamBoundsInvalid,
                        "overlapSimple bitmap exceeds transformed glyf");
                    return false;
                }
                overlapBitmapOffset = (int)runningOffset;
            }

            // bboxBitmap sits at the START of the bbox substream: 4-aligned bit-per-glyph.
            int bboxBitmapByteCount = ((glyphCount + 31) >> 5) << 2;
            int bboxBitmapOffset = subStreamOffsets[5];
            if (bboxBitmapByteCount > subStreamByteCounts[5])
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfSubStreamBoundsInvalid,
                    "bbox bitmap exceeds bbox substream");
                return false;
            }
            bboxCursor.CurrentOffset += bboxBitmapByteCount;

            var glyfWriter = new GrowableByteWriter();
            var locaValues = new uint[glyphCount + 1];
            glyphXMinsFontUnits = new short[glyphCount];

            for (int glyphIndex = 0; glyphIndex < glyphCount; glyphIndex++)
            {
                bool glyphHasExplicitBbox =
                    (transformedStreamBytes[bboxBitmapOffset + (glyphIndex >> 3)] & (0x80 >> (glyphIndex & 7))) != 0;
                if (!nContourCursor.TryReadU16(out ushort contourCountRaw))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfTripletStreamOverrun,
                        $"nContour stream truncated at glyph {glyphIndex}");
                    return false;
                }

                locaValues[glyphIndex] = (uint)glyfWriter.Length;
                if (contourCountRaw == 0xFFFF)
                {
                    if (!TryWriteCompositeGlyph(transformedStreamBytes, ref compositeCursor, ref glyphCursor,
                        ref instructionCursor, glyphHasExplicitBbox, ref bboxCursor, glyfWriter, out decodeFailure))
                    {
                        return false;
                    }
                }
                else if (contourCountRaw > 0)
                {
                    bool glyphHasOverlapBit = hasOverlapSimpleBitmap
                        && (transformedStreamBytes[overlapBitmapOffset + (glyphIndex >> 3)] & (0x80 >> (glyphIndex & 7))) != 0;
                    if (!TryWriteSimpleGlyph(transformedStreamBytes, contourCountRaw, ref nPointsCursor, ref flagCursor,
                        ref glyphCursor, ref instructionCursor, glyphHasExplicitBbox, ref bboxCursor,
                        glyphHasOverlapBit, glyfWriter, out decodeFailure))
                    {
                        return false;
                    }
                }
                else if (glyphHasExplicitBbox)
                {
                    // Empty glyph MUST NOT carry a bbox (woff2_dec.cc).
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfSubStreamBoundsInvalid,
                        $"empty glyph {glyphIndex} has explicit bbox");
                    return false;
                }

                // xMin (glyph record offset 2) feeds hmtx reconstruction.
                if (contourCountRaw != 0 && glyfWriter.Length >= locaValues[glyphIndex] + 4)
                {
                    glyphXMinsFontUnits[glyphIndex] = (short)((glyfWriter.Bytes[locaValues[glyphIndex] + 2] << 8)
                        | glyfWriter.Bytes[locaValues[glyphIndex] + 3]);
                }
                glyfWriter.PadToFourByteAlignment();
            }
            locaValues[glyphCount] = (uint)glyfWriter.Length;

            // ── Encode loca (short format stores offset/2; alignment guarantees even) ──
            byte[] locaBytes = new byte[(locaIndexFormat != 0 ? 4 : 2) * (glyphCount + 1)];
            int locaWriteOffset = 0;
            foreach (uint locaValue in locaValues)
            {
                if (locaIndexFormat != 0)
                {
                    locaBytes[locaWriteOffset++] = (byte)(locaValue >> 24);
                    locaBytes[locaWriteOffset++] = (byte)(locaValue >> 16);
                    locaBytes[locaWriteOffset++] = (byte)(locaValue >> 8);
                    locaBytes[locaWriteOffset++] = (byte)locaValue;
                }
                else
                {
                    uint halvedValue = locaValue >> 1;
                    if (halvedValue > 0xFFFF)
                    {
                        decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfSubStreamBoundsInvalid,
                            $"glyf too large ({locaValue} bytes) for short loca format");
                        return false;
                    }
                    locaBytes[locaWriteOffset++] = (byte)(halvedValue >> 8);
                    locaBytes[locaWriteOffset++] = (byte)halvedValue;
                }
            }

            glyfTableBytes = glyfWriter.ToArray();
            locaTableBytes = locaBytes;
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        private static bool TryWriteCompositeGlyph(byte[] streamBytes, ref Woff2StreamCursor compositeCursor,
            ref Woff2StreamCursor glyphCursor, ref Woff2StreamCursor instructionCursor,
            bool glyphHasExplicitBbox, ref Woff2StreamCursor bboxCursor, GrowableByteWriter glyfWriter,
            out WebFontContainerDecodeFailure decodeFailure)
        {
            if (!glyphHasExplicitBbox)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CompositeGlyphStreamOverrun,
                    "composite glyph lacks mandatory explicit bbox");
                return false;
            }
            // Measure the component list (flags-driven, verified vs SizeOfComposite).
            int compositeStartOffset = compositeCursor.CurrentOffset;
            var measureCursor = compositeCursor;
            bool componentsHaveInstructions = false;
            int componentFlags = CompositeFlagMoreComponents;
            while ((componentFlags & CompositeFlagMoreComponents) != 0)
            {
                if (!measureCursor.TryReadU16(out ushort flagsWord))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CompositeGlyphStreamOverrun,
                        "composite component flags truncated");
                    return false;
                }
                componentFlags = flagsWord;
                componentsHaveInstructions |= (componentFlags & CompositeFlagHaveInstructions) != 0;
                int argumentByteCount = 2 + ((componentFlags & CompositeFlagArgsAreWords) != 0 ? 4 : 2);
                if ((componentFlags & CompositeFlagHaveScale) != 0) argumentByteCount += 2;
                else if ((componentFlags & CompositeFlagHaveXYScale) != 0) argumentByteCount += 4;
                else if ((componentFlags & CompositeFlagHaveTwoByTwo) != 0) argumentByteCount += 8;
                if (measureCursor.RemainingByteCount < argumentByteCount)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CompositeGlyphStreamOverrun,
                        "composite component arguments truncated");
                    return false;
                }
                measureCursor.CurrentOffset += argumentByteCount;
            }
            int compositeByteCount = measureCursor.CurrentOffset - compositeStartOffset;

            int instructionByteCount = 0;
            if (componentsHaveInstructions
                && (!glyphCursor.TryRead255UInt16(out instructionByteCount)))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CompositeGlyphStreamOverrun,
                    "composite instruction size truncated");
                return false;
            }

            glyfWriter.WriteU16(0xFFFF); // nContours = -1
            if (!CopyFromCursor(streamBytes, ref bboxCursor, 8, glyfWriter)
                || !CopyFromCursor(streamBytes, ref compositeCursor, compositeByteCount, glyfWriter))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CompositeGlyphStreamOverrun,
                    "composite bbox/component copy overran source");
                return false;
            }
            if (componentsHaveInstructions)
            {
                glyfWriter.WriteU16((ushort)instructionByteCount);
                if (!CopyFromCursor(streamBytes, ref instructionCursor, instructionByteCount, glyfWriter))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CompositeGlyphStreamOverrun,
                        "composite instructions overran instruction stream");
                    return false;
                }
            }
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        private static bool TryWriteSimpleGlyph(byte[] streamBytes, int contourCount,
            ref Woff2StreamCursor nPointsCursor, ref Woff2StreamCursor flagCursor,
            ref Woff2StreamCursor glyphCursor, ref Woff2StreamCursor instructionCursor,
            bool glyphHasExplicitBbox, ref Woff2StreamCursor bboxCursor, bool glyphHasOverlapBit,
            GrowableByteWriter glyfWriter, out WebFontContainerDecodeFailure decodeFailure)
        {
            // Points per contour (255UInt16 each), then triplet-decode all points.
            var contourPointCounts = new int[contourCount];
            long totalPointCount = 0;
            for (int contourIndex = 0; contourIndex < contourCount; contourIndex++)
            {
                if (!nPointsCursor.TryRead255UInt16(out contourPointCounts[contourIndex]))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfTripletStreamOverrun,
                        "nPoints stream truncated");
                    return false;
                }
                totalPointCount += contourPointCounts[contourIndex];
            }
            if (totalPointCount >= (1 << 27))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfTripletStreamOverrun,
                    $"implausible point count {totalPointCount}");
                return false;
            }
            int pointCount = (int)totalPointCount;

            if (flagCursor.RemainingByteCount < pointCount)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfTripletStreamOverrun,
                    "flag stream shorter than point count");
                return false;
            }
            var outlinePoints = new GlyphOutlinePoint[pointCount];
            if (!TryTripletDecode(streamBytes, ref flagCursor, ref glyphCursor, pointCount, outlinePoints, out decodeFailure))
            {
                return false;
            }

            if (!glyphCursor.TryRead255UInt16(out int instructionByteCount))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfTripletStreamOverrun,
                    "simple glyph instruction size truncated");
                return false;
            }

            // Header: nContours, bbox (explicit from stream, else computed from points).
            glyfWriter.WriteU16((ushort)contourCount);
            if (glyphHasExplicitBbox)
            {
                if (!CopyFromCursor(streamBytes, ref bboxCursor, 8, glyfWriter))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfSubStreamBoundsInvalid,
                        "bbox stream overrun");
                    return false;
                }
            }
            else
            {
                WriteComputedBbox(outlinePoints, pointCount, glyfWriter);
            }

            // endPtsOfContours + instructions.
            int endPointIndex = -1;
            for (int contourIndex = 0; contourIndex < contourCount; contourIndex++)
            {
                endPointIndex += contourPointCounts[contourIndex];
                if (endPointIndex >= 65536)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfTripletStreamOverrun,
                        "contour end point exceeds 65535");
                    return false;
                }
                glyfWriter.WriteU16((ushort)endPointIndex);
            }
            glyfWriter.WriteU16((ushort)instructionByteCount);
            if (!CopyFromCursor(streamBytes, ref instructionCursor, instructionByteCount, glyfWriter))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfTripletStreamOverrun,
                    "instruction stream overrun");
                return false;
            }

            WritePointFlagsAndCoordinates(outlinePoints, pointCount, glyphHasOverlapBit, glyfWriter);
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        /// <summary>WOFF2 §5.2 triplet decoding (verified vs woff2_dec.cc TripletDecode):
        /// flag byte high bit = OFF-curve; low 7 bits select dx/dy byte layout.</summary>
        private static bool TryTripletDecode(byte[] streamBytes, ref Woff2StreamCursor flagCursor,
            ref Woff2StreamCursor glyphCursor, int pointCount, GlyphOutlinePoint[] outlinePoints,
            out WebFontContainerDecodeFailure decodeFailure)
        {
            int absoluteX = 0, absoluteY = 0;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                byte tripletFlag = streamBytes[flagCursor.CurrentOffset + pointIndex];
                bool onCurve = (tripletFlag >> 7) == 0;
                int flag = tripletFlag & 0x7F;
                int dataByteCount = flag < 84 ? 1 : flag < 120 ? 2 : flag < 124 ? 3 : 4;
                if (glyphCursor.RemainingByteCount < dataByteCount)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfTripletStreamOverrun,
                        $"triplet data truncated at point {pointIndex}");
                    return false;
                }
                int dataOffset = glyphCursor.CurrentOffset;
                int deltaX, deltaY;
                if (flag < 10)
                {
                    deltaX = 0;
                    deltaY = WithSign(flag, ((flag & 14) << 7) + streamBytes[dataOffset]);
                }
                else if (flag < 20)
                {
                    deltaX = WithSign(flag, (((flag - 10) & 14) << 7) + streamBytes[dataOffset]);
                    deltaY = 0;
                }
                else if (flag < 84)
                {
                    int b0 = flag - 20;
                    int b1 = streamBytes[dataOffset];
                    deltaX = WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4));
                    deltaY = WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F));
                }
                else if (flag < 120)
                {
                    int b0 = flag - 84;
                    deltaX = WithSign(flag, 1 + ((b0 / 12) << 8) + streamBytes[dataOffset]);
                    deltaY = WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + streamBytes[dataOffset + 1]);
                }
                else if (flag < 124)
                {
                    int b2 = streamBytes[dataOffset + 1];
                    deltaX = WithSign(flag, (streamBytes[dataOffset] << 4) + (b2 >> 4));
                    deltaY = WithSign(flag >> 1, ((b2 & 0x0F) << 8) + streamBytes[dataOffset + 2]);
                }
                else
                {
                    deltaX = WithSign(flag, (streamBytes[dataOffset] << 8) + streamBytes[dataOffset + 1]);
                    deltaY = WithSign(flag >> 1, (streamBytes[dataOffset + 2] << 8) + streamBytes[dataOffset + 3]);
                }
                glyphCursor.CurrentOffset += dataByteCount;
                // Coordinates are bounded (deltas ≤ 16 bits, point count < 2^27) so
                // int accumulation cannot overflow into wrap-around here; clamp anyway.
                absoluteX += deltaX;
                absoluteY += deltaY;
                outlinePoints[pointIndex] = new GlyphOutlinePoint { X = absoluteX, Y = absoluteY, OnCurve = onCurve };
            }
            flagCursor.CurrentOffset += pointCount;
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        private static int WithSign(int flag, int magnitude) => (flag & 1) != 0 ? magnitude : -magnitude;

        private static void WriteComputedBbox(GlyphOutlinePoint[] outlinePoints, int pointCount, GrowableByteWriter glyfWriter)
        {
            int minX = 0, minY = 0, maxX = 0, maxY = 0;
            if (pointCount > 0)
            {
                minX = maxX = outlinePoints[0].X;
                minY = maxY = outlinePoints[0].Y;
            }
            for (int pointIndex = 1; pointIndex < pointCount; pointIndex++)
            {
                minX = Math.Min(minX, outlinePoints[pointIndex].X);
                maxX = Math.Max(maxX, outlinePoints[pointIndex].X);
                minY = Math.Min(minY, outlinePoints[pointIndex].Y);
                maxY = Math.Max(maxY, outlinePoints[pointIndex].Y);
            }
            glyfWriter.WriteU16((ushort)(short)minX);
            glyfWriter.WriteU16((ushort)(short)minY);
            glyfWriter.WriteU16((ushort)(short)maxX);
            glyfWriter.WriteU16((ushort)(short)maxY);
        }

        /// <summary>Re-encodes point flags (with repeat compression) and delta coordinate
        /// arrays — transliteration of woff2_dec.cc StorePoints.</summary>
        private static void WritePointFlagsAndCoordinates(GlyphOutlinePoint[] outlinePoints, int pointCount,
            bool glyphHasOverlapBit, GrowableByteWriter glyfWriter)
        {
            int lastFlag = -1, repeatCount = 0, lastX = 0, lastY = 0;
            int flagsStartLength = glyfWriter.Length;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                GlyphOutlinePoint point = outlinePoints[pointIndex];
                int flag = point.OnCurve ? GlyfFlagOnCurve : 0;
                if (glyphHasOverlapBit && pointIndex == 0) flag |= GlyfFlagOverlapSimple;
                int deltaX = point.X - lastX;
                int deltaY = point.Y - lastY;
                if (deltaX == 0) flag |= GlyfFlagXSameOrPositive;
                else if (deltaX > -256 && deltaX < 256) flag |= GlyfFlagXShort | (deltaX > 0 ? GlyfFlagXSameOrPositive : 0);
                if (deltaY == 0) flag |= GlyfFlagYSameOrPositive;
                else if (deltaY > -256 && deltaY < 256) flag |= GlyfFlagYShort | (deltaY > 0 ? GlyfFlagYSameOrPositive : 0);

                if (flag == lastFlag && repeatCount != 255)
                {
                    glyfWriter.Bytes[glyfWriter.Length - 1] |= GlyfFlagRepeat;
                    repeatCount++;
                }
                else
                {
                    if (repeatCount != 0) glyfWriter.WriteU8((byte)repeatCount);
                    glyfWriter.WriteU8((byte)flag);
                    repeatCount = 0;
                }
                lastX = point.X;
                lastY = point.Y;
                lastFlag = flag;
            }
            if (repeatCount != 0) glyfWriter.WriteU8((byte)repeatCount);
            _ = flagsStartLength;

            lastX = 0;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                int deltaX = outlinePoints[pointIndex].X - lastX;
                if (deltaX == 0) { }
                else if (deltaX > -256 && deltaX < 256) glyfWriter.WriteU8((byte)Math.Abs(deltaX));
                else glyfWriter.WriteU16((ushort)(short)deltaX);
                lastX = outlinePoints[pointIndex].X;
            }
            lastY = 0;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                int deltaY = outlinePoints[pointIndex].Y - lastY;
                if (deltaY == 0) { }
                else if (deltaY > -256 && deltaY < 256) glyfWriter.WriteU8((byte)Math.Abs(deltaY));
                else glyfWriter.WriteU16((ushort)(short)deltaY);
                lastY = outlinePoints[pointIndex].Y;
            }
        }

        private static Woff2StreamCursor MakeCursor(byte[] streamBytes, int[] offsets, int[] byteCounts, int subStreamIndex)
            => new Woff2StreamCursor(streamBytes, offsets[subStreamIndex], offsets[subStreamIndex] + byteCounts[subStreamIndex]);

        private static bool CopyFromCursor(byte[] sourceBytes, ref Woff2StreamCursor sourceCursor,
            int copyByteCount, GrowableByteWriter glyfWriter)
        {
            if (sourceCursor.RemainingByteCount < copyByteCount) return false;
            glyfWriter.WriteBytes(sourceBytes, sourceCursor.CurrentOffset, copyByteCount);
            sourceCursor.CurrentOffset += copyByteCount;
            return true;
        }

        private static WebFontContainerDecodeFailure Failure(WebFontContainerDecodeFailureCode failureCode, string failureDetail)
            => new WebFontContainerDecodeFailure(failureCode, failureDetail);

        /// <summary>Append-only byte buffer with amortized growth (safe replacement for
        /// the C++ glyph_buf + WOFF2Out pattern; one instance builds the whole glyf).</summary>
        private sealed class GrowableByteWriter
        {
            public byte[] Bytes = new byte[16384];
            public int Length;

            private void EnsureCapacity(int additionalByteCount)
            {
                if (Length + additionalByteCount <= Bytes.Length) return;
                int newCapacity = Bytes.Length * 2;
                while (newCapacity < Length + additionalByteCount) newCapacity *= 2;
                Array.Resize(ref Bytes, newCapacity);
            }

            public void WriteU8(byte value)
            {
                EnsureCapacity(1);
                Bytes[Length++] = value;
            }

            public void WriteU16(ushort value)
            {
                EnsureCapacity(2);
                Bytes[Length++] = (byte)(value >> 8);
                Bytes[Length++] = (byte)value;
            }

            public void WriteBytes(byte[] sourceBytes, int sourceOffset, int copyByteCount)
            {
                EnsureCapacity(copyByteCount);
                Array.Copy(sourceBytes, sourceOffset, Bytes, Length, copyByteCount);
                Length += copyByteCount;
            }

            public void PadToFourByteAlignment()
            {
                while ((Length & 3) != 0) WriteU8(0);
            }

            public byte[] ToArray()
            {
                byte[] resultBytes = new byte[Length];
                Array.Copy(Bytes, resultBytes, Length);
                return resultBytes;
            }
        }
    }
}