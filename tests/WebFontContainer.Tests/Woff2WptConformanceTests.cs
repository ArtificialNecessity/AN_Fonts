using System;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.WebFontContainer;
using Xunit;

namespace WebFontContainerTests
{
    /// <summary>
    /// Decoder-side WOFF2 spec-conformance verdicts against the W3C WPT css/WOFF2
    /// fixtures (AN_SilkyNvg/plans/WOFF_decoder_strictness.md §3/§4; provenance in
    /// WptFixtures/README.md). Every must-REJECT fixture must fail TryDecodeToSfnt
    /// fail-soft (false + failure code, never a throw); the must-ACCEPT fixtures must
    /// decode AND parse in stb. StrictWoff parsing mode is used so that
    /// header-signature-001 (magic 'XXXX') is a decoder verdict rather than an sfnt
    /// pass-through.
    /// </summary>
    public class Woff2WptConformanceTests
    {
        private static byte[] ReadFixtureBytes(string wptTestName)
            => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "WptFixtures", wptTestName + ".woff2"));

        [Theory]
        // §3.1 block layout — extraneous data between/after blocks
        [InlineData("blocks-extraneous-data-001")]
        [InlineData("blocks-extraneous-data-002")]
        [InlineData("blocks-extraneous-data-003")]
        [InlineData("blocks-extraneous-data-004")]
        [InlineData("blocks-extraneous-data-005")]
        [InlineData("blocks-extraneous-data-006")]
        [InlineData("blocks-extraneous-data-007")]
        [InlineData("blocks-extraneous-data-008")]
        // §3.2 block overlap
        [InlineData("blocks-overlap-001")]
        [InlineData("blocks-overlap-002")]
        [InlineData("blocks-overlap-003")]
        // §3.3 UIntBase128 violations
        [InlineData("datatypes-invalid-base128-001")]
        [InlineData("datatypes-invalid-base128-002")]
        [InlineData("datatypes-invalid-base128-003")]
        // §3.4 header violations
        [InlineData("header-signature-001")]
        [InlineData("header-length-001")]
        [InlineData("header-length-002")]
        [InlineData("header-numTables-001")]
        // §3.5 table directory
        [InlineData("directory-mismatched-tables-001")]
        // §3.6 compressed stream / decompressed length
        [InlineData("tabledata-brotli-001")]
        [InlineData("tabledata-decompressed-length-001")]
        [InlineData("tabledata-decompressed-length-002")]
        [InlineData("tabledata-decompressed-length-003")]
        [InlineData("tabledata-decompressed-length-004")]
        [InlineData("tabledata-extraneous-data-001")]
        // §3.7 glyf/loca reconstruction
        [InlineData("tabledata-glyf-bbox-002")]
        [InlineData("tabledata-glyf-bbox-003")]
        [InlineData("tabledata-glyf-origlength-001")]
        [InlineData("tabledata-glyf-origlength-002")]
        [InlineData("tabledata-glyf-origlength-003")]
        [InlineData("tabledata-bad-origlength-loca-001")]
        [InlineData("tabledata-bad-origlength-loca-002")]
        [InlineData("tabledata-non-zero-loca-001")]
        // §3.8 transform flags / hmtx transform
        [InlineData("tabledata-transform-bad-flag-001")]
        [InlineData("tabledata-transform-bad-flag-002")]
        [InlineData("tabledata-transform-hmtx-003")]
        [InlineData("tabledata-transform-hmtx-004")]
        public void MustRejectFixtureIsRejected(string wptTestName)
        {
            byte[] fixtureBytes = ReadFixtureBytes(wptTestName);
            bool decodeSucceeded = WebFontContainerDecoder.TryDecodeToSfnt(fixtureBytes,
                WebFontContainerParsingMode.StrictWoff,
                out _, out WebFontContainerDecodeFailure decodeFailure);
            Assert.False(decodeSucceeded, $"{wptTestName}: decode must fail but succeeded");
            Assert.NotEqual(WebFontContainerDecodeFailureCode.None, decodeFailure.FailureCode);
        }

        [Theory]
        // §3.5: VALID font using custom-tag encoding for known tables (both encodings legal)
        [InlineData("directory-knowntags-001")]
        // §3.7: VALID simple glyph with no explicit bbox (bbox bitmap 0 → recompute)
        [InlineData("tabledata-glyf-bbox-001")]
        // §3.9: metadata is display-only — a div inside the copyright text element must
        // have NO effect on font loading (conform-metadata-noeffect)
        [InlineData("metadatadisplay-schema-copyright-017")]
        public void MustAcceptFixtureDecodesAndParses(string wptTestName)
        {
            byte[] fixtureBytes = ReadFixtureBytes(wptTestName);
            Assert.True(WebFontContainerDecoder.TryDecodeToSfnt(fixtureBytes,
                WebFontContainerParsingMode.StrictWoff,
                out SfntFontFileBytes reconstructedSfnt, out WebFontContainerDecodeFailure decodeFailure),
                $"{wptTestName}: {decodeFailure}");

            var reconstructedFontInfo = new FontInfo();
            Assert.Equal(1, reconstructedFontInfo.stbtt_InitFont(reconstructedSfnt.FontFileBytes, 0));
        }

        [Fact]
        public void UnknownMagicPassesThroughInLenientModeOnly()
        {
            byte[] fixtureBytes = ReadFixtureBytes("header-signature-001"); // magic 'XXXX'

            // Lenient (default) mode: unknown magic passes through as plain sfnt bytes…
            Assert.Equal(WebFontContainerFormat.NotAWebFontContainer,
                WebFontContainerDecoder.SniffContainerFormat(fixtureBytes));
            Assert.True(WebFontContainerDecoder.TryDecodeToSfnt(fixtureBytes, out SfntFontFileBytes passThroughBytes, out _));
            // …zero-copy: the same byte array, so rejection falls to the sfnt consumer.
            Assert.Same(fixtureBytes, passThroughBytes.FontFileBytes);

            // StrictWoff mode: the decoder itself rejects with the dedicated code.
            Assert.False(WebFontContainerDecoder.TryDecodeToSfnt(fixtureBytes,
                WebFontContainerParsingMode.StrictWoff, out _, out WebFontContainerDecodeFailure strictFailure));
            Assert.Equal(WebFontContainerDecodeFailureCode.NotAWoffContainerInStrictMode, strictFailure.FailureCode);
        }
    }
}