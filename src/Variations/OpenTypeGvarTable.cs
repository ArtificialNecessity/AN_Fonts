using System;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Variations
{
	/// <summary>
	/// Parsed 'gvar' table (OpenType 1.9.1 — _EXTERNAL_APIS/OpenType/gvar.md + otvarcommonformats.md;
	/// implementation rules in README_variations_implementation_notes.md §3–§4). The header is parsed once;
	/// per-glyph tuple variation stores are decoded on demand by <see cref="TryComputePointDeltas"/>, which
	/// returns the NET (already scalar-weighted, IUP-inferred, summed over tuples) x/y delta per point in
	/// DOUBLE. The caller adds them to the default coordinates and rounds ONCE (fontTools parity).
	/// Every read is bounds-checked; malformed data yields a failure code, never an exception.
	/// </summary>
	public sealed class OpenTypeGvarTable
	{
		private const ushort TupleVariationCountSharedPointNumbersFlag = 0x8000;
		private const ushort TupleVariationCountMask = 0x0FFF;
		private const ushort TupleIndexEmbeddedPeakTupleFlag = 0x8000;
		private const ushort TupleIndexIntermediateRegionFlag = 0x4000;
		private const ushort TupleIndexPrivatePointNumbersFlag = 0x2000;
		private const ushort TupleIndexMask = 0x0FFF;
		private const byte PointRunPointsAreWordsFlag = 0x80;
		private const byte PointRunCountMask = 0x7F;
		private const byte DeltaRunDeltasAreZeroFlag = 0x80;
		private const byte DeltaRunDeltasAreWordsFlag = 0x40;
		private const byte DeltaRunCountMask = 0x3F;

		/// <summary>The four phantom points every glyph's gvar point list ends with (left, right, top, bottom side bearings).</summary>
		public const int PhantomPointCount = 4;

		private readonly FakePtr<byte> _fontData;
		private readonly int _tableOffset;
		private readonly int _tableLength;
		private readonly int _sharedTuplesOffset;        // absolute offset into font data
		private readonly int _glyphVariationDataArrayOffset; // absolute
		private readonly bool _longOffsets;

		public int AxisCount { get; }
		public int GlyphCount { get; }
		public int SharedTupleCount { get; }

		private OpenTypeGvarTable(FakePtr<byte> fontData, int tableOffset, int tableLength, int axisCount, int glyphCount,
			int sharedTupleCount, int sharedTuplesOffset, int glyphVariationDataArrayOffset, bool longOffsets)
		{
			_fontData = fontData;
			_tableOffset = tableOffset;
			_tableLength = tableLength;
			AxisCount = axisCount;
			GlyphCount = glyphCount;
			SharedTupleCount = sharedTupleCount;
			_sharedTuplesOffset = sharedTuplesOffset;
			_glyphVariationDataArrayOffset = glyphVariationDataArrayOffset;
			_longOffsets = longOffsets;
		}

		public static bool TryParse(FakePtr<byte> fontData, int tableOffset, int tableLength, int fvarAxisCount, int fontGlyphCount,
			out OpenTypeGvarTable table, out FontVariationParseFailure failure)
		{
			table = null;
			if (tableLength < 20 || tableOffset < 0 || tableOffset + tableLength > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GvarTruncatedHeader, "gvar", "table shorter than the 20-byte header or outside the font data");
				return false;
			}
			var t = fontData + tableOffset;
			ushort majorVersion = ttUSHORT(t);
			if (majorVersion != 1)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GvarUnsupportedVersion, "gvar", "majorVersion " + majorVersion);
				return false;
			}
			int axisCount = ttUSHORT(t + 4);
			if (axisCount != fvarAxisCount)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GvarAxisCountMismatch, "gvar", "axisCount " + axisCount + " != fvar axisCount " + fvarAxisCount);
				return false;
			}
			int sharedTupleCount = ttUSHORT(t + 6);
			long sharedTuplesOffset = ttULONG(t + 8);
			int glyphCount = ttUSHORT(t + 12);
			ushort flags = ttUSHORT(t + 14);
			long glyphVariationDataArrayOffset = ttULONG(t + 16);
			bool longOffsets = (flags & 1) != 0;
			if (glyphCount != fontGlyphCount)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GvarGlyphCountMismatch, "gvar", "glyphCount " + glyphCount + " != maxp numGlyphs " + fontGlyphCount);
				return false;
			}
			long offsetsEnd = 20L + (long)(glyphCount + 1) * (longOffsets ? 4 : 2);
			if (offsetsEnd > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GvarOffsetArrayOutOfBounds, "gvar", "glyphVariationDataOffsets array ends at " + offsetsEnd + " > tableLength " + tableLength);
				return false;
			}
			long sharedTuplesEnd = sharedTuplesOffset + (long)sharedTupleCount * axisCount * 2;
			if (sharedTuplesEnd > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GvarSharedTuplesOutOfBounds, "gvar", "shared tuples end at " + sharedTuplesEnd + " > tableLength " + tableLength);
				return false;
			}
			if (glyphVariationDataArrayOffset > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GvarGlyphVariationDataOutOfBounds, "gvar", "glyphVariationDataArrayOffset " + glyphVariationDataArrayOffset + " > tableLength " + tableLength);
				return false;
			}
			table = new OpenTypeGvarTable(fontData, tableOffset, tableLength, axisCount, glyphCount, sharedTupleCount,
				tableOffset + (int)sharedTuplesOffset, tableOffset + (int)glyphVariationDataArrayOffset, longOffsets);
			failure = FontVariationParseFailure.None;
			return true;
		}

		/// <summary>Absolute [start, start+length) of the glyph's GlyphVariationData; length 0 = no variation data for the glyph.</summary>
		public bool TryGetGlyphVariationDataRange(int glyphIndex, out int absoluteStart, out int length, out FontVariationParseFailure failure)
		{
			absoluteStart = 0; length = 0;
			if (glyphIndex < 0 || glyphIndex >= GlyphCount)
			{
				failure = FontVariationParseFailure.None; // glyph outside gvar's range: no variation (not a table fault)
				return true;
			}
			var offsets = _fontData + _tableOffset + 20;
			long start, end;
			if (_longOffsets)
			{
				start = ttULONG(offsets + glyphIndex * 4);
				end = ttULONG(offsets + glyphIndex * 4 + 4);
			}
			else
			{
				start = ttUSHORT(offsets + glyphIndex * 2) * 2L;
				end = ttUSHORT(offsets + glyphIndex * 2 + 2) * 2L;
			}
			if (end < start || _glyphVariationDataArrayOffset - _tableOffset + end > _tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GvarGlyphVariationDataOutOfBounds, "gvar", "glyph " + glyphIndex + " data range [" + start + ", " + end + ") beyond table end");
				return false;
			}
			absoluteStart = _glyphVariationDataArrayOffset + (int)start;
			length = (int)(end - start);
			failure = FontVariationParseFailure.None;
			return true;
		}

		/// <summary>
		/// Net per-point deltas for one glyph at <paramref name="instance"/>. <paramref name="pointCount"/> INCLUDES
		/// the 4 phantom points (outline points + 4 for simple glyphs; components + 4 for composites).
		/// <paramref name="defaultX"/>/<paramref name="defaultY"/> are the default coordinates (integers) for IUP;
		/// <paramref name="contourEndPointIndices"/> are the simple glyph's contour end indices (sorted), or NULL for a
		/// composite glyph (no inference: unreferenced components/phantoms get 0). Output arrays are zeroed first.
		/// Returns false with a failure only for malformed data; a glyph without variation data returns true with zeros.
		/// </summary>
		public bool TryComputePointDeltas(int glyphIndex, in FontVariationNormalizedCoordinates instance,
			int[] defaultX, int[] defaultY, int pointCount, ushort[] contourEndPointIndices,
			double[] netDeltaX, double[] netDeltaY, out FontVariationParseFailure failure)
		{
			Array.Clear(netDeltaX, 0, pointCount);
			Array.Clear(netDeltaY, 0, pointCount);
			if (!TryGetGlyphVariationDataRange(glyphIndex, out int dataStart, out int dataLength, out failure))
				return false;
			if (dataLength == 0 || instance.IsDefault)
				return true;
			if (dataLength < 4)
				return Fail(out failure, FontVariationParseFailureCode.GvarTupleHeaderOutOfBounds, "glyph " + glyphIndex + ": GlyphVariationData shorter than 4 bytes");

			var g = _fontData + dataStart;
			ushort tupleVariationCountField = ttUSHORT(g);
			int tupleCount = tupleVariationCountField & TupleVariationCountMask;
			bool hasSharedPointNumbers = (tupleVariationCountField & TupleVariationCountSharedPointNumbersFlag) != 0;
			int serializedDataOffset = ttUSHORT(g + 2);
			if (serializedDataOffset > dataLength)
				return Fail(out failure, FontVariationParseFailureCode.GvarTupleHeaderOutOfBounds, "glyph " + glyphIndex + ": dataOffset beyond GlyphVariationData");

			// Pass 1: read the tuple headers (variable size) and compute each tuple's scalar.
			var tupleDataSizes = new int[tupleCount];
			var tupleScalars = new double[tupleCount];
			var tupleHasPrivatePoints = new bool[tupleCount];
			var peak = new double[AxisCount];
			var start = new double[AxisCount];
			var end = new double[AxisCount];
			int headerCursor = 4;
			for (int tupleIndex = 0; tupleIndex < tupleCount; tupleIndex++)
			{
				if (headerCursor + 4 > serializedDataOffset)
					return Fail(out failure, FontVariationParseFailureCode.GvarTupleHeaderOutOfBounds, "glyph " + glyphIndex + ": tuple header " + tupleIndex + " overlaps serialized data");
				tupleDataSizes[tupleIndex] = ttUSHORT(g + headerCursor);
				ushort tupleIndexField = ttUSHORT(g + headerCursor + 2);
				headerCursor += 4;
				bool embeddedPeak = (tupleIndexField & TupleIndexEmbeddedPeakTupleFlag) != 0;
				bool intermediate = (tupleIndexField & TupleIndexIntermediateRegionFlag) != 0;
				tupleHasPrivatePoints[tupleIndex] = (tupleIndexField & TupleIndexPrivatePointNumbersFlag) != 0;
				if (embeddedPeak)
				{
					if (headerCursor + AxisCount * 2 > serializedDataOffset)
						return Fail(out failure, FontVariationParseFailureCode.GvarTupleHeaderOutOfBounds, "glyph " + glyphIndex + ": embedded peak tuple beyond header area");
					ReadTuple(g + headerCursor, peak);
					headerCursor += AxisCount * 2;
				}
				else
				{
					int sharedIndex = tupleIndexField & TupleIndexMask;
					if (sharedIndex >= SharedTupleCount)
						return Fail(out failure, FontVariationParseFailureCode.GvarSharedTupleIndexOutOfRange, "glyph " + glyphIndex + ": shared tuple index " + sharedIndex + " >= " + SharedTupleCount);
					ReadTuple(_fontData + _sharedTuplesOffset + sharedIndex * AxisCount * 2, peak);
				}
				if (intermediate)
				{
					if (headerCursor + AxisCount * 4 > serializedDataOffset)
						return Fail(out failure, FontVariationParseFailureCode.GvarTupleHeaderOutOfBounds, "glyph " + glyphIndex + ": intermediate tuples beyond header area");
					ReadTuple(g + headerCursor, start);
					ReadTuple(g + headerCursor + AxisCount * 2, end);
					headerCursor += AxisCount * 4;
					tupleScalars[tupleIndex] = OpenTypeVariationRegionScalar.Compute(instance, start, peak, end, AxisCount);
				}
				else
				{
					tupleScalars[tupleIndex] = OpenTypeVariationRegionScalar.ComputeNonIntermediate(instance, peak, AxisCount);
				}
			}

			// Pass 2: serialized data — optional shared point numbers, then per-tuple [private points] + deltas.
			int dataCursor = serializedDataOffset;
			int[] sharedPointNumbers = null;   // null => no shared point numbers
			int sharedPointNumberCount = 0;    // -1 => "all points"
			if (hasSharedPointNumbers)
			{
				if (!TryDecodePackedPointNumbers(g, ref dataCursor, dataLength, pointCount, out sharedPointNumbers, out sharedPointNumberCount, out failure, glyphIndex))
					return false;
			}

			var tupleDeltaX = new double[pointCount];
			var tupleDeltaY = new double[pointCount];
			var pointHasExplicitDelta = new bool[pointCount];
			for (int tupleIndex = 0; tupleIndex < tupleCount; tupleIndex++)
			{
				int tupleDataStart = dataCursor;
				int tupleDataEnd = tupleDataStart + tupleDataSizes[tupleIndex];
				if (tupleDataEnd > dataLength)
					return Fail(out failure, FontVariationParseFailureCode.GvarPackedDeltasOutOfBounds, "glyph " + glyphIndex + ": tuple " + tupleIndex + " data beyond GlyphVariationData");
				dataCursor = tupleDataEnd;
				double scalar = tupleScalars[tupleIndex];
				if (scalar == 0.0)
					continue;

				int cursor = tupleDataStart;
				int[] pointNumbers;
				int pointNumberCount;
				if (tupleHasPrivatePoints[tupleIndex])
				{
					if (!TryDecodePackedPointNumbers(g, ref cursor, tupleDataEnd, pointCount, out pointNumbers, out pointNumberCount, out failure, glyphIndex))
						return false;
				}
				else if (hasSharedPointNumbers)
				{
					pointNumbers = sharedPointNumbers;
					pointNumberCount = sharedPointNumberCount;
				}
				else
				{
					// Neither private nor shared point numbers: fontTools treats this as an empty point set (no effect).
					continue;
				}

				int deltaCount = pointNumberCount < 0 ? pointCount : pointNumberCount;
				Array.Clear(tupleDeltaX, 0, pointCount);
				Array.Clear(tupleDeltaY, 0, pointCount);
				Array.Clear(pointHasExplicitDelta, 0, pointCount);
				// x deltas then y deltas, each a packed run sequence of exactly deltaCount logical values.
				if (!TryDecodePackedDeltas(g, ref cursor, tupleDataEnd, deltaCount, pointNumbers, pointNumberCount, tupleDeltaX, pointHasExplicitDelta, out failure, glyphIndex))
					return false;
				if (!TryDecodePackedDeltas(g, ref cursor, tupleDataEnd, deltaCount, pointNumbers, pointNumberCount, tupleDeltaY, null, out failure, glyphIndex))
					return false;

				if (pointNumberCount >= 0 && contourEndPointIndices != null)
					InferUnreferencedPointDeltas(defaultX, defaultY, pointCount, contourEndPointIndices, pointHasExplicitDelta, tupleDeltaX, tupleDeltaY);
				// Composite glyphs / phantom points: unreferenced entries simply stay 0 (no inference — gvar spec).

				for (int p = 0; p < pointCount; p++)
				{
					netDeltaX[p] += scalar * tupleDeltaX[p];
					netDeltaY[p] += scalar * tupleDeltaY[p];
				}
			}
			failure = FontVariationParseFailure.None;
			return true;
		}

		private void ReadTuple(FakePtr<byte> p, double[] into)
		{
			for (int axis = 0; axis < AxisCount; axis++)
				into[axis] = ttSHORT(p + axis * 2) / 16384.0;
		}

		private static bool Fail(out FontVariationParseFailure failure, FontVariationParseFailureCode code, string detail)
		{
			failure = new FontVariationParseFailure(code, "gvar", detail);
			return false;
		}

		/// <summary>
		/// Packed point numbers (otvarcommonformats "Packed Point Numbers"). count = -1 signals ALL points.
		/// Point numbers are cumulative differences; repeats are legal (their deltas accumulate).
		/// </summary>
		private static bool TryDecodePackedPointNumbers(FakePtr<byte> g, ref int cursor, int limit, int pointCount,
			out int[] pointNumbers, out int pointNumberCount, out FontVariationParseFailure failure, int glyphIndex)
		{
			pointNumbers = null;
			pointNumberCount = 0;
			if (cursor + 1 > limit)
				return Fail(out failure, FontVariationParseFailureCode.GvarPointNumbersOutOfBounds, "glyph " + glyphIndex + ": point-number count byte missing");
			int count = g[cursor++];
			if (count == 0)
			{
				pointNumberCount = -1; // all points, including the phantoms
				failure = FontVariationParseFailure.None;
				return true;
			}
			if ((count & PointRunPointsAreWordsFlag) != 0)
			{
				if (cursor + 1 > limit)
					return Fail(out failure, FontVariationParseFailureCode.GvarPointNumbersOutOfBounds, "glyph " + glyphIndex + ": 2-byte point-number count truncated");
				count = ((count & PointRunCountMask) << 8) | g[cursor++];
			}
			pointNumbers = new int[count];
			int decoded = 0;
			int current = 0;
			while (decoded < count)
			{
				if (cursor + 1 > limit)
					return Fail(out failure, FontVariationParseFailureCode.GvarPointNumbersOutOfBounds, "glyph " + glyphIndex + ": point-number run header missing");
				byte runHeader = g[cursor++];
				int runLength = (runHeader & PointRunCountMask) + 1;
				bool words = (runHeader & PointRunPointsAreWordsFlag) != 0;
				int runBytes = runLength * (words ? 2 : 1);
				if (cursor + runBytes > limit)
					return Fail(out failure, FontVariationParseFailureCode.GvarPointNumbersOutOfBounds, "glyph " + glyphIndex + ": point-number run data truncated");
				for (int i = 0; i < runLength && decoded < count; i++)
				{
					int difference = words ? ttUSHORT(g + cursor + i * 2) : g[cursor + i];
					current += difference;
					if (current < 0 || current >= pointCount)
						return Fail(out failure, FontVariationParseFailureCode.GvarPointNumberOutOfRange, "glyph " + glyphIndex + ": point number " + current + " >= point count " + pointCount);
					pointNumbers[decoded++] = current;
				}
				cursor += runBytes;
			}
			pointNumberCount = count;
			failure = FontVariationParseFailure.None;
			return true;
		}

		/// <summary>
		/// Packed deltas (otvarcommonformats "Packed Deltas"): runs of zero / int8 / int16 until
		/// <paramref name="deltaCount"/> logical values are read. Values are accumulated into
		/// <paramref name="target"/> at the referenced point numbers (or 0..n-1 for "all points").
		/// </summary>
		private static bool TryDecodePackedDeltas(FakePtr<byte> g, ref int cursor, int limit, int deltaCount,
			int[] pointNumbers, int pointNumberCount, double[] target, bool[] markExplicit,
			out FontVariationParseFailure failure, int glyphIndex)
		{
			int decoded = 0;
			while (decoded < deltaCount)
			{
				if (cursor + 1 > limit)
					return Fail(out failure, FontVariationParseFailureCode.GvarPackedDeltasOutOfBounds, "glyph " + glyphIndex + ": delta run header missing");
				byte runHeader = g[cursor++];
				int runLength = (runHeader & DeltaRunCountMask) + 1;
				if ((runHeader & DeltaRunDeltasAreZeroFlag) != 0)
				{
					for (int i = 0; i < runLength && decoded < deltaCount; i++, decoded++)
					{
						int p = pointNumberCount < 0 ? decoded : pointNumbers[decoded];
						if (markExplicit != null) markExplicit[p] = true;
					}
					continue;
				}
				bool words = (runHeader & DeltaRunDeltasAreWordsFlag) != 0;
				int runBytes = runLength * (words ? 2 : 1);
				if (cursor + runBytes > limit)
					return Fail(out failure, FontVariationParseFailureCode.GvarPackedDeltasOutOfBounds, "glyph " + glyphIndex + ": delta run data truncated");
				for (int i = 0; i < runLength && decoded < deltaCount; i++, decoded++)
				{
					int value = words ? ttSHORT(g + cursor + i * 2) : (sbyte)g[cursor + i];
					int p = pointNumberCount < 0 ? decoded : pointNumbers[decoded];
					target[p] += value;
					if (markExplicit != null) markExplicit[p] = true;
				}
				cursor += runBytes;
			}
			failure = FontVariationParseFailure.None;
			return true;
		}

		/// <summary>
		/// IUP — inferred deltas for un-referenced points, per contour, per axis (gvar chapter "Inferred deltas";
		/// fontTools varLib.iup.iup_contour / iup_segment, README §4). Phantom points are 1-point pseudo-contours and
		/// therefore need no inference (unreferenced => 0). Coordinates compared as INTEGERS (the default outline).
		/// </summary>
		private static void InferUnreferencedPointDeltas(int[] defaultX, int[] defaultY, int pointCount, ushort[] contourEndPointIndices,
			bool[] pointHasExplicitDelta, double[] deltaX, double[] deltaY)
		{
			int contourStart = 0;
			for (int contour = 0; contour < contourEndPointIndices.Length; contour++)
			{
				int contourEnd = contourEndPointIndices[contour]; // inclusive
				if (contourEnd >= pointCount - PhantomPointCount) break; // malformed endPts: stop rather than touch phantoms
				InferContour(defaultX, defaultY, contourStart, contourEnd, pointHasExplicitDelta, deltaX, deltaY);
				contourStart = contourEnd + 1;
			}
		}

		private static void InferContour(int[] defaultX, int[] defaultY, int first, int last, bool[] hasDelta, double[] deltaX, double[] deltaY)
		{
			int n = last - first + 1;
			if (n <= 0) return;
			// Find referenced points in this contour.
			int firstReferenced = -1, referencedCount = 0;
			for (int p = first; p <= last; p++)
				if (hasDelta[p]) { referencedCount++; if (firstReferenced < 0) firstReferenced = p; }
			if (referencedCount == n) return;                 // nothing to infer
			if (referencedCount == 0) return;                 // all deltas stay 0 (already zero)
			// Walk the contour circularly starting at the first referenced point; every run of unreferenced
			// points between two referenced points r1 -> r2 (wrapping) is interpolated from (r1, r2).
			int r1 = firstReferenced;
			int steps = 0;
			while (steps < n)
			{
				// find next referenced point r2 after r1 (circular)
				int r2 = r1;
				int runLength = 0;
				do
				{
					r2 = r2 == last ? first : r2 + 1;
					steps++;
					if (hasDelta[r2]) break;
					runLength++;
				} while (r2 != r1);
				if (runLength > 0)
				{
					InterpolateRun(defaultX, deltaX, first, last, r1, r2, runLength);
					InterpolateRun(defaultY, deltaY, first, last, r1, r2, runLength);
				}
				r1 = r2;
				if (r1 == firstReferenced && steps >= n) break;
			}
		}

		/// <summary>fontTools iup_segment for one axis: the runLength points after r1 (circular within [first,last]).</summary>
		private static void InterpolateRun(int[] coord, double[] delta, int first, int last, int r1, int r2, int runLength)
		{
			double x1 = coord[r1], x2 = coord[r2];
			double d1 = delta[r1], d2 = delta[r2];
			if (x1 > x2)
			{
				double tx = x1; x1 = x2; x2 = tx;
				double td = d1; d1 = d2; d2 = td;
			}
			bool sameCoordinate = x1 == x2;
			double scale = sameCoordinate ? 0.0 : (d2 - d1) / (x2 - x1);
			int p = r1;
			for (int i = 0; i < runLength; i++)
			{
				p = p == last ? first : p + 1;
				double x = coord[p];
				double d;
				if (sameCoordinate)
					d = d1 == d2 ? d1 : 0.0;
				else if (x <= x1)
					d = d1;
				else if (x >= x2)
					d = d2;
				else
				{
					// Two operations on purpose (no fused multiply-add) — bit-for-bit with fontTools (issue 3703).
					double nudge = (x - x1) * scale;
					d = d1 + nudge;
				}
				delta[p] = d;
			}
		}
	}
}