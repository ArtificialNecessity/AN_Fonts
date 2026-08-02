namespace StbTrueTypeSharp.TrueTypeHinting.Geometry
{
    [System.Flags]
    internal enum TrueTypePointTouchFlags : byte
    {
        None = 0,
        Horizontal = 1,
        Vertical = 2,
    }

    /// <summary>One scaled TrueType outline point with immutable original and mutable current coordinates.</summary>
    internal sealed class TrueTypeHintingPoint
    {
        internal TrueTypeHintingPoint(int originalHorizontalF26Dot6, int originalVerticalF26Dot6, bool isOnCurve)
        {
            OriginalHorizontalF26Dot6 = originalHorizontalF26Dot6;
            OriginalVerticalF26Dot6 = originalVerticalF26Dot6;
            CurrentHorizontalF26Dot6 = originalHorizontalF26Dot6;
            CurrentVerticalF26Dot6 = originalVerticalF26Dot6;
            IsOnCurve = isOnCurve;
        }

        internal int OriginalHorizontalF26Dot6 { get; set; }
        internal int OriginalVerticalF26Dot6 { get; set; }
        internal int CurrentHorizontalF26Dot6 { get; set; }
        internal int CurrentVerticalF26Dot6 { get; set; }
        internal bool IsOnCurve { get; }
        internal TrueTypePointTouchFlags TouchFlags { get; set; }
        internal bool IsTouchedHorizontally => (TouchFlags & TrueTypePointTouchFlags.Horizontal) != 0;
        internal bool IsTouchedVertically => (TouchFlags & TrueTypePointTouchFlags.Vertical) != 0;

        internal TrueTypeHintingPoint Clone()
            => new TrueTypeHintingPoint(OriginalHorizontalF26Dot6, OriginalVerticalF26Dot6, IsOnCurve)
            {
                CurrentHorizontalF26Dot6 = CurrentHorizontalF26Dot6,
                CurrentVerticalF26Dot6 = CurrentVerticalF26Dot6,
                TouchFlags = TouchFlags,
            };
    }
}