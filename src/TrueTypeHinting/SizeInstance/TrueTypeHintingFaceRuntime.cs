using System;
using System.Collections.Generic;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;

namespace StbTrueTypeSharp.TrueTypeHinting.SizeInstance
{
    /// <summary>One reusable fpgm runtime and a deterministic ppem size-instance cache for one face.</summary>
    public sealed class TrueTypeHintingFaceRuntime
    {
        private readonly TrueTypeHintingFontFace _trueTypeHintingFontFace;
        private readonly TrueTypeVirtualMachineState _fontProgramVirtualMachineState;
        private readonly TrueTypeInstructionInterpreter _trueTypeInstructionInterpreter;
        private readonly Dictionary<DevicePpemY, TrueTypeHintingSizeInstanceResult> _sizeInstanceResults =
            new Dictionary<DevicePpemY, TrueTypeHintingSizeInstanceResult>();

        private TrueTypeHintingFaceRuntime(TrueTypeHintingFontFace trueTypeHintingFontFace,
            TrueTypeVirtualMachineState fontProgramVirtualMachineState,
            TrueTypeInstructionInterpreter trueTypeInstructionInterpreter, int fontProgramInstructionCount)
        {
            _trueTypeHintingFontFace = trueTypeHintingFontFace;
            _fontProgramVirtualMachineState = fontProgramVirtualMachineState;
            _trueTypeInstructionInterpreter = trueTypeInstructionInterpreter;
            FontProgramInstructionCount = fontProgramInstructionCount;
        }

        public int FontProgramInstructionCount { get; }
        public int CachedSizeInstanceCount => _sizeInstanceResults.Count;

        internal static bool TryCreate(TrueTypeHintingFontFace trueTypeHintingFontFace,
            out TrueTypeHintingFaceRuntime trueTypeHintingFaceRuntime,
            out TrueTypeYHintingFailure trueTypeHintingFaceRuntimeFailure)
        {
            if (trueTypeHintingFontFace == null) throw new ArgumentNullException(nameof(trueTypeHintingFontFace));
            var trueTypeInstructionInterpreter = new TrueTypeInstructionInterpreter(
                TrueTypeExecutionLimits.FromMaximumProfile(trueTypeHintingFontFace.MaximumProfile));
            var fontProgramVirtualMachineState = new TrueTypeVirtualMachineState(
                new TrueTypeStorageCapacity(trueTypeHintingFontFace.MaximumProfile.MaximumStorageCount.Value),
                new int[0],
                new TrueTypeRasterizerEnvironment(new DevicePpemY(1),
                    new TrueTypeUnitsPerEmScale(trueTypeHintingFontFace.UnitsPerEm.Value), true, true));
            TrueTypeVirtualMachineResult fontProgramResult = trueTypeInstructionInterpreter.Execute(
                trueTypeHintingFontFace.FontProgram.FontProgram.ToByteArray(), fontProgramVirtualMachineState);
            if (!fontProgramResult.Succeeded)
            {
                trueTypeHintingFaceRuntime = null;
                trueTypeHintingFaceRuntimeFailure = Failure(TrueTypeHintingFailureCode.FontProgramExecutionFailed,
                    "fpgm execution failed: " + fontProgramResult.Failure);
                return false;
            }

            trueTypeHintingFaceRuntime = new TrueTypeHintingFaceRuntime(trueTypeHintingFontFace,
                fontProgramVirtualMachineState, trueTypeInstructionInterpreter, fontProgramResult.ExecutedInstructionCount);
            trueTypeHintingFaceRuntimeFailure = default;
            return true;
        }

        public TrueTypeHintingSizeInstanceResult GetOrCreateSizeInstance(DevicePpemY devicePpemY)
        {
            if (_sizeInstanceResults.TryGetValue(devicePpemY, out TrueTypeHintingSizeInstanceResult cachedSizeInstanceResult))
                return cachedSizeInstanceResult;

            TrueTypeHintingSizeInstanceResult sizeInstanceResult = TrueTypeHintingSizeInstanceFactory.Create(
                _trueTypeHintingFontFace, devicePpemY, _fontProgramVirtualMachineState,
                _trueTypeInstructionInterpreter, FontProgramInstructionCount);
            _sizeInstanceResults.Add(devicePpemY, sizeInstanceResult);
            return sizeInstanceResult;
        }

        private static TrueTypeYHintingFailure Failure(TrueTypeHintingFailureCode failureCode, string failureMessage)
            => new TrueTypeYHintingFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}