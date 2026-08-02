using System;

namespace StbTrueTypeSharp.TrueTypeHinting.Geometry
{
    internal readonly struct TrueTypeContourEndPointIndex
    {
        internal TrueTypeContourEndPointIndex(int trueTypeContourEndPointIndexValue)
        {
            if (trueTypeContourEndPointIndexValue < 0)
                throw new ArgumentOutOfRangeException(nameof(trueTypeContourEndPointIndexValue));
            Value = trueTypeContourEndPointIndexValue;
        }
        internal int Value { get; }
    }

    /// <summary>One TrueType point zone containing original/current coordinates and touched flags.</summary>
    internal sealed class TrueTypeHintingZone
    {
        private readonly TrueTypeHintingPoint[] _trueTypeHintingPoints;
        private readonly TrueTypeContourEndPointIndex[] _trueTypeContourEndPointIndices;

        internal TrueTypeHintingZone(TrueTypeHintingPoint[] trueTypeHintingPoints,
            TrueTypeContourEndPointIndex[] trueTypeContourEndPointIndices)
        {
            if (trueTypeHintingPoints == null) throw new ArgumentNullException(nameof(trueTypeHintingPoints));
            if (trueTypeContourEndPointIndices == null) throw new ArgumentNullException(nameof(trueTypeContourEndPointIndices));
            _trueTypeHintingPoints = ClonePoints(trueTypeHintingPoints);
            _trueTypeContourEndPointIndices = (TrueTypeContourEndPointIndex[])trueTypeContourEndPointIndices.Clone();
        }

        internal int PointCount => _trueTypeHintingPoints.Length;
        internal int ContourCount => _trueTypeContourEndPointIndices.Length;

        internal bool TryGetPoint(int trueTypePointIndex, out TrueTypeHintingPoint trueTypeHintingPoint)
        {
            if (trueTypePointIndex < 0 || trueTypePointIndex >= _trueTypeHintingPoints.Length)
            {
                trueTypeHintingPoint = null;
                return false;
            }
            trueTypeHintingPoint = _trueTypeHintingPoints[trueTypePointIndex];
            return true;
        }

        internal TrueTypeContourEndPointIndex GetContourEndPointIndex(int trueTypeContourIndex)
            => _trueTypeContourEndPointIndices[trueTypeContourIndex];

        internal TrueTypeHintingZone Clone()
            => new TrueTypeHintingZone(_trueTypeHintingPoints, _trueTypeContourEndPointIndices);

        internal TrueTypeHintingPoint[] ClonePointsForResult() => ClonePoints(_trueTypeHintingPoints);

        private static TrueTypeHintingPoint[] ClonePoints(TrueTypeHintingPoint[] trueTypeHintingPoints)
        {
            var clonedTrueTypeHintingPoints = new TrueTypeHintingPoint[trueTypeHintingPoints.Length];
            for (int trueTypePointIndex = 0; trueTypePointIndex < trueTypeHintingPoints.Length; trueTypePointIndex++)
                clonedTrueTypeHintingPoints[trueTypePointIndex] = trueTypeHintingPoints[trueTypePointIndex].Clone();
            return clonedTrueTypeHintingPoints;
        }
    }
}