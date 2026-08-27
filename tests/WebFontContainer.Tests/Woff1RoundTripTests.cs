using System;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.WebFontContainer;
using Xunit;

namespace WebFontContainerTests
{
    /// <summary>
    /// Phase 1 tests (plans/woff_web_fonts.md): WOFF1 container decode round-trip
    /// against the fontTools-generated twin of Roboto-Regular.ttf, plus sniffing
    /// and pass-through behavior.
    /// </summary>
    public class Woff1RoundTripTests
    {
        private static byte[] ReadTestFileBytes(string testFileName)
            => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, testFileName));

        [Fact]
        public void SniffsWoff1TtfAndGarbageCorrectly()
        {
            Assert.Equal(WebFontContainerFormat.Woff1,
                WebFontContainerDecoder.SniffContainerFormat(ReadTestFileBytes("Roboto-Regular.generated.woff")));
            Assert.Equal(WebFontContainerFormat.NotAWebFontContainer,
                WebFontContainerDecoder.SniffContainerFormat(ReadTestFileBytes("Roboto-Regular.ttf")));
            Assert.Equal(WebFontContainerFormat.NotAWebFontContainer,
                WebFontContainerDecoder.SniffContainerFormat(new byte[] { 1, 2, 3 }));
            Assert.Equal(WebFontContainerFormat.NotAWebFontContainer,
                WebFontContainerDecoder.SniffContainerFormat(null));
        }

        [Fact]
        public void PlainSfntBytesPassThroughUnchangedAndZeroCopy()
        {
            byte[] trueTypeFontFileBytes = ReadTestFileBytes("Roboto-Regular.ttf");
            Assert.True(WebFontContainerDecoder.TryDecodeToSfnt(trueTypeFontFileBytes,
                out SfntFontFileBytes sfntFontFileBytes, out WebFontContainerDecodeFailure decodeFailure),
                decodeFailure.ToString());
            Assert.Same(trueTypeFontFileBytes, sfntFontFileBytes.FontFileBytes); // zero-copy pass-through
        }

        [Fact]
        public void Woff1DecodesAndEveryTableIsByteIdenticalExceptHead()
        {
            byte[] originalTrueTypeBytes = ReadTestFileBytes("Roboto-Regular.ttf");
            byte[] woffContainerBytes = ReadTestFileBytes("Roboto-Regular.generated.woff");

            Assert.True(WebFontContainerDecoder.TryDecodeToSfnt(woffContainerBytes,
                out SfntFontFileBytes reconstructedSfnt, out WebFontContainerDecodeFailure decodeFailure),
                decodeFailure.ToString());
            byte[] reconstructedSfntBytes = reconstructedSfnt.FontFileBytes;

            SfntTableDirectory originalDirectory = SfntTableDirectory.Parse(originalTrueTypeBytes);
            SfntTableDirectory reconstructedDirectory = SfntTableDirectory.Parse(reconstructedSfntBytes);
            Assert.Equal(originalDirectory.TableCount, reconstructedDirectory.TableCount);

            for (int tableIndex = 0; tableIndex < originalDirectory.TableCount; tableIndex++)
            {
                uint tableTag = originalDirectory.TableTags[tableIndex];
                int reconstructedIndex = Array.IndexOf(reconstructedDirectory.TableTags, tableTag);
                Assert.True(reconstructedIndex >= 0, $"table 0x{tableTag:X8} missing from reconstruction");

                byte[] originalTableBytes = originalDirectory.CopyTableBytes(originalTrueTypeBytes, tableIndex);
                byte[] reconstructedTableBytes = reconstructedDirectory.CopyTableBytes(reconstructedSfntBytes, reconstructedIndex);

                if (tableTag == 0x68656164) // 'head': checkSumAdjustment (bytes 8-11) is recomputed
                {
                    Assert.Equal(originalTableBytes.Length, reconstructedTableBytes.Length);
                    for (int byteIndex = 0; byteIndex < originalTableBytes.Length; byteIndex++)
                    {
                        if (byteIndex >= 8 && byteIndex < 12) continue;
                        Assert.True(originalTableBytes[byteIndex] == reconstructedTableBytes[byteIndex],
                            $"head byte {byteIndex} differs");
                    }
                }
                else
                {
                    Assert.Equal(originalTableBytes, reconstructedTableBytes);
                }
            }
        }

        [Fact]
        public void ReconstructedSfntParsesInStbAndMatchesOriginalMetricsAndOutlines()
        {
            byte[] originalTrueTypeBytes = ReadTestFileBytes("Roboto-Regular.ttf");
            byte[] woffContainerBytes = ReadTestFileBytes("Roboto-Regular.generated.woff");
            Assert.True(WebFontContainerDecoder.TryDecodeToSfnt(woffContainerBytes,
                out SfntFontFileBytes reconstructedSfnt, out WebFontContainerDecodeFailure decodeFailure),
                decodeFailure.ToString());

            var originalFontInfo = new FontInfo();
            var reconstructedFontInfo = new FontInfo();
            Assert.Equal(1, originalFontInfo.stbtt_InitFont(originalTrueTypeBytes, 0));
            Assert.Equal(1, reconstructedFontInfo.stbtt_InitFont(reconstructedSfnt.FontFileBytes, 0));

            originalFontInfo.stbtt_GetFontVMetrics(out int originalAscent, out int originalDescent, out int originalLineGap);
            reconstructedFontInfo.stbtt_GetFontVMetrics(out int reconstructedAscent, out int reconstructedDescent, out int reconstructedLineGap);
            Assert.Equal(originalAscent, reconstructedAscent);
            Assert.Equal(originalDescent, reconstructedDescent);
            Assert.Equal(originalLineGap, reconstructedLineGap);

            // Probe a spread of codepoints: metrics, box, and outline vertex identity.
            foreach (char probeCharacter in "AgHxQé@0.,~")
            {
                int originalGlyphIndex = originalFontInfo.stbtt_FindGlyphIndex(probeCharacter);
                int reconstructedGlyphIndex = reconstructedFontInfo.stbtt_FindGlyphIndex(probeCharacter);
                Assert.Equal(originalGlyphIndex, reconstructedGlyphIndex);
                if (originalGlyphIndex == 0) continue;

                int originalAdvance = 0, originalLsb = 0, reconstructedAdvance = 0, reconstructedLsb = 0;
                originalFontInfo.stbtt_GetGlyphHMetrics(originalGlyphIndex, ref originalAdvance, ref originalLsb);
                reconstructedFontInfo.stbtt_GetGlyphHMetrics(reconstructedGlyphIndex, ref reconstructedAdvance, ref reconstructedLsb);
                Assert.Equal(originalAdvance, reconstructedAdvance);
                Assert.Equal(originalLsb, reconstructedLsb);

                int originalVertexCount = originalFontInfo.stbtt_GetGlyphShape(originalGlyphIndex, out var originalVertices);
                int reconstructedVertexCount = reconstructedFontInfo.stbtt_GetGlyphShape(reconstructedGlyphIndex, out var reconstructedVertices);
                Assert.Equal(originalVertexCount, reconstructedVertexCount);
                for (int vertexIndex = 0; vertexIndex < originalVertexCount; vertexIndex++)
                {
                    Assert.Equal(originalVertices[vertexIndex].type, reconstructedVertices[vertexIndex].type);
                    Assert.Equal(originalVertices[vertexIndex].x, reconstructedVertices[vertexIndex].x);
                    Assert.Equal(originalVertices[vertexIndex].y, reconstructedVertices[vertexIndex].y);
                }
            }
        }

        /// <summary>Minimal sfnt table-directory reader for byte-identity comparison in tests.</summary>
        private sealed class SfntTableDirectory
        {
            public int TableCount = 0;
            public uint[] TableTags = Array.Empty<uint>();
            public uint[] TableOffsets = Array.Empty<uint>();
            public uint[] TableByteCounts = Array.Empty<uint>();

            public static SfntTableDirectory Parse(byte[] sfntBytes)
            {
                var directory = new SfntTableDirectory();
                directory.TableCount = (sfntBytes[4] << 8) | sfntBytes[5];
                directory.TableTags = new uint[directory.TableCount];
                directory.TableOffsets = new uint[directory.TableCount];
                directory.TableByteCounts = new uint[directory.TableCount];
                for (int tableIndex = 0; tableIndex < directory.TableCount; tableIndex++)
                {
                    int entryOffset = 12 + tableIndex * 16;
                    directory.TableTags[tableIndex] = ReadU32(sfntBytes, entryOffset);
                    directory.TableOffsets[tableIndex] = ReadU32(sfntBytes, entryOffset + 8);
                    directory.TableByteCounts[tableIndex] = ReadU32(sfntBytes, entryOffset + 12);
                }
                return directory;
            }

            public byte[] CopyTableBytes(byte[] sfntBytes, int tableIndex)
            {
                byte[] tableBytes = new byte[TableByteCounts[tableIndex]];
                Array.Copy(sfntBytes, TableOffsets[tableIndex], tableBytes, 0, tableBytes.Length);
                return tableBytes;
            }

            private static uint ReadU32(byte[] bytes, int offset)
                => ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }
}