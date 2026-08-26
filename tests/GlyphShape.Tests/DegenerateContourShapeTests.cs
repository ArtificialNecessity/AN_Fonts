using System;
using System.IO;
using StbTrueTypeSharp;
using Xunit;
using Xunit.Abstractions;
using static StbTrueTypeSharp.Common;

namespace GlyphShape.Tests;

// Regression pin for AN_FluidUI _BUGFIX/380 (fixed in SafeStbTrueTypeSharp d4b40a9):
//
// Consolas-REGULAR encodes several invisible format-control codepoints as degenerate
// hint-carrier glyphs: 1 contour, 1 point, and that single point is OFF-CURVE
// (glyf flags byte 0x30). stbtt__GetGlyphShapeTT's contour-start on-curve synthesis
// looked ahead to vertices[off + i + 1] unconditionally — with n=1 that indexes one
// past the vertex array (silent heap overread in upstream C stb_truetype; hard
// IndexOutOfRangeException in this C# port). The crash escaped the FluidUI render
// thread and killed the whole WPT worker process on 4 css-text line-break pages.
//
// The fix treats a lone off-curve contour-start point as a degenerate on-curve start
// (the contour emits no ink either way — shape parses to moveto+close).
public class DegenerateContourShapeTests
{
    private readonly ITestOutputHelper _output;

    public DegenerateContourShapeTests(ITestOutputHelper output) => _output = output;

    // The exact codepoints that crashed the WPT worker (css3-text-line-break-baspglwj-121/
    // -123/-131 and line-breaking-028), plus U+2009 which shares the empty-glyph shape
    // family in Consolas but parses to 0 contours (control: must stay non-throwing too).
    [Theory]
    [InlineData(0x202F)] // NARROW NO-BREAK SPACE       (lone off-curve point in consola.ttf)
    [InlineData(0x034F)] // COMBINING GRAPHEME JOINER   (lone off-curve point in consola.ttf)
    [InlineData(0xFEFF)] // ZERO WIDTH NO-BREAK SPACE   (lone off-curve point in consola.ttf)
    [InlineData(0x205F)] // MEDIUM MATHEMATICAL SPACE   (lone off-curve point in consola.ttf)
    [InlineData(0x2009)] // THIN SPACE                  (0-contour empty glyph — control case)
    public void ConsolasFormatControlGlyphs_ShapeParse_DoesNotThrow(int codepoint)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf");
        if (!File.Exists(path))
        {
            _output.WriteLine($"SKIP: system font not present: {path}");
            return; // guarded skip (non-Windows / stripped install) — KernOracleTests pattern
        }

        byte[] fontData = File.ReadAllBytes(path);
        var fontInfo = new FontInfo();
        Assert.True(fontInfo.stbtt_InitFont(fontData, stbtt_GetFontOffsetForIndex(fontData, 0)) != 0,
            "stb failed to init consola.ttf");

        int glyphIndex = fontInfo.stbtt_FindGlyphIndex(codepoint);
        if (glyphIndex == 0)
        {
            _output.WriteLine($"SKIP: U+{codepoint:X4} not mapped by this consola.ttf version");
            return; // future font versions may drop the mapping — then there is nothing to pin
        }

        // 1) The raw shape parse — the exact call that threw IndexOutOfRangeException.
        int vertexCount = fontInfo.stbtt_GetGlyphShape(glyphIndex, out stbtt_vertex[]? vertices);
        _output.WriteLine($"U+{codepoint:X4} g={glyphIndex} vertexCount={vertexCount}");

        // A degenerate 1-point contour must parse to zero ink: either no vertices at all
        // (0-contour form) or a bare moveto(+close) pair — never a drawable curve/line.
        Assert.InRange(vertexCount, 0, 2);

        // 2) The full raster entry point Fontstash uses (renders even 0x0 masks).
        float scale = fontInfo.stbtt_ScaleForMappingEmToPixels(25f);
        int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
        fontInfo.stbtt_GetGlyphBitmapBoxSubpixel(glyphIndex, scale, scale, 0f, 0f, ref x0, ref y0, ref x1, ref y1);
        int maskWidth = x1 - x0, maskHeight = y1 - y0;
        byte[] maskPixels = new byte[Math.Max(1, maskWidth * maskHeight)];
        fontInfo.stbtt_MakeGlyphBitmapSubpixel(new FakePtr<byte>(maskPixels, 0),
            maskWidth, maskHeight, Math.Max(1, maskWidth), scale, scale, 0f, 0f, glyphIndex);

        // Invisible spacing glyphs must produce an empty bitmap box.
        Assert.Equal(0, maskWidth);
        Assert.Equal(0, maskHeight);
    }

    // Real ink-bearing glyphs must be unaffected by the degenerate-contour guard.
    [Theory]
    [InlineData('a', 30, 60)]
    [InlineData('X', 10, 30)]
    [InlineData('!', 10, 30)]
    public void ConsolasInkGlyphs_ShapeParse_StillProducesOutlines(int codepoint, int minVertices, int maxVertices)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf");
        if (!File.Exists(path))
        {
            _output.WriteLine($"SKIP: system font not present: {path}");
            return;
        }

        byte[] fontData = File.ReadAllBytes(path);
        var fontInfo = new FontInfo();
        Assert.True(fontInfo.stbtt_InitFont(fontData, stbtt_GetFontOffsetForIndex(fontData, 0)) != 0);

        int glyphIndex = fontInfo.stbtt_FindGlyphIndex(codepoint);
        Assert.NotEqual(0, glyphIndex);

        int vertexCount = fontInfo.stbtt_GetGlyphShape(glyphIndex, out _);
        _output.WriteLine($"U+{codepoint:X4} g={glyphIndex} vertexCount={vertexCount}");
        Assert.InRange(vertexCount, minVertices, maxVertices);
    }
}