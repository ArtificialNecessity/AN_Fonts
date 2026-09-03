using StbTrueTypeSharp.Variations;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Layout
{
	/// <summary>
	/// One ConditionFormat1 (OpenType Layout Common Table Formats, "Condition table format 1: font variation axis range"):
	/// matches iff filterRangeMinValue <= coords[axisIndex] <= filterRangeMaxValue on the NORMALIZED F2Dot14 coordinate,
	/// both ends INCLUSIVE. Values are stored raw (F2Dot14 shorts) so the comparison is exact integer arithmetic against
	/// the exact 2.14 value the normalization pipeline produces (otvaroverview: "this exact value must be used").
	/// </summary>
	public readonly struct OpenTypeLayoutAxisRangeCondition
	{
		public ushort AxisIndex { get; }
		public short FilterRangeMinF2Dot14 { get; }
		public short FilterRangeMaxF2Dot14 { get; }

		public OpenTypeLayoutAxisRangeCondition(ushort axisIndex, short filterRangeMinF2Dot14, short filterRangeMaxF2Dot14)
		{
			AxisIndex = axisIndex;
			FilterRangeMinF2Dot14 = filterRangeMinF2Dot14;
			FilterRangeMaxF2Dot14 = filterRangeMaxF2Dot14;
		}

		public bool Matches(in FontVariationNormalizedCoordinates coords)
		{
			short coord = coords.GetF2Dot14(AxisIndex);
			return coord >= FilterRangeMinF2Dot14 && coord <= FilterRangeMaxF2Dot14;
		}
	}

	/// <summary>
	/// One FeatureVariationRecord after parsing. The record-level rules of the spec are pre-resolved into flags so that
	/// evaluation at an instance is a plain loop over <see cref="Conditions"/>:
	/// <list type="bullet">
	/// <item><see cref="IsUniversal"/>: conditionSetOffset == 0 OR an EMPTY ConditionSet — matches every context.</item>
	/// <item><see cref="ConditionSetCanNeverMatch"/>: a condition of an UNRECOGNISED format (spec: "fail to match the
	/// condition set, but continue to test other condition sets").</item>
	/// <item><see cref="IsIgnored"/>: a format-1 condition whose axisIndex >= fvar.axisCount (spec: "the feature variation
	/// record containing this condition table is ignored"). NOTE HarfBuzz instead reads coord 0 for a missing axis; we
	/// follow the published spec.</item>
	/// <item><see cref="SubstitutionVersionSupported"/>: FeatureTableSubstitution.majorVersion == 1. When a record MATCHES but
	/// this is false the record is REJECTED and evaluation continues with the next record (spec precedence rule).</item>
	/// <item><see cref="HasSubstitutions"/>: featureTableSubstitutionOffset != 0. A match without substitutions is still a
	/// match (evaluation stops) — it simply substitutes nothing.</item>
	/// </list>
	/// </summary>
	public sealed class OpenTypeLayoutFeatureVariationRecord
	{
		public OpenTypeLayoutAxisRangeCondition[] Conditions { get; }
		public bool IsUniversal { get; }
		public bool ConditionSetCanNeverMatch { get; }
		public bool IsIgnored { get; }
		public bool SubstitutionVersionSupported { get; }
		public bool HasSubstitutions { get; }
		/// <summary>Absolute offsets (into the font data) of the alternate Feature tables, parallel to <see cref="SubstitutedFeatureIndices"/>; sorted ascending by feature index as the spec requires.</summary>
		public ushort[] SubstitutedFeatureIndices { get; }
		public int[] AlternateFeatureTableAbsoluteOffsets { get; }

		internal OpenTypeLayoutFeatureVariationRecord(OpenTypeLayoutAxisRangeCondition[] conditions, bool isUniversal, bool conditionSetCanNeverMatch,
			bool isIgnored, bool substitutionVersionSupported, bool hasSubstitutions, ushort[] substitutedFeatureIndices, int[] alternateFeatureTableAbsoluteOffsets)
		{
			Conditions = conditions;
			IsUniversal = isUniversal;
			ConditionSetCanNeverMatch = conditionSetCanNeverMatch;
			IsIgnored = isIgnored;
			SubstitutionVersionSupported = substitutionVersionSupported;
			HasSubstitutions = hasSubstitutions;
			SubstitutedFeatureIndices = substitutedFeatureIndices;
			AlternateFeatureTableAbsoluteOffsets = alternateFeatureTableAbsoluteOffsets;
		}

		/// <summary>Spec ConditionSet semantics: conditions are ANDed; empty/NULL set is universal; unknown format never matches.</summary>
		public bool ConditionSetMatches(in FontVariationNormalizedCoordinates coords)
		{
			if (IsIgnored || ConditionSetCanNeverMatch) return false;
			if (IsUniversal) return true;
			for (int i = 0; i < Conditions.Length; i++)
				if (!Conditions[i].Matches(coords))
					return false;
			return true;
		}

		/// <summary>
		/// FeatureTableSubstitution lookup for one feature index (spec: records ascending by featureIndex; first equal record
		/// wins; a record with a HIGHER index ends the search with no substitution). Returns -1 when the default Feature
		/// table stays in force.
		/// </summary>
		public int GetAlternateFeatureTableAbsoluteOffset(int featureIndex)
		{
			if (!HasSubstitutions) return -1;
			for (int i = 0; i < SubstitutedFeatureIndices.Length; i++)
			{
				int recordFeatureIndex = SubstitutedFeatureIndices[i];
				if (recordFeatureIndex == featureIndex) return AlternateFeatureTableAbsoluteOffsets[i];
				if (recordFeatureIndex > featureIndex) return -1;
			}
			return -1;
		}
	}

	/// <summary>
	/// Parsed FeatureVariations table (OpenType Layout Common Table Formats §"Feature variations", OT 1.9.1 —
	/// _EXTERNAL_APIS/OpenType/otl_common_formats_chapter2.latest.md). TABLE-AGNOSTIC: GSUB 1.1 and GPOS 1.1 carry the
	/// identical structure at the identical header position (Offset32 at byte 10). Part D (spec §9) wires it into GSUB;
	/// the GPOS kern-feature hook is deliberately deferred until a fixture exercises it (spec §9.9).
	/// Every offset is bounds-validated against the PARENT table's length at parse time so evaluation never throws.
	/// </summary>
	public sealed class OpenTypeLayoutFeatureVariations
	{
		public OpenTypeLayoutFeatureVariationRecord[] Records { get; }

		private OpenTypeLayoutFeatureVariations(OpenTypeLayoutFeatureVariationRecord[] records)
		{
			Records = records;
		}

		/// <summary>
		/// The spec's selection rule: records in order; the FIRST whose ConditionSet matches is the candidate; if its
		/// FeatureTableSubstitution version is supported it is USED and evaluation stops, otherwise it is REJECTED and
		/// evaluation continues. Returns the record index, or -1 when no record applies (default Feature tables in force).
		/// </summary>
		public int FindApplicableRecordIndex(in FontVariationNormalizedCoordinates coords)
		{
			for (int i = 0; i < Records.Length; i++)
			{
				var record = Records[i];
				if (!record.ConditionSetMatches(coords)) continue;
				if (!record.SubstitutionVersionSupported) continue; // rejected candidate: keep scanning
				return i;
			}
			return -1;
		}

		/// <summary>
		/// Parses the FeatureVariations table at <paramref name="featureVariationsOffsetInParent"/> (relative to the parent
		/// GSUB/GPOS table start). <paramref name="parentTableTag"/> is used for failure records only.
		/// </summary>
		public static bool TryParse(FakePtr<byte> fontData, int parentTableOffset, int parentTableLength, int featureVariationsOffsetInParent,
			int fvarAxisCount, string parentTableTag, out OpenTypeLayoutFeatureVariations table, out FontVariationParseFailure failure)
		{
			table = null;
			long fvStart = (long)parentTableOffset + featureVariationsOffsetInParent;
			long parentEnd = (long)parentTableOffset + parentTableLength;
			if (featureVariationsOffsetInParent <= 0 || fvStart + 8 > parentEnd || parentEnd > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutFeatureVariationsTruncated, parentTableTag,
					"FeatureVariations header at " + featureVariationsOffsetInParent + " outside the table (length " + parentTableLength + ")");
				return false;
			}
			var fv = fontData + (int)fvStart;
			int majorVersion = ttUSHORT(fv);
			if (majorVersion != 1)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutFeatureVariationsUnsupportedVersion, parentTableTag,
					"FeatureVariations majorVersion " + majorVersion + " (only 1 is defined)");
				return false;
			}
			long recordCount = ttULONG(fv + 4);
			if (fvStart + 8 + recordCount * 8 > parentEnd)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutFeatureVariationsTruncated, parentTableTag,
					"featureVariationRecordCount " + recordCount + " beyond the table");
				return false;
			}
			var records = new OpenTypeLayoutFeatureVariationRecord[(int)recordCount];
			for (int r = 0; r < records.Length; r++)
			{
				var rec = fv + 8 + r * 8;
				long conditionSetOffset = ttULONG(rec);
				long substitutionOffset = ttULONG(rec + 4);
				if (!TryParseConditionSet(fontData, fvStart, parentEnd, conditionSetOffset, fvarAxisCount, parentTableTag, r,
						out OpenTypeLayoutAxisRangeCondition[] conditions, out bool isUniversal, out bool canNeverMatch, out bool isIgnored, out failure))
					return false;
				if (!TryParseFeatureTableSubstitution(fontData, fvStart, parentEnd, substitutionOffset, parentTableTag, r,
						out bool versionSupported, out bool hasSubstitutions, out ushort[] featureIndices, out int[] alternateOffsets, out failure))
					return false;
				records[r] = new OpenTypeLayoutFeatureVariationRecord(conditions, isUniversal, canNeverMatch, isIgnored, versionSupported, hasSubstitutions, featureIndices, alternateOffsets);
			}
			table = new OpenTypeLayoutFeatureVariations(records);
			failure = FontVariationParseFailure.None;
			return true;
		}

		private static bool TryParseConditionSet(FakePtr<byte> fontData, long fvStart, long parentEnd, long conditionSetOffset, int fvarAxisCount,
			string parentTableTag, int recordIndex, out OpenTypeLayoutAxisRangeCondition[] conditions, out bool isUniversal, out bool canNeverMatch,
			out bool isIgnored, out FontVariationParseFailure failure)
		{
			conditions = new OpenTypeLayoutAxisRangeCondition[0];
			isUniversal = false; canNeverMatch = false; isIgnored = false;
			failure = FontVariationParseFailure.None;
			if (conditionSetOffset == 0) { isUniversal = true; return true; } // spec: NULL ConditionSet = universal condition
			long csStart = fvStart + conditionSetOffset;
			if (csStart + 2 > parentEnd)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutConditionSetTruncated, parentTableTag, "record " + recordIndex + ": ConditionSet at " + conditionSetOffset + " beyond the table");
				return false;
			}
			var cs = fontData + (int)csStart;
			int conditionCount = ttUSHORT(cs);
			if (csStart + 2 + conditionCount * 4L > parentEnd)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutConditionSetTruncated, parentTableTag, "record " + recordIndex + ": conditionCount " + conditionCount + " beyond the table");
				return false;
			}
			if (conditionCount == 0) { isUniversal = true; return true; } // spec: empty set matches all contexts
			conditions = new OpenTypeLayoutAxisRangeCondition[conditionCount];
			for (int c = 0; c < conditionCount; c++)
			{
				long condStart = csStart + ttULONG(cs + 2 + c * 4);
				if (condStart + 2 > parentEnd)
				{
					failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutConditionSetTruncated, parentTableTag, "record " + recordIndex + ": condition " + c + " beyond the table");
					return false;
				}
				var cond = fontData + (int)condStart;
				int format = ttUSHORT(cond);
				if (format != 1)
				{
					// Spec: unrecognised condition format => this ConditionSet fails to match; other records are still tested.
					// (Formats 2-5 exist in HarfBuzz / the 2024 proposal but not in the published 1.9.1 chapter — spec §9.11.)
					canNeverMatch = true;
					continue;
				}
				if (condStart + 8 > parentEnd)
				{
					failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutConditionSetTruncated, parentTableTag, "record " + recordIndex + ": ConditionFormat1 " + c + " truncated");
					return false;
				}
				ushort axisIndex = ttUSHORT(cond + 2);
				if (axisIndex >= fvarAxisCount)
					isIgnored = true; // spec: invalid axisIndex => the whole FeatureVariationRecord is ignored
				conditions[c] = new OpenTypeLayoutAxisRangeCondition(axisIndex, ttSHORT(cond + 4), ttSHORT(cond + 6));
			}
			return true;
		}

		private static bool TryParseFeatureTableSubstitution(FakePtr<byte> fontData, long fvStart, long parentEnd, long substitutionOffset,
			string parentTableTag, int recordIndex, out bool versionSupported, out bool hasSubstitutions, out ushort[] featureIndices, out int[] alternateOffsets,
			out FontVariationParseFailure failure)
		{
			versionSupported = true; hasSubstitutions = false;
			featureIndices = new ushort[0]; alternateOffsets = new int[0];
			failure = FontVariationParseFailure.None;
			if (substitutionOffset == 0) return true; // spec: NULL => no substitutions are made (still a match)
			long ftsStart = fvStart + substitutionOffset;
			if (ftsStart + 6 > parentEnd)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutFeatureTableSubstitutionTruncated, parentTableTag, "record " + recordIndex + ": FeatureTableSubstitution at " + substitutionOffset + " beyond the table");
				return false;
			}
			var fts = fontData + (int)ftsStart;
			if (ttUSHORT(fts) != 1)
			{
				// Spec: unsupported FeatureTableSubstitution version => this record is REJECTED when it matches; the reader keeps
				// the record so precedence continues to the next one. Not a parse failure.
				versionSupported = false;
				return true;
			}
			int substitutionCount = ttUSHORT(fts + 4);
			if (ftsStart + 6 + substitutionCount * 6L > parentEnd)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutFeatureTableSubstitutionTruncated, parentTableTag, "record " + recordIndex + ": substitutionCount " + substitutionCount + " beyond the table");
				return false;
			}
			hasSubstitutions = true;
			featureIndices = new ushort[substitutionCount];
			alternateOffsets = new int[substitutionCount];
			for (int s = 0; s < substitutionCount; s++)
			{
				var sub = fts + 6 + s * 6;
				featureIndices[s] = ttUSHORT(sub);
				long altStart = ftsStart + ttULONG(sub + 2);
				// Alternate Feature table: featureParamsOffset(u16) lookupIndexCount(u16) lookupListIndices[count].
				if (altStart + 4 > parentEnd || altStart + 4 + ttUSHORT(fontData + (int)altStart + 2) * 2L > parentEnd)
				{
					failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutFeatureTableSubstitutionTruncated, parentTableTag, "record " + recordIndex + ": alternate Feature table " + s + " beyond the table");
					return false;
				}
				alternateOffsets[s] = (int)altStart;
			}
			return true;
		}
	}
}