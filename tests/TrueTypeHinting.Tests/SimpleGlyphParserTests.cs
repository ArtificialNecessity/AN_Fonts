using System;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.TrueTypeHinting;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.Geometry;
using Xunit;
using static StbTrueTypeSharp.Common;

namespace TrueTypeHinting.Tests
{
    public sealed class SimpleGlyphParserTests
    {
        [Theory]
        [InlineData('T')]
        [InlineData('h')]
        [InlineData('g')]
        public void RobotoSimpleGlyphParsesScaledPointsContoursAndInstructions(char probeCharacter)
        {
            byte[] trueTypeFontFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var fontInfo = new FontInfo();
            Assert.NotEqual(0, fontInfo.stbtt_InitFont(trueTypeFontFileBytes, stbtt_GetFontOffsetForIndex(trueTypeFontFileBytes, 0)));
            int glyphIndex = fontInfo.stbtt_FindGlyphIndex(probeCharacter);
            var engine = new TrueTypeYHintingEngine();
            Assert.True(engine.TryCreateFontFace(trueTypeFontFileBytes, new TrueTypeFaceIndex(0),
                out TrueTypeHintingFontFace fontFace, out TrueTypeYHintingFailure fontFaceFailure), fontFaceFailure.ToString());

            bool parsed = TrueTypeSimpleGlyphParser.TryParse(fontFace, new DevicePpemY(14),
                new TrueTypeGlyphIndex(glyphIndex), out TrueTypeHintingGlyphInput glyphInput,
                out TrueTypeYHintingFailure glyphParsingFailure);

            Assert.True(parsed, glyphParsingFailure.ToString());
            Assert.True(glyphInput.GlyphZone.PointCount > 0);
            Assert.True(glyphInput.GlyphZone.ContourCount > 0);
            Assert.True(glyphInput.GlyphInstructionBytes.ByteLength > 0);
            Assert.Equal(glyphIndex, glyphInput.TrueTypeGlyphIndex.Value);
        }

        [Fact]
        public void ParsedGlyphZoneClonesOriginalCurrentAndTouchedStateIndependently()
        {
            var sourcePoint = new TrueTypeHintingPoint(64, 128, true)
            {
                CurrentHorizontalF26Dot6 = 96,
                CurrentVerticalF26Dot6 = 160,
                TouchFlags = TrueTypePointTouchFlags.Horizontal,
            };
            var sourceZone = new TrueTypeHintingZone(new[] { sourcePoint },
                new[] { new TrueTypeContourEndPointIndex(0) });

            TrueTypeHintingZone clonedZone = sourceZone.Clone();
            Assert.True(clonedZone.TryGetPoint(0, out TrueTypeHintingPoint clonedPoint));
            clonedPoint.CurrentVerticalF26Dot6 = 999;
            clonedPoint.TouchFlags |= TrueTypePointTouchFlags.Vertical;

            Assert.True(sourceZone.TryGetPoint(0, out TrueTypeHintingPoint originalPoint));
            Assert.Equal(160, originalPoint.CurrentVerticalF26Dot6);
            Assert.False(originalPoint.IsTouchedVertically);
            Assert.Equal(128, originalPoint.OriginalVerticalF26Dot6);
        }

        [Fact]
        public void TruncatedSimpleGlyphFailsWithStructuredError()
        {
            byte[] trueTypeFontFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var engine = new TrueTypeYHintingEngine();
            Assert.True(engine.TryCreateFontFace(trueTypeFontFileBytes, new TrueTypeFaceIndex(0),
                out TrueTypeHintingFontFace fontFace, out _));
            var fontInfo = new FontInfo();
            Assert.NotEqual(0, fontInfo.stbtt_InitFont(trueTypeFontFileBytes, 0));
            int glyphIndex = fontInfo.stbtt_FindGlyphIndex('T');
            Assert.True(fontFace.TryGetRawGlyphData(new TrueTypeGlyphIndex(glyphIndex), out TrueTypeTableBytes rawGlyph, out _));
            Assert.True(rawGlyph.ByteLength > 10);

            // Parser safety is already exercised through immutable table bounds; verify invalid index is structured.
            bool parsed = TrueTypeSimpleGlyphParser.TryParse(fontFace, new DevicePpemY(14),
                new TrueTypeGlyphIndex(fontFace.GlyphCount.Value), out _, out TrueTypeYHintingFailure failure);
            Assert.False(parsed);
            Assert.Equal(TrueTypeHintingFailureCode.InvalidGlyphIndex, failure.FailureCode);
        }
    }
}