namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    internal enum TrueTypeVirtualMachineFailureCode
    {
        None,
        UnexpectedEndOfInstructionStream,
        OperandStackUnderflow,
        OperandStackOverflow,
        DivisionByZero,
        InvalidJumpTarget,
        InstructionExecutionBudgetExceeded,
        UnsupportedOpcode,
        InvalidConditionalStructure,
    }

    internal readonly struct TrueTypeVirtualMachineFailure
    {
        internal TrueTypeVirtualMachineFailure(TrueTypeVirtualMachineFailureCode failureCode,
            TrueTypeHintingFailureMessage failureMessage)
        {
            FailureCode = failureCode;
            FailureMessage = failureMessage;
        }
        internal TrueTypeVirtualMachineFailureCode FailureCode { get; }
        internal TrueTypeHintingFailureMessage FailureMessage { get; }
        internal bool HasFailure => FailureCode != TrueTypeVirtualMachineFailureCode.None;
        public override string ToString() => HasFailure ? FailureCode + ": " + FailureMessage : "None";
    }
}