using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp
{
	/// <summary>How a (base codepoint, variation selector) pair maps through cmap format 14 (UVS).</summary>
	public enum TrueTypeUvsMapping
	{
		/// <summary>No UVS entry: the selector is NOT consumed by cmap (GSUB may still ligate it).</summary>
		None = 0,
		/// <summary>Default UVS range: base keeps its ordinary cmap glyph; the selector is consumed.</summary>
		Default = 1,
		/// <summary>Non-default UVS mapping: base maps to the variation glyph; the selector is consumed.</summary>
		NonDefault = 2,
	}

	/// <summary>
	/// cmap format 14 (Unicode Variation Sequences) reader — M2 (plans/color_glyph_fonts.md §5.4). HarfBuzz resolves
	/// (base, VS) pairs through this subtable BEFORE GSUB and removes the consumed selector from the buffer; emoji
	/// fonts use it for text/emoji presentation (VS15/VS16). Located lazily; malformed subtables read as absent.
	/// </summary>
	public partial class FontInfo
	{
		private int _uvsSubtableOffset = -1;   // -1 = not resolved, 0 = absent, >0 = absolute offset of the format-14 subtable

		/// <summary>UVS lookup for (base codepoint, variation selector). glyphIndex is meaningful only for NonDefault.</summary>
		public TrueTypeUvsMapping stbtt_GetVariationSequenceGlyph(int baseCodepoint, int variationSelector, out int glyphIndex)
		{
			glyphIndex = 0;
			int uvs = stbtt__GetUvsSubtableOffset();
			if (uvs == 0) return TrueTypeUvsMapping.None;
			var t = this.data + uvs;
			long recordCount = ttULONG(t + 6);
			if (10 + recordCount * 11 > t.RemainingLength) return TrueTypeUvsMapping.None;
			// VariationSelectorRecords: varSelector uint24, defaultUVSOffset u32, nonDefaultUVSOffset u32 — sorted; few in practice.
			for (long r = 0; r < recordCount; r++)
			{
				var rec = t + (int)(10 + r * 11);
				int varSelector = (rec[0] << 16) | (rec[1] << 8) | rec[2];
				if (varSelector < variationSelector) continue;
				if (varSelector > variationSelector) break;
				long nonDefaultOffset = ttULONG(rec + 7);
				if (nonDefaultOffset != 0 && nonDefaultOffset + 4 <= t.RemainingLength)
				{
					var nd = t + (int)nonDefaultOffset;
					long mappingCount = ttULONG(nd);
					if (4 + mappingCount * 5 <= nd.RemainingLength)
					{
						for (long m = 0; m < mappingCount; m++)
						{
							var map = nd + (int)(4 + m * 5);
							int unicodeValue = (map[0] << 16) | (map[1] << 8) | map[2];
							if (unicodeValue < baseCodepoint) continue;
							if (unicodeValue > baseCodepoint) break;
							glyphIndex = ttUSHORT(map + 3);
							return glyphIndex != 0 ? TrueTypeUvsMapping.NonDefault : TrueTypeUvsMapping.None;
						}
					}
				}
				long defaultOffset = ttULONG(rec + 3);
				if (defaultOffset != 0 && defaultOffset + 4 <= t.RemainingLength)
				{
					var d = t + (int)defaultOffset;
					long rangeCount = ttULONG(d);
					if (4 + rangeCount * 4 <= d.RemainingLength)
					{
						for (long g = 0; g < rangeCount; g++)
						{
							var range = d + (int)(4 + g * 4);
							int start = (range[0] << 16) | (range[1] << 8) | range[2];
							int end = start + range[3];
							if (baseCodepoint < start) break;
							if (baseCodepoint <= end) return TrueTypeUvsMapping.Default;
						}
					}
				}
				break;
			}
			return TrueTypeUvsMapping.None;
		}

		private int stbtt__GetUvsSubtableOffset()
		{
			if (_uvsSubtableOffset >= 0) return _uvsSubtableOffset;
			_uvsSubtableOffset = 0;
			uint cmap = stbtt__find_table(this.data, (uint)this.fontstart, "cmap");
			if (cmap == 0) return 0;
			var ptr = this.data;
			int numTables = ttUSHORT(ptr + (int)cmap + 2);
			for (int i = 0; i < numTables; i++)
			{
				int encodingRecord = (int)(cmap + 4 + 8 * i);
				int platformId = ttUSHORT(ptr + encodingRecord);
				int encodingId = ttUSHORT(ptr + encodingRecord + 2);
				if (platformId != STBTT_PLATFORM_ID_UNICODE || encodingId != 5) continue; // Unicode Variation Sequences
				int subtable = (int)(cmap + ttULONG(ptr + encodingRecord + 4));
				if ((ptr + subtable).RemainingLength >= 10 && ttUSHORT(ptr + subtable) == 14)
					_uvsSubtableOffset = subtable;
				break;
			}
			return _uvsSubtableOffset;
		}
	}
}