using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Variations
{
	/// <summary>
	/// ItemVariationStore (otvarcommonformats "Item Variation Store"): a VariationRegionList + N
	/// ItemVariationData subtables of delta-set rows. Used by HVAR and MVAR here (later GPOS VariationIndex
	/// and CFF2). Regions are decoded once; delta rows are read on demand and bounds-checked.
	/// Value at an instance = default + otRound(Σ regionScalar × delta) (README §5, fontTools VarStoreInstancer).
	/// </summary>
	public sealed class OpenTypeItemVariationStore
	{
		/// <summary>Outer/inner both 0xFFFF: the item has no variation data.</summary>
		public const int NoVariationIndex = 0xFFFF;

		private readonly FakePtr<byte> _fontData;
		private readonly int _storeOffset;   // absolute
		private readonly int _storeLength;   // bytes available from _storeOffset (parent table end)
		private readonly double[][] _regionStart;  // [region][axis]
		private readonly double[][] _regionPeak;
		private readonly double[][] _regionEnd;
		private readonly int[] _itemVariationDataOffsets; // absolute; 0 = NULL

		public int AxisCount { get; }
		public int RegionCount => _regionPeak.Length;
		public int ItemVariationDataCount => _itemVariationDataOffsets.Length;

		private OpenTypeItemVariationStore(FakePtr<byte> fontData, int storeOffset, int storeLength, int axisCount,
			double[][] regionStart, double[][] regionPeak, double[][] regionEnd, int[] itemVariationDataOffsets)
		{
			_fontData = fontData;
			_storeOffset = storeOffset;
			_storeLength = storeLength;
			AxisCount = axisCount;
			_regionStart = regionStart;
			_regionPeak = regionPeak;
			_regionEnd = regionEnd;
			_itemVariationDataOffsets = itemVariationDataOffsets;
		}

		public static bool TryParse(FakePtr<byte> fontData, int storeOffset, int storeLength, int fvarAxisCount, string parentTableTag,
			out OpenTypeItemVariationStore store, out FontVariationParseFailure failure)
		{
			store = null;
			if (storeLength < 8 || storeOffset < 0 || storeOffset + storeLength > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.ItemVariationStoreTruncated, parentTableTag, "ItemVariationStore shorter than 8 bytes or outside the font data");
				return false;
			}
			var s = fontData + storeOffset;
			ushort format = ttUSHORT(s);
			if (format != 1)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.ItemVariationStoreUnsupportedFormat, parentTableTag, "ItemVariationStore format " + format);
				return false;
			}
			long regionListOffset = ttULONG(s + 2);
			int itemVariationDataCount = ttUSHORT(s + 6);
			if (8L + itemVariationDataCount * 4L > storeLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.ItemVariationStoreTruncated, parentTableTag, "itemVariationDataOffsets array beyond store end");
				return false;
			}
			// Region list
			if (regionListOffset + 4 > storeLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.ItemVariationStoreRegionListOutOfBounds, parentTableTag, "VariationRegionList header beyond store end");
				return false;
			}
			var r = s + (int)regionListOffset;
			int axisCount = ttUSHORT(r);
			int regionCount = ttUSHORT(r + 2) & 0x7FFF;
			if (axisCount != fvarAxisCount)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.ItemVariationStoreAxisCountMismatch, parentTableTag, "VariationRegionList axisCount " + axisCount + " != fvar axisCount " + fvarAxisCount);
				return false;
			}
			if (regionListOffset + 4 + (long)regionCount * axisCount * 6 > storeLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.ItemVariationStoreRegionListOutOfBounds, parentTableTag, "VariationRegionList records beyond store end");
				return false;
			}
			var regionStart = new double[regionCount][];
			var regionPeak = new double[regionCount][];
			var regionEnd = new double[regionCount][];
			for (int region = 0; region < regionCount; region++)
			{
				regionStart[region] = new double[axisCount];
				regionPeak[region] = new double[axisCount];
				regionEnd[region] = new double[axisCount];
				var rec = r + 4 + region * axisCount * 6;
				for (int axis = 0; axis < axisCount; axis++)
				{
					regionStart[region][axis] = ttSHORT(rec + axis * 6) / 16384.0;
					regionPeak[region][axis] = ttSHORT(rec + axis * 6 + 2) / 16384.0;
					regionEnd[region][axis] = ttSHORT(rec + axis * 6 + 4) / 16384.0;
				}
			}
			// Subtable offsets (validated lazily per read; NULL = 0)
			var dataOffsets = new int[itemVariationDataCount];
			for (int i = 0; i < itemVariationDataCount; i++)
			{
				long off = ttULONG(s + 8 + i * 4);
				if (off != 0 && off + 6 > storeLength)
				{
					failure = new FontVariationParseFailure(FontVariationParseFailureCode.ItemVariationStoreDataOutOfBounds, parentTableTag, "ItemVariationData[" + i + "] header beyond store end");
					return false;
				}
				dataOffsets[i] = off == 0 ? 0 : storeOffset + (int)off;
			}
			store = new OpenTypeItemVariationStore(fontData, storeOffset, storeLength, axisCount, regionStart, regionPeak, regionEnd, dataOffsets);
			failure = FontVariationParseFailure.None;
			return true;
		}

		/// <summary>
		/// Interpolated adjustment (UNROUNDED double) for delta-set (outer, inner) at the instance. 0 for
		/// NoVariationIndex, NULL subtables, out-of-range indices, or malformed rows (fail-soft, like fontTools).
		/// </summary>
		public double ComputeDelta(int outerIndex, int innerIndex, in FontVariationNormalizedCoordinates instance)
		{
			if (instance.IsDefault) return 0.0;
			if (outerIndex == NoVariationIndex && innerIndex == NoVariationIndex) return 0.0;
			if (outerIndex < 0 || outerIndex >= _itemVariationDataOffsets.Length) return 0.0;
			int dataOffset = _itemVariationDataOffsets[outerIndex];
			if (dataOffset == 0) return 0.0;
			var d = _fontData + dataOffset;
			int storeEnd = _storeOffset + _storeLength;
			int itemCount = ttUSHORT(d);
			ushort wordDeltaCountField = ttUSHORT(d + 2);
			int regionIndexCount = ttUSHORT(d + 4);
			bool longWords = (wordDeltaCountField & 0x8000) != 0;
			int wordDeltaCount = wordDeltaCountField & 0x7FFF;
			if (innerIndex < 0 || innerIndex >= itemCount || wordDeltaCount > regionIndexCount) return 0.0;
			int regionIndexesOffset = dataOffset + 6;
			int rowSize = longWords ? (regionIndexCount + wordDeltaCount) * 2 : regionIndexCount + wordDeltaCount;
			long rowsStart = regionIndexesOffset + regionIndexCount * 2L;
			long rowStart = rowsStart + (long)innerIndex * rowSize;
			if (rowStart + rowSize > storeEnd) return 0.0;
			var row = _fontData + (int)rowStart;
			double net = 0.0;
			int cursor = 0;
			for (int column = 0; column < regionIndexCount; column++)
			{
				int delta;
				bool isWord = column < wordDeltaCount;
				if (longWords)
				{
					if (isWord) { delta = ttLONG(row + cursor); cursor += 4; }
					else { delta = ttSHORT(row + cursor); cursor += 2; }
				}
				else
				{
					if (isWord) { delta = ttSHORT(row + cursor); cursor += 2; }
					else { delta = (sbyte)row[cursor]; cursor += 1; }
				}
				if (delta == 0) continue;
				int regionIndex = ttUSHORT(_fontData + regionIndexesOffset + column * 2);
				if (regionIndex >= _regionPeak.Length) continue;
				double scalar = OpenTypeVariationRegionScalar.Compute(instance, _regionStart[regionIndex], _regionPeak[regionIndex], _regionEnd[regionIndex], AxisCount);
				if (scalar == 0.0) continue;
				net += scalar * delta;
			}
			return net;
		}

		/// <summary>
		/// CFF2 'blend' support (README §8.2): the ACTIVE ItemVariationData (selected by vsindex) contributes only its
		/// regionIndexes — CFF2 stores the deltas in the charstring, not in the store (itemCount == 0). Fills
		/// <paramref name="scalars"/>[0..k) with the tent scalar of each listed region at the instance and returns k
		/// (the number of deltas each blended operand carries). Returns -1 for an out-of-range / NULL subtable or a
		/// buffer too small for k. Default coords ⇒ every scalar 0 (blend then yields the default operands).
		/// </summary>
		public int GetRegionScalars(int itemVariationDataIndex, in FontVariationNormalizedCoordinates instance, double[] scalars)
		{
			if (itemVariationDataIndex < 0 || itemVariationDataIndex >= _itemVariationDataOffsets.Length) return -1;
			int dataOffset = _itemVariationDataOffsets[itemVariationDataIndex];
			if (dataOffset == 0) return -1;
			var d = _fontData + dataOffset;
			int regionIndexCount = ttUSHORT(d + 4);
			if (scalars == null || scalars.Length < regionIndexCount) return -1;
			long regionIndexesEnd = dataOffset + 6L + regionIndexCount * 2L;
			if (regionIndexesEnd > _storeOffset + (long)_storeLength) return -1;
			for (int column = 0; column < regionIndexCount; column++)
			{
				int regionIndex = ttUSHORT(d + 6 + column * 2);
				scalars[column] = regionIndex < _regionPeak.Length && !instance.IsDefault
					? OpenTypeVariationRegionScalar.Compute(instance, _regionStart[regionIndex], _regionPeak[regionIndex], _regionEnd[regionIndex], AxisCount)
					: 0.0;
			}
			return regionIndexCount;
		}

		/// <summary>regionIndexCount of one ItemVariationData (k for CFF2 blend), or -1 when absent.</summary>
		public int GetRegionIndexCount(int itemVariationDataIndex)
		{
			if (itemVariationDataIndex < 0 || itemVariationDataIndex >= _itemVariationDataOffsets.Length) return -1;
			int dataOffset = _itemVariationDataOffsets[itemVariationDataIndex];
			return dataOffset == 0 ? -1 : ttUSHORT(_fontData + dataOffset + 4);
		}
	}

	/// <summary>
	/// DeltaSetIndexMap (otvarcommonformats "Associating Target Items to Variation Data"): maps an item index
	/// (glyph id for HVAR) to a packed (outer, inner) delta-set index. Format 0 (u16 count) and 1 (u32 count).
	/// Indices >= mapCount use the LAST entry (spec).
	/// </summary>
	public sealed class OpenTypeDeltaSetIndexMap
	{
		private readonly FakePtr<byte> _fontData;
		private readonly int _mapDataOffset;   // absolute
		private readonly int _mapCount;
		private readonly int _entrySize;       // 1..4 bytes
		private readonly int _innerIndexBitCount;

		public int MapCount => _mapCount;

		private OpenTypeDeltaSetIndexMap(FakePtr<byte> fontData, int mapDataOffset, int mapCount, int entrySize, int innerIndexBitCount)
		{
			_fontData = fontData;
			_mapDataOffset = mapDataOffset;
			_mapCount = mapCount;
			_entrySize = entrySize;
			_innerIndexBitCount = innerIndexBitCount;
		}

		public static bool TryParse(FakePtr<byte> fontData, int mapOffset, int availableLength, string parentTableTag,
			out OpenTypeDeltaSetIndexMap map, out FontVariationParseFailure failure)
		{
			map = null;
			if (availableLength < 4 || mapOffset < 0 || mapOffset + availableLength > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.DeltaSetIndexMapOutOfBounds, parentTableTag, "DeltaSetIndexMap header beyond table end");
				return false;
			}
			var m = fontData + mapOffset;
			byte format = m[0];
			byte entryFormat = m[1];
			int headerSize;
			long mapCount;
			if (format == 0) { mapCount = ttUSHORT(m + 2); headerSize = 4; }
			else if (format == 1) { if (availableLength < 6) { failure = new FontVariationParseFailure(FontVariationParseFailureCode.DeltaSetIndexMapOutOfBounds, parentTableTag, "DeltaSetIndexMap format 1 header truncated"); return false; } mapCount = ttULONG(m + 2); headerSize = 6; }
			else
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.DeltaSetIndexMapOutOfBounds, parentTableTag, "DeltaSetIndexMap format " + format);
				return false;
			}
			int innerIndexBitCount = (entryFormat & 0x0F) + 1;
			int entrySize = ((entryFormat & 0x30) >> 4) + 1;
			if (headerSize + mapCount * entrySize > availableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.DeltaSetIndexMapOutOfBounds, parentTableTag, "DeltaSetIndexMap mapData beyond table end");
				return false;
			}
			map = new OpenTypeDeltaSetIndexMap(fontData, mapOffset + headerSize, (int)mapCount, entrySize, innerIndexBitCount);
			failure = FontVariationParseFailure.None;
			return true;
		}

		public void Lookup(int itemIndex, out int outerIndex, out int innerIndex)
		{
			if (_mapCount == 0) { outerIndex = OpenTypeItemVariationStore.NoVariationIndex; innerIndex = OpenTypeItemVariationStore.NoVariationIndex; return; }
			if (itemIndex < 0) itemIndex = 0;
			if (itemIndex >= _mapCount) itemIndex = _mapCount - 1;
			var e = _fontData + _mapDataOffset + itemIndex * _entrySize;
			uint entry = 0;
			for (int i = 0; i < _entrySize; i++)
				entry = (entry << 8) | e[i];
			outerIndex = (int)(entry >> _innerIndexBitCount);
			innerIndex = (int)(entry & ((1u << _innerIndexBitCount) - 1));
		}
	}
}