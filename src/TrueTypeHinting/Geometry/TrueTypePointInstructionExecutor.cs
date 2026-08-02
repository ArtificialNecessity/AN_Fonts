using System;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;

namespace StbTrueTypeSharp.TrueTypeHinting.Geometry
{
    /// <summary>Point-addressing TrueType instructions over twilight and simple-glyph zones.</summary>
    internal static class TrueTypePointInstructionExecutor
    {
        internal static bool ExecuteMoveDirectAbsolutePoint(bool round, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int pointIndex, out failure) ||
                !TryPoint(context, context.VirtualMachineState.GraphicsState.ZonePointerZero, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            int current = TrueTypeHintingGeometryOperations.ProjectCurrent(point, state.ProjectionVector);
            int target = round ? TrueTypeHintingGeometryOperations.RoundF26Dot6(current, state) : current;
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, target - current, state, out failure)) return false;
            state.ReferencePointZero = state.ReferencePointOne = new TrueTypeReferencePointIndex(pointIndex);
            return Success(out failure);
        }

        internal static bool ExecuteMoveIndirectAbsolutePoint(bool round, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int controlValueIndex, out failure) ||
                !context.OperandStack.TryPop(out int pointIndex, out failure) ||
                !context.VirtualMachineState.TryReadControlValue(controlValueIndex, out int controlValue, out failure) ||
                !TryPoint(context, context.VirtualMachineState.GraphicsState.ZonePointerZero, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            int current = TrueTypeHintingGeometryOperations.ProjectCurrent(point, state.ProjectionVector);
            if (state.ZonePointerZero.Value == 0)
            {
                if (!TryInitializeTwilightPointAbsolute(point, controlValue, state, out failure)) return false;
                current = TrueTypeHintingGeometryOperations.ProjectCurrent(point, state.ProjectionVector);
            }
            int target = controlValue;
            if (round)
            {
                if (Math.Abs(controlValue - current) > state.ControlValueCutInF26Dot6) target = current;
                target = TrueTypeHintingGeometryOperations.RoundF26Dot6(target, state);
            }
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, target - current, state, out failure)) return false;
            state.ReferencePointZero = state.ReferencePointOne = new TrueTypeReferencePointIndex(pointIndex);
            return Success(out failure);
        }

        internal static bool ExecuteMoveDirectRelativePoint(byte opcode, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int pointIndex, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerOne, pointIndex, out TrueTypeHintingPoint point, out failure) ||
                !TryPoint(context, state.ZonePointerZero, state.ReferencePointZero.Value, out TrueTypeHintingPoint referencePoint, out failure)) return false;
            int originalDistance = TrueTypeHintingGeometryOperations.ProjectDistanceOriginal(point, referencePoint, state.DualProjectionVector);
            int currentDistance = TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(point, referencePoint, state.ProjectionVector);
            int targetDistance = ApplySingleWidthCutIn(originalDistance, state);
            targetDistance = ApplyRelativeRoundingAndMinimumDistance(targetDistance, originalDistance, opcode, state);
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, targetDistance - currentDistance, state, out failure)) return false;
            UpdateRelativeReferencePoints(state, pointIndex, opcode);
            return Success(out failure);
        }

        internal static bool ExecuteMoveIndirectRelativePoint(byte opcode, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int controlValueIndex, out failure) ||
                !context.OperandStack.TryPop(out int pointIndex, out failure) ||
                !context.VirtualMachineState.TryReadControlValue(controlValueIndex, out int controlValueDistance, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerOne, pointIndex, out TrueTypeHintingPoint point, out failure) ||
                !TryPoint(context, state.ZonePointerZero, state.ReferencePointZero.Value, out TrueTypeHintingPoint referencePoint, out failure)) return false;
            int targetDistance = ApplySingleWidthCutIn(controlValueDistance, state);
            if (state.ZonePointerOne.Value == 0 &&
                !TryInitializeTwilightPointRelative(point, referencePoint, targetDistance, state, out failure)) return false;
            int originalDistance = TrueTypeHintingGeometryOperations.ProjectDistanceOriginal(point, referencePoint, state.DualProjectionVector);
            int currentDistance = TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(point, referencePoint, state.ProjectionVector);
            if (state.AutoFlip && ((originalDistance < 0) != (targetDistance < 0))) targetDistance = -targetDistance;
            bool roundAndUseControlValueCutIn = (opcode & 0x04) != 0;
            if (roundAndUseControlValueCutIn && state.ZonePointerZero.Value == state.ZonePointerOne.Value &&
                Math.Abs(targetDistance - originalDistance) > state.ControlValueCutInF26Dot6)
                targetDistance = originalDistance;
            targetDistance = ApplyRelativeRoundingAndMinimumDistance(targetDistance, originalDistance, opcode, state);
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, targetDistance - currentDistance, state, out failure)) return false;
            UpdateRelativeReferencePoints(state, pointIndex, opcode);
            return Success(out failure);
        }

        internal static bool ExecuteMoveStackIndirectRelativePoint(bool setReferencePointZero, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int distance, out failure) || !context.OperandStack.TryPop(out int pointIndex, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerOne, pointIndex, out TrueTypeHintingPoint point, out failure) ||
                !TryPoint(context, state.ZonePointerZero, state.ReferencePointZero.Value, out TrueTypeHintingPoint referencePoint, out failure)) return false;
            if (state.ZonePointerOne.Value == 0 &&
                !TryInitializeTwilightPointRelative(point, referencePoint, distance, state, out failure)) return false;
            int currentDistance = TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(point, referencePoint, state.ProjectionVector);
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, distance - currentDistance, state, out failure)) return false;
            state.ReferencePointOne = state.ReferencePointZero;
            state.ReferencePointTwo = new TrueTypeReferencePointIndex(pointIndex);
            if (setReferencePointZero) state.ReferencePointZero = new TrueTypeReferencePointIndex(pointIndex);
            return Success(out failure);
        }

        internal static bool ExecuteSetVectorToLine(bool setProjectionVector, bool setDualProjectionVector,
            bool perpendicular, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int firstPointIndex, out failure) ||
                !context.OperandStack.TryPop(out int secondPointIndex, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerTwo, firstPointIndex, out TrueTypeHintingPoint firstPoint, out failure) ||
                !TryPoint(context, state.ZonePointerOne, secondPointIndex, out TrueTypeHintingPoint secondPoint, out failure) ||
                !TrueTypeHintingGeometryOperations.TryCreateCurrentLineUnitVector(firstPoint, secondPoint, perpendicular,
                    out TrueTypeUnitVector currentLineVector, out failure)) return false;

            if (setDualProjectionVector)
            {
                if (!TrueTypeHintingGeometryOperations.TryCreateOriginalLineUnitVector(firstPoint, secondPoint, perpendicular,
                        out TrueTypeUnitVector originalLineVector, out failure)) return false;
                state.ProjectionVector = currentLineVector;
                state.DualProjectionVector = originalLineVector;
            }
            else if (setProjectionVector)
            {
                state.ProjectionVector = currentLineVector;
                state.DualProjectionVector = currentLineVector;
            }
            else
            {
                state.FreedomVector = currentLineVector;
            }
            return Success(out failure);
        }

        internal static bool ExecuteAlignPoints(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int zoneOnePointIndex, out failure) ||
                !context.OperandStack.TryPop(out int zoneZeroPointIndex, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerOne, zoneOnePointIndex, out TrueTypeHintingPoint zoneOnePoint, out failure) ||
                !TryPoint(context, state.ZonePointerZero, zoneZeroPointIndex, out TrueTypeHintingPoint zoneZeroPoint, out failure)) return false;

            int halfProjectedDistance = TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(
                zoneZeroPoint, zoneOnePoint, state.ProjectionVector) / 2;
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(
                    zoneOnePoint, halfProjectedDistance, state, out failure) ||
                !TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(
                    zoneZeroPoint, -halfProjectedDistance, state, out failure)) return false;
            return Success(out failure);
        }

        internal static bool ExecuteIntersectLines(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int lineAFirstPointIndex, out failure) ||
                !context.OperandStack.TryPop(out int lineASecondPointIndex, out failure) ||
                !context.OperandStack.TryPop(out int lineBFirstPointIndex, out failure) ||
                !context.OperandStack.TryPop(out int lineBSecondPointIndex, out failure) ||
                !context.OperandStack.TryPop(out int movedPointIndex, out failure)) return false;
            TrueTypeGraphicsState graphicsState = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, graphicsState.ZonePointerOne, lineAFirstPointIndex,
                    out TrueTypeHintingPoint lineAFirstPoint, out failure) ||
                !TryPoint(context, graphicsState.ZonePointerOne, lineASecondPointIndex,
                    out TrueTypeHintingPoint lineASecondPoint, out failure) ||
                !TryPoint(context, graphicsState.ZonePointerZero, lineBFirstPointIndex,
                    out TrueTypeHintingPoint lineBFirstPoint, out failure) ||
                !TryPoint(context, graphicsState.ZonePointerZero, lineBSecondPointIndex,
                    out TrueTypeHintingPoint lineBSecondPoint, out failure) ||
                !TryPoint(context, graphicsState.ZonePointerTwo, movedPointIndex,
                    out TrueTypeHintingPoint movedPoint, out failure)) return false;

            long lineADeltaHorizontal = (long)lineASecondPoint.CurrentHorizontalF26Dot6 -
                lineAFirstPoint.CurrentHorizontalF26Dot6;
            long lineADeltaVertical = (long)lineASecondPoint.CurrentVerticalF26Dot6 -
                lineAFirstPoint.CurrentVerticalF26Dot6;
            long lineBDeltaHorizontal = (long)lineBSecondPoint.CurrentHorizontalF26Dot6 -
                lineBFirstPoint.CurrentHorizontalF26Dot6;
            long lineBDeltaVertical = (long)lineBSecondPoint.CurrentVerticalF26Dot6 -
                lineBFirstPoint.CurrentVerticalF26Dot6;
            decimal lineCrossProduct = (decimal)lineADeltaHorizontal * lineBDeltaVertical -
                (decimal)lineADeltaVertical * lineBDeltaHorizontal;
            decimal lineDotProduct = (decimal)lineADeltaHorizontal * lineBDeltaHorizontal +
                (decimal)lineADeltaVertical * lineBDeltaVertical;
            bool linesHaveStableIntersection = lineCrossProduct != 0 &&
                19m * Math.Abs(lineCrossProduct) > Math.Abs(lineDotProduct);

            int intersectionHorizontalF26Dot6;
            int intersectionVerticalF26Dot6;
            if (!linesHaveStableIntersection)
            {
                intersectionHorizontalF26Dot6 = DivideByFourTruncated(
                    (long)lineAFirstPoint.CurrentHorizontalF26Dot6 + lineASecondPoint.CurrentHorizontalF26Dot6 +
                    lineBFirstPoint.CurrentHorizontalF26Dot6 + lineBSecondPoint.CurrentHorizontalF26Dot6);
                intersectionVerticalF26Dot6 = DivideByFourTruncated(
                    (long)lineAFirstPoint.CurrentVerticalF26Dot6 + lineASecondPoint.CurrentVerticalF26Dot6 +
                    lineBFirstPoint.CurrentVerticalF26Dot6 + lineBSecondPoint.CurrentVerticalF26Dot6);
            }
            else
            {
                long lineOriginDeltaHorizontal = (long)lineBFirstPoint.CurrentHorizontalF26Dot6 -
                    lineAFirstPoint.CurrentHorizontalF26Dot6;
                long lineOriginDeltaVertical = (long)lineBFirstPoint.CurrentVerticalF26Dot6 -
                    lineAFirstPoint.CurrentVerticalF26Dot6;
                decimal lineAParameterNumerator = (decimal)lineOriginDeltaHorizontal * lineBDeltaVertical -
                    (decimal)lineOriginDeltaVertical * lineBDeltaHorizontal;
                decimal intersectionHorizontal = lineAFirstPoint.CurrentHorizontalF26Dot6 +
                    lineAParameterNumerator * lineADeltaHorizontal / lineCrossProduct;
                decimal intersectionVertical = lineAFirstPoint.CurrentVerticalF26Dot6 +
                    lineAParameterNumerator * lineADeltaVertical / lineCrossProduct;
                if (!TryRoundIntersectionCoordinate(intersectionHorizontal, out intersectionHorizontalF26Dot6) ||
                    !TryRoundIntersectionCoordinate(intersectionVertical, out intersectionVerticalF26Dot6))
                {
                    failure = Failure(TrueTypeVirtualMachineFailureCode.InvalidGraphicsStateValue,
                        "ISECT produced a coordinate outside the supported F26Dot6 range.");
                    return false;
                }
            }

            movedPoint.CurrentHorizontalF26Dot6 = intersectionHorizontalF26Dot6;
            movedPoint.CurrentVerticalF26Dot6 = intersectionVerticalF26Dot6;
            movedPoint.TouchFlags |= TrueTypePointTouchFlags.Horizontal | TrueTypePointTouchFlags.Vertical;
            return Success(out failure);
        }

        private static int DivideByFourTruncated(long value) => (int)(value / 4);

        private static bool TryRoundIntersectionCoordinate(decimal coordinate, out int roundedCoordinate)
        {
            decimal rounded = Math.Round(coordinate, 0, MidpointRounding.AwayFromZero);
            if (rounded < int.MinValue || rounded > int.MaxValue) { roundedCoordinate = 0; return false; }
            roundedCoordinate = (int)rounded;
            return true;
        }

        internal static bool ExecuteGetCoordinate(bool original, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int pointIndex, out failure) ||
                !TryPoint(context, context.VirtualMachineState.GraphicsState.ZonePointerTwo, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
            TrueTypeGraphicsState graphicsState = context.VirtualMachineState.GraphicsState;
            int coordinate = original
                ? TrueTypeHintingGeometryOperations.ProjectOriginal(point, graphicsState.DualProjectionVector)
                : TrueTypeHintingGeometryOperations.ProjectCurrent(point, graphicsState.ProjectionVector);
            return context.OperandStack.TryPush(coordinate, out failure);
        }

        internal static bool ExecuteSetCoordinateFromStack(TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int coordinate, out failure) || !context.OperandStack.TryPop(out int pointIndex, out failure) ||
                !TryPoint(context, context.VirtualMachineState.GraphicsState.ZonePointerTwo, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            int current = TrueTypeHintingGeometryOperations.ProjectCurrent(point, state.ProjectionVector);
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, coordinate - current, state, out failure)) return false;
            if (state.ZonePointerTwo.Value == 0)
            {
                point.OriginalHorizontalF26Dot6 = point.CurrentHorizontalF26Dot6;
                point.OriginalVerticalF26Dot6 = point.CurrentVerticalF26Dot6;
            }
            return Success(out failure);
        }

        internal static bool ExecuteMeasureDistance(bool original, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int zoneZeroPointIndex, out failure) ||
                !context.OperandStack.TryPop(out int zoneOnePointIndex, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerOne, zoneOnePointIndex, out TrueTypeHintingPoint zoneOnePoint, out failure) ||
                !TryPoint(context, state.ZonePointerZero, zoneZeroPointIndex, out TrueTypeHintingPoint zoneZeroPoint, out failure)) return false;
            int distance = original
                ? TrueTypeHintingGeometryOperations.ProjectDistanceOriginal(zoneOnePoint, zoneZeroPoint, state.DualProjectionVector)
                : TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(zoneOnePoint, zoneZeroPoint, state.ProjectionVector);
            return context.OperandStack.TryPush(distance, out failure);
        }

        internal static bool ExecuteInterpolatePoints(TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerZero, state.ReferencePointOne.Value, out TrueTypeHintingPoint firstReferencePoint, out failure) ||
                !TryPoint(context, state.ZonePointerOne, state.ReferencePointTwo.Value, out TrueTypeHintingPoint secondReferencePoint, out failure)) return false;
            int originalFirst = TrueTypeHintingGeometryOperations.ProjectOriginal(firstReferencePoint, state.DualProjectionVector);
            int originalSecond = TrueTypeHintingGeometryOperations.ProjectOriginal(secondReferencePoint, state.DualProjectionVector);
            int currentFirst = TrueTypeHintingGeometryOperations.ProjectCurrent(firstReferencePoint, state.ProjectionVector);
            int currentSecond = TrueTypeHintingGeometryOperations.ProjectCurrent(secondReferencePoint, state.ProjectionVector);
            int originalReferenceRange = originalSecond - originalFirst;
            int currentReferenceRange = currentSecond - currentFirst;
            int loopCount = state.LoopCount.Value;
            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                if (!context.OperandStack.TryPop(out int pointIndex, out failure) || !TryPoint(context, state.ZonePointerTwo, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
                int originalPointDistance = TrueTypeHintingGeometryOperations.ProjectOriginal(point, state.DualProjectionVector) - originalFirst;
                int currentPointDistance = TrueTypeHintingGeometryOperations.ProjectCurrent(point, state.ProjectionVector) - currentFirst;
                int targetPointDistance = originalPointDistance == 0
                    ? 0
                    : originalReferenceRange == 0
                        ? originalPointDistance
                        : TrueTypeHintingGeometryOperations.MultiplyDivideRounded(
                            originalPointDistance, currentReferenceRange, originalReferenceRange);
                if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(
                        point, targetPointDistance - currentPointDistance, state, out failure)) return false;
            }
            state.LoopCount = new TrueTypeLoopCount(1);
            return Success(out failure);
        }

        internal static bool ExecuteAlignRelativePoints(TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerZero, state.ReferencePointZero.Value, out TrueTypeHintingPoint referencePoint, out failure)) return false;
            int loopCount = state.LoopCount.Value;
            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                if (!context.OperandStack.TryPop(out int pointIndex, out failure) || !TryPoint(context, state.ZonePointerOne, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
                int distance = TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(point, referencePoint, state.ProjectionVector);
                if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, -distance, state, out failure)) return false;
            }
            state.LoopCount = new TrueTypeLoopCount(1);
            return Success(out failure);
        }

        internal static bool ExecuteShiftPoints(bool useReferencePointOne, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryGetReferencePointShift(context, useReferencePointOne, out _, out _, out int shift, out failure)) return false;
            return ShiftLoopPoints(context, shift, out failure);
        }

        internal static bool ExecuteShiftContour(bool useReferencePointOne, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int contourIndex, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!context.ExecutionZones.TryGetZone(state.ZonePointerTwo, out TrueTypeHintingZone targetZone, out failure)) return false;
            if (!targetZone.TryGetContourPointRange(contourIndex, out int firstPointIndex, out int lastPointIndex))
            {
                failure = Failure(TrueTypeVirtualMachineFailureCode.InvalidContourIndex,
                    "The TrueType contour index lies outside the selected zone.");
                return false;
            }
            if (!TryGetReferencePointShift(context, useReferencePointOne, out TrueTypeZonePointerIndex referenceZone,
                    out int referencePointIndex, out int shift, out failure)) return false;

            for (int pointIndex = firstPointIndex; pointIndex <= lastPointIndex; pointIndex++)
            {
                if (referenceZone.Value == state.ZonePointerTwo.Value && pointIndex == referencePointIndex) continue;
                if (!targetZone.TryGetPoint(pointIndex, out TrueTypeHintingPoint point) ||
                    !TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(
                        point, shift, state, markPointTouched: true, out failure)) return false;
            }
            return Success(out failure);
        }

        internal static bool ExecuteShiftZone(bool useReferencePointOne, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int targetZoneIndex, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            var targetZonePointer = new TrueTypeZonePointerIndex(targetZoneIndex);
            if (!context.ExecutionZones.TryGetZone(targetZonePointer, out TrueTypeHintingZone targetZone, out failure) ||
                !TryGetReferencePointShift(context, useReferencePointOne, out TrueTypeZonePointerIndex referenceZone,
                    out int referencePointIndex, out int shift, out failure)) return false;

            int shiftedPointCount = targetZoneIndex == 1 ? targetZone.OutlinePointCount : targetZone.PointCount;
            for (int pointIndex = 0; pointIndex < shiftedPointCount; pointIndex++)
            {
                if (referenceZone.Value == targetZoneIndex && pointIndex == referencePointIndex) continue;
                if (!targetZone.TryGetPoint(pointIndex, out TrueTypeHintingPoint point) ||
                    !TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(
                        point, shift, state, markPointTouched: false, out failure)) return false;
            }
            return Success(out failure);
        }

        private static bool TryGetReferencePointShift(TrueTypeExecutionContext context, bool useReferencePointOne,
            out TrueTypeZonePointerIndex referenceZone, out int referencePointIndex, out int shift,
            out TrueTypeVirtualMachineFailure failure)
        {
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            TrueTypeReferencePointIndex referencePoint = useReferencePointOne ? state.ReferencePointOne : state.ReferencePointTwo;
            referenceZone = useReferencePointOne ? state.ZonePointerZero : state.ZonePointerOne;
            referencePointIndex = referencePoint.Value;
            if (!TryPoint(context, referenceZone, referencePointIndex, out TrueTypeHintingPoint point, out failure))
            {
                shift = 0;
                return false;
            }
            shift = TrueTypeHintingGeometryOperations.ProjectCurrent(point, state.ProjectionVector) -
                TrueTypeHintingGeometryOperations.ProjectOriginal(point, state.DualProjectionVector);
            return true;
        }

        internal static bool ExecuteShiftPointsByPixelAmount(TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int shift, out failure)) return false;
            TrueTypeGraphicsState graphicsState = context.VirtualMachineState.GraphicsState;
            int loopCount = graphicsState.LoopCount.Value;
            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                if (!context.OperandStack.TryPop(out int pointIndex, out failure) ||
                    !TryPoint(context, graphicsState.ZonePointerTwo, pointIndex,
                        out TrueTypeHintingPoint point, out failure)) return false;
                TrueTypeHintingGeometryOperations.MovePointAlongFreedomVectorDistance(
                    point, shift, graphicsState);
            }
            graphicsState.LoopCount = new TrueTypeLoopCount(1);
            return Success(out failure);
        }

        internal static bool ExecuteUntouchPoint(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int pointIndex, out failure)) return false;
            TrueTypeGraphicsState graphicsState = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, graphicsState.ZonePointerZero, pointIndex,
                    out TrueTypeHintingPoint hintingPoint, out failure)) return false;
            if (graphicsState.FreedomVector.HorizontalComponent.Value != 0)
                hintingPoint.TouchFlags &= ~TrueTypePointTouchFlags.Horizontal;
            if (graphicsState.FreedomVector.VerticalComponent.Value != 0)
                hintingPoint.TouchFlags &= ~TrueTypePointTouchFlags.Vertical;
            return Success(out failure);
        }

        internal static bool ExecuteFlipPoints(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            TrueTypeGraphicsState graphicsState = context.VirtualMachineState.GraphicsState;
            int loopCount = graphicsState.LoopCount.Value;
            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                if (!context.OperandStack.TryPop(out int pointIndex, out failure) ||
                    !TryPoint(context, graphicsState.ZonePointerZero, pointIndex,
                        out TrueTypeHintingPoint hintingPoint, out failure)) return false;
                hintingPoint.IsOnCurve = !hintingPoint.IsOnCurve;
            }
            graphicsState.LoopCount = new TrueTypeLoopCount(1);
            return Success(out failure);
        }

        internal static bool ExecuteSetPointRangeCurveState(bool onCurve, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int highPointIndex, out failure) ||
                !context.OperandStack.TryPop(out int lowPointIndex, out failure)) return false;
            if (lowPointIndex < 0 || highPointIndex < lowPointIndex)
            {
                failure = Failure(TrueTypeVirtualMachineFailureCode.InvalidPointIndex,
                    "The TrueType point range must have non-negative ordered endpoints.");
                return false;
            }
            TrueTypeZonePointerIndex targetZonePointer = context.VirtualMachineState.GraphicsState.ZonePointerZero;
            if (!context.ExecutionZones.TryGetZone(targetZonePointer, out TrueTypeHintingZone targetZone, out failure) ||
                highPointIndex >= targetZone.PointCount)
            {
                if (!failure.HasFailure)
                    failure = Failure(TrueTypeVirtualMachineFailureCode.InvalidPointIndex,
                        "The TrueType point range lies outside the selected zone.");
                return false;
            }
            for (int pointIndex = lowPointIndex; pointIndex <= highPointIndex; pointIndex++)
            {
                targetZone.TryGetPoint(pointIndex, out TrueTypeHintingPoint hintingPoint);
                hintingPoint.IsOnCurve = onCurve;
            }
            return Success(out failure);
        }

        internal static bool ExecuteDeltaPoints(TrueTypeExecutionContext context, int ppemBias, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int exceptionCount, out failure)) return false;
            if (exceptionCount < 0 || exceptionCount > context.OperandStack.OperandCount / 2)
            {
                failure = Failure(TrueTypeVirtualMachineFailureCode.OperandStackUnderflow, "DELTAP count exceeds available pairs.");
                return false;
            }
            for (int exceptionIndex = 0; exceptionIndex < exceptionCount; exceptionIndex++)
            {
                if (!context.OperandStack.TryPop(out int pointIndex, out failure) || !context.OperandStack.TryPop(out int argument, out failure)) return false;
                TrueTypeGraphicsState graphicsState = context.VirtualMachineState.GraphicsState;
                int targetPpem = ((argument >> 4) & 15) + graphicsState.DeltaBasePpem + ppemBias;
                if (targetPpem != context.VirtualMachineState.RasterizerEnvironment.DevicePpemY.Value) continue;
                int distance = TrueTypeDeltaState.DecodeDistanceF26Dot6(argument, graphicsState.DeltaShift);
                if (!TryPoint(context, graphicsState.ZonePointerZero, pointIndex, out TrueTypeHintingPoint point, out failure) ||
                    !TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, distance, graphicsState, out failure)) return false;
            }
            return Success(out failure);
        }

        private static bool ShiftLoopPoints(TrueTypeExecutionContext context, int shift, out TrueTypeVirtualMachineFailure failure)
        {
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            int loopCount = state.LoopCount.Value;
            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                if (!context.OperandStack.TryPop(out int pointIndex, out failure) || !TryPoint(context, state.ZonePointerTwo, pointIndex, out TrueTypeHintingPoint point, out failure) ||
                    !TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, shift, state, out failure)) return false;
            }
            state.LoopCount = new TrueTypeLoopCount(1);
            return Success(out failure);
        }

        private static int ApplySingleWidthCutIn(int candidateDistanceF26Dot6, TrueTypeGraphicsState state)
        {
            if (state.SingleWidthCutInF26Dot6 <= 0 ||
                Math.Abs(Math.Abs(candidateDistanceF26Dot6) - state.SingleWidthValueF26Dot6) >= state.SingleWidthCutInF26Dot6)
                return candidateDistanceF26Dot6;
            return candidateDistanceF26Dot6 < 0 ? -state.SingleWidthValueF26Dot6 : state.SingleWidthValueF26Dot6;
        }

        private static int ApplyRelativeRoundingAndMinimumDistance(int candidateDistance, int originalDistance,
            byte opcode, TrueTypeGraphicsState state)
        {
            int target = candidateDistance;
            bool round = (opcode & 0x04) != 0;
            if (round) target = TrueTypeHintingGeometryOperations.RoundF26Dot6(target, state);
            if ((opcode & 0x08) != 0 && Math.Abs(target) < state.MinimumDistanceF26Dot6)
                target = originalDistance < 0 ? -state.MinimumDistanceF26Dot6 : state.MinimumDistanceF26Dot6;
            return target;
        }

        private static void UpdateRelativeReferencePoints(TrueTypeGraphicsState state, int pointIndex, byte opcode)
        {
            state.ReferencePointOne = state.ReferencePointZero;
            state.ReferencePointTwo = new TrueTypeReferencePointIndex(pointIndex);
            if ((opcode & 0x10) != 0) state.ReferencePointZero = new TrueTypeReferencePointIndex(pointIndex);
        }

        private static bool TryInitializeTwilightPointAbsolute(TrueTypeHintingPoint point,
            int projectedCoordinateF26Dot6, TrueTypeGraphicsState graphicsState,
            out TrueTypeVirtualMachineFailure failure)
        {
            point.OriginalHorizontalF26Dot6 = point.CurrentHorizontalF26Dot6 = 0;
            point.OriginalVerticalF26Dot6 = point.CurrentVerticalF26Dot6 = 0;
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(
                    point, projectedCoordinateF26Dot6, graphicsState, out failure)) return false;
            point.OriginalHorizontalF26Dot6 = point.CurrentHorizontalF26Dot6;
            point.OriginalVerticalF26Dot6 = point.CurrentVerticalF26Dot6;
            return true;
        }

        private static bool TryInitializeTwilightPointRelative(TrueTypeHintingPoint point,
            TrueTypeHintingPoint referencePoint, int projectedDistanceF26Dot6,
            TrueTypeGraphicsState graphicsState, out TrueTypeVirtualMachineFailure failure)
        {
            point.OriginalHorizontalF26Dot6 = point.CurrentHorizontalF26Dot6 =
                referencePoint.OriginalHorizontalF26Dot6;
            point.OriginalVerticalF26Dot6 = point.CurrentVerticalF26Dot6 =
                referencePoint.OriginalVerticalF26Dot6;
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(
                    point, projectedDistanceF26Dot6, graphicsState, out failure)) return false;
            point.OriginalHorizontalF26Dot6 = point.CurrentHorizontalF26Dot6;
            point.OriginalVerticalF26Dot6 = point.CurrentVerticalF26Dot6;
            return true;
        }

        private static bool TryPoint(TrueTypeExecutionContext context, TrueTypeZonePointerIndex zone, int pointIndex, out TrueTypeHintingPoint point, out TrueTypeVirtualMachineFailure failure)
            => TrueTypeHintingGeometryOperations.TryGetPoint(context.ExecutionZones, zone, pointIndex, out point, out failure);

        private static bool Success(out TrueTypeVirtualMachineFailure failure) { failure = default; return true; }
        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode code, string message)
            => new TrueTypeVirtualMachineFailure(code, new TrueTypeHintingFailureMessage(message));
    }
}