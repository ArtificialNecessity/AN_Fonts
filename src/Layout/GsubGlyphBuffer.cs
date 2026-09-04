using System;

namespace StbTrueTypeSharp.Layout
{
	/// <summary>
	/// Struct-of-arrays glyph run for GSUB application (SilkyNvg plans/color_glyph_fonts.md §5.2). Glyph ids are mutated
	/// in place by <see cref="GsubLookupApplier"/>; cluster fields track which source code units each glyph covers so the
	/// text iterator can advance over folded codepoints (ligatures) and report selection boundaries.
	/// Cluster merge law (HarfBuzz semantics, _EXTERNAL_APIS/opentype_gsub_lookup_types.md §Application model):
	///  - ligature (N glyphs -> 1): cluster start = min of the components; codepoint count = sum (on the ligature glyph);
	///  - multiple substitution (1 -> N): every output inherits the source cluster start; the FIRST output carries the
	///    codepoint count, the rest carry 0 (they are fragments of the same cluster);
	///  - deletion (empty sequence): the deleted glyph's codepoint count merges into the PREVIOUS glyph, or the
	///    FOLLOWING glyph when deleting at position 0 (HarfBuzz delete_glyph).
	/// Never throws on the text hot path: out-of-range arguments are ignored (buffer unchanged).
	/// </summary>
	public sealed class GsubGlyphBuffer
	{
		private const int InitialCapacity = 16;

		public int Length;
		/// <summary>Glyph ids, mutable in place. Valid entries: [0, Length).</summary>
		public ushort[] GlyphIds = new ushort[InitialCapacity];
		/// <summary>Source UTF-16 index of the cluster's first code unit (min-merged on ligature/delete).</summary>
		public int[] ClusterStart = new int[InitialCapacity];
		/// <summary>Codepoints folded into this glyph (1 for plain glyphs, 0 for non-first multiple-subst outputs) — the
		/// iterator's advance-over-source count. Saturates at 255.</summary>
		public byte[] ClusterCodepointCount = new byte[InitialCapacity];
		/// <summary>True when the SOURCE codepoint is a Unicode default-ignorable (ZWJ, variation selectors, tags —
		/// <see cref="UnicodeDefaultIgnorable"/>): MAYBE-skipped during sequence matching (skipped unless it matches
		/// the expected element, HarfBuzz matcher law); survivors are deleted by the text iterator (§5.4).
		/// Substitution outputs are always false.</summary>
		public bool[] IsDefaultIgnorable = new bool[InitialCapacity];

		public void Reset() => Length = 0;

		public void Append(ushort glyphId, int clusterStart, int codepointCount)
			=> Append(glyphId, clusterStart, codepointCount, false);

		public void Append(ushort glyphId, int clusterStart, int codepointCount, bool isDefaultIgnorable)
		{
			EnsureCapacity(Length + 1);
			GlyphIds[Length] = glyphId;
			ClusterStart[Length] = clusterStart;
			ClusterCodepointCount[Length] = SaturateByte(codepointCount);
			IsDefaultIgnorable[Length] = isDefaultIgnorable;
			Length++;
		}

		/// <summary>Deletes the glyph at <paramref name="pos"/>, merging its codepoint count into the previous glyph
		/// (or the following glyph at position 0). Equivalent to ReplaceRange(pos, 1, empty).</summary>
		public void Delete(int pos) => ReplaceRange(pos, 1, ReadOnlySpan<ushort>.Empty);

		/// <summary>
		/// Ligature substitution over possibly NON-CONTIGUOUS matched positions (LookupFlag skipping can leave
		/// skipped glyphs between components; they survive the ligation \u2014 HarfBuzz behaviour). The first matched
		/// position receives the ligature glyph with cluster start = min and codepoint count = sum of ALL matched
		/// positions; the remaining matched positions are removed without further merging (already merged here).
		/// <paramref name="matchedPositions"/> must be ascending and within [0, Length).
		/// </summary>
		public void Ligate(ushort ligatureGlyph, ReadOnlySpan<int> matchedPositions)
		{
			if (matchedPositions.Length == 0) return;
			int first = matchedPositions[0];
			if (first < 0 || matchedPositions[matchedPositions.Length - 1] >= Length) return;
			int last = matchedPositions[matchedPositions.Length - 1];
			int mergedStart = int.MaxValue;
			int mergedCount = 0;
			for (int i = 0; i < matchedPositions.Length; i++)
			{
				int p = matchedPositions[i];
				if (ClusterStart[p] < mergedStart) mergedStart = ClusterStart[p];
				mergedCount += ClusterCodepointCount[p];
			}
			// HarfBuzz merge_clusters spans the WHOLE matched range: skipped (unmatched) glyphs between the
			// components adopt the merged cluster start too (their codepoint counts stay their own).
			for (int i = first; i <= last; i++)
				if (ClusterStart[i] > mergedStart) ClusterStart[i] = mergedStart;
			GlyphIds[first] = ligatureGlyph;
			ClusterStart[first] = mergedStart;
			ClusterCodepointCount[first] = SaturateByte(mergedCount);
			IsDefaultIgnorable[first] = false;
			for (int i = matchedPositions.Length - 1; i >= 1; i--)
				RemoveAtNoMerge(matchedPositions[i]);
		}

		private void RemoveAtNoMerge(int pos)
		{
			int tailCount = Length - pos - 1;
			if (tailCount > 0)
			{
				Array.Copy(GlyphIds, pos + 1, GlyphIds, pos, tailCount);
				Array.Copy(ClusterStart, pos + 1, ClusterStart, pos, tailCount);
				Array.Copy(ClusterCodepointCount, pos + 1, ClusterCodepointCount, pos, tailCount);
				Array.Copy(IsDefaultIgnorable, pos + 1, IsDefaultIgnorable, pos, tailCount);
			}
			Length--;
		}

		/// <summary>
		/// Replaces <paramref name="count"/> glyphs starting at <paramref name="pos"/> with <paramref name="substitutes"/>,
		/// applying the cluster merge law above. Handles all GSUB shapes: single (1->1), ligature (N->1),
		/// multiple (1->N), and deletion (N->0). No-op when the range is out of bounds.
		/// </summary>
		public void ReplaceRange(int pos, int count, ReadOnlySpan<ushort> substitutes)
		{
			if (pos < 0 || count < 0 || pos + count > Length) return;
			int newLength = Length - count + substitutes.Length;
			EnsureCapacity(newLength);

			// Cluster values of the replaced span, per the merge law.
			int mergedStart = int.MaxValue;
			int mergedCount = 0;
			for (int i = pos; i < pos + count; i++)
			{
				if (ClusterStart[i] < mergedStart) mergedStart = ClusterStart[i];
				mergedCount += ClusterCodepointCount[i];
			}
			if (count == 0) mergedStart = pos < Length ? ClusterStart[pos] : (Length > 0 ? ClusterStart[Length - 1] : 0);

			if (substitutes.Length == 0 && count > 0)
			{
				// Deletion: merge the span's codepoint count into the previous glyph, or the following at start.
				if (pos > 0)
				{
					ClusterCodepointCount[pos - 1] = SaturateByte(ClusterCodepointCount[pos - 1] + mergedCount);
				}
				else if (Length - count > 0)
				{
					// Deleting at position 0: the following glyph absorbs the count AND the (smaller) cluster start.
					ClusterCodepointCount[count] = SaturateByte(ClusterCodepointCount[count] + mergedCount);
					if (mergedStart < ClusterStart[count]) ClusterStart[count] = mergedStart;
				}
			}

			// Shift the tail.
			int tailCount = Length - (pos + count);
			if (tailCount > 0 && substitutes.Length != count)
			{
				Array.Copy(GlyphIds, pos + count, GlyphIds, pos + substitutes.Length, tailCount);
				Array.Copy(ClusterStart, pos + count, ClusterStart, pos + substitutes.Length, tailCount);
				Array.Copy(ClusterCodepointCount, pos + count, ClusterCodepointCount, pos + substitutes.Length, tailCount);
				Array.Copy(IsDefaultIgnorable, pos + count, IsDefaultIgnorable, pos + substitutes.Length, tailCount);
			}

			// Write the substitutes: all inherit the merged cluster start; the first carries the codepoint count.
			for (int i = 0; i < substitutes.Length; i++)
			{
				GlyphIds[pos + i] = substitutes[i];
				ClusterStart[pos + i] = mergedStart == int.MaxValue ? 0 : mergedStart;
				ClusterCodepointCount[pos + i] = i == 0 ? SaturateByte(mergedCount) : (byte)0;
				IsDefaultIgnorable[pos + i] = false; // substitution outputs are real glyphs
			}

			Length = newLength;
		}

		private void EnsureCapacity(int needed)
		{
			if (needed <= GlyphIds.Length) return;
			int newCapacity = GlyphIds.Length * 2;
			if (newCapacity < needed) newCapacity = needed;
			Array.Resize(ref GlyphIds, newCapacity);
			Array.Resize(ref ClusterStart, newCapacity);
			Array.Resize(ref ClusterCodepointCount, newCapacity);
			Array.Resize(ref IsDefaultIgnorable, newCapacity);
		}

		private static byte SaturateByte(int value) => value > 255 ? (byte)255 : (byte)(value < 0 ? 0 : value);
	}
}