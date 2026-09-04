using System;
using System.Collections.Generic;
using System.IO;
using HarfBuzzSharp;
using StbTrueTypeSharp;
using StbTrueTypeSharp.Layout;
using StbTrueTypeSharp.Variations;
using Xunit;
using Xunit.Abstractions;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace GposKerning.Tests;

// M2 GSUB glyph-sequence oracle (plans/color_glyph_fonts.md §5.5; ground truth
// _EXTERNAL_APIS/opentype_gsub_lookup_types.md). Contract under test: shaping the UTS #51 emoji corpus through
// GsubLookupApplier produces the EXACT glyph-id sequence AND cluster values HarfBuzz produces, on BOTH Noto builds.
//
// Feature set pinned EXPLICITLY: +ccmp +liga +rlig +clig (the HarfBuzz default set that touches emoji), all other
// defaults OFF. ClusterLevel = MonotoneCharacters (level 1): HarfBuzz's level 0 additionally merges clusters to
// grapheme boundaries WITHOUT a substitution — our buffer merges only on ligature/delete, which is exactly level 1.
// VS15 (263A FE0E) is deliberately NOT here: recorded M2 deviation, handled at the iterator level (§5.4).
public class GsubSequenceOracleTests
{
    private readonly ITestOutputHelper _output;

    public GsubSequenceOracleTests(ITestOutputHelper output) => _output = output;

    private static readonly Feature[] PinnedFeatures =
    {
        new Feature(new Tag('c', 'c', 'm', 'p'), 1),
        new Feature(new Tag('l', 'i', 'g', 'a'), 1),
        new Feature(new Tag('r', 'l', 'i', 'g'), 1),
        new Feature(new Tag('c', 'l', 'i', 'g'), 1),
        new Feature(new Tag('c', 'a', 'l', 't'), 0),
        new Feature(new Tag('c', 'u', 'r', 's'), 0),
        new Feature(new Tag('d', 'i', 's', 't'), 0),
        new Feature(new Tag('k', 'e', 'r', 'n'), 0),
    };

    private static readonly OpenTypeFeatureTag[] StbFeatures =
    {
        OpenTypeFeatureTag.Ccmp, OpenTypeFeatureTag.Liga, OpenTypeFeatureTag.Rlig, OpenTypeFeatureTag.Clig,
    };

    // UTS #51 corpus (_EXTERNAL_APIS/opentype_gsub_lookup_types.md §corpus). Codepoint arrays, not strings,
    // so the mechanism under test is visible at the call site.
    public static IEnumerable<object[]> CorpusSequences()
    {
        var corpus = new (string name, int[] codepoints)[]
        {
            ("thumbs up + medium skin tone (type 4, 2 components)", new[] { 0x1F44D, 0x1F3FD }),
            ("family ZWJ (type 4, 5 components)", new[] { 0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467 }),
            ("flag US (type 4, RI pair)", new[] { 0x1F1FA, 0x1F1F8 }),
            ("unknown RI pair (type 5 fmt 2 → placeholder + delete)", new[] { 0x1F1FD, 0x1F1FD }),
            ("keycap 1 (type 4, 3 components)", new[] { 0x0031, 0xFE0F, 0x20E3 }),
            ("heart + VS16 (type 4, VS16 absorbed)", new[] { 0x2764, 0xFE0F }),
            ("England tag sequence (type 4, 7 components)", new[] { 0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F }),
            ("stray tag after base (type 6 fmt 2 → delete)", new[] { 0x1F3F4, 0xE0030 }),
        };
        foreach (var font in new[] { "NotoColorEmoji.ttf", "NotoColorEmoji-Regular.ttf" })
            foreach (var (name, codepoints) in corpus)
                yield return new object[] { font, name, codepoints };
    }

    [Theory]
    [MemberData(nameof(CorpusSequences))]
    public void CorpusSequence_MatchesHarfBuzz(string fontFile, string name, int[] codepoints)
    {
        var (stb, hbFont) = LoadBoth(fontFile);
        using (hbFont)
        {
            var failure = CompareSequence(stb, hbFont, codepoints);
            Assert.True(failure == null, $"{fontFile} / {name}: {failure}");
        }
    }

    [Theory]
    [InlineData("NotoColorEmoji.ttf")]
    [InlineData("NotoColorEmoji-Regular.ttf")]
    public void RandomSequenceFuzz_MatchesHarfBuzz(string fontFile)
    {
        var (stb, hbFont) = LoadBoth(fontFile);
        using (hbFont)
        {
            // Deterministic fuzz (plan §5.5): 200 random sequences assembled from the emoji mechanism alphabet.
            var rng = new Random(0x5EED);
            var bases = new List<int>();
            for (int cp = 0x1F300; cp <= 0x1F9FF && bases.Count < 128; cp++)
                if (stb.stbtt_FindGlyphIndex(cp) != 0) bases.Add(cp);
            Assert.True(bases.Count > 50, "emoji base pool unexpectedly small");

            var mismatches = new List<string>();
            for (int i = 0; i < 200; i++)
            {
                var seq = new List<int> { bases[rng.Next(bases.Count)] };
                int parts = rng.Next(0, 4);
                for (int p = 0; p < parts; p++)
                {
                    switch (rng.Next(4))
                    {
                        case 0: seq.Add(0x1F3FB + rng.Next(5)); break;                   // skin tone modifier
                        case 1: seq.Add(0x200D); seq.Add(bases[rng.Next(bases.Count)]); break; // ZWJ + base
                        case 2: seq.Add(0xFE0F); break;                                  // VS16
                        case 3: seq.Add(0x1F1E6 + rng.Next(26)); break;                  // regional indicator
                    }
                }
                var failure = CompareSequence(stb, hbFont, seq.ToArray());
                if (failure != null) mismatches.Add($"seq#{i} [{string.Join(" ", seq.ConvertAll(c => c.ToString("X")))}]: {failure}");
            }

            foreach (var m in mismatches.GetRange(0, Math.Min(mismatches.Count, 25)))
                _output.WriteLine("  " + m);
            Assert.True(mismatches.Count == 0, $"{fontFile}: {mismatches.Count}/200 fuzz sequences mismatch HarfBuzz");
        }
    }

    /// <summary>Null = exact match; otherwise a human-readable diff of glyph ids / clusters.</summary>
    private static string? CompareSequence(FontInfo stb, HbFont hbFont, int[] codepoints)
    {
        // --- stb side: cmap-resolve each codepoint, then the ONE buffer applier ---
        var buffer = new GsubGlyphBuffer();
        int utf16Index = 0;
        for (int c = 0; c < codepoints.Length; c++)
        {
            int cp = codepoints[c];
            int glyph = stb.stbtt_FindGlyphIndex(cp);
            int utf16Len = cp > 0xFFFF ? 2 : 1;
            // cmap format 14 (UVS) pairing — HarfBuzz law: a (base, VS) pair with a UVS entry consumes the
            // selector BEFORE GSUB; the base carries both codepoints in its cluster.
            int next = c + 1 < codepoints.Length ? codepoints[c + 1] : -1;
            bool nextIsVs = (next >= 0xFE00 && next <= 0xFE0F) || (next >= 0xE0100 && next <= 0xE01EF);
            if (nextIsVs && stb.stbtt_GetVariationSequenceGlyph(cp, next, out int variationGlyph) is var uvs
                && uvs != TrueTypeUvsMapping.None)
            {
                if (uvs == TrueTypeUvsMapping.NonDefault) glyph = variationGlyph;
                utf16Len += next > 0xFFFF ? 2 : 1;
                buffer.Append((ushort)glyph, utf16Index, 2, false);
                utf16Index += utf16Len;
                c++; // selector consumed
                continue;
            }
            buffer.Append((ushort)glyph, utf16Index, 1, UnicodeDefaultIgnorable.IsDefaultIgnorable(cp)); // glyph 0 stays: HarfBuzz shapes .notdef too
            utf16Index += utf16Len;
        }
        var applier = stb.stbtt__GetGsubApplier();
        Assert.NotNull(applier);
        var lookups = applier.ResolveLookups(StbFeatures, FontVariationNormalizedCoordinates.Default);
        applier.Apply(lookups, buffer);

        // --- HarfBuzz side ---
        string text = ToUtf16String(codepoints);
        using var hbBuffer = new HbBuffer();
        hbBuffer.ClusterLevel = ClusterLevel.MonotoneCharacters;
        hbBuffer.AddUtf16(text);
        hbBuffer.GuessSegmentProperties();
        hbFont.Shape(hbBuffer, PinnedFeatures);
        var infos = hbBuffer.GlyphInfos;

        if (infos.Length != buffer.Length)
            return $"length: stb={buffer.Length} hb={infos.Length} (stb=[{FormatBuffer(buffer)}] hb=[{FormatHb(infos)}])";
        for (int i = 0; i < infos.Length; i++)
        {
            // A default-ignorable that SURVIVES shaping is hidden by HarfBuzz (substituted with the font's
            // invisible glyph, zero width) and DELETED by our text iterator (§5.4) — the glyph ids legitimately
            // differ at those positions; the cluster values must still agree.
            if (!buffer.IsDefaultIgnorable[i] && infos[i].Codepoint != buffer.GlyphIds[i])
                return $"glyph[{i}]: stb={buffer.GlyphIds[i]} hb={infos[i].Codepoint} (stb=[{FormatBuffer(buffer)}] hb=[{FormatHb(infos)}])";
            if (infos[i].Cluster != (uint)buffer.ClusterStart[i])
                return $"cluster[{i}]: stb={buffer.ClusterStart[i]} hb={infos[i].Cluster} (stb=[{FormatBuffer(buffer)}] hb=[{FormatHb(infos)}])";
        }
        return null;
    }

    private static (FontInfo stb, HbFont hbFont) LoadBoth(string fontFile)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fontFile);
        byte[] data = File.ReadAllBytes(path);
        var stb = new FontInfo();
        Assert.Equal(1, stb.stbtt_InitFont(data, 0));
        var blob = Blob.FromFile(path);
        var face = new Face(blob, 0);
        var hbFont = new HbFont(face);
        hbFont.SetScale((int)face.UnitsPerEm, (int)face.UnitsPerEm);
        return (stb, hbFont);
    }

    private static string ToUtf16String(int[] codepoints)
    {
        var sb = new System.Text.StringBuilder();
        foreach (int cp in codepoints) sb.Append(char.ConvertFromUtf32(cp));
        return sb.ToString();
    }

    private static string FormatBuffer(GsubGlyphBuffer b)
    {
        var parts = new List<string>();
        for (int i = 0; i < b.Length; i++) parts.Add($"{b.GlyphIds[i]}@{b.ClusterStart[i]}+{b.ClusterCodepointCount[i]}");
        return string.Join(" ", parts);
    }

    private static string FormatHb(GlyphInfo[] infos)
    {
        var parts = new List<string>();
        foreach (var gi in infos) parts.Add($"{gi.Codepoint}@{gi.Cluster}");
        return string.Join(" ", parts);
    }
}