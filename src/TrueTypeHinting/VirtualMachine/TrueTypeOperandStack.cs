using System;

namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    /// <summary>Fixed-capacity signed 32-bit operand stack used by the TrueType VM.</summary>
    internal sealed class TrueTypeOperandStack
    {
        private readonly int[] _trueTypeOperandValues;
        private int _trueTypeOperandCount;

        internal TrueTypeOperandStack(TrueTypeOperandStackCapacity trueTypeOperandStackCapacity)
            => _trueTypeOperandValues = new int[trueTypeOperandStackCapacity.Value];

        internal int OperandCount => _trueTypeOperandCount;

        internal bool TryPush(int trueTypeOperandValue, out TrueTypeVirtualMachineFailure trueTypeStackFailure)
        {
            if (_trueTypeOperandCount >= _trueTypeOperandValues.Length)
            {
                trueTypeStackFailure = Failure(TrueTypeVirtualMachineFailureCode.OperandStackOverflow,
                    "The TrueType operand stack exceeded its configured capacity.");
                return false;
            }
            _trueTypeOperandValues[_trueTypeOperandCount++] = trueTypeOperandValue;
            trueTypeStackFailure = default;
            return true;
        }

        internal bool TryPop(out int trueTypeOperandValue, out TrueTypeVirtualMachineFailure trueTypeStackFailure)
        {
            if (_trueTypeOperandCount == 0)
            {
                trueTypeOperandValue = 0;
                trueTypeStackFailure = Failure(TrueTypeVirtualMachineFailureCode.OperandStackUnderflow,
                    "The TrueType operand stack is empty.");
                return false;
            }
            trueTypeOperandValue = _trueTypeOperandValues[--_trueTypeOperandCount];
            trueTypeStackFailure = default;
            return true;
        }

        internal bool TryPeekFromTop(int zeroBasedDepthFromStackTop, out int trueTypeOperandValue,
            out TrueTypeVirtualMachineFailure trueTypeStackFailure)
        {
            if (zeroBasedDepthFromStackTop < 0 || zeroBasedDepthFromStackTop >= _trueTypeOperandCount)
            {
                trueTypeOperandValue = 0;
                trueTypeStackFailure = Failure(TrueTypeVirtualMachineFailureCode.OperandStackUnderflow,
                    "The requested TrueType operand-stack depth is unavailable.");
                return false;
            }
            trueTypeOperandValue = _trueTypeOperandValues[_trueTypeOperandCount - 1 - zeroBasedDepthFromStackTop];
            trueTypeStackFailure = default;
            return true;
        }

        internal bool TryCopyIndexedFromTop(int oneBasedStackIndex, out TrueTypeVirtualMachineFailure trueTypeStackFailure)
        {
            if (!TryPeekFromTop(oneBasedStackIndex - 1, out int trueTypeOperandValue, out trueTypeStackFailure))
                return false;
            return TryPush(trueTypeOperandValue, out trueTypeStackFailure);
        }

        internal bool TryMoveIndexedToTop(int oneBasedStackIndex, out TrueTypeVirtualMachineFailure trueTypeStackFailure)
        {
            int trueTypeOperandArrayIndex = _trueTypeOperandCount - oneBasedStackIndex;
            if (oneBasedStackIndex <= 0 || trueTypeOperandArrayIndex < 0)
            {
                trueTypeStackFailure = Failure(TrueTypeVirtualMachineFailureCode.OperandStackUnderflow,
                    "MINDEX requested an unavailable TrueType operand-stack index.");
                return false;
            }
            int trueTypeOperandValue = _trueTypeOperandValues[trueTypeOperandArrayIndex];
            for (int operandShiftIndex = trueTypeOperandArrayIndex; operandShiftIndex < _trueTypeOperandCount - 1; operandShiftIndex++)
                _trueTypeOperandValues[operandShiftIndex] = _trueTypeOperandValues[operandShiftIndex + 1];
            _trueTypeOperandValues[_trueTypeOperandCount - 1] = trueTypeOperandValue;
            trueTypeStackFailure = default;
            return true;
        }

        internal bool TrySwapTop(out TrueTypeVirtualMachineFailure trueTypeStackFailure)
        {
            if (_trueTypeOperandCount < 2)
            {
                trueTypeStackFailure = Failure(TrueTypeVirtualMachineFailureCode.OperandStackUnderflow,
                    "SWAP requires two TrueType operands.");
                return false;
            }
            int firstTrueTypeOperandValue = _trueTypeOperandValues[_trueTypeOperandCount - 1];
            _trueTypeOperandValues[_trueTypeOperandCount - 1] = _trueTypeOperandValues[_trueTypeOperandCount - 2];
            _trueTypeOperandValues[_trueTypeOperandCount - 2] = firstTrueTypeOperandValue;
            trueTypeStackFailure = default;
            return true;
        }

        internal bool TryRollTopThree(out TrueTypeVirtualMachineFailure trueTypeStackFailure)
        {
            if (_trueTypeOperandCount < 3)
            {
                trueTypeStackFailure = Failure(TrueTypeVirtualMachineFailureCode.OperandStackUnderflow,
                    "ROLL requires three TrueType operands.");
                return false;
            }
            int thirdFromTopTrueTypeOperandValue = _trueTypeOperandValues[_trueTypeOperandCount - 3];
            _trueTypeOperandValues[_trueTypeOperandCount - 3] = _trueTypeOperandValues[_trueTypeOperandCount - 2];
            _trueTypeOperandValues[_trueTypeOperandCount - 2] = _trueTypeOperandValues[_trueTypeOperandCount - 1];
            _trueTypeOperandValues[_trueTypeOperandCount - 1] = thirdFromTopTrueTypeOperandValue;
            trueTypeStackFailure = default;
            return true;
        }

        internal void Clear() => _trueTypeOperandCount = 0;

        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage)
            => new TrueTypeVirtualMachineFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}