using StbTrueTypeSharp;
using StbTrueTypeSharp.Variations;
using Xunit;

namespace Variations.Tests
{
	/// <summary>fvar/avar parsing + the normalization pipeline (README_variations_implementation_notes.md §1).</summary>
	public class VariationNormalizationTests
	{
		private static FontVariationUserAxisValue Axis(string tag, double value) => new FontVariationUserAxisValue(OpenTypeAxisTag.FromString(tag), value);

		[Fact]
		public void StaticFont_HasNoVariations_AndNormalizesToDefault()
		{
			FontInfo staticFont = VariationFixtureCatalog.LoadFont("Inter.no-var.subset.ttf");
			Assert.False(staticFont.stbtt_HasVariations);
			Assert.False(staticFont.stbtt_VariationParseFailure.IsFailure);
			Assert.Empty(staticFont.stbtt_GetVariationAxes());
			Assert.True(staticFont.stbtt_NormalizeVariationRequest(new[] { Axis("wght", 700) }).IsDefault);
		}

		[Fact]
		public void Inter_Fvar_ParsesTwoAxes()
		{
			FontInfo inter = VariationFixtureCatalog.LoadFont("Inter.var.subset.ttf");
			Assert.True(inter.stbtt_HasVariations, inter.stbtt_VariationParseFailure.ToString());
			var axes = inter.stbtt_GetVariationAxes();
			Assert.Equal(2, axes.Length);
			Assert.Equal(OpenTypeAxisTag.Weight, axes[0].AxisTag);
			Assert.Equal(100.0, axes[0].MinValue); Assert.Equal(400.0, axes[0].DefaultValue); Assert.Equal(900.0, axes[0].MaxValue);
			Assert.Equal(OpenTypeAxisTag.Slant, axes[1].AxisTag);
			Assert.Equal(-10.0, axes[1].MinValue); Assert.Equal(0.0, axes[1].DefaultValue); Assert.Equal(0.0, axes[1].MaxValue);
			Assert.Equal(18, inter.stbtt_GetVariationNamedInstances().Length);
		}

		[Theory]
		[InlineData(400.0, 0)]        // default
		[InlineData(100.0, -16384)]   // min -> -1
		[InlineData(900.0, 16384)]    // max -> +1
		[InlineData(650.0, 8192)]     // (650-400)/(900-400) = 0.5
		[InlineData(250.0, -8192)]    // (250-400)/(400-100) = -0.5
		[InlineData(50.0, -16384)]    // below min: CLAMPS to min (the WPT descriptor-01 'wght 50' case)
		[InlineData(5000.0, 16384)]   // above max: clamps to max
		public void Inter_NoAvar_WeightNormalizesPiecewise(double userWeight, int expectedF2Dot14)
		{
			FontInfo inter = VariationFixtureCatalog.LoadFont("Inter.var.subset.ttf");
			var coords = inter.stbtt_NormalizeVariationRequest(new[] { Axis("wght", userWeight) });
			Assert.Equal(expectedF2Dot14, coords.GetF2Dot14(0));
			Assert.Equal(0, coords.GetF2Dot14(1));
			Assert.Equal(expectedF2Dot14 == 0, coords.IsDefault);
		}

		[Fact]
		public void Inter_SlantAxis_DefaultEqualsMax_NormalizesOnlyDownward()
		{
			FontInfo inter = VariationFixtureCatalog.LoadFont("Inter.var.subset.ttf");
			Assert.Equal(-16384, inter.stbtt_NormalizeVariationRequest(new[] { Axis("slnt", -10) }).GetF2Dot14(1));
			Assert.Equal(-8192, inter.stbtt_NormalizeVariationRequest(new[] { Axis("slnt", -5) }).GetF2Dot14(1));
			// default == max: any positive request clamps to the default => 0 (the divide-by-zero trap the clamp prevents)
			Assert.True(inter.stbtt_NormalizeVariationRequest(new[] { Axis("slnt", 20) }).IsDefault);
		}

		[Fact]
		public void UnknownAxisTags_AreIgnored_NeverAnError()
		{
			FontInfo inter = VariationFixtureCatalog.LoadFont("Inter.var.subset.ttf");
			Assert.True(inter.stbtt_NormalizeVariationRequest(new[] { Axis("wdth", 75), Axis("GRAD", 100) }).IsDefault);
			var mixed = inter.stbtt_NormalizeVariationRequest(new[] { Axis("wdth", 75), Axis("wght", 900) });
			Assert.Equal(16384, mixed.GetF2Dot14(0));
		}

		[Fact]
		public void NotoSansSymbols_Avar_RemapsExactlyAtKnee_AndLinearlyBetweenKnees()
		{
			// avar wght map (from the font, via fontTools): -1->-1, -0.6667->-0.7969, -0.3333->-0.5, 0->0, 0.2->0.18, ...
			FontInfo noto = VariationFixtureCatalog.LoadFont("NotoSansSymbols-VariableFont_wght.ttf");
			Assert.True(noto.stbtt_HasVariations, noto.stbtt_VariationParseFailure.ToString());
			var tables = noto.stbtt__GetVariationTables();
			Assert.NotNull(tables.Avar);
			Assert.True(tables.Avar.HasSegmentMap(0));
			Assert.Equal(9, tables.Avar.GetSegmentMapCount(0));

			// wght 300: default-normalized -1/3 lands EXACTLY on the knee whose from is F2Dot14(-0.3333) -> to -0.5.
			// -1/3 in double is not bit-equal to the F2Dot14 knee (-5461/16384), so this goes through the linear
			// branch between the knee and its neighbour and must still quantize to -8192 (fontTools parity).
			Assert.Equal(-8192, noto.stbtt_NormalizeVariationRequest(new[] { Axis("wght", 300) }).GetF2Dot14(0));

			// wght 250: default-normalized -0.5, strictly between the knees at -0.6667 and -0.3333:
			// expected = to_a + (to_b - to_a) * (v - from_a) / (from_b - from_a), computed from the parsed map.
			tables.Avar.GetSegmentMapPair(0, 1, out double fromA, out double toA);
			tables.Avar.GetSegmentMapPair(0, 2, out double fromB, out double toB);
			Assert.True(fromA < -0.5 && -0.5 < fromB, "knee bracketing assumption");
			double expected = toA + (toB - toA) * (-0.5 - fromA) / (fromB - fromA);
			Assert.Equal(FontVariationNormalization.ToF2Dot14(expected), noto.stbtt_NormalizeVariationRequest(new[] { Axis("wght", 250) }).GetF2Dot14(0));
			// Anchors survive the remap exactly.
			Assert.Equal(-16384, noto.stbtt_NormalizeVariationRequest(new[] { Axis("wght", 100) }).GetF2Dot14(0));
			Assert.Equal(16384, noto.stbtt_NormalizeVariationRequest(new[] { Axis("wght", 900) }).GetF2Dot14(0));
		}

		[Fact]
		public void RobotoFlex_ParsesAllThirteenAxes_AndAllVariationTables()
		{
			FontInfo flex = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor("RobotoFlex.instanced.default.ttf"));
			Assert.True(flex.stbtt_HasVariations, flex.stbtt_VariationParseFailure.ToString());
			Assert.Equal(13, flex.stbtt_GetVariationAxes().Length);
			var tables = flex.stbtt__GetVariationTables();
			Assert.NotNull(tables.Avar);
			Assert.NotNull(tables.Gvar);
			Assert.NotNull(tables.Hvar);
			Assert.NotNull(tables.Mvar);
			Assert.Equal(896, tables.Gvar.GlyphCount);
			Assert.Equal(13, tables.Gvar.AxisCount);
		}
	}
}