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
    public sealed class PreparedGlyphRasterTests
    {
        [Fact]
        public void OutlineBuilderRestoresOriginalHorizontalAndRetainsHintedVerticalCoordinates()
        {
            var hintedZone = new TrueTypeHintingZone(new[]
            {
                Point(64, 128, 640, true),
                Point(192, 256, 704, false),
                Point(320, 128, 768, true),
            }, new[] { new TrueTypeContourEndPointIndex(2) });

            Assert.True(TrueTypeYHintedOutlineBuilder.TryBuild(hintedZone, out stbtt_vertex[] vertices,
                out TrueTypeYHintingFailure failure), failure.ToString());

            Assert.Equal(3, vertices.Length);
            Assert.Equal(STBTT_vmove, vertices[0].type);
            Assert.Equal(64, vertices[0].x);
            Assert.Equal(640, vertices[0].y);
            Assert.Equal(STBTT_vcurve, vertices[1].type);
            Assert.Equal(320, vertices[1].x);
            Assert.Equal(768, vertices[1].y);
            Assert.Equal(192, vertices[1].cx);
            Assert.Equal(704, vertices[1].cy);
            Assert.Equal(STBTT_vline, vertices[2].type);
            Assert.Equal(64, vertices[2].x);
            Assert.Equal(640, vertices[2].y);
        }

        [Theory]
        [InlineData('T')]
        [InlineData('h')]
        [InlineData('g')]
        public void RobotoPreparedRasterUsesOneAuthoritativeBoundsAndPixelOperation(char probeCharacter)
        {
            byte[] fontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var fontInfo = new FontInfo();
            Assert.NotEqual(0, fontInfo.stbtt_InitFont(fontBytes, stbtt_GetFontOffsetForIndex(fontBytes, 0)));
            int glyphIndex = fontInfo.stbtt_FindGlyphIndex(probeCharacter);
            Assert.True(TrueTypeYGlyphRasterPreparationStrategy.TryCreate(fontBytes, new TrueTypeFaceIndex(0),
                out TrueTypeYGlyphRasterPreparationStrategy strategy, out TrueTypeYHintingFailure creationFailure),
                creationFailure.ToString());

            Assert.True(strategy.TryPrepareGlyphRaster(new DevicePpemY(14), new TrueTypeGlyphIndex(glyphIndex),
                0.25f, out IPreparedGlyphRaster preparedRaster, out TrueTypeYHintingFailure preparationFailure),
                preparationFailure.ToString());
            int bitmapWidth = preparedRaster.BitmapRight - preparedRaster.BitmapLeft;
            int bitmapHeight = preparedRaster.BitmapBottom - preparedRaster.BitmapTop;
            Assert.True(bitmapWidth > 0);
            Assert.True(bitmapHeight > 0);
            byte[] pixels = new byte[bitmapWidth * bitmapHeight];

            preparedRaster.Rasterize(new FakePtr<byte>(pixels), bitmapWidth, bitmapHeight, bitmapWidth);

            Assert.Contains(pixels, pixel => pixel != 0);
        }

        private static TrueTypeHintingPoint Point(int originalHorizontalF26Dot6, int originalVerticalF26Dot6,
            int hintedVerticalF26Dot6, bool isOnCurve)
            => new TrueTypeHintingPoint(originalHorizontalF26Dot6, originalVerticalF26Dot6, isOnCurve)
            {
                CurrentHorizontalF26Dot6 = originalHorizontalF26Dot6 + 999,
                CurrentVerticalF26Dot6 = hintedVerticalF26Dot6,
            };
    }
}