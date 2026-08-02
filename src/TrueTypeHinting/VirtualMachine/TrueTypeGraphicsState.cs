namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    internal readonly struct TrueTypeReferencePointIndex
    {
        internal TrueTypeReferencePointIndex(int trueTypeReferencePointIndexValue) => Value = trueTypeReferencePointIndexValue;
        internal int Value { get; }
    }

    internal readonly struct TrueTypeZonePointerIndex
    {
        internal TrueTypeZonePointerIndex(int trueTypeZonePointerIndexValue) => Value = trueTypeZonePointerIndexValue;
        internal int Value { get; }
    }

    internal readonly struct TrueTypeLoopCount
    {
        internal TrueTypeLoopCount(int trueTypeLoopCountValue) => Value = trueTypeLoopCountValue;
        internal int Value { get; }
    }

    /// <summary>Per-execution TrueType graphics state; geometry phases will extend this type.</summary>
    internal sealed class TrueTypeGraphicsState
    {
        internal TrueTypeReferencePointIndex ReferencePointZero { get; set; } = new TrueTypeReferencePointIndex(0);
        internal TrueTypeReferencePointIndex ReferencePointOne { get; set; } = new TrueTypeReferencePointIndex(0);
        internal TrueTypeReferencePointIndex ReferencePointTwo { get; set; } = new TrueTypeReferencePointIndex(0);
        internal TrueTypeZonePointerIndex ZonePointerZero { get; set; } = new TrueTypeZonePointerIndex(1);
        internal TrueTypeZonePointerIndex ZonePointerOne { get; set; } = new TrueTypeZonePointerIndex(1);
        internal TrueTypeZonePointerIndex ZonePointerTwo { get; set; } = new TrueTypeZonePointerIndex(1);
        internal TrueTypeLoopCount LoopCount { get; set; } = new TrueTypeLoopCount(1);
    }
}