using System;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;

namespace StbTrueTypeSharp.TrueTypeHinting.Geometry
{
    /// <summary>Projection, movement, and interpolation primitives for TrueType point zones.</summary>
    internal static class TrueTypeHintingGeometryOperations
    {
        internal static bool TryGetPoint(TrueTypeHintingExecutionZones executionZones,
            TrueTypeZonePointerIndex zonePointerIndex, int pointIndex, out TrueTypeHintingPoint hintingPoint,
            out TrueTypeVirtualMachineFailure geometryFailure)
        {
            if (executionZones == null)
            {
                hintingPoint = null;
                geometryFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidZonePointer,
                    "A point instruction was executed without TrueType point zones.");
                return false;
            }
            if (!executionZones.TryGetZone(zonePointerIndex, out TrueTypeHintingZone hintingZone, out geometryFailure))
            {
                hintingPoint = null;
                return false;
            }
            if (!hintingZone.TryGetPoint(pointIndex, out hintingPoint))
            {
                geometryFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidPointIndex,
                    "The TrueType point index lies outside the selected zone.");
                return false;
            }
            geometryFailure = default;
            return true;
        }

        internal static int ProjectCurrent(TrueTypeHintingPoint hintingPoint, TrueTypeUnitVector projectionVector)
            => Project(hintingPoint.CurrentHorizontalF26Dot6, hintingPoint.CurrentVerticalF26Dot6, projectionVector);

        internal static int ProjectOriginal(TrueTypeHintingPoint hintingPoint, TrueTypeUnitVector projectionVector)
            => Project(hintingPoint.OriginalHorizontalF26Dot6, hintingPoint.OriginalVerticalF26Dot6, projectionVector);

        internal static int ProjectDistanceCurrent(TrueTypeHintingPoint firstPoint, TrueTypeHintingPoint secondPoint,
            TrueTypeUnitVector projectionVector)
            => Project(firstPoint.CurrentHorizontalF26Dot6 - secondPoint.CurrentHorizontalF26Dot6,
                firstPoint.CurrentVerticalF26Dot6 - secondPoint.CurrentVerticalF26Dot6, projectionVector);

        internal static int ProjectDistanceOriginal(TrueTypeHintingPoint firstPoint, TrueTypeHintingPoint secondPoint,
            TrueTypeUnitVector dualProjectionVector)
            => Project(firstPoint.OriginalHorizontalF26Dot6 - secondPoint.OriginalHorizontalF26Dot6,
                firstPoint.OriginalVerticalF26Dot6 - secondPoint.OriginalVerticalF26Dot6, dualProjectionVector);

        internal static bool TryCreateCurrentLineUnitVector(TrueTypeHintingPoint firstPoint,
            TrueTypeHintingPoint secondPoint, bool perpendicular, out TrueTypeUnitVector lineUnitVector,
            out TrueTypeVirtualMachineFailure geometryFailure)
            => TryCreateLineUnitVector(
                secondPoint.CurrentHorizontalF26Dot6 - firstPoint.CurrentHorizontalF26Dot6,
                secondPoint.CurrentVerticalF26Dot6 - firstPoint.CurrentVerticalF26Dot6,
                perpendicular, out lineUnitVector, out geometryFailure);

        internal static bool TryCreateOriginalLineUnitVector(TrueTypeHintingPoint firstPoint,
            TrueTypeHintingPoint secondPoint, bool perpendicular, out TrueTypeUnitVector lineUnitVector,
            out TrueTypeVirtualMachineFailure geometryFailure)
            => TryCreateLineUnitVector(
                secondPoint.OriginalHorizontalF26Dot6 - firstPoint.OriginalHorizontalF26Dot6,
                secondPoint.OriginalVerticalF26Dot6 - firstPoint.OriginalVerticalF26Dot6,
                perpendicular, out lineUnitVector, out geometryFailure);

        private static bool TryCreateLineUnitVector(int horizontalDifferenceF26Dot6, int verticalDifferenceF26Dot6,
            bool perpendicular, out TrueTypeUnitVector lineUnitVector, out TrueTypeVirtualMachineFailure geometryFailure)
        {
            long squaredLength = (long)horizontalDifferenceF26Dot6 * horizontalDifferenceF26Dot6 +
                (long)verticalDifferenceF26Dot6 * verticalDifferenceF26Dot6;
            if (squaredLength == 0)
            {
                lineUnitVector = default;
                geometryFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidFreedomProjectionVectors,
                    "A line-derived TrueType vector requires two distinct point positions.");
                return false;
            }
            double lineLength = Math.Sqrt(squaredLength);
            int horizontalComponent = (int)Math.Round(horizontalDifferenceF26Dot6 * 16384.0 / lineLength,
                MidpointRounding.AwayFromZero);
            int verticalComponent = (int)Math.Round(verticalDifferenceF26Dot6 * 16384.0 / lineLength,
                MidpointRounding.AwayFromZero);
            lineUnitVector = perpendicular
                ? new TrueTypeUnitVector(new TrueTypeVectorComponent(-verticalComponent), new TrueTypeVectorComponent(horizontalComponent))
                : new TrueTypeUnitVector(new TrueTypeVectorComponent(horizontalComponent), new TrueTypeVectorComponent(verticalComponent));
            geometryFailure = default;
            return true;
        }

        internal static bool TryMovePointByProjectedDistance(TrueTypeHintingPoint hintingPoint, int projectedDistanceF26Dot6,
            TrueTypeGraphicsState graphicsState, out TrueTypeVirtualMachineFailure geometryFailure)
        {
            long projectionFreedomDotF2Dot28 =
                (long)graphicsState.ProjectionVector.HorizontalComponent.Value * graphicsState.FreedomVector.HorizontalComponent.Value +
                (long)graphicsState.ProjectionVector.VerticalComponent.Value * graphicsState.FreedomVector.VerticalComponent.Value;
            if (projectionFreedomDotF2Dot28 == 0)
            {
                geometryFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidFreedomProjectionVectors,
                    "Projection and freedom vectors are orthogonal; projected movement is undefined.");
                return false;
            }

            long horizontalMovementNumerator = (long)projectedDistanceF26Dot6 * 0x4000 *
                graphicsState.FreedomVector.HorizontalComponent.Value;
            long verticalMovementNumerator = (long)projectedDistanceF26Dot6 * 0x4000 *
                graphicsState.FreedomVector.VerticalComponent.Value;
            hintingPoint.CurrentHorizontalF26Dot6 += DivideRounded(horizontalMovementNumerator, projectionFreedomDotF2Dot28);
            hintingPoint.CurrentVerticalF26Dot6 += DivideRounded(verticalMovementNumerator, projectionFreedomDotF2Dot28);
            if (graphicsState.FreedomVector.HorizontalComponent.Value != 0)
                hintingPoint.TouchFlags |= TrueTypePointTouchFlags.Horizontal;
            if (graphicsState.FreedomVector.VerticalComponent.Value != 0)
                hintingPoint.TouchFlags |= TrueTypePointTouchFlags.Vertical;
            geometryFailure = default;
            return true;
        }

        internal static int RoundF26Dot6(int valueF26Dot6, TrueTypeGraphicsState graphicsState)
        {
            switch (graphicsState.RoundingMode)
            {
                case TrueTypeRoundingMode.Off: return valueF26Dot6;
                case TrueTypeRoundingMode.DownToGrid: return valueF26Dot6 >= 0 ? valueF26Dot6 & ~63 : -((-valueF26Dot6 + 63) & ~63);
                case TrueTypeRoundingMode.UpToGrid: return valueF26Dot6 >= 0 ? (valueF26Dot6 + 63) & ~63 : -((-valueF26Dot6) & ~63);
                case TrueTypeRoundingMode.ToHalfGrid: return valueF26Dot6 >= 0 ? ((valueF26Dot6 + 32) & ~63) + 32 : -(((-valueF26Dot6 + 32) & ~63) + 32);
                case TrueTypeRoundingMode.ToDoubleGrid: return valueF26Dot6 >= 0 ? (valueF26Dot6 + 16) & ~31 : -((-valueF26Dot6 + 16) & ~31);
                case TrueTypeRoundingMode.Super:
                case TrueTypeRoundingMode.Super45:
                    return RoundSuperF26Dot6(valueF26Dot6, graphicsState.SuperRoundingState);
                default: return valueF26Dot6 >= 0 ? (valueF26Dot6 + 32) & ~63 : -((-valueF26Dot6 + 32) & ~63);
            }
        }

        private static int RoundSuperF26Dot6(int valueF26Dot6, TrueTypeSuperRoundingState superRoundingState)
        {
            int valueSign = valueF26Dot6 < 0 ? -1 : 1;
            long absoluteValue = Math.Abs((long)valueF26Dot6);
            long adjustedValue = absoluteValue + superRoundingState.ThresholdF26Dot6 - superRoundingState.PhaseF26Dot6;
            long roundedMagnitude = FloorDivide(adjustedValue, superRoundingState.PeriodF26Dot6) *
                superRoundingState.PeriodF26Dot6 + superRoundingState.PhaseF26Dot6;
            if (roundedMagnitude < 0) roundedMagnitude = 0;
            return (int)(valueSign * roundedMagnitude);
        }

        private static long FloorDivide(long numerator, long positiveDenominator)
            => numerator >= 0 ? numerator / positiveDenominator : -((-numerator + positiveDenominator - 1) / positiveDenominator);

        internal static void InterpolateUntouchedPoints(TrueTypeHintingZone hintingZone, bool verticalAxis)
        {
            int contourStartPointIndex = 0;
            for (int contourIndex = 0; contourIndex < hintingZone.ContourCount; contourIndex++)
            {
                int contourEndPointIndex = hintingZone.GetContourEndPointIndex(contourIndex).Value;
                InterpolateUntouchedContour(hintingZone, contourStartPointIndex, contourEndPointIndex, verticalAxis);
                contourStartPointIndex = contourEndPointIndex + 1;
            }
        }

        private static void InterpolateUntouchedContour(TrueTypeHintingZone hintingZone, int contourStartPointIndex,
            int contourEndPointIndex, bool verticalAxis)
        {
            int firstTouchedPointIndex = -1;
            for (int pointIndex = contourStartPointIndex; pointIndex <= contourEndPointIndex; pointIndex++)
            {
                hintingZone.TryGetPoint(pointIndex, out TrueTypeHintingPoint point);
                bool touched = verticalAxis ? point.IsTouchedVertically : point.IsTouchedHorizontally;
                if (touched) { firstTouchedPointIndex = pointIndex; break; }
            }
            if (firstTouchedPointIndex < 0) return;

            int previousTouchedPointIndex = firstTouchedPointIndex;
            int searchPointIndex = firstTouchedPointIndex + 1;
            while (true)
            {
                if (searchPointIndex > contourEndPointIndex) searchPointIndex = contourStartPointIndex;
                if (searchPointIndex == firstTouchedPointIndex) break;
                hintingZone.TryGetPoint(searchPointIndex, out TrueTypeHintingPoint searchPoint);
                bool touched = verticalAxis ? searchPoint.IsTouchedVertically : searchPoint.IsTouchedHorizontally;
                if (touched)
                {
                    InterpolateBetweenTouchedPoints(hintingZone, previousTouchedPointIndex, searchPointIndex,
                        contourStartPointIndex, contourEndPointIndex, verticalAxis);
                    previousTouchedPointIndex = searchPointIndex;
                }
                searchPointIndex++;
            }
            InterpolateBetweenTouchedPoints(hintingZone, previousTouchedPointIndex, firstTouchedPointIndex,
                contourStartPointIndex, contourEndPointIndex, verticalAxis);
        }

        private static void InterpolateBetweenTouchedPoints(TrueTypeHintingZone hintingZone, int firstTouchedPointIndex,
            int secondTouchedPointIndex, int contourStartPointIndex, int contourEndPointIndex, bool verticalAxis)
        {
            hintingZone.TryGetPoint(firstTouchedPointIndex, out TrueTypeHintingPoint firstTouchedPoint);
            hintingZone.TryGetPoint(secondTouchedPointIndex, out TrueTypeHintingPoint secondTouchedPoint);
            int firstOriginal = verticalAxis ? firstTouchedPoint.OriginalVerticalF26Dot6 : firstTouchedPoint.OriginalHorizontalF26Dot6;
            int secondOriginal = verticalAxis ? secondTouchedPoint.OriginalVerticalF26Dot6 : secondTouchedPoint.OriginalHorizontalF26Dot6;
            int firstCurrent = verticalAxis ? firstTouchedPoint.CurrentVerticalF26Dot6 : firstTouchedPoint.CurrentHorizontalF26Dot6;
            int secondCurrent = verticalAxis ? secondTouchedPoint.CurrentVerticalF26Dot6 : secondTouchedPoint.CurrentHorizontalF26Dot6;

            int pointIndex = NextContourPoint(firstTouchedPointIndex, contourStartPointIndex, contourEndPointIndex);
            while (pointIndex != secondTouchedPointIndex)
            {
                hintingZone.TryGetPoint(pointIndex, out TrueTypeHintingPoint point);
                int pointOriginal = verticalAxis ? point.OriginalVerticalF26Dot6 : point.OriginalHorizontalF26Dot6;
                int pointCurrent;
                if (firstOriginal == secondOriginal)
                    pointCurrent = pointOriginal + firstCurrent - firstOriginal;
                else if (pointOriginal <= Math.Min(firstOriginal, secondOriginal))
                    pointCurrent = pointOriginal + (firstOriginal < secondOriginal ? firstCurrent - firstOriginal : secondCurrent - secondOriginal);
                else if (pointOriginal >= Math.Max(firstOriginal, secondOriginal))
                    pointCurrent = pointOriginal + (firstOriginal > secondOriginal ? firstCurrent - firstOriginal : secondCurrent - secondOriginal);
                else
                    pointCurrent = firstCurrent + (int)(((long)(pointOriginal - firstOriginal) * (secondCurrent - firstCurrent)) /
                        (secondOriginal - firstOriginal));
                if (verticalAxis) point.CurrentVerticalF26Dot6 = pointCurrent;
                else point.CurrentHorizontalF26Dot6 = pointCurrent;
                pointIndex = NextContourPoint(pointIndex, contourStartPointIndex, contourEndPointIndex);
            }
        }

        private static int NextContourPoint(int pointIndex, int contourStartPointIndex, int contourEndPointIndex)
            => pointIndex >= contourEndPointIndex ? contourStartPointIndex : pointIndex + 1;

        private static int Project(int horizontalF26Dot6, int verticalF26Dot6, TrueTypeUnitVector vector)
            => (int)(((long)horizontalF26Dot6 * vector.HorizontalComponent.Value +
                (long)verticalF26Dot6 * vector.VerticalComponent.Value) / 0x4000);

        private static int DivideRounded(long numerator, long denominator)
        {
            bool negative = (numerator < 0) != (denominator < 0);
            long absoluteNumerator = Math.Abs(numerator);
            long absoluteDenominator = Math.Abs(denominator);
            long quotient = (absoluteNumerator + absoluteDenominator / 2) / absoluteDenominator;
            return (int)(negative ? -quotient : quotient);
        }

        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage)
            => new TrueTypeVirtualMachineFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}