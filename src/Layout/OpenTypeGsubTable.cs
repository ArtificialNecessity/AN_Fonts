using System;
using StbTrueTypeSharp.Variations;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Layout
{
	/// <summary>
	/// The GSUB lookups in force for ONE variation instance after FeatureVariations resolution (spec §9.4): the union of
	/// the lookup indices of every selected feature ('rvrn' + the LangSys required feature), ascending = LookupList
	/// application order, de-duplicated. <see cref="UnsupportedLookupCount"/> counts lookups among them whose type is not
	/// SingleSubst (1) or Extension-wrapped SingleSubst (7->1) — THIS MILESTONE skips those (spec §9.8, approved).
	/// </summary>
	public readonly struct GsubResolvedLookupSet
	{
		public static readonly GsubResolvedLookupSet Empty = new GsubResolvedLookupSet(new ushort[0], 0, -1);

		public ushort[] LookupIndicesAscending { get; }
		public int UnsupportedLookupCount { get; }
		/// <summary>Index of the FeatureVariations record that was applied, or -1 (default Feature tables in force).</summary>
		public int AppliedFeatureVariationRecordIndex { get; }
		public bool IsEmpty => LookupIndicesAscending.Length == 0;

		public GsubResolvedLookupSet(ushort[] lookupIndicesAscending, int unsupportedLookupCount, int appliedFeatureVariationRecordIndex)
		{
			LookupIndicesAscending = lookupIndicesAscending;
			UnsupportedLookupCount = unsupportedLookupCount;
			AppliedFeatureVariationRecordIndex = appliedFeatureVariationRecordIndex;
		}
	}

	/// <summary>
	/// Parsed GSUB (OpenType 1.9.1 — _EXTERNAL_APIS/OpenType/gsub.md) restricted to what Part D needs: header 1.0/1.1,
	/// ScriptList/FeatureList/LookupList, the FeatureVariations table (shared reader), and SingleSubst application.
	/// Deliberately NOT a shaper (README "Scope", spec §9.8/§9.10): no glyph-sequence buffer, so lookup types 2–6 and 8 are
	/// counted, never applied. Applied UNIVERSALLY — to static fonts as well — because 'rvrn' is a required feature and
	/// because fontTools bakes the resolved 'rvrn' into a STATIC twin (the substitution oracle).
	/// </summary>
	public sealed class OpenTypeGsubTable
	{
		public const ushort LookupTypeSingleSubstitution = 1;
		public const ushort LookupTypeExtensionSubstitution = 7;

		private readonly FakePtr<byte> _fontData;
		private readonly int _tableOffset;
		private readonly int _tableLength;
		private readonly int _featureListOffset;
		private readonly int _lookupListOffset;
		private readonly int _lookupCount;
		private readonly int _featureCount;
		private readonly FakePtr<byte> _defaultLangSys;   // Null when no latn/DFLT default LangSys

		/// <summary>Parsed FeatureVariations (GSUB 1.1 with a non-NULL offset); null otherwise.</summary>
		public OpenTypeLayoutFeatureVariations FeatureVariations { get; }
		public int MinorVersion { get; }
		public int LookupCount => _lookupCount;
		/// <summary>True when the default LangSys reaches an 'rvrn' feature or names a required feature: something CAN apply.</summary>
		public bool HasRequiredVariationAlternates { get; }

		private OpenTypeGsubTable(FakePtr<byte> fontData, int tableOffset, int tableLength, int minorVersion, int featureListOffset, int featureCount,
			int lookupListOffset, int lookupCount, FakePtr<byte> defaultLangSys, OpenTypeLayoutFeatureVariations featureVariations, bool hasRequiredVariationAlternates)
		{
			_fontData = fontData;
			_tableOffset = tableOffset;
			_tableLength = tableLength;
			MinorVersion = minorVersion;
			_featureListOffset = featureListOffset;
			_featureCount = featureCount;
			_lookupListOffset = lookupListOffset;
			_lookupCount = lookupCount;
			_defaultLangSys = defaultLangSys;
			FeatureVariations = featureVariations;
			HasRequiredVariationAlternates = hasRequiredVariationAlternates;
		}

		public static bool TryParse(FakePtr<byte> fontData, int tableOffset, int tableLength, int fvarAxisCount,
			out OpenTypeGsubTable table, out FontVariationParseFailure failure)
		{
			table = null;
			if (tableLength < 10 || tableOffset <= 0 || (long)tableOffset + tableLength > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GsubTruncatedHeader, "GSUB", "table shorter than the 10-byte header or outside the font data");
				return false;
			}
			var g = fontData + tableOffset;
			int majorVersion = ttUSHORT(g);
			int minorVersion = ttUSHORT(g + 2);
			if (majorVersion != 1 || minorVersion > 1)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GsubUnsupportedVersion, "GSUB", "version " + majorVersion + "." + minorVersion);
				return false;
			}
			int scriptListOffset = ttUSHORT(g + 4);
			int featureListOffset = ttUSHORT(g + 6);
			int lookupListOffset = ttUSHORT(g + 8);
			if (scriptListOffset == 0 || scriptListOffset + 2 > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GsubScriptListOutOfBounds, "GSUB", "scriptListOffset " + scriptListOffset);
				return false;
			}
			if (featureListOffset == 0 || featureListOffset + 2 > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GsubFeatureListOutOfBounds, "GSUB", "featureListOffset " + featureListOffset);
				return false;
			}
			int featureCount = ttUSHORT(g + featureListOffset);
			if (featureListOffset + 2 + featureCount * 6L > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GsubFeatureListOutOfBounds, "GSUB", "featureCount " + featureCount + " beyond the table");
				return false;
			}
			if (lookupListOffset == 0 || lookupListOffset + 2 > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GsubLookupListOutOfBounds, "GSUB", "lookupListOffset " + lookupListOffset);
				return false;
			}
			int lookupCount = ttUSHORT(g + lookupListOffset);
			if (lookupListOffset + 2 + lookupCount * 2L > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.GsubLookupListOutOfBounds, "GSUB", "lookupCount " + lookupCount + " beyond the table");
				return false;
			}

			OpenTypeLayoutFeatureVariations featureVariations = null;
			if (minorVersion >= 1)
			{
				if (tableLength < 14)
				{
					failure = new FontVariationParseFailure(FontVariationParseFailureCode.GsubTruncatedHeader, "GSUB", "version 1.1 header shorter than 14 bytes");
					return false;
				}
				long featureVariationsOffset = ttULONG(g + 10); // Offset32 at byte 10 — identical position in GPOS 1.1
				if (featureVariationsOffset != 0)
				{
					if (featureVariationsOffset >= tableLength)
					{
						failure = new FontVariationParseFailure(FontVariationParseFailureCode.LayoutFeatureVariationsTruncated, "GSUB", "featureVariationsOffset " + featureVariationsOffset + " >= tableLength " + tableLength);
						return false;
					}
					if (!OpenTypeLayoutFeatureVariations.TryParse(fontData, tableOffset, tableLength, (int)featureVariationsOffset, fvarAxisCount, "GSUB", out featureVariations, out failure))
						return false;
				}
			}

			FakePtr<byte> langSys;
			bool haveLangSys = OpenTypeLayoutScriptSelection.TryGetDefaultLangSys(g + scriptListOffset, tableLength - scriptListOffset, out langSys);
			bool hasRvrnOrRequired = false;
			if (haveLangSys)
			{
				if (OpenTypeLayoutScriptSelection.GetRequiredFeatureIndex(langSys) >= 0) hasRvrnOrRequired = true;
				int n = OpenTypeLayoutScriptSelection.GetFeatureIndexCount(langSys);
				for (int i = 0; i < n && !hasRvrnOrRequired; i++)
				{
					int featureIndex = OpenTypeLayoutScriptSelection.GetFeatureIndex(langSys, i);
					if (featureIndex < featureCount && IsRvrnTag(g + featureListOffset + 2 + 6 * featureIndex)) hasRvrnOrRequired = true;
				}
			}

			table = new OpenTypeGsubTable(fontData, tableOffset, tableLength, minorVersion, featureListOffset, featureCount, lookupListOffset, lookupCount,
				haveLangSys ? langSys : FakePtr<byte>.Null, featureVariations, hasRvrnOrRequired);
			failure = FontVariationParseFailure.None;
			return true;
		}

		private static bool IsRvrnTag(FakePtr<byte> featureRecord) => featureRecord[0] == 'r' && featureRecord[1] == 'v' && featureRecord[2] == 'r' && featureRecord[3] == 'n';

		/// <summary>
		/// Spec §9.4 step 1–3: select 'rvrn' feature(s) + the required feature from the default LangSys; replace each selected
		/// Feature table by its FeatureVariations alternate when the first applicable record names it; union the lookup
		/// indices ascending. The result is what fontTools bakes into an instanced twin's plain 'rvrn' feature.
		/// </summary>
		public GsubResolvedLookupSet ResolveRequiredVariationLookups(in FontVariationNormalizedCoordinates coords)
		{
			if (_defaultLangSys.IsNull || !HasRequiredVariationAlternates) return GsubResolvedLookupSet.Empty;
			int appliedRecord = FeatureVariations == null ? -1 : FeatureVariations.FindApplicableRecordIndex(coords);
			var record = appliedRecord >= 0 ? FeatureVariations.Records[appliedRecord] : null;

			var marked = new bool[_lookupCount];
			int unsupported = 0;
			int required = OpenTypeLayoutScriptSelection.GetRequiredFeatureIndex(_defaultLangSys);
			if (required >= 0 && required < _featureCount)
				MarkFeatureLookups(required, record, marked, ref unsupported);
			int featureIndexCount = OpenTypeLayoutScriptSelection.GetFeatureIndexCount(_defaultLangSys);
			for (int i = 0; i < featureIndexCount; i++)
			{
				int featureIndex = OpenTypeLayoutScriptSelection.GetFeatureIndex(_defaultLangSys, i);
				if (featureIndex >= _featureCount || featureIndex == required) continue;
				if (!IsRvrnTag(_fontData + _tableOffset + _featureListOffset + 2 + 6 * featureIndex)) continue;
				MarkFeatureLookups(featureIndex, record, marked, ref unsupported);
			}

			int count = 0;
			for (int i = 0; i < marked.Length; i++) if (marked[i]) count++;
			var indices = new ushort[count];
			for (int i = 0, w = 0; i < marked.Length; i++) if (marked[i]) indices[w++] = (ushort)i;
			return new GsubResolvedLookupSet(indices, unsupported, appliedRecord);
		}

		private void MarkFeatureLookups(int featureIndex, OpenTypeLayoutFeatureVariationRecord appliedRecord, bool[] marked, ref int unsupported)
		{
			FakePtr<byte> featureTable;
			int alternateAbsolute = appliedRecord == null ? -1 : appliedRecord.GetAlternateFeatureTableAbsoluteOffset(featureIndex);
			if (alternateAbsolute >= 0)
				featureTable = _fontData + alternateAbsolute;
			else
			{
				var featureList = _fontData + _tableOffset + _featureListOffset;
				int featureOffset = ttUSHORT(featureList + 2 + 6 * featureIndex + 4);
				if (_featureListOffset + featureOffset + 4 > _tableLength) return;
				featureTable = featureList + featureOffset;
			}
			int lookupIndexCount = ttUSHORT(featureTable + 2);
			for (int li = 0; li < lookupIndexCount; li++)
			{
				int lookupIndex = ttUSHORT(featureTable + 4 + 2 * li);
				if (lookupIndex >= _lookupCount || marked[lookupIndex]) continue;
				marked[lookupIndex] = true;
				if (!IsSingleSubstitutionLookup(lookupIndex)) unsupported++;
			}
		}

		private FakePtr<byte> GetLookupTable(int lookupIndex, out int bytesAvailable)
		{
			var lookupList = _fontData + _tableOffset + _lookupListOffset;
			int lookupOffset = ttUSHORT(lookupList + 2 + 2 * lookupIndex);
			bytesAvailable = _tableLength - _lookupListOffset - lookupOffset;
			return lookupList + lookupOffset;
		}

		/// <summary>Type 1, or type 7 whose extension subtables are type 1 (spec: all extension subtables share one type).</summary>
		public bool IsSingleSubstitutionLookup(int lookupIndex)
		{
			var lookup = GetLookupTable(lookupIndex, out int avail);
			if (avail < 6) return false;
			int lookupType = ttUSHORT(lookup);
			if (lookupType == LookupTypeSingleSubstitution) return true;
			if (lookupType != LookupTypeExtensionSubstitution) return false;
			int subTableCount = ttUSHORT(lookup + 4);
			if (subTableCount == 0 || 6 + subTableCount * 2 > avail) return false;
			var ext = lookup + ttUSHORT(lookup + 6);
			return ext.RemainingLength >= 8 && ttUSHORT(ext) == 1 && ttUSHORT(ext + 2) == LookupTypeSingleSubstitution;
		}

		/// <summary>
		/// Applies the resolved lookups to ONE glyph, in LookupList order, each to the output of the previous (spec: "the
		/// substitution action of each lookup is applied to the results of previous lookups"). Within a lookup the FIRST
		/// subtable whose Coverage contains the glyph applies and ends the lookup. LookupFlags are irrelevant for a single-glyph
		/// input (spec: flags affect OTHER glyphs of the input sequence, never the current glyph). Non-SingleSubst lookups are
		/// skipped (counted at resolve time). Never throws: malformed subtables leave the glyph unchanged.
		/// </summary>
		public int ApplySingleSubstitutionLookups(in GsubResolvedLookupSet lookups, int glyph)
		{
			var indices = lookups.LookupIndicesAscending;
			for (int k = 0; k < indices.Length; k++)
			{
				var lookup = GetLookupTable(indices[k], out int avail);
				if (avail < 6) continue;
				int lookupType = ttUSHORT(lookup);
				if (lookupType != LookupTypeSingleSubstitution && lookupType != LookupTypeExtensionSubstitution) continue;
				int subTableCount = ttUSHORT(lookup + 4);
				if (6 + subTableCount * 2 > avail) continue;
				for (int st = 0; st < subTableCount; st++)
				{
					var subtable = lookup + ttUSHORT(lookup + 6 + 2 * st);
					if (lookupType == LookupTypeExtensionSubstitution)
					{
						if (subtable.RemainingLength < 8 || ttUSHORT(subtable) != 1 || ttUSHORT(subtable + 2) != LookupTypeSingleSubstitution) break;
						subtable = subtable + (int)ttULONG(subtable + 4);
					}
					if (TryApplySingleSubstSubtable(subtable, glyph, out int substituted))
					{
						glyph = substituted;
						break; // first applying subtable ends this lookup
					}
				}
			}
			return glyph;
		}

		/// <summary>SingleSubstFormat1 (delta, mod 65536) / SingleSubstFormat2 (array by coverage index). False = not covered / malformed.</summary>
		private bool TryApplySingleSubstSubtable(FakePtr<byte> subtable, int glyph, out int substituted)
		{
			substituted = glyph;
			if (subtable.RemainingLength < 6) return false;
			int format = ttUSHORT(subtable);
			int coverageOffset = ttUSHORT(subtable + 2);
			var coverage = subtable + coverageOffset;
			if (coverage.RemainingLength < 4) return false;
			int coverageFormat = ttUSHORT(coverage);
			int coverageEntries = ttUSHORT(coverage + 2);
			if ((coverageFormat == 1 && coverage.RemainingLength < 4 + coverageEntries * 2) || (coverageFormat == 2 && coverage.RemainingLength < 4 + coverageEntries * 6)) return false;
			int coverageIndex = stbtt__GetCoverageIndex(coverage, glyph);
			if (coverageIndex < 0) return false;
			switch (format)
			{
				case 1:
				{
					int delta = ttSHORT(subtable + 4);
					substituted = (glyph + delta) & 0xFFFF; // spec: addition modulo 65536
					return true;
				}
				case 2:
				{
					int glyphCount = ttUSHORT(subtable + 4);
					if (coverageIndex >= glyphCount || subtable.RemainingLength < 6 + glyphCount * 2) return false;
					substituted = ttUSHORT(subtable + 6 + 2 * coverageIndex);
					return true;
				}
				default:
					return false;
			}
		}
	}
}