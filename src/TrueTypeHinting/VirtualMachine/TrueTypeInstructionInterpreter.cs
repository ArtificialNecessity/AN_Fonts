using System;
using System.Collections.Generic;

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
            int executedInstructionCount)
        {
            Succeeded = trueTypeProgramSucceeded;
            FinalOperandStack = finalOperandStack ?? new TrueTypeOperandValue[0];
            Failure = trueTypeProgramFailure;
            ExecutedInstructionCount = executedInstructionCount;
        }

        internal bool Succeeded { get; }
        internal TrueTypeOperandValue[] FinalOperandStack { get; }
        internal TrueTypeVirtualMachineFailure Failure { get; }
        internal int ExecutedInstructionCount { get; }
    }

    /// <summary>Core scalar/flow opcode dispatcher. Geometry and function opcodes land in later phases.</summary>
    internal sealed class TrueTypeInstructionInterpreter
    {
        private readonly TrueTypeExecutionLimits _trueTypeExecutionLimits;

        internal TrueTypeInstructionInterpreter(TrueTypeExecutionLimits trueTypeExecutionLimits)
            => _trueTypeExecutionLimits = trueTypeExecutionLimits ?? throw new ArgumentNullException(nameof(trueTypeExecutionLimits));

        internal TrueTypeVirtualMachineResult Execute(byte[] trueTypeInstructionBytes)
        {
            if (!TrueTypeInstructionStream.TryValidateConditionalStructure(trueTypeInstructionBytes,
                    out TrueTypeVirtualMachineFailure trueTypeValidationFailure))
                return new TrueTypeVirtualMachineResult(false, new TrueTypeOperandValue[0],
                    trueTypeValidationFailure, executedInstructionCount: 0);

            var trueTypeInstructionStream = new TrueTypeInstructionStream(trueTypeInstructionBytes);
            var trueTypeOperandStack = new TrueTypeOperandStack(_trueTypeExecutionLimits.OperandStackCapacity);
            var trueTypeGraphicsState = new TrueTypeGraphicsState();
            int executedInstructionCount = 0;

            while (trueTypeInstructionStream.HasRemainingInstructionBytes)
            {
                if (++executedInstructionCount > _trueTypeExecutionLimits.InstructionExecutionBudget.Value)
                    return Failed(TrueTypeVirtualMachineFailureCode.InstructionExecutionBudgetExceeded,
                        "The TrueType program exceeded its instruction-execution budget.", trueTypeOperandStack, executedInstructionCount);
                if (!trueTypeInstructionStream.TryReadByte(out byte trueTypeOpcodeByte, out TrueTypeVirtualMachineFailure trueTypeProgramFailure))
                    return Failed(trueTypeProgramFailure, trueTypeOperandStack, executedInstructionCount);

                if (!ExecuteOpcode(trueTypeOpcodeByte, trueTypeInstructionStream, trueTypeOperandStack,
                        trueTypeGraphicsState, out trueTypeProgramFailure))
                    return Failed(trueTypeProgramFailure, trueTypeOperandStack, executedInstructionCount);
            }

            return new TrueTypeVirtualMachineResult(true, SnapshotStack(trueTypeOperandStack), default, executedInstructionCount);
        }

        private static bool ExecuteOpcode(byte trueTypeOpcodeByte, TrueTypeInstructionStream trueTypeInstructionStream,
            TrueTypeOperandStack trueTypeOperandStack, TrueTypeGraphicsState trueTypeGraphicsState,
            out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            if (trueTypeOpcodeByte >= 0xB0 && trueTypeOpcodeByte <= 0xB7)
                return PushBytes(trueTypeInstructionStream, trueTypeOperandStack, trueTypeOpcodeByte - 0xAF, out trueTypeProgramFailure);
            if (trueTypeOpcodeByte >= 0xB8 && trueTypeOpcodeByte <= 0xBF)
                return PushWords(trueTypeInstructionStream, trueTypeOperandStack, trueTypeOpcodeByte - 0xB7, out trueTypeProgramFailure);

            switch ((TrueTypeInstructionOpcode)trueTypeOpcodeByte)
            {
                case TrueTypeInstructionOpcode.PushBytesVariable:
                    return ReadPushCountThenPush(false, trueTypeInstructionStream, trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.PushWordsVariable:
                    return ReadPushCountThenPush(true, trueTypeInstructionStream, trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Duplicate:
                    if (!trueTypeOperandStack.TryPeekFromTop(0, out int duplicateOperandValue, out trueTypeProgramFailure)) return false;
                    return trueTypeOperandStack.TryPush(duplicateOperandValue, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Pop:
                    return trueTypeOperandStack.TryPop(out _, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Clear:
                    trueTypeOperandStack.Clear(); trueTypeProgramFailure = default; return true;
                case TrueTypeInstructionOpcode.Swap:
                    return trueTypeOperandStack.TrySwapTop(out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Depth:
                    return trueTypeOperandStack.TryPush(trueTypeOperandStack.OperandCount, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.CopyIndexed:
                    if (!trueTypeOperandStack.TryPop(out int copyIndexedStackIndex, out trueTypeProgramFailure)) return false;
                    return trueTypeOperandStack.TryCopyIndexedFromTop(copyIndexedStackIndex, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.MoveIndexed:
                    if (!trueTypeOperandStack.TryPop(out int moveIndexedStackIndex, out trueTypeProgramFailure)) return false;
                    return trueTypeOperandStack.TryMoveIndexedToTop(moveIndexedStackIndex, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Roll:
                    return trueTypeOperandStack.TryRollTopThree(out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.If:
                    if (!trueTypeOperandStack.TryPop(out int conditionalValue, out trueTypeProgramFailure)) return false;
                    if (conditionalValue == 0) return trueTypeInstructionStream.TrySkipToConditionalBranch(true, out trueTypeProgramFailure);
                    trueTypeProgramFailure = default; return true;
                case TrueTypeInstructionOpcode.Else:
                    return trueTypeInstructionStream.TrySkipToConditionalBranch(false, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.EndIf:
                    trueTypeProgramFailure = default; return true;
                case TrueTypeInstructionOpcode.JumpRelative:
                    if (!trueTypeOperandStack.TryPop(out int relativeJumpByteOffset, out trueTypeProgramFailure)) return false;
                    return trueTypeInstructionStream.TryJumpRelativeFromCurrentPosition(relativeJumpByteOffset, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.JumpRelativeOnTrue:
                    return ExecuteConditionalJump(true, trueTypeInstructionStream, trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.JumpRelativeOnFalse:
                    return ExecuteConditionalJump(false, trueTypeInstructionStream, trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Add:
                    return ExecuteBinary(trueTypeOperandStack, (left, right) => left + right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Subtract:
                    return ExecuteBinary(trueTypeOperandStack, (left, right) => left - right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Multiply:
                    return ExecuteBinary(trueTypeOperandStack, (left, right) => (int)(((long)left * right) / 64), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Divide:
                    return ExecuteDivide(trueTypeOperandStack, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Absolute:
                    return ExecuteUnary(trueTypeOperandStack, value => Math.Abs(value), out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Negate:
                    return ExecuteUnary(trueTypeOperandStack, value => -value, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Floor:
                    return ExecuteUnary(trueTypeOperandStack, value => value & ~63, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Ceiling:
                    return ExecuteUnary(trueTypeOperandStack, value => (value + 63) & ~63, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Maximum:
                    return ExecuteBinary(trueTypeOperandStack, Math.Max, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Minimum:
                    return ExecuteBinary(trueTypeOperandStack, Math.Min, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.LessThan:
                    return ExecuteComparison(trueTypeOperandStack, (left, right) => left < right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.LessThanOrEqual:
                    return ExecuteComparison(trueTypeOperandStack, (left, right) => left <= right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GreaterThan:
                    return ExecuteComparison(trueTypeOperandStack, (left, right) => left > right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.GreaterThanOrEqual:
                    return ExecuteComparison(trueTypeOperandStack, (left, right) => left >= right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Equal:
                    return ExecuteComparison(trueTypeOperandStack, (left, right) => left == right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.NotEqual:
                    return ExecuteComparison(trueTypeOperandStack, (left, right) => left != right, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.And:
                    return ExecuteComparison(trueTypeOperandStack, (left, right) => left != 0 && right != 0, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Or:
                    return ExecuteComparison(trueTypeOperandStack, (left, right) => left != 0 || right != 0, out trueTypeProgramFailure);
                case TrueTypeInstructionOpcode.Not:
                    return ExecuteUnary(trueTypeOperandStack, value => value == 0 ? 1 : 0, out trueTypeProgramFailure);
                default:
                    trueTypeProgramFailure = Failure(TrueTypeVirtualMachineFailureCode.UnsupportedOpcode,
                        "Unsupported TrueType opcode 0x" + trueTypeOpcodeByte.ToString("X2") + ".");
                    return false;
            }
        }

        private static bool ReadPushCountThenPush(bool pushedValuesAreWords, TrueTypeInstructionStream trueTypeInstructionStream,
            TrueTypeOperandStack trueTypeOperandStack, out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            if (!trueTypeInstructionStream.TryReadByte(out byte pushedValueCount, out trueTypeProgramFailure)) return false;
            return pushedValuesAreWords
                ? PushWords(trueTypeInstructionStream, trueTypeOperandStack, pushedValueCount, out trueTypeProgramFailure)
                : PushBytes(trueTypeInstructionStream, trueTypeOperandStack, pushedValueCount, out trueTypeProgramFailure);
        }

        private static bool PushBytes(TrueTypeInstructionStream trueTypeInstructionStream, TrueTypeOperandStack trueTypeOperandStack,
            int pushedValueCount, out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            for (int pushedValueIndex = 0; pushedValueIndex < pushedValueCount; pushedValueIndex++)
            {
                if (!trueTypeInstructionStream.TryReadByte(out byte pushedByteValue, out trueTypeProgramFailure) ||
                    !trueTypeOperandStack.TryPush(pushedByteValue, out trueTypeProgramFailure)) return false;
            }
            trueTypeProgramFailure = default; return true;
        }

        private static bool PushWords(TrueTypeInstructionStream trueTypeInstructionStream, TrueTypeOperandStack trueTypeOperandStack,
            int pushedValueCount, out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            for (int pushedValueIndex = 0; pushedValueIndex < pushedValueCount; pushedValueIndex++)
            {
                if (!trueTypeInstructionStream.TryReadSignedWord(out int pushedSignedWordValue, out trueTypeProgramFailure) ||
                    !trueTypeOperandStack.TryPush(pushedSignedWordValue, out trueTypeProgramFailure)) return false;
            }
            trueTypeProgramFailure = default; return true;
        }

        private static bool ExecuteConditionalJump(bool jumpWhenConditionTrue, TrueTypeInstructionStream trueTypeInstructionStream,
            TrueTypeOperandStack trueTypeOperandStack, out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            if (!trueTypeOperandStack.TryPop(out int relativeJumpByteOffset, out trueTypeProgramFailure) ||
                !trueTypeOperandStack.TryPop(out int jumpCondition, out trueTypeProgramFailure)) return false;
            if ((jumpCondition != 0) == jumpWhenConditionTrue)
                return trueTypeInstructionStream.TryJumpRelativeFromCurrentPosition(relativeJumpByteOffset, out trueTypeProgramFailure);
            trueTypeProgramFailure = default; return true;
        }

        private static bool ExecuteDivide(TrueTypeOperandStack trueTypeOperandStack, out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            if (!TryPopBinary(trueTypeOperandStack, out int leftOperandValue, out int rightOperandValue, out trueTypeProgramFailure)) return false;
            if (rightOperandValue == 0)
            {
                trueTypeProgramFailure = Failure(TrueTypeVirtualMachineFailureCode.DivisionByZero, "The TrueType DIV divisor is zero.");
                return false;
            }
            return trueTypeOperandStack.TryPush((int)(((long)leftOperandValue * 64) / rightOperandValue), out trueTypeProgramFailure);
        }

        private static bool ExecuteUnary(TrueTypeOperandStack trueTypeOperandStack, Func<int, int> unaryOperation,
            out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            if (!trueTypeOperandStack.TryPop(out int operandValue, out trueTypeProgramFailure)) return false;
            return trueTypeOperandStack.TryPush(unaryOperation(operandValue), out trueTypeProgramFailure);
        }

        private static bool ExecuteBinary(TrueTypeOperandStack trueTypeOperandStack, Func<int, int, int> binaryOperation,
            out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            if (!TryPopBinary(trueTypeOperandStack, out int leftOperandValue, out int rightOperandValue, out trueTypeProgramFailure)) return false;
            return trueTypeOperandStack.TryPush(binaryOperation(leftOperandValue, rightOperandValue), out trueTypeProgramFailure);
        }

        private static bool ExecuteComparison(TrueTypeOperandStack trueTypeOperandStack, Func<int, int, bool> comparisonOperation,
            out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
            => ExecuteBinary(trueTypeOperandStack, (left, right) => comparisonOperation(left, right) ? 1 : 0, out trueTypeProgramFailure);

        private static bool TryPopBinary(TrueTypeOperandStack trueTypeOperandStack, out int leftOperandValue,
            out int rightOperandValue, out TrueTypeVirtualMachineFailure trueTypeProgramFailure)
        {
            if (!trueTypeOperandStack.TryPop(out rightOperandValue, out trueTypeProgramFailure) ||
                !trueTypeOperandStack.TryPop(out leftOperandValue, out trueTypeProgramFailure))
            {
                leftOperandValue = 0;
                rightOperandValue = 0;
                return false;
            }
            return true;
        }

        private static TrueTypeVirtualMachineResult Failed(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage,
            TrueTypeOperandStack trueTypeOperandStack, int executedInstructionCount)
            => Failed(Failure(failureCode, failureMessage), trueTypeOperandStack, executedInstructionCount);

        private static TrueTypeVirtualMachineResult Failed(TrueTypeVirtualMachineFailure trueTypeProgramFailure,
            TrueTypeOperandStack trueTypeOperandStack, int executedInstructionCount)
            => new TrueTypeVirtualMachineResult(false, SnapshotStack(trueTypeOperandStack), trueTypeProgramFailure, executedInstructionCount);

        private static TrueTypeOperandValue[] SnapshotStack(TrueTypeOperandStack trueTypeOperandStack)
        {
            var finalOperandStack = new List<TrueTypeOperandValue>(trueTypeOperandStack.OperandCount);
            for (int operandDepthFromTop = trueTypeOperandStack.OperandCount - 1; operandDepthFromTop >= 0; operandDepthFromTop--)
            {
                if (trueTypeOperandStack.TryPeekFromTop(operandDepthFromTop, out int operandValue, out _))
                    finalOperandStack.Add(new TrueTypeOperandValue(operandValue));
            }
            return finalOperandStack.ToArray();
        }

        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage)
            => new TrueTypeVirtualMachineFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}