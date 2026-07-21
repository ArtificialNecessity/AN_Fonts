using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarfBuzzSharp;
using StbTrueTypeSharp;
using Xunit;
using Xunit.Abstractions;
using static StbTrueTypeSharp.Common;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace GposKerning.Tests;

// Numeric HarfBuzz kerning oracle (plan: plans/crisp_text_gpos_kerning.md Phase 0).
//
// Contract under test:  stbtt_GetGlyphKernAdvance(g1, g2)  ==
//                       hb xAdvance(g1) - unkerned hmtx advance(g1)
// Both sides in UNSCALED FONT UNITS (hb_font scale pinned to unitsPerEm)
// => exact integer equality, no tolerance.
//
// Feature set is pinned EXPLICITLY: +kern only, everything else OFF.
// HarfBuzz enables calt/clig/curs/dist by default; calt/clig are GSUB and can
// substitute glyphs BEFORE positioning — a phantom mismatch no GPOS fix closes.
public class KernOracleTests
{
    private readonly ITestOutputHelper _output;

    public KernOracleTests(ITestOutputHelper output) => _output = output;

    // Codepoint set for pair enumeration: Latin letters, digits, common punctuation.
    private const string ProbeChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
        ".,'\"!?;:-()[]/";

    private static readonly Feature[] PinnedFeatures =
    {
        new Feature(new Tag('k', 'e', 'r', 'n'), 1),
        new Feature(new Tag('l', 'i', 'g', 'a'), 0),
        new Feature(new Tag('c', 'l', 'i', 'g'), 0),
        new Feature(new Tag('c', 'a', 'l', 't'), 0),
        new Feature(new Tag('c', 'u', 'r', 's'), 0),
        new Feature(new Tag('d', 'i', 's', 't'), 0),
    };

    [Theory]
    [InlineData("Roboto-Regular.ttf", false)]
    [InlineData("arial.ttf", true)]
    [InlineData("times.ttf", true)]
    [InlineData("consola.ttf", true)]
    public void StbKernMatchesHarfBuzz(string fontFile, bool systemFont)
    {
        string path = systemFont
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), fontFile)
            : Path.Combine(AppContext.BaseDirectory, fontFile);
        if (systemFont && !File.Exists(path))
        {
            _output.WriteLine($"SKIP: system font not present: {path}");
            return; // guarded skip (non-Windows / stripped install)
        }

        byte[] data = File.ReadAllBytes(path);

        // --- stb side ---
        var stb = new FontInfo();
        Assert.True(stb.stbtt_InitFont(data, stbtt_GetFontOffsetForIndex(data, 0)) != 0,
            $"stb failed to init {fontFile}");

        // --- HarfBuzz side, scale pinned to unitsPerEm (font units in == font units out) ---
        using var blob = Blob.FromFile(path);
        using var face = new Face(blob, 0);
        int upem = (int)face.UnitsPerEm;
        using var hbFont = new HbFont(face);
        hbFont.SetScale(upem, upem);

        var mismatches = new List<string>();
        int pairsTested = 0, pairsKerned = 0, substituted = 0;

        foreach (char c1 in ProbeChars)
        foreach (char c2 in ProbeChars)
        {
            int g1 = stb.stbtt_FindGlyphIndex(c1);
            int g2 = stb.stbtt_FindGlyphIndex(c2);
            if (g1 == 0 || g2 == 0)
                continue;

            int advanceWidth = 0, lsb = 0;
            stb.stbtt_GetGlyphHMetrics(g1, ref advanceWidth, ref lsb);

            using var buffer = new HbBuffer();
            buffer.Direction = Direction.LeftToRight;
            buffer.Script = Script.Latin;
            buffer.Language = new Language("en");
            buffer.AddUtf16(new string(new[] { c1, c2 })); // sets ContentType=Unicode
            hbFont.Shape(buffer, PinnedFeatures);

            var infos = buffer.GlyphInfos;
            var positions = buffer.GlyphPositions;

            // With the pinned feature set no GSUB substitution should occur.
            // If it does, that's real signal (feature-set contamination), not a GPOS bug.
            if (infos.Length != 2 || infos[0].Codepoint != (uint)g1 || infos[1].Codepoint != (uint)g2)
            {
                substituted++;
                mismatches.Add($"SUBSTITUTION '{c1}{c2}': hb produced " +
                    $"[{string.Join(",", FormatGlyphs(infos))}] expected [{g1},{g2}]");
                continue;
            }

            int hbKern = positions[0].XAdvance - advanceWidth;
            int stbKern = stb.stbtt_GetGlyphKernAdvance(g1, g2);

            pairsTested++;
            if (hbKern != 0)
                pairsKerned++;
            if (stbKern != hbKern)
                mismatches.Add($"'{c1}{c2}' (g{g1},g{g2}): stb={stbKern} hb={hbKern}");
        }

        _output.WriteLine($"{fontFile}: upem={upem} pairsTested={pairsTested} " +
            $"hbKernedPairs={pairsKerned} substitutions={substituted} mismatches={mismatches.Count}");
        foreach (var m in Take(mismatches, 50))
            _output.WriteLine("  " + m);
        if (mismatches.Count > 50)
            _output.WriteLine($"  ... and {mismatches.Count - 50} more");

        Assert.True(mismatches.Count == 0,
            $"{fontFile}: {mismatches.Count} kern mismatches vs HarfBuzz oracle " +
            $"({pairsKerned} hb-kerned pairs of {pairsTested} tested). " +
            "See test output for the diagnostic baseline.");
    }

    private static IEnumerable<string> FormatGlyphs(GlyphInfo[] infos)
    {
        foreach (var i in infos)
            yield return i.Codepoint.ToString();
    }

    private static IEnumerable<string> Take(List<string> list, int n)
    {
        for (int i = 0; i < list.Count && i < n; i++)
            yield return list[i];
    }
}