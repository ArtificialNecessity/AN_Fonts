using System;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;

namespace StbTrueTypeSharp.TrueTypeHinting.Geometry
{
    /// <summary>Twilight (zone 0) and glyph (zone 1) point spaces for one glyph execution.</summary>
    internal sealed class TrueTypeHintingExecutionZones
    {
        internal TrueTypeHintingExecutionZones(TrueTypeHintingZone twilightZone, TrueTypeHintingZone glyphZone)
        {
            TwilightZone = twilightZone ?? throw new ArgumentNullException(nameof(twilightZone));
            GlyphZone = glyphZone ?? throw new ArgumentNullException(nameof(glyphZone));
        }

        internal TrueTypeHintingZone TwilightZone { get; }
        internal TrueTypeHintingZone GlyphZone { get; }

        internal bool TryGetZone(TrueTypeZonePointerIndex zonePointerIndex, out TrueTypeHintingZone hintingZone,
            out TrueTypeVirtualMachineFailure zoneFailure)
        {
            switch (zonePointerIndex.Value)
            {
                case 0: hintingZone = TwilightZone; zoneFailure = default; return true;
                case 1: hintingZone = GlyphZone; zoneFailure = default; return true;
                default:
                    hintingZone = null;
                    zoneFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidZonePointer,
                        "TrueType zone pointer must select twilight zone 0 or glyph zone 1.");
                    return false;
            }
        }

        internal static TrueTypeHintingExecutionZones Create(TrueTypeHintingZone glyphZone, int maximumTwilightPointCount)
        {
            var twilightPoints = new TrueTypeHintingPoint[Math.Max(0, maximumTwilightPointCount)];
            for (int twilightPointIndex = 0; twilightPointIndex < twilightPoints.Length; twilightPointIndex++)
                twilightPoints[twilightPointIndex] = new TrueTypeHintingPoint(0, 0, true);
            var twilightContours = twilightPoints.Length == 0
                ? new TrueTypeContourEndPointIndex[0]
                : new[] { new TrueTypeContourEndPointIndex(twilightPoints.Length - 1) };
            return new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(twilightPoints, twilightContours), glyphZone.Clone());
        }

        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage)
            => new TrueTypeVirtualMachineFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}