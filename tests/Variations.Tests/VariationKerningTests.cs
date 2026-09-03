using StbTrueTypeSharp;
using StbTrueTypeSharp.Variations;
using Xunit;

namespace Variations.Tests
{
	/// <summary>
	/// Part C oracle (plans/font_variable_support_fvar_avar_gvar_hvar.md; README §7): GPOS pair kerning at a
	/// variation instance = static xAdvance + otRound(GDEF ItemVariationStore delta via the record's VariationIndex).
	/// fontTools' instancer bakes exactly that into the instanced twin's static GPOS, so for EVERY pair:
	///     vf.stbtt_GetGlyphKernAdvanceVar(g1, g2, P)  ==  twin.stbtt_GetGlyphKernAdvance(g1, g2)
	/// in font units, exact integer equality (no tolerance — a systematic ±1 means a rounding-order divergence).
	/// </summary>
	public class VariationKerningTests
	{
		// Glyph-id range scanned per fixture pair. Kern coverage in these fonts sits in the low ids (Latin);
		// 400² pairs × 21 fixtures is a few million cheap binary searches.
		private const int ScannedGlyphCount = 400;

		[Theory]
		[InlineData("NotoSansSymbols-VariableFont_wght.ttf")]
		[InlineData("RobotoFlex-VariableFont_GRAD,XOPQ,XTRA,YOPQ,YTAS,YTDE,YTFI,YTLC,YTUC,opsz,slnt,wdth,wght.ttf")]
		[InlineData("Inter.var.subset.ttf")] // all three VFs carry a GDEF 1.3 ItemVariationStore (Inter's drives its wght900 kern delta)
		public void GdefItemVariationStore_IsParsedWhenPresent(string variableFontFile)
		{
			FontInfo vf = VariationFixtureCatalog.LoadFont(variableFontFile);
			Assert.True(vf.stbtt_HasVariations, vf.stbtt_VariationParseFailure.ToString());
			var tables = vf.stbtt__GetVariationTables();
			Assert.True(tables.GdefItemVariationStore != null, "GDEF ItemVariationStore expected but not parsed: " + vf.stbtt_VariationParseFailure);
			Assert.True(tables.GdefItemVariationStore.RegionCount > 0, "GDEF ItemVariationStore parsed with no regions");
		}

		[Theory]
		[MemberData(nameof(VariationFixtureCatalog.AllInstancedFixtures), MemberType = typeof(VariationFixtureCatalog))]
		public void KernAdvanceAtInstance_MatchesInstancedTwinGpos(string instancedFileName)
		{
			FontInfo vf = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(instancedFileName));
			FontInfo twin = VariationFixtureCatalog.LoadFont(instancedFileName);
			Assert.True(vf.stbtt_HasVariations, vf.stbtt_VariationParseFailure.ToString());
			FontVariationNormalizedCoordinates coords = vf.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(instancedFileName));

			int glyphCount = System.Math.Min(ScannedGlyphCount, System.Math.Min(vf.numGlyphs, twin.numGlyphs));
			int kernedPairs = 0, variedPairs = 0, mismatches = 0;
			string? firstMismatch = null;
			for (int g1 = 0; g1 < glyphCount; g1++)
			for (int g2 = 0; g2 < glyphCount; g2++)
			{
				int expected = twin.stbtt_GetGlyphKernAdvance(g1, g2);
				int staticValue = vf.stbtt_GetGlyphKernAdvance(g1, g2);
				int actual = vf.stbtt_GetGlyphKernAdvanceVar(g1, g2, coords);
				if (expected != 0 || staticValue != 0) kernedPairs++;
				if (expected != staticValue) variedPairs++;
				if (actual != expected)
				{
					mismatches++;
					firstMismatch ??= $"pair (g{g1}, g{g2}): twin {expected}, ours {actual} (static {staticValue})";
				}
			}

			Assert.True(mismatches == 0,
				$"{instancedFileName}: {mismatches} mismatching pairs of {kernedPairs} kerned ({variedPairs} vary at this instance). First: {firstMismatch}");
		}

		[Fact]
		public void DefaultInstance_IsByteIdenticalToStaticKernPath()
		{
			FontInfo vf = VariationFixtureCatalog.LoadFont("RobotoFlex-VariableFont_GRAD,XOPQ,XTRA,YOPQ,YTAS,YTDE,YTFI,YTLC,YTUC,opsz,slnt,wdth,wght.ttf");
			for (int g1 = 0; g1 < ScannedGlyphCount; g1++)
			for (int g2 = 0; g2 < ScannedGlyphCount; g2++)
				Assert.Equal(vf.stbtt_GetGlyphKernAdvance(g1, g2), vf.stbtt_GetGlyphKernAdvanceVar(g1, g2, FontVariationNormalizedCoordinates.Default));
		}
	}
}