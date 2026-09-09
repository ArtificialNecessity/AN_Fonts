using System.Collections.Generic;
using System.IO;
using StbTrueTypeSharp;
using Xunit;
using static StbTrueTypeSharp.Common;

namespace Variations.Tests
{
	/// <summary>
	/// cmap encoding-record ranking and per-subtable key translation
	/// (plans/BUGFIX_cmap_subtable_selection_mac_platform.md §3.6 / §4). The Quartz fixtures are the real producer's
	/// bytes (macOS PDFContext embeds ONLY a (1,0) format 6 subtable); the Synthetic.* fixtures are OFL twins built by
	/// Fonts/generate_cmap_fixtures.py for the cases no real font in the repo exercises.
	/// </summary>
	public class CmapSubtableSelectionTests
	{
		private const string QuartzCourier = "QuartzPdfSubset.Courier.maccmap-only.ttf";
		private const string QuartzAvenirNextBold = "QuartzPdfSubset.AvenirNext-Bold.maccmap-only.ttf";

		/// <summary>Courier subset: the 31 Mac Roman (== ASCII here) codes that map to a non-zero GID (spec §1).</summary>
		private static readonly Dictionary<int, int> QuartzCourierExpectedGlyphByCodepoint = new Dictionary<int, int>
		{
			{ 0x20, 1 }, { 0x2c, 2 }, { 0x2d, 3 }, { 0x2e, 4 }, { 'D', 5 }, { 'G', 6 }, { 'H', 7 }, { 'N', 8 },
			{ 'a', 9 }, { 'b', 10 }, { 'c', 11 }, { 'd', 12 }, { 'e', 13 }, { 'f', 14 }, { 'g', 15 }, { 'h', 16 }, { 'i', 17 },
			{ 'k', 18 }, { 'l', 19 }, { 'm', 20 }, { 'n', 21 }, { 'o', 22 }, { 'p', 23 }, { 'r', 24 }, { 's', 25 }, { 't', 26 },
			{ 'u', 27 }, { 'v', 28 }, { 'w', 29 }, { 'x', 30 }, { 'y', 31 },
		};

		// ── 1. Mac-only fonts load ───────────────────────────────────────────────────────────────────────────────────

		[Theory]
		[InlineData(QuartzCourier, 2048, 32)]
		[InlineData(QuartzAvenirNextBold, 1000, 37)]
		[InlineData("Synthetic.Inter.maccmap-only.ttf", 2816, 7)]
		public void MacRomanOnlySubset_InitFontSucceeds(string fileName, int expectedUnitsPerEm, int expectedNumGlyphs)
		{
			FontInfo font = VariationFixtureCatalog.LoadFont(fileName);   // asserts stbtt_InitFont != 0
			Assert.Equal(FontInfo.CmapSubtableKeyKind.MacRoman, font.cmapSubtableKeyKind);
			Assert.NotEqual(0, font.index_map);
			Assert.Equal(6, ttUSHORT(font.data + (uint)font.index_map));   // the Quartz/synthetic subtable is format 6
			Assert.Equal(expectedNumGlyphs, font.numGlyphs);
			Assert.Equal(expectedUnitsPerEm, ttUSHORT(font.data + (uint)font.head + 18));
		}

		// ── 2. ASCII codes resolve to the listed GIDs; unmapped ASCII is 0; every hit has an outline (except space) ──

		[Fact]
		public void MacRomanOnlySubset_AsciiCodepointsResolve()
		{
			FontInfo font = VariationFixtureCatalog.LoadFont(QuartzCourier);
			foreach (var pair in QuartzCourierExpectedGlyphByCodepoint)
			{
				int glyph = font.stbtt_FindGlyphIndex(pair.Key);
				Assert.True(pair.Value == glyph, $"U+{pair.Key:X4}: expected GID {pair.Value}, got {glyph}");
				int vertexCount = font.stbtt_GetGlyphShape(glyph, out _);
				if (pair.Key == 0x20)
					Assert.Equal(0, vertexCount);
				else
					Assert.True(vertexCount > 0, $"U+{pair.Key:X4} GID {glyph}: empty outline");
			}
			foreach (int unmapped in new[] { 'A', 'z', 'j', 'q', '0', 0x7F })
				Assert.Equal(0, font.stbtt_FindGlyphIndex(unmapped));
		}

		// ── 3. Non-ASCII goes through the Mac Roman table, NOT identity ────────────────────────────────────────────

		[Theory]
		[InlineData(QuartzAvenirNextBold, 36)]
		[InlineData("Synthetic.Inter.maccmap-only.ttf", 6)]
		public void MacRomanOnlySubset_NonAsciiCodepointTranslates(string fileName, int expectedFiGlyph)
		{
			FontInfo font = VariationFixtureCatalog.LoadFont(fileName);
			// Mac Roman byte 0xDE is 'fi' (U+FB01). The fixture maps 0xDE -> expectedFiGlyph.
			Assert.Equal(expectedFiGlyph, font.stbtt_FindGlyphIndex(0xFB01));
			// U+00DE (Þ) is what a naive identity read would look up — it has NO Mac Roman byte and must miss.
			Assert.Equal(0, font.stbtt_FindGlyphIndex(0x00DE));
			// Codepoints outside Mac Roman entirely never touch the subtable.
			Assert.Equal(0, font.stbtt_FindGlyphIndex(0x4E2D));
			Assert.Equal(0, font.stbtt_FindGlyphIndex(0x1F600));
		}

		[Fact]
		public void MacRomanEncoding_ReverseTable_IsConsistentAndCarriesBothCurrencyCodepoints()
		{
			for (int b = 0; b < 0x100; b++)
				Assert.Equal(b, MacRomanEncoding.UnicodeToMacRomanByte(MacRomanEncoding.MacRomanByteToUnicode((byte)b)));
			Assert.Equal(0xDB, MacRomanEncoding.UnicodeToMacRomanByte(0x20AC));   // EURO SIGN (Mac OS 8.5+)
			Assert.Equal(0xDB, MacRomanEncoding.UnicodeToMacRomanByte(0x00A4));   // CURRENCY SIGN (pre-8.5 / PDF / fontTools)
			Assert.Equal(0xDE, MacRomanEncoding.UnicodeToMacRomanByte(0xFB01));   // fi
			Assert.Equal(0xF0, MacRomanEncoding.UnicodeToMacRomanByte(0xF8FF));   // Apple logo
			Assert.Equal(MacRomanEncoding.NoMacRomanByte, MacRomanEncoding.UnicodeToMacRomanByte(0x00DE));
			Assert.Equal(MacRomanEncoding.NoMacRomanByte, MacRomanEncoding.UnicodeToMacRomanByte(-1));
		}

		// ── 4. Unicode beats Mac when both are present ─────────────────────────────────────────────────────────────

		[Fact]
		public void SubtableRanking_PrefersUnicodeOverMac()
		{
			// Roboto-Regular carries (0,3) fmt4 + (1,0) fmt6 + (3,1) fmt4 (fontTools probe, spec §3.5).
			string robotoPath = Path.Combine(VariationFixtureCatalog.FontsDirectory, "..", "..", "..", "..", "..", "GposKerning.Tests", "Roboto-Regular.ttf");
			if (!File.Exists(robotoPath))
				robotoPath = Path.Combine(VariationFixtureCatalog.FontsDirectory, "..", "..", "..", "..", "..", "..", "GposKerning.Tests", "Roboto-Regular.ttf");
			Assert.True(File.Exists(robotoPath), "Roboto-Regular.ttf not found relative to test output: " + robotoPath);
			var font = new FontInfo();
			Assert.NotEqual(0, font.stbtt_InitFont(File.ReadAllBytes(robotoPath), 0));
			Assert.Equal(FontInfo.CmapSubtableKeyKind.Unicode, font.cmapSubtableKeyKind);
			Assert.Equal(ExpectedSubtableOffset(font, 3, 1), font.index_map);
			Assert.Equal(4, ttUSHORT(font.data + (uint)font.index_map));
			Assert.NotEqual(0, font.stbtt_FindGlyphIndex(0x20AC));   // € is in Roboto's Unicode table; would ALSO be in Mac 0xDB
		}

		[Theory]
		[InlineData("Inter.no-var.subset.ttf")]                 // (0,3)(0,4)(3,1)(3,10) -> (3,10) fmt12
		[InlineData("SourceSerif4Variable-Roman.ttf")]
		[InlineData("NotoSansSymbols-VariableFont_wght.ttf")]
		public void SubtableRanking_PrefersMicrosoftFullRepertoire(string fileName)
		{
			FontInfo font = VariationFixtureCatalog.LoadFont(fileName);
			Assert.Equal(FontInfo.CmapSubtableKeyKind.Unicode, font.cmapSubtableKeyKind);
			Assert.Equal(ExpectedSubtableOffset(font, 3, 10), font.index_map);
			Assert.Equal(12, ttUSHORT(font.data + (uint)font.index_map));
		}

		[Theory]
		[InlineData("Fraunces.instanced.default.ttf")]          // (0,3)(3,1) only -> (3,1) fmt4
		[InlineData("AdobeVFPrototype.otf")]
		public void SubtableRanking_FallsBackToMicrosoftBmp(string fileName)
		{
			FontInfo font = VariationFixtureCatalog.LoadFont(fileName);
			Assert.Equal(FontInfo.CmapSubtableKeyKind.Unicode, font.cmapSubtableKeyKind);
			Assert.Equal(ExpectedSubtableOffset(font, 3, 1), font.index_map);
		}

		// ── 5. A (0,5) format 14 record must never be chosen as the primary table ─────────────────────────────────

		[Fact]
		public void SubtableRanking_SkipsUnreadableFormat14()
		{
			FontInfo font = VariationFixtureCatalog.LoadFont("Synthetic.Inter.uvs-plus-bmp.ttf");
			Assert.Equal(FontInfo.CmapSubtableKeyKind.Unicode, font.cmapSubtableKeyKind);
			Assert.Equal(ExpectedSubtableOffset(font, 3, 1), font.index_map);
			Assert.Equal(4, ttUSHORT(font.data + (uint)font.index_map));
			AssertInterSubsetMapping(font);
		}

		// ── Format 8 / 10 readers (approved additions, spec §3.5/§3.7) ─────────────────────────────────────────────

		[Theory]
		[InlineData("Synthetic.Inter.cmap-fmt10-only.ttf", 10)]
		[InlineData("Synthetic.Inter.cmap-fmt8-only.ttf", 8)]
		public void Format8And10_Readers_ResolveTheInterSubset(string fileName, int expectedFormat)
		{
			FontInfo font = VariationFixtureCatalog.LoadFont(fileName);
			Assert.Equal(FontInfo.CmapSubtableKeyKind.Unicode, font.cmapSubtableKeyKind);
			Assert.Equal(expectedFormat, ttUSHORT(font.data + (uint)font.index_map));
			AssertInterSubsetMapping(font);
		}

		// ── Regression: every fixture in Fonts/ still loads and maps 'a' ───────────────────────────────────────────

		[Fact]
		public void EveryFixtureInFontsDirectory_StillLoads()
		{
			foreach (string path in Directory.GetFiles(VariationFixtureCatalog.FontsDirectory, "*.?tf"))
			{
				var font = new FontInfo();
				Assert.True(font.stbtt_InitFont(File.ReadAllBytes(path), 0) != 0, "stbtt_InitFont failed for " + Path.GetFileName(path));
				Assert.True(font.stbtt_FindGlyphIndex('a') != 0, "'a' unmapped in " + Path.GetFileName(path));
			}
		}

		// ── helpers ───────────────────────────────────────────────────────────────────────────────────────────────

		/// <summary>Inter.no-var.subset maps exactly a l n s t -> GIDs 1..5 in glyph order; nothing else.</summary>
		private static void AssertInterSubsetMapping(FontInfo font)
		{
			Assert.Equal(1, font.stbtt_FindGlyphIndex('a'));
			Assert.Equal(2, font.stbtt_FindGlyphIndex('l'));
			Assert.Equal(3, font.stbtt_FindGlyphIndex('n'));
			Assert.Equal(4, font.stbtt_FindGlyphIndex('s'));
			Assert.Equal(5, font.stbtt_FindGlyphIndex('t'));
			Assert.Equal(0, font.stbtt_FindGlyphIndex('b'));
			Assert.Equal(0, font.stbtt_FindGlyphIndex('m'));     // inside the a..t range but unmapped: trimmed-array 0 entry
			Assert.Equal(0, font.stbtt_FindGlyphIndex('u'));     // just past the range
			Assert.Equal(0, font.stbtt_FindGlyphIndex(0x1F600));
		}

		/// <summary>Absolute offset of the (platformId, encodingId) subtable by walking the encoding records directly.</summary>
		private static int ExpectedSubtableOffset(FontInfo font, int platformId, int encodingId)
		{
			uint cmap = stbtt__find_table(font.data, (uint)font.fontstart, "cmap");
			int numTables = ttUSHORT(font.data + cmap + 2);
			for (int i = 0; i < numTables; i++)
			{
				uint rec = cmap + 4 + 8 * (uint)i;
				if (ttUSHORT(font.data + rec) == platformId && ttUSHORT(font.data + rec + 2) == encodingId)
					return (int)(cmap + ttULONG(font.data + rec + 4));
			}
			Assert.Fail($"no ({platformId},{encodingId}) encoding record");
			return 0;
		}
	}
}