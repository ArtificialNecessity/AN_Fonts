using System;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;

namespace StbTrueTypeSharp.TrueTypeHinting.SizeInstance
{
    internal static class TrueTypeHintingSizeInstanceFactory
    {
        internal static TrueTypeHintingSizeInstanceResult Create(TrueTypeHintingFontFace trueTypeHintingFontFace,
            DevicePpemY devicePpemY)
        {
            if (trueTypeHintingFontFace == null) throw new ArgumentNullException(nameof(trueTypeHintingFontFace));
            if (!TrueTypeHintingFaceRuntime.TryCreate(trueTypeHintingFontFace,
                    out TrueTypeHintingFaceRuntime trueTypeHintingFaceRuntime,
                    out TrueTypeYHintingFailure trueTypeHintingFaceRuntimeFailure))
                return TrueTypeHintingSizeInstanceResult.Failed(trueTypeHintingFaceRuntimeFailure);
            return trueTypeHintingFaceRuntime.GetOrCreateSizeInstance(devicePpemY);
        }

        internal static TrueTypeHintingSizeInstanceResult Create(TrueTypeHintingFontFace trueTypeHintingFontFace,
            DevicePpemY devicePpemY, TrueTypeVirtualMachineState fontProgramVirtualMachineState,
            TrueTypeInstructionInterpreter trueTypeInstructionInterpreter, int fontProgramInstructionCount)
        {
            if (!TryScaleControlValues(trueTypeHintingFontFace.FontProgram.ControlValueTable.ToByteArray(),
                    trueTypeHintingFontFace.UnitsPerEm.Value, devicePpemY.Value, out int[] scaledControlValues,
                    out TrueTypeYHintingFailure controlValueScalingFailure))
                return TrueTypeHintingSizeInstanceResult.Failed(controlValueScalingFailure);

            TrueTypeVirtualMachineState virtualMachineState = fontProgramVirtualMachineState.CloneForSizeInstance(
                scaledControlValues,
                new TrueTypeRasterizerEnvironment(devicePpemY,
                    new TrueTypeUnitsPerEmScale(trueTypeHintingFontFace.UnitsPerEm.Value),
                    symmetricSmoothingEnabled: true,
                    grayscaleClearTypeEnabled: true));

            TrueTypeVirtualMachineResult controlValueProgramResult = trueTypeInstructionInterpreter.Execute(
                trueTypeHintingFontFace.FontProgram.ControlValueProgram.ToByteArray(), virtualMachineState);
            if (!controlValueProgramResult.Succeeded)
                return TrueTypeHintingSizeInstanceResult.Failed(Failure(TrueTypeHintingFailureCode.ControlValueProgramExecutionFailed,
                    "prep execution failed: " + controlValueProgramResult.Failure));

            return TrueTypeHintingSizeInstanceResult.Success(new TrueTypeHintingSizeInstance(
                trueTypeHintingFontFace, devicePpemY,
                virtualMachineState, fontProgramInstructionCount,
                controlValueProgramResult.ExecutedInstructionCount));
        }

        internal static bool TryScaleControlValues(byte[] controlValueTableBytes, int trueTypeUnitsPerEm,
            int devicePpemY, out int[] scaledControlValues, out TrueTypeYHintingFailure controlValueScalingFailure)
        {
            if (controlValueTableBytes == null) controlValueTableBytes = new byte[0];
            if ((controlValueTableBytes.Length & 1) != 0)
            {
                scaledControlValues = null;
                controlValueScalingFailure = Failure(TrueTypeHintingFailureCode.MalformedControlValueTable,
                    "The cvt table byte length is not a multiple of FWORD size.");
                return false;
            }

            scaledControlValues = new int[controlValueTableBytes.Length / 2];
            for (int controlValueIndex = 0; controlValueIndex < scaledControlValues.Length; controlValueIndex++)
            {
                int controlValueByteOffset = controlValueIndex * 2;
                short controlValueFontUnits = unchecked((short)((controlValueTableBytes[controlValueByteOffset] << 8) |
                    controlValueTableBytes[controlValueByteOffset + 1]));
                long scaledF26Dot6Numerator = (long)controlValueFontUnits * devicePpemY * 64;
                scaledControlValues[controlValueIndex] = DivideRoundedSymmetrically(scaledF26Dot6Numerator, trueTypeUnitsPerEm);
            }
            controlValueScalingFailure = default;
            return true;
        }

        private static int DivideRoundedSymmetrically(long signedNumerator, int positiveDenominator)
        {
            if (signedNumerator >= 0) return (int)((signedNumerator + positiveDenominator / 2) / positiveDenominator);
            return (int)-((-signedNumerator + positiveDenominator / 2) / positiveDenominator);
        }

        private static TrueTypeYHintingFailure Failure(TrueTypeHintingFailureCode failureCode, string failureMessage)
            => new TrueTypeYHintingFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}