using System;
using System.Collections.Generic;
using System.IO;
using HarfBuzzSharp;
using StbTrueTypeSharp;
using StbTrueTypeSharp.Variations;
using Xunit;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace Variations.Tests
{
	/// <summary>
	/// Part D oracles (spec §9.6). fontTools' instancer evaluates FeatureVariations at the pinned location and bakes the
	/// WINNING lookups into the twin's plain 'rvrn' feature, so for every twin whose source VF has GSUB:
	///   1. our resolution on the VF at the coords has the twin's rvrn lookup COUNT (indices are renumbered by fontTools),
	///   2. our glyph selection on the VF for EVERY cmap codepoint == the twin's (exact integers),
	///   3. HarfBuzz shaping the TWIN (independent reader of the same baked GSUB) == our glyph ids on the VF.
	/// (3) is the independent check: (1) and (2) read the twin's GSUB with the SAME reader under test.
	/// </summary>
	public class RequiredVariationAlternatesTests
	{
		/// <summary>Twins whose source VF carries a GSUB table (FontStyleTest, Roboto Flex, Adobe glyf + CFF2).</summary>
		public static IEnumerable<object[]> TwinsWithGsub()
		{
			foreach (var row in VariationFixtureCatalog.AllInstancedFixtures())
			{
				string twinName = (string)row[0];
				var vf = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(twinName));
				if (vf.gsub != 0) yield return row;
			}
		}

		[Theory]
		[MemberData(nameof(TwinsWithGsub))]
		public void ResolvedLookups_MatchTwinBakedRvrn(string twinName)
		{
			FontInfo vf = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(twinName));
			FontInfo twin = VariationFixtureCatalog.LoadFont(twinName);
			Assert.False(vf.stbtt_GsubParseFailure.IsFailure, vf.stbtt_GsubParseFailure.ToString());
			Assert.False(twin.stbtt_GsubParseFailure.IsFailure, twin.stbtt_GsubParseFailure.ToString());
			var coords = vf.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(twinName));
			var ours = vf.stbtt_ResolveRequiredVariationLookups(coords);
			var baked = twin.stbtt_ResolveRequiredVariationLookups(FontVariationNormalizedCoordinates.Default);
			Assert.Equal(-1, baked.AppliedFeatureVariationRecordIndex); // fontTools dropped FeatureVariations from the twin
			// MEASURED 2026-09-02: fontTools PRUNES lookups no longer referenced after instancing and RENUMBERS the LookupList
			// (FontStyleTest default: VF winner = lookup 1, twin's rvrn -> [0]). Lookup INDICES are therefore not comparable
			// across the twin; the lookup COUNT is (one baked lookup per alternate lookup), and the glyph-level result is the
			// real oracle (EveryCmapGlyph_SubstitutesLikeTwin).
			Assert.Equal(baked.LookupIndicesAscending.Length, ours.LookupIndicesAscending.Length);
			Assert.Equal(0, ours.UnsupportedLookupCount); // every rvrn lookup in our fixtures is SingleSubst (measured with fontTools)
		}

		[Theory]
		[MemberData(nameof(TwinsWithGsub))]
		public void EveryCmapGlyph_SubstitutesLikeTwin(string twinName)
		{
			FontInfo vf = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(twinName));
			FontInfo twin = VariationFixtureCatalog.LoadFont(twinName);
			var coords = vf.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(twinName));
			var mismatches = new List<string>();
			int compared = 0, substituted = 0;
			for (int cp = 0x20; cp <= 0xFFFF; cp++)
			{
				int cmapGlyph = vf.stbtt_FindGlyphIndex(cp);
				if (cmapGlyph == 0) continue;
				Assert.Equal(cmapGlyph, twin.stbtt_FindGlyphIndex(cp)); // same glyph order (generator invariant)
				int ours = vf.stbtt_FindGlyphIndexVar(cp, coords);
				int theirs = twin.stbtt_FindGlyphIndexVar(cp, FontVariationNormalizedCoordinates.Default);
				compared++;
				if (theirs != cmapGlyph) substituted++;
				if (ours != theirs) mismatches.Add($"U+{cp:X4}: cmap {cmapGlyph} ours {ours} twin {theirs}");
			}
			Assert.True(mismatches.Count == 0, $"{twinName}: {mismatches.Count} glyph-selection mismatches over {compared} codepoints ({substituted} substituted by the twin):\n" + string.Join("\n", mismatches));
			Assert.True(compared > 0);
		}

		[Fact]
		public void FontStyleTest_KnownSubstitutions_AtTheWptPositions()
		{
			// Glyph ids measured with fontTools 2026-09-02 (FIXTURES.md): cmap v a r f o n t = 49 28 45 33 42 41 47;
			// .italic v a r f n t = 120 104 118 109 117 47 (t has no .italic; o has neither); .mono r f = 131 127.
			FontInfo vf = VariationFixtureCatalog.LoadFont("FontStyleTest-slnt-VF.ttf");
			Assert.True(vf.stbtt_HasRequiredVariationAlternates);
			var slnt = OpenTypeAxisTag.FromString("slnt");
			int[] Ids(double slntValue)
			{
				var coords = vf.stbtt_NormalizeVariationRequest(new[] { new FontVariationUserAxisValue(slnt, slntValue) });
				var ids = new int[7]; string s = "varfont";
				for (int i = 0; i < 7; i++) ids[i] = vf.stbtt_FindGlyphIndexVar(s[i], coords);
				return ids;
			}
			Assert.Equal(new[] { 49, 28, 131, 127, 42, 41, 47 }, Ids(0));        // default instance: f r -> .mono (NOT a no-op)
			Assert.Equal(new[] { 49, 28, 131, 127, 42, 41, 47 }, Ids(-7));       // just above the knee
			Assert.Equal(new[] { 120, 104, 118, 109, 42, 117, 47 }, Ids(-7.5));  // exactly ON the -0.5 knee (inclusive) -> italic
			Assert.Equal(new[] { 120, 104, 118, 109, 42, 117, 47 }, Ids(-14));   // WPT slnt-variable.html
			Assert.Equal(new[] { 120, 104, 118, 109, 42, 117, 47 }, Ids(-15));   // axis min
		}

		/// <summary>The probe string per stem: letters whose rvrn alternates exist in that font (measured).</summary>
		private static string ProbeTextFor(string twinName)
			=> twinName.StartsWith("FontStyleTest", StringComparison.Ordinal) ? "varfont" : "Hamburgefonstiv0123";

		[Theory]
		[MemberData(nameof(TwinsWithGsub))]
		public void HarfBuzzShapingTheTwin_YieldsOurGlyphIds(string twinName)
		{
			// HarfBuzz reads the twin's baked rvrn with ITS OWN GSUB code: the independent oracle for glyph SELECTION.
			// Features pinned like GposKerning.Tests: rvrn is applied by HarfBuzz unconditionally (stage 0); every optional
			// GSUB feature is OFF so no ligature/calt can move glyphs; kerning OFF (positions are not under test here).
			FontInfo vf = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor(twinName));
			var coords = vf.stbtt_NormalizeVariationRequest(VariationFixtureCatalog.DesignPositionOf(twinName));
			string text = ProbeTextFor(twinName);

			string twinPath = Path.Combine(VariationFixtureCatalog.FontsDirectory, twinName);
			using var blob = Blob.FromFile(twinPath);
			using var face = new Face(blob, 0);
			using var hbFont = new HbFont(face);
			hbFont.SetScale((int)face.UnitsPerEm, (int)face.UnitsPerEm);
			using var buffer = new HbBuffer();
			buffer.Direction = Direction.LeftToRight;
			buffer.Script = Script.Latin;
			buffer.Language = new Language("en");
			buffer.AddUtf16(text);
			hbFont.Shape(buffer, PinnedFeatures);
			var infos = buffer.GlyphInfos;
			Assert.Equal(text.Length, infos.Length); // no ligatures with the pinned feature set

			var mismatches = new List<string>();
			int hbSubstituted = 0;
			for (int i = 0; i < text.Length; i++)
			{
				int cmapGlyph = vf.stbtt_FindGlyphIndex(text[i]);
				int ours = vf.stbtt_FindGlyphIndexVar(text[i], coords);
				int hb = (int)infos[i].Codepoint;
				if (hb != cmapGlyph) hbSubstituted++;
				if (ours != hb) mismatches.Add($"'{text[i]}': cmap {cmapGlyph} ours {ours} harfbuzz {hb}");
			}
			Assert.True(mismatches.Count == 0, $"{twinName}: {mismatches.Count} glyph-id mismatches vs HarfBuzz (hb substituted {hbSubstituted} of {text.Length}):\n" + string.Join("\n", mismatches));
		}

		private static readonly Feature[] PinnedFeatures =
		{
			new Feature(new Tag('k', 'e', 'r', 'n'), 0),
			new Feature(new Tag('l', 'i', 'g', 'a'), 0),
			new Feature(new Tag('c', 'l', 'i', 'g'), 0),
			new Feature(new Tag('c', 'a', 'l', 't'), 0),
			new Feature(new Tag('c', 'u', 'r', 's'), 0),
			new Feature(new Tag('d', 'i', 's', 't'), 0),
			new Feature(new Tag('c', 'c', 'm', 'p'), 0),
			new Feature(new Tag('l', 'o', 'c', 'l'), 0),
			new Feature(new Tag('m', 'a', 'r', 'k'), 0),
			new Feature(new Tag('m', 'k', 'm', 'k'), 0),
		};

		[Fact]
		public void StaticFontsWithoutRvrn_AreIdentity_AndVariableFontsWithoutGsub_TooEmpty()
		{
			FontInfo inter = VariationFixtureCatalog.LoadFont("Inter.var.subset.ttf");    // GSUB 1.0, 'numr' only, no rvrn
			Assert.False(inter.stbtt_HasRequiredVariationAlternates);
			Assert.True(inter.stbtt_ResolveRequiredVariationLookups(FontVariationNormalizedCoordinates.Default).IsEmpty);
			FontInfo noto = VariationFixtureCatalog.LoadFont("NotoSansSymbols-VariableFont_wght.ttf");
			Assert.False(noto.stbtt_HasRequiredVariationAlternates);
			FontInfo wptStatic = VariationFixtureCatalog.LoadFont("Inter.no-var.subset.ttf"); // no GSUB at all
			Assert.Equal(0, wptStatic.gsub);
			Assert.False(wptStatic.stbtt_HasRequiredVariationAlternates);
			for (int cp = 0x20; cp < 0x7F; cp++)
				Assert.Equal(wptStatic.stbtt_FindGlyphIndex(cp), wptStatic.stbtt_FindGlyphIndexVar(cp, FontVariationNormalizedCoordinates.Default));
		}

		[Fact]
		public void NoFixtureHasGposFeatureVariations_SoTheDeferredHookIsNotYetNeeded()
		{
			// Spec §9.9: the GPOS FeatureVariations hook is deliberately unwired until a fixture exercises it. This pins the
			// premise; when a font flips this diagnostic to true, wire the hook (see stbtt__ResolveGposKernLookups' comment).
			foreach (var row in VariationFixtureCatalog.AllInstancedFixtures())
			{
				var vf = VariationFixtureCatalog.LoadFont(VariationFixtureCatalog.SourceVariableFontFileFor((string)row[0]));
				Assert.False(vf.stbtt_HasUnappliedGposFeatureVariations, (string)row[0]);
			}
		}
	}
}