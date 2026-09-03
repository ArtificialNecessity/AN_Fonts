using System;
using System.Collections.Generic;
using StbTrueTypeSharp;
using StbTrueTypeSharp.Layout;
using StbTrueTypeSharp.Variations;
using Xunit;

namespace Variations.Tests
{
	/// <summary>
	/// Spec-rule tests for the FeatureVariations machinery on HAND-BUILT GSUB tables (OpenType Layout Common Table Formats
	/// §"Feature variations", OT 1.9.1; SilkyNvg spec §9.1). Each test isolates one sentence of the spec that a real fixture
	/// cannot isolate: precedence, version rejection, unknown condition format, invalid axis index, universal sets, NULL
	/// offsets, knee inclusivity, delta wrap, Extension wrapping, truncation.
	/// </summary>
	public class FeatureVariationsSpecRuleTests
	{
		private const int AxisCount = 2;
		private const int InputGlyph = 5;

		/// <summary>One hand-built SingleSubst lookup: covers InputGlyph only; format 1 (delta) or format 2 (explicit target).</summary>
		private sealed class SyntheticLookup
		{
			public int LookupType = 1;      // 1, 7 (extension wrapping a type 1), or another type to exercise "unsupported"
			public int SubstFormat = 1;
			public short Delta;
			public ushort ExplicitTarget;
		}

		private sealed class SyntheticCondition
		{
			public int Format = 1;
			public ushort AxisIndex;
			public short Min;
			public short Max;
		}

		private sealed class SyntheticRecord
		{
			public SyntheticCondition[]? Conditions;        // null => conditionSetOffset = 0 (universal); empty => empty ConditionSet (universal)
			public bool NullSubstitution;                   // featureTableSubstitutionOffset = 0
			public int SubstitutionMajorVersion = 1;
			public ushort[] AlternateLookups = new ushort[0];
		}

		/// <summary>
		/// Builds [4 pad bytes][GSUB 1.1]: DFLT script -> default LangSys -> one 'rvrn' feature (EMPTY default lookups) +
		/// optional required feature; N lookups; FeatureVariations with the given records. Returns the bytes and the GSUB offset.
		/// </summary>
		private static byte[] BuildGsub(SyntheticLookup[] lookups, SyntheticRecord[] records, out int gsubOffset, ushort[]? defaultRvrnLookups = null,
			ushort[]? requiredFeatureLookups = null, bool truncateFeatureVariations = false)
		{
			var w = new ByteWriter();
			gsubOffset = 4;
			w.U32(0xDEADBEEF); // pad so tableOffset > 0
			int gsubStart = w.Length;
			defaultRvrnLookups = defaultRvrnLookups ?? new ushort[0];
			bool hasRequired = requiredFeatureLookups != null;
			int featureCount = hasRequired ? 2 : 1;

			// Header 1.1
			int scriptListOffset = 14;
			int scriptListSize = 2 + 6 + 4 + (6 + 2);                       // ScriptList + Script + LangSys(1 feature index)
			int featureListOffset = scriptListOffset + scriptListSize;
			int featureListSize = 2 + 6 * featureCount + (4 + 2 * defaultRvrnLookups.Length) + (hasRequired ? 4 + 2 * requiredFeatureLookups!.Length : 0);
			int lookupListOffset = featureListOffset + featureListSize;
			// per-lookup block sizes come from LookupBlockSize(): Lookup(8) [+ Extension(8)] + SingleSubst(6|8) + Coverage(6)
			int lookupListSize = 2 + 2 * lookups.Length;
			var lookupOffsets = new int[lookups.Length];
			int cursor = lookupListSize;
			for (int i = 0; i < lookups.Length; i++) { lookupOffsets[i] = cursor; cursor += LookupBlockSize(lookups[i]); }
			int lookupListTotal = cursor;
			int featureVariationsOffset = lookupListOffset + lookupListTotal;

			w.U16(1); w.U16(1); w.U16((ushort)scriptListOffset); w.U16((ushort)featureListOffset); w.U16((ushort)lookupListOffset);
			w.U32((uint)featureVariationsOffset);

			// ScriptList: 1 record 'DFLT' -> Script at +8
			w.U16(1); w.Tag("DFLT"); w.U16(8);
			// Script: defaultLangSysOffset = 4, langSysCount 0
			w.U16(4); w.U16(0);
			// LangSys: lookupOrder 0, requiredFeatureIndex, featureIndexCount 1, featureIndices[0] = 0 (rvrn)
			w.U16(0); w.U16(hasRequired ? (ushort)1 : (ushort)0xFFFF); w.U16(1); w.U16(0);
			Assert.Equal(featureListOffset, w.Length - gsubStart);

			// FeatureList
			w.U16((ushort)featureCount);
			int rvrnFeatureOffset = 2 + 6 * featureCount;
			w.Tag("rvrn"); w.U16((ushort)rvrnFeatureOffset);
			if (hasRequired) { w.Tag("reqd"); w.U16((ushort)(rvrnFeatureOffset + 4 + 2 * defaultRvrnLookups.Length)); }
			w.U16(0); w.U16((ushort)defaultRvrnLookups.Length); foreach (var l in defaultRvrnLookups) w.U16(l);
			if (hasRequired) { w.U16(0); w.U16((ushort)requiredFeatureLookups!.Length); foreach (var l in requiredFeatureLookups) w.U16(l); }
			Assert.Equal(lookupListOffset, w.Length - gsubStart);

			// LookupList
			w.U16((ushort)lookups.Length);
			foreach (var off in lookupOffsets) w.U16((ushort)off);
			for (int i = 0; i < lookups.Length; i++)
			{
				Assert.Equal(lookupListOffset + lookupOffsets[i], w.Length - gsubStart);
				WriteLookup(w, lookups[i]);
			}
			Assert.Equal(featureVariationsOffset, w.Length - gsubStart);

			// FeatureVariations
			w.U16(1); w.U16(0); w.U32(truncateFeatureVariations ? 1000u : (uint)records.Length);
			if (truncateFeatureVariations) return w.ToArray();
			// records: 8 bytes each; then per-record ConditionSet + FTS blocks appended in order
			int fvBodyCursor = 8 + 8 * records.Length;
			var csOffsets = new int[records.Length];
			var ftsOffsets = new int[records.Length];
			for (int r = 0; r < records.Length; r++)
			{
				var rec = records[r];
				if (rec.Conditions == null) csOffsets[r] = 0;
				else { csOffsets[r] = fvBodyCursor; fvBodyCursor += 2 + 4 * rec.Conditions.Length + 8 * rec.Conditions.Length; }
				if (rec.NullSubstitution) ftsOffsets[r] = 0;
				else { ftsOffsets[r] = fvBodyCursor; fvBodyCursor += 6 + 6 + 4 + 2 * rec.AlternateLookups.Length; }
			}
			for (int r = 0; r < records.Length; r++) { w.U32((uint)csOffsets[r]); w.U32((uint)ftsOffsets[r]); }
			int fvStart = gsubStart + featureVariationsOffset;
			for (int r = 0; r < records.Length; r++)
			{
				var rec = records[r];
				if (rec.Conditions != null)
				{
					Assert.Equal(csOffsets[r], w.Length - fvStart);
					w.U16((ushort)rec.Conditions.Length);
					int condBase = 2 + 4 * rec.Conditions.Length;
					for (int c = 0; c < rec.Conditions.Length; c++) w.U32((uint)(condBase + 8 * c));
					foreach (var cond in rec.Conditions) { w.U16((ushort)cond.Format); w.U16(cond.AxisIndex); w.S16(cond.Min); w.S16(cond.Max); }
				}
				if (!rec.NullSubstitution)
				{
					Assert.Equal(ftsOffsets[r], w.Length - fvStart);
					w.U16((ushort)rec.SubstitutionMajorVersion); w.U16(0); w.U16(1);
					w.U16(0); w.U32(12);                                    // featureIndex 0 (rvrn), alternate Feature at +12
					w.U16(0); w.U16((ushort)rec.AlternateLookups.Length); foreach (var l in rec.AlternateLookups) w.U16(l);
				}
			}
			return w.ToArray();
		}

		private static int LookupBlockSize(SyntheticLookup l)
		{
			int subst = l.SubstFormat == 1 ? 6 : 8;
			int coverage = 6;
			return l.LookupType == 7 ? 8 + 8 + subst + coverage : 8 + subst + coverage;
		}

		private static void WriteLookup(ByteWriter w, SyntheticLookup l)
		{
			w.U16((ushort)l.LookupType); w.U16(0); w.U16(1); w.U16(8);   // Lookup: type, flag, subTableCount 1, subtable at +8
			if (l.LookupType == 7)
			{
				w.U16(1); w.U16(1); w.U32(8);                              // ExtensionSubst fmt1: extensionLookupType 1, extension at +8
			}
			int substSize = l.SubstFormat == 1 ? 6 : 8;
			w.U16((ushort)l.SubstFormat); w.U16((ushort)substSize);        // coverage right after the subtable
			if (l.SubstFormat == 1) w.S16(l.Delta); else { w.U16(1); w.U16(l.ExplicitTarget); }
			w.U16(1); w.U16(1); w.U16(InputGlyph);                          // CoverageFormat1: 1 glyph
		}

		private static OpenTypeGsubTable Parse(byte[] bytes, int gsubOffset)
		{
			Assert.True(OpenTypeGsubTable.TryParse(new FakePtr<byte>(bytes), gsubOffset, bytes.Length - gsubOffset, AxisCount, out var table, out var failure), failure.ToString());
			return table;
		}

		private static FontVariationNormalizedCoordinates Coords(short axis0, short axis1 = 0) => new FontVariationNormalizedCoordinates(new[] { axis0, axis1 });

		private static SyntheticLookup DeltaLookup(short delta) => new SyntheticLookup { Delta = delta };
		private static SyntheticCondition Axis0(short min, short max) => new SyntheticCondition { AxisIndex = 0, Min = min, Max = max };

		private static int Apply(OpenTypeGsubTable table, in FontVariationNormalizedCoordinates coords)
			=> table.ApplySingleSubstitutionLookups(table.ResolveRequiredVariationLookups(coords), InputGlyph);

		[Fact]
		public void FirstMatchingRecord_Wins_EvenWhenLaterRecordsAlsoMatch()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10), DeltaLookup(20) }, new[]
			{
				new SyntheticRecord { Conditions = new[] { Axis0(-16384, 0) }, AlternateLookups = new ushort[] { 0 } },
				new SyntheticRecord { Conditions = new[] { Axis0(-16384, 16384) }, AlternateLookups = new ushort[] { 1 } },
			}, out int off);
			var t = Parse(bytes, off);
			Assert.Equal(InputGlyph + 10, Apply(t, Coords(-8000)));   // both match; record 0 wins
			Assert.Equal(InputGlyph + 20, Apply(t, Coords(+8000)));   // only record 1 matches
		}

		[Fact]
		public void UnsupportedSubstitutionVersion_RejectsTheMatchingRecord_AndContinuesToTheNext()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10), DeltaLookup(20) }, new[]
			{
				new SyntheticRecord { Conditions = new[] { Axis0(-16384, 16384) }, SubstitutionMajorVersion = 2, AlternateLookups = new ushort[] { 0 } },
				new SyntheticRecord { Conditions = new[] { Axis0(-16384, 16384) }, AlternateLookups = new ushort[] { 1 } },
			}, out int off);
			var t = Parse(bytes, off);
			var set = t.ResolveRequiredVariationLookups(Coords(0));
			Assert.Equal(1, set.AppliedFeatureVariationRecordIndex);
			Assert.Equal(InputGlyph + 20, Apply(t, Coords(0)));
		}

		[Fact]
		public void UnrecognisedConditionFormat_FailsThatSet_LaterRecordStillTested()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10), DeltaLookup(20) }, new[]
			{
				new SyntheticRecord { Conditions = new[] { new SyntheticCondition { Format = 9, AxisIndex = 0, Min = -16384, Max = 16384 } }, AlternateLookups = new ushort[] { 0 } },
				new SyntheticRecord { Conditions = new[] { Axis0(-16384, 16384) }, AlternateLookups = new ushort[] { 1 } },
			}, out int off);
			var t = Parse(bytes, off);
			Assert.True(t.FeatureVariations!.Records[0].ConditionSetCanNeverMatch);
			Assert.Equal(InputGlyph + 20, Apply(t, Coords(0)));
		}

		[Fact]
		public void InvalidAxisIndex_IgnoresTheWholeRecord()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10), DeltaLookup(20) }, new[]
			{
				new SyntheticRecord { Conditions = new[] { new SyntheticCondition { AxisIndex = AxisCount, Min = -16384, Max = 16384 } }, AlternateLookups = new ushort[] { 0 } },
				new SyntheticRecord { Conditions = new[] { Axis0(-16384, 16384) }, AlternateLookups = new ushort[] { 1 } },
			}, out int off);
			var t = Parse(bytes, off);
			Assert.True(t.FeatureVariations!.Records[0].IsIgnored);
			Assert.Equal(InputGlyph + 20, Apply(t, Coords(0)));
		}

		[Fact]
		public void EmptyConditionSet_AndNullConditionSetOffset_AreUniversal()
		{
			var emptySet = BuildGsub(new[] { DeltaLookup(10) }, new[] { new SyntheticRecord { Conditions = new SyntheticCondition[0], AlternateLookups = new ushort[] { 0 } } }, out int off1);
			var nullSet = BuildGsub(new[] { DeltaLookup(10) }, new[] { new SyntheticRecord { Conditions = null, AlternateLookups = new ushort[] { 0 } } }, out int off2);
			Assert.Equal(InputGlyph + 10, Apply(Parse(emptySet, off1), Coords(12345, -321)));
			Assert.Equal(InputGlyph + 10, Apply(Parse(nullSet, off2), FontVariationNormalizedCoordinates.Default));
		}

		[Fact]
		public void NullSubstitutionOffset_IsAMatchThatSubstitutesNothing_AndStopsEvaluation()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10) }, new[]
			{
				new SyntheticRecord { Conditions = new[] { Axis0(-16384, 16384) }, NullSubstitution = true },
				new SyntheticRecord { Conditions = new[] { Axis0(-16384, 16384) }, AlternateLookups = new ushort[] { 0 } },
			}, out int off);
			var t = Parse(bytes, off);
			var set = t.ResolveRequiredVariationLookups(Coords(0));
			Assert.Equal(0, set.AppliedFeatureVariationRecordIndex);
			Assert.True(set.IsEmpty);
			Assert.Equal(InputGlyph, Apply(t, Coords(0)));
		}

		[Fact]
		public void AxisRange_IsInclusiveAtBothEnds_OnTheExactF2Dot14()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10) }, new[] { new SyntheticRecord { Conditions = new[] { Axis0(-16384, -8192) }, AlternateLookups = new ushort[] { 0 } } }, out int off);
			var t = Parse(bytes, off);
			Assert.Equal(InputGlyph + 10, Apply(t, Coords(-16384)));  // min inclusive
			Assert.Equal(InputGlyph + 10, Apply(t, Coords(-8192)));   // max inclusive (the FontStyleTest -0.5 knee)
			Assert.Equal(InputGlyph, Apply(t, Coords(-8191)));        // one ULP above: no match
			Assert.Equal(InputGlyph, Apply(t, Coords(0)));
		}

		[Fact]
		public void MultipleConditions_AreANDed()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10) }, new[] { new SyntheticRecord { Conditions = new[] { Axis0(0, 16384), new SyntheticCondition { AxisIndex = 1, Min = 8192, Max = 16384 } }, AlternateLookups = new ushort[] { 0 } } }, out int off);
			var t = Parse(bytes, off);
			Assert.Equal(InputGlyph + 10, Apply(t, Coords(100, 9000)));
			Assert.Equal(InputGlyph, Apply(t, Coords(100, 100)));     // axis 1 fails
			Assert.Equal(InputGlyph, Apply(t, Coords(-100, 9000)));   // axis 0 fails
		}

		[Fact]
		public void SingleSubstFormat1_DeltaWrapsModulo65536_AndFormat2_UsesCoverageIndex()
		{
			var wrap = BuildGsub(new[] { DeltaLookup(-7) }, new[] { new SyntheticRecord { Conditions = null, AlternateLookups = new ushort[] { 0 } } }, out int off1);
			Assert.Equal((InputGlyph - 7) & 0xFFFF, Apply(Parse(wrap, off1), Coords(0)));
			var fmt2 = BuildGsub(new[] { new SyntheticLookup { SubstFormat = 2, ExplicitTarget = 777 } }, new[] { new SyntheticRecord { Conditions = null, AlternateLookups = new ushort[] { 0 } } }, out int off2);
			Assert.Equal(777, Apply(Parse(fmt2, off2), Coords(0)));
		}

		[Fact]
		public void ExtensionLookupType7_WrappingSingleSubst_IsApplied()
		{
			var bytes = BuildGsub(new[] { new SyntheticLookup { LookupType = 7, Delta = 33 } }, new[] { new SyntheticRecord { Conditions = null, AlternateLookups = new ushort[] { 0 } } }, out int off);
			var t = Parse(bytes, off);
			Assert.True(t.IsSingleSubstitutionLookup(0));
			Assert.Equal(InputGlyph + 33, Apply(t, Coords(0)));
		}

		[Fact]
		public void LookupsApplyInLookupListOrder_EachToThePreviousOutput()
		{
			// Alternate feature lists lookups as [1, 0]; application order must still be LookupList order 0 then 1.
			// Lookup 0 covers InputGlyph -> +10; lookup 1 covers InputGlyph too but the glyph has already moved, so it must NOT apply.
			var bytes = BuildGsub(new[] { DeltaLookup(10), DeltaLookup(100) }, new[] { new SyntheticRecord { Conditions = null, AlternateLookups = new ushort[] { 1, 0 } } }, out int off);
			var t = Parse(bytes, off);
			var set = t.ResolveRequiredVariationLookups(Coords(0));
			Assert.Equal(new ushort[] { 0, 1 }, set.LookupIndicesAscending);
			Assert.Equal(InputGlyph + 10, Apply(t, Coords(0)));
		}

		[Fact]
		public void RequiredFeature_IsApplied_AndUnsupportedLookupTypes_AreCountedNotApplied()
		{
			// Required feature -> lookup 1 (type 4 ligature: unsupported this milestone); rvrn default empty; no FV records.
			var bytes = BuildGsub(new[] { DeltaLookup(10), new SyntheticLookup { LookupType = 4, Delta = 99 } }, new SyntheticRecord[0],
				out int off, defaultRvrnLookups: new ushort[] { 0 }, requiredFeatureLookups: new ushort[] { 1 });
			var t = Parse(bytes, off);
			var set = t.ResolveRequiredVariationLookups(Coords(0));
			Assert.Equal(new ushort[] { 0, 1 }, set.LookupIndicesAscending);
			Assert.Equal(1, set.UnsupportedLookupCount);
			Assert.Equal(InputGlyph + 10, Apply(t, Coords(0)));   // lookup 1 skipped
		}

		[Fact]
		public void DefaultRvrnLookups_Apply_WhenNoRecordMatches()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10), DeltaLookup(20) }, new[] { new SyntheticRecord { Conditions = new[] { Axis0(8192, 16384) }, AlternateLookups = new ushort[] { 1 } } },
				out int off, defaultRvrnLookups: new ushort[] { 0 });
			var t = Parse(bytes, off);
			Assert.Equal(InputGlyph + 10, Apply(t, Coords(0)));       // default rvrn
			Assert.Equal(InputGlyph + 20, Apply(t, Coords(10000)));   // alternate REPLACES the default feature table (not additive)
		}

		[Fact]
		public void TruncatedFeatureVariations_IsAParseFailure_NeverAThrow()
		{
			var bytes = BuildGsub(new[] { DeltaLookup(10) }, new SyntheticRecord[0], out int off, truncateFeatureVariations: true);
			Assert.False(OpenTypeGsubTable.TryParse(new FakePtr<byte>(bytes), off, bytes.Length - off, AxisCount, out _, out var failure));
			Assert.Equal(FontVariationParseFailureCode.LayoutFeatureVariationsTruncated, failure.Code);
			Assert.Equal("GSUB", failure.TableTag);
		}

		/// <summary>Big-endian byte writer for hand-built OpenType tables.</summary>
		private sealed class ByteWriter
		{
			private readonly List<byte> _bytes = new List<byte>();
			public int Length => _bytes.Count;
			public void U16(ushort v) { _bytes.Add((byte)(v >> 8)); _bytes.Add((byte)v); }
			public void S16(short v) => U16((ushort)v);
			public void U32(uint v) { _bytes.Add((byte)(v >> 24)); _bytes.Add((byte)(v >> 16)); _bytes.Add((byte)(v >> 8)); _bytes.Add((byte)v); }
			public void Tag(string tag) { foreach (char c in tag) _bytes.Add((byte)c); }
			public byte[] ToArray() => _bytes.ToArray();
		}
	}
}