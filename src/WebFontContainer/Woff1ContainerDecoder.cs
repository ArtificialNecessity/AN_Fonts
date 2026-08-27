using System;

namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Decodes a W3C WOFF 1.0 container (spec §3–§4) into a plain sfnt byte stream.
    /// Every table is either stored raw (compLength == origLength) or zlib-compressed
    /// (compLength &lt; origLength). The sfnt is rebuilt with a fresh header/directory,
    /// 4-byte-aligned tables in file order, and a recomputed head.checkSumAdjustment
    /// (plans/woff_web_fonts.md: recompute, spec-compliant).
    /// All reads are bounds-validated; malformed input reports a failure code, never throws.
    /// </summary>
    internal static class Woff1ContainerDecoder
    {
        private const int WoffHeaderByteCount = 44;
        private const int WoffTableDirectoryEntryByteCount = 20;
        private const int SfntHeaderByteCount = 12;
        private const int SfntTableDirectoryEntryByteCount = 16;
        private const uint HeadTableTag = 0x68656164; // 'head'
        private const uint CheckSumAdjustmentMagic = 0xB1B0AFBA;

        internal static bool TryDecode(byte[] woffFileBytes, IWebFontDecompressor webFontDecompressor,
            int reconstructedSfntByteCountCap,
            out byte[] reconstructedSfntBytes, out WebFontContainerDecodeFailure decodeFailure)
        {
            reconstructedSfntBytes = null;

            if (woffFileBytes.Length < WoffHeaderByteCount)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.TruncatedContainerHeader,
                    $"WOFF1 file is {woffFileBytes.Length} bytes; header needs {WoffHeaderByteCount}");
                return false;
            }

            // ── WOFF header (spec §3) ──
            uint sfntFlavor = ReadU32(woffFileBytes, 4);
            uint declaredWoffFileLength = ReadU32(woffFileBytes, 8);
            int woffTableCount = ReadU16(woffFileBytes, 12);
            int reservedField = ReadU16(woffFileBytes, 14);
            uint declaredTotalSfntSize = ReadU32(woffFileBytes, 16);
            // majorVersion(20) minorVersion(22) metaOffset(24) metaLength(28)
            // metaOrigLength(32) privOffset(36) privLength(40): not needed for sfnt rebuild.

            if (reservedField != 0)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff1ReservedFieldNonZero,
                    $"reserved field is {reservedField}");
                return false;
            }
            if (declaredWoffFileLength != (uint)woffFileBytes.Length)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff1DeclaredLengthMismatch,
                    $"header declares {declaredWoffFileLength} bytes, file is {woffFileBytes.Length}");
                return false;
            }
            if (declaredTotalSfntSize > (uint)reconstructedSfntByteCountCap)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.ReconstructedSfntSizeExceedsCap,
                    $"totalSfntSize {declaredTotalSfntSize} exceeds cap {reconstructedSfntByteCountCap}");
                return false;
            }

            long tableDirectoryEndOffset = WoffHeaderByteCount + (long)woffTableCount * WoffTableDirectoryEntryByteCount;
            if (tableDirectoryEndOffset > woffFileBytes.Length)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff1TableDirectoryTruncated,
                    $"{woffTableCount} directory entries need {tableDirectoryEndOffset} bytes, file is {woffFileBytes.Length}");
                return false;
            }

            // ── WOFF table directory (spec §4): tag/offset/compLength/origLength/origChecksum ──
            var woffTableEntries = new Woff1TableDirectoryEntry[woffTableCount];
            long reconstructedSfntByteCount = SfntHeaderByteCount + (long)woffTableCount * SfntTableDirectoryEntryByteCount;
            uint previousTableTag = 0;
            for (int tableIndex = 0; tableIndex < woffTableCount; tableIndex++)
            {
                int entryOffset = WoffHeaderByteCount + tableIndex * WoffTableDirectoryEntryByteCount;
                Woff1TableDirectoryEntry entry;
                entry.TableTag = ReadU32(woffFileBytes, entryOffset);
                entry.CompressedDataOffset = ReadU32(woffFileBytes, entryOffset + 4);
                entry.CompressedByteCount = ReadU32(woffFileBytes, entryOffset + 8);
                entry.OriginalByteCount = ReadU32(woffFileBytes, entryOffset + 12);
                entry.OriginalChecksum = ReadU32(woffFileBytes, entryOffset + 16);

                if (tableIndex > 0 && entry.TableTag <= previousTableTag)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff1TableDirectoryNotAscendingByTag,
                        $"entry {tableIndex} tag 0x{entry.TableTag:X8} <= previous 0x{previousTableTag:X8}");
                    return false;
                }
                previousTableTag = entry.TableTag;

                if (entry.CompressedByteCount > entry.OriginalByteCount)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff1TableCompressedLargerThanOriginal,
                        $"table 0x{entry.TableTag:X8}: compLength {entry.CompressedByteCount} > origLength {entry.OriginalByteCount}");
                    return false;
                }
                long compressedDataEnd = (long)entry.CompressedDataOffset + entry.CompressedByteCount;
                if (entry.CompressedDataOffset < tableDirectoryEndOffset || compressedDataEnd > woffFileBytes.Length)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff1TableOffsetOutOfBounds,
                        $"table 0x{entry.TableTag:X8}: data [{entry.CompressedDataOffset}, {compressedDataEnd}) outside [{tableDirectoryEndOffset}, {woffFileBytes.Length})");
                    return false;
                }

                reconstructedSfntByteCount = Align4(reconstructedSfntByteCount) + entry.OriginalByteCount;
                if (reconstructedSfntByteCount > reconstructedSfntByteCountCap)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.ReconstructedSfntSizeExceedsCap,
                        $"running sfnt size {reconstructedSfntByteCount} exceeds cap {reconstructedSfntByteCountCap}");
                    return false;
                }
                woffTableEntries[tableIndex] = entry;
            }
            reconstructedSfntByteCount = Align4(reconstructedSfntByteCount);

            // Spec §3: totalSfntSize MUST equal the size of the reconstructed sfnt.
            if (declaredTotalSfntSize != (uint)reconstructedSfntByteCount)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.TotalSfntSizeMismatch,
                    $"header totalSfntSize {declaredTotalSfntSize}, computed {reconstructedSfntByteCount}");
                return false;
            }

            // ── Rebuild the sfnt: header, directory, then tables 4-byte-aligned in directory order ──
            byte[] sfntBytes = new byte[reconstructedSfntByteCount];
            WriteU32(sfntBytes, 0, sfntFlavor);
            WriteU16(sfntBytes, 4, (ushort)woffTableCount);
            ComputeBinarySearchFields(woffTableCount, out ushort searchRange, out ushort entrySelector, out ushort rangeShift);
            WriteU16(sfntBytes, 6, searchRange);
            WriteU16(sfntBytes, 8, entrySelector);
            WriteU16(sfntBytes, 10, rangeShift);

            int nextTableWriteOffset = SfntHeaderByteCount + woffTableCount * SfntTableDirectoryEntryByteCount;
            int headTableOffset = -1;
            int headTableByteCount = 0;
            for (int tableIndex = 0; tableIndex < woffTableCount; tableIndex++)
            {
                Woff1TableDirectoryEntry entry = woffTableEntries[tableIndex];
                nextTableWriteOffset = (int)Align4(nextTableWriteOffset);

                int directoryEntryOffset = SfntHeaderByteCount + tableIndex * SfntTableDirectoryEntryByteCount;
                WriteU32(sfntBytes, directoryEntryOffset, entry.TableTag);
                WriteU32(sfntBytes, directoryEntryOffset + 4, entry.OriginalChecksum);
                WriteU32(sfntBytes, directoryEntryOffset + 8, (uint)nextTableWriteOffset);
                WriteU32(sfntBytes, directoryEntryOffset + 12, entry.OriginalByteCount);

                if (entry.CompressedByteCount == entry.OriginalByteCount)
                {
                    // Stored raw (spec §4: same length means uncompressed).
                    Array.Copy(woffFileBytes, (int)entry.CompressedDataOffset,
                        sfntBytes, nextTableWriteOffset, (int)entry.OriginalByteCount);
                }
                else
                {
                    byte[] inflatedTableBytes = new byte[entry.OriginalByteCount];
                    if (!webFontDecompressor.TryInflateZlibFramedStream(woffFileBytes,
                        (int)entry.CompressedDataOffset, (int)entry.CompressedByteCount,
                        inflatedTableBytes, out decodeFailure))
                    {
                        return false;
                    }
                    Array.Copy(inflatedTableBytes, 0, sfntBytes, nextTableWriteOffset, inflatedTableBytes.Length);
                }

                if (entry.TableTag == HeadTableTag && entry.OriginalByteCount >= 12)
                {
                    headTableOffset = nextTableWriteOffset;
                    headTableByteCount = (int)entry.OriginalByteCount;
                }
                nextTableWriteOffset += (int)entry.OriginalByteCount;
            }

            RecomputeHeadCheckSumAdjustment(sfntBytes, headTableOffset, headTableByteCount);

            reconstructedSfntBytes = sfntBytes;
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        /// <summary>
        /// Spec-compliant checkSumAdjustment (plans/woff_web_fonts.md — resolved: recompute):
        /// zero the field (head+8), sum the whole file as big-endian u32 words (implicit
        /// zero padding), store 0xB1B0AFBA minus the sum. No-op if head was absent/short.
        /// </summary>
        private static void RecomputeHeadCheckSumAdjustment(byte[] sfntBytes, int headTableOffset, int headTableByteCount)
        {
            if (headTableOffset < 0 || headTableByteCount < 12)
            {
                return;
            }
            WriteU32(sfntBytes, headTableOffset + 8, 0);
            uint wholeFontChecksum = 0;
            for (int wordOffset = 0; wordOffset < sfntBytes.Length; wordOffset += 4)
            {
                uint wordValue = 0;
                for (int byteIndex = 0; byteIndex < 4; byteIndex++)
                {
                    int byteOffset = wordOffset + byteIndex;
                    wordValue = (wordValue << 8) | (byteOffset < sfntBytes.Length ? sfntBytes[byteOffset] : (byte)0);
                }
                unchecked { wholeFontChecksum += wordValue; }
            }
            WriteU32(sfntBytes, headTableOffset + 8, unchecked(CheckSumAdjustmentMagic - wholeFontChecksum));
        }

        private static void ComputeBinarySearchFields(int tableCount,
            out ushort searchRange, out ushort entrySelector, out ushort rangeShift)
        {
            int largestPowerOfTwo = 1;
            int log2 = 0;
            while (largestPowerOfTwo * 2 <= tableCount)
            {
                largestPowerOfTwo *= 2;
                log2++;
            }
            searchRange = (ushort)(largestPowerOfTwo * 16);
            entrySelector = (ushort)log2;
            rangeShift = (ushort)(tableCount * 16 - searchRange);
        }

        private struct Woff1TableDirectoryEntry
        {
            public uint TableTag;
            public uint CompressedDataOffset;
            public uint CompressedByteCount;
            public uint OriginalByteCount;
            public uint OriginalChecksum;
        }

        private static long Align4(long value) => (value + 3) & ~3L;

        private static WebFontContainerDecodeFailure Failure(WebFontContainerDecodeFailureCode failureCode, string failureDetail)
            => new WebFontContainerDecodeFailure(failureCode, failureDetail);

        private static ushort ReadU16(byte[] bytes, int offset)
            => (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

        private static uint ReadU32(byte[] bytes, int offset)
            => SystemCompressionWebFontDecompressor.ReadBigEndianUInt32(bytes, offset);

        private static void WriteU16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)(value >> 8);
            bytes[offset + 1] = (byte)value;
        }

        private static void WriteU32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }
    }
}