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

        internal static TrueTypeUnitVector NormalizeUnitVector(int horizontalComponent, int verticalComponent,
            TrueTypeUnitVector zeroVectorFallback)
        {
            if (horizontalComponent == 0 && verticalComponent == 0) return zeroVectorFallback;
            double vectorLength = Math.Sqrt((long)horizontalComponent * horizontalComponent +
                (long)verticalComponent * verticalComponent);
            int normalizedHorizontalComponent = (int)Math.Round(horizontalComponent * 16384.0 / vectorLength,
                MidpointRounding.AwayFromZero);
            int normalizedVerticalComponent = (int)Math.Round(verticalComponent * 16384.0 / vectorLength,
                MidpointRounding.AwayFromZero);
            return new TrueTypeUnitVector(new TrueTypeVectorComponent(normalizedHorizontalComponent),
                new TrueTypeVectorComponent(normalizedVerticalComponent));
        }

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
                // Microsoft-compatible degenerate-line behavior ignores the perpendicular variant.
                lineUnitVector = TrueTypeUnitVector.Horizontal;
                geometryFailure = default;
                return true;
            }
            TrueTypeUnitVector parallelUnitVector = NormalizeUnitVector(horizontalDifferenceF26Dot6,
                verticalDifferenceF26Dot6, TrueTypeUnitVector.Horizontal);
            int horizontalComponent = parallelUnitVector.HorizontalComponent.Value;
            int verticalComponent = parallelUnitVector.VerticalComponent.Value;
            lineUnitVector = perpendicular
                ? new TrueTypeUnitVector(new TrueTypeVectorComponent(-verticalComponent), new TrueTypeVectorComponent(horizontalComponent))
                : new TrueTypeUnitVector(new TrueTypeVectorComponent(horizontalComponent), new TrueTypeVectorComponent(verticalComponent));
            geometryFailure = default;
            return true;
        }

        internal static bool TryMovePointByProjectedDistance(TrueTypeHintingPoint hintingPoint, int projectedDistanceF26Dot6,
            TrueTypeGraphicsState graphicsState, out TrueTypeVirtualMachineFailure geometryFailure)
            => TryMovePointByProjectedDistance(hintingPoint, projectedDistanceF26Dot6, graphicsState,
                markPointTouched: true, out geometryFailure);

        internal static bool TryMovePointByProjectedDistance(TrueTypeHintingPoint hintingPoint, int projectedDistanceF26Dot6,
            TrueTypeGraphicsState graphicsState, bool markPointTouched, out TrueTypeVirtualMachineFailure geometryFailure)
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
            if (markPointTouched)
            {
                if (graphicsState.FreedomVector.HorizontalComponent.Value != 0)
                    hintingPoint.TouchFlags |= TrueTypePointTouchFlags.Horizontal;
                if (graphicsState.FreedomVector.VerticalComponent.Value != 0)
                    hintingPoint.TouchFlags |= TrueTypePointTouchFlags.Vertical;
            }
            geometryFailure = default;
            return true;
        }

        internal static void MovePointAlongFreedomVectorDistance(TrueTypeHintingPoint hintingPoint,
            int freedomDistanceF26Dot6, TrueTypeGraphicsState graphicsState)
        {
            hintingPoint.CurrentHorizontalF26Dot6 += DivideRounded(
                (long)freedomDistanceF26Dot6 * graphicsState.FreedomVector.HorizontalComponent.Value, 0x4000);
            hintingPoint.CurrentVerticalF26Dot6 += DivideRounded(
                (long)freedomDistanceF26Dot6 * graphicsState.FreedomVector.VerticalComponent.Value, 0x4000);
            if (graphicsState.FreedomVector.HorizontalComponent.Value != 0)
                hintingPoint.TouchFlags |= TrueTypePointTouchFlags.Horizontal;
            if (graphicsState.FreedomVector.VerticalComponent.Value != 0)
                hintingPoint.TouchFlags |= TrueTypePointTouchFlags.Vertical;
        }

        internal static int RoundF26Dot6(int valueF26Dot6, TrueTypeGraphicsState graphicsState)
        {
            switch (graphicsState.RoundingMode)
            {
                case TrueTypeRoundingMode.Off: return valueF26Dot6;
                case TrueTypeRoundingMode.DownToGrid: return RoundMagnitudeToGrid(valueF26Dot6, roundMagnitudeUp: false);
                case TrueTypeRoundingMode.UpToGrid: return RoundMagnitudeToGrid(valueF26Dot6, roundMagnitudeUp: true);
                case TrueTypeRoundingMode.ToHalfGrid: return RoundToHalfGrid(valueF26Dot6);
                case TrueTypeRoundingMode.ToDoubleGrid: return RoundMagnitudeToPeriod(valueF26Dot6, 32, 16);
                case TrueTypeRoundingMode.Super:
                case TrueTypeRoundingMode.Super45:
                    return RoundSuperF26Dot6(valueF26Dot6, graphicsState.SuperRoundingState);
                default: return RoundMagnitudeToPeriod(valueF26Dot6, 64, 32);
            }
        }

        private static int RoundMagnitudeToGrid(int valueF26Dot6, bool roundMagnitudeUp)
        {
            long absoluteMagnitude = Math.Abs((long)valueF26Dot6);
            long roundedMagnitude = roundMagnitudeUp
                ? (absoluteMagnitude + 63) & ~63L
                : absoluteMagnitude & ~63L;
            return ApplyOriginalSign(valueF26Dot6, roundedMagnitude);
        }

        private static int RoundToHalfGrid(int valueF26Dot6)
        {
            long absoluteMagnitude = Math.Abs((long)valueF26Dot6);
            long roundedMagnitude = (absoluteMagnitude & ~63L) + 32;
            return ApplyOriginalSign(valueF26Dot6, roundedMagnitude);
        }

        private static int RoundMagnitudeToPeriod(int valueF26Dot6, int periodF26Dot6, int halfPeriodF26Dot6)
        {
            long absoluteMagnitude = Math.Abs((long)valueF26Dot6);
            long roundedMagnitude = ((absoluteMagnitude + halfPeriodF26Dot6) / periodF26Dot6) * periodF26Dot6;
            return ApplyOriginalSign(valueF26Dot6, roundedMagnitude);
        }

        private static int ApplyOriginalSign(int originalValue, long roundedMagnitude)
            => originalValue < 0 ? (int)-roundedMagnitude : (int)roundedMagnitude;

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
                bool firstReferenceIsLowerOrEqual = firstOriginal <= secondOriginal;
                int lowerOriginal = firstReferenceIsLowerOrEqual ? firstOriginal : secondOriginal;
                int lowerCurrent = firstReferenceIsLowerOrEqual ? firstCurrent : secondCurrent;
                int upperOriginal = firstReferenceIsLowerOrEqual ? secondOriginal : firstOriginal;
                int upperCurrent = firstReferenceIsLowerOrEqual ? secondCurrent : firstCurrent;
                if (pointOriginal <= lowerOriginal)
                    pointCurrent = pointOriginal + lowerCurrent - lowerOriginal;
                else if (pointOriginal >= upperOriginal)
                    pointCurrent = pointOriginal + upperCurrent - upperOriginal;
                else
                    pointCurrent = lowerCurrent + DivideRounded(
                        (long)(pointOriginal - lowerOriginal) * (upperCurrent - lowerCurrent), upperOriginal - lowerOriginal);
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

        internal static int MultiplyDivideRounded(int multiplicand, int multiplier, int divisor)
            => DivideRounded((long)multiplicand * multiplier, divisor);

        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage)
            => new TrueTypeVirtualMachineFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}