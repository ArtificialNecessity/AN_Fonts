using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Variations
{
	/// <summary>
	/// Parsed 'avar' table (OpenType 1.9.1 — _EXTERNAL_APIS/OpenType/avar.md): one piecewise-linear
	/// segment map per fvar axis remapping DEFAULT-normalized coordinates to final normalized coordinates.
	/// Evaluated in DOUBLE, exactly like fontTools' piecewiseLinearMap (our fixture oracle); the caller
	/// quantizes to F2Dot14 ONCE, AFTER this remap (see README_variations_implementation_notes.md §1).
	/// avar v2 extras (VarIdxMap/VarStore) are not applied (Part C); v1 segment maps are honored for any version.
	/// </summary>
	public sealed class OpenTypeAvarTable
	{
		/// <summary>Per axis: sorted fromCoordinate keys, or null when the axis has no (valid) map => identity.</summary>
		private readonly double[][] _fromCoordinatesPerAxis;
		private readonly double[][] _toCoordinatesPerAxis;

		public int AxisCount => _fromCoordinatesPerAxis.Length;
		public ushort MajorVersion { get; }

		private OpenTypeAvarTable(double[][] fromCoordinatesPerAxis, double[][] toCoordinatesPerAxis, ushort majorVersion)
		{
			_fromCoordinatesPerAxis = fromCoordinatesPerAxis;
			_toCoordinatesPerAxis = toCoordinatesPerAxis;
			MajorVersion = majorVersion;
		}

		/// <summary>True when the axis carries a non-identity segment map (useful for tests/diagnostics).</summary>
		public bool HasSegmentMap(int axisIndex)
			=> axisIndex >= 0 && axisIndex < _fromCoordinatesPerAxis.Length && _fromCoordinatesPerAxis[axisIndex] != null;

		/// <summary>Number of (from,to) pairs for the axis (0 when identity).</summary>
		public int GetSegmentMapCount(int axisIndex)
			=> HasSegmentMap(axisIndex) ? _fromCoordinatesPerAxis[axisIndex].Length : 0;

		public void GetSegmentMapPair(int axisIndex, int pairIndex, out double fromCoordinate, out double toCoordinate)
		{
			fromCoordinate = _fromCoordinatesPerAxis[axisIndex][pairIndex];
			toCoordinate = _toCoordinatesPerAxis[axisIndex][pairIndex];
		}

		/// <summary>
		/// fontTools.varLib.models.piecewiseLinearMap semantics: exact key hit => mapped value; below the
		/// first key / above the last => shift by that endpoint's (to - from); otherwise linear between the
		/// bracketing keys. Identity for axes without a map.
		/// </summary>
		public double Remap(int axisIndex, double defaultNormalizedValue)
		{
			if (!HasSegmentMap(axisIndex))
				return defaultNormalizedValue;
			double[] from = _fromCoordinatesPerAxis[axisIndex];
			double[] to = _toCoordinatesPerAxis[axisIndex];
			int n = from.Length;
			double v = defaultNormalizedValue;
			if (v <= from[0])
				return v == from[0] ? to[0] : v + to[0] - from[0];
			if (v >= from[n - 1])
				return v == from[n - 1] ? to[n - 1] : v + to[n - 1] - from[n - 1];
			// find the first key >= v (keys are strictly increasing)
			int hi = 1;
			while (from[hi] < v) hi++;
			if (from[hi] == v)
				return to[hi];
			int lo = hi - 1;
			double a = from[lo], b = from[hi];
			double va = to[lo], vb = to[hi];
			return va + (vb - va) * (v - a) / (b - a);
		}

		internal static double ReadF2Dot14(FakePtr<byte> p) => ttSHORT(p) / 16384.0;

		public static bool TryParse(FakePtr<byte> fontData, int tableOffset, int tableLength, int fvarAxisCount,
			out OpenTypeAvarTable table, out FontVariationParseFailure failure)
		{
			table = null;
			if (tableLength < 8 || tableOffset < 0 || tableOffset + tableLength > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.AvarTruncatedHeader, "avar", "table shorter than the 8-byte header or outside the font data");
				return false;
			}
			var t = fontData + tableOffset;
			ushort majorVersion = ttUSHORT(t);
			int axisCount = ttUSHORT(t + 6);
			if (axisCount != fvarAxisCount)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.AvarAxisCountMismatch, "avar", "axisCount " + axisCount + " != fvar axisCount " + fvarAxisCount);
				return false;
			}
			var fromPerAxis = new double[axisCount][];
			var toPerAxis = new double[axisCount][];
			int cursor = 8;
			for (int axis = 0; axis < axisCount; axis++)
			{
				if (cursor + 2 > tableLength)
				{
					failure = new FontVariationParseFailure(FontVariationParseFailureCode.AvarSegmentMapOutOfBounds, "avar", "segment map " + axis + " header beyond table end");
					return false;
				}
				int positionMapCount = ttUSHORT(t + cursor);
				cursor += 2;
				if (cursor + positionMapCount * 4 > tableLength)
				{
					failure = new FontVariationParseFailure(FontVariationParseFailureCode.AvarSegmentMapOutOfBounds, "avar", "segment map " + axis + " records beyond table end");
					return false;
				}
				// Collect strictly-increasing fromCoordinate records (spec: a record whose from <= the previous
				// from, or whose to < the previous to, MAY be ignored). fontTools keeps a from->to dict; last wins
				// on duplicates — identical outcome for well-formed fonts, lenient for sloppy ones.
				var from = new double[positionMapCount];
				var to = new double[positionMapCount];
				int kept = 0;
				for (int i = 0; i < positionMapCount; i++)
				{
					double f = ReadF2Dot14(t + cursor + i * 4);
					double m = ReadF2Dot14(t + cursor + i * 4 + 2);
					if (kept > 0 && f == from[kept - 1]) { to[kept - 1] = m; continue; }   // duplicate from: last wins
					if (kept > 0 && (f < from[kept - 1] || m < to[kept - 1])) continue; // retrograde: ignore
					from[kept] = f; to[kept] = m; kept++;
				}
				cursor += positionMapCount * 4;
				// Spec: a map is only in effect if it contains the -1/-1, 0/0 and 1/1 anchors; otherwise NO modification.
				bool hasAnchors = kept >= 3 && ContainsPair(from, to, kept, -1.0, -1.0) && ContainsPair(from, to, kept, 0.0, 0.0) && ContainsPair(from, to, kept, 1.0, 1.0);
				if (!hasAnchors)
					continue; // identity for this axis
				if (kept != from.Length)
				{
					var f2 = new double[kept]; var t2 = new double[kept];
					System.Array.Copy(from, f2, kept); System.Array.Copy(to, t2, kept);
					from = f2; to = t2;
				}
				fromPerAxis[axis] = from;
				toPerAxis[axis] = to;
			}
			table = new OpenTypeAvarTable(fromPerAxis, toPerAxis, majorVersion);
			failure = FontVariationParseFailure.None;
			return true;
		}

		private static bool ContainsPair(double[] from, double[] to, int count, double f, double m)
		{
			for (int i = 0; i < count; i++)
				if (from[i] == f && to[i] == m) return true;
			return false;
		}
	}
}