using System;
using System.Collections.Generic;

namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Decodes a W3C WOFF2 container into a plain sfnt/TTC byte stream
    /// (plans/woff_web_fonts.md Phases 2-4; logic verified against
    /// 3P_woff2/src/woff2_dec.cc and the W3C WOFF2 spec).
    /// The single Brotli stream is decoded via the decompression seam; transformed
    /// glyf/loca tables are reversed by <see cref="Woff2TransformedGlyfReconstructor"/>;
    /// transformed hmtx is rebuilt here from the glyf reconstruction's xMin data.
    /// All reads bounds-validated; hostile input reports failure codes, never throws.
    /// </summary>
    internal static class Woff2ContainerDecoder
    {
        private const int Woff2HeaderByteCount = 48;
        private const int SfntHeaderByteCount = 12;
        private const int SfntTableDirectoryEntryByteCount = 16;
        private const uint CheckSumAdjustmentMagic = 0xB1B0AFBA;

        private sealed class Woff2TableRecord
        {
            public uint TableTag;
            public int TransformVersion;      // flag byte bits 6-7
            public bool IsTransformed;        // glyf/loca: version 0; others: version != 0
            public uint SourceOffset;         // into the decompressed stream
            public uint SourceByteCount;      // transformLength (== origLength when untransformed)
            public uint FinalByteCount;       // origLength (loca: derived after glyf reconstruction)
            public uint FinalOffset;          // into the reconstructed sfnt
            public uint TableChecksum;
            public byte[] ReconstructedTableBytes; // set for transformed glyf/loca/hmtx
            public bool HasBeenReconstructed;
        }

        private sealed class Woff2CollectionFontRecord
        {
            public uint SfntFlavor;
            public int[] TableRecordIndices;
        }

        internal static bool TryDecode(byte[] woff2FileBytes, IWebFontDecompressor webFontDecompressor,
            int reconstructedSfntByteCountCap,
            out byte[] reconstructedSfntBytes, out WebFontContainerDecodeFailure decodeFailure)
        {
            reconstructedSfntBytes = null;

            // ── Header (WOFF2 §4.1, 48 bytes) ──
            if (woff2FileBytes.Length < Woff2HeaderByteCount)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.TruncatedContainerHeader,
                    $"WOFF2 file is {woff2FileBytes.Length} bytes; header needs {Woff2HeaderByteCount}");
                return false;
            }
            var headerCursor = new Woff2StreamCursor(woff2FileBytes, 4, woff2FileBytes.Length);
            headerCursor.TryReadU32(out uint sfntFlavor);
            headerCursor.TryReadU32(out uint declaredFileLength);
            headerCursor.TryReadU16(out ushort tableCount);
            headerCursor.TryReadU16(out _ /* reserved */);
            headerCursor.TryReadU32(out _ /* totalSfntSize: untrusted, recomputed (woff2_dec.cc) */);
            headerCursor.TryReadU32(out uint totalCompressedByteCount);
            headerCursor.TryReadU16(out _); headerCursor.TryReadU16(out _); // major/minor version
            headerCursor.TryReadU32(out uint metadataBlockOffset);
            headerCursor.TryReadU32(out uint metadataBlockByteCount);
            headerCursor.TryReadU32(out _ /* metaOrigLength: display-only, unused here */);
            headerCursor.TryReadU32(out uint privateBlockOffset);
            headerCursor.TryReadU32(out uint privateBlockByteCount);

            if (declaredFileLength != (uint)woff2FileBytes.Length || tableCount == 0)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                    $"declared length {declaredFileLength} vs actual {woff2FileBytes.Length}, tableCount {tableCount}");
                return false;
            }
            // Metadata/private block extents must lie within the file (woff2_dec.cc
            // ReadWOFF2Header; offset 0 means the block is absent).
            if ((metadataBlockOffset != 0
                    && ((long)metadataBlockOffset >= woff2FileBytes.Length
                        || woff2FileBytes.Length - metadataBlockOffset < metadataBlockByteCount))
                || (privateBlockOffset != 0
                    && ((long)privateBlockOffset >= woff2FileBytes.Length
                        || woff2FileBytes.Length - privateBlockOffset < privateBlockByteCount)))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2BlockLayoutInvalid,
                    $"metadata [{metadataBlockOffset}, +{metadataBlockByteCount}) or private [{privateBlockOffset}, +{privateBlockByteCount}) outside file length {woff2FileBytes.Length}");
                return false;
            }

            // ── Table directory (WOFF2 §4.2): flag byte, optional tag, varint lengths ──
            var tableRecords = new Woff2TableRecord[tableCount];
            uint runningSourceOffset = 0;
            for (int tableIndex = 0; tableIndex < tableCount; tableIndex++)
            {
                if (!headerCursor.TryReadU8(out byte directoryFlagByte))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                        $"directory truncated at table {tableIndex}");
                    return false;
                }
                uint tableTag;
                if ((directoryFlagByte & 0x3F) == 0x3F)
                {
                    if (!headerCursor.TryReadU32(out tableTag))
                    {
                        decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                            $"explicit tag truncated at table {tableIndex}");
                        return false;
                    }
                }
                else
                {
                    tableTag = Woff2KnownTableTags.KnownTableTags[directoryFlagByte & 0x3F];
                }

                int transformVersion = (directoryFlagByte >> 6) & 0x03;
                bool isGlyfOrLoca = tableTag == Woff2KnownTableTags.GlyfTableTag || tableTag == Woff2KnownTableTags.LocaTableTag;
                bool isTransformed = isGlyfOrLoca ? transformVersion == 0 : transformVersion != 0;

                if (!headerCursor.TryReadUIntBase128(out uint originalByteCount))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2VariableIntegerOverflow,
                        $"origLength UIntBase128 invalid at table {tableIndex}");
                    return false;
                }
                uint transformedByteCount = originalByteCount;
                if (isTransformed)
                {
                    if (!headerCursor.TryReadUIntBase128(out transformedByteCount))
                    {
                        decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2VariableIntegerOverflow,
                            $"transformLength UIntBase128 invalid at table {tableIndex}");
                        return false;
                    }
                    if (tableTag == Woff2KnownTableTags.LocaTableTag && transformedByteCount != 0)
                    {
                        decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TransformedLocaMustBeZeroLength,
                            $"transformed loca declares {transformedByteCount} bytes");
                        return false;
                    }
                }
                if (runningSourceOffset + transformedByteCount < runningSourceOffset)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                        $"source offset overflow at table {tableIndex}");
                    return false;
                }

                tableRecords[tableIndex] = new Woff2TableRecord
                {
                    TableTag = tableTag,
                    TransformVersion = transformVersion,
                    IsTransformed = isTransformed,
                    SourceOffset = runningSourceOffset,
                    SourceByteCount = transformedByteCount,
                    FinalByteCount = originalByteCount,
                };
                runningSourceOffset += transformedByteCount;
            }
            uint decompressedStreamByteCount = runningSourceOffset;

            // ── Optional collection directory (WOFF2 ¤4.3) when flavor is 'ttcf' ──
            uint collectionHeaderVersion = 0;
            Woff2CollectionFontRecord[] collectionFontRecords = null;
            if (sfntFlavor == Woff2KnownTableTags.TtcFontFlavor)
            {
                if (!TryReadCollectionDirectory(ref headerCursor, tableRecords,
                    out collectionHeaderVersion, out collectionFontRecords, out decodeFailure))
                {
                    return false;
                }
            }

            // ── Brotli stream (single stream covering all tables, WOFF2 ¤1) ──
            long compressedStreamOffset = headerCursor.CurrentOffset;
            if (compressedStreamOffset + totalCompressedByteCount > woff2FileBytes.Length)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                    $"compressed stream [{compressedStreamOffset}, +{totalCompressedByteCount}) exceeds file length {woff2FileBytes.Length}");
                return false;
            }

            // ── Strict block layout (WOFF2 §3, woff2_dec.cc ReadWOFF2Header tail):
            // compressed data, metadata, and private blocks must be contiguous (allowing
            // only 4-byte padding between blocks) and together consume the whole file.
            // Rejects extraneous bytes between/after blocks and overlapping blocks
            // (WPT blocks-extraneous-data-001..008, blocks-overlap-001..003).
            long expectedNextBlockOffset = Align4(compressedStreamOffset + totalCompressedByteCount);
            if (metadataBlockOffset != 0)
            {
                if (metadataBlockOffset != expectedNextBlockOffset)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2BlockLayoutInvalid,
                        $"metadata block at {metadataBlockOffset}, expected {expectedNextBlockOffset}");
                    return false;
                }
                expectedNextBlockOffset = Align4((long)metadataBlockOffset + metadataBlockByteCount);
            }
            if (privateBlockOffset != 0)
            {
                if (privateBlockOffset != expectedNextBlockOffset)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2BlockLayoutInvalid,
                        $"private block at {privateBlockOffset}, expected {expectedNextBlockOffset}");
                    return false;
                }
                expectedNextBlockOffset = Align4((long)privateBlockOffset + privateBlockByteCount);
            }
            if (expectedNextBlockOffset != Align4(woff2FileBytes.Length))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2BlockLayoutInvalid,
                    $"blocks end at {expectedNextBlockOffset}, file length {woff2FileBytes.Length}");
                return false;
            }

            if (decompressedStreamByteCount > (uint)reconstructedSfntByteCountCap)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.ReconstructedSfntSizeExceedsCap,
                    $"decompressed stream {decompressedStreamByteCount} exceeds cap {reconstructedSfntByteCountCap}");
                return false;
            }
            byte[] decompressedStreamBytes = new byte[decompressedStreamByteCount];
            if (!webFontDecompressor.TryDecodeBrotliStream(woff2FileBytes, (int)compressedStreamOffset,
                (int)totalCompressedByteCount, decompressedStreamBytes, out decodeFailure))
            {
                return false;
            }

            // ── Reverse transforms (glyf+loca pair, then hmtx which needs glyf xMins) ──
            if (!TryReverseTableTransforms(tableRecords, decompressedStreamBytes, out short[] glyphXMinsFontUnits, out decodeFailure))
            {
                return false;
            }

            // ── Assemble output: offset table(s) + directories + tag-sorted table data ──
            return TryAssembleSfnt(tableRecords, collectionHeaderVersion, collectionFontRecords,
                sfntFlavor, decompressedStreamBytes, reconstructedSfntByteCountCap,
                out reconstructedSfntBytes, out decodeFailure);
        }

        private static bool TryReadCollectionDirectory(ref Woff2StreamCursor headerCursor,
            Woff2TableRecord[] tableRecords, out uint collectionHeaderVersion,
            out Woff2CollectionFontRecord[] collectionFontRecords, out WebFontContainerDecodeFailure decodeFailure)
        {
            collectionFontRecords = null;
            if (!headerCursor.TryReadU32(out collectionHeaderVersion)
                || (collectionHeaderVersion != 0x00010000 && collectionHeaderVersion != 0x00020000)
                || !headerCursor.TryRead255UInt16(out int collectionFontCount)
                || collectionFontCount == 0)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CollectionDirectoryInvalid,
                    "collection header version/font count invalid");
                return false;
            }
            collectionFontRecords = new Woff2CollectionFontRecord[collectionFontCount];
            for (int fontIndex = 0; fontIndex < collectionFontCount; fontIndex++)
            {
                if (!headerCursor.TryRead255UInt16(out int fontTableCount) || fontTableCount == 0
                    || !headerCursor.TryReadU32(out uint fontFlavor))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CollectionDirectoryInvalid,
                        $"collection font {fontIndex} header invalid");
                    return false;
                }
                int[] fontTableIndices = new int[fontTableCount];
                int glyfDirectoryIndex = -1;
                int locaDirectoryIndex = -1;
                for (int tableSlot = 0; tableSlot < fontTableCount; tableSlot++)
                {
                    if (!headerCursor.TryRead255UInt16(out int tableRecordIndex) || tableRecordIndex >= tableRecords.Length)
                    {
                        decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CollectionDirectoryInvalid,
                            $"collection font {fontIndex} table index invalid");
                        return false;
                    }
                    fontTableIndices[tableSlot] = tableRecordIndex;
                    uint slotTableTag = tableRecords[tableRecordIndex].TableTag;
                    if (slotTableTag == Woff2KnownTableTags.GlyfTableTag) glyfDirectoryIndex = tableRecordIndex;
                    else if (slotTableTag == Woff2KnownTableTags.LocaTableTag) locaDirectoryIndex = tableRecordIndex;
                }
                // Each collection font's glyf/loca must be a CONSECUTIVE directory pair
                // (woff2_dec.cc ReadWOFF2Header: "TTC font has non-consecutive glyf/loca").
                // Rejects TTCs pairing mismatched glyf/loca tables
                // (WPT directory-mismatched-tables-001).
                if ((glyfDirectoryIndex >= 0 || locaDirectoryIndex >= 0)
                    && locaDirectoryIndex != glyfDirectoryIndex + 1)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2CollectionDirectoryInvalid,
                        $"collection font {fontIndex} glyf index {glyfDirectoryIndex} / loca index {locaDirectoryIndex} not consecutive");
                    return false;
                }
                collectionFontRecords[fontIndex] = new Woff2CollectionFontRecord
                {
                    SfntFlavor = fontFlavor,
                    TableRecordIndices = fontTableIndices,
                };
            }
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        /// <summary>
        /// Reverses the glyf/loca transform (via Woff2TransformedGlyfReconstructor) and the
        /// hmtx transform (WOFF2 ¤5.4, verified against woff2_dec.cc ReconstructTransformedHmtx).
        /// glyf sorts before hmtx which sorts before loca by tag, but we process explicitly
        /// by tag lookup so stream order doesn't matter.
        /// </summary>
        private static bool TryReverseTableTransforms(Woff2TableRecord[] tableRecords,
            byte[] decompressedStreamBytes, out short[] glyphXMinsFontUnits,
            out WebFontContainerDecodeFailure decodeFailure)
        {
            glyphXMinsFontUnits = null;
            Woff2TableRecord glyfRecord = FindTableRecord(tableRecords, Woff2KnownTableTags.GlyfTableTag);
            Woff2TableRecord locaRecord = FindTableRecord(tableRecords, Woff2KnownTableTags.LocaTableTag);

            // glyf without loca (or vice versa) is invalid (woff2_dec.cc ReconstructFont:
            // "Cannot have just one of glyf/loca").
            if ((glyfRecord != null) != (locaRecord != null))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                    "font has just one of glyf/loca");
                return false;
            }
            // Both must share the same transform state (woff2_dec.cc: "Cannot transform
            // just one of glyf/loca"). Catches e.g. glyf flagged null-transform (version 3)
            // while loca is still transformed (WPT tabledata-transform-bad-flag-002).
            if (glyfRecord != null && glyfRecord.IsTransformed != locaRecord.IsTransformed)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                    $"glyf transformed={glyfRecord.IsTransformed} but loca transformed={locaRecord.IsTransformed}");
                return false;
            }

            if (glyfRecord != null && glyfRecord.IsTransformed)
            {
                if (locaRecord == null || !locaRecord.IsTransformed)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                        "transformed glyf requires transformed loca");
                    return false;
                }
                if (!ValidateSourceSlice(glyfRecord, decompressedStreamBytes, out decodeFailure))
                {
                    return false;
                }
                if (!Woff2TransformedGlyfReconstructor.TryReconstruct(decompressedStreamBytes,
                    (int)glyfRecord.SourceOffset, (int)glyfRecord.SourceByteCount,
                    locaRecord.FinalByteCount,
                    out byte[] reconstructedGlyfBytes, out byte[] reconstructedLocaBytes,
                    out glyphXMinsFontUnits, out decodeFailure))
                {
                    return false;
                }
                // WOFF2 §4.3 conform-glyf-origLength (symmetric to conform-mustRejectLoca):
                // the declared glyf origLength must match the reconstructed table size,
                // tolerating ONLY the final glyph record's 4-byte alignment padding
                // (0-3 bytes; the reconstructor pads like woff2_dec.cc Pad4, but the
                // original glyf need not have been padded). Rejects WPT
                // tabledata-glyf-origlength-001/002/003 while accepting valid fonts.
                long glyfPaddingByteCount = (long)reconstructedGlyfBytes.Length - glyfRecord.FinalByteCount;
                if (glyfPaddingByteCount < 0 || glyfPaddingByteCount > 3)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2GlyfOrigLengthMismatch,
                        $"glyf origLength {glyfRecord.FinalByteCount}, reconstructed {reconstructedGlyfBytes.Length}");
                    return false;
                }
                glyfRecord.ReconstructedTableBytes = reconstructedGlyfBytes;
                glyfRecord.FinalByteCount = (uint)reconstructedGlyfBytes.Length;
                glyfRecord.HasBeenReconstructed = true;
                locaRecord.ReconstructedTableBytes = reconstructedLocaBytes;
                locaRecord.FinalByteCount = (uint)reconstructedLocaBytes.Length;
                locaRecord.HasBeenReconstructed = true;
            }

            Woff2TableRecord hmtxRecord = FindTableRecord(tableRecords, Woff2KnownTableTags.HmtxTableTag);
            if (hmtxRecord != null && hmtxRecord.IsTransformed)
            {
                if (!ValidateSourceSlice(hmtxRecord, decompressedStreamBytes, out decodeFailure))
                {
                    return false;
                }
                if (!TryReconstructTransformedHmtx(tableRecords, hmtxRecord, decompressedStreamBytes,
                    glyphXMinsFontUnits, out decodeFailure))
                {
                    return false;
                }
            }

            // Any OTHER transformed table is unknown (woff2_dec.cc: "transform unknown").
            foreach (Woff2TableRecord tableRecord in tableRecords)
            {
                if (tableRecord.IsTransformed && !tableRecord.HasBeenReconstructed)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                        $"unknown transform on table 0x{tableRecord.TableTag:X8} version {tableRecord.TransformVersion}");
                    return false;
                }
                if (!tableRecord.HasBeenReconstructed
                    && !ValidateSourceSlice(tableRecord, decompressedStreamBytes, out decodeFailure))
                {
                    return false;
                }
            }
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        /// <summary>WOFF2 ¤5.4 hmtx transform reversal: flags byte, advances always present,
        /// proportional/monospace lsb arrays elided when the corresponding flag bit is SET
        /// (elided lsbs come from glyf xMins).</summary>
        private static bool TryReconstructTransformedHmtx(Woff2TableRecord[] tableRecords,
            Woff2TableRecord hmtxRecord, byte[] decompressedStreamBytes, short[] glyphXMinsFontUnits,
            out WebFontContainerDecodeFailure decodeFailure)
        {
            // numGlyphs from maxp (offset 4), numHMetrics from hhea (offset 34) — read from
            // their (untransformed) source slices, same data woff2_dec.cc gathers.
            if (!TryReadU16FromTableSource(tableRecords, decompressedStreamBytes, 0x6D617870 /* maxp */, 4, out int glyphCount)
                || !TryReadU16FromTableSource(tableRecords, decompressedStreamBytes, 0x68686561 /* hhea */, 34, out int hMetricCount))
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2HmtxReconstructionInvalid,
                    "transformed hmtx requires readable maxp and hhea");
                return false;
            }
            if (glyphXMinsFontUnits == null || glyphXMinsFontUnits.Length != glyphCount)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2HmtxReconstructionInvalid,
                    "transformed hmtx requires transformed glyf xMin data");
                return false;
            }
            if (hMetricCount < 1 || hMetricCount > glyphCount)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2HmtxReconstructionInvalid,
                    $"numHMetrics {hMetricCount} vs numGlyphs {glyphCount}");
                return false;
            }

            var hmtxCursor = new Woff2StreamCursor(decompressedStreamBytes,
                (int)hmtxRecord.SourceOffset, (int)(hmtxRecord.SourceOffset + hmtxRecord.SourceByteCount));
            if (!hmtxCursor.TryReadU8(out byte hmtxFlags) || (hmtxFlags & 0xFC) != 0 || hmtxFlags == 0)
            {
                // Bits 2-7 reserved; flags==0 means nothing was elided — "transformed" with
                // no transform is invalid (woff2_dec.cc).
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2HmtxReconstructionInvalid,
                    $"hmtx transform flags 0x{hmtxFlags:X2} invalid");
                return false;
            }
            bool proportionalLsbsElided = (hmtxFlags & 1) != 0;
            bool monospaceLsbsElided = (hmtxFlags & 2) != 0;

            var advanceWidths = new ushort[hMetricCount];
            var leftSideBearings = new short[glyphCount];
            for (int metricIndex = 0; metricIndex < hMetricCount; metricIndex++)
            {
                if (!hmtxCursor.TryReadU16(out advanceWidths[metricIndex]))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2HmtxReconstructionInvalid,
                        "advance stream truncated");
                    return false;
                }
            }
            for (int glyphIndex = 0; glyphIndex < glyphCount; glyphIndex++)
            {
                bool lsbElided = glyphIndex < hMetricCount ? proportionalLsbsElided : monospaceLsbsElided;
                if (lsbElided)
                {
                    leftSideBearings[glyphIndex] = glyphXMinsFontUnits[glyphIndex];
                }
                else if (!hmtxCursor.TryReadS16(out leftSideBearings[glyphIndex]))
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2HmtxReconstructionInvalid,
                        "lsb stream truncated");
                    return false;
                }
            }

            byte[] hmtxTableBytes = new byte[2 * glyphCount + 2 * hMetricCount];
            int writeOffset = 0;
            for (int glyphIndex = 0; glyphIndex < glyphCount; glyphIndex++)
            {
                if (glyphIndex < hMetricCount)
                {
                    WriteU16(hmtxTableBytes, writeOffset, advanceWidths[glyphIndex]);
                    writeOffset += 2;
                }
                WriteU16(hmtxTableBytes, writeOffset, (ushort)leftSideBearings[glyphIndex]);
                writeOffset += 2;
            }
            hmtxRecord.ReconstructedTableBytes = hmtxTableBytes;
            hmtxRecord.FinalByteCount = (uint)hmtxTableBytes.Length;
            hmtxRecord.HasBeenReconstructed = true;
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        private static bool TryAssembleSfnt(Woff2TableRecord[] tableRecords,
            uint collectionHeaderVersion, Woff2CollectionFontRecord[] collectionFontRecords,
            uint sfntFlavor, byte[] decompressedStreamBytes, int reconstructedSfntByteCountCap,
            out byte[] reconstructedSfntBytes, out WebFontContainerDecodeFailure decodeFailure)
        {
            reconstructedSfntBytes = null;
            bool isCollection = collectionFontRecords != null;

            // Header region size: single font = offset table + directory; collection =
            // TTC header + per-font offset tables + per-font directories (woff2_dec.cc
            // ComputeOffsetToFirstTable / CollectionHeaderSize).
            long headerRegionByteCount;
            if (!isCollection)
            {
                headerRegionByteCount = SfntHeaderByteCount + (long)tableRecords.Length * SfntTableDirectoryEntryByteCount;
            }
            else
            {
                headerRegionByteCount = collectionHeaderVersion == 0x00020000 ? 12 + 12 : 12; // ttcf header (+v2 DSIG fields)
                headerRegionByteCount += 4L * collectionFontRecords.Length; // offset-table offsets
                foreach (Woff2CollectionFontRecord fontRecord in collectionFontRecords)
                {
                    headerRegionByteCount += SfntHeaderByteCount + (long)fontRecord.TableRecordIndices.Length * SfntTableDirectoryEntryByteCount;
                }
            }

            // Total size: header region + 4-aligned tables (unique tables written once).
            long totalByteCount = headerRegionByteCount;
            foreach (Woff2TableRecord tableRecord in tableRecords)
            {
                totalByteCount = Align4(totalByteCount) + tableRecord.FinalByteCount;
                if (totalByteCount > reconstructedSfntByteCountCap)
                {
                    decodeFailure = Failure(WebFontContainerDecodeFailureCode.ReconstructedSfntSizeExceedsCap,
                        $"running sfnt size {totalByteCount} exceeds cap {reconstructedSfntByteCountCap}");
                    return false;
                }
            }
            totalByteCount = Align4(totalByteCount);

            byte[] sfntBytes = new byte[totalByteCount];

            // ── Write table data (input order preserves glyf-before-loca adjacency), compute checksums ──
            long nextTableWriteOffset = headerRegionByteCount;
            foreach (Woff2TableRecord tableRecord in tableRecords)
            {
                nextTableWriteOffset = Align4(nextTableWriteOffset);
                tableRecord.FinalOffset = (uint)nextTableWriteOffset;
                if (tableRecord.HasBeenReconstructed)
                {
                    Array.Copy(tableRecord.ReconstructedTableBytes, 0, sfntBytes, nextTableWriteOffset, tableRecord.ReconstructedTableBytes.Length);
                }
                else
                {
                    Array.Copy(decompressedStreamBytes, (int)tableRecord.SourceOffset, sfntBytes, nextTableWriteOffset, (int)tableRecord.SourceByteCount);
                }
                if (tableRecord.TableTag == Woff2KnownTableTags.HeadTableTag && tableRecord.FinalByteCount >= 12)
                {
                    WriteU32(sfntBytes, (int)(tableRecord.FinalOffset + 8), 0); // zero checkSumAdjustment before summing
                }
                tableRecord.TableChecksum = ComputeTableChecksum(sfntBytes, (int)tableRecord.FinalOffset, (int)tableRecord.FinalByteCount);
                nextTableWriteOffset += tableRecord.FinalByteCount;
            }

            // ── Write header region + per-font checkSumAdjustment ──
            if (!isCollection)
            {
                WriteFontHeaderAndDirectory(sfntBytes, 0, sfntFlavor, tableRecords, GetSortedByTag(tableRecords));
                RecomputeCheckSumAdjustmentForFont(sfntBytes, 0, (int)headerRegionByteCount, tableRecords, GetSortedByTag(tableRecords));
            }
            else
            {
                WriteU32(sfntBytes, 0, Woff2KnownTableTags.TtcFontFlavor);
                WriteU32(sfntBytes, 4, collectionHeaderVersion);
                WriteU32(sfntBytes, 8, (uint)collectionFontRecords.Length);
                int offsetTableSlotOffset = 12;
                int nextFontHeaderOffset = 12 + 4 * collectionFontRecords.Length
                    + (collectionHeaderVersion == 0x00020000 ? 12 : 0);
                int dsigFieldsOffset = 12 + 4 * collectionFontRecords.Length;
                if (collectionHeaderVersion == 0x00020000)
                {
                    WriteU32(sfntBytes, dsigFieldsOffset, 0);     // dsigTag: none
                    WriteU32(sfntBytes, dsigFieldsOffset + 4, 0); // dsigLength
                    WriteU32(sfntBytes, dsigFieldsOffset + 8, 0); // dsigOffset
                }
                foreach (Woff2CollectionFontRecord fontRecord in collectionFontRecords)
                {
                    WriteU32(sfntBytes, offsetTableSlotOffset, (uint)nextFontHeaderOffset);
                    offsetTableSlotOffset += 4;
                    var fontTableRecords = new Woff2TableRecord[fontRecord.TableRecordIndices.Length];
                    for (int tableSlot = 0; tableSlot < fontTableRecords.Length; tableSlot++)
                    {
                        fontTableRecords[tableSlot] = tableRecords[fontRecord.TableRecordIndices[tableSlot]];
                    }
                    Woff2TableRecord[] sortedFontTableRecords = GetSortedByTag(fontTableRecords);
                    int fontHeaderByteCount = SfntHeaderByteCount + fontTableRecords.Length * SfntTableDirectoryEntryByteCount;
                    WriteFontHeaderAndDirectory(sfntBytes, nextFontHeaderOffset, fontRecord.SfntFlavor, fontTableRecords, sortedFontTableRecords);
                    RecomputeCheckSumAdjustmentForFont(sfntBytes, nextFontHeaderOffset, fontHeaderByteCount, fontTableRecords, sortedFontTableRecords);
                    nextFontHeaderOffset += fontHeaderByteCount;
                }
            }

            reconstructedSfntBytes = sfntBytes;
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        private static void WriteFontHeaderAndDirectory(byte[] sfntBytes, int headerOffset,
            uint sfntFlavor, Woff2TableRecord[] fontTableRecords, Woff2TableRecord[] sortedFontTableRecords)
        {
            WriteU32(sfntBytes, headerOffset, sfntFlavor);
            WriteU16(sfntBytes, headerOffset + 4, (ushort)fontTableRecords.Length);
            int largestPowerOfTwo = 1, log2 = 0;
            while (largestPowerOfTwo * 2 <= fontTableRecords.Length) { largestPowerOfTwo *= 2; log2++; }
            WriteU16(sfntBytes, headerOffset + 6, (ushort)(largestPowerOfTwo * 16));
            WriteU16(sfntBytes, headerOffset + 8, (ushort)log2);
            WriteU16(sfntBytes, headerOffset + 10, (ushort)(fontTableRecords.Length * 16 - largestPowerOfTwo * 16));
            for (int sortedIndex = 0; sortedIndex < sortedFontTableRecords.Length; sortedIndex++)
            {
                Woff2TableRecord tableRecord = sortedFontTableRecords[sortedIndex];
                int entryOffset = headerOffset + SfntHeaderByteCount + sortedIndex * SfntTableDirectoryEntryByteCount;
                WriteU32(sfntBytes, entryOffset, tableRecord.TableTag);
                WriteU32(sfntBytes, entryOffset + 4, tableRecord.TableChecksum);
                WriteU32(sfntBytes, entryOffset + 8, tableRecord.FinalOffset);
                WriteU32(sfntBytes, entryOffset + 12, tableRecord.FinalByteCount);
            }
        }

        /// <summary>
        /// head.checkSumAdjustment per font: 0xB1B0AFBA minus (header/directory region
        /// checksum + sum of table checksums), matching woff2_dec.cc's font_checksum
        /// accounting. The head table's own field was zeroed before its checksum.
        /// </summary>
        private static void RecomputeCheckSumAdjustmentForFont(byte[] sfntBytes, int headerOffset,
            int headerRegionByteCount, Woff2TableRecord[] fontTableRecords, Woff2TableRecord[] sortedFontTableRecords)
        {
            Woff2TableRecord headRecord = null;
            uint fontChecksum = ComputeTableChecksum(sfntBytes, headerOffset, headerRegionByteCount);
            foreach (Woff2TableRecord tableRecord in sortedFontTableRecords)
            {
                unchecked { fontChecksum += tableRecord.TableChecksum; }
                if (tableRecord.TableTag == Woff2KnownTableTags.HeadTableTag && tableRecord.FinalByteCount >= 12)
                {
                    headRecord = tableRecord;
                }
            }
            if (headRecord != null)
            {
                WriteU32(sfntBytes, (int)(headRecord.FinalOffset + 8), unchecked(CheckSumAdjustmentMagic - fontChecksum));
            }
        }

        private static Woff2TableRecord[] GetSortedByTag(Woff2TableRecord[] tableRecords)
        {
            var sortedRecords = (Woff2TableRecord[])tableRecords.Clone();
            Array.Sort(sortedRecords, (left, right) => left.TableTag.CompareTo(right.TableTag));
            return sortedRecords;
        }

        private static Woff2TableRecord FindTableRecord(Woff2TableRecord[] tableRecords, uint tableTag)
        {
            foreach (Woff2TableRecord tableRecord in tableRecords)
            {
                if (tableRecord.TableTag == tableTag) return tableRecord;
            }
            return null;
        }

        private static bool TryReadU16FromTableSource(Woff2TableRecord[] tableRecords,
            byte[] decompressedStreamBytes, uint tableTag, int fieldOffset, out int fieldValue)
        {
            fieldValue = 0;
            Woff2TableRecord tableRecord = FindTableRecord(tableRecords, tableTag);
            if (tableRecord == null || tableRecord.IsTransformed
                || tableRecord.SourceByteCount < (uint)(fieldOffset + 2)
                || tableRecord.SourceOffset + tableRecord.SourceByteCount > (uint)decompressedStreamBytes.Length)
            {
                return false;
            }
            int absoluteOffset = (int)(tableRecord.SourceOffset + fieldOffset);
            fieldValue = (decompressedStreamBytes[absoluteOffset] << 8) | decompressedStreamBytes[absoluteOffset + 1];
            return true;
        }

        private static bool ValidateSourceSlice(Woff2TableRecord tableRecord,
            byte[] decompressedStreamBytes, out WebFontContainerDecodeFailure decodeFailure)
        {
            long sliceEnd = (long)tableRecord.SourceOffset + tableRecord.SourceByteCount;
            if (sliceEnd > decompressedStreamBytes.Length)
            {
                decodeFailure = Failure(WebFontContainerDecodeFailureCode.Woff2TableDirectoryInvalid,
                    $"table 0x{tableRecord.TableTag:X8} source slice exceeds decompressed stream");
                return false;
            }
            decodeFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        /// <summary>Standard sfnt checksum: big-endian u32 sum with implicit zero padding.</summary>
        private static uint ComputeTableChecksum(byte[] sfntBytes, int tableOffset, int tableByteCount)
        {
            uint checksum = 0;
            for (int wordOffset = 0; wordOffset < tableByteCount; wordOffset += 4)
            {
                uint wordValue = 0;
                for (int byteIndex = 0; byteIndex < 4; byteIndex++)
                {
                    int byteOffset = tableOffset + wordOffset + byteIndex;
                    wordValue = (wordValue << 8) | (wordOffset + byteIndex < tableByteCount ? sfntBytes[byteOffset] : (byte)0);
                }
                unchecked { checksum += wordValue; }
            }
            return checksum;
        }

        private static long Align4(long value) => (value + 3) & ~3L;

        private static WebFontContainerDecodeFailure Failure(WebFontContainerDecodeFailureCode failureCode, string failureDetail)
            => new WebFontContainerDecodeFailure(failureCode, failureDetail);

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