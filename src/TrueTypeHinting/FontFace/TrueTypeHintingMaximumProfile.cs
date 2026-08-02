using System;

namespace StbTrueTypeSharp.TrueTypeHinting.FontFace
{
    public readonly struct TrueTypeMaximumPointCount { internal TrueTypeMaximumPointCount(int value) => Value = value; public int Value { get; } }
    public readonly struct TrueTypeMaximumContourCount { internal TrueTypeMaximumContourCount(int value) => Value = value; public int Value { get; } }
    public readonly struct TrueTypeMaximumZoneCount { internal TrueTypeMaximumZoneCount(int value) => Value = value; public int Value { get; } }
    public readonly struct TrueTypeMaximumTwilightPointCount { internal TrueTypeMaximumTwilightPointCount(int value) => Value = value; public int Value { get; } }
    public readonly struct TrueTypeMaximumStorageCount { internal TrueTypeMaximumStorageCount(int value) => Value = value; public int Value { get; } }
    public readonly struct TrueTypeMaximumFunctionDefinitionCount { internal TrueTypeMaximumFunctionDefinitionCount(int value) => Value = value; public int Value { get; } }
    public readonly struct TrueTypeMaximumInstructionDefinitionCount { internal TrueTypeMaximumInstructionDefinitionCount(int value) => Value = value; public int Value { get; } }
    public readonly struct TrueTypeMaximumOperandStackCount { internal TrueTypeMaximumOperandStackCount(int value) => Value = value; public int Value { get; } }
    public readonly struct TrueTypeMaximumInstructionByteCount { internal TrueTypeMaximumInstructionByteCount(int value) => Value = value; public int Value { get; } }

    /// <summary>Bounds declared by a TrueType maxp version-1 table.</summary>
    public sealed class TrueTypeHintingMaximumProfile
    {
        internal TrueTypeHintingMaximumProfile(ushort maximumPointCount, ushort maximumContourCount,
            ushort maximumZoneCount, ushort maximumTwilightPointCount, ushort maximumStorageCount,
            ushort maximumFunctionDefinitionCount, ushort maximumInstructionDefinitionCount,
            ushort maximumOperandStackCount, ushort maximumInstructionByteCount)
        {
            MaximumPointCount = new TrueTypeMaximumPointCount(maximumPointCount);
            MaximumContourCount = new TrueTypeMaximumContourCount(maximumContourCount);
            MaximumZoneCount = new TrueTypeMaximumZoneCount(maximumZoneCount);
            MaximumTwilightPointCount = new TrueTypeMaximumTwilightPointCount(maximumTwilightPointCount);
            MaximumStorageCount = new TrueTypeMaximumStorageCount(maximumStorageCount);
            MaximumFunctionDefinitionCount = new TrueTypeMaximumFunctionDefinitionCount(maximumFunctionDefinitionCount);
            MaximumInstructionDefinitionCount = new TrueTypeMaximumInstructionDefinitionCount(maximumInstructionDefinitionCount);
            MaximumOperandStackCount = new TrueTypeMaximumOperandStackCount(maximumOperandStackCount);
            MaximumInstructionByteCount = new TrueTypeMaximumInstructionByteCount(maximumInstructionByteCount);
        }

        public TrueTypeMaximumPointCount MaximumPointCount { get; }
        public TrueTypeMaximumContourCount MaximumContourCount { get; }
        public TrueTypeMaximumZoneCount MaximumZoneCount { get; }
        public TrueTypeMaximumTwilightPointCount MaximumTwilightPointCount { get; }
        public TrueTypeMaximumStorageCount MaximumStorageCount { get; }
        public TrueTypeMaximumFunctionDefinitionCount MaximumFunctionDefinitionCount { get; }
        public TrueTypeMaximumInstructionDefinitionCount MaximumInstructionDefinitionCount { get; }
        public TrueTypeMaximumOperandStackCount MaximumOperandStackCount { get; }
        public TrueTypeMaximumInstructionByteCount MaximumInstructionByteCount { get; }
    }
}