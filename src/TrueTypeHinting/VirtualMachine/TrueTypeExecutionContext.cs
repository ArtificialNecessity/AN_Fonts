namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    /// <summary>Mutable execution state shared across a root program and all function calls.</summary>
    internal sealed class TrueTypeExecutionContext
    {
        internal TrueTypeExecutionContext(TrueTypeExecutionLimits trueTypeExecutionLimits,
            TrueTypeVirtualMachineState trueTypeVirtualMachineState)
        {
            ExecutionLimits = trueTypeExecutionLimits;
            VirtualMachineState = trueTypeVirtualMachineState;
            OperandStack = new TrueTypeOperandStack(trueTypeExecutionLimits.OperandStackCapacity);
        }

        internal TrueTypeExecutionLimits ExecutionLimits { get; }
        internal TrueTypeVirtualMachineState VirtualMachineState { get; }
        internal TrueTypeOperandStack OperandStack { get; }
        internal int ExecutedInstructionCount { get; set; }
        internal int ActiveCallDepth { get; set; }
    }
}