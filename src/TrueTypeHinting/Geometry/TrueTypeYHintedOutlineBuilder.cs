using System;
using System.Collections.Generic;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.TrueTypeHinting.Geometry
{
    /// <summary>Reconstructs stb quadratic vertices from a completed glyph zone.</summary>
    internal static class TrueTypeYHintedOutlineBuilder
    {
        internal static bool TryBuild(TrueTypeHintingZone hintedGlyphZone, out stbtt_vertex[] verticesF26Dot6,
            out TrueTypeYHintingFailure outlineFailure)
        {
            if (hintedGlyphZone == null) throw new ArgumentNullException(nameof(hintedGlyphZone));
            var vertices = new List<stbtt_vertex>(hintedGlyphZone.OutlinePointCount + hintedGlyphZone.ContourCount * 2);
            for (int contourIndex = 0; contourIndex < hintedGlyphZone.ContourCount; contourIndex++)
            {
                if (!hintedGlyphZone.TryGetContourPointRange(contourIndex, out int firstPointIndex, out int lastPointIndex) ||
                    !hintedGlyphZone.TryGetPoint(firstPointIndex, out TrueTypeHintingPoint firstPoint) ||
                    !hintedGlyphZone.TryGetPoint(lastPointIndex, out TrueTypeHintingPoint lastPoint))
                    return Failed("A hinted contour has an invalid point range.", out verticesF26Dot6, out outlineFailure);

                int startHorizontalF26Dot6;
                int startVerticalF26Dot6;
                int iterationFirstPointIndex;
                int iterationLastPointIndex;
                if (firstPoint.IsOnCurve)
                {
                    startHorizontalF26Dot6 = firstPoint.OriginalHorizontalF26Dot6;
                    startVerticalF26Dot6 = firstPoint.CurrentVerticalF26Dot6;
                    iterationFirstPointIndex = firstPointIndex + 1;
                    iterationLastPointIndex = lastPointIndex;
                }
                else if (lastPoint.IsOnCurve)
                {
                    startHorizontalF26Dot6 = lastPoint.OriginalHorizontalF26Dot6;
                    startVerticalF26Dot6 = lastPoint.CurrentVerticalF26Dot6;
                    iterationFirstPointIndex = firstPointIndex;
                    iterationLastPointIndex = lastPointIndex - 1;
                }
                else
                {
                    startHorizontalF26Dot6 = Midpoint(lastPoint.OriginalHorizontalF26Dot6, firstPoint.OriginalHorizontalF26Dot6);
                    startVerticalF26Dot6 = Midpoint(lastPoint.CurrentVerticalF26Dot6, firstPoint.CurrentVerticalF26Dot6);
                    iterationFirstPointIndex = firstPointIndex;
                    iterationLastPointIndex = lastPointIndex;
                }

                if (!TryAddVertex(vertices, STBTT_vmove, startHorizontalF26Dot6, startVerticalF26Dot6, 0, 0))
                    return Failed("A hinted move coordinate lies outside the raster vertex range.", out verticesF26Dot6, out outlineFailure);

                bool hasControlPoint = false;
                int controlHorizontalF26Dot6 = 0;
                int controlVerticalF26Dot6 = 0;
                for (int pointIndex = iterationFirstPointIndex; pointIndex <= iterationLastPointIndex; pointIndex++)
                {
                    if (!hintedGlyphZone.TryGetPoint(pointIndex, out TrueTypeHintingPoint point))
                        return Failed("A hinted contour references a missing point.", out verticesF26Dot6, out outlineFailure);
                    int pointHorizontalF26Dot6 = point.OriginalHorizontalF26Dot6;
                    int pointVerticalF26Dot6 = point.CurrentVerticalF26Dot6;
                    if (point.IsOnCurve)
                    {
                        byte vertexType = (byte)(hasControlPoint ? STBTT_vcurve : STBTT_vline);
                        if (!TryAddVertex(vertices, vertexType, pointHorizontalF26Dot6, pointVerticalF26Dot6,
                                controlHorizontalF26Dot6, controlVerticalF26Dot6))
                            return Failed("A hinted line or curve coordinate lies outside the raster vertex range.", out verticesF26Dot6, out outlineFailure);
                        hasControlPoint = false;
                    }
                    else
                    {
                        if (hasControlPoint)
                        {
                            int impliedHorizontalF26Dot6 = Midpoint(controlHorizontalF26Dot6, pointHorizontalF26Dot6);
                            int impliedVerticalF26Dot6 = Midpoint(controlVerticalF26Dot6, pointVerticalF26Dot6);
                            if (!TryAddVertex(vertices, STBTT_vcurve, impliedHorizontalF26Dot6, impliedVerticalF26Dot6,
                                    controlHorizontalF26Dot6, controlVerticalF26Dot6))
                                return Failed("A hinted implied curve point lies outside the raster vertex range.", out verticesF26Dot6, out outlineFailure);
                        }
                        controlHorizontalF26Dot6 = pointHorizontalF26Dot6;
                        controlVerticalF26Dot6 = pointVerticalF26Dot6;
                        hasControlPoint = true;
                    }
                }

                if (!TryAddVertex(vertices, (byte)(hasControlPoint ? STBTT_vcurve : STBTT_vline),
                        startHorizontalF26Dot6, startVerticalF26Dot6, controlHorizontalF26Dot6, controlVerticalF26Dot6))
                    return Failed("A hinted closing segment lies outside the raster vertex range.", out verticesF26Dot6, out outlineFailure);
            }

            verticesF26Dot6 = vertices.ToArray();
            outlineFailure = default;
            return true;
        }

        private static int Midpoint(int firstValue, int secondValue) => (firstValue + secondValue) >> 1;

        private static bool TryAddVertex(List<stbtt_vertex> vertices, byte vertexType, int horizontalF26Dot6,
            int verticalF26Dot6, int controlHorizontalF26Dot6, int controlVerticalF26Dot6)
        {
            if (horizontalF26Dot6 < short.MinValue || horizontalF26Dot6 > short.MaxValue ||
                verticalF26Dot6 < short.MinValue || verticalF26Dot6 > short.MaxValue ||
                controlHorizontalF26Dot6 < short.MinValue || controlHorizontalF26Dot6 > short.MaxValue ||
                controlVerticalF26Dot6 < short.MinValue || controlVerticalF26Dot6 > short.MaxValue)
                return false;
            vertices.Add(new stbtt_vertex
            {
                type = vertexType,
                x = (short)horizontalF26Dot6,
                y = (short)verticalF26Dot6,
                cx = (short)controlHorizontalF26Dot6,
                cy = (short)controlVerticalF26Dot6,
            });
            return true;
        }

        private static bool Failed(string failureMessage, out stbtt_vertex[] verticesF26Dot6,
            out TrueTypeYHintingFailure outlineFailure)
        {
            verticesF26Dot6 = null;
            outlineFailure = new TrueTypeYHintingFailure(TrueTypeHintingFailureCode.HintedOutlineConstructionFailed,
                new TrueTypeHintingFailureMessage(failureMessage));
            return false;
        }
    }
}