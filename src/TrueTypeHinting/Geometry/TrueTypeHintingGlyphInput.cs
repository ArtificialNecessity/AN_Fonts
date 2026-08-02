using System;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;

namespace StbTrueTypeSharp.TrueTypeHinting.Geometry
{
    internal readonly struct TrueTypeGlyphInstructionBytes
    {
        private readonly byte[] _trueTypeGlyphInstructionBytes;
        internal TrueTypeGlyphInstructionBytes(byte[] trueTypeGlyphInstructionBytes)
            => _trueTypeGlyphInstructionBytes = trueTypeGlyphInstructionBytes == null
                ? new byte[0]
                : (byte[])trueTypeGlyphInstructionBytes.Clone();
        internal int ByteLength => _trueTypeGlyphInstructionBytes == null ? 0 : _trueTypeGlyphInstructionBytes.Length;
        internal byte[] ToByteArray() => _trueTypeGlyphInstructionBytes == null
            ? new byte[0]
            : (byte[])_trueTypeGlyphInstructionBytes.Clone();
    }

    /// <summary>Scaled simple-glyph points, contours, and instruction bytes ready for VM execution.</summary>
    internal sealed class TrueTypeHintingGlyphInput
    {
        internal TrueTypeHintingGlyphInput(TrueTypeGlyphIndex trueTypeGlyphIndex,
            TrueTypeHintingZone glyphZone, TrueTypeGlyphInstructionBytes glyphInstructionBytes)
        {
            TrueTypeGlyphIndex = trueTypeGlyphIndex;
            GlyphZone = glyphZone ?? throw new ArgumentNullException(nameof(glyphZone));
            GlyphInstructionBytes = glyphInstructionBytes;
        }

        internal TrueTypeGlyphIndex TrueTypeGlyphIndex { get; }
        internal TrueTypeHintingZone GlyphZone { get; }
        internal TrueTypeGlyphInstructionBytes GlyphInstructionBytes { get; }
    }

    internal static class TrueTypeSimpleGlyphParser
    {
        private const byte OnCurvePointFlag = 0x01;
        private const byte HorizontalShortVectorFlag = 0x02;
        private const byte VerticalShortVectorFlag = 0x04;
        private const byte RepeatFlag = 0x08;
        private const byte HorizontalSameOrPositiveShortFlag = 0x10;
        private const byte VerticalSameOrPositiveShortFlag = 0x20;

        internal static bool TryParse(TrueTypeHintingFontFace trueTypeHintingFontFace, DevicePpemY devicePpemY,
            TrueTypeGlyphIndex trueTypeGlyphIndex, out TrueTypeHintingGlyphInput trueTypeHintingGlyphInput,
            out TrueTypeYHintingFailure trueTypeGlyphParsingFailure)
        {
            trueTypeHintingGlyphInput = null;
            if (trueTypeHintingFontFace == null) throw new ArgumentNullException(nameof(trueTypeHintingFontFace));
            if (!trueTypeHintingFontFace.TryGetRawGlyphData(trueTypeGlyphIndex,
                    out TrueTypeTableBytes trueTypeRawGlyphData, out trueTypeGlyphParsingFailure))
                return false;

            byte[] rawGlyphDataBytes = trueTypeRawGlyphData.ToByteArray();
            if (rawGlyphDataBytes.Length == 0)
            {
                trueTypeHintingGlyphInput = new TrueTypeHintingGlyphInput(trueTypeGlyphIndex,
                    new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]),
                    new TrueTypeGlyphInstructionBytes(new byte[0]));
                trueTypeGlyphParsingFailure = default;
                return true;
            }
            if (rawGlyphDataBytes.Length < 10)
                return Failed("The simple glyf record is shorter than its header.", out trueTypeGlyphParsingFailure);

            short contourCount = ReadInt16(rawGlyphDataBytes, 0);
            if (contourCount < 0)
                return Failed("The requested glyph is composite; Phase 3 currently accepts simple glyphs only.", out trueTypeGlyphParsingFailure);
            if (contourCount == 0)
            {
                trueTypeHintingGlyphInput = new TrueTypeHintingGlyphInput(trueTypeGlyphIndex,
                    new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]),
                    new TrueTypeGlyphInstructionBytes(new byte[0]));
                trueTypeGlyphParsingFailure = default;
                return true;
            }

            int byteCursor = 10;
            if (!CanRead(rawGlyphDataBytes, byteCursor, contourCount * 2 + 2))
                return Failed("The simple glyph contour endpoints or instruction length are truncated.", out trueTypeGlyphParsingFailure);
            var contourEndPointIndices = new TrueTypeContourEndPointIndex[contourCount];
            int previousContourEndPointIndex = -1;
            for (int contourIndex = 0; contourIndex < contourCount; contourIndex++)
            {
                int contourEndPointIndex = ReadUInt16(rawGlyphDataBytes, byteCursor);
                byteCursor += 2;
                if (contourEndPointIndex <= previousContourEndPointIndex)
                    return Failed("Simple glyph contour endpoints are not strictly increasing.", out trueTypeGlyphParsingFailure);
                contourEndPointIndices[contourIndex] = new TrueTypeContourEndPointIndex(contourEndPointIndex);
                previousContourEndPointIndex = contourEndPointIndex;
            }
            int pointCount = previousContourEndPointIndex + 1;
            int instructionByteLength = ReadUInt16(rawGlyphDataBytes, byteCursor);
            byteCursor += 2;
            if (!CanRead(rawGlyphDataBytes, byteCursor, instructionByteLength))
                return Failed("The simple glyph instruction stream is truncated.", out trueTypeGlyphParsingFailure);
            byte[] glyphInstructionBytes = new byte[instructionByteLength];
            Buffer.BlockCopy(rawGlyphDataBytes, byteCursor, glyphInstructionBytes, 0, instructionByteLength);
            byteCursor += instructionByteLength;

            var pointFlags = new byte[pointCount];
            int pointFlagIndex = 0;
            while (pointFlagIndex < pointCount)
            {
                if (!CanRead(rawGlyphDataBytes, byteCursor, 1))
                    return Failed("The simple glyph point flags are truncated.", out trueTypeGlyphParsingFailure);
                byte pointFlag = rawGlyphDataBytes[byteCursor++];
                pointFlags[pointFlagIndex++] = pointFlag;
                if ((pointFlag & RepeatFlag) == 0) continue;
                if (!CanRead(rawGlyphDataBytes, byteCursor, 1))
                    return Failed("A repeated simple-glyph flag has no repeat count.", out trueTypeGlyphParsingFailure);
                int repeatedFlagCount = rawGlyphDataBytes[byteCursor++];
                if (repeatedFlagCount > pointCount - pointFlagIndex)
                    return Failed("A simple-glyph flag repeat exceeds the point count.", out trueTypeGlyphParsingFailure);
                for (int repeatedFlagIndex = 0; repeatedFlagIndex < repeatedFlagCount; repeatedFlagIndex++)
                    pointFlags[pointFlagIndex++] = pointFlag;
            }

            var horizontalFontUnits = new int[pointCount];
            var verticalFontUnits = new int[pointCount];
            int currentHorizontalFontUnits = 0;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                if (!TryReadCoordinateDelta(rawGlyphDataBytes, ref byteCursor, pointFlags[pointIndex],
                        HorizontalShortVectorFlag, HorizontalSameOrPositiveShortFlag,
                        out int coordinateDeltaFontUnits))
                    return Failed("The simple glyph horizontal coordinates are truncated.", out trueTypeGlyphParsingFailure);
                currentHorizontalFontUnits += coordinateDeltaFontUnits;
                horizontalFontUnits[pointIndex] = currentHorizontalFontUnits;
            }
            int currentVerticalFontUnits = 0;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                if (!TryReadCoordinateDelta(rawGlyphDataBytes, ref byteCursor, pointFlags[pointIndex],
                        VerticalShortVectorFlag, VerticalSameOrPositiveShortFlag,
                        out int coordinateDeltaFontUnits))
                    return Failed("The simple glyph vertical coordinates are truncated.", out trueTypeGlyphParsingFailure);
                currentVerticalFontUnits += coordinateDeltaFontUnits;
                verticalFontUnits[pointIndex] = currentVerticalFontUnits;
            }

            var hintingPoints = new TrueTypeHintingPoint[pointCount];
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                hintingPoints[pointIndex] = new TrueTypeHintingPoint(
                    ScaleFontUnitsToF26Dot6(horizontalFontUnits[pointIndex], trueTypeHintingFontFace.UnitsPerEm.Value, devicePpemY.Value),
                    ScaleFontUnitsToF26Dot6(verticalFontUnits[pointIndex], trueTypeHintingFontFace.UnitsPerEm.Value, devicePpemY.Value),
                    (pointFlags[pointIndex] & OnCurvePointFlag) != 0);
            }

            trueTypeHintingGlyphInput = new TrueTypeHintingGlyphInput(trueTypeGlyphIndex,
                new TrueTypeHintingZone(hintingPoints, contourEndPointIndices),
                new TrueTypeGlyphInstructionBytes(glyphInstructionBytes));
            trueTypeGlyphParsingFailure = default;
            return true;
        }

        private static bool TryReadCoordinateDelta(byte[] rawGlyphDataBytes, ref int byteCursor, byte pointFlag,
            byte shortVectorFlag, byte sameOrPositiveShortFlag, out int coordinateDeltaFontUnits)
        {
            if ((pointFlag & shortVectorFlag) != 0)
            {
                if (!CanRead(rawGlyphDataBytes, byteCursor, 1)) { coordinateDeltaFontUnits = 0; return false; }
                int shortMagnitude = rawGlyphDataBytes[byteCursor++];
                coordinateDeltaFontUnits = (pointFlag & sameOrPositiveShortFlag) != 0 ? shortMagnitude : -shortMagnitude;
                return true;
            }
            if ((pointFlag & sameOrPositiveShortFlag) != 0)
            {
                coordinateDeltaFontUnits = 0;
                return true;
            }
            if (!CanRead(rawGlyphDataBytes, byteCursor, 2)) { coordinateDeltaFontUnits = 0; return false; }
            coordinateDeltaFontUnits = ReadInt16(rawGlyphDataBytes, byteCursor);
            byteCursor += 2;
            return true;
        }

        private static int ScaleFontUnitsToF26Dot6(int fontUnitValue, int unitsPerEm, int devicePpemY)
        {
            long numerator = (long)fontUnitValue * devicePpemY * 64;
            return numerator >= 0 ? (int)((numerator + unitsPerEm / 2) / unitsPerEm)
                : (int)-((-numerator + unitsPerEm / 2) / unitsPerEm);
        }

        private static ushort ReadUInt16(byte[] dataBytes, int byteOffset)
            => (ushort)((dataBytes[byteOffset] << 8) | dataBytes[byteOffset + 1]);
        private static short ReadInt16(byte[] dataBytes, int byteOffset)
            => unchecked((short)ReadUInt16(dataBytes, byteOffset));
        private static bool CanRead(byte[] dataBytes, int byteOffset, int byteLength)
            => byteOffset >= 0 && byteLength >= 0 && byteOffset <= dataBytes.Length - byteLength;

        private static bool Failed(string failureMessage, out TrueTypeYHintingFailure failure)
        {
            failure = new TrueTypeYHintingFailure(TrueTypeHintingFailureCode.MalformedGlyphData,
                new TrueTypeHintingFailureMessage(failureMessage));
            return false;
        }
    }
}