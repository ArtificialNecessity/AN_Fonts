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
			// CFF2 has no phantom points (README §8.2) — the phantom path does not exist for it; HVAR parity is covered
			// by ProductionAdvance_HvarWins_AndMatchesTwinWhereFontIsSelfConsistent.
			if (variableFont.isCff2) return;
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

		/// <summary>
		/// fontTools' _instantiateVHVAR writes hmtx = default + Python round(HVAR delta) — HALF-TO-EVEN — while the
		/// renderer convention we implement is otRound (half-up; HarfBuzz roundf, FreeType FT_fixedToInt = Chrome).
		/// The two differ only when the HVAR delta is an exact .5 tie (CFF2 fixtures at CNTR 50: 7 glyphs). glyf
		/// fixtures never hit this because fontTools drops HVAR there and writes phantom-derived otRound advances.
		/// Accepts a twin advance iff it is exactly that tie-break of the same delta — anything else is a real error.
		/// </summary>
		private static bool IsFontToolsHalfEvenTie(FontInfo variableFont, int glyph, in FontVariationNormalizedCoordinates coords, int twinAdvance)
		{
			var hvar = variableFont.stbtt__GetVariationTables()?.Hvar;
			if (hvar == null) return false;
			int defaultAdvance = 0, defaultLsb = 0;
			variableFont.stbtt_GetGlyphHMetrics(glyph, ref defaultAdvance, ref defaultLsb);
			double delta = hvar.ComputeAdvanceWidthDelta(glyph, coords);
			bool isExactTie = Math.Abs(delta - Math.Floor(delta) - 0.5) < 1e-9;
			return isExactTie && twinAdvance == defaultAdvance + (int)Math.Round(delta, MidpointRounding.ToEven);
		}

		[Theory]
		[MemberData(nameof(VariationFixtureCatalog.AllInstancedFixtures), MemberType = typeof(VariationFixtureCatalog))]
		public void ProductionAdvance_HvarWins_AndMatchesTwinWhereFontIsSelfConsistent(string instancedFileName)
		{
			// Production rule: HVAR wins (spec/FreeType/HarfBuzz). Where the font's HVAR agrees with its phantoms the
			// fixture is an exact oracle. Where they DISAGREE the font itself is inconsistent; we record those glyphs
			// and require them to be the known set (Inter.var.subset '.notdef': HVAR -156/+260 vs phantom 0).
			// CFF2 (Part B): no phantom points exist, HVAR is the ONLY advance source, and fontTools writes exactly
			// default + otRound(HVAR delta) into the twin's hmtx — every glyph must match, no self-consistency set.
			FontInfo variableFont = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(instancedFileName));
			FontInfo instancedTwin = VariationFixtureCatalog.LoadFont(instancedFileName);
			var coords = variableFont.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(instancedFileName));
			var failures = new List<string>();
			var selfInconsistentGlyphs = new List<int>();
			for (int glyph = 0; glyph < variableFont.numGlyphs; glyph++)
			{
				int hvarAdvance = 0, lsb = 0, twinAdvance = 0, twinLsb = 0;
				variableFont.stbtt_GetGlyphHMetricsVar(glyph, coords, ref hvarAdvance, ref lsb);
				instancedTwin.stbtt_GetGlyphHMetrics(glyph, ref twinAdvance, ref twinLsb);
				if (!variableFont.isCff2)
				{
					variableFont.stbtt__TryGetPhantomDerivedAdvanceVar(glyph, coords, out int phantomAdvance);
					if (hvarAdvance != phantomAdvance) { selfInconsistentGlyphs.Add(glyph); continue; }
				}
				if (hvarAdvance != twinAdvance && !IsFontToolsHalfEvenTie(variableFont, glyph, coords, twinAdvance))
					failures.Add($"glyph {glyph}: advance ours={hvarAdvance} twin={twinAdvance}");
			}
			Assert.True(failures.Count == 0, $"{instancedFileName}: {failures.Count} advance mismatches:\n" + string.Join("\n", failures));
			// Inter '.notdef' HVAR regions are all wght-peaked: the disagreement exists only when wght is off default.
			bool isInterWeightOffDefault = instancedFileName.StartsWith("Inter.", StringComparison.Ordinal) && coords.GetF2Dot14(0) != 0;
			var expectedInconsistent = isInterWeightOffDefault ? new List<int> { 0 } : new List<int>();
			// Fraunces (2026-09-03): 'gcircumflex' (539) and 'gmacron' (544) — composites whose gvar carries NO phantom
			// advance delta while HVAR does (−117.6 at wght 400; fontTools' phantom-derived twin hmtx stays at the default
			// 1225). Same self-inconsistency class as Inter '.notdef'; production (HVAR wins) matches Chrome/FreeType.
			// The set is derived, not hard-coded per position: a glyph is expected to disagree exactly when its HVAR
			// delta rounds to non-zero at these coords, so any THIRD glyph appearing is still a failure.
			if (instancedFileName.StartsWith("Fraunces.", StringComparison.Ordinal))
			{
				var hvar = variableFont.stbtt__GetVariationTables()?.Hvar;
				Assert.NotNull(hvar);
				foreach (int knownSelfInconsistentGlyph in new[] { 539, 544 })
				{
					int phantomAdvance = 0;
					variableFont.stbtt__TryGetPhantomDerivedAdvanceVar(knownSelfInconsistentGlyph, coords, out phantomAdvance);
					int defaultAdvance = 0, defaultLsb = 0;
					variableFont.stbtt_GetGlyphHMetrics(knownSelfInconsistentGlyph, ref defaultAdvance, ref defaultLsb);
					if (defaultAdvance + FontVariationNormalization.OtRound(hvar.ComputeAdvanceWidthDelta(knownSelfInconsistentGlyph, coords)) != phantomAdvance)
						expectedInconsistent.Add(knownSelfInconsistentGlyph);
				}
			}
			Assert.Equal(expectedInconsistent, selfInconsistentGlyphs);
		}

		[Theory]
		[MemberData(nameof(VariationFixtureCatalog.AllInstancedFixtures), MemberType = typeof(VariationFixtureCatalog))]
		public void EveryGlyphLeftSideBearing_MatchesInstancedTwinHmtx_WithinOneUnit(string instancedFileName)
		{
			// fontTools writes lsb = otRound(xMin_varied - leftPhantomX_varied) into the instanced hmtx.
			// CFF2 (Part B): fontTools leaves hmtx lsb at the DEFAULT instance (no phantom points, HVAR has no LsbMap —
			// checked 2026-09-02: twin hmtx lsb == VF default lsb while the twin's own outline starts elsewhere), so the
			// twin's hmtx is STALE and the oracle is the twin's outline bbox xMin — which is what our lsb computes.
			// CFF2 at the DEFAULT instance (added 2026-09-03, Source Serif 4 / Source Sans 3 VF): our lsb is the hmtx value
			// verbatim (the no-op path) and the twin's hmtx is NOT stale there; stbtt_GetGlyphBox on a CFF font is a
			// CONTROL-POINT bbox that can undershoot a cubic's true extremum (Source Sans 'uni002E002D0029': exact xMin
			// 29.92 vs control 28), so the bbox is the wrong oracle at default — compare hmtx there. (Consequence for
			// production: the varied CFF2 lsb IS control-bbox-derived and may sit a few units left of the true ink edge;
			// glyph placement uses the bitmap box, not lsb, so pixels are unaffected.)
			FontInfo variableFont = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(instancedFileName));
			FontInfo instancedTwin = VariationFixtureCatalog.LoadFont(instancedFileName);
			var coords = variableFont.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(instancedFileName));
			var failures = new List<string>();
			for (int glyph = 0; glyph < variableFont.numGlyphs; glyph++)
			{
				int oursAdvance = 0, oursLsb = 0, twinAdvance = 0, twinLsb = 0;
				variableFont.stbtt_GetGlyphHMetricsVar(glyph, coords, ref oursAdvance, ref oursLsb);
				if (variableFont.isCff2 && !coords.IsDefault)
				{
					int bx0 = 0, by0 = 0, bx1 = 0, by1 = 0;
					if (instancedTwin.stbtt_GetGlyphBox(glyph, ref bx0, ref by0, ref bx1, ref by1) == 0)
						continue; // empty glyph: lsb is the default value on both sides by construction
					twinLsb = bx0;
				}
				else
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