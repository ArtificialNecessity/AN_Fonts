using System;
using System.Collections.Generic;
using StbTrueTypeSharp.TrueTypeHinting.Geometry;

namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    internal readonly struct TrueTypeOperandValue
    {
        internal TrueTypeOperandValue(int trueTypeOperandValue) => Value = trueTypeOperandValue;
        internal int Value { get; }
        public override string ToString() => Value.ToString();
    }

    internal sealed class TrueTypeVirtualMachineResult
    {
        internal TrueTypeVirtualMachineResult(bool trueTypeProgramSucceeded,
            TrueTypeOperandValue[] finalOperandStack, TrueTypeVirtualMachineFailure trueTypeProgramFailure,
            int executedInstructionCount, TrueTypeVirtualMachineState trueTypeVirtualMachineState)
        {
            Succeeded = trueTypeProgramSucceeded;
            FinalOperandStack = finalOperandStack ?? new TrueTypeOperandValue[0];
            Failure = trueTypeProgramFailure;
            ExecutedInstructionCount = executedInstructionCount;
            VirtualMachineState = trueTypeVirtualMachineState;
        }

        internal bool Succeeded { get; }
        internal TrueTypeOperandValue[] FinalOperandStack { get; }
        internal TrueTypeVirtualMachineFailure Failure { get; }
        internal int ExecutedInstructionCount { get; }
        internal TrueTypeVirtualMachineState VirtualMachineState { get; }
    }

    /// <summary>Scalar, flow, function, storage, CVT, and graphics-state opcode dispatcher.</summary>
    internal sealed class TrueTypeInstructionInterpreter
    {
        private readonly TrueTypeExecutionLimits _trueTypeExecutionLimits;

        internal TrueTypeInstructionInterpreter(TrueTypeExecutionLimits trueTypeExecutionLimits)
            => _trueTypeExecutionLimits = trueTypeExecutionLimits ?? throw new ArgumentNullException(nameof(trueTypeExecutionLimits));

        internal TrueTypeVirtualMachineResult Execute(byte[] trueTypeInstructionBytes)
            => Execute(trueTypeInstructionBytes, TrueTypeVirtualMachineState.ForTests());

        internal TrueTypeVirtualMachineResult Execute(byte[] trueTypeInstructionBytes,
            TrueTypeVirtualMachineState trueTypeVirtualMachineState)
            => Execute(trueTypeInstructionBytes, trueTypeVirtualMachineState, null);

        internal TrueTypeVirtualMachineResult Execute(byte[] trueTypeInstructionBytes,
            TrueTypeVirtualMachineState trueTypeVirtualMachineState,
            TrueTypeHintingExecutionZones trueTypeHintingExecutionZones)
        {
            if (trueTypeVirtualMachineState == null) throw new ArgumentNullException(nameof(trueTypeVirtualMachineState));
            if (!TrueTypeInstructionStream.TryValidateConditionalStructure(trueTypeInstructionBytes,
                    out TrueTypeVirtualMachineFailure trueTypeValidationFailure))
                return new TrueTypeVirtualMachineResult(false, new TrueTypeOperandValue[0],
                    trueTypeValidationFailure, 0, trueTypeVirtualMachineState);

            var trueTypeExecutionContext = new TrueTypeExecutionContext(_trueTypeExecutionLimits,
                trueTypeVirtualMachineState, trueTypeHintingExecutionZones);
            if (!ExecuteProgram(trueTypeInstructionBytes, trueTypeExecutionContext, out TrueTypeVirtualMachineFailure trueTypeProgramFailure))
                return Failed(trueTypeProgramFailure, trueTypeExecutionContext);
            return new TrueTypeVirtualMachineResult(true, SnapshotStack(trueTypeExecutionContext.OperandStack),
                default, trueTypeExecutionContext.ExecutedInstructionCount, trueTypeVirtualMachineState);
        }

        private bool ExecuteProgram(byte[] trueTypeInstructionBytes, TrueTypeExecutionContext trueTypeExecutionContext,
            out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            var trueTypeInstructionStream = new TrueTypeInstructionStream(trueTypeInstructionBytes);
            while (trueTypeInstructionStream.HasRemainingInstructionBytes)
            {
                if (++trueTypeExecutionContext.ExecutedInstructionCount > trueTypeExecutionContext.ExecutionLimits.InstructionExecutionBudget.Value)
                {
                    trueTypeProgramFailure = Failure(TrueTypeVirtualMachineFailureCode.InstructionExecutionBudgetExceeded,
                        "The TrueType program exceeded its instruction-execution budget.");
                    return false;
                }
                int trueTypeOpcodeBytePosition = trueTypeInstructionStream.InstructionBytePosition;
                if (!trueTypeInstructionStream.TryReadByte(out byte trueTypeOpcodeByte, out trueTypeProgramFailure) ||
                    !ExecuteOpcode(trueTypeOpcodeByte, trueTypeInstructionStream, trueTypeExecutionContext, out trueTypeProgramFailure))
                {
                    if (trueTypeProgramFailure.HasFailure)
                        trueTypeProgramFailure = Failure(trueTypeProgramFailure.FailureCode,
                            trueTypeProgramFailure.FailureMessage + " [opcode 0x" + trueTypeOpcodeByte.ToString("X2") +
                            " at program byte " + trueTypeOpcodeBytePosition + "]");
                    return false;
                }
            }
            trueTypeProgramFailure = default;
            return true;
        }

        private bool ExecuteOpcode(byte trueTypeOpcodeByte, TrueTypeInstructionStream trueTypeInstructionStream,
            TrueTypeExecutionContext trueTypeExecutionContext, out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            TrueTypeOperandStack trueTypeOperandStack = trueTypeExecutionContext.OperandStack;
            TrueTypeVirtualMachineState trueTypeVirtualMachineState = trueTypeExecutionContext.VirtualMachineState;
            TrueTypeGraphicsState trueTypeGraphicsState = trueTypeVirtualMachineState.GraphicsState;

            if (trueTypeOpcodeByte >= 0xB0 && trueTypeOpcodeByte <= 0xB7)
                return PushBytes(trueTypeInstructionStream, trueTypeOperandStack, trueTypeOpcodeByte - 0xAF, out trueTypeProgramFailure);
            if (trueTypeOpcodeByte >= 0xB8 && trueTypeOpcodeByte <= 0xBF)
                return PushWords(trueTypeInstructionStream, trueTypeOperandStack, trueTypeOpcodeByte - 0xB7, out trueTypeProgramFailure);
            if (trueTypeOpcodeByte >= 0xC0 && trueTypeOpcodeByte <= 0xDF)
                return ExecuteMoveDirectRelativePoint(trueTypeOpcodeByte, trueTypeExecutionContext, out trueTypeProgramFailure);
            if (trueTypeOpcodeByte >= 0xE0)
                return ExecuteMoveIndirectRelativePoint(trueTypeOpcodeByte, trueTypeExecutionContext, out trueTypeProgramFailure);

            switch ((TrueTypeInstructionOpcode)trueTypeOpcodeByte)
            {
                case TrueTypeInstructionOpcode.SetVectorsToYAxis: SetBothVectors(trueTypeGraphicsState, TrueTypeUnitVector.Vertical); break;
                case TrueTypeInstructionOpcode.SetVectorsToXAxis: SetBothVectors(trueTypeGraphicsState, TrueTypeUnitVector.Horizontal); break;
                case TrueTypeInstructionOpcode.SetProjectionVectorToYAxis: SetProjectionVector(trueTypeGraphicsState, TrueTypeUnitVector.Vertical); break;
                case TrueTypeInstructionOpcode.SetProjectionVectorToXAxis: SetProjectionVector(trueTypeGraphicsState, TrueTypeUnitVector.Horizontal); break;
                case TrueTypeInstructionOpcode.SetFreedomVectorToYAxis: trueTypeGraphicsState.FreedomVector = TrueTypeUnitVector.Vertical; break;
                case TrueTypeInstructionOpcode.SetFreedomVectorToXAxis: trueTypeGraphicsState.FreedomVector = TrueTypeUnitVector.Horizontal; break;
                case TrueTypeInstructionOpcode.SetProjectionVectorFromStack:
                    if (!TryPopUnitVector(trueTypeOperandStack, out TrueTypeUnitVector projectionVector, out trueTypeProgramFailure)) return false;
                    SetProjectionVector(trueTypeGraphicsState, projectionVector); return true;
                case TrueTypeInstructionOpcode.SetFreedomVectorFromStack:
                    if (!TryPopUnitVector(trueTypeOperandStack, out TrueTypeUnitVector freedomVector, out trueTypeProgramFailure)) return false;
                    trueTypeGraphicsState.FreedomVector = freedomVector; return Success(out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GetProjectionVector:
                    return PushUnitVector(trueTypeOperandStack, trueTypeGraphicsState.ProjectionVector, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GetFreedomVector:
                    return PushUnitVector(trueTypeOperandStack, trueTypeGraphicsState.FreedomVector, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetFreedomVectorToProjectionVector:
                    trueTypeGraphicsState.FreedomVector = trueTypeGraphicsState.ProjectionVector; break;
                case TrueTypeInstructionOpcode.SetReferencePointZero: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ReferencePointZero = new TrueTypeReferencePointIndex(value), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetReferencePointOne: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(value), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetReferencePointTwo: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ReferencePointTwo = new TrueTypeReferencePointIndex(value), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetZonePointerZero: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(value), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetZonePointerOne: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(value), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetZonePointerTwo: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ZonePointerTwo = new TrueTypeZonePointerIndex(value), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetAllZonePointers:
                    return PopReferencePoint(trueTypeOperandStack, value => { var zone = new TrueTypeZonePointerIndex(value); trueTypeGraphicsState.ZonePointerZero = zone; trueTypeGraphicsState.ZonePointerOne = zone; trueTypeGraphicsState.ZonePointerTwo = zone; }, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetLoopCount: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.LoopCount = new TrueTypeLoopCount(value), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.RoundToGrid: trueTypeGraphicsState.RoundingMode = TrueTypeRoundingMode.ToGrid; break;
                case TrueTypeInstructionOpcode.RoundToHalfGrid: trueTypeGraphicsState.RoundingMode = TrueTypeRoundingMode.ToHalfGrid; break;
                case TrueTypeInstructionOpcode.RoundOff: trueTypeGraphicsState.RoundingMode = TrueTypeRoundingMode.Off; break;
                case TrueTypeInstructionOpcode.RoundUpToGrid: trueTypeGraphicsState.RoundingMode = TrueTypeRoundingMode.UpToGrid; break;
                case TrueTypeInstructionOpcode.RoundDownToGrid: trueTypeGraphicsState.RoundingMode = TrueTypeRoundingMode.DownToGrid; break;
                case TrueTypeInstructionOpcode.RoundGrayDistance:
                case TrueTypeInstructionOpcode.RoundBlackDistance:
                case TrueTypeInstructionOpcode.RoundWhiteDistance:
                case TrueTypeInstructionOpcode.RoundReservedDistance: return ExecuteUnary(trueTypeOperandStack, value => RoundF26Dot6(value, trueTypeGraphicsState.RoundingMode), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.NoRoundGrayDistance:
                case TrueTypeInstructionOpcode.NoRoundBlackDistance:
                case TrueTypeInstructionOpcode.NoRoundWhiteDistance:
                case TrueTypeInstructionOpcode.NoRoundReservedDistance: return ExecuteUnary(trueTypeOperandStack, value => value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetMinimumDistance: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.MinimumDistanceF26Dot6 = value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetControlValueCutIn: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ControlValueCutInF26Dot6 = value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetSingleWidthCutIn: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.SingleWidthCutInF26Dot6 = value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetSingleWidth: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.SingleWidthValueF26Dot6 = value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetDeltaBase: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.DeltaBasePpem = value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetDeltaShift: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.DeltaShift = value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.FlipOn: trueTypeGraphicsState.AutoFlip = true; break;
                case TrueTypeInstructionOpcode.FlipOff: trueTypeGraphicsState.AutoFlip = false; break;
                case TrueTypeInstructionOpcode.MeasurePixelsPerEm: return trueTypeOperandStack.TryPush(trueTypeVirtualMachineState.RasterizerEnvironment.DevicePpemY.Value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MeasurePointSize: return trueTypeOperandStack.TryPush(trueTypeVirtualMachineState.RasterizerEnvironment.PointSizeF26Dot6, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GetInformation: return ExecuteGetInformation(trueTypeOperandStack, trueTypeVirtualMachineState.RasterizerEnvironment, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.ScanControl: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ScanControlFlags = value & 0xFFFF, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.ScanType: return PopReferencePoint(trueTypeOperandStack, value => trueTypeGraphicsState.ScanType = value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.InstructionControl: return ExecuteInstructionControl(trueTypeOperandStack, trueTypeGraphicsState, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MoveDirectAbsolutePointWithoutRounding: return ExecuteMoveDirectAbsolutePoint(false, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MoveDirectAbsolutePointWithRounding: return ExecuteMoveDirectAbsolutePoint(true, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MoveIndirectAbsolutePointWithoutRounding: return ExecuteMoveIndirectAbsolutePoint(false, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MoveIndirectAbsolutePointWithRounding: return ExecuteMoveIndirectAbsolutePoint(true, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MoveStackIndirectRelativePointKeepReference: return ExecuteMoveStackIndirectRelativePoint(false, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MoveStackIndirectRelativePointSetReference: return ExecuteMoveStackIndirectRelativePoint(true, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GetCurrentCoordinate: return ExecuteGetCoordinate(false, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GetOriginalCoordinate: return ExecuteGetCoordinate(true, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.SetCoordinateFromStack: return ExecuteSetCoordinateFromStack(trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MeasureDistanceCurrent: return ExecuteMeasureDistance(false, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MeasureDistanceOriginal: return ExecuteMeasureDistance(true, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.InterpolateUntouchedPointsYAxis:
                    if (!TryRequireExecutionZones(trueTypeExecutionContext, out trueTypeProgramFailure)) return false;
                    TrueTypeHintingGeometryOperations.InterpolateUntouchedPoints(trueTypeExecutionContext.ExecutionZones.GlyphZone, true); break;
                case TrueTypeInstructionOpcode.InterpolateUntouchedPointsXAxis:
                    if (!TryRequireExecutionZones(trueTypeExecutionContext, out trueTypeProgramFailure)) return false;
                    TrueTypeHintingGeometryOperations.InterpolateUntouchedPoints(trueTypeExecutionContext.ExecutionZones.GlyphZone, false); break;
                case TrueTypeInstructionOpcode.InterpolatePoints: return ExecuteInterpolatePoints(trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.AlignRelativePoints: return ExecuteAlignRelativePoints(trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.ShiftPointsUsingReferencePointTwo: return ExecuteShiftPoints(false, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.ShiftPointsUsingReferencePointOne: return ExecuteShiftPoints(true, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.ShiftPointsByPixelAmount: return ExecuteShiftPointsByPixelAmount(trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.DeltaPointOne: return ExecuteDeltaPoints(trueTypeExecutionContext, 0, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.DeltaPointTwo: return ExecuteDeltaPoints(trueTypeExecutionContext, 16, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.DeltaPointThree: return ExecuteDeltaPoints(trueTypeExecutionContext, 32, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.FunctionDefinition: return DefineFunction(true, trueTypeInstructionStream, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.InstructionDefinition: return DefineFunction(false, trueTypeInstructionStream, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.EndFunction: trueTypeProgramFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidFunctionDefinition, "ENDF appeared outside a definition."); return false;
                case TrueTypeInstructionOpcode.Call: return CallFunction(false, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.LoopCall: return CallFunction(true, trueTypeExecutionContext, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.WriteStorage: return WriteStorage(trueTypeOperandStack, trueTypeVirtualMachineState, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.ReadStorage: return ReadStorage(trueTypeOperandStack, trueTypeVirtualMachineState, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.WriteControlValuePixels: return WriteControlValue(trueTypeOperandStack, trueTypeVirtualMachineState, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.WriteControlValueFontUnits: return WriteControlValueFontUnits(trueTypeOperandStack, trueTypeVirtualMachineState, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.ReadControlValue: return ReadControlValue(trueTypeOperandStack, trueTypeVirtualMachineState, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.DeltaControlValueOne: return ExecuteDeltaControlValue(trueTypeOperandStack, trueTypeVirtualMachineState, 0, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.DeltaControlValueTwo: return ExecuteDeltaControlValue(trueTypeOperandStack, trueTypeVirtualMachineState, 16, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.DeltaControlValueThree: return ExecuteDeltaControlValue(trueTypeOperandStack, trueTypeVirtualMachineState, 32, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.PushBytesVariable: return ReadPushCountThenPush(false, trueTypeInstructionStream, trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.PushWordsVariable: return ReadPushCountThenPush(true, trueTypeInstructionStream, trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Duplicate: if (!trueTypeOperandStack.TryPeekFromTop(0, out int duplicateValue, out trueTypeProgramFailure)) return false; return trueTypeOperandStack.TryPush(duplicateValue, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Pop: return trueTypeOperandStack.TryPop(out _, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Clear: trueTypeOperandStack.Clear(); break;
                case TrueTypeInstructionOpcode.Swap: return trueTypeOperandStack.TrySwapTop(out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Depth: return trueTypeOperandStack.TryPush(trueTypeOperandStack.OperandCount, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.CopyIndexed: if (!trueTypeOperandStack.TryPop(out int copyIndex, out trueTypeProgramFailure)) return false; return trueTypeOperandStack.TryCopyIndexedFromTop(copyIndex, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MoveIndexed: if (!trueTypeOperandStack.TryPop(out int moveIndex, out trueTypeProgramFailure)) return false; return trueTypeOperandStack.TryMoveIndexedToTop(moveIndex, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Roll: return trueTypeOperandStack.TryRollTopThree(out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.If: if (!trueTypeOperandStack.TryPop(out int condition, out trueTypeProgramFailure)) return false; if (condition == 0) return trueTypeInstructionStream.TrySkipToConditionalBranch(true, out trueTypeProgramFailure); break;
                case TrueTypeInstructionOpcode.Else: return trueTypeInstructionStream.TrySkipToConditionalBranch(false, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.EndIf: break;
                case TrueTypeInstructionOpcode.JumpRelative: if (!trueTypeOperandStack.TryPop(out int jumpOffset, out trueTypeProgramFailure)) return false; return trueTypeInstructionStream.TryJumpRelativeFromCurrentPosition(jumpOffset, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.JumpRelativeOnTrue: return ExecuteConditionalJump(true, trueTypeInstructionStream, trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.JumpRelativeOnFalse: return ExecuteConditionalJump(false, trueTypeInstructionStream, trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Add: return ExecuteBinary(trueTypeOperandStack, (left, right) => left + right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Subtract: return ExecuteBinary(trueTypeOperandStack, (left, right) => left - right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Multiply: return ExecuteBinary(trueTypeOperandStack, (left, right) => (int)(((long)left * right) / 64), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Divide: return ExecuteDivide(trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Absolute: return ExecuteUnary(trueTypeOperandStack, Math.Abs, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Negate: return ExecuteUnary(trueTypeOperandStack, value => -value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Floor: return ExecuteUnary(trueTypeOperandStack, value => value & ~63, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Ceiling: return ExecuteUnary(trueTypeOperandStack, value => (value + 63) & ~63, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Maximum: return ExecuteBinary(trueTypeOperandStack, Math.Max, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Minimum: return ExecuteBinary(trueTypeOperandStack, Math.Min, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.LessThan: return Compare(trueTypeOperandStack, (left, right) => left < right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.LessThanOrEqual: return Compare(trueTypeOperandStack, (left, right) => left <= right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GreaterThan: return Compare(trueTypeOperandStack, (left, right) => left > right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GreaterThanOrEqual: return Compare(trueTypeOperandStack, (left, right) => left >= right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Equal: return Compare(trueTypeOperandStack, (left, right) => left == right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.NotEqual: return Compare(trueTypeOperandStack, (left, right) => left != right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.And: return Compare(trueTypeOperandStack, (left, right) => left != 0 && right != 0, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Or: return Compare(trueTypeOperandStack, (left, right) => left != 0 || right != 0, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Not: return ExecuteUnary(trueTypeOperandStack, value => value == 0 ? 1 : 0, out trueTypeProgramFailure);
                default:
                    if (trueTypeVirtualMachineState.FunctionDefinitions.TryGetInstruction(trueTypeOpcodeByte, out byte[] instructionDefinitionBody))
                        return ExecuteCalledProgram(instructionDefinitionBody, trueTypeExecutionContext, out trueTypeProgramFailure);
                    trueTypeProgramFailure = Failure(TrueTypeVirtualMachineFailureCode.UnsupportedOpcode, "Unsupported TrueType opcode 0x" + trueTypeOpcodeByte.ToString("X2") + ".");
                    return false;
            }
            return Success(out trueTypeProgramFailure);
        }

        private bool DefineFunction(bool isFunctionDefinition, TrueTypeInstructionStream stream, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (!context.OperandStack.TryPop(out int identifier, out failure) || !stream.TryReadDefinitionBody(out byte[] body, out failure)) return false;
            return isFunctionDefinition
                ? context.VirtualMachineState.FunctionDefinitions.TryDefineFunction(new TrueTypeFunctionIdentifier(identifier), body, out failure)
                : context.VirtualMachineState.FunctionDefinitions.TryDefineInstruction(unchecked((byte)identifier), body, out failure);
        }

        private bool CallFunction(bool isLoopCall, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            int callCount = 1;
            if (!context.OperandStack.TryPop(out int functionIdentifier, out failure)) return false;
            // LOOPCALL stack order is [..., repeatCount, functionIdentifier]; function is topmost.
            if (isLoopCall && !context.OperandStack.TryPop(out callCount, out failure)) return false;
            if (callCount < 0) { failure = Failure(TrueTypeVirtualMachineFailureCode.InvalidFunctionDefinition, "LOOPCALL count is negative."); return false; }
            if (!context.VirtualMachineState.FunctionDefinitions.TryGetFunction(new TrueTypeFunctionIdentifier(functionIdentifier), out byte[] body, out failure)) return false;
            for (int callIndex = 0; callIndex < callCount; callIndex++)
                if (!ExecuteCalledProgram(body, context, out failure)) return false;
            return Success(out failure);
        }

        private bool ExecuteCalledProgram(byte[] body, TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
        {
            if (context.ActiveCallDepth >= context.ExecutionLimits.CallDepthLimit.Value)
            {
                failure = Failure(TrueTypeVirtualMachineFailureCode.CallDepthLimitExceeded, "The TrueType call-depth limit was exceeded.");
                return false;
            }
            context.ActiveCallDepth++;
            try { return ExecuteProgram(body, context, out failure); }
            finally { context.ActiveCallDepth--; }
        }

        private static bool ExecuteMoveDirectAbsolutePoint(bool round, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteMoveDirectAbsolutePoint(round, context, out failure);

        private static bool ExecuteMoveIndirectAbsolutePoint(bool round, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteMoveIndirectAbsolutePoint(round, context, out failure);

        private static bool ExecuteMoveDirectRelativePoint(byte opcode, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteMoveDirectRelativePoint(opcode, context, out failure);

        private static bool ExecuteMoveIndirectRelativePoint(byte opcode, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteMoveIndirectRelativePoint(opcode, context, out failure);

        private static bool ExecuteMoveStackIndirectRelativePoint(bool setReferencePointZero,
            TrueTypeExecutionContext context, out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteMoveStackIndirectRelativePoint(setReferencePointZero, context, out failure);

        private static bool ExecuteGetCoordinate(bool original, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteGetCoordinate(original, context, out failure);

        private static bool ExecuteSetCoordinateFromStack(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteSetCoordinateFromStack(context, out failure);

        private static bool ExecuteMeasureDistance(bool original, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteMeasureDistance(original, context, out failure);

        private static bool ExecuteInterpolatePoints(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteInterpolatePoints(context, out failure);

        private static bool ExecuteAlignRelativePoints(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteAlignRelativePoints(context, out failure);

        private static bool ExecuteShiftPoints(bool useReferencePointOne, TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteShiftPoints(useReferencePointOne, context, out failure);

        private static bool ExecuteShiftPointsByPixelAmount(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteShiftPointsByPixelAmount(context, out failure);

        private static bool ExecuteDeltaPoints(TrueTypeExecutionContext context, int ppemBias,
            out TrueTypeVirtualMachineFailure failure)
            => TrueTypePointInstructionExecutor.ExecuteDeltaPoints(context, ppemBias, out failure);

        private static bool TryRequireExecutionZones(TrueTypeExecutionContext context,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (context.ExecutionZones != null) { failure = default; return true; }
            failure = Failure(TrueTypeVirtualMachineFailureCode.InvalidZonePointer,
                "A point instruction was executed without TrueType point zones.");
            return false;
        }

        private static bool WriteStorage(TrueTypeOperandStack stack, TrueTypeVirtualMachineState state, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int value, out failure) || !stack.TryPop(out int index, out failure)) return false;
            return state.TryWriteStorage(index, value, out failure);
        }
        private static bool ReadStorage(TrueTypeOperandStack stack, TrueTypeVirtualMachineState state, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int index, out failure) || !state.TryReadStorage(index, out int value, out failure)) return false;
            return stack.TryPush(value, out failure);
        }
        private static bool WriteControlValue(TrueTypeOperandStack stack, TrueTypeVirtualMachineState state, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int value, out failure) || !stack.TryPop(out int index, out failure)) return false;
            return state.TryWriteControlValue(index, value, out failure);
        }
        private static bool ReadControlValue(TrueTypeOperandStack stack, TrueTypeVirtualMachineState state, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int index, out failure) || !state.TryReadControlValue(index, out int value, out failure)) return false;
            return stack.TryPush(value, out failure);
        }

        private static bool WriteControlValueFontUnits(TrueTypeOperandStack stack, TrueTypeVirtualMachineState state,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int fontUnitValue, out failure) || !stack.TryPop(out int index, out failure)) return false;
            return state.TryWriteControlValue(index, state.ScaleFontUnitsToF26Dot6(fontUnitValue), out failure);
        }

        private static bool ExecuteDeltaControlValue(TrueTypeOperandStack stack, TrueTypeVirtualMachineState state,
            int deltaPpemBias, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int exceptionCount, out failure)) return false;
            if (exceptionCount < 0 || exceptionCount > stack.OperandCount / 2)
            {
                failure = Failure(TrueTypeVirtualMachineFailureCode.OperandStackUnderflow,
                    "The DELTAC exception count exceeds the available operand pairs.");
                return false;
            }
            for (int exceptionIndex = 0; exceptionIndex < exceptionCount; exceptionIndex++)
            {
                if (!stack.TryPop(out int controlValueIndex, out failure) ||
                    !stack.TryPop(out int packedDeltaArgument, out failure)) return false;
                int targetPpem = ((packedDeltaArgument >> 4) & 0x0F) + state.GraphicsState.DeltaBasePpem + deltaPpemBias;
                if (targetPpem != state.RasterizerEnvironment.DevicePpemY.Value) continue;
                int deltaStep = (packedDeltaArgument & 0x0F) - 8;
                if (deltaStep >= 0) deltaStep++;
                int deltaF26Dot6 = deltaStep * (1 << Math.Max(0, 6 - state.GraphicsState.DeltaShift));
                if (!state.TryReadControlValue(controlValueIndex, out int currentControlValue, out failure) ||
                    !state.TryWriteControlValue(controlValueIndex, currentControlValue + deltaF26Dot6, out failure)) return false;
            }
            return Success(out failure);
        }

        private static bool ExecuteGetInformation(TrueTypeOperandStack stack, TrueTypeRasterizerEnvironment environment,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int selector, out failure)) return false;
            int result = 0;
            // Modern DirectWrite (Windows 8+) reports Microsoft rasterizer v2.1 (40).
            if ((selector & (1 << 0)) != 0) result = 40;
            if ((selector & (1 << 1)) != 0 && environment.GlyphRotated) result |= 1 << 8;
            if ((selector & (1 << 2)) != 0 && environment.GlyphStretched) result |= 1 << 9;
            // This snapshot currently represents non-variable TrueType faces: selector bit 3 returns zero.
            if ((selector & (1 << 6)) != 0 && environment.ClearTypeHintingEnabled) result |= 1 << 13;
            if ((selector & (1 << 10)) != 0 && environment.SubpixelPositioningEnabled) result |= 1 << 17;
            if ((selector & (1 << 11)) != 0 && environment.SymmetricSmoothingEnabled) result |= 1 << 18;
            if ((selector & (1 << 12)) != 0 && environment.GrayscaleClearTypeEnabled) result |= 1 << 19;
            return stack.TryPush(result, out failure);
        }

        private static bool ExecuteInstructionControl(TrueTypeOperandStack stack, TrueTypeGraphicsState state,
            out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int selector, out failure) || !stack.TryPop(out int value, out failure)) return false;
            switch (selector)
            {
                case 1 when value == 0 || value == 1:
                    state.InstructionControlFlags = value == 0 ? state.InstructionControlFlags & ~1 : state.InstructionControlFlags | 1;
                    return Success(out failure);
                case 2 when value == 0 || value == 2:
                    state.InstructionControlFlags = value == 0 ? state.InstructionControlFlags & ~2 : state.InstructionControlFlags | 2;
                    return Success(out failure);
                case 3 when value == 0 || value == 4:
                    state.InstructionControlFlags = value == 0 ? state.InstructionControlFlags & ~4 : state.InstructionControlFlags | 4;
                    return Success(out failure);
                default:
                    failure = Failure(TrueTypeVirtualMachineFailureCode.InvalidFunctionDefinition,
                        "INSTCTRL received an unsupported selector/value pair.");
                    return false;
            }
        }

        private static int RoundF26Dot6(int valueF26Dot6, TrueTypeRoundingMode roundingMode)
        {
            switch (roundingMode)
            {
                case TrueTypeRoundingMode.Off: return valueF26Dot6;
                case TrueTypeRoundingMode.DownToGrid: return valueF26Dot6 >= 0 ? valueF26Dot6 & ~63 : -((-valueF26Dot6 + 63) & ~63);
                case TrueTypeRoundingMode.UpToGrid: return valueF26Dot6 >= 0 ? (valueF26Dot6 + 63) & ~63 : -((-valueF26Dot6) & ~63);
                case TrueTypeRoundingMode.ToHalfGrid: return valueF26Dot6 >= 0 ? ((valueF26Dot6 + 32) & ~63) + 32 : -(((-valueF26Dot6 + 32) & ~63) + 32);
                default: return valueF26Dot6 >= 0 ? (valueF26Dot6 + 32) & ~63 : -((-valueF26Dot6 + 32) & ~63);
            }
        }

        private static void SetBothVectors(TrueTypeGraphicsState state, TrueTypeUnitVector vector) { SetProjectionVector(state, vector); state.FreedomVector = vector; }
        private static void SetProjectionVector(TrueTypeGraphicsState state, TrueTypeUnitVector vector) { state.ProjectionVector = vector; state.DualProjectionVector = vector; }
        private static bool TryPopUnitVector(TrueTypeOperandStack stack, out TrueTypeUnitVector vector, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int vertical, out failure) || !stack.TryPop(out int horizontal, out failure)) { vector = default; return false; }
            try { vector = new TrueTypeUnitVector(new TrueTypeVectorComponent(horizontal), new TrueTypeVectorComponent(vertical)); return Success(out failure); }
            catch (ArgumentOutOfRangeException) { vector = default; failure = Failure(TrueTypeVirtualMachineFailureCode.InvalidFunctionDefinition, "A vector component is outside F2Dot14 range."); return false; }
        }
        private static bool PushUnitVector(TrueTypeOperandStack stack, TrueTypeUnitVector vector, out TrueTypeVirtualMachineFailure failure)
            => stack.TryPush(vector.HorizontalComponent.Value, out failure) && stack.TryPush(vector.VerticalComponent.Value, out failure);
        private static bool PopReferencePoint(TrueTypeOperandStack stack, Action<int> assign, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int value, out failure)) return false;
            assign(value); return Success(out failure);
        }

        private static bool ReadPushCountThenPush(bool words, TrueTypeInstructionStream stream, TrueTypeOperandStack stack, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stream.TryReadByte(out byte count, out failure)) return false;
            return words ? PushWords(stream, stack, count, out failure) : PushBytes(stream, stack, count, out failure);
        }
        private static bool PushBytes(TrueTypeInstructionStream stream, TrueTypeOperandStack stack, int count, out TrueTypeVirtualMachineFailure failure)
        {
            for (int index = 0; index < count; index++) if (!stream.TryReadByte(out byte value, out failure) || !stack.TryPush(value, out failure)) return false;
            return Success(out failure);
        }
        private static bool PushWords(TrueTypeInstructionStream stream, TrueTypeOperandStack stack, int count, out TrueTypeVirtualMachineFailure failure)
        {
            for (int index = 0; index < count; index++) if (!stream.TryReadSignedWord(out int value, out failure) || !stack.TryPush(value, out failure)) return false;
            return Success(out failure);
        }
        private static bool ExecuteConditionalJump(bool jumpWhenTrue, TrueTypeInstructionStream stream, TrueTypeOperandStack stack, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int offset, out failure) || !stack.TryPop(out int condition, out failure)) return false;
            return (condition != 0) == jumpWhenTrue ? stream.TryJumpRelativeFromCurrentPosition(offset, out failure) : Success(out failure);
        }
        private static bool ExecuteDivide(TrueTypeOperandStack stack, out TrueTypeVirtualMachineFailure failure)
        {
            if (!TryPopBinary(stack, out int left, out int right, out failure)) return false;
            if (right == 0) { failure = Failure(TrueTypeVirtualMachineFailureCode.DivisionByZero, "The TrueType DIV divisor is zero."); return false; }
            return stack.TryPush((int)(((long)left * 64) / right), out failure);
        }
        private static bool ExecuteUnary(TrueTypeOperandStack stack, Func<int, int> operation, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out int value, out failure)) return false;
            return stack.TryPush(operation(value), out failure);
        }
        private static bool ExecuteBinary(TrueTypeOperandStack stack, Func<int, int, int> operation, out TrueTypeVirtualMachineFailure failure)
        {
            if (!TryPopBinary(stack, out int left, out int right, out failure)) return false;
            return stack.TryPush(operation(left, right), out failure);
        }
        private static bool Compare(TrueTypeOperandStack stack, Func<int, int, bool> comparison, out TrueTypeVirtualMachineFailure failure)
            => ExecuteBinary(stack, (left, right) => comparison(left, right) ? 1 : 0, out failure);
        private static bool TryPopBinary(TrueTypeOperandStack stack, out int left, out int right, out TrueTypeVirtualMachineFailure failure)
        {
            if (!stack.TryPop(out right, out failure) || !stack.TryPop(out left, out failure)) { left = right = 0; return false; }
            return true;
        }

        private static TrueTypeVirtualMachineResult Failed(TrueTypeVirtualMachineFailure failure, TrueTypeExecutionContext context)
            => new TrueTypeVirtualMachineResult(false, SnapshotStack(context.OperandStack), failure, context.ExecutedInstructionCount, context.VirtualMachineState);
        private static TrueTypeOperandValue[] SnapshotStack(TrueTypeOperandStack stack)
        {
            var snapshot = new List<TrueTypeOperandValue>(stack.OperandCount);
            for (int depth = stack.OperandCount - 1; depth >= 0; depth--) if (stack.TryPeekFromTop(depth, out int value, out _)) snapshot.Add(new TrueTypeOperandValue(value));
            return snapshot.ToArray();
        }
        private static bool Success(out TrueTypeVirtualMachineFailure failure) { failure = default; return true; }
        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode code, string message)
            => new TrueTypeVirtualMachineFailure(code, new TrueTypeHintingFailureMessage(message));
    }
}