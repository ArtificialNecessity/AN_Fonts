using System;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp
{
	public partial class FontInfo
	{
		public Buf cff = null;
		public Buf charstrings = null;
		public FakePtr<byte> data;
		public Buf fdselect = null;
		public Buf fontdicts = null;
		public int fontstart;
		public int glyf;
		public int gpos;
		public Buf gsubrs = null;
		public int head;
		public int hhea;
		public int hmtx;
		/// <summary>Absolute offset of the cmap subtable selected by <see cref="stbtt__InitFont_finish"/> (0 = none).</summary>
		public int index_map;
		/// <summary>What the selected cmap subtable is keyed by; <see cref="stbtt_FindGlyphIndex"/> translates Unicode to it.</summary>
		public CmapSubtableKeyKind cmapSubtableKeyKind;

		/// <summary>
		/// Key space of the cmap subtable at <see cref="index_map"/> (plans/BUGFIX_cmap_subtable_selection_mac_platform.md §3.1).
		/// </summary>
		public enum CmapSubtableKeyKind : byte
		{
			/// <summary>(0,x) (2,x) (3,1) (3,10): the Unicode scalar is the key (identity).</summary>
			Unicode = 0,
			/// <summary>
			/// (3,0) Microsoft Symbol: codes live at U+F020..U+F0FF. Lookup tries the scalar as given, then
			/// <c>0xF000 | scalar</c> for scalars ≤ 0xFF.
			/// </summary>
			MicrosoftSymbol = 1,
			/// <summary>
			/// (1,0) Macintosh Roman: the key is the Mac OS Roman BYTE (<see cref="MacRomanEncoding"/>); codepoints with
			/// no Mac Roman byte have no glyph.
			/// </summary>
			MacRoman = 2,
		}
		public int indexToLocFormat;
		public int kern;
		public int loca;
		public int numGlyphs;
		public Buf subrs = null;
		public int svg;
		// Vertical metrics tables (0 = absent).
		public int vhea;
		public int vmtx;
		// Colour bitmap strike tables (0 = absent) — plans/color_glyph_fonts.md §3.4.
		public int cblc;
		public int cbdt;
		public int sbix;
		/// <summary>Which outline source (if any) this face carries. <see cref="TrueTypeOutlineFormat.None"/> is a
		/// VALID loaded state for colour-bitmap-only fonts: every outline query then reports an empty glyph.</summary>
		public TrueTypeOutlineFormat outlineFormat;

		/// <summary>True when the face has vector outlines (glyf, 'CFF ' or CFF2).</summary>
		public bool HasOutlines => this.outlineFormat != TrueTypeOutlineFormat.None;

		/// <summary>True when CBLC+CBDT (both required) or sbix is present — the acceptance condition for outline-less fonts.</summary>
		public bool HasColorBitmapStrikeTables => (this.cblc != 0 && this.cbdt != 0) || this.sbix != 0;

		/// <summary>Outline source of a loaded face.</summary>
		public enum TrueTypeOutlineFormat : byte
		{
			/// <summary>No glyf/loca, 'CFF ' or CFF2 — accepted only with colour bitmap strike tables.</summary>
			None = 0,
			/// <summary>TrueType quadratic outlines (glyf + loca).</summary>
			TrueTypeGlyf = 1,
			/// <summary>'CFF ' Type 2 charstrings.</summary>
			Cff = 2,
			/// <summary>CFF2 charstrings (variable-capable).</summary>
			Cff2 = 3,
		}

		public int stbtt__get_svg()
		{
			uint t = 0;
			if (this.svg < 0)
			{
				t = stbtt__find_table(this.data, (uint)this.fontstart, "SVG ");
				if (t != 0)
				{
					var offset = ttULONG(this.data + t + 2);
					this.svg = (int)(t + offset);
				}
				else
				{
					this.svg = 0;
				}
			}

			return this.svg;
		}

		public int stbtt_InitFont_internal(byte[] data, int fontstart)
		{
			uint cmap = 0;
			var ptr = new FakePtr<byte>(data);
			this.data = ptr;
			this.fontstart = fontstart;
			this.cff = new Buf(FakePtr<byte>.Null, 0);
			this.isCff2 = false;
			this.cff2VariationStoreOffset = 0;
			cmap = stbtt__find_table(ptr, (uint)fontstart, "cmap");
			this.loca = (int)stbtt__find_table(ptr, (uint)fontstart, "loca");
			this.head = (int)stbtt__find_table(ptr, (uint)fontstart, "head");
			this.glyf = (int)stbtt__find_table(ptr, (uint)fontstart, "glyf");
			this.hhea = (int)stbtt__find_table(ptr, (uint)fontstart, "hhea");
			this.hmtx = (int)stbtt__find_table(ptr, (uint)fontstart, "hmtx");
			this.kern = (int)stbtt__find_table(ptr, (uint)fontstart, "kern");
			this.gpos = (int)stbtt__find_table(ptr, (uint)fontstart, "GPOS");
			// GSUB (FontInfoGsub.cs): 'rvrn' + FeatureVariations glyph selection (Part D). Located here, parsed lazily.
			this.gsub = (int)stbtt__find_table(ptr, (uint)fontstart, "GSUB");
			// OpenType Font Variations (FontInfoVariations.cs): located here, parsed lazily on first use.
			this.fvar = (int)stbtt__find_table(ptr, (uint)fontstart, "fvar");
			// GDEF: only its v1.3 ItemVariationStore is used (GPOS VariationIndex deltas, Part C).
			this.gdef = (int)stbtt__find_table(ptr, (uint)fontstart, "GDEF");
			this.avar = (int)stbtt__find_table(ptr, (uint)fontstart, "avar");
			this.gvar = (int)stbtt__find_table(ptr, (uint)fontstart, "gvar");
			this.hvar = (int)stbtt__find_table(ptr, (uint)fontstart, "HVAR");
			this.mvar = (int)stbtt__find_table(ptr, (uint)fontstart, "MVAR");
			this._variationTablesResolved = false;
			this._variationTables = null;
			// Vertical metrics (vhea/vmtx) — located here, read on demand (stbtt_GetFontVerticalMetrics / stbtt_GetGlyphVMetrics).
			this.vhea = (int)stbtt__find_table(ptr, (uint)fontstart, "vhea");
			this.vmtx = (int)stbtt__find_table(ptr, (uint)fontstart, "vmtx");
			// Colour bitmap strike tables (plans/color_glyph_fonts.md §3.4). Located here so an OUTLINE-LESS colour
			// font (Noto Color Emoji CBDT build: no glyf/loca/CFF) is a valid font; parsed by ColorBitmap/ on demand.
			this.cblc = (int)stbtt__find_table(ptr, (uint)fontstart, "CBLC");
			this.cbdt = (int)stbtt__find_table(ptr, (uint)fontstart, "CBDT");
			this.sbix = (int)stbtt__find_table(ptr, (uint)fontstart, "sbix");
			this.outlineFormat = TrueTypeOutlineFormat.None;
			if (cmap == 0 || this.head == 0 || this.hhea == 0 || this.hmtx == 0)
				return 0;
			if (this.glyf != 0)
			{
				if (this.loca == 0)
					return 0;
				this.outlineFormat = TrueTypeOutlineFormat.TrueTypeGlyf;
			}
			else if (stbtt__find_table(ptr, (uint)fontstart, "CFF ") == 0
			         && stbtt__find_table(ptr, (uint)fontstart, "CFF2") == 0)
			{
				// No outline table at all. Accept ONLY when a colour bitmap strike table can supply the glyphs
				// (OpenType 1.9.1 CBLC/CBDT or sbix); otherwise this is not a renderable font.
				if (!HasColorBitmapStrikeTables)
					return 0;
				this.loca = 0;                 // defensive: stbtt__GetGlyfOffset guards on glyf == 0 || loca == 0
				return stbtt__InitFont_finish(ptr, cmap);
			}
			else
			{
				Buf b = null;
				Buf topdict = null;
				Buf topdictidx = null;
				var cstype = (uint)2;
				var charstrings = (uint)0;
				var fdarrayoff = (uint)0;
				var fdselectoff = (uint)0;
				uint cff = 0;
				cff = stbtt__find_table(ptr, (uint)fontstart, "CFF ");
				if (cff == 0)
				{
					// Part B: CFF2 outlines (variable or static) — README §8.1. Separate reader because the header,
					// TopDICT keys and INDEX count width all differ from 'CFF '.
					return stbtt__InitCff2(ptr, cmap) ? stbtt__InitFont_finish(ptr, cmap) : 0;
				}
				if (cff == 0)
					return 0;
				this.fontdicts = new Buf(FakePtr<byte>.Null, 0);
				this.fdselect = new Buf(FakePtr<byte>.Null, 0);
				this.cff = new Buf(new FakePtr<byte>(ptr, (int)cff), 512 * 1024 * 1024);
				b = this.cff.Clone();
				b.stbtt__buf_skip(2);
				b.stbtt__buf_seek(b.stbtt__buf_get8());
				b.stbtt__cff_get_index();
				topdictidx = b.stbtt__cff_get_index();
				topdict = topdictidx.stbtt__cff_index_get(0);
				b.stbtt__cff_get_index();
				this.gsubrs = b.stbtt__cff_get_index();
				topdict.stbtt__dict_get_ints(17, out charstrings);
				topdict.stbtt__dict_get_ints_default(0x100 | 6, ref cstype);
				topdict.stbtt__dict_get_ints_default(0x100 | 36, ref fdarrayoff);
				topdict.stbtt__dict_get_ints_default(0x100 | 37, ref fdselectoff);
				this.subrs = Buf.stbtt__get_subrs(b.Clone(), topdict);

				if (cstype != 2)
					return 0;
				if (charstrings == 0)
					return 0;
				if (fdarrayoff != 0)
				{
					if (fdselectoff == 0)
						return 0;
					b.stbtt__buf_seek((int)fdarrayoff);
					this.fontdicts = b.stbtt__cff_get_index();
					this.fdselect = b.stbtt__buf_range((int)fdselectoff, (int)(b.size - fdselectoff));
				}

				b.stbtt__buf_seek((int)charstrings);
				this.charstrings = b.stbtt__cff_get_index();
				this.outlineFormat = TrueTypeOutlineFormat.Cff;
			}

			return stbtt__InitFont_finish(ptr, cmap);
		}

		/// <summary>True when the outlines come from a CFF2 table (Part B): CFF2 INDEX counts are uint32, charstrings
		/// carry no width and no endchar, and 'blend'/'vsindex' may appear (README §8.1). Every <c>cff.size != 0</c>
		/// branch still treats the font as CFF-outlined; this flag selects the CFF2 deviations inside those paths.</summary>
		public bool isCff2;
		/// <summary>Absolute offset (in <c>data</c>) of the CFF2 VariationStore (TopDICT key 24): <c>uint16 length</c> then
		/// an ItemVariationStore. 0 when the CFF2 table has no variations. Parsed lazily with the fvar tables.</summary>
		public int cff2VariationStoreOffset;

		/// <summary>
		/// CFF2 table reader (OpenType CFF2 chapter; README §8.1/§8.3 step 1). Header: major(1) minor(1) headerSize(1)
		/// topDICTSize(2); TopDICT at headerSize; GlobalSubrINDEX at headerSize + topDICTSize. TopDICT keys:
		/// CharStringINDEXOffset(17), VariationStoreOffset(24), FontDICTINDEXOffset(12 36), FontDICTSelectOffset(12 37).
		/// Every FontDICT's PrivateDICTOffset(18) is 'size offset' from the CFF2 start, exactly like 'CFF ' — so
		/// Buf.stbtt__get_subrs and stbtt__cid_get_glyph_subrs are reused as-is (with the uint32 INDEX counts).
		/// Populates the same fields the 'CFF ' path does (cff, gsubrs, charstrings, fontdicts, fdselect, subrs).
		/// </summary>
		private bool stbtt__InitCff2(FakePtr<byte> ptr, uint cmap)
		{
			uint cff2 = stbtt__find_table(ptr, (uint)fontstart, "CFF2");
			if (cff2 == 0)
				return false;
			int cff2Length = stbtt__find_table_length("CFF2");
			if (cff2Length < 5)
				return false;
			this.isCff2 = true;
			this.fontdicts = new Buf(FakePtr<byte>.Null, 0).AsCff2();
			this.fdselect = new Buf(FakePtr<byte>.Null, 0).AsCff2();
			this.cff = new Buf(new FakePtr<byte>(ptr, (int)cff2), (ulong)cff2Length).AsCff2();
			Buf b = this.cff.Clone();
			int majorVersion = b.stbtt__buf_get8();
			b.stbtt__buf_get8(); // minorVersion
			int headerSize = b.stbtt__buf_get8();
			int topDictSize = (int)b.stbtt__buf_get(2);
			if (majorVersion != 2 || headerSize < 5 || headerSize + topDictSize > cff2Length)
				return false;
			Buf topdict = b.stbtt__buf_range(headerSize, topDictSize);
			b.stbtt__buf_seek(headerSize + topDictSize);
			this.gsubrs = b.stbtt__cff_get_index();

			uint charstringsOffset = 0, variationStoreOffset = 0, fdArrayOffset = 0, fdSelectOffset = 0;
			topdict.stbtt__dict_get_ints_default(17, ref charstringsOffset);
			topdict.stbtt__dict_get_ints_default(24, ref variationStoreOffset);
			topdict.stbtt__dict_get_ints_default(0x100 | 36, ref fdArrayOffset);
			topdict.stbtt__dict_get_ints_default(0x100 | 37, ref fdSelectOffset);
			if (charstringsOffset == 0 || charstringsOffset >= (uint)cff2Length || fdArrayOffset == 0 || fdArrayOffset >= (uint)cff2Length)
				return false;

			b.stbtt__buf_seek((int)fdArrayOffset);
			this.fontdicts = b.stbtt__cff_get_index();
			if (this.fontdicts.stbtt__cff_index_count() < 1)
				return false;
			if (fdSelectOffset != 0 && fdSelectOffset < (uint)cff2Length)
				this.fdselect = b.stbtt__buf_range((int)fdSelectOffset, (int)(cff2Length - fdSelectOffset));
			// FontDICT#0's local subroutines are the default 'subrs' (the 'CFF ' path uses the TopDICT Private; CFF2 has
			// no TopDICT Private). Glyphs mapped by FontDICTSelect to another FD re-resolve via stbtt__cid_get_glyph_subrs.
			this.subrs = Buf.stbtt__get_subrs(this.cff.Clone(), this.fontdicts.stbtt__cff_index_get(0));
			b.stbtt__buf_seek((int)charstringsOffset);
			this.charstrings = b.stbtt__cff_get_index();
			this.outlineFormat = TrueTypeOutlineFormat.Cff2;
			this.cff2VariationStoreOffset = variationStoreOffset != 0 && variationStoreOffset + 2 < (uint)cff2Length
				? (int)(cff2 + variationStoreOffset) : 0;
			return true;
		}

		/// <summary>Tail of stbtt_InitFont_internal shared by the glyf, 'CFF ' and CFF2 paths: maxp, cmap encoding
		/// record selection, indexToLocFormat.</summary>
		private int stbtt__InitFont_finish(FakePtr<byte> ptr, uint cmap)
		{
			int i, numTables;
			uint t = stbtt__find_table(ptr, (uint)fontstart, "maxp");
			if (t != 0)
				this.numGlyphs = ttUSHORT(ptr + t + 4);
			else
				this.numGlyphs = 0xffff;
			this.svg = -1;
			numTables = ttUSHORT(ptr + cmap + 2);
			this.index_map = 0;
			this.cmapSubtableKeyKind = CmapSubtableKeyKind.Unicode;
			int bestRank = 0;
			for (i = 0; i < numTables; ++i)
			{
				var encoding_record = (uint)(cmap + 4 + 8 * i);
				if ((ptr + encoding_record).RemainingLength < 8)
					break;
				int platformId = ttUSHORT(ptr + encoding_record);
				int encodingId = ttUSHORT(ptr + encoding_record + 2);
				int rank = stbtt__RankCmapEncodingRecord(platformId, encodingId, out CmapSubtableKeyKind keyKind);
				// Ties: first record wins — records are spec-sorted by (platformID, encodingID), so the lower
				// encodingID is the one already held.
				if (rank <= bestRank)
					continue;
				var subtableOffset = cmap + ttULONG(ptr + encoding_record + 4);
				if (!stbtt__IsReadableCmapSubtable(ptr, subtableOffset))
					continue;
				bestRank = rank;
				this.index_map = (int)subtableOffset;
				this.cmapSubtableKeyKind = keyKind;
			}

			if (this.index_map == 0)
				return 0;
			this.indexToLocFormat = ttUSHORT(ptr + this.head + 50);
			return 1;
		}

		/// <summary>
		/// Preference order for cmap encoding records (plans/BUGFIX_cmap_subtable_selection_mac_platform.md §3.6).
		/// Higher wins; 0 = cannot be used as the primary subtable (CJK code pages, Mac non-Roman scripts, custom
		/// platform, (0,5) UVS supplement — see plans/BUGFIX_cmap_remaining_gaps.md).
		/// </summary>
		private static int stbtt__RankCmapEncodingRecord(int platformId, int encodingId, out CmapSubtableKeyKind keyKind)
		{
			keyKind = CmapSubtableKeyKind.Unicode;
			switch (platformId)
			{
				case STBTT_PLATFORM_ID_MICROSOFT:
					switch (encodingId)
					{
						case STBTT_MS_EID_UNICODE_FULL: return 7;
						case STBTT_MS_EID_UNICODE_BMP: return 5;
						case STBTT_MS_EID_SYMBOL:
							keyKind = CmapSubtableKeyKind.MicrosoftSymbol;
							return 3;
						default: return 0; // ShiftJIS / PRC / Big5 / Wansung / Johab
					}
				case STBTT_PLATFORM_ID_UNICODE:
					switch (encodingId)
					{
						case STBTT_UNICODE_EID_UNICODE_2_0_FULL: return 6;
						case STBTT_UNICODE_EID_UNICODE_FULL_FORMAT13: return 6;
						case STBTT_UNICODE_EID_UNICODE_1_0:
						case STBTT_UNICODE_EID_UNICODE_1_1:
						case STBTT_UNICODE_EID_ISO_10646:
						case STBTT_UNICODE_EID_UNICODE_2_0_BMP: return 4;
						default: return 0; // (0,5) format 14 is a supplement, never the primary table
					}
				case STBTT_PLATFORM_ID_MAC:
					if (encodingId != STBTT_MAC_EID_ROMAN)
						return 0;
					keyKind = CmapSubtableKeyKind.MacRoman;
					return 2;
				case STBTT_PLATFORM_ID_ISO:
					// Deprecated platform; 7-bit ASCII, ISO 10646 and ISO 8859-1 are all Unicode prefixes → identity key.
					return encodingId <= 2 ? 1 : 0;
				default:
					return 0;
			}
		}

		/// <summary>
		/// True when the subtable header is inside the file and its format has a reader in
		/// <see cref="stbtt__LookupCmapSubtable"/> (0, 4, 6, 8, 10, 12, 13). Keeps a (0,4) format 14 or a format 2
		/// record from out-ranking a readable one.
		/// </summary>
		private static bool stbtt__IsReadableCmapSubtable(FakePtr<byte> ptr, uint subtableOffset)
		{
			if ((ptr + subtableOffset).RemainingLength < 4)
				return false;
			switch (ttUSHORT(ptr + subtableOffset))
			{
				case 0:
				case 4:
				case 6:
				case 8:
				case 10:
				case 12:
				case 13:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Unicode scalar → glyph ID through the selected cmap subtable. The subtable chosen by
		/// <see cref="stbtt__InitFont_finish"/> may be keyed by something other than Unicode
		/// (<see cref="cmapSubtableKeyKind"/>); the key is translated here before the format readers run
		/// (plans/BUGFIX_cmap_subtable_selection_mac_platform.md §3.2). 0 = no glyph.
		/// </summary>
		public int stbtt_FindGlyphIndex(int unicode_codepoint)
		{
			switch (this.cmapSubtableKeyKind)
			{
				case CmapSubtableKeyKind.MacRoman:
				{
					// (1,0) subtables are keyed by Mac OS Roman BYTES. ASCII is identity; 0x80+ goes through the table.
					int macRomanByte = MacRomanEncoding.UnicodeToMacRomanByte(unicode_codepoint);
					if (macRomanByte < 0)
						return 0;
					return stbtt__LookupCmapSubtable(macRomanByte);
				}
				case CmapSubtableKeyKind.MicrosoftSymbol:
				{
					// (3,0) symbol fonts encode 0x20..0xFF at U+F020..U+F0FF (OpenType "Non-Standard (Symbol) Fonts";
					// what Windows and HarfBuzz do). Direct lookup first so U+F0xx itself also resolves; only the
					// spec-sanctioned 0xF000 duplicate is tried, never F1xx/F2xx vendor ranges.
					int direct = stbtt__LookupCmapSubtable(unicode_codepoint);
					if (direct != 0 || (uint)unicode_codepoint > 0xFF)
						return direct;
					return stbtt__LookupCmapSubtable(0xF000 | unicode_codepoint);
				}
				default:
					return stbtt__LookupCmapSubtable(unicode_codepoint);
			}
		}

		/// <summary>Reads the selected cmap subtable with an already-translated key. Formats 0, 4, 6, 8, 10, 12, 13.</summary>
		private int stbtt__LookupCmapSubtable(int unicode_codepoint)
		{
			var data = this.data;
			var index_map = (uint)this.index_map;
			var format = ttUSHORT(data + index_map + 0);
			if (format == 0)
			{
				var bytes = (int)ttUSHORT(data + index_map + 2);
				if (unicode_codepoint < bytes - 6)
					return data[index_map + 6 + unicode_codepoint];
				return 0;
			}

			if (format == 6)
			{
				var first = (uint)ttUSHORT(data + index_map + 6);
				var count = (uint)ttUSHORT(data + index_map + 8);
				if ((uint)unicode_codepoint >= first && (uint)unicode_codepoint < first + count)
					return ttUSHORT(data + index_map + 10 + (unicode_codepoint - first) * 2);
				return 0;
			}

			if (format == 2)
				return 0;

			if (format == 4)
			{
				var segcount = (ushort)(ttUSHORT(data + index_map + 6) >> 1);
				var searchRange = (ushort)(ttUSHORT(data + index_map + 8) >> 1);
				var entrySelector = ttUSHORT(data + index_map + 10);
				var rangeShift = (ushort)(ttUSHORT(data + index_map + 12) >> 1);
				var endCount = index_map + 14;
				var search = endCount;
				if (unicode_codepoint > 0xffff)
					return 0;
				if (unicode_codepoint >= ttUSHORT(data + search + rangeShift * 2))
					search += (uint)(rangeShift * 2);
				search -= 2;
				while (entrySelector != 0)
				{
					ushort end = 0;
					searchRange >>= 1;
					end = ttUSHORT(data + search + searchRange * 2);
					if (unicode_codepoint > end)
						search += (uint)(searchRange * 2);
					--entrySelector;
				}

				search += 2;
				{
					ushort offset = 0;
					ushort start = 0;
					var item = (ushort)((search - endCount) >> 1);
					start = ttUSHORT(data + index_map + 14 + segcount * 2 + 2 + 2 * item);
					if (unicode_codepoint < start)
						return 0;
					offset = ttUSHORT(data + index_map + 14 + segcount * 6 + 2 + 2 * item);
					if (offset == 0)
						return (ushort)(unicode_codepoint +
										 ttSHORT(data + index_map + 14 + segcount * 4 + 2 + 2 * item));
					return ttUSHORT(data + offset + (unicode_codepoint - start) * 2 + index_map + 14 + segcount * 6 +
									2 + 2 * item);
				}
			}

			if (format == 10)
			{
				// Trimmed array, 32-bit (OpenType cmap "Format 10"): format 6 with u32 startCharCode @12, u32 numChars @16,
				// u16 glyphIdArray[] @20. _EXTERNAL_APIS/OpenType/cmap.md.
				var firstCharCode = ttULONG(data + index_map + 12);
				var numChars = ttULONG(data + index_map + 16);
				if ((uint)unicode_codepoint >= firstCharCode && (uint)unicode_codepoint - firstCharCode < numChars)
					return ttUSHORT(data + index_map + 20 + ((uint)unicode_codepoint - firstCharCode) * 2);
				return 0;
			}

			if (format == 12 || format == 13 || format == 8)
			{
				// Formats 12/13: u32 numGroups @12, groups @16. Format 8 (mixed 16/32, deprecated): u8 is32[8192] @12,
				// u32 numGroups @8204, groups @8208 — same SequentialMapGroup record. The is32 bitmap only matters when
				// tokenising a UTF-16 stream; we always have the full scalar value, so a group either covers it or not.
				uint groupsBase = format == 8 ? index_map + 8208u : index_map + 16u;
				var ngroups = ttULONG(data + (format == 8 ? index_map + 8204u : index_map + 12u));
				var low = 0;
				var high = 0;
				low = 0;
				high = (int)ngroups;
				while (low < high)
				{
					var mid = low + ((high - low) >> 1);
					var start_char = ttULONG(data + groupsBase + mid * 12);
					var end_char = ttULONG(data + groupsBase + mid * 12 + 4);
					if ((uint)unicode_codepoint < start_char)
					{
						high = mid;
					}
					else if ((uint)unicode_codepoint > end_char)
					{
						low = mid + 1;
					}
					else
					{
						var start_glyph = ttULONG(data + groupsBase + mid * 12 + 8);
						if (format != 13)
							return (int)(start_glyph + unicode_codepoint - start_char);
						return (int)start_glyph;
					}
				}

				return 0;
			}

			return 0;
		}

		public int stbtt_GetCodepointShape(int unicode_codepoint, out stbtt_vertex[] vertices)
		{
			return stbtt_GetGlyphShape(stbtt_FindGlyphIndex(unicode_codepoint), out vertices);
		}

		public int stbtt__GetGlyfOffset(int glyph_index)
		{
			var g1 = 0;
			var g2 = 0;
			// Outline-less fonts (colour bitmap strikes only — plans/color_glyph_fonts.md §3.4): no glyf/loca to
			// index. Every TrueType outline entry point funnels through here, so this single guard makes them all
			// report "empty glyph" instead of reading table offset 0.
			if (this.glyf == 0 || this.loca == 0)
				return -1;
			if (glyph_index >= this.numGlyphs)
				return -1;
			if (this.indexToLocFormat >= 2)
				return -1;
			if (this.indexToLocFormat == 0)
			{
				g1 = this.glyf + ttUSHORT(this.data + this.loca + glyph_index * 2) * 2;
				g2 = this.glyf + ttUSHORT(this.data + this.loca + glyph_index * 2 + 2) * 2;
			}
			else
			{
				g1 = (int)(this.glyf + ttULONG(this.data + this.loca + glyph_index * 4));
				g2 = (int)(this.glyf + ttULONG(this.data + this.loca + glyph_index * 4 + 4));
			}

			return g1 == g2 ? -1 : g1;
		}

		public int stbtt_GetGlyphBox(int glyph_index, ref int x0, ref int y0, ref int x1,
			ref int y1)
		{
			if (this.cff.size != 0)
			{
				// An EMPTY charstring (space; CFF2 zero-length or 'CFF ' bare endchar) has no box — report 0 like the glyf path.
				if (stbtt__GetGlyphInfoT2(glyph_index, ref x0, ref y0, ref x1, ref y1) == 0)
					return 0;
			}
			else
			{
				var g = stbtt__GetGlyfOffset(glyph_index);
				if (g < 0)
					return 0;
				x0 = ttSHORT(this.data + g + 2);
				y0 = ttSHORT(this.data + g + 4);
				x1 = ttSHORT(this.data + g + 6);
				y1 = ttSHORT(this.data + g + 8);
			}

			return 1;
		}

		public int stbtt_GetCodepointBox(int codepoint, ref int x0, ref int y0, ref int x1,
			ref int y1)
		{
			return stbtt_GetGlyphBox(stbtt_FindGlyphIndex(codepoint), ref x0, ref y0, ref x1, ref y1);
		}

		public int stbtt_IsGlyphEmpty(int glyph_index)
		{
			short numberOfContours = 0;
			var g = 0;

			int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
			if (this.cff.size != 0)
				return stbtt__GetGlyphInfoT2(glyph_index, ref x0, ref y0, ref x1, ref y1) == 0 ? 1 : 0;
			g = stbtt__GetGlyfOffset(glyph_index);
			if (g < 0)
				return 1;
			numberOfContours = ttSHORT(this.data + g);
			return numberOfContours == 0 ? 1 : 0;
		}

		public int stbtt__GetGlyphShapeTT(int glyph_index, out stbtt_vertex[] pvertices)
			=> stbtt__GetGlyphShapeTT(glyph_index, Variations.FontVariationNormalizedCoordinates.Default, out pvertices);

		/// <summary>
		/// TrueType outline at a variation instance. Default coords = the classic un-varied path (this IS the
		/// original function body; variation enters only at two marked seams).
		/// </summary>
		public int stbtt__GetGlyphShapeTT(int glyph_index, in Variations.FontVariationNormalizedCoordinates coords, out stbtt_vertex[] pvertices)
		{
			short numberOfContours = 0;
			FakePtr<byte> endPtsOfContours;
			var data = this.data;
			stbtt_vertex[] vertices = null;
			var num_vertices = 0;
			var g = stbtt__GetGlyfOffset(glyph_index);
			pvertices = null;
			if (g < 0)
				return 0;
			numberOfContours = ttSHORT(data + g);
			if (numberOfContours > 0)
			{
				var flags = (byte)0;
				byte flagcount = 0;
				var ins = 0;
				var i = 0;
				var j = 0;
				var m = 0;
				var n = 0;
				var next_move = 0;
				var was_off = 0;
				var off = 0;
				var start_off = 0;
				var x = 0;
				var y = 0;
				var cx = 0;
				var cy = 0;
				var sx = 0;
				var sy = 0;
				var scx = 0;
				var scy = 0;
				FakePtr<byte> points;
				endPtsOfContours = data + g + 10;
				ins = ttUSHORT(data + g + 10 + numberOfContours * 2);
				points = data + g + 10 + numberOfContours * 2 + 2 + ins;
				n = 1 + ttUSHORT(endPtsOfContours + numberOfContours * 2 - 2);
				m = n + 2 * numberOfContours;
				vertices = new stbtt_vertex[m];
				next_move = 0;
				flagcount = 0;
				off = m - n;
				for (i = 0; i < n; ++i)
				{
					if (flagcount == 0)
					{
						flags = points.GetAndIncrease();
						if ((flags & 8) != 0)
							flagcount = points.GetAndIncrease();
					}
					else
					{
						--flagcount;
					}

					vertices[off + i].type = flags;
				}

				x = 0;
				for (i = 0; i < n; ++i)
				{
					flags = vertices[off + i].type;
					if ((flags & 2) != 0)
					{
						var dx = (short)points.GetAndIncrease();
						x += (flags & 16) != 0 ? dx : -dx;
					}
					else
					{
						if ((flags & 16) == 0)
						{
							x = x + (short)(points[0] * 256 + points[1]);
							points += 2;
						}
					}

					vertices[off + i].x = (short)x;
				}

				y = 0;
				for (i = 0; i < n; ++i)
				{
					flags = vertices[off + i].type;
					if ((flags & 4) != 0)
					{
						var dy = (short)points.GetAndIncrease();
						y += (flags & 32) != 0 ? dy : -dy;
					}
					else
					{
						if ((flags & 32) == 0)
						{
							y = y + (short)(points[0] * 256 + points[1]);
							points += 2;
						}
					}

					vertices[off + i].y = (short)y;
				}

				// VARIATION seam: the point arrays are complete; apply gvar deltas (+ IUP) to them BEFORE the
				// on/off-curve emission below, so the flag/contour logic never knows variation exists.
				if (!coords.IsDefault)
					stbtt__ApplyGlyphVariationToSimpleGlyphPoints(glyph_index, coords, vertices, off, n, endPtsOfContours, numberOfContours, g);

				num_vertices = 0;
				sx = sy = cx = cy = scx = scy = 0;
				for (i = 0; i < n; ++i)
				{
					flags = vertices[off + i].type;
					x = vertices[off + i].x;
					y = vertices[off + i].y;
					if (next_move == i)
					{
						if (i != 0)
							num_vertices = stbtt__close_shape(vertices, num_vertices, was_off, start_off, sx, sy, scx,
								scy, cx, cy);
						start_off = (flags & 1) != 0 ? 0 : 1;
						if (start_off != 0)
						{
							scx = x;
							scy = y;
							if (i + 1 >= n)
							{
								// SAFE-PORT FIX (_BUGFIX 380): degenerate contour whose FIRST point
								// is off-curve and is also the LAST point of the glyph (e.g. the
								// 1-point hint-carrier glyphs Consolas uses for U+202F/U+034F/
								// U+FEFF/U+205F). Upstream stb reads vertices[off+i+1] here, one
								// past the end (silent overread in C, IndexOutOfRangeException in
								// C#). Treat the lone off-curve point as a degenerate on-curve
								// start; the contour emits no ink either way.
								start_off = 0;
								sx = x;
								sy = y;
							}
							else if ((vertices[off + i + 1].type & 1) == 0)
							{
								sx = (x + vertices[off + i + 1].x) >> 1;
								sy = (y + vertices[off + i + 1].y) >> 1;
							}
							else
							{
								sx = vertices[off + i + 1].x;
								sy = vertices[off + i + 1].y;
								++i;
							}
						}
						else
						{
							sx = x;
							sy = y;
						}

						stbtt_setvertex(ref vertices[num_vertices++], STBTT_vmove, sx, sy, 0, 0);
						was_off = 0;
						next_move = 1 + ttUSHORT(endPtsOfContours + j * 2);
						++j;
					}
					else
					{
						if ((flags & 1) == 0)
						{
							if (was_off != 0)
								stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, (cx + x) >> 1,
									(cy + y) >> 1, cx, cy);
							cx = x;
							cy = y;
							was_off = 1;
						}
						else
						{
							if (was_off != 0)
								stbtt_setvertex(ref vertices[num_vertices++], STBTT_vcurve, x, y, cx, cy);
							else
								stbtt_setvertex(ref vertices[num_vertices++], STBTT_vline, x, y, 0, 0);
							was_off = 0;
						}
					}
				}

				num_vertices = stbtt__close_shape(vertices, num_vertices, was_off, start_off, sx, sy, scx, scy, cx, cy);
			}
			else if (numberOfContours < 0)
			{
				var more = 1;
				var comp = data + g + 10;
				// VARIATION: gvar "points" of a composite are its components (in record order) + 4 phantoms.
				// Count the components first so the delta arrays can be sized, then walk the records again.
				double[] componentOffsetDeltaX = null;
				double[] componentOffsetDeltaY = null;
				if (!coords.IsDefault)
				{
					int componentCount = stbtt__CountCompositeComponents(comp);
					componentOffsetDeltaX = stbtt__ComputeCompositeComponentOffsetDeltas(glyph_index, coords, componentCount, g, out componentOffsetDeltaY);
				}
				int componentIndex = 0;
				num_vertices = 0;
				vertices = null;
				while (more != 0)
				{
					ushort flags = 0;
					ushort gidx = 0;
					var comp_num_verts = 0;
					var i = 0;
					stbtt_vertex[] comp_verts;
					stbtt_vertex[] tmp;
					var mtx = new float[6];
					mtx[0] = 1;
					mtx[1] = 0;
					mtx[2] = 0;
					mtx[3] = 1;
					mtx[4] = 0;
					mtx[5] = 0;
					flags = (ushort)ttSHORT(comp);
					comp += 2;
					gidx = (ushort)ttSHORT(comp);
					comp += 2;
					if ((flags & 2) != 0)
					{
						if ((flags & 1) != 0)
						{
							mtx[4] = ttSHORT(comp);
							comp += 2;
							mtx[5] = ttSHORT(comp);
							comp += 2;
						}
						else
						{
							mtx[4] = (sbyte)comp.Value;
							comp += 1;
							mtx[5] = (sbyte)comp.Value;
							comp += 1;
						}
					}

					if ((flags & (1 << 3)) != 0)
					{
						mtx[0] = mtx[3] = ttSHORT(comp) / 16384.0f;
						comp += 2;
						mtx[1] = mtx[2] = 0;
					}
					else if ((flags & (1 << 6)) != 0)
					{
						mtx[0] = ttSHORT(comp) / 16384.0f;
						comp += 2;
						mtx[1] = mtx[2] = 0;
						mtx[3] = ttSHORT(comp) / 16384.0f;
						comp += 2;
					}
					else if ((flags & (1 << 7)) != 0)
					{
						mtx[0] = ttSHORT(comp) / 16384.0f;
						comp += 2;
						mtx[1] = ttSHORT(comp) / 16384.0f;
						comp += 2;
						mtx[2] = ttSHORT(comp) / 16384.0f;
						comp += 2;
						mtx[3] = ttSHORT(comp) / 16384.0f;
						comp += 2;
					}

					// COMPOSITE TRANSFORM (OpenType glyf spec "Composite Glyph Description"; FreeType TT_Process_Composite_Component;
					// fontTools GlyphComponent):   x' = a*x + c*y + dx,   y' = b*x + d*y + dy   with (a b c d) = mtx[0..3].
					// The offset (dx, dy) is UNSCALED unless SCALED_COMPONENT_OFFSET (bit 11) is set — then dx scales by
					// |(a, b)| and dy by |(c, d)| (Apple semantics; FreeType's default when neither bit 11 nor UNSCALED bit 12 is set
					// is unscaled). Upstream stb_truetype computes m = |(a, b)|, n = |(c, d)| and then multiplies the WHOLE
					// transformed point by m / n — i.e. a scaled component is scaled twice (WE_HAVE_A_SCALE 1.5 → ×2.25) and the
					// offset is always scaled. FOUND 2026-09-03 by Fraunces 'bullet' (period @ scale 1.5): stb xMin 202 vs the font's
					// own header/fontTools/FreeType 135. Fixed here; the only oracle that could see it was the fontTools-instanced
					// hmtx (VariationMetricsTests lsb) — VF-vs-twin outline identity passes either way because both sides run this code.
					float offsetScaleX = 1f, offsetScaleY = 1f;
					if ((flags & (1 << 11)) != 0)
					{
						offsetScaleX = (float)Math.Sqrt(mtx[0] * mtx[0] + mtx[1] * mtx[1]);
						offsetScaleY = (float)Math.Sqrt(mtx[2] * mtx[2] + mtx[3] * mtx[3]);
					}
					// VARIATION (gvar composite rule): component offset deltas apply only to ARGS_ARE_XY_VALUES
					// components (flag bit 1); the delta-adjusted offset is what the component scale sees. Component
					// scale/2x2 is never varied. The component itself is fetched at the SAME instance.
					if (componentOffsetDeltaX != null && (flags & 2) != 0 && componentIndex < componentOffsetDeltaX.Length - Variations.OpenTypeGvarTable.PhantomPointCount)
					{
						mtx[4] = (float)Variations.FontVariationNormalization.OtRound(mtx[4] + componentOffsetDeltaX[componentIndex]);
						mtx[5] = (float)Variations.FontVariationNormalization.OtRound(mtx[5] + componentOffsetDeltaY[componentIndex]);
					}
					float dx = mtx[4] * offsetScaleX;
					float dy = mtx[5] * offsetScaleY;
					componentIndex++;
					comp_num_verts = coords.IsDefault
						? stbtt_GetGlyphShape(gidx, out comp_verts)
						: stbtt_GetGlyphShapeVar(gidx, coords, out comp_verts);
					if (comp_num_verts > 0)
					{
						// Round (not truncate) the transformed coordinates: fontTools GlyphCoordinates.toInt / FreeType FT_MulFix
						// both round; truncation toward zero would bias every scaled component by up to 1 unit toward the origin.
						for (i = 0; i < comp_num_verts; ++i)
						{
							short x = 0;
							short y = 0;
							x = comp_verts[i].x;
							y = comp_verts[i].y;
							comp_verts[i].x = (short)Math.Round(mtx[0] * x + mtx[2] * y + dx, MidpointRounding.AwayFromZero);
							comp_verts[i].y = (short)Math.Round(mtx[1] * x + mtx[3] * y + dy, MidpointRounding.AwayFromZero);
							x = comp_verts[i].cx;
							y = comp_verts[i].cy;
							comp_verts[i].cx = (short)Math.Round(mtx[0] * x + mtx[2] * y + dx, MidpointRounding.AwayFromZero);
							comp_verts[i].cy = (short)Math.Round(mtx[1] * x + mtx[3] * y + dy, MidpointRounding.AwayFromZero);
						}

						tmp = new stbtt_vertex[num_vertices + comp_num_verts];
						if (num_vertices > 0)
							Array.Copy(vertices, tmp, num_vertices);

						Array.Copy(comp_verts, 0, tmp, num_vertices, comp_num_verts);
						vertices = tmp;
						num_vertices += comp_num_verts;
					}

					more = flags & (1 << 5);
				}
			}

			pvertices = vertices;
			return num_vertices;
		}

		public Buf stbtt__cid_get_glyph_subrs(int glyph_index)
		{
			var fdselect = this.fdselect;
			var nranges = 0;
			var start = 0;
			var end = 0;
			var v = 0;
			var fmt = 0;
			var fdselector = -1;
			var i = 0;
			fdselect.stbtt__buf_seek(0);
			fmt = fdselect.stbtt__buf_get8();
			if (fmt == 0)
			{
				fdselect.stbtt__buf_skip(glyph_index);
				fdselector = fdselect.stbtt__buf_get8();
			}
			else if (fmt == 3)
			{
				nranges = (int)fdselect.stbtt__buf_get(2);
				start = (int)fdselect.stbtt__buf_get(2);
				for (i = 0; i < nranges; i++)
				{
					v = fdselect.stbtt__buf_get8();
					end = (int)fdselect.stbtt__buf_get(2);
					if (glyph_index >= start && glyph_index < end)
					{
						fdselector = v;
						break;
					}

					start = end;
				}
			}
			else if (fmt == 4)
			{
				// CFF2 FontDICTSelect format 4 (README §8.1): uint32 numRanges, Range4 { uint32 first, uint16 fontDICTID },
				// uint32 sentinel — a glyph belongs to the range whose 'first' <= glyph < next 'first' (sentinel last).
				nranges = (int)fdselect.stbtt__buf_get(4);
				start = (int)fdselect.stbtt__buf_get(4);
				for (i = 0; i < nranges; i++)
				{
					v = (int)fdselect.stbtt__buf_get(2);
					end = (int)fdselect.stbtt__buf_get(4);
					if (glyph_index >= start && glyph_index < end)
					{
						fdselector = v;
						break;
					}
					start = end;
				}
			}

			if (fdselector == -1)
				return new Buf(FakePtr<byte>.Null, 0);
			return Buf.stbtt__get_subrs(this.cff, fontdicts.stbtt__cff_index_get(fdselector));
		}

		/// <summary>
		/// CFF2 (README §8.2): the initial ItemVariationData index for a glyph's charstring = the 'vsindex' (key 22) of
		/// the PrivateDICT of the glyph's FontDICT, default 0. A charstring-level vsindex (0x0F) overrides it.
		/// FontDICT = FontDICTSelect(glyph) when present, else FontDICT#0.
		/// </summary>
		private int stbtt__cff2_private_dict_vsindex(int glyph_index)
		{
			int fdIndex = 0;
			if (this.fdselect.size != 0)
			{
				var fdselect = this.fdselect;
				fdselect.stbtt__buf_seek(0);
				int fmt = fdselect.stbtt__buf_get8();
				if (fmt == 0) { fdselect.stbtt__buf_skip(glyph_index); fdIndex = fdselect.stbtt__buf_get8(); }
				else if (fmt == 3 || fmt == 4)
				{
					int countBytes = fmt == 3 ? 2 : 4, fdBytes = fmt == 3 ? 1 : 2;
					int nranges = (int)fdselect.stbtt__buf_get(countBytes);
					int start = (int)fdselect.stbtt__buf_get(countBytes);
					for (int r = 0; r < nranges; r++)
					{
						int fd = (int)fdselect.stbtt__buf_get(fdBytes);
						int end = (int)fdselect.stbtt__buf_get(countBytes);
						if (glyph_index >= start && glyph_index < end) { fdIndex = fd; break; }
						start = end;
					}
				}
			}
			var privateLoc = new uint[2];
			fontdicts.stbtt__cff_index_get(fdIndex).stbtt__dict_get_ints(18, 2, new FakePtr<uint>(privateLoc));
			if (privateLoc[0] == 0 || privateLoc[1] == 0) return 0;
			uint vsindex = 0;
			this.cff.stbtt__buf_range((int)privateLoc[1], (int)privateLoc[0]).stbtt__dict_get_ints_default(22, ref vsindex);
			return (int)vsindex;
		}

		public int stbtt__run_charstring(int glyph_index, CharStringContext c)
			=> stbtt__run_charstring(glyph_index, c, Variations.FontVariationNormalizedCoordinates.Default);

		/// <summary>CFF2 operand-stack limit (OpenType CFF2 "Decoding DICT and CharString data": 513). 'CFF ' allows 48.</summary>
		private const int Cff2MaxOperandStackDepth = 513;

		/// <summary>
		/// Type 2 / CFF2 charstring interpreter. CFF2 deviations (README §8.1/§8.2), active only when isCff2:
		/// operand stack 513 (blend deltas ride the stack), no 'endchar'/'return' — a charstring or subroutine ends
		/// at the end of its data (subroutine ⇒ implicit return; top level ⇒ close_shape + success), 'vsindex'
		/// (0x0F) selects the ItemVariationData whose regions scale the deltas, 'blend' (0x10) folds n×k deltas into
		/// n defaults using those scalars at <paramref name="coords"/>. Default coords ⇒ all scalars 0 ⇒ blend returns
		/// the defaults: the CFF2 no-op proof is "blend with zero scalars", never "skip blend".
		/// </summary>
		public int stbtt__run_charstring(int glyph_index, CharStringContext c, in Variations.FontVariationNormalizedCoordinates coords)
		{
			var in_header = 1;
			var maskbits = 0;
			var subr_stack_height = 0;
			var sp = 0;
			var v = 0;
			var i = 0;
			var b0 = 0;
			var has_subrs = 0;
			var clear_stack = 0;
			var s = new float[Cff2MaxOperandStackDepth]; // 513 for CFF2; the CFF limit (48) is a subset
			var subr_stack = new Buf[10];
			for (i = 0; i < subr_stack.Length; ++i)
				subr_stack[i] = null;

			var subrs = this.subrs;
			float f = 0;
			var b = this.charstrings.stbtt__cff_index_get(glyph_index);

			// CFF2 variation state (README §8.2): active ItemVariationData + its lazily computed region scalars.
			Variations.OpenTypeItemVariationStore cff2Store = this.isCff2 ? stbtt__GetVariationTables()?.Cff2VariationStore : null;
			int activeItemVariationData = this.isCff2 && cff2Store != null ? stbtt__cff2_private_dict_vsindex(glyph_index) : 0;
			double[] regionScalars = null;
			int activeRegionCount = -1; // -1 = not computed for the active ivd yet

			while (true)
			{
				if (b.cursor >= b.size)
				{
					if (!this.isCff2)
						return 0; // 'CFF ': running off the end without endchar is malformed
					// CFF2: end of data IS the return (subroutine) / the end of the glyph (top level).
					if (subr_stack_height > 0)
					{
						b = subr_stack[--subr_stack_height];
						continue;
					}
					c.stbtt__csctx_close_shape();
					return 1;
				}
				i = 0;
				clear_stack = 1;
				b0 = b.stbtt__buf_get8();
				switch (b0)
				{
					case 0x13:
					case 0x14:
						if (in_header != 0)
							maskbits += sp / 2;
						in_header = 0;
						b.stbtt__buf_skip((maskbits + 7) / 8);
						break;
					case 0x01:
					case 0x03:
					case 0x12:
					case 0x17:
						maskbits += sp / 2;
						break;
					case 0x15:
						in_header = 0;
						if (sp < 2)
							return 0;
						c.stbtt__csctx_rmove_to(s[sp - 2], s[sp - 1]);
						break;
					case 0x04:
						in_header = 0;
						if (sp < 1)
							return 0;
						c.stbtt__csctx_rmove_to(0, s[sp - 1]);
						break;
					case 0x16:
						in_header = 0;
						if (sp < 1)
							return 0;
						c.stbtt__csctx_rmove_to(s[sp - 1], 0);
						break;
					case 0x05:
						if (sp < 2)
							return 0;
						for (; i + 1 < sp; i += 2)
							c.stbtt__csctx_rline_to(s[i], s[i + 1]);
						break;
					case 0x07:
					case 0x06:
						if (sp < 1)
							return 0;
						var goto_vlineto = b0 == 0x07 ? 1 : 0;
						for (; ; )
						{
							if (goto_vlineto == 0)
							{
								if (i >= sp)
									break;
								c.stbtt__csctx_rline_to(s[i], 0);
								i++;
							}

							goto_vlineto = 0;
							if (i >= sp)
								break;
							c.stbtt__csctx_rline_to(0, s[i]);
							i++;
						}

						break;
					case 0x1F:
					case 0x1E:
						if (sp < 4)
							return 0;
						var goto_hvcurveto = b0 == 0x1F ? 1 : 0;
						for (; ; )
						{
							if (goto_hvcurveto == 0)
							{
								if (i + 3 >= sp)
									break;
								c.stbtt__csctx_rccurve_to(0, s[i], s[i + 1], s[i + 2], s[i + 3],
									sp - i == 5 ? s[i + 4] : 0.0f);
								i += 4;
							}

							goto_hvcurveto = 0;
							if (i + 3 >= sp)
								break;
							c.stbtt__csctx_rccurve_to(s[i], 0, s[i + 1], s[i + 2], sp - i == 5 ? s[i + 4] : 0.0f,
								s[i + 3]);
							i += 4;
						}

						break;
					case 0x08:
						if (sp < 6)
							return 0;
						for (; i + 5 < sp; i += 6)
							c.stbtt__csctx_rccurve_to(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
						break;
					case 0x18:
						if (sp < 8)
							return 0;
						for (; i + 5 < sp - 2; i += 6)
							c.stbtt__csctx_rccurve_to(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
						if (i + 1 >= sp)
							return 0;
						c.stbtt__csctx_rline_to(s[i], s[i + 1]);
						break;
					case 0x19:
						if (sp < 8)
							return 0;
						for (; i + 1 < sp - 6; i += 2)
							c.stbtt__csctx_rline_to(s[i], s[i + 1]);
						if (i + 5 >= sp)
							return 0;
						c.stbtt__csctx_rccurve_to(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
						break;
					case 0x1A:
					case 0x1B:
						if (sp < 4)
							return 0;
						f = (float)0.0;
						if ((sp & 1) != 0)
						{
							f = s[i];
							i++;
						}

						for (; i + 3 < sp; i += 4)
						{
							if (b0 == 0x1B)
								c.stbtt__csctx_rccurve_to(s[i], f, s[i + 1], s[i + 2], s[i + 3], (float)0.0);
							else
								c.stbtt__csctx_rccurve_to(f, s[i], s[i + 1], s[i + 2], (float)0.0, s[i + 3]);
							f = (float)0.0;
						}

						break;
					case 0x0A:
					case 0x1D:
						if (b0 == 0x0A)
							if (has_subrs == 0)
							{
								if (this.fdselect.size != 0)
									subrs = stbtt__cid_get_glyph_subrs(glyph_index);
								has_subrs = 1;
							}

						if (sp < 1)
							return 0;
						v = (int)s[--sp];
						if (subr_stack_height >= 10)
							return 0;
						subr_stack[subr_stack_height++] = b;
						b = b0 == 0x0A ? subrs.stbtt__get_subr(v) : this.gsubrs.stbtt__get_subr(v);
						if (b.size == 0)
							return 0;
						b.cursor = 0;
						clear_stack = 0;
						break;
					case 0x0B:
						if (subr_stack_height <= 0)
							return 0;
						b = subr_stack[--subr_stack_height];
						clear_stack = 0;
						break;
					case 0x0E:
						c.stbtt__csctx_close_shape();
						return 1;
					case 0x0F: // CFF2 vsindex: [ ivd ] — select the ItemVariationData for subsequent blends
						if (!this.isCff2 || sp < 1)
							return 0;
						activeItemVariationData = (int)s[sp - 1];
						activeRegionCount = -1; // scalars recomputed on the next blend
						break;
					case 0x10: // CFF2 blend: [ ... n defaults, n*k deltas, n ] -> [ ... n blended values ]
					{
						if (!this.isCff2 || cff2Store == null || sp < 1)
							return 0;
						if (activeRegionCount < 0)
						{
							activeRegionCount = cff2Store.GetRegionIndexCount(activeItemVariationData);
							if (activeRegionCount < 0)
								return 0;
							if (regionScalars == null || regionScalars.Length < activeRegionCount)
								regionScalars = new double[Math.Max(activeRegionCount, 1)];
							if (cff2Store.GetRegionScalars(activeItemVariationData, coords, regionScalars) != activeRegionCount)
								return 0;
						}
						int blendCount = (int)s[sp - 1];
						int k = activeRegionCount;
						if (blendCount < 0 || blendCount > 513 || sp < 1 + blendCount + blendCount * k)
							return 0;
						int baseIndex = sp - 1 - blendCount - blendCount * k;
						for (int valueIndex = 0; valueIndex < blendCount; valueIndex++)
						{
							double deltaSum = 0.0;
							int deltaStart = baseIndex + blendCount + valueIndex * k;
							for (int region = 0; region < k; region++)
							{
								double scalar = regionScalars[region];
								if (scalar != 0.0)
									deltaSum += scalar * s[deltaStart + region];
							}
							// ORACLE PARITY (fontTools varLib.instancer.instantiateCFF2, read 2026-09-02): the instancer rounds
							// the DELTA SUM with Python round() — half-to-EVEN — and adds it to the untouched default:
							//     value = default + round_half_even(Σ scalar·delta)
							// Operands are RELATIVE coordinates, so any other tie-break (otRound on the total broke ties
							// upward) accumulates along a contour: measured 2–6 unit drifts at fractional scalars (CNTR 50)
							// and even flips close_shape's start!=end test. FreeType keeps 16.16 fixed here instead — a
							// sub-unit divergence from Chrome we accept in exchange for an exact fixture oracle.
							s[baseIndex + valueIndex] = (float)(s[baseIndex + valueIndex] + Math.Round(deltaSum, MidpointRounding.ToEven));
						}
						sp = baseIndex + blendCount;
						clear_stack = 0; // the blended operands stay for the path operator that follows
						break;
					}
					case 0x0C:
					{
						float dx1 = 0;
						float dx2 = 0;
						float dx3 = 0;
						float dx4 = 0;
						float dx5 = 0;
						float dx6 = 0;
						float dy1 = 0;
						float dy2 = 0;
						float dy3 = 0;
						float dy4 = 0;
						float dy5 = 0;
						float dy6 = 0;
						float dx = 0;
						float dy = 0;
						var b1 = (int)b.stbtt__buf_get8();
						switch (b1)
						{
							case 0x22:
								if (sp < 7)
									return 0;
								dx1 = s[0];
								dx2 = s[1];
								dy2 = s[2];
								dx3 = s[3];
								dx4 = s[4];
								dx5 = s[5];
								dx6 = s[6];
								c.stbtt__csctx_rccurve_to(dx1, 0, dx2, dy2, dx3, 0);
								c.stbtt__csctx_rccurve_to(dx4, 0, dx5, -dy2, dx6, 0);
								break;
							case 0x23:
								if (sp < 13)
									return 0;
								dx1 = s[0];
								dy1 = s[1];
								dx2 = s[2];
								dy2 = s[3];
								dx3 = s[4];
								dy3 = s[5];
								dx4 = s[6];
								dy4 = s[7];
								dx5 = s[8];
								dy5 = s[9];
								dx6 = s[10];
								dy6 = s[11];
								c.stbtt__csctx_rccurve_to(dx1, dy1, dx2, dy2, dx3, dy3);
								c.stbtt__csctx_rccurve_to(dx4, dy4, dx5, dy5, dx6, dy6);
								break;
							case 0x24:
								if (sp < 9)
									return 0;
								dx1 = s[0];
								dy1 = s[1];
								dx2 = s[2];
								dy2 = s[3];
								dx3 = s[4];
								dx4 = s[5];
								dx5 = s[6];
								dy5 = s[7];
								dx6 = s[8];
								c.stbtt__csctx_rccurve_to(dx1, dy1, dx2, dy2, dx3, 0);
								c.stbtt__csctx_rccurve_to(dx4, 0, dx5, dy5, dx6, -(dy1 + dy2 + dy5));
								break;
							case 0x25:
								if (sp < 11)
									return 0;
								dx1 = s[0];
								dy1 = s[1];
								dx2 = s[2];
								dy2 = s[3];
								dx3 = s[4];
								dy3 = s[5];
								dx4 = s[6];
								dy4 = s[7];
								dx5 = s[8];
								dy5 = s[9];
								dx6 = dy6 = s[10];
								dx = dx1 + dx2 + dx3 + dx4 + dx5;
								dy = dy1 + dy2 + dy3 + dy4 + dy5;
								if (Math.Abs((double)dx) > Math.Abs((double)dy))
									dy6 = -dy;
								else
									dx6 = -dx;
								c.stbtt__csctx_rccurve_to(dx1, dy1, dx2, dy2, dx3, dy3);
								c.stbtt__csctx_rccurve_to(dx4, dy4, dx5, dy5, dx6, dy6);
								break;
							default:
								// Unknown/deprecated escape operators (e.g. dotsection 12 0) — treat as no-op
								break;
						}
					} 
					break;
					default:
						if (b0 != 255 && b0 != 28 && (b0 < 32 || b0 > 254))
							return 0;
						if (b0 == 255)
						{
							f = (float)(int)b.stbtt__buf_get(4) / 0x10000;
						}
						else
						{
							b.stbtt__buf_skip(-1);
							f = (short)b.stbtt__cff_int();
						}

						if (sp >= s.Length) // 48 for 'CFF ' semantics is a subset of the 513-entry CFF2 stack
							return 0;
						s[sp++] = f;
						clear_stack = 0;
						break;
				}

				if (clear_stack != 0)
					sp = 0;
			}
		}

		public int stbtt__GetGlyphShapeT2(int glyph_index, out stbtt_vertex[] pvertices)
			=> stbtt__GetGlyphShapeT2(glyph_index, Variations.FontVariationNormalizedCoordinates.Default, out pvertices);

		/// <summary>Type 2 / CFF2 outline at a variation instance (coords only matter for CFF2 'blend'; ignored by 'CFF ').</summary>
		public int stbtt__GetGlyphShapeT2(int glyph_index, in Variations.FontVariationNormalizedCoordinates coords, out stbtt_vertex[] pvertices)
		{
			var count_ctx = new CharStringContext();
			count_ctx.bounds = 1;
			var output_ctx = new CharStringContext();
			if (stbtt__run_charstring(glyph_index, count_ctx, coords) != 0)
			{
				pvertices = new stbtt_vertex[count_ctx.num_vertices];
				output_ctx.pvertices = pvertices;
				if (stbtt__run_charstring(glyph_index, output_ctx, coords) != 0)
					return output_ctx.num_vertices;
			}

			pvertices = null;
			return 0;
		}

		public int stbtt__GetGlyphInfoT2(int glyph_index, ref int x0, ref int y0,
			ref int x1, ref int y1)
		{
			var c = new CharStringContext();
			c.bounds = 1;
			var r = stbtt__run_charstring(glyph_index, c);
			x0 = r != 0 ? c.min_x : 0;
			y0 = r != 0 ? c.min_y : 0;
			x1 = r != 0 ? c.max_x : 0;
			y1 = r != 0 ? c.max_y : 0;
			return r != 0 ? c.num_vertices : 0;
		}

		public int stbtt_GetGlyphShape(int glyph_index, out stbtt_vertex[] pvertices)
		{
			if (this.cff.size == 0)
				return stbtt__GetGlyphShapeTT(glyph_index, out pvertices);
			return stbtt__GetGlyphShapeT2(glyph_index, out pvertices);
		}

		/// <summary>
		/// Detects exactly-horizontal outline LINE segments of at least
		/// min_length_font_units (SilkyNvg plans/crisp_text_yonly_autofit_gridfit.md
		/// Phase 2 — stem-edge detection for y-only grid-fit). Returns edge count;
		/// edge_ys are y positions in FONT UNITS (y-up), edge_is_top_of_ink is true
		/// when the ink lies BELOW the edge (the edge is the upper boundary of a
		/// bar). Ink side comes from segment direction: TrueType fills clockwise
		/// (y-up), so a +x segment has ink below it; CFF/Type2 winds the opposite
		/// way and is flipped accordingly. Curves are ignored — flat bar edges
		/// (crossbars of H/e/t/f, arms of E) are straight lines in practice.
		/// </summary>
		public int stbtt_GetGlyphHorizontalLineEdges(int glyph_index, int min_length_font_units,
			out float[] edge_ys, out bool[] edge_is_top_of_ink)
		{
			edge_ys = null;
			edge_is_top_of_ink = null;
			stbtt_vertex[] vertices;
			var num_verts = stbtt_GetGlyphShape(glyph_index, out vertices);
			if (num_verts <= 0)
				return 0;

			var is_cff = this.cff.size != 0;
			var count = 0;
			float[] ys = null;
			bool[] tops = null;
			float prev_x = 0;
			float prev_y = 0;
			for (var i = 0; i < num_verts; ++i)
			{
				var v = vertices[i];
				if (v.type == STBTT_vline && v.y == prev_y && Math.Abs(v.x - prev_x) >= min_length_font_units)
				{
					// TrueType (CW, y-up): travelling +x means interior is below
					// the segment -> upper boundary of ink. CFF (CCW): flipped.
					var top_of_ink = v.x > prev_x;
					if (is_cff)
						top_of_ink = !top_of_ink;
					if (ys == null)
					{
						ys = new float[8];
						tops = new bool[8];
					}
					else if (count == ys.Length)
					{
						Array.Resize(ref ys, count * 2);
						Array.Resize(ref tops, count * 2);
					}
					ys[count] = v.y;
					tops[count] = top_of_ink;
					++count;
				}
				prev_x = v.x;
				prev_y = v.y;
			}

			if (count == 0)
				return 0;
			Array.Resize(ref ys, count);
			Array.Resize(ref tops, count);
			edge_ys = ys;
			edge_is_top_of_ink = tops;
			return count;
		}

		public void stbtt_GetGlyphHMetrics(int glyph_index, ref int advanceWidth,
			ref int leftSideBearing)
		{
			var numOfLongHorMetrics = ttUSHORT(this.data + this.hhea + 34);
			if (glyph_index < numOfLongHorMetrics)
			{
				advanceWidth = ttSHORT(this.data + this.hmtx + 4 * glyph_index);
				leftSideBearing = ttSHORT(this.data + this.hmtx + 4 * glyph_index + 2);
			}
			else
			{
				advanceWidth = ttSHORT(this.data + this.hmtx + 4 * (numOfLongHorMetrics - 1));
				leftSideBearing = ttSHORT(this.data + this.hmtx + 4 * numOfLongHorMetrics +
										  2 * (glyph_index - numOfLongHorMetrics));
			}
		}

		/// <summary>
		/// Vertical header metrics (OpenType 'vhea': ascent/descent/lineGap for vertical layout, font units).
		/// Parsed at load, consumed by a future vertical-text plan (plans/color_glyph_fonts.md Q4).
		/// Returns false (zeros) when the font has no 'vhea'.
		/// </summary>
		public bool stbtt_GetFontVerticalMetrics(out int vertAscent, out int vertDescent, out int vertLineGap)
		{
			vertAscent = 0;
			vertDescent = 0;
			vertLineGap = 0;
			if (this.vhea == 0)
				return false;
			// vhea: Version16Dot16 @0, vertTypoAscender @4, vertTypoDescender @6, vertTypoLineGap @8.
			vertAscent = ttSHORT(this.data + this.vhea + 4);
			vertDescent = ttSHORT(this.data + this.vhea + 6);
			vertLineGap = ttSHORT(this.data + this.vhea + 8);
			return true;
		}

		/// <summary>
		/// Per-glyph vertical metrics (OpenType 'vmtx': advanceHeight + topSideBearing, font units). Same
		/// long/short metric split as hmtx, with numOfLongVerMetrics at vhea+34. Returns false (zeros) when
		/// the font lacks 'vhea' or 'vmtx'.
		/// </summary>
		public bool stbtt_GetGlyphVMetrics(int glyph_index, out int advanceHeight, out int topSideBearing)
		{
			advanceHeight = 0;
			topSideBearing = 0;
			if (this.vhea == 0 || this.vmtx == 0)
				return false;
			int numOfLongVerMetrics = ttUSHORT(this.data + this.vhea + 34);
			if (numOfLongVerMetrics == 0)
				return false;
			if (glyph_index < numOfLongVerMetrics)
			{
				advanceHeight = ttUSHORT(this.data + this.vmtx + 4 * glyph_index);
				topSideBearing = ttSHORT(this.data + this.vmtx + 4 * glyph_index + 2);
			}
			else
			{
				advanceHeight = ttUSHORT(this.data + this.vmtx + 4 * (numOfLongVerMetrics - 1));
				topSideBearing = ttSHORT(this.data + this.vmtx + 4 * numOfLongVerMetrics +
				                         2 * (glyph_index - numOfLongVerMetrics));
			}
			return true;
		}

		public int stbtt_GetKerningTableLength()
		{
			var data = this.data + this.kern;
			if (this.kern == 0)
				return 0;
			if (ttUSHORT(data + 2) < 1)
				return 0;
			if (ttUSHORT(data + 8) != 1)
				return 0;
			return ttUSHORT(data + 10);
		}

		public int stbtt_GetKerningTable(stbtt_kerningentry[] table, int table_length)
		{
			var data = this.data + this.kern;
			var k = 0;
			var length = 0;
			if (this.kern == 0)
				return 0;
			if (ttUSHORT(data + 2) < 1)
				return 0;
			if (ttUSHORT(data + 8) != 1)
				return 0;
			length = ttUSHORT(data + 10);
			if (table_length < length)
				length = table_length;
			for (k = 0; k < length; k++)
			{
				table[k].glyph1 = ttUSHORT(data + 18 + k * 6);
				table[k].glyph2 = ttUSHORT(data + 20 + k * 6);
				table[k].advance = ttSHORT(data + 22 + k * 6);
			}

			return length;
		}

		public int stbtt__GetGlyphKernInfoAdvance(int glyph1, int glyph2)
		{
			var data = this.data + this.kern;
			uint needle = 0;
			uint straw = 0;
			var l = 0;
			var r = 0;
			var m = 0;
			if (this.kern == 0)
				return 0;
			if (ttUSHORT(data + 2) < 1)
				return 0;
			if (ttUSHORT(data + 8) != 1)
				return 0;
			l = 0;
			r = ttUSHORT(data + 10) - 1;
			needle = (uint)((glyph1 << 16) | glyph2);
			while (l <= r)
			{
				m = (l + r) >> 1;
				straw = ttULONG(data + 18 + m * 6);
				if (needle < straw)
					r = m - 1;
				else if (needle > straw)
					l = m + 1;
				else
					return ttSHORT(data + 22 + m * 6);
			}

			return 0;
		}

		public int stbtt__GetGlyphGPOSInfoAdvance(int glyph1, int glyph2)
			=> stbtt__GetGlyphGPOSInfoAdvance(glyph1, glyph2, Variations.FontVariationNormalizedCoordinates.Default, null);

		// gdefStore: the GDEF ItemVariationStore for VariationIndex deltas (Part C); null = static kerning.
		private int stbtt__GetGlyphGPOSInfoAdvance(int glyph1, int glyph2,
			in Variations.FontVariationNormalizedCoordinates coords, Variations.OpenTypeItemVariationStore gdefStore)
		{
			var kernLookups = stbtt__ResolveGposKernLookups();
			if (kernLookups == null)
				return 0;
			var gposData = this.data + this.gpos;
			var lookupList = gposData + ttUSHORT(gposData + 8);
			var totalAdvance = 0;
			for (var i = 0; i < kernLookups.Length; ++i)
			{
				if (!kernLookups[i])
					continue;
				var lookupTable = lookupList + ttUSHORT(lookupList + 2 + 2 * i);
				totalAdvance += stbtt__GPOSLookupPairAdvance(lookupTable, glyph1, glyph2, coords, gdefStore);
			}

			return totalAdvance;
		}

		// Cached result of stbtt__ResolveGposKernLookups (resolved once per font).
		private bool[] gposKernLookups;
		private bool gposKernResolved;

		// Resolves latn/DFLT default-LangSys 'kern' feature lookups ONCE per font.
		// Returns null when GPOS has no usable kern feature for latn/DFLT — callers
		// (stbtt_GetGlyphKernAdvance) then fall back to the legacy 'kern' table.
		//
		// ── GPOS FeatureVariations: READER EXISTS, HOOK DELIBERATELY ABSENT (spec §9.9, approved 2026-09-02) ──
		// GPOS 1.1 carries `Offset32 featureVariationsOffset` at byte 10 — the same FeatureVariations / ConditionSet /
		// FeatureTableSubstitution structure GSUB uses to swap a feature's lookup list per region of design space. A font
		// MAY use it to give the 'kern' feature different pair lookups at, say, heavy weights. The table-agnostic reader
		// (Layout/OpenTypeLayoutFeatureVariations.cs) is written and unit-tested on GSUB fixtures + synthetic tables, but
		// it is NOT wired here because NONE of our variable fonts carries a GPOS FeatureVariations table (all six checked
		// with fontTools 2026-09-02: `GPOS FV: False`) and untested wiring is where bugs hide. When a fixture arrives:
		//   var fv = OpenTypeLayoutFeatureVariations.TryParse(data, gpos, gposLength, ttULONG(data+gpos+10), axisCount, "GPOS", ...)
		//   int rec = fv.FindApplicableRecordIndex(coords);           // per INSTANCE, so this resolution can no longer be cached once per font
		//   featureTable = rec >= 0 && fv.Records[rec].GetAlternateFeatureTableAbsoluteOffset(featureIndex) is int alt && alt >= 0
		//                ? data + alt : defaultFeatureTable;
		// `stbtt_HasUnappliedGposFeatureVariations` (FontInfoGsub.cs) is true for a font that would need this hook, so the
		// gap is visible rather than silent.
		private bool[] stbtt__ResolveGposKernLookups()
		{
			if (this.gposKernResolved)
				return this.gposKernLookups;
			this.gposKernResolved = true;
			if (this.gpos == 0)
				return null;
			var data = this.data + this.gpos;
			if (ttUSHORT(data + 0) != 1)
				return null;
			// Minor version 0 or 1 (1.1 = has the featureVariationsOffset field; see the block comment above).
			if (ttUSHORT(data + 2) > 1)
				return null;
			var scriptList = data + ttUSHORT(data + 4);
			var featureList = data + ttUSHORT(data + 6);
			var lookupList = data + ttUSHORT(data + 8);
			var lookupCount = (int)ttUSHORT(lookupList);

			// Script selection: prefer 'latn', fall back to 'DFLT'; use the default LangSys. ONE implementation shared with
			// GSUB (Layout/OpenTypeLayoutScriptSelection.cs) so substitution and positioning agree on the LangSys in force.
			int gposLength = stbtt__find_table_length("GPOS");
			FakePtr<byte> langSys;
			if (!Layout.OpenTypeLayoutScriptSelection.TryGetDefaultLangSys(scriptList, gposLength - (scriptList.Offset - data.Offset), out langSys))
				return null;

			// Feature selection: mark the lookups of every 'kern' feature in the LangSys,
			// then apply marked lookups in LookupList INDEX order (OpenType application
			// order), accumulating adjustments ACROSS lookups.
			var kernLookups = new bool[lookupCount];
			var anyKern = false;
			var featureIndexCount = Layout.OpenTypeLayoutScriptSelection.GetFeatureIndexCount(langSys);
			for (var fi = 0; fi < featureIndexCount; ++fi)
			{
				var featureIndex = Layout.OpenTypeLayoutScriptSelection.GetFeatureIndex(langSys, fi);
				var featureRec = featureList + 2 + 6 * featureIndex;
				if (featureRec[0] != 'k' || featureRec[1] != 'e' || featureRec[2] != 'r' || featureRec[3] != 'n')
					continue;
				var featureTable = featureList + ttUSHORT(featureRec + 4);
				var featureLookupCount = (int)ttUSHORT(featureTable + 2);
				for (var li = 0; li < featureLookupCount; ++li)
				{
					int lookupIndex = ttUSHORT(featureTable + 4 + 2 * li);
					if (lookupIndex < lookupCount)
					{
						kernLookups[lookupIndex] = true;
						anyKern = true;
					}
				}
			}

			if (!anyKern)
				return null;

			this.gposKernLookups = kernLookups;
			return kernLookups;
		}

		// Applies one GPOS lookup (type 2 PairAdjustment, incl. type 9 Extension wrapping)
		// to the glyph pair. Within a lookup, the FIRST subtable that applies wins.
		// LookupFlags are deliberately ignored: for base-glyph pair kerning the only flag
		// seen in target fonts is IgnoreMarks (0x0008), a no-op when neither glyph is a mark.
		private int stbtt__GPOSLookupPairAdvance(FakePtr<byte> lookupTable, int glyph1, int glyph2,
			in Variations.FontVariationNormalizedCoordinates coords, Variations.OpenTypeItemVariationStore gdefStore)
		{
			var lookupType = (int)ttUSHORT(lookupTable);
			var subTableCount = (int)ttUSHORT(lookupTable + 4);
			var subTableOffsets = lookupTable + 6;
			if (lookupType != 2 && lookupType != 9)
				return 0;

			for (var sti = 0; sti < subTableCount; sti++)
			{
				var table = lookupTable + ttUSHORT(subTableOffsets + 2 * sti);
				if (lookupType == 9)
				{
					// Extension positioning: format(u16), extensionLookupType(u16), extensionOffset(u32)
					if (ttUSHORT(table) != 1)
						continue;
					if (ttUSHORT(table + 2) != 2)
						continue; // inner lookup is not PairAdjustment
					table = table + (int)ttULONG(table + 4);
				}

				int xAdvance;
				if (stbtt__GPOSPairSubtableApplyVar(table, glyph1, glyph2, coords, gdefStore, out xAdvance))
					return xAdvance; // first applying subtable ends the lookup
			}

			return 0;
		}

		// One PairPos subtable (format 1 or 2). Returns true if the subtable APPLIED
		// (per HarfBuzz semantics: fmt1 = pair record found; fmt2 = coverage + valid classes,
		// even when the adjustment value is 0). xAdvance is in unscaled font units.
		public static bool stbtt__GPOSPairSubtableApply(FakePtr<byte> table, int glyph1, int glyph2, out int xAdvance)
			=> stbtt__GPOSPairSubtableApplyVar(table, glyph1, glyph2, Variations.FontVariationNormalizedCoordinates.Default, null, out xAdvance);

		// Variable-font aware PairPos apply (Part C). When <paramref name="gdefStore"/> is non-null and the
		// instance is not the default, the X_ADVANCE_DEVICE field (ValueFormat 0x0040) is read: a VariationIndex
		// table (deltaFormat 0x8000) adds otRound(store delta) to xAdvance; ppem Device tables (0x0001-0x0003)
		// are ignored (unhinted rendering). Device offsets are relative to the IMMEDIATE PARENT table: the PairSet
		// for format 1, the PairPos subtable for format 2 (OT spec, ValueRecord) — NOT the ValueRecord.
		public static bool stbtt__GPOSPairSubtableApplyVar(FakePtr<byte> table, int glyph1, int glyph2,
			in Variations.FontVariationNormalizedCoordinates coords, Variations.OpenTypeItemVariationStore gdefStore, out int xAdvance)
		{
			xAdvance = 0;
			var posFormat = (int)ttUSHORT(table);
			var coverageIndex = stbtt__GetCoverageIndex(table + ttUSHORT(table + 2), glyph1);
			if (coverageIndex == -1)
				return false;

			var valueFormat1 = (int)ttUSHORT(table + 4);
			var valueFormat2 = (int)ttUSHORT(table + 6);
			// ValueRecord layout: record size = 2 x popcount(valueFormat) bytes (device-table
			// bits 0x0010-0x0080 each contribute a 2-byte offset field to the SIZE but are
			// ignored for the value); xAdvance sits after the fields below bit 2
			// (XPlacement, YPlacement), i.e. at byte offset 2 x popcount(valueFormat & 0x0003).
			var valueRecordSize1 = 2 * stbtt__popcount(valueFormat1);
			var valueRecordSize2 = 2 * stbtt__popcount(valueFormat2);
			var hasXAdvance = (valueFormat1 & 4) != 0;
			var xAdvanceOffset = 2 * stbtt__popcount(valueFormat1 & 3);
			// X_ADVANCE_DEVICE sits after every lower field (xPla yPla xAdv yAdv xPlaDev yPlaDev) that is present.
			var applyAdvanceVariation = gdefStore != null && !coords.IsDefault && (valueFormat1 & 0x0040) != 0;
			var xAdvanceDeviceOffsetInRecord = 2 * stbtt__popcount(valueFormat1 & 0x003F);

			switch (posFormat)
			{
				case 1:
				{
					var pairValueTable = table + ttUSHORT(table + 10 + 2 * coverageIndex);
					var pairValueCount = (int)ttUSHORT(pairValueTable);
					var pairValueArray = pairValueTable + 2;
					var stride = 2 + valueRecordSize1 + valueRecordSize2;
					var l = 0;
					var r = pairValueCount - 1;
					while (l <= r)
					{
						var m = (l + r) >> 1;
						var pairValue = pairValueArray + stride * m;
						var straw = (int)ttUSHORT(pairValue);
						if (glyph2 < straw)
							r = m - 1;
						else if (glyph2 > straw)
							l = m + 1;
						else
						{
							if (hasXAdvance)
								xAdvance = ttSHORT(pairValue + 2 + xAdvanceOffset);
							if (applyAdvanceVariation)
								xAdvance += stbtt__GPOSVariationIndexDelta(pairValueTable, ttUSHORT(pairValue + 2 + xAdvanceDeviceOffsetInRecord), gdefStore, coords);
							return true;
						}
					}

					return false; // coverage hit but no pair record: NOT applied, try next subtable
				}
				case 2:
				{
					var glyph1class = stbtt__GetGlyphClass(table + ttUSHORT(table + 8), glyph1);
					var glyph2class = stbtt__GetGlyphClass(table + ttUSHORT(table + 10), glyph2);
					var class1Count = (int)ttUSHORT(table + 12);
					var class2Count = (int)ttUSHORT(table + 14);
					if (glyph1class < 0 || glyph1class >= class1Count || glyph2class < 0 || glyph2class >= class2Count)
						return false; // malformed classes: treat as not applied
					var recordSize = valueRecordSize1 + valueRecordSize2;
					var record = table + 16 + (glyph1class * class2Count + glyph2class) * recordSize;
					if (hasXAdvance)
						xAdvance = ttSHORT(record + xAdvanceOffset);
					if (applyAdvanceVariation)
						xAdvance += stbtt__GPOSVariationIndexDelta(table, ttUSHORT(record + xAdvanceDeviceOffsetInRecord), gdefStore, coords);
					return true; // fmt2 applies whenever classes resolve (even value 0)
				}
				default:
					return false;
			}
		}

		// Reads the Device/VariationIndex table at <paramref name="deviceOffsetFromParent"/> (0 = NULL) and
		// returns the ROUNDED delta for the instance: only deltaFormat 0x8000 (VariationIndex: outer, inner,
		// 0x8000) contributes; formats 1-3 are ppem hinting deltas for non-variable fonts and yield 0.
		// Bounds-checked: a truncated table yields 0 (fail-soft, never throws at draw time).
		private static int stbtt__GPOSVariationIndexDelta(FakePtr<byte> parentTable, int deviceOffsetFromParent,
			Variations.OpenTypeItemVariationStore gdefStore, in Variations.FontVariationNormalizedCoordinates coords)
		{
			if (deviceOffsetFromParent == 0)
				return 0;
			var device = parentTable + deviceOffsetFromParent;
			if (device.RemainingLength < 6)
				return 0;
			if (ttUSHORT(device + 4) != 0x8000)
				return 0; // ppem Device table (or reserved bits set): not a variation reference
			int outerIndex = ttUSHORT(device), innerIndex = ttUSHORT(device + 2);
			return Variations.FontVariationNormalization.OtRound(gdefStore.ComputeDelta(outerIndex, innerIndex, coords));
		}

		private static int stbtt__popcount(int v)
		{
			v = v - ((v >> 1) & 0x55555555);
			v = (v & 0x33333333) + ((v >> 2) & 0x33333333);
			return (((v + (v >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24;
		}

		public int stbtt_GetGlyphKernAdvance(int g1, int g2)
		{
			return stbtt_GetGlyphKernAdvanceVar(g1, g2, Variations.FontVariationNormalizedCoordinates.Default);
		}

		/// <summary>
		/// Pair kerning at a variation instance (Part C, README §7): the static GPOS xAdvance PLUS the
		/// VariationIndex delta from the GDEF ItemVariationStore when the ValueRecord carries one
		/// (X_ADVANCE_DEVICE, deltaFormat 0x8000) — otRound(Σ regionScalar × delta), the same rule as HVAR.
		/// Default coords, static fonts, fonts without a GDEF store, and the legacy 'kern' table path are
		/// byte-identical to <see cref="stbtt_GetGlyphKernAdvance(int,int)"/>. Oracle: fontTools-instanced
		/// twins' GPOS (tests/graphical/VariableFontTest --strings [ADV] kern Δ) and HarfBuzz.
		/// </summary>
		public int stbtt_GetGlyphKernAdvanceVar(int g1, int g2, in Variations.FontVariationNormalizedCoordinates coords)
		{
			// GPOS pair kerning when the font exposes a latn/DFLT 'kern' feature;
			// otherwise fall back to the legacy 'kern' table (also covers fonts whose
			// GPOS exists but carries no kern feature, e.g. mark/mkmk-only GPOS).
			// Font-level decision, never mixed per-pair.
			var xAdvance = 0;
			if (this.gpos != 0 && stbtt__ResolveGposKernLookups() != null)
			{
				Variations.OpenTypeItemVariationStore gdefStore = coords.IsDefault ? null : stbtt__GetVariationTables()?.GdefItemVariationStore;
				xAdvance += stbtt__GetGlyphGPOSInfoAdvance(g1, g2, coords, gdefStore);
			}
			else if (this.kern != 0)
				xAdvance += stbtt__GetGlyphKernInfoAdvance(g1, g2);
			return xAdvance;
		}

		public int stbtt_GetCodepointKernAdvance(int ch1, int ch2)
		{
			if (this.kern == 0 && this.gpos == 0)
				return 0;
			return stbtt_GetGlyphKernAdvance(stbtt_FindGlyphIndex(ch1), stbtt_FindGlyphIndex(ch2));
		}

		public void stbtt_GetCodepointHMetrics(int codepoint, ref int advanceWidth,
			ref int leftSideBearing)
		{
			stbtt_GetGlyphHMetrics(stbtt_FindGlyphIndex(codepoint), ref advanceWidth, ref leftSideBearing);
		}

		public void stbtt_GetFontVMetrics(out int ascent, out int descent, out int lineGap)
		{
			ascent = ttSHORT(this.data + this.hhea + 4);
			descent = ttSHORT(this.data + this.hhea + 6);
			lineGap = ttSHORT(this.data + this.hhea + 8);
		}

		/// <summary>Reads the complete vertical metric selection data from OS/2 version 0 or later.</summary>
		public bool stbtt_GetOs2VerticalMetrics(out Os2VerticalMetrics metrics)
		{
			var tab = (int)stbtt__find_table(this.data, (uint)this.fontstart, "OS/2");
			// Every OS/2 version contains fields through usWinDescent at byte 77.
			// Validate the backing data before reading malformed/truncated font input.
			if (tab == 0 || tab < 0 || tab > this.data.RemainingLength - 78)
			{
				metrics = default;
				return false;
			}

			metrics = new Os2VerticalMetrics(
				ttUSHORT(this.data + tab),
				ttUSHORT(this.data + tab + 62),
				ttSHORT(this.data + tab + 68),
				ttSHORT(this.data + tab + 70),
				ttSHORT(this.data + tab + 72),
				ttUSHORT(this.data + tab + 74),
				ttUSHORT(this.data + tab + 76));
			return true;
		}

		public int stbtt_GetFontVMetricsOS2(ref int typoAscent, ref int typoDescent,
			ref int typoLineGap)
		{
			if (!stbtt_GetOs2VerticalMetrics(out Os2VerticalMetrics metrics))
				return 0;
			typoAscent = metrics.TypographicAscent;
			typoDescent = metrics.TypographicDescent;
			typoLineGap = metrics.TypographicLineGap;
			return 1;
		}

		public void stbtt_GetFontBoundingBox(ref int x0, ref int y0, ref int x1, ref int y1)
		{
			x0 = ttSHORT(this.data + this.head + 36);
			y0 = ttSHORT(this.data + this.head + 38);
			x1 = ttSHORT(this.data + this.head + 40);
			y1 = ttSHORT(this.data + this.head + 42);
		}

		public float stbtt_ScaleForPixelHeight(float height)
		{
			var fheight = ttSHORT(this.data + this.hhea + 4) - ttSHORT(this.data + this.hhea + 6);
			return height / fheight;
		}

		public float stbtt_ScaleForMappingEmToPixels(float pixels)
		{
			var unitsPerEm = (int)ttUSHORT(this.data + this.head + 18);
			return pixels / unitsPerEm;
		}

		public FakePtr<byte> stbtt_FindSVGDoc(int gl)
		{
			var i = 0;
			var data = this.data;
			var svg_doc_list = data + stbtt__get_svg();
			var numEntries = (int)ttUSHORT(svg_doc_list);
			var svg_docs = svg_doc_list + 2;
			for (i = 0; i < numEntries; i++)
			{
				var svg_doc = svg_docs + 12 * i;
				if (gl >= ttUSHORT(svg_doc) && gl <= ttUSHORT(svg_doc + 2))
					return svg_doc;
			}

			return FakePtr<byte>.Null;
		}

		public int stbtt_GetGlyphSVG(int gl, ref FakePtr<byte> svg)
		{
			var data = this.data;
			FakePtr<byte> svg_doc;
			if (this.svg == 0)
				return 0;
			svg_doc = stbtt_FindSVGDoc(gl);
			if (!svg_doc.IsNull)
			{
				svg = data + this.svg + ttULONG(svg_doc + 4);
				return (int)ttULONG(svg_doc + 8);
			}

			return 0;
		}

		public int stbtt_GetCodepointSVG(int unicode_codepoint, ref FakePtr<byte> svg)
		{
			return stbtt_GetGlyphSVG(stbtt_FindGlyphIndex(unicode_codepoint), ref svg);
		}

		public void stbtt_GetGlyphBitmapBoxSubpixel(int glyph, float scale_x, float scale_y,
			float shift_x, float shift_y, ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			var x0 = 0;
			var y0 = 0;
			var x1 = 0;
			var y1 = 0;
			if (stbtt_GetGlyphBox(glyph, ref x0, ref y0, ref x1, ref y1) == 0)
			{
				ix0 = 0;
				iy0 = 0;
				ix1 = 0;
				iy1 = 0;
			}
			else
			{
				ix0 = (int)Math.Floor(x0 * scale_x + shift_x);
				iy0 = (int)Math.Floor(-y1 * scale_y + shift_y);
				ix1 = (int)Math.Ceiling(x1 * scale_x + shift_x);
				iy1 = (int)Math.Ceiling(-y0 * scale_y + shift_y);
			}
		}

		/// <summary>
		/// stbtt_GetGlyphBitmapBoxSubpixel with a y-only grid-fit remap
		/// (SilkyNvg plans/crisp_text_yonly_autofit_gridfit.md). The remap operates
		/// in y-UP pixel space (baseline 0, ascenders positive) and MUST be
		/// monotonic non-decreasing — only the two box corners are remapped here,
		/// which bounds all remapped outline y values iff the map is monotonic.
		/// x is untouched (identical to the unmapped box).
		/// </summary>
		public void stbtt_GetGlyphBitmapBoxSubpixelYRemap(int glyph, float scale_x, float scale_y,
			float shift_x, float shift_y, stbtt_YPixelGridFitRemap y_remap,
			ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			if (y_remap == null)
			{
				stbtt_GetGlyphBitmapBoxSubpixel(glyph, scale_x, scale_y, shift_x, shift_y, ref ix0, ref iy0, ref ix1, ref iy1);
				return;
			}
			var x0 = 0;
			var y0 = 0;
			var x1 = 0;
			var y1 = 0;
			if (stbtt_GetGlyphBox(glyph, ref x0, ref y0, ref x1, ref y1) == 0)
			{
				ix0 = 0;
				iy0 = 0;
				ix1 = 0;
				iy1 = 0;
			}
			else
			{
				ix0 = (int)Math.Floor(x0 * scale_x + shift_x);
				iy0 = (int)Math.Floor(-y_remap(y1 * scale_y) + shift_y);
				ix1 = (int)Math.Ceiling(x1 * scale_x + shift_x);
				iy1 = (int)Math.Ceiling(-y_remap(y0 * scale_y) + shift_y);
			}
		}

		public void stbtt_GetGlyphBitmapBox(int glyph, float scale_x, float scale_y,
			ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			stbtt_GetGlyphBitmapBoxSubpixel(glyph, scale_x, scale_y, 0.0f, 0.0f, ref ix0, ref iy0, ref ix1, ref iy1);
		}

		public void stbtt_GetCodepointBitmapBoxSubpixel(int codepoint, float scale_x,
			float scale_y, float shift_x, float shift_y, ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			stbtt_GetGlyphBitmapBoxSubpixel(stbtt_FindGlyphIndex(codepoint), scale_x, scale_y, shift_x,
				shift_y, ref ix0, ref iy0, ref ix1, ref iy1);
		}

		public void stbtt_GetCodepointBitmapBox(int codepoint, float scale_x, float scale_y,
			ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			stbtt_GetCodepointBitmapBoxSubpixel(codepoint, scale_x, scale_y, 0.0f, 0.0f, ref ix0, ref iy0,
				ref ix1, ref iy1);
		}

		public FakePtr<byte> stbtt_GetGlyphBitmapSubpixel(float scale_x, float scale_y,
			float shift_x, float shift_y, int glyph, ref int width, ref int height, ref int xoff, ref int yoff)
		{
			var ix0 = 0;
			var iy0 = 0;
			var ix1 = 0;
			var iy1 = 0;
			var gbm = new Bitmap();
			stbtt_vertex[] vertices;
			var num_verts = stbtt_GetGlyphShape(glyph, out vertices);
			if (scale_x == 0)
				scale_x = scale_y;
			if (scale_y == 0)
			{
				if (scale_x == 0)
					return FakePtr<byte>.Null;
				scale_y = scale_x;
			}

			stbtt_GetGlyphBitmapBoxSubpixel(glyph, scale_x, scale_y, shift_x, shift_y, ref ix0, ref iy0, ref ix1,
				ref iy1);
			gbm.w = ix1 - ix0;
			gbm.h = iy1 - iy0;
			width = gbm.w;
			height = gbm.h;
			xoff = ix0;
			yoff = iy0;
			if (gbm.w != 0 && gbm.h != 0)
			{
				gbm.pixels = FakePtr<byte>.CreateWithSize(gbm.w * gbm.h);
				gbm.stride = gbm.w;
				gbm.stbtt_Rasterize(0.35f, vertices, num_verts, scale_x, scale_y, shift_x, shift_y, ix0, iy0, 1);
			}

			return gbm.pixels;
		}

		public FakePtr<byte> stbtt_GetGlyphBitmap(float scale_x, float scale_y, int glyph,
			ref int width, ref int height, ref int xoff, ref int yoff)
		{
			return stbtt_GetGlyphBitmapSubpixel(scale_x, scale_y, 0.0f, 0.0f, glyph, ref width, ref height,
				ref xoff, ref yoff);
		}

		public void stbtt_MakeGlyphBitmapSubpixel(FakePtr<byte> output, int out_w,
			int out_h, int out_stride, float scale_x, float scale_y, float shift_x, float shift_y, int glyph)
		{
			var ix0 = 0;
			var iy0 = 0;
			var ix1 = 0;
			var iy1 = 0;
			stbtt_vertex[] vertices;
			var num_verts = stbtt_GetGlyphShape(glyph, out vertices);
			var gbm = new Bitmap();
			stbtt_GetGlyphBitmapBoxSubpixel(glyph, scale_x, scale_y, shift_x, shift_y, ref ix0, ref iy0, ref ix1,
				ref iy1);
			gbm.pixels = output;
			gbm.w = out_w;
			gbm.h = out_h;
			gbm.stride = out_stride;

			if (gbm.w != 0 && gbm.h != 0)
				gbm.stbtt_Rasterize(0.35f, vertices, num_verts, scale_x, scale_y, shift_x, shift_y, ix0, iy0, 1);
		}

		/// <summary>
		/// stbtt_MakeGlyphBitmapSubpixel with a y-only grid-fit remap (SilkyNvg
		/// plans/crisp_text_yonly_autofit_gridfit.md). The remap is applied to the
		/// flattened outline in y-UP pixel space (see stbtt_YPixelGridFitRemap) and
		/// to the bitmap-box corners, so the output origin matches
		/// stbtt_GetGlyphBitmapBoxSubpixelYRemap. x (including shift_x subpixel
		/// phase) is untouched.
		/// </summary>
		public void stbtt_MakeGlyphBitmapSubpixelYRemap(FakePtr<byte> output, int out_w,
			int out_h, int out_stride, float scale_x, float scale_y, float shift_x, float shift_y, int glyph,
			stbtt_YPixelGridFitRemap y_remap)
		{
			var ix0 = 0;
			var iy0 = 0;
			var ix1 = 0;
			var iy1 = 0;
			stbtt_vertex[] vertices;
			var num_verts = stbtt_GetGlyphShape(glyph, out vertices);
			var gbm = new Bitmap();
			stbtt_GetGlyphBitmapBoxSubpixelYRemap(glyph, scale_x, scale_y, shift_x, shift_y, y_remap,
				ref ix0, ref iy0, ref ix1, ref iy1);
			gbm.pixels = output;
			gbm.w = out_w;
			gbm.h = out_h;
			gbm.stride = out_stride;

			if (gbm.w != 0 && gbm.h != 0)
				gbm.stbtt_Rasterize(0.35f, vertices, num_verts, scale_x, scale_y, shift_x, shift_y, ix0, iy0, 1, y_remap);
		}

		public void stbtt_MakeGlyphBitmap(FakePtr<byte> output, int out_w, int out_h,
			int out_stride, float scale_x, float scale_y, int glyph)
		{
			stbtt_MakeGlyphBitmapSubpixel(output, out_w, out_h, out_stride, scale_x, scale_y, 0.0f, 0.0f, glyph);
		}

		public FakePtr<byte> stbtt_GetCodepointBitmapSubpixel(float scale_x, float scale_y,
			float shift_x, float shift_y, int codepoint, ref int width, ref int height, ref int xoff, ref int yoff)
		{
			return stbtt_GetGlyphBitmapSubpixel(scale_x, scale_y, shift_x, shift_y,
				stbtt_FindGlyphIndex(codepoint), ref width, ref height, ref xoff, ref yoff);
		}

		public void stbtt_MakeCodepointBitmapSubpixelPrefilter(FakePtr<byte> output,
			int out_w, int out_h, int out_stride, float scale_x, float scale_y, float shift_x, float shift_y,
			int oversample_x, int oversample_y, ref float sub_x, ref float sub_y, int codepoint)
		{
			stbtt_MakeGlyphBitmapSubpixelPrefilter(output, out_w, out_h, out_stride, scale_x, scale_y, shift_x,
				shift_y, oversample_x, oversample_y, ref sub_x, ref sub_y, stbtt_FindGlyphIndex(codepoint));
		}

		public void stbtt_MakeCodepointBitmapSubpixel(FakePtr<byte> output, int out_w,
			int out_h, int out_stride, float scale_x, float scale_y, float shift_x, float shift_y, int codepoint)
		{
			stbtt_MakeGlyphBitmapSubpixel(output, out_w, out_h, out_stride, scale_x, scale_y, shift_x, shift_y,
				stbtt_FindGlyphIndex(codepoint));
		}

		public FakePtr<byte> stbtt_GetCodepointBitmap(float scale_x, float scale_y,
			int codepoint, ref int width, ref int height, ref int xoff, ref int yoff)
		{
			return stbtt_GetCodepointBitmapSubpixel(scale_x, scale_y, 0.0f, 0.0f, codepoint, ref width,
				ref height, ref xoff, ref yoff);
		}

		public void stbtt_MakeCodepointBitmap(FakePtr<byte> output, int out_w, int out_h,
			int out_stride, float scale_x, float scale_y, int codepoint)
		{
			stbtt_MakeCodepointBitmapSubpixel(output, out_w, out_h, out_stride, scale_x, scale_y, 0.0f, 0.0f,
				codepoint);
		}

		public void stbtt_MakeGlyphBitmapSubpixelPrefilter(FakePtr<byte> output, int out_w,
			int out_h, int out_stride, float scale_x, float scale_y, float shift_x, float shift_y, int prefilter_x,
			int prefilter_y, ref float sub_x, ref float sub_y, int glyph)
		{
			stbtt_MakeGlyphBitmapSubpixel(output, out_w - (prefilter_x - 1), out_h - (prefilter_y - 1),
				out_stride, scale_x, scale_y, shift_x, shift_y, glyph);
			if (prefilter_x > 1)
				stbtt__h_prefilter(output, out_w, out_h, out_stride, (uint)prefilter_x);
			if (prefilter_y > 1)
				stbtt__v_prefilter(output, out_w, out_h, out_stride, (uint)prefilter_y);
			sub_x = stbtt__oversample_shift(prefilter_x);
			sub_y = stbtt__oversample_shift(prefilter_y);
		}

		public byte[] stbtt_GetGlyphSDF(float scale, int glyph, int padding,
			byte onedge_value, float pixel_dist_scale, ref int width, ref int height, ref int xoff, ref int yoff)
		{
			var scale_x = scale;
			var scale_y = scale;
			var ix0 = 0;
			var iy0 = 0;
			var ix1 = 0;
			var iy1 = 0;
			var w = 0;
			var h = 0;
			byte[] data = null;
			if (scale == 0)
				return null;
			stbtt_GetGlyphBitmapBoxSubpixel(glyph, scale, scale, 0.0f, 0.0f, ref ix0, ref iy0, ref ix1, ref iy1);
			if (ix0 == ix1 || iy0 == iy1)
				return null;
			ix0 -= padding;
			iy0 -= padding;
			ix1 += padding;
			iy1 += padding;
			w = ix1 - ix0;
			h = iy1 - iy0;
			width = w;
			height = h;
			xoff = ix0;
			yoff = iy0;
			scale_y = -scale_y;
			{
				var x = 0;
				var y = 0;
				var i = 0;
				var j = 0;
				float[] precompute;
				stbtt_vertex[] verts;
				var num_verts = stbtt_GetGlyphShape(glyph, out verts);
				data = new byte[w * h];
				precompute = new float[num_verts];
				for (i = 0, j = num_verts - 1; i < num_verts; j = i++)
					if (verts[i].type == STBTT_vline)
					{
						var x0 = verts[i].x * scale_x;
						var y0 = verts[i].y * scale_y;
						var x1 = verts[j].x * scale_x;
						var y1 = verts[j].y * scale_y;
						var dist = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
						precompute[i] = dist == 0 ? 0.0f : 1.0f / dist;
					}
					else if (verts[i].type == STBTT_vcurve)
					{
						var x2 = verts[j].x * scale_x;
						var y2 = verts[j].y * scale_y;
						var x1 = verts[i].cx * scale_x;
						var y1 = verts[i].cy * scale_y;
						var x0 = verts[i].x * scale_x;
						var y0 = verts[i].y * scale_y;
						var bx = x0 - 2 * x1 + x2;
						var by = y0 - 2 * y1 + y2;
						var len2 = bx * bx + by * by;
						if (len2 != 0.0f)
							precompute[i] = 1.0f / (bx * bx + by * by);
						else
							precompute[i] = 0.0f;
					}
					else
					{
						precompute[i] = 0.0f;
					}

				for (y = iy0; y < iy1; ++y)
					for (x = ix0; x < ix1; ++x)
					{
						float val = 0;
						var min_dist = 999999.0f;
						var sx = x + 0.5f;
						var sy = y + 0.5f;
						var x_gspace = sx / scale_x;
						var y_gspace = sy / scale_y;
						var winding = stbtt__compute_crossings_x(x_gspace, y_gspace, num_verts, verts);
						for (i = 0; i < num_verts; ++i)
						{
							var x0 = verts[i].x * scale_x;
							var y0 = verts[i].y * scale_y;
							var dist2 = (x0 - sx) * (x0 - sx) + (y0 - sy) * (y0 - sy);
							if (dist2 < min_dist * min_dist)
								min_dist = (float)Math.Sqrt(dist2);
							if (verts[i].type == STBTT_vline)
							{
								var x1 = verts[i - 1].x * scale_x;
								var y1 = verts[i - 1].y * scale_y;
								var dist = (float)Math.Abs((double)((x1 - x0) * (y0 - sy) - (y1 - y0) * (x0 - sx))) *
										   precompute[i];
								if (dist < min_dist)
								{
									var dx = x1 - x0;
									var dy = y1 - y0;
									var px = x0 - sx;
									var py = y0 - sy;
									var t = -(px * dx + py * dy) / (dx * dx + dy * dy);
									if (t >= 0.0f && t <= 1.0f)
										min_dist = dist;
								}
							}
							else if (verts[i].type == STBTT_vcurve)
							{
								var x2 = verts[i - 1].x * scale_x;
								var y2 = verts[i - 1].y * scale_y;
								var x1 = verts[i].cx * scale_x;
								var y1 = verts[i].cy * scale_y;
								var box_x0 = (x0 < x1 ? x0 : x1) < x2 ? x0 < x1 ? x0 : x1 : x2;
								var box_y0 = (y0 < y1 ? y0 : y1) < y2 ? y0 < y1 ? y0 : y1 : y2;
								var box_x1 = (x0 < x1 ? x1 : x0) < x2 ? x2 : x0 < x1 ? x1 : x0;
								var box_y1 = (y0 < y1 ? y1 : y0) < y2 ? y2 : y0 < y1 ? y1 : y0;
								if (sx > box_x0 - min_dist && sx < box_x1 + min_dist && sy > box_y0 - min_dist &&
									sy < box_y1 + min_dist)
								{
									var num = 0;
									var ax = x1 - x0;
									var ay = y1 - y0;
									var bx = x0 - 2 * x1 + x2;
									var by = y0 - 2 * y1 + y2;
									var mx = x0 - sx;
									var my = y0 - sy;
									var res = new float[3];
									float px = 0;
									float py = 0;
									float t = 0;
									float it = 0;
									var a_inv = precompute[i];
									if (a_inv == 0.0)
									{
										var a = 3 * (ax * bx + ay * by);
										var b = 2 * (ax * ax + ay * ay) + (mx * bx + my * by);
										var c = mx * ax + my * ay;
										if (a == 0.0)
										{
											if (b != 0.0)
												res[num++] = -c / b;
										}
										else
										{
											var discriminant = b * b - 4 * a * c;
											if (discriminant < 0)
											{
												num = 0;
											}
											else
											{
												var root = (float)Math.Sqrt(discriminant);
												res[0] = (-b - root) / (2 * a);
												res[1] = (-b + root) / (2 * a);
												num = 2;
											}
										}
									}
									else
									{
										var b = 3 * (ax * bx + ay * by) * a_inv;
										var c = (2 * (ax * ax + ay * ay) + (mx * bx + my * by)) * a_inv;
										var d = (mx * ax + my * ay) * a_inv;
										num = stbtt__solve_cubic(b, c, d, res);
									}

									if (num >= 1 && res[0] >= 0.0f && res[0] <= 1.0f)
									{
										t = res[0];
										it = 1.0f - t;
										px = it * it * x0 + 2 * t * it * x1 + t * t * x2;
										py = it * it * y0 + 2 * t * it * y1 + t * t * y2;
										dist2 = (px - sx) * (px - sx) + (py - sy) * (py - sy);
										if (dist2 < min_dist * min_dist)
											min_dist = (float)Math.Sqrt(dist2);
									}

									if (num >= 2 && res[1] >= 0.0f && res[1] <= 1.0f)
									{
										t = res[1];
										it = 1.0f - t;
										px = it * it * x0 + 2 * t * it * x1 + t * t * x2;
										py = it * it * y0 + 2 * t * it * y1 + t * t * y2;
										dist2 = (px - sx) * (px - sx) + (py - sy) * (py - sy);
										if (dist2 < min_dist * min_dist)
											min_dist = (float)Math.Sqrt(dist2);
									}

									if (num >= 3 && res[2] >= 0.0f && res[2] <= 1.0f)
									{
										t = res[2];
										it = 1.0f - t;
										px = it * it * x0 + 2 * t * it * x1 + t * t * x2;
										py = it * it * y0 + 2 * t * it * y1 + t * t * y2;
										dist2 = (px - sx) * (px - sx) + (py - sy) * (py - sy);
										if (dist2 < min_dist * min_dist)
											min_dist = (float)Math.Sqrt(dist2);
									}
								}
							}
						}

						if (winding == 0)
							min_dist = -min_dist;
						val = onedge_value + pixel_dist_scale * min_dist;
						if (val < 0)
							val = 0;
						else if (val > 255)
							val = 255;
						data[(y - iy0) * w + (x - ix0)] = (byte)val;
					}
			}

			return data;
		}

		public byte[] stbtt_GetCodepointSDF(float scale, int codepoint, int padding,
			byte onedge_value, float pixel_dist_scale, ref int width, ref int height, ref int xoff, ref int yoff)
		{
			return stbtt_GetGlyphSDF(scale, stbtt_FindGlyphIndex(codepoint), padding, onedge_value,
				pixel_dist_scale, ref width, ref height, ref xoff, ref yoff);
		}

		public FakePtr<byte> stbtt_GetFontNameString(FontInfo font, ref int length, int platformID,
			int encodingID, int languageID, int nameID)
		{
			var i = 0;
			var count = 0;
			var stringOffset = 0;
			var fc = font.data;
			var offset = (uint)font.fontstart;
			var nm = stbtt__find_table(fc, offset, "name");
			if (nm == 0)
				return FakePtr<byte>.Null;
			count = ttUSHORT(fc + nm + 2);
			stringOffset = (int)(nm + ttUSHORT(fc + nm + 4));
			for (i = 0; i < count; ++i)
			{
				var loc = (uint)(nm + 6 + 12 * i);
				if (platformID == ttUSHORT(fc + loc + 0) && encodingID == ttUSHORT(fc + loc + 2) &&
					languageID == ttUSHORT(fc + loc + 4) && nameID == ttUSHORT(fc + loc + 6))
				{
					length = ttUSHORT(fc + loc + 8);
					return fc + stringOffset + ttUSHORT(fc + loc + 10);
				}
			}

			return FakePtr<byte>.Null;
		}

		public int stbtt_InitFont(byte[] data, int offset)
		{
			return stbtt_InitFont_internal(data, offset);
		}

		public int stbtt_GetUnitsPerEm()
		{
			return (int)ttUSHORT(this.data + this.head + 18);
		}

		/// <summary>
		/// Returns the sxHeight field from the OS/2 table (int16, font design units).
		/// sxHeight was added in OS/2 version 2 at byte offset 86. Returns 0 with
		/// available=false when the table/field is absent, truncated, or non-positive.
		/// A non-positive sxHeight is not a usable x-height metric per OpenType.
		/// </summary>
		public int stbtt_GetXHeight(out bool available)
		{
			var tab = (int)stbtt__find_table(this.data, (uint)this.fontstart, "OS/2");
			// Version 2 fields require bytes through sxHeight at offsets 86..87.
			if (tab == 0 || tab < 0 || tab > this.data.RemainingLength - 88)
			{
				available = false;
				return 0;
			}

			int version = ttUSHORT(this.data + tab);
			if (version < 2)
			{
				available = false;
				return 0;
			}

			int xHeight = ttSHORT(this.data + tab + 86);
			available = xHeight > 0;
			return available ? xHeight : 0;
		}

		/// <summary>
		/// Returns the sCapHeight field from the OS/2 table (int16, font units).
		/// sCapHeight was added in OS/2 version 2. Returns 0 if the OS/2 table
		/// is missing or is version 0/1 (which don't contain sCapHeight).
		/// </summary>
		public int stbtt_GetCapHeight(out bool available)
		{
			var tab = (int)stbtt__find_table(this.data, (uint)this.fontstart, "OS/2");
			if (tab == 0)
			{
				available = false;
				return 0;
			}
			int version = ttUSHORT(this.data + tab); // OS/2 table version at offset 0
			if (version < 2)
			{
				available = false;
				return 0;
			}
			available = true;
			return ttSHORT(this.data + tab + 88);
		}
	}
}