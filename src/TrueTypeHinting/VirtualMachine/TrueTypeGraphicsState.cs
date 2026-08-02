using System;

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

    /// <summary>A signed F2Dot14 vector component.</summary>
    internal readonly struct TrueTypeVectorComponent
    {
        internal TrueTypeVectorComponent(int trueTypeVectorComponentValue)
        {
            if (trueTypeVectorComponentValue < short.MinValue || trueTypeVectorComponentValue > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(trueTypeVectorComponentValue));
            Value = trueTypeVectorComponentValue;
        }
        internal int Value { get; }
    }

    internal readonly struct TrueTypeUnitVector
    {
        internal TrueTypeUnitVector(TrueTypeVectorComponent horizontalComponent, TrueTypeVectorComponent verticalComponent)
        {
            HorizontalComponent = horizontalComponent;
            VerticalComponent = verticalComponent;
        }
        internal TrueTypeVectorComponent HorizontalComponent { get; }
        internal TrueTypeVectorComponent VerticalComponent { get; }
        internal static TrueTypeUnitVector Horizontal => new TrueTypeUnitVector(new TrueTypeVectorComponent(0x4000), new TrueTypeVectorComponent(0));
        internal static TrueTypeUnitVector Vertical => new TrueTypeUnitVector(new TrueTypeVectorComponent(0), new TrueTypeVectorComponent(0x4000));
    }

    internal enum TrueTypeRoundingMode
    {
        ToHalfGrid,
        ToGrid,
        ToDoubleGrid,
        DownToGrid,
        UpToGrid,
        Off,
        Super,
        Super45,
    }

    /// <summary>Font/size graphics state; point-dependent fields are consumed by geometry phases.</summary>
    internal sealed class TrueTypeGraphicsState
    {
        internal TrueTypeUnitVector ProjectionVector { get; set; } = TrueTypeUnitVector.Horizontal;
        internal TrueTypeUnitVector FreedomVector { get; set; } = TrueTypeUnitVector.Horizontal;
        internal TrueTypeUnitVector DualProjectionVector { get; set; } = TrueTypeUnitVector.Horizontal;
        internal TrueTypeReferencePointIndex ReferencePointZero { get; set; } = new TrueTypeReferencePointIndex(0);
        internal TrueTypeReferencePointIndex ReferencePointOne { get; set; } = new TrueTypeReferencePointIndex(0);
        internal TrueTypeReferencePointIndex ReferencePointTwo { get; set; } = new TrueTypeReferencePointIndex(0);
        internal TrueTypeZonePointerIndex ZonePointerZero { get; set; } = new TrueTypeZonePointerIndex(1);
        internal TrueTypeZonePointerIndex ZonePointerOne { get; set; } = new TrueTypeZonePointerIndex(1);
        internal TrueTypeZonePointerIndex ZonePointerTwo { get; set; } = new TrueTypeZonePointerIndex(1);
        internal TrueTypeLoopCount LoopCount { get; set; } = new TrueTypeLoopCount(1);
        internal TrueTypeRoundingMode RoundingMode { get; set; } = TrueTypeRoundingMode.ToGrid;
        internal int MinimumDistanceF26Dot6 { get; set; } = 64;
        internal int ControlValueCutInF26Dot6 { get; set; } = 68;
        internal int SingleWidthCutInF26Dot6 { get; set; } = 0;
        internal int SingleWidthValueF26Dot6 { get; set; } = 0;
        internal bool AutoFlip { get; set; } = true;
    }
}