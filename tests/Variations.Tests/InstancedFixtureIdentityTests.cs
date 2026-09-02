using System;
using System.Collections.Generic;
using StbTrueTypeSharp;
using StbTrueTypeSharp.Variations;
using Xunit;
using static StbTrueTypeSharp.Common;

namespace Variations.Tests
{
	/// <summary>
	/// THE definitive correctness gate (plans/font_variable_support_fvar_avar_gvar_hvar.md §6): for every
	/// glyph, our varied outline on the VARIABLE font must equal the plain outline of the fontTools-INSTANCED
	/// static twin within ±1 font unit per coordinate (one rounding). Exercises tuple decoding, region scalars,
	/// IUP, phantom points, composites and avar knees against an independent reference implementation.
	/// </summary>
	public class InstancedFixtureIdentityTests
	{
		private const int OutlineToleranceFontUnits = 1;

		[Theory]
		[MemberData(nameof(VariationFixtureCatalog.AllInstancedFixtures), MemberType = typeof(VariationFixtureCatalog))]
		public void EveryGlyphOutline_MatchesInstancedTwin_WithinOneFontUnit(string instancedFileName)
		{
			FontInfo variableFont = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(instancedFileName));
			FontInfo instancedTwin = VariationFixtureCatalog.LoadFont(instancedFileName);
			Assert.True(variableFont.stbtt_HasVariations, variableFont.stbtt_VariationParseFailure.ToString());
			Assert.Equal(variableFont.numGlyphs, instancedTwin.numGlyphs);

			var coords = variableFont.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(instancedFileName));
			bool isDefaultPosition = instancedFileName.EndsWith(".instanced.default.ttf", StringComparison.Ordinal);
			Assert.Equal(isDefaultPosition, coords.IsDefault);
			int tolerance = isDefaultPosition ? 0 : OutlineToleranceFontUnits;   // default instance: byte-identical, no rounding slack

			var failures = new List<string>();
			int maxAbsDifference = 0;
			int comparedGlyphs = 0, comparedCoordinates = 0;
			for (int glyph = 0; glyph < variableFont.numGlyphs; glyph++)
			{
				int oursCount = variableFont.stbtt_GetGlyphShapeVar(glyph, coords, out stbtt_vertex[] ours);
				int twinCount = instancedTwin.stbtt_GetGlyphShape(glyph, out stbtt_vertex[] twin);
				if (oursCount != twinCount)
				{
					failures.Add($"glyph {glyph}: vertex count ours={oursCount} twin={twinCount}");
					continue;
				}
				comparedGlyphs++;
				for (int v = 0; v < oursCount; v++)
				{
					if (ours[v].type != twin[v].type)
					{
						failures.Add($"glyph {glyph} vertex {v}: type ours={ours[v].type} twin={twin[v].type}");
						break;
					}
					int d = Math.Max(Math.Max(Math.Abs(ours[v].x - twin[v].x), Math.Abs(ours[v].y - twin[v].y)),
						Math.Max(Math.Abs(ours[v].cx - twin[v].cx), Math.Abs(ours[v].cy - twin[v].cy)));
					comparedCoordinates++;
					if (d > maxAbsDifference) maxAbsDifference = d;
					if (d > tolerance)
					{
						failures.Add($"glyph {glyph} vertex {v} (type {ours[v].type}): ours=({ours[v].x},{ours[v].y} c {ours[v].cx},{ours[v].cy}) twin=({twin[v].x},{twin[v].y} c {twin[v].cx},{twin[v].cy}) |Δ|={d}");
						if (failures.Count > 40) break;
					}
				}
				if (failures.Count > 40) break;
			}
			Assert.True(failures.Count == 0,
				$"{instancedFileName}: {failures.Count}+ mismatches (compared {comparedGlyphs} glyphs / {comparedCoordinates} coordinates, max|Δ|={maxAbsDifference}):\n" + string.Join("\n", failures));
			Assert.True(comparedGlyphs > 0);
		}

		[Fact]
		public void DefaultCoordinates_RouteToTheUnvariedPath_ByteForByte()
		{
			FontInfo flex = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor("RobotoFlex.instanced.default.ttf"));
			for (int glyph = 0; glyph < flex.numGlyphs; glyph++)
			{
				int a = flex.stbtt_GetGlyphShape(glyph, out stbtt_vertex[] plain);
				int b = flex.stbtt_GetGlyphShapeVar(glyph, FontVariationNormalizedCoordinates.Default, out stbtt_vertex[] viaVar);
				Assert.Equal(a, b);
				for (int v = 0; v < a; v++)
					Assert.True(plain[v].x == viaVar[v].x && plain[v].y == viaVar[v].y && plain[v].cx == viaVar[v].cx && plain[v].cy == viaVar[v].cy && plain[v].type == viaVar[v].type, $"glyph {glyph} vertex {v}");
			}
		}

		[Fact]
		public void Inter_DefaultInstanceFixture_EqualsWptNoVarTwin()
		{
			// Two independent statics of the same instance: fontTools' bake and WPT's shipped no-var subset.
			FontInfo baked = VariationFixtureCatalog.LoadFont("Inter.var.subset.instanced.default.ttf");
			FontInfo wpt = VariationFixtureCatalog.LoadFont("Inter.no-var.subset.ttf");
			Assert.Equal(baked.numGlyphs, wpt.numGlyphs);
			for (int glyph = 0; glyph < baked.numGlyphs; glyph++)
			{
				int a = baked.stbtt_GetGlyphShape(glyph, out stbtt_vertex[] va);
				int b = wpt.stbtt_GetGlyphShape(glyph, out stbtt_vertex[] vb);
				Assert.Equal(a, b);
				for (int v = 0; v < a; v++)
					Assert.True(va[v].x == vb[v].x && va[v].y == vb[v].y && va[v].cx == vb[v].cx && va[v].cy == vb[v].cy, $"glyph {glyph} vertex {v}");
			}
		}
	}
}