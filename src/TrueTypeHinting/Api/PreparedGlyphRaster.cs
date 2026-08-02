using System;
using StbTrueTypeSharp.TrueTypeHinting.Geometry;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.TrueTypeHinting
{
    /// <summary>Prepares one glyph outline for bitmap bounds and rasterization.</summary>
    public interface IGlyphRasterPreparationStrategy
    {
        bool TryPrepareGlyphRaster(DevicePpemY devicePpemY, TrueTypeGlyphIndex trueTypeGlyphIndex,
            float horizontalSubpixelShift, out IPreparedGlyphRaster preparedGlyphRaster,
            out TrueTypeYHintingFailure preparationFailure);
    }

    /// <summary>
    /// Immutable bitmap bounds and raster operation produced by one outline-preparation strategy.
    /// The same prepared object must supply both atlas bounds and pixels.
    /// </summary>
    public interface IPreparedGlyphRaster
    {
        int BitmapLeft { get; }
        int BitmapTop { get; }
        int BitmapRight { get; }
        int BitmapBottom { get; }

        void Rasterize(FakePtr<byte> output, int outputWidth, int outputHeight, int outputStride);
    }

    /// <summary>Strict TrueType Y-only strategy. It never substitutes custom autofit.</summary>
    public sealed class TrueTypeYGlyphRasterPreparationStrategy : IGlyphRasterPreparationStrategy
    {
        private readonly TrueTypeYHintingEngine _trueTypeYHintingEngine;
        private readonly FontFace.TrueTypeHintingFontFace _trueTypeHintingFontFace;

        private TrueTypeYGlyphRasterPreparationStrategy(TrueTypeYHintingEngine trueTypeYHintingEngine,
            FontFace.TrueTypeHintingFontFace trueTypeHintingFontFace)
        {
            _trueTypeYHintingEngine = trueTypeYHintingEngine;
            _trueTypeHintingFontFace = trueTypeHintingFontFace;
        }

        public static bool TryCreate(byte[] trueTypeFontFileBytes, TrueTypeFaceIndex trueTypeFaceIndex,
            out TrueTypeYGlyphRasterPreparationStrategy preparationStrategy,
            out TrueTypeYHintingFailure preparationFailure)
        {
            var trueTypeYHintingEngine = new TrueTypeYHintingEngine();
            if (!trueTypeYHintingEngine.TryCreateFontFace(trueTypeFontFileBytes, trueTypeFaceIndex,
                    out FontFace.TrueTypeHintingFontFace trueTypeHintingFontFace, out preparationFailure))
            {
                preparationStrategy = null;
                return false;
            }
            preparationStrategy = new TrueTypeYGlyphRasterPreparationStrategy(
                trueTypeYHintingEngine, trueTypeHintingFontFace);
            return true;
        }

        public bool TryPrepareGlyphRaster(DevicePpemY devicePpemY, TrueTypeGlyphIndex trueTypeGlyphIndex,
            float horizontalSubpixelShift, out IPreparedGlyphRaster preparedGlyphRaster,
            out TrueTypeYHintingFailure preparationFailure)
        {
            SizeInstance.TrueTypeHintingSizeInstanceResult sizeInstanceResult =
                _trueTypeYHintingEngine.CreateSizeInstance(_trueTypeHintingFontFace, devicePpemY);
            if (!sizeInstanceResult.Succeeded)
            {
                preparedGlyphRaster = null;
                preparationFailure = sizeInstanceResult.Failure;
                return false;
            }
            return _trueTypeYHintingEngine.TryPrepareGlyphRaster(sizeInstanceResult.SizeInstance,
                trueTypeGlyphIndex, horizontalSubpixelShift, out preparedGlyphRaster, out preparationFailure);
        }
    }

    internal sealed class TrueTypeYPreparedGlyphRaster : IPreparedGlyphRaster
    {
        private readonly stbtt_vertex[] _quadraticVerticesF26Dot6;
        private readonly float _horizontalSubpixelShift;

        internal TrueTypeYPreparedGlyphRaster(stbtt_vertex[] quadraticVerticesF26Dot6,
            float horizontalSubpixelShift)
        {
            _quadraticVerticesF26Dot6 = quadraticVerticesF26Dot6 ?? throw new ArgumentNullException(nameof(quadraticVerticesF26Dot6));
            _horizontalSubpixelShift = horizontalSubpixelShift;
            ComputeBitmapBounds(quadraticVerticesF26Dot6, horizontalSubpixelShift,
                out int bitmapLeft, out int bitmapTop, out int bitmapRight, out int bitmapBottom);
            BitmapLeft = bitmapLeft;
            BitmapTop = bitmapTop;
            BitmapRight = bitmapRight;
            BitmapBottom = bitmapBottom;
        }

        public int BitmapLeft { get; }
        public int BitmapTop { get; }
        public int BitmapRight { get; }
        public int BitmapBottom { get; }

        public void Rasterize(FakePtr<byte> output, int outputWidth, int outputHeight, int outputStride)
        {
            var glyphBitmap = new Bitmap
            {
                pixels = output,
                w = outputWidth,
                h = outputHeight,
                stride = outputStride,
            };
            if (outputWidth != 0 && outputHeight != 0)
                glyphBitmap.stbtt_Rasterize(0.35f, _quadraticVerticesF26Dot6, _quadraticVerticesF26Dot6.Length,
                    1f / 64f, 1f / 64f, _horizontalSubpixelShift, 0f, BitmapLeft, BitmapTop, 1);
        }

        private static void ComputeBitmapBounds(stbtt_vertex[] verticesF26Dot6, float horizontalSubpixelShift,
            out int bitmapLeft, out int bitmapTop, out int bitmapRight, out int bitmapBottom)
        {
            if (verticesF26Dot6.Length == 0)
            {
                bitmapLeft = bitmapTop = bitmapRight = bitmapBottom = 0;
                return;
            }

            int minimumHorizontalF26Dot6 = int.MaxValue;
            int minimumVerticalF26Dot6 = int.MaxValue;
            int maximumHorizontalF26Dot6 = int.MinValue;
            int maximumVerticalF26Dot6 = int.MinValue;
            for (int vertexIndex = 0; vertexIndex < verticesF26Dot6.Length; vertexIndex++)
            {
                stbtt_vertex vertex = verticesF26Dot6[vertexIndex];
                IncludeCoordinate(vertex.x, vertex.y, ref minimumHorizontalF26Dot6, ref minimumVerticalF26Dot6,
                    ref maximumHorizontalF26Dot6, ref maximumVerticalF26Dot6);
                if (vertex.type == STBTT_vcurve || vertex.type == STBTT_vcubic)
                    IncludeCoordinate(vertex.cx, vertex.cy, ref minimumHorizontalF26Dot6, ref minimumVerticalF26Dot6,
                        ref maximumHorizontalF26Dot6, ref maximumVerticalF26Dot6);
                if (vertex.type == STBTT_vcubic)
                    IncludeCoordinate(vertex.cx1, vertex.cy1, ref minimumHorizontalF26Dot6, ref minimumVerticalF26Dot6,
                        ref maximumHorizontalF26Dot6, ref maximumVerticalF26Dot6);
            }

            bitmapLeft = (int)Math.Floor(minimumHorizontalF26Dot6 / 64f + horizontalSubpixelShift);
            bitmapTop = (int)Math.Floor(-maximumVerticalF26Dot6 / 64f);
            bitmapRight = (int)Math.Ceiling(maximumHorizontalF26Dot6 / 64f + horizontalSubpixelShift);
            bitmapBottom = (int)Math.Ceiling(-minimumVerticalF26Dot6 / 64f);
        }

        private static void IncludeCoordinate(int horizontalF26Dot6, int verticalF26Dot6,
            ref int minimumHorizontalF26Dot6, ref int minimumVerticalF26Dot6,
            ref int maximumHorizontalF26Dot6, ref int maximumVerticalF26Dot6)
        {
            minimumHorizontalF26Dot6 = Math.Min(minimumHorizontalF26Dot6, horizontalF26Dot6);
            minimumVerticalF26Dot6 = Math.Min(minimumVerticalF26Dot6, verticalF26Dot6);
            maximumHorizontalF26Dot6 = Math.Max(maximumHorizontalF26Dot6, horizontalF26Dot6);
            maximumVerticalF26Dot6 = Math.Max(maximumVerticalF26Dot6, verticalF26Dot6);
        }
    }
}