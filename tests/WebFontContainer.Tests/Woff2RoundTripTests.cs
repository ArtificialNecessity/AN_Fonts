using System;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.WebFontContainer;
using Xunit;

namespace WebFontContainerTests
{
    /// <summary>
    /// Phase 2/3 tests (plans/woff_web_fonts.md): WOFF2 decode with TRANSFORMED
    /// glyf/loca (fontTools default encoding) must reconstruct a font whose parsed
    /// metrics and outlines are identical to the original TTF. Outline identity
    /// through stb is the strongest practical check: triplet decoding, bbox handling,
    /// endPtsOfContours, instructions, and loca regeneration all feed it.
    /// </summary>
    public class Woff2RoundTripTests
    {
        private static byte[] ReadTestFileBytes(string testFileName)
            => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, testFileName));

        [Fact]
        public void SniffsWoff2Correctly()
        {
            Assert.Equal(WebFontContainerFormat.Woff2,
                WebFontContainerDecoder.SniffContainerFormat(ReadTestFileBytes("Roboto-Regular.generated.woff2")));
        }

        [Fact]
        public void Woff2WithTransformedGlyfDecodesAndParsesInStb()
        {
            byte[] woff2ContainerBytes = ReadTestFileBytes("Roboto-Regular.generated.woff2");
            Assert.True(WebFontContainerDecoder.TryDecodeToSfnt(woff2ContainerBytes,
                out SfntFontFileBytes reconstructedSfnt, out WebFontContainerDecodeFailure decodeFailure),
                decodeFailure.ToString());

            var reconstructedFontInfo = new FontInfo();
            Assert.Equal(1, reconstructedFontInfo.stbtt_InitFont(reconstructedSfnt.FontFileBytes, 0));
        }

        [Fact]
        public void Woff2ReconstructionMatchesOriginalMetricsAndOutlinesForEveryGlyph()
        {
            byte[] originalTrueTypeBytes = ReadTestFileBytes("Roboto-Regular.ttf");
            byte[] woff2ContainerBytes = ReadTestFileBytes("Roboto-Regular.generated.woff2");
            Assert.True(WebFontContainerDecoder.TryDecodeToSfnt(woff2ContainerBytes,
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

            // EVERY glyph in the font: hmetrics, box, and full outline vertex identity.
            // This sweeps simple glyphs, composites, empty glyphs, and instruction-carrying
            // glyphs through the reconstructed glyf/loca.
            int glyphCount = originalFontInfo.numGlyphs;
            Assert.Equal(glyphCount, reconstructedFontInfo.numGlyphs);
            Assert.True(glyphCount > 1000, $"expected a real glyph sweep, got {glyphCount}");
            for (int glyphIndex = 0; glyphIndex < glyphCount; glyphIndex++)
            {
                int originalAdvance = 0, originalLsb = 0, reconstructedAdvance = 0, reconstructedLsb = 0;
                originalFontInfo.stbtt_GetGlyphHMetrics(glyphIndex, ref originalAdvance, ref originalLsb);
                reconstructedFontInfo.stbtt_GetGlyphHMetrics(glyphIndex, ref reconstructedAdvance, ref reconstructedLsb);
                Assert.True(originalAdvance == reconstructedAdvance && originalLsb == reconstructedLsb,
                    $"glyph {glyphIndex}: hmetrics {originalAdvance}/{originalLsb} vs {reconstructedAdvance}/{reconstructedLsb}");

                int originalVertexCount = originalFontInfo.stbtt_GetGlyphShape(glyphIndex, out var originalVertices);
                int reconstructedVertexCount = reconstructedFontInfo.stbtt_GetGlyphShape(glyphIndex, out var reconstructedVertices);
                Assert.True(originalVertexCount == reconstructedVertexCount,
                    $"glyph {glyphIndex}: vertex count {originalVertexCount} vs {reconstructedVertexCount}");
                for (int vertexIndex = 0; vertexIndex < originalVertexCount; vertexIndex++)
                {
                    Assert.True(originalVertices[vertexIndex].type == reconstructedVertices[vertexIndex].type
                        && originalVertices[vertexIndex].x == reconstructedVertices[vertexIndex].x
                        && originalVertices[vertexIndex].y == reconstructedVertices[vertexIndex].y,
                        $"glyph {glyphIndex} vertex {vertexIndex} differs");
                }
            }
        }

        [Theory]
        [InlineData(4)]
        [InlineData(47)]
        public void TruncatedWoff2HeaderFails(int truncatedByteCount)
        {
            byte[] truncatedBytes = new byte[truncatedByteCount];
            Array.Copy(ReadTestFileBytes("Roboto-Regular.generated.woff2"), truncatedBytes, truncatedByteCount);
            Assert.False(WebFontContainerDecoder.TryDecodeToSfnt(truncatedBytes, out _,
                out WebFontContainerDecodeFailure decodeFailure));
            Assert.Equal(WebFontContainerDecodeFailureCode.TruncatedContainerHeader, decodeFailure.FailureCode);
        }

        [Fact]
        public void CorruptedBrotliStreamFailsWithoutThrowing()
        {
            byte[] mutatedBytes = ReadTestFileBytes("Roboto-Regular.generated.woff2");
            // Corrupt bytes deep inside the compressed stream (well past header/directory).
            int corruptionOffset = mutatedBytes.Length / 2;
            mutatedBytes[corruptionOffset] ^= 0xFF;
            mutatedBytes[corruptionOffset + 1] ^= 0xFF;
            // Brotli (RFC 7932) has NO integrity checksum, so corruption may either be
            // detected (stream structure broken) or silently decode to garbage bytes of
            // a plausible length. The contract under test is strictly FAIL-SOFT: the
            // decoder must never throw on hostile bytes; if it "succeeds", downstream
            // stb parsing operates on bounds-validated reconstructed tables.
            bool decodeSucceeded = WebFontContainerDecoder.TryDecodeToSfnt(mutatedBytes,
                out SfntFontFileBytes reconstructedSfnt, out WebFontContainerDecodeFailure decodeFailure);
            if (decodeSucceeded)
            {
                // Garbage-tolerant path: stb init on the garbled sfnt must also not throw.
                var fontInfo = new FontInfo();
                _ = fontInfo.stbtt_InitFont(reconstructedSfnt.FontFileBytes, 0);
            }
            else
            {
                Assert.NotEqual(WebFontContainerDecodeFailureCode.None, decodeFailure.FailureCode);
            }
        }

        [Fact]
        public void TruncatedCompressedStreamFailsWithoutThrowing()
        {
            byte[] validBytes = ReadTestFileBytes("Roboto-Regular.generated.woff2");
            byte[] truncatedBytes = new byte[validBytes.Length * 3 / 4];
            Array.Copy(validBytes, truncatedBytes, truncatedBytes.Length);
            // Fix declared length so we penetrate past the header check and exercise
            // the compressed-stream bounds validation.
            truncatedBytes[8] = (byte)((uint)truncatedBytes.Length >> 24);
            truncatedBytes[9] = (byte)((uint)truncatedBytes.Length >> 16);
            truncatedBytes[10] = (byte)((uint)truncatedBytes.Length >> 8);
            truncatedBytes[11] = (byte)truncatedBytes.Length;
            bool decodeSucceeded = WebFontContainerDecoder.TryDecodeToSfnt(truncatedBytes, out _,
                out WebFontContainerDecodeFailure decodeFailure);
            Assert.False(decodeSucceeded);
            Assert.NotEqual(WebFontContainerDecodeFailureCode.None, decodeFailure.FailureCode);
        }
    }
}