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
        internal int DeltaBasePpem { get; set; } = 9;
        internal int DeltaShift { get; set; } = 3;
        internal int ScanControlFlags { get; set; } = 0;
        internal int ScanType { get; set; } = 0;
        internal int InstructionControlFlags { get; set; } = 0;
        internal bool AutoFlip { get; set; } = true;

        /// <summary>Copies the prepared size defaults and resets glyph-local references.</summary>
        internal TrueTypeGraphicsState CloneForGlyphExecution()
        {
            return new TrueTypeGraphicsState
            {
                ProjectionVector = ProjectionVector,
                FreedomVector = FreedomVector,
                DualProjectionVector = DualProjectionVector,
                ReferencePointZero = new TrueTypeReferencePointIndex(0),
                ReferencePointOne = new TrueTypeReferencePointIndex(0),
                ReferencePointTwo = new TrueTypeReferencePointIndex(0),
                ZonePointerZero = new TrueTypeZonePointerIndex(1),
                ZonePointerOne = new TrueTypeZonePointerIndex(1),
                ZonePointerTwo = new TrueTypeZonePointerIndex(1),
                LoopCount = new TrueTypeLoopCount(1),
                RoundingMode = RoundingMode,
                MinimumDistanceF26Dot6 = MinimumDistanceF26Dot6,
                ControlValueCutInF26Dot6 = ControlValueCutInF26Dot6,
                SingleWidthCutInF26Dot6 = SingleWidthCutInF26Dot6,
                SingleWidthValueF26Dot6 = SingleWidthValueF26Dot6,
                DeltaBasePpem = DeltaBasePpem,
                DeltaShift = DeltaShift,
                ScanControlFlags = ScanControlFlags,
                ScanType = ScanType,
                InstructionControlFlags = InstructionControlFlags,
                AutoFlip = AutoFlip,
            };
        }

        internal TrueTypeGraphicsState ClonePreparedState()
        {
            TrueTypeGraphicsState clonedGraphicsState = CloneForGlyphExecution();
            clonedGraphicsState.ReferencePointZero = ReferencePointZero;
            clonedGraphicsState.ReferencePointOne = ReferencePointOne;
            clonedGraphicsState.ReferencePointTwo = ReferencePointTwo;
            clonedGraphicsState.ZonePointerZero = ZonePointerZero;
            clonedGraphicsState.ZonePointerOne = ZonePointerOne;
            clonedGraphicsState.ZonePointerTwo = ZonePointerTwo;
            clonedGraphicsState.LoopCount = LoopCount;
            return clonedGraphicsState;
        }
    }
}