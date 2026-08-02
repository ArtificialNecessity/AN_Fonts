using System;
using System.Collections.Generic;

namespace StbTrueTypeSharp.TrueTypeHinting.FontFace
{
    /// <summary>Bounds-checking SFNT/TTC reader used only by the TrueType hinting subsystem.</summary>
    internal sealed class TrueTypeHintingTableReader
    {
        private const uint TrueTypeVersionOne = 0x00010000;
        private const uint AppleTrueTypeVersion = 0x74727565; // 'true'
        private const uint TrueTypeCollectionTag = 0x74746366; // 'ttcf'

        private readonly byte[] _fontFileBytes;
        private readonly Dictionary<TrueTypeTableTag, TrueTypeTableRange> _trueTypeTableRanges;

        private TrueTypeHintingTableReader(byte[] fontFileBytes,
            Dictionary<TrueTypeTableTag, TrueTypeTableRange> trueTypeTableRanges)
        {
            _fontFileBytes = fontFileBytes;
            _trueTypeTableRanges = trueTypeTableRanges;
        }

        internal static bool TryCreate(byte[] fontFileBytes, TrueTypeFaceIndex trueTypeFaceIndex,
            out TrueTypeHintingTableReader trueTypeHintingTableReader,
            out TrueTypeYHintingFailure trueTypeTableReaderFailure)
        {
            trueTypeHintingTableReader = null;
            if (fontFileBytes == null)
            {
                trueTypeTableReaderFailure = Failure(TrueTypeHintingFailureCode.NullFontData, "Font data is null.");
                return false;
            }

            if (!TryResolveFaceByteOffset(fontFileBytes, trueTypeFaceIndex, out int faceByteOffset, out trueTypeTableReaderFailure))
                return false;
            if (!CanRead(fontFileBytes, faceByteOffset, 12))
            {
                trueTypeTableReaderFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory, "The SFNT offset table is truncated.");
                return false;
            }

            uint sfntVersion = ReadUInt32(fontFileBytes, faceByteOffset);
            if (sfntVersion != TrueTypeVersionOne && sfntVersion != AppleTrueTypeVersion)
            {
                trueTypeTableReaderFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory,
                    "The selected face is not a TrueType-outline SFNT face.");
                return false;
            }

            int trueTypeTableCount = ReadUInt16(fontFileBytes, faceByteOffset + 4);
            long tableDirectoryByteLength = 12L + trueTypeTableCount * 16L;
            if (tableDirectoryByteLength > int.MaxValue || !CanRead(fontFileBytes, faceByteOffset, (int)tableDirectoryByteLength))
            {
                trueTypeTableReaderFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory, "The SFNT table directory is truncated.");
                return false;
            }

            var trueTypeTableRanges = new Dictionary<TrueTypeTableTag, TrueTypeTableRange>(trueTypeTableCount);
            for (int tableRecordIndex = 0; tableRecordIndex < trueTypeTableCount; tableRecordIndex++)
            {
                int tableRecordByteOffset = faceByteOffset + 12 + tableRecordIndex * 16;
                var trueTypeTableTag = new TrueTypeTableTag(ReadUInt32(fontFileBytes, tableRecordByteOffset));
                uint tableByteOffsetUnsigned = ReadUInt32(fontFileBytes, tableRecordByteOffset + 8);
                uint tableByteLengthUnsigned = ReadUInt32(fontFileBytes, tableRecordByteOffset + 12);
                if (tableByteOffsetUnsigned > int.MaxValue || tableByteLengthUnsigned > int.MaxValue ||
                    !CanRead(fontFileBytes, (int)tableByteOffsetUnsigned, (int)tableByteLengthUnsigned))
                {
                    trueTypeTableReaderFailure = Failure(TrueTypeHintingFailureCode.TruncatedTable,
                        "SFNT table '" + trueTypeTableTag + "' lies outside the font data.");
                    return false;
                }
                if (trueTypeTableRanges.ContainsKey(trueTypeTableTag))
                {
                    trueTypeTableReaderFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory,
                        "The SFNT table directory contains duplicate table '" + trueTypeTableTag + "'.");
                    return false;
                }
                trueTypeTableRanges.Add(trueTypeTableTag, new TrueTypeTableRange((int)tableByteOffsetUnsigned, (int)tableByteLengthUnsigned));
            }

            trueTypeHintingTableReader = new TrueTypeHintingTableReader(fontFileBytes, trueTypeTableRanges);
            trueTypeTableReaderFailure = default;
            return true;
        }

        internal bool TryCopyRequiredTable(TrueTypeTableTag trueTypeTableTag, int minimumTableByteLength,
            out TrueTypeTableBytes trueTypeTableBytes, out TrueTypeYHintingFailure trueTypeTableFailure)
        {
            if (!_trueTypeTableRanges.TryGetValue(trueTypeTableTag, out TrueTypeTableRange trueTypeTableRange))
            {
                trueTypeTableBytes = EmptyTable();
                trueTypeTableFailure = Failure(TrueTypeHintingFailureCode.MissingRequiredTable,
                    "Required TrueType table '" + trueTypeTableTag + "' is missing.");
                return false;
            }
            if (trueTypeTableRange.ByteLength < minimumTableByteLength)
            {
                trueTypeTableBytes = EmptyTable();
                trueTypeTableFailure = Failure(TrueTypeHintingFailureCode.TruncatedTable,
                    "TrueType table '" + trueTypeTableTag + "' is shorter than " + minimumTableByteLength + " bytes.");
                return false;
            }
            trueTypeTableBytes = CopyTable(trueTypeTableRange);
            trueTypeTableFailure = default;
            return true;
        }

        internal TrueTypeTableBytes CopyOptionalTable(TrueTypeTableTag trueTypeTableTag)
            => _trueTypeTableRanges.TryGetValue(trueTypeTableTag, out TrueTypeTableRange trueTypeTableRange)
                ? CopyTable(trueTypeTableRange)
                : EmptyTable();

        private TrueTypeTableBytes CopyTable(TrueTypeTableRange trueTypeTableRange)
        {
            byte[] trueTypeTableBytes = new byte[trueTypeTableRange.ByteLength];
            Buffer.BlockCopy(_fontFileBytes, trueTypeTableRange.ByteOffset, trueTypeTableBytes, 0, trueTypeTableBytes.Length);
            return new TrueTypeTableBytes(trueTypeTableBytes);
        }

        private static bool TryResolveFaceByteOffset(byte[] fontFileBytes, TrueTypeFaceIndex trueTypeFaceIndex,
            out int faceByteOffset, out TrueTypeYHintingFailure trueTypeFaceFailure)
        {
            faceByteOffset = 0;
            if (!CanRead(fontFileBytes, 0, 4))
            {
                trueTypeFaceFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory, "Font data is shorter than an SFNT signature.");
                return false;
            }

            if (ReadUInt32(fontFileBytes, 0) != TrueTypeCollectionTag)
            {
                if (trueTypeFaceIndex.Value != 0)
                {
                    trueTypeFaceFailure = Failure(TrueTypeHintingFailureCode.InvalidFaceIndex,
                        "A standalone SFNT font contains only face index zero.");
                    return false;
                }
                trueTypeFaceFailure = default;
                return true;
            }

            if (!CanRead(fontFileBytes, 0, 12))
            {
                trueTypeFaceFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory, "The TTC header is truncated.");
                return false;
            }
            uint trueTypeCollectionFaceCount = ReadUInt32(fontFileBytes, 8);
            if ((uint)trueTypeFaceIndex.Value >= trueTypeCollectionFaceCount)
            {
                trueTypeFaceFailure = Failure(TrueTypeHintingFailureCode.InvalidFaceIndex,
                    "The requested TTC face index is outside the collection.");
                return false;
            }
            int faceOffsetEntryByteOffset = 12 + trueTypeFaceIndex.Value * 4;
            if (!CanRead(fontFileBytes, faceOffsetEntryByteOffset, 4))
            {
                trueTypeFaceFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory, "The TTC face-offset array is truncated.");
                return false;
            }
            uint faceByteOffsetUnsigned = ReadUInt32(fontFileBytes, faceOffsetEntryByteOffset);
            if (faceByteOffsetUnsigned > int.MaxValue || !CanRead(fontFileBytes, (int)faceByteOffsetUnsigned, 4))
            {
                trueTypeFaceFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory, "The TTC face offset is invalid.");
                return false;
            }
            faceByteOffset = (int)faceByteOffsetUnsigned;
            trueTypeFaceFailure = default;
            return true;
        }

        internal static ushort ReadUInt16(byte[] dataBytes, int byteOffset)
            => (ushort)((dataBytes[byteOffset] << 8) | dataBytes[byteOffset + 1]);
        internal static short ReadInt16(byte[] dataBytes, int byteOffset)
            => unchecked((short)ReadUInt16(dataBytes, byteOffset));
        internal static uint ReadUInt32(byte[] dataBytes, int byteOffset)
            => (uint)(dataBytes[byteOffset] << 24 | dataBytes[byteOffset + 1] << 16 | dataBytes[byteOffset + 2] << 8 | dataBytes[byteOffset + 3]);

        private static bool CanRead(byte[] dataBytes, int byteOffset, int byteLength)
            => byteOffset >= 0 && byteLength >= 0 && byteOffset <= dataBytes.Length - byteLength;

        private static TrueTypeTableBytes EmptyTable() => new TrueTypeTableBytes(Array.Empty<byte>());
        private static TrueTypeYHintingFailure Failure(TrueTypeHintingFailureCode failureCode, string failureMessage)
            => new TrueTypeYHintingFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}