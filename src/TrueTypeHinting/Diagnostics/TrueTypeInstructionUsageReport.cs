using System;
using System.Collections.Generic;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;

namespace StbTrueTypeSharp.TrueTypeHinting.Diagnostics
{
    public readonly struct TrueTypeOpcodeValue
    {
        internal TrueTypeOpcodeValue(byte trueTypeOpcodeByteValue) => Value = trueTypeOpcodeByteValue;
        public byte Value { get; }
        public override string ToString() => "0x" + Value.ToString("X2");
    }

    public readonly struct TrueTypeOpcodeOccurrenceCount
    {
        internal TrueTypeOpcodeOccurrenceCount(int trueTypeOpcodeOccurrenceCountValue) => Value = trueTypeOpcodeOccurrenceCountValue;
        public int Value { get; }
    }

    public sealed class TrueTypeOpcodeUsageEntry
    {
        internal TrueTypeOpcodeUsageEntry(TrueTypeOpcodeValue trueTypeOpcode, TrueTypeOpcodeOccurrenceCount occurrenceCount)
        {
            TrueTypeOpcode = trueTypeOpcode;
            OccurrenceCount = occurrenceCount;
        }
        public TrueTypeOpcodeValue TrueTypeOpcode { get; }
        public TrueTypeOpcodeOccurrenceCount OccurrenceCount { get; }
    }

    /// <summary>Static opcode inventory that excludes immediate PUSH payload bytes.</summary>
    public sealed class TrueTypeInstructionUsageReport
    {
        internal TrueTypeInstructionUsageReport(TrueTypeOpcodeUsageEntry[] opcodeUsageEntries,
            TrueTypeYHintingFailure analysisFailure)
        {
            OpcodeUsageEntries = opcodeUsageEntries ?? new TrueTypeOpcodeUsageEntry[0];
            AnalysisFailure = analysisFailure;
        }

        public IReadOnlyList<TrueTypeOpcodeUsageEntry> OpcodeUsageEntries { get; }
        public TrueTypeYHintingFailure AnalysisFailure { get; }
        public bool Succeeded => !AnalysisFailure.HasFailure;
    }

    public static class TrueTypeInstructionUsageAnalyzer
    {
        public static TrueTypeInstructionUsageReport Analyze(TrueTypeTableBytes trueTypeInstructionTableBytes)
        {
            if (trueTypeInstructionTableBytes == null) throw new ArgumentNullException(nameof(trueTypeInstructionTableBytes));
            byte[] trueTypeInstructionBytes = trueTypeInstructionTableBytes.ToByteArray();
            var trueTypeOpcodeCounts = new SortedDictionary<byte, int>();
            int instructionBytePosition = 0;

            while (instructionBytePosition < trueTypeInstructionBytes.Length)
            {
                byte trueTypeOpcodeByte = trueTypeInstructionBytes[instructionBytePosition++];
                trueTypeOpcodeCounts.TryGetValue(trueTypeOpcodeByte, out int existingOccurrenceCount);
                trueTypeOpcodeCounts[trueTypeOpcodeByte] = existingOccurrenceCount + 1;

                int immediatePayloadByteCount;
                if (trueTypeOpcodeByte >= 0xB0 && trueTypeOpcodeByte <= 0xB7)
                    immediatePayloadByteCount = trueTypeOpcodeByte - 0xAF;
                else if (trueTypeOpcodeByte >= 0xB8 && trueTypeOpcodeByte <= 0xBF)
                    immediatePayloadByteCount = (trueTypeOpcodeByte - 0xB7) * 2;
                else if (trueTypeOpcodeByte == 0x40 || trueTypeOpcodeByte == 0x41)
                {
                    if (instructionBytePosition >= trueTypeInstructionBytes.Length)
                        return Failed("A variable PUSH instruction is missing its operand count.");
                    int pushedValueCount = trueTypeInstructionBytes[instructionBytePosition++];
                    immediatePayloadByteCount = pushedValueCount * (trueTypeOpcodeByte == 0x41 ? 2 : 1);
                }
                else
                    immediatePayloadByteCount = 0;

                if (instructionBytePosition > trueTypeInstructionBytes.Length - immediatePayloadByteCount)
                    return Failed("A PUSH payload extends beyond the instruction table.");
                instructionBytePosition += immediatePayloadByteCount;
            }

            var trueTypeOpcodeUsageEntries = new TrueTypeOpcodeUsageEntry[trueTypeOpcodeCounts.Count];
            int trueTypeOpcodeUsageEntryIndex = 0;
            foreach (KeyValuePair<byte, int> trueTypeOpcodeCount in trueTypeOpcodeCounts)
            {
                trueTypeOpcodeUsageEntries[trueTypeOpcodeUsageEntryIndex++] = new TrueTypeOpcodeUsageEntry(
                    new TrueTypeOpcodeValue(trueTypeOpcodeCount.Key),
                    new TrueTypeOpcodeOccurrenceCount(trueTypeOpcodeCount.Value));
            }
            return new TrueTypeInstructionUsageReport(trueTypeOpcodeUsageEntries, default);
        }

        private static TrueTypeInstructionUsageReport Failed(string trueTypeInstructionAnalysisFailureMessage)
            => new TrueTypeInstructionUsageReport(new TrueTypeOpcodeUsageEntry[0],
                new TrueTypeYHintingFailure(TrueTypeHintingFailureCode.FontProgramExecutionFailed,
                    new TrueTypeHintingFailureMessage(trueTypeInstructionAnalysisFailureMessage)));
    }
}