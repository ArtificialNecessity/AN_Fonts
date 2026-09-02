using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Variations
{
	/// <summary>
	/// Parsed HVAR (OpenType 1.9.1 — _EXTERNAL_APIS/OpenType/hvar.md): advance-width variation (required) plus
	/// optional lsb/rsb maps. When present, HVAR WINS over phantom-point-derived advances (spec, HarfBuzz, fontTools).
	/// </summary>
	public sealed class OpenTypeHvarTable
	{
		public OpenTypeItemVariationStore Store { get; }
		/// <summary>Null => implicit mapping: outer 0, inner = glyph id.</summary>
		public OpenTypeDeltaSetIndexMap AdvanceWidthMapping { get; }
		public OpenTypeDeltaSetIndexMap LeftSideBearingMapping { get; }

		private OpenTypeHvarTable(OpenTypeItemVariationStore store, OpenTypeDeltaSetIndexMap advanceWidthMapping, OpenTypeDeltaSetIndexMap leftSideBearingMapping)
		{
			Store = store;
			AdvanceWidthMapping = advanceWidthMapping;
			LeftSideBearingMapping = leftSideBearingMapping;
		}

		public static bool TryParse(FakePtr<byte> fontData, int tableOffset, int tableLength, int fvarAxisCount,
			out OpenTypeHvarTable table, out FontVariationParseFailure failure)
		{
			table = null;
			if (tableLength < 20 || tableOffset < 0 || tableOffset + tableLength > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.HvarTruncatedHeader, "HVAR", "table shorter than the 20-byte header or outside the font data");
				return false;
			}
			var t = fontData + tableOffset;
			long storeOffset = ttULONG(t + 4);
			long advanceMapOffset = ttULONG(t + 8);
			long lsbMapOffset = ttULONG(t + 12);
			if (storeOffset == 0 || storeOffset >= tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.HvarTruncatedHeader, "HVAR", "itemVariationStoreOffset " + storeOffset + " invalid");
				return false;
			}
			if (!OpenTypeItemVariationStore.TryParse(fontData, tableOffset + (int)storeOffset, tableLength - (int)storeOffset, fvarAxisCount, "HVAR", out OpenTypeItemVariationStore store, out failure))
				return false;
			OpenTypeDeltaSetIndexMap advanceMap = null;
			if (advanceMapOffset != 0)
			{
				if (advanceMapOffset >= tableLength || !OpenTypeDeltaSetIndexMap.TryParse(fontData, tableOffset + (int)advanceMapOffset, tableLength - (int)advanceMapOffset, "HVAR", out advanceMap, out failure))
					return false;
			}
			OpenTypeDeltaSetIndexMap lsbMap = null;
			if (lsbMapOffset != 0)
			{
				if (lsbMapOffset >= tableLength || !OpenTypeDeltaSetIndexMap.TryParse(fontData, tableOffset + (int)lsbMapOffset, tableLength - (int)lsbMapOffset, "HVAR", out lsbMap, out failure))
					return false;
			}
			table = new OpenTypeHvarTable(store, advanceMap, lsbMap);
			failure = FontVariationParseFailure.None;
			return true;
		}

		/// <summary>Unrounded advance-width adjustment for the glyph at the instance.</summary>
		public double ComputeAdvanceWidthDelta(int glyphIndex, in FontVariationNormalizedCoordinates instance)
		{
			int outer, inner;
			if (AdvanceWidthMapping != null) AdvanceWidthMapping.Lookup(glyphIndex, out outer, out inner);
			else { outer = 0; inner = glyphIndex; }
			return Store.ComputeDelta(outer, inner, instance);
		}
	}

	/// <summary>
	/// Parsed MVAR (OpenType 1.9.1 — _EXTERNAL_APIS/OpenType/mvar.md): font-wide metric deltas keyed by 4-byte
	/// value tags (hasc/hdsc/hlgp = OS/2 typo ascender/descender/lineGap, xhgt, cpht, ...).
	/// </summary>
	public sealed class OpenTypeMvarTable
	{
		public static readonly uint TagHorizontalAscender = OpenTypeAxisTag.FromString("hasc").PackedValue;
		public static readonly uint TagHorizontalDescender = OpenTypeAxisTag.FromString("hdsc").PackedValue;
		public static readonly uint TagHorizontalLineGap = OpenTypeAxisTag.FromString("hlgp").PackedValue;
		public static readonly uint TagXHeight = OpenTypeAxisTag.FromString("xhgt").PackedValue;
		public static readonly uint TagCapHeight = OpenTypeAxisTag.FromString("cpht").PackedValue;

		public OpenTypeItemVariationStore Store { get; }
		private readonly uint[] _valueTags;      // sorted per spec (binary order)
		private readonly int[] _outerIndices;
		private readonly int[] _innerIndices;

		public int ValueRecordCount => _valueTags.Length;

		private OpenTypeMvarTable(OpenTypeItemVariationStore store, uint[] valueTags, int[] outerIndices, int[] innerIndices)
		{
			Store = store;
			_valueTags = valueTags;
			_outerIndices = outerIndices;
			_innerIndices = innerIndices;
		}

		public static bool TryParse(FakePtr<byte> fontData, int tableOffset, int tableLength, int fvarAxisCount,
			out OpenTypeMvarTable table, out FontVariationParseFailure failure)
		{
			table = null;
			if (tableLength < 12 || tableOffset < 0 || tableOffset + tableLength > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.MvarTruncatedHeader, "MVAR", "table shorter than the 12-byte header or outside the font data");
				return false;
			}
			var t = fontData + tableOffset;
			int valueRecordSize = ttUSHORT(t + 6);
			int valueRecordCount = ttUSHORT(t + 8);
			int storeOffset = ttUSHORT(t + 10);
			if (valueRecordCount == 0 || storeOffset == 0)
			{
				// Legal: an MVAR with no records varies nothing.
				table = new OpenTypeMvarTable(null, new uint[0], new int[0], new int[0]);
				failure = FontVariationParseFailure.None;
				return true;
			}
			if (valueRecordSize < 8 || 12L + (long)valueRecordCount * valueRecordSize > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.MvarValueRecordsOutOfBounds, "MVAR", "valueRecords beyond table end (size " + valueRecordSize + ", count " + valueRecordCount + ")");
				return false;
			}
			if (storeOffset >= tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.MvarTruncatedHeader, "MVAR", "itemVariationStoreOffset " + storeOffset + " >= tableLength " + tableLength);
				return false;
			}
			if (!OpenTypeItemVariationStore.TryParse(fontData, tableOffset + storeOffset, tableLength - storeOffset, fvarAxisCount, "MVAR", out OpenTypeItemVariationStore store, out failure))
				return false;
			var tags = new uint[valueRecordCount];
			var outers = new int[valueRecordCount];
			var inners = new int[valueRecordCount];
			for (int i = 0; i < valueRecordCount; i++)
			{
				var rec = t + 12 + i * valueRecordSize;
				tags[i] = ttULONG(rec);
				outers[i] = ttUSHORT(rec + 4);
				inners[i] = ttUSHORT(rec + 6);
			}
			table = new OpenTypeMvarTable(store, tags, outers, inners);
			failure = FontVariationParseFailure.None;
			return true;
		}

		/// <summary>Unrounded delta for the value tag at the instance; 0 when the tag is absent (constant metric).</summary>
		public double ComputeDelta(uint valueTag, in FontVariationNormalizedCoordinates instance)
		{
			if (Store == null) return 0.0;
			for (int i = 0; i < _valueTags.Length; i++)   // records are few (<= ~40); linear scan is fine
				if (_valueTags[i] == valueTag)
					return Store.ComputeDelta(_outerIndices[i], _innerIndices[i], instance);
			return 0.0;
		}
	}
}