using System;
using System.Collections.Generic;
using StbTrueTypeSharp;
using StbTrueTypeSharp.Variations;
using Xunit;

namespace Variations.Tests
{
	/// <summary>
	/// Part B cross-oracle (README §8): Adobe ships the SAME design as CFF2 (AdobeVFPrototype.otf) and as glyf+gvar
	/// (AdobeVFPrototype.ttf). At any design position the two outlines must agree up to cubic<->quadratic authoring
	/// tolerance — compared as glyph BOUNDING BOXES (point lists are not comparable across outline formats). This is
	/// independent of fontTools: it cross-checks our CFF2 blend path against our (already fixture-proven) gvar path.
	/// </summary>
	public class Cff2GlyfCrossOracleTests
	{
		// Control-polygon bbox of a quadratic conversion of a cubic can differ from the cubic's control bbox by a
		// few units (off-curve points move); 6 units is generous on a 1000-upm face and still catches any real error
		// (a wrong scalar or operand layout moves whole contours by tens of units).
		private const int BoundsToleranceFontUnits = 6;

		public static IEnumerable<object[]> DesignPositions()
		{
			yield return new object[] { "default" };
			yield return new object[] { "wght200" };
			yield return new object[] { "wght600" };
			yield return new object[] { "wght900" };
			yield return new object[] { "CNTR100" };
			yield return new object[] { "wght900_CNTR50" };
			yield return new object[] { "wght900_CNTR100" };
		}

		[Theory]
		[MemberData(nameof(DesignPositions))]
		public void Cff2OutlineBounds_MatchGlyfTwinBounds_AtSamePosition(string position)
		{
			FontInfo cff2 = VariationFixtureCatalog.LoadFont("AdobeVFPrototype.otf");
			FontInfo glyf = VariationFixtureCatalog.LoadFont("AdobeVFPrototype.ttf");
			Assert.True(cff2.isCff2, "AdobeVFPrototype.otf did not load as CFF2");
			Assert.True(cff2.stbtt_HasVariations, cff2.stbtt_VariationParseFailure.ToString());
			Assert.True(glyf.stbtt_HasVariations, glyf.stbtt_VariationParseFailure.ToString());
			var axisValues = VariationFixtureCatalog.DesignPositionOf($"x.instanced.{position}.ttf");
			var cff2Coords = cff2.stbtt_NormalizeVariationRequest(axisValues);
			var glyfCoords = glyf.stbtt_NormalizeVariationRequest(axisValues);
			Assert.Equal(cff2.numGlyphs, glyf.numGlyphs);

			var failures = new List<string>();
			int compared = 0;
			for (int glyph = 0; glyph < cff2.numGlyphs; glyph++)
			{
				bool cff2HasBox = cff2.stbtt_GetGlyphBoxVar(glyph, cff2Coords, out int cx0, out int cy0, out int cx1, out int cy1);
				bool glyfHasBox = glyf.stbtt_GetGlyphBoxVar(glyph, glyfCoords, out int gx0, out int gy0, out int gx1, out int gy1);
				if (!cff2HasBox && !glyfHasBox) continue;
				compared++;
				if (cff2HasBox != glyfHasBox) { failures.Add($"glyph {glyph}: one side empty (cff2 {cff2HasBox}, glyf {glyfHasBox})"); continue; }
				int maxDelta = Math.Max(Math.Max(Math.Abs(cx0 - gx0), Math.Abs(cy0 - gy0)), Math.Max(Math.Abs(cx1 - gx1), Math.Abs(cy1 - gy1)));
				if (maxDelta > BoundsToleranceFontUnits)
					failures.Add($"glyph {glyph}: cff2 box ({cx0},{cy0})-({cx1},{cy1}) vs glyf ({gx0},{gy0})-({gx1},{gy1}) max|Δ|={maxDelta}");
			}
			Assert.True(compared > 40, $"only {compared} glyphs compared");
			Assert.True(failures.Count == 0, $"{position}: {failures.Count} glyph bbox mismatches over {compared} glyphs:\n" + string.Join("\n", failures));
		}
	}
}