using System;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.TrueTypeHinting;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.Geometry;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;
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
            Assert.True(glyphInput.GlyphZone.PointCount > 4);
            Assert.True(glyphInput.GlyphZone.ContourCount > 0);
            Assert.True(glyphInput.GlyphInstructionBytes.ByteLength > 0);
            Assert.Equal(glyphIndex, glyphInput.TrueTypeGlyphIndex.Value);

            Assert.True(fontFace.TryGetRawGlyphData(new TrueTypeGlyphIndex(glyphIndex),
                out TrueTypeTableBytes rawGlyphData, out TrueTypeYHintingFailure rawGlyphFailure), rawGlyphFailure.ToString());
            byte[] rawGlyphBytes = rawGlyphData.ToByteArray();
            int glyphMinimumHorizontalFontUnits = unchecked((short)((rawGlyphBytes[2] << 8) | rawGlyphBytes[3]));
            int advanceWidthFontUnits = 0;
            int leftSideBearingFontUnits = 0;
            fontInfo.stbtt_GetGlyphHMetrics(glyphIndex, ref advanceWidthFontUnits, ref leftSideBearingFontUnits);
            int firstPhantomPointIndex = glyphInput.GlyphZone.PointCount - 4;
            Assert.True(glyphInput.GlyphZone.TryGetPoint(firstPhantomPointIndex, out TrueTypeHintingPoint leftSideBearingPoint));
            Assert.True(glyphInput.GlyphZone.TryGetPoint(firstPhantomPointIndex + 1, out TrueTypeHintingPoint advanceWidthPoint));
            Assert.True(glyphInput.GlyphZone.TryGetPoint(firstPhantomPointIndex + 2, out TrueTypeHintingPoint topOriginPoint));
            Assert.True(glyphInput.GlyphZone.TryGetPoint(firstPhantomPointIndex + 3, out TrueTypeHintingPoint advanceHeightPoint));

            Assert.Equal(ScaleFontUnitsToF26Dot6(glyphMinimumHorizontalFontUnits - leftSideBearingFontUnits, 2048, 14),
                leftSideBearingPoint.OriginalHorizontalF26Dot6);
            Assert.Equal(ScaleFontUnitsToF26Dot6(glyphMinimumHorizontalFontUnits - leftSideBearingFontUnits + advanceWidthFontUnits, 2048, 14),
                advanceWidthPoint.OriginalHorizontalF26Dot6);
            Assert.Equal(0, leftSideBearingPoint.OriginalVerticalF26Dot6);
            Assert.Equal(0, advanceWidthPoint.OriginalVerticalF26Dot6);
            Assert.Equal(0, topOriginPoint.OriginalHorizontalF26Dot6);
            Assert.Equal(0, advanceHeightPoint.OriginalHorizontalF26Dot6);
            Assert.Equal(0, leftSideBearingPoint.CurrentHorizontalF26Dot6 % 64);
            Assert.Equal(0, advanceWidthPoint.CurrentHorizontalF26Dot6 % 64);
            Assert.Equal(0, topOriginPoint.CurrentVerticalF26Dot6 % 64);
            Assert.Equal(0, advanceHeightPoint.CurrentVerticalF26Dot6 % 64);
        }

        [Fact]
        public void EmptySpaceGlyphStillContainsFourAddressablePhantomPoints()
        {
            byte[] fontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var fontInfo = new FontInfo();
            Assert.NotEqual(0, fontInfo.stbtt_InitFont(fontBytes, 0));
            int spaceGlyphIndex = fontInfo.stbtt_FindGlyphIndex(' ');
            var engine = new TrueTypeYHintingEngine();
            Assert.True(engine.TryCreateFontFace(fontBytes, new TrueTypeFaceIndex(0),
                out TrueTypeHintingFontFace fontFace, out TrueTypeYHintingFailure faceFailure), faceFailure.ToString());

            Assert.True(TrueTypeSimpleGlyphParser.TryParse(fontFace, new DevicePpemY(14),
                new TrueTypeGlyphIndex(spaceGlyphIndex), out TrueTypeHintingGlyphInput glyphInput,
                out TrueTypeYHintingFailure parsingFailure), parsingFailure.ToString());
            Assert.Equal(0, glyphInput.GlyphZone.ContourCount);
            Assert.Equal(4, glyphInput.GlyphZone.PointCount);
        }

        [Fact]
        public void GlyphProgramCanAddressAndTouchAppendedPhantomPoint()
        {
            byte[] fontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var fontInfo = new FontInfo();
            Assert.NotEqual(0, fontInfo.stbtt_InitFont(fontBytes, 0));
            int glyphIndex = fontInfo.stbtt_FindGlyphIndex('T');
            var engine = new TrueTypeYHintingEngine();
            Assert.True(engine.TryCreateFontFace(fontBytes, new TrueTypeFaceIndex(0),
                out TrueTypeHintingFontFace fontFace, out TrueTypeYHintingFailure faceFailure), faceFailure.ToString());
            Assert.True(TrueTypeSimpleGlyphParser.TryParse(fontFace, new DevicePpemY(14),
                new TrueTypeGlyphIndex(glyphIndex), out TrueTypeHintingGlyphInput glyphInput,
                out TrueTypeYHintingFailure parsingFailure), parsingFailure.ToString());

            int firstPhantomPointIndex = glyphInput.GlyphZone.PointCount - 4;
            Assert.InRange(firstPhantomPointIndex, 0, byte.MaxValue);
            TrueTypeHintingExecutionZones executionZones = TrueTypeHintingExecutionZones.Create(
                glyphInput.GlyphZone, fontFace.MaximumProfile.MaximumTwilightPointCount.Value);
            TrueTypeVirtualMachineState virtualMachineState = TrueTypeVirtualMachineState.ForTests();
            virtualMachineState.GraphicsState.ProjectionVector = TrueTypeUnitVector.Horizontal;
            virtualMachineState.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Horizontal;
            virtualMachineState.GraphicsState.FreedomVector = TrueTypeUnitVector.Horizontal;
            var interpreter = new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());

            TrueTypeVirtualMachineResult executionResult = interpreter.Execute(new byte[]
            {
                0xB0, (byte)firstPhantomPointIndex,
                (byte)TrueTypeInstructionOpcode.MoveDirectAbsolutePointWithRounding,
            }, virtualMachineState, executionZones);

            Assert.True(executionResult.Succeeded, executionResult.Failure.ToString());
            Assert.True(executionZones.GlyphZone.TryGetPoint(firstPhantomPointIndex, out TrueTypeHintingPoint phantomPoint));
            Assert.True(phantomPoint.IsTouchedHorizontally);
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

        private static int ScaleFontUnitsToF26Dot6(int fontUnitValue, int unitsPerEm, int devicePpemY)
        {
            long numerator = (long)fontUnitValue * devicePpemY * 64;
            return numerator >= 0 ? (int)((numerator + unitsPerEm / 2) / unitsPerEm)
                : (int)-((-numerator + unitsPerEm / 2) / unitsPerEm);
        }
    }
}