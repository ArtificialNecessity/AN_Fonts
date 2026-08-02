using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using StbTrueTypeSharp.TrueTypeHinting.Geometry;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;

namespace StbTrueTypeSharp.TrueTypeHinting.Diagnostics
{
    /// <summary>Hard bounds for opt-in instruction tracing; tracing never changes VM execution limits.</summary>
    internal readonly struct TrueTypeExecutionTraceLimits
    {
        internal TrueTypeExecutionTraceLimits(int maximumEntryCount, int maximumStackValueCountPerEntry,
            int maximumPointCountPerZonePerEntry, int maximumTotalPointSnapshotCount = 1_000_000)
        {
            if (maximumEntryCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));
            if (maximumStackValueCountPerEntry <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStackValueCountPerEntry));
            if (maximumPointCountPerZonePerEntry <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPointCountPerZonePerEntry));
            if (maximumTotalPointSnapshotCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumTotalPointSnapshotCount));
            MaximumEntryCount = maximumEntryCount;
            MaximumStackValueCountPerEntry = maximumStackValueCountPerEntry;
            MaximumPointCountPerZonePerEntry = maximumPointCountPerZonePerEntry;
            MaximumTotalPointSnapshotCount = maximumTotalPointSnapshotCount;
        }

        internal int MaximumEntryCount { get; }
        internal int MaximumStackValueCountPerEntry { get; }
        internal int MaximumPointCountPerZonePerEntry { get; }
        internal int MaximumTotalPointSnapshotCount { get; }
        internal static TrueTypeExecutionTraceLimits ForTests() => new TrueTypeExecutionTraceLimits(4096, 256, 1024);
    }

    internal readonly struct TrueTypeTracedPoint
    {
        internal TrueTypeTracedPoint(int pointIndex, TrueTypeHintingPoint hintingPoint)
        {
            PointIndex = pointIndex;
            OriginalHorizontalF26Dot6 = hintingPoint.OriginalHorizontalF26Dot6;
            OriginalVerticalF26Dot6 = hintingPoint.OriginalVerticalF26Dot6;
            CurrentHorizontalF26Dot6 = hintingPoint.CurrentHorizontalF26Dot6;
            CurrentVerticalF26Dot6 = hintingPoint.CurrentVerticalF26Dot6;
            TouchFlags = hintingPoint.TouchFlags;
            IsOnCurve = hintingPoint.IsOnCurve;
        }

        internal int PointIndex { get; }
        internal int OriginalHorizontalF26Dot6 { get; }
        internal int OriginalVerticalF26Dot6 { get; }
        internal int CurrentHorizontalF26Dot6 { get; }
        internal int CurrentVerticalF26Dot6 { get; }
        internal TrueTypePointTouchFlags TouchFlags { get; }
        internal bool IsOnCurve { get; }
    }

    internal sealed class TrueTypeInstructionTraceEntry
    {
        internal TrueTypeInstructionTraceEntry(int traceEntrySequenceNumber, int instructionExecutionOrdinal,
            int callDepth, int instructionBytePosition,
            byte opcode, int[] operandStackValues, TrueTypeGraphicsState graphicsState,
            TrueTypeTracedPoint[] twilightPoints, TrueTypeTracedPoint[] glyphPoints, bool snapshotsTruncated)
        {
            TraceEntrySequenceNumber = traceEntrySequenceNumber;
            InstructionExecutionOrdinal = instructionExecutionOrdinal;
            CallDepth = callDepth;
            InstructionBytePosition = instructionBytePosition;
            Opcode = opcode;
            OperandStackValues = operandStackValues;
            ProjectionVector = graphicsState.ProjectionVector;
            FreedomVector = graphicsState.FreedomVector;
            DualProjectionVector = graphicsState.DualProjectionVector;
            ReferencePointZero = graphicsState.ReferencePointZero.Value;
            ReferencePointOne = graphicsState.ReferencePointOne.Value;
            ReferencePointTwo = graphicsState.ReferencePointTwo.Value;
            ZonePointerZero = graphicsState.ZonePointerZero.Value;
            ZonePointerOne = graphicsState.ZonePointerOne.Value;
            ZonePointerTwo = graphicsState.ZonePointerTwo.Value;
            LoopCount = graphicsState.LoopCount.Value;
            RoundingMode = graphicsState.RoundingMode;
            TwilightPoints = twilightPoints;
            GlyphPoints = glyphPoints;
            SnapshotsTruncated = snapshotsTruncated;
        }

        internal int TraceEntrySequenceNumber { get; }
        internal int InstructionExecutionOrdinal { get; }
        internal int CallDepth { get; }
        internal int InstructionBytePosition { get; }
        internal byte Opcode { get; }
        internal int[] OperandStackValues { get; }
        internal TrueTypeUnitVector ProjectionVector { get; }
        internal TrueTypeUnitVector FreedomVector { get; }
        internal TrueTypeUnitVector DualProjectionVector { get; }
        internal int ReferencePointZero { get; }
        internal int ReferencePointOne { get; }
        internal int ReferencePointTwo { get; }
        internal int ZonePointerZero { get; }
        internal int ZonePointerOne { get; }
        internal int ZonePointerTwo { get; }
        internal int LoopCount { get; }
        internal TrueTypeRoundingMode RoundingMode { get; }
        internal TrueTypeTracedPoint[] TwilightPoints { get; }
        internal TrueTypeTracedPoint[] GlyphPoints { get; }
        internal bool SnapshotsTruncated { get; }
    }

    /// <summary>Opt-in, bounded post-instruction snapshots for differential VM diagnosis.</summary>
    internal sealed class TrueTypeHintingExecutionTrace
    {
        private readonly TrueTypeExecutionTraceLimits _traceLimits;
        private readonly List<TrueTypeInstructionTraceEntry> _traceEntries;
        private int _capturedPointSnapshotCount;

        internal TrueTypeHintingExecutionTrace(TrueTypeExecutionTraceLimits traceLimits)
        {
            _traceLimits = traceLimits;
            _traceEntries = new List<TrueTypeInstructionTraceEntry>(Math.Min(traceLimits.MaximumEntryCount, 4096));
        }

        internal IReadOnlyList<TrueTypeInstructionTraceEntry> Entries => _traceEntries;
        internal bool WasTruncated { get; private set; }

        internal void Capture(int executedInstructionNumber, int callDepth, int instructionBytePosition, byte opcode,
            TrueTypeOperandStack operandStack, TrueTypeVirtualMachineState virtualMachineState,
            TrueTypeHintingExecutionZones executionZones)
        {
            if (_traceEntries.Count >= _traceLimits.MaximumEntryCount)
            {
                WasTruncated = true;
                return;
            }

            int[] stackValues = operandStack.SnapshotBottomToTop(_traceLimits.MaximumStackValueCountPerEntry,
                out bool stackTruncated);
            TrueTypeTracedPoint[] twilightPoints = SnapshotZone(executionZones?.TwilightZone, out bool twilightTruncated);
            TrueTypeTracedPoint[] glyphPoints = SnapshotZone(executionZones?.GlyphZone, out bool glyphTruncated);
            bool snapshotsTruncated = stackTruncated || twilightTruncated || glyphTruncated;
            WasTruncated |= snapshotsTruncated;
            _traceEntries.Add(new TrueTypeInstructionTraceEntry(_traceEntries.Count + 1,
                executedInstructionNumber, callDepth, instructionBytePosition, opcode, stackValues,
                virtualMachineState.GraphicsState, twilightPoints, glyphPoints, snapshotsTruncated));
        }

        internal string ToDeterministicText()
        {
            var text = new StringBuilder();
            foreach (TrueTypeInstructionTraceEntry entry in _traceEntries)
            {
                text.Append('#').Append(entry.TraceEntrySequenceNumber.ToString(CultureInfo.InvariantCulture))
                    .Append(" exec=").Append(entry.InstructionExecutionOrdinal.ToString(CultureInfo.InvariantCulture))
                    .Append(" depth=").Append(entry.CallDepth.ToString(CultureInfo.InvariantCulture))
                    .Append(" ip=").Append(entry.InstructionBytePosition.ToString(CultureInfo.InvariantCulture))
                    .Append(" op=0x").Append(entry.Opcode.ToString("X2", CultureInfo.InvariantCulture))
                    .Append(" stack=[");
                AppendIntegers(text, entry.OperandStackValues);
                text.Append("] pv=");
                AppendVector(text, entry.ProjectionVector);
                text.Append(" fv=");
                AppendVector(text, entry.FreedomVector);
                text.Append(" dpv=");
                AppendVector(text, entry.DualProjectionVector);
                text.Append(" rp=").Append(entry.ReferencePointZero).Append(',').Append(entry.ReferencePointOne).Append(',').Append(entry.ReferencePointTwo)
                    .Append(" zp=").Append(entry.ZonePointerZero).Append(',').Append(entry.ZonePointerOne).Append(',').Append(entry.ZonePointerTwo)
                    .Append(" loop=").Append(entry.LoopCount).Append(" round=").Append(entry.RoundingMode).AppendLine();
                AppendPoints(text, "z0", entry.TwilightPoints);
                AppendPoints(text, "z1", entry.GlyphPoints);
            }
            if (WasTruncated) text.AppendLine("[trace-truncated]");
            return text.ToString();
        }

        private TrueTypeTracedPoint[] SnapshotZone(TrueTypeHintingZone hintingZone, out bool zoneTruncated)
        {
            if (hintingZone == null)
            {
                zoneTruncated = false;
                return new TrueTypeTracedPoint[0];
            }
            int remainingTotalPointSnapshots = _traceLimits.MaximumTotalPointSnapshotCount - _capturedPointSnapshotCount;
            int tracedPointCount = Math.Min(Math.Min(hintingZone.PointCount,
                _traceLimits.MaximumPointCountPerZonePerEntry), Math.Max(0, remainingTotalPointSnapshots));
            var tracedPoints = new TrueTypeTracedPoint[tracedPointCount];
            for (int pointIndex = 0; pointIndex < tracedPointCount; pointIndex++)
            {
                hintingZone.TryGetPoint(pointIndex, out TrueTypeHintingPoint hintingPoint);
                tracedPoints[pointIndex] = new TrueTypeTracedPoint(pointIndex, hintingPoint);
            }
            _capturedPointSnapshotCount += tracedPointCount;
            zoneTruncated = tracedPointCount < hintingZone.PointCount;
            return tracedPoints;
        }

        private static void AppendIntegers(StringBuilder text, int[] values)
        {
            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                if (valueIndex != 0) text.Append(',');
                text.Append(values[valueIndex].ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AppendVector(StringBuilder text, TrueTypeUnitVector vector)
            => text.Append('(').Append(vector.HorizontalComponent.Value).Append(',').Append(vector.VerticalComponent.Value).Append(')');

        private static void AppendPoints(StringBuilder text, string zoneName, TrueTypeTracedPoint[] points)
        {
            foreach (TrueTypeTracedPoint point in points)
            {
                text.Append(zoneName).Append('[').Append(point.PointIndex).Append("] o=")
                    .Append('(').Append(point.OriginalHorizontalF26Dot6).Append(',').Append(point.OriginalVerticalF26Dot6).Append(')')
                    .Append(" c=").Append('(').Append(point.CurrentHorizontalF26Dot6).Append(',').Append(point.CurrentVerticalF26Dot6).Append(')')
                    .Append(" touch=").Append((int)point.TouchFlags).Append(" on=").Append(point.IsOnCurve ? 1 : 0).AppendLine();
            }
        }
    }
}