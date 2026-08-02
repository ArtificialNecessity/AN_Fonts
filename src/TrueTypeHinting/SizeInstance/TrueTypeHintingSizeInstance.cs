using System;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;

namespace StbTrueTypeSharp.TrueTypeHinting.SizeInstance
{
    /// <summary>Prepared, ppem-specific TrueType VM state after fpgm and prep execution.</summary>
    public sealed class TrueTypeHintingSizeInstance
    {
        internal TrueTypeHintingSizeInstance(DevicePpemY devicePpemY, TrueTypeVirtualMachineState virtualMachineState,
            int fontProgramInstructionCount, int controlValueProgramInstructionCount)
        {
            DevicePpemY = devicePpemY;
            VirtualMachineState = virtualMachineState ?? throw new ArgumentNullException(nameof(virtualMachineState));
            FontProgramInstructionCount = fontProgramInstructionCount;
            ControlValueProgramInstructionCount = controlValueProgramInstructionCount;
        }

        public DevicePpemY DevicePpemY { get; }
        public int FontProgramInstructionCount { get; }
        public int ControlValueProgramInstructionCount { get; }
        public int ScaledControlValueCount => VirtualMachineState.ControlValueCount.Value;
        internal TrueTypeVirtualMachineState VirtualMachineState { get; }

        /// <summary>Creates isolated glyph state with prepared defaults and reset glyph-local references.</summary>
        internal TrueTypeVirtualMachineState CreateGlyphExecutionState()
            => VirtualMachineState.CloneForGlyphExecution();
    }

    public sealed class TrueTypeHintingSizeInstanceResult
    {
        private TrueTypeHintingSizeInstanceResult(bool succeeded, TrueTypeHintingSizeInstance sizeInstance,
            TrueTypeYHintingFailure failure)
        {
            Succeeded = succeeded;
            SizeInstance = sizeInstance;
            Failure = failure;
        }

        public bool Succeeded { get; }
        public TrueTypeHintingSizeInstance SizeInstance { get; }
        public TrueTypeYHintingFailure Failure { get; }
        internal static TrueTypeHintingSizeInstanceResult Success(TrueTypeHintingSizeInstance sizeInstance)
            => new TrueTypeHintingSizeInstanceResult(true, sizeInstance, default);
        internal static TrueTypeHintingSizeInstanceResult Failed(TrueTypeYHintingFailure failure)
            => new TrueTypeHintingSizeInstanceResult(false, null, failure);
    }
}