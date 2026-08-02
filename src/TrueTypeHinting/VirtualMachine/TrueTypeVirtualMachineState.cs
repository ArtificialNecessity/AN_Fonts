using System;

namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    internal readonly struct TrueTypeStorageCapacity
    {
        internal TrueTypeStorageCapacity(int trueTypeStorageCapacityValue)
        {
            if (trueTypeStorageCapacityValue < 0) throw new ArgumentOutOfRangeException(nameof(trueTypeStorageCapacityValue));
            Value = trueTypeStorageCapacityValue;
        }
        internal int Value { get; }
    }

    internal readonly struct TrueTypeControlValueCount
    {
        internal TrueTypeControlValueCount(int trueTypeControlValueCountValue)
        {
            if (trueTypeControlValueCountValue < 0) throw new ArgumentOutOfRangeException(nameof(trueTypeControlValueCountValue));
            Value = trueTypeControlValueCountValue;
        }
        internal int Value { get; }
    }

    internal readonly struct TrueTypeUnitsPerEmScale
    {
        internal TrueTypeUnitsPerEmScale(int trueTypeUnitsPerEmScaleValue)
        {
            if (trueTypeUnitsPerEmScaleValue <= 0) throw new ArgumentOutOfRangeException(nameof(trueTypeUnitsPerEmScaleValue));
            Value = trueTypeUnitsPerEmScaleValue;
        }
        internal int Value { get; }
    }

    internal sealed class TrueTypeRasterizerEnvironment
    {
        internal TrueTypeRasterizerEnvironment(DevicePpemY devicePpemY, TrueTypeUnitsPerEmScale unitsPerEmScale,
            bool symmetricSmoothingEnabled, bool grayscaleClearTypeEnabled)
        {
            DevicePpemY = devicePpemY;
            UnitsPerEmScale = unitsPerEmScale;
            SymmetricSmoothingEnabled = symmetricSmoothingEnabled;
            GrayscaleClearTypeEnabled = grayscaleClearTypeEnabled;
        }

        internal DevicePpemY DevicePpemY { get; }
        internal TrueTypeUnitsPerEmScale UnitsPerEmScale { get; }
        internal bool SymmetricSmoothingEnabled { get; }
        internal bool GrayscaleClearTypeEnabled { get; }
        internal bool SubpixelPositioningEnabled => true;
        internal bool ClearTypeHintingEnabled => true;
        internal bool GlyphRotated => false;
        internal bool GlyphStretched => false;
        internal int PointSizeF26Dot6 => DevicePpemY.Value * 48; // 96 DPI: points = pixels * 72/96.
    }

    /// <summary>Persistent font/size VM state shared by fpgm, prep, and glyph executions.</summary>
    internal sealed class TrueTypeVirtualMachineState
    {
        private readonly int[] _trueTypeStorageValues;
        private readonly int[] _trueTypeControlValues;

        internal TrueTypeVirtualMachineState(TrueTypeStorageCapacity trueTypeStorageCapacity,
            int[] initialTrueTypeControlValues, TrueTypeRasterizerEnvironment rasterizerEnvironment)
        {
            _trueTypeStorageValues = new int[trueTypeStorageCapacity.Value];
            _trueTypeControlValues = initialTrueTypeControlValues == null
                ? new int[0]
                : (int[])initialTrueTypeControlValues.Clone();
            RasterizerEnvironment = rasterizerEnvironment ?? throw new ArgumentNullException(nameof(rasterizerEnvironment));
            FunctionDefinitions = new TrueTypeFunctionDefinitions();
            GraphicsState = new TrueTypeGraphicsState();
        }

        internal TrueTypeRasterizerEnvironment RasterizerEnvironment { get; }
        internal TrueTypeFunctionDefinitions FunctionDefinitions { get; }
        internal TrueTypeGraphicsState GraphicsState { get; }
        internal TrueTypeStorageCapacity StorageCapacity => new TrueTypeStorageCapacity(_trueTypeStorageValues.Length);
        internal TrueTypeControlValueCount ControlValueCount => new TrueTypeControlValueCount(_trueTypeControlValues.Length);

        internal bool TryReadStorage(int trueTypeStorageIndex, out int trueTypeStorageValue,
            out TrueTypeVirtualMachineFailure trueTypeStateFailure)
        {
            if (trueTypeStorageIndex < 0 || trueTypeStorageIndex >= _trueTypeStorageValues.Length)
            {
                trueTypeStorageValue = 0;
                trueTypeStateFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidStorageIndex,
                    "The TrueType storage index is outside the configured storage area.");
                return false;
            }
            trueTypeStorageValue = _trueTypeStorageValues[trueTypeStorageIndex];
            trueTypeStateFailure = default;
            return true;
        }

        internal bool TryWriteStorage(int trueTypeStorageIndex, int trueTypeStorageValue,
            out TrueTypeVirtualMachineFailure trueTypeStateFailure)
        {
            if (trueTypeStorageIndex < 0 || trueTypeStorageIndex >= _trueTypeStorageValues.Length)
            {
                trueTypeStateFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidStorageIndex,
                    "The TrueType storage index is outside the configured storage area.");
                return false;
            }
            _trueTypeStorageValues[trueTypeStorageIndex] = trueTypeStorageValue;
            trueTypeStateFailure = default;
            return true;
        }

        internal bool TryReadControlValue(int trueTypeControlValueIndex, out int trueTypeControlValue,
            out TrueTypeVirtualMachineFailure trueTypeStateFailure)
        {
            if (trueTypeControlValueIndex < 0 || trueTypeControlValueIndex >= _trueTypeControlValues.Length)
            {
                trueTypeControlValue = 0;
                trueTypeStateFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidControlValueIndex,
                    "The TrueType control-value index is outside the CVT.");
                return false;
            }
            trueTypeControlValue = _trueTypeControlValues[trueTypeControlValueIndex];
            trueTypeStateFailure = default;
            return true;
        }

        internal bool TryWriteControlValue(int trueTypeControlValueIndex, int trueTypeControlValue,
            out TrueTypeVirtualMachineFailure trueTypeStateFailure)
        {
            if (trueTypeControlValueIndex < 0 || trueTypeControlValueIndex >= _trueTypeControlValues.Length)
            {
                trueTypeStateFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidControlValueIndex,
                    "The TrueType control-value index is outside the CVT.");
                return false;
            }
            _trueTypeControlValues[trueTypeControlValueIndex] = trueTypeControlValue;
            trueTypeStateFailure = default;
            return true;
        }

        internal int ScaleFontUnitsToF26Dot6(int trueTypeFontUnitValue)
        {
            long scaledNumerator = (long)trueTypeFontUnitValue * RasterizerEnvironment.DevicePpemY.Value * 64;
            int unitsPerEm = RasterizerEnvironment.UnitsPerEmScale.Value;
            return scaledNumerator >= 0
                ? (int)((scaledNumerator + unitsPerEm / 2) / unitsPerEm)
                : (int)-((-scaledNumerator + unitsPerEm / 2) / unitsPerEm);
        }

        internal static TrueTypeVirtualMachineState ForTests(int storageCapacity = 32, params int[] initialControlValues)
            => new TrueTypeVirtualMachineState(new TrueTypeStorageCapacity(storageCapacity), initialControlValues,
                new TrueTypeRasterizerEnvironment(new DevicePpemY(16), new TrueTypeUnitsPerEmScale(2048), true, true));

        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage)
            => new TrueTypeVirtualMachineFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}