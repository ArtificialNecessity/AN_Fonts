using System;

namespace StbTrueTypeSharp.Variations
{
	/// <summary>
	/// A position in a font's NORMALIZED variation space: one F2Dot14 value per fvar axis, in fvar
	/// axis order (-16384 .. +16384 == -1.0 .. +1.0). Produced by
	/// <see cref="FontInfo.stbtt_NormalizeVariationRequest"/>; consumed by every *Var outline/metrics
	/// entry point. The DEFAULT instance (all zero, or a static font) is <see cref="Default"/> and MUST
	/// route to the un-varied code paths byte-for-byte — that is the no-op proof.
	/// This struct is the coordinate tuple only; cache keying is the consumer's dense per-face instance id.
	/// </summary>
	public readonly struct FontVariationNormalizedCoordinates : IEquatable<FontVariationNormalizedCoordinates>
	{
		/// <summary>Null for the default instance / static font; otherwise one F2Dot14 per axis.</summary>
		private readonly short[] _f2dot14PerAxis;

		public static readonly FontVariationNormalizedCoordinates Default = default;

		/// <summary>Takes ownership of the array. An all-zero array collapses to <see cref="Default"/>.</summary>
		public FontVariationNormalizedCoordinates(short[] f2dot14PerAxis)
		{
			if (f2dot14PerAxis != null)
			{
				bool allZero = true;
				for (int i = 0; i < f2dot14PerAxis.Length; i++)
					if (f2dot14PerAxis[i] != 0) { allZero = false; break; }
				if (allZero)
					f2dot14PerAxis = null;
			}
			_f2dot14PerAxis = f2dot14PerAxis;
		}

		/// <summary>True for the default instance (no variation applies) and for static fonts.</summary>
		public bool IsDefault => _f2dot14PerAxis == null;

		/// <summary>Number of axes carried (0 for Default).</summary>
		public int AxisCount => _f2dot14PerAxis == null ? 0 : _f2dot14PerAxis.Length;

		/// <summary>Raw F2Dot14 for axis i (0 beyond AxisCount — Default answers 0 for every axis).</summary>
		public short GetF2Dot14(int axisIndex)
			=> _f2dot14PerAxis == null || axisIndex < 0 || axisIndex >= _f2dot14PerAxis.Length ? (short)0 : _f2dot14PerAxis[axisIndex];

		/// <summary>Axis i as a double in [-1, 1] (exact: F2Dot14 / 16384).</summary>
		public double GetNormalized(int axisIndex) => GetF2Dot14(axisIndex) / 16384.0;

		/// <summary>Defensive copy of the raw tuple (empty array for Default).</summary>
		public short[] ToF2Dot14Array()
		{
			if (_f2dot14PerAxis == null) return new short[0];
			var copy = new short[_f2dot14PerAxis.Length];
			Array.Copy(_f2dot14PerAxis, copy, copy.Length);
			return copy;
		}

		public bool Equals(FontVariationNormalizedCoordinates other)
		{
			if (_f2dot14PerAxis == null || other._f2dot14PerAxis == null)
				return _f2dot14PerAxis == null && other._f2dot14PerAxis == null;
			if (_f2dot14PerAxis.Length != other._f2dot14PerAxis.Length)
				return false;
			for (int i = 0; i < _f2dot14PerAxis.Length; i++)
				if (_f2dot14PerAxis[i] != other._f2dot14PerAxis[i])
					return false;
			return true;
		}

		public override bool Equals(object obj) => obj is FontVariationNormalizedCoordinates other && Equals(other);

		public override int GetHashCode()
		{
			if (_f2dot14PerAxis == null) return 0;
			unchecked
			{
				int hash = 17;
				for (int i = 0; i < _f2dot14PerAxis.Length; i++)
					hash = hash * 31 + _f2dot14PerAxis[i];
				return hash;
			}
		}

		public override string ToString()
		{
			if (_f2dot14PerAxis == null) return "(default instance)";
			var parts = new string[_f2dot14PerAxis.Length];
			for (int i = 0; i < parts.Length; i++)
				parts[i] = (_f2dot14PerAxis[i] / 16384.0).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
			return "(" + string.Join(", ", parts) + ")";
		}
	}

	/// <summary>One USER-space axis request: e.g. ("wght", 350). Unknown tags are ignored at normalization.</summary>
	public readonly struct FontVariationUserAxisValue
	{
		public OpenTypeAxisTag AxisTag { get; }
		public double UserValue { get; }

		public FontVariationUserAxisValue(OpenTypeAxisTag axisTag, double userValue)
		{
			AxisTag = axisTag;
			UserValue = userValue;
		}

		public override string ToString() => AxisTag + "=" + UserValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}
}