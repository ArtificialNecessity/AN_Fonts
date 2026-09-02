using System;
using System.Collections.Generic;
using StbTrueTypeSharp;
using StbTrueTypeSharp.Variations;
using Xunit;

namespace Variations.Tests
{
	/// <summary>HVAR advances / phantom-derived lsb vs the instanced twin's hmtx; MVAR vs its hhea (README §5).</summary>
	public class VariationMetricsTests
	{
		[Theory]
		[MemberData(nameof(VariationFixtureCatalog.AllInstancedFixtures), MemberType = typeof(VariationFixtureCatalog))]
		public void EveryGlyphPhantomDerivedAdvance_MatchesInstancedTwinHmtx_Exactly(string instancedFileName)
		{
			// fontTools' instancer DROPS HVAR for fully-pinned glyf fonts and writes the gvar PHANTOM-derived advance
			// into hmtx (instancer/__init__.py _instantiateVHVAR), so the fixture pins the phantom math exactly.
			FontInfo variableFont = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(instancedFileName));
			FontInfo instancedTwin = VariationFixtureCatalog.LoadFont(instancedFileName);
			var coords = variableFont.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(instancedFileName));
			var failures = new List<string>();
			for (int glyph = 0; glyph < variableFont.numGlyphs; glyph++)
			{
				int twinAdvance = 0, twinLsb = 0;
				Assert.True(variableFont.stbtt__TryGetPhantomDerivedAdvanceVar(glyph, coords, out int oursAdvance), $"glyph {glyph}: no phantom data");
				instancedTwin.stbtt_GetGlyphHMetrics(glyph, ref twinAdvance, ref twinLsb);
				if (oursAdvance != twinAdvance)
					failures.Add($"glyph {glyph}: advance ours={oursAdvance} twin={twinAdvance}");
				if (failures.Count > 40) break;
			}
			Assert.True(failures.Count == 0, $"{instancedFileName}: {failures.Count}+ advance mismatches:\n" + string.Join("\n", failures));
		}

		[Theory]
		[MemberData(nameof(VariationFixtureCatalog.AllInstancedFixtures), MemberType = typeof(VariationFixtureCatalog))]
		public void ProductionAdvance_HvarWins_AndMatchesTwinWhereFontIsSelfConsistent(string instancedFileName)
		{
			// Production rule: HVAR wins (spec/FreeType/HarfBuzz). Where the font's HVAR agrees with its phantoms the
			// fixture is an exact oracle. Where they DISAGREE the font itself is inconsistent; we record those glyphs
			// and require them to be the known set (Inter.var.subset '.notdef': HVAR -156/+260 vs phantom 0).
			FontInfo variableFont = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(instancedFileName));
			FontInfo instancedTwin = VariationFixtureCatalog.LoadFont(instancedFileName);
			var coords = variableFont.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(instancedFileName));
			var failures = new List<string>();
			var selfInconsistentGlyphs = new List<int>();
			for (int glyph = 0; glyph < variableFont.numGlyphs; glyph++)
			{
				int hvarAdvance = 0, lsb = 0, twinAdvance = 0, twinLsb = 0;
				variableFont.stbtt_GetGlyphHMetricsVar(glyph, coords, ref hvarAdvance, ref lsb);
				variableFont.stbtt__TryGetPhantomDerivedAdvanceVar(glyph, coords, out int phantomAdvance);
				instancedTwin.stbtt_GetGlyphHMetrics(glyph, ref twinAdvance, ref twinLsb);
				if (hvarAdvance != phantomAdvance) { selfInconsistentGlyphs.Add(glyph); continue; }
				if (hvarAdvance != twinAdvance)
					failures.Add($"glyph {glyph}: advance ours={hvarAdvance} twin={twinAdvance}");
			}
			Assert.True(failures.Count == 0, $"{instancedFileName}: {failures.Count} advance mismatches:\n" + string.Join("\n", failures));
			// Inter '.notdef' HVAR regions are all wght-peaked: the disagreement exists only when wght is off default.
			bool isInterWeightOffDefault = instancedFileName.StartsWith("Inter.", StringComparison.Ordinal) && coords.GetF2Dot14(0) != 0;
			var expectedInconsistent = isInterWeightOffDefault ? new List<int> { 0 } : new List<int>();
			Assert.Equal(expectedInconsistent, selfInconsistentGlyphs);
		}

		[Theory]
		[MemberData(nameof(VariationFixtureCatalog.AllInstancedFixtures), MemberType = typeof(VariationFixtureCatalog))]
		public void EveryGlyphLeftSideBearing_MatchesInstancedTwinHmtx_WithinOneUnit(string instancedFileName)
		{
			// fontTools writes lsb = otRound(xMin_varied - leftPhantomX_varied) into the instanced hmtx.
			FontInfo variableFont = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(instancedFileName));
			FontInfo instancedTwin = VariationFixtureCatalog.LoadFont(instancedFileName);
			var coords = variableFont.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(instancedFileName));
			var failures = new List<string>();
			for (int glyph = 0; glyph < variableFont.numGlyphs; glyph++)
			{
				int oursAdvance = 0, oursLsb = 0, twinAdvance = 0, twinLsb = 0;
				variableFont.stbtt_GetGlyphHMetricsVar(glyph, coords, ref oursAdvance, ref oursLsb);
				instancedTwin.stbtt_GetGlyphHMetrics(glyph, ref twinAdvance, ref twinLsb);
				if (Math.Abs(oursLsb - twinLsb) > 1)
					failures.Add($"glyph {glyph}: lsb ours={oursLsb} twin={twinLsb}");
				if (failures.Count > 40) break;
			}
			Assert.True(failures.Count == 0, $"{instancedFileName}: {failures.Count}+ lsb mismatches:\n" + string.Join("\n", failures));
		}

		[Theory]
		[MemberData(nameof(VariationFixtureCatalog.AllInstancedFixtures), MemberType = typeof(VariationFixtureCatalog))]
		public void VerticalMetrics_MatchInstancedTwinHhea(string instancedFileName)
		{
			FontInfo variableFont = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(instancedFileName));
			FontInfo instancedTwin = VariationFixtureCatalog.LoadFont(instancedFileName);
			var coords = variableFont.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(instancedFileName));
			variableFont.stbtt_GetFontVMetricsVar(coords, out int ascent, out int descent, out int lineGap);
			instancedTwin.stbtt_GetFontVMetrics(out int twinAscent, out int twinDescent, out int twinLineGap);
			Assert.True(ascent == twinAscent && descent == twinDescent && lineGap == twinLineGap,
				$"{instancedFileName}: hhea ours=({ascent},{descent},{lineGap}) twin=({twinAscent},{twinDescent},{twinLineGap})");
		}
	}
}