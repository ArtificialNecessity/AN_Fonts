using System;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;

namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    internal readonly struct TrueTypeInstructionExecutionBudget
    {
        internal TrueTypeInstructionExecutionBudget(int trueTypeInstructionExecutionBudgetValue)
        {
            if (trueTypeInstructionExecutionBudgetValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(trueTypeInstructionExecutionBudgetValue));
            Value = trueTypeInstructionExecutionBudgetValue;
        }
        internal int Value { get; }
    }

    internal readonly struct TrueTypeCallDepthLimit
    {
        internal TrueTypeCallDepthLimit(int trueTypeCallDepthLimitValue)
        {
            if (trueTypeCallDepthLimitValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(trueTypeCallDepthLimitValue));
            Value = trueTypeCallDepthLimitValue;
        }
        internal int Value { get; }
    }

    internal readonly struct TrueTypeOperandStackCapacity
    {
        internal TrueTypeOperandStackCapacity(int trueTypeOperandStackCapacityValue)
        {
            if (trueTypeOperandStackCapacityValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(trueTypeOperandStackCapacityValue));
            Value = trueTypeOperandStackCapacityValue;
        }
        internal int Value { get; }
    }

    /// <summary>Hard caps applied in addition to the font's maxp declarations.</summary>
    internal sealed class TrueTypeExecutionLimits
    {
        private const int LibraryMaximumInstructionExecutions = 1_000_000;
        private const int LibraryMaximumCallDepth = 64;
        private const int LibraryMaximumOperandStackCapacity = 16_384;

        internal TrueTypeExecutionLimits(TrueTypeInstructionExecutionBudget instructionExecutionBudget,
            TrueTypeCallDepthLimit callDepthLimit, TrueTypeOperandStackCapacity operandStackCapacity)
        {
            InstructionExecutionBudget = instructionExecutionBudget;
            CallDepthLimit = callDepthLimit;
            OperandStackCapacity = operandStackCapacity;
        }

        internal TrueTypeInstructionExecutionBudget InstructionExecutionBudget { get; }
        internal TrueTypeCallDepthLimit CallDepthLimit { get; }
        internal TrueTypeOperandStackCapacity OperandStackCapacity { get; }

        internal static TrueTypeExecutionLimits FromMaximumProfile(TrueTypeHintingMaximumProfile maximumProfile)
        {
            if (maximumProfile == null) throw new ArgumentNullException(nameof(maximumProfile));
            int declaredStackCapacity = Math.Max(32, maximumProfile.MaximumOperandStackCount.Value);
            int declaredInstructionBytes = Math.Max(256, maximumProfile.MaximumInstructionByteCount.Value);
            return new TrueTypeExecutionLimits(
                new TrueTypeInstructionExecutionBudget(Math.Min(LibraryMaximumInstructionExecutions, declaredInstructionBytes * 256)),
                new TrueTypeCallDepthLimit(LibraryMaximumCallDepth),
                new TrueTypeOperandStackCapacity(Math.Min(LibraryMaximumOperandStackCapacity, declaredStackCapacity)));
        }

        internal static TrueTypeExecutionLimits ForTests(int instructionExecutionBudgetValue = 4096,
            int operandStackCapacityValue = 256, int callDepthLimitValue = 16)
            => new TrueTypeExecutionLimits(new TrueTypeInstructionExecutionBudget(instructionExecutionBudgetValue),
                new TrueTypeCallDepthLimit(callDepthLimitValue), new TrueTypeOperandStackCapacity(operandStackCapacityValue));
    }
}