namespace StbTrueTypeSharp.Variations
{
	/// <summary>
	/// The per-region scalar ("tent" function) shared by gvar tuple variation headers and the
	/// ItemVariationStore region list. OT 1.9.1 Overview "Algorithm for Interpolation of Instance Values"
	/// with fontTools' supportScalar(ot=True) handling of ill-formed regions (README §2):
	/// every ill-formed axis yields FACTOR 1 (axis ignored), never 0.
	/// </summary>
	public static class OpenTypeVariationRegionScalar
	{
		/// <summary>
		/// Product over axes of the per-axis scalar. start/peak/end are normalized coordinates in [-1, 1]
		/// (already converted from F2Dot14). For a non-intermediate region pass start = min(0, peak),
		/// end = max(0, peak) — see <see cref="ComputeNonIntermediate"/>.
		/// </summary>
		public static double Compute(in FontVariationNormalizedCoordinates instance, double[] start, double[] peak, double[] end, int axisCount)
		{
			double scalar = 1.0;
			for (int axis = 0; axis < axisCount; axis++)
			{
				double p = peak[axis];
				if (p == 0.0)
					continue;                                   // axis does not participate
				double s = start[axis], e = end[axis];
				if (s > p || p > e)
					continue;                                   // malformed region on this axis: ignore the axis
				if (s < 0.0 && e > 0.0)
					continue;                                   // straddles zero with a nonzero peak: ignore the axis
				double v = instance.GetNormalized(axis);
				if (v == p)
					continue;                                   // exactly at the peak: factor 1
				if (v <= s || v >= e)
					return 0.0;                                 // outside the region: the whole region contributes nothing
				scalar *= v < p ? (v - s) / (p - s) : (e - v) / (e - p);
			}
			return scalar;
		}

		/// <summary>Region defined by a peak tuple alone: start = min(0, peak), end = max(0, peak) per axis.</summary>
		public static double ComputeNonIntermediate(in FontVariationNormalizedCoordinates instance, double[] peak, int axisCount)
		{
			double scalar = 1.0;
			for (int axis = 0; axis < axisCount; axis++)
			{
				double p = peak[axis];
				if (p == 0.0)
					continue;
				double v = instance.GetNormalized(axis);
				if (v == p)
					continue;
				double s = p < 0.0 ? p : 0.0;
				double e = p > 0.0 ? p : 0.0;
				if (v <= s || v >= e)
					return 0.0;
				scalar *= v < p ? (v - s) / (p - s) : (e - v) / (e - p);
			}
			return scalar;
		}
	}
}