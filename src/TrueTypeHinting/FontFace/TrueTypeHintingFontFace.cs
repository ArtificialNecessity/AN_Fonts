using System;

namespace StbTrueTypeSharp.TrueTypeHinting.FontFace
{
    /// <summary>Immutable, bounds-validated TrueType face snapshot owned by the hinting subsystem.</summary>
    public sealed class TrueTypeHintingFontFace
    {
        private readonly byte[] _glyphDataTableBytes;
        private readonly byte[] _glyphLocationTableBytes;
        private readonly short _glyphLocationFormat;

        internal TrueTypeHintingFontFace(TrueTypeFaceIndex trueTypeFaceIndex,
            TrueTypeUnitsPerEm trueTypeUnitsPerEm, TrueTypeGlyphCount trueTypeGlyphCount,
            TrueTypeHintingMaximumProfile trueTypeHintingMaximumProfile,
            TrueTypeHintingFontProgram trueTypeHintingFontProgram,
            byte[] glyphDataTableBytes, byte[] glyphLocationTableBytes, short glyphLocationFormat,
            TrueTypeTableBytes gridFittingAndScanConversionTable)
        {
            FaceIndex = trueTypeFaceIndex;
            UnitsPerEm = trueTypeUnitsPerEm;
            GlyphCount = trueTypeGlyphCount;
            MaximumProfile = trueTypeHintingMaximumProfile;
            FontProgram = trueTypeHintingFontProgram;
            _glyphDataTableBytes = glyphDataTableBytes ?? throw new ArgumentNullException(nameof(glyphDataTableBytes));
            _glyphLocationTableBytes = glyphLocationTableBytes ?? throw new ArgumentNullException(nameof(glyphLocationTableBytes));
            _glyphLocationFormat = glyphLocationFormat;
            GridFittingAndScanConversionTable = gridFittingAndScanConversionTable;
        }

        public TrueTypeFaceIndex FaceIndex { get; }
        public TrueTypeUnitsPerEm UnitsPerEm { get; }
        public TrueTypeGlyphCount GlyphCount { get; }
        public TrueTypeHintingMaximumProfile MaximumProfile { get; }
        public TrueTypeHintingFontProgram FontProgram { get; }
        public TrueTypeTableBytes GridFittingAndScanConversionTable { get; }

        /// <summary>Returns one immutable copy of the raw glyf record for a validated glyph index.</summary>
        public bool TryGetRawGlyphData(TrueTypeGlyphIndex trueTypeGlyphIndex,
            out TrueTypeTableBytes trueTypeRawGlyphData, out TrueTypeYHintingFailure trueTypeGlyphDataFailure)
        {
            if (trueTypeGlyphIndex.Value >= GlyphCount.Value)
            {
                trueTypeRawGlyphData = new TrueTypeTableBytes(Array.Empty<byte>());
                trueTypeGlyphDataFailure = Failure(TrueTypeHintingFailureCode.InvalidGlyphIndex,
                    trueTypeGlyphIndex + " exceeds glyph count " + GlyphCount.Value + ".");
                return false;
            }

            if (!TryReadGlyphLocation(trueTypeGlyphIndex.Value, out int glyphByteStart) ||
                !TryReadGlyphLocation(trueTypeGlyphIndex.Value + 1, out int glyphByteEnd) ||
                glyphByteStart < 0 || glyphByteEnd < glyphByteStart || glyphByteEnd > _glyphDataTableBytes.Length)
            {
                trueTypeRawGlyphData = new TrueTypeTableBytes(Array.Empty<byte>());
                trueTypeGlyphDataFailure = Failure(TrueTypeHintingFailureCode.MalformedGlyphData,
                    "The loca offsets do not describe a valid glyf record for " + trueTypeGlyphIndex + ".");
                return false;
            }

            byte[] rawGlyphDataBytes = new byte[glyphByteEnd - glyphByteStart];
            Buffer.BlockCopy(_glyphDataTableBytes, glyphByteStart, rawGlyphDataBytes, 0, rawGlyphDataBytes.Length);
            trueTypeRawGlyphData = new TrueTypeTableBytes(rawGlyphDataBytes);
            trueTypeGlyphDataFailure = default;
            return true;
        }

        private bool TryReadGlyphLocation(int glyphLocationEntryIndex, out int glyphDataByteOffset)
        {
            if (_glyphLocationFormat == 0)
            {
                int locaByteOffset = glyphLocationEntryIndex * 2;
                if (locaByteOffset < 0 || locaByteOffset > _glyphLocationTableBytes.Length - 2)
                {
                    glyphDataByteOffset = 0;
                    return false;
                }
                glyphDataByteOffset = ReadUInt16BigEndian(_glyphLocationTableBytes, locaByteOffset) * 2;
                return true;
            }

            int longLocaByteOffset = glyphLocationEntryIndex * 4;
            if (longLocaByteOffset < 0 || longLocaByteOffset > _glyphLocationTableBytes.Length - 4)
            {
                glyphDataByteOffset = 0;
                return false;
            }
            uint longGlyphDataByteOffset = ReadUInt32BigEndian(_glyphLocationTableBytes, longLocaByteOffset);
            if (longGlyphDataByteOffset > int.MaxValue)
            {
                glyphDataByteOffset = 0;
                return false;
            }
            glyphDataByteOffset = (int)longGlyphDataByteOffset;
            return true;
        }

        private static ushort ReadUInt16BigEndian(byte[] dataBytes, int byteOffset)
            => (ushort)((dataBytes[byteOffset] << 8) | dataBytes[byteOffset + 1]);

        private static uint ReadUInt32BigEndian(byte[] dataBytes, int byteOffset)
            => (uint)(dataBytes[byteOffset] << 24 | dataBytes[byteOffset + 1] << 16 |
                dataBytes[byteOffset + 2] << 8 | dataBytes[byteOffset + 3]);

        private static TrueTypeYHintingFailure Failure(TrueTypeHintingFailureCode failureCode, string failureMessage)
            => new TrueTypeYHintingFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}