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
            int target = round ? TrueTypeHintingGeometryOperations.RoundF26Dot6(current, state.RoundingMode) : current;
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
                SetTwilightPointFromProjection(point, controlValue, state.ProjectionVector);
                current = controlValue;
            }
            int target = controlValue;
            if (round)
            {
                if (Math.Abs(controlValue - current) > state.ControlValueCutInF26Dot6) target = current;
                target = TrueTypeHintingGeometryOperations.RoundF26Dot6(target, state.RoundingMode);
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
            int targetDistance = ApplyRelativeDistanceRules(originalDistance, originalDistance, opcode, state);
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
            int originalDistance = TrueTypeHintingGeometryOperations.ProjectDistanceOriginal(point, referencePoint, state.DualProjectionVector);
            int currentDistance = TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(point, referencePoint, state.ProjectionVector);
            if (state.AutoFlip && ((originalDistance < 0) != (controlValueDistance < 0))) controlValueDistance = -controlValueDistance;
            int targetDistance = ApplyRelativeDistanceRules(controlValueDistance, originalDistance, opcode, state);
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
            int currentDistance = TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(point, referencePoint, state.ProjectionVector);
            if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, distance - currentDistance, state, out failure)) return false;
            state.ReferencePointOne = state.ReferencePointZero;
            state.ReferencePointTwo = new TrueTypeReferencePointIndex(pointIndex);
            if (setReferencePointZero) state.ReferencePointZero = new TrueTypeReferencePointIndex(pointIndex);
            return Success(out failure);
        }

        internal static bool ExecuteGetCoordinate(bool original, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int pointIndex, out failure) ||
                !TryPoint(context, context.VirtualMachineState.GraphicsState.ZonePointerTwo, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
            int coordinate = original
                ? TrueTypeHintingGeometryOperations.ProjectOriginal(point, context.VirtualMachineState.GraphicsState.ProjectionVector)
                : TrueTypeHintingGeometryOperations.ProjectCurrent(point, context.VirtualMachineState.GraphicsState.ProjectionVector);
            return context.OperandStack.TryPush(coordinate, out failure);
        }

        internal static bool ExecuteSetCoordinateFromStack(TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int coordinate, out failure) || !context.OperandStack.TryPop(out int pointIndex, out failure) ||
                !TryPoint(context, context.VirtualMachineState.GraphicsState.ZonePointerTwo, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            int current = TrueTypeHintingGeometryOperations.ProjectCurrent(point, state.ProjectionVector);
            return TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, coordinate - current, state, out failure);
        }

        internal static bool ExecuteMeasureDistance(bool original, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int secondPointIndex, out failure) || !context.OperandStack.TryPop(out int firstPointIndex, out failure)) return false;
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerOne, firstPointIndex, out TrueTypeHintingPoint firstPoint, out failure) ||
                !TryPoint(context, state.ZonePointerZero, secondPointIndex, out TrueTypeHintingPoint secondPoint, out failure)) return false;
            int distance = original
                ? TrueTypeHintingGeometryOperations.ProjectDistanceOriginal(firstPoint, secondPoint, state.DualProjectionVector)
                : TrueTypeHintingGeometryOperations.ProjectDistanceCurrent(firstPoint, secondPoint, state.ProjectionVector);
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
            int loopCount = Math.Max(1, state.LoopCount.Value);
            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                if (!context.OperandStack.TryPop(out int pointIndex, out failure) || !TryPoint(context, state.ZonePointerTwo, pointIndex, out TrueTypeHintingPoint point, out failure)) return false;
                int originalPoint = TrueTypeHintingGeometryOperations.ProjectOriginal(point, state.DualProjectionVector);
                int target = originalSecond == originalFirst ? currentFirst : currentFirst +
                    (int)(((long)(originalPoint - originalFirst) * (currentSecond - currentFirst)) / (originalSecond - originalFirst));
                int current = TrueTypeHintingGeometryOperations.ProjectCurrent(point, state.ProjectionVector);
                if (!TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, target - current, state, out failure)) return false;
            }
            state.LoopCount = new TrueTypeLoopCount(1);
            return Success(out failure);
        }

        internal static bool ExecuteAlignRelativePoints(TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            if (!TryPoint(context, state.ZonePointerZero, state.ReferencePointZero.Value, out TrueTypeHintingPoint referencePoint, out failure)) return false;
            int loopCount = Math.Max(1, state.LoopCount.Value);
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
            TrueTypeReferencePointIndex referencePointIndex = useReferencePointOne ? state.ReferencePointOne : state.ReferencePointTwo;
            TrueTypeZonePointerIndex referenceZone = useReferencePointOne ? state.ZonePointerZero : state.ZonePointerOne;
            if (!TryPoint(context, referenceZone, referencePointIndex.Value, out TrueTypeHintingPoint referencePoint, out failure)) return false;
            int shift = TrueTypeHintingGeometryOperations.ProjectCurrent(referencePoint, state.ProjectionVector) -
                TrueTypeHintingGeometryOperations.ProjectOriginal(referencePoint, state.DualProjectionVector);
            return ShiftLoopPoints(context, shift, out failure);
        }

        internal static bool ExecuteShiftPointsByPixelAmount(TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int shift, out failure)) return false;
            return ShiftLoopPoints(context, shift, out failure);
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
                int targetPpem = ((argument >> 4) & 15) + context.VirtualMachineState.GraphicsState.DeltaBasePpem + ppemBias;
                if (targetPpem != context.VirtualMachineState.RasterizerEnvironment.DevicePpemY.Value) continue;
                int step = (argument & 15) - 8; if (step >= 0) step++;
                int distance = step * (1 << Math.Max(0, 6 - context.VirtualMachineState.GraphicsState.DeltaShift));
                if (!TryPoint(context, context.VirtualMachineState.GraphicsState.ZonePointerZero, pointIndex, out TrueTypeHintingPoint point, out failure) ||
                    !TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, distance, context.VirtualMachineState.GraphicsState, out failure)) return false;
            }
            return Success(out failure);
        }

        private static bool ShiftLoopPoints(TrueTypeExecutionContext context, int shift, out TrueTypeVirtualMachineFailure failure)
        {
            TrueTypeGraphicsState state = context.VirtualMachineState.GraphicsState;
            int loopCount = Math.Max(1, state.LoopCount.Value);
            for (int loopIndex = 0; loopIndex < loopCount; loopIndex++)
            {
                if (!context.OperandStack.TryPop(out int pointIndex, out failure) || !TryPoint(context, state.ZonePointerTwo, pointIndex, out TrueTypeHintingPoint point, out failure) ||
                    !TrueTypeHintingGeometryOperations.TryMovePointByProjectedDistance(point, shift, state, out failure)) return false;
            }
            state.LoopCount = new TrueTypeLoopCount(1);
            return Success(out failure);
        }

        private static int ApplyRelativeDistanceRules(int candidateDistance, int originalDistance, byte opcode, TrueTypeGraphicsState state)
        {
            int target = candidateDistance;
            bool round = (opcode & 0x04) != 0;
            if (round) target = TrueTypeHintingGeometryOperations.RoundF26Dot6(target, state.RoundingMode);
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

        private static void SetTwilightPointFromProjection(TrueTypeHintingPoint point, int projectionCoordinate, TrueTypeUnitVector projectionVector)
        {
            point.OriginalHorizontalF26Dot6 = point.CurrentHorizontalF26Dot6 =
                (int)(((long)projectionCoordinate * projectionVector.HorizontalComponent.Value) / 0x4000);
            point.OriginalVerticalF26Dot6 = point.CurrentVerticalF26Dot6 =
                (int)(((long)projectionCoordinate * projectionVector.VerticalComponent.Value) / 0x4000);
        }

        private static bool TryPoint(TrueTypeExecutionContext context, TrueTypeZonePointerIndex zone, int pointIndex, out TrueTypeHintingPoint point, out TrueTypeVirtualMachineFailure failure)
            => TrueTypeHintingGeometryOperations.TryGetPoint(context.ExecutionZones, zone, pointIndex, out point, out failure);

        private static bool Success(out TrueTypeVirtualMachineFailure failure) { failure = default; return true; }
        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode code, string message)
            => new TrueTypeVirtualMachineFailure(code, new TrueTypeHintingFailureMessage(message));
    }
}