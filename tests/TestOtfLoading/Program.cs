using System;
using System.IO;
using StbTrueTypeSharp;
using static StbTrueTypeSharp.Common;

string testDir = AppContext.BaseDirectory;
string fontPath = Path.Combine(testDir, "debug_frutiger_roman.otf");

byte[] data = File.ReadAllBytes(fontPath);
var fontInfo = new FontInfo();
int offset = stbtt_GetFontOffsetForIndex(data, 0);
fontInfo.stbtt_InitFont(data, offset);

Console.WriteLine($"Font: numGlyphs={fontInfo.numGlyphs}, subrs.size={fontInfo.subrs?.size ?? 0}, gsubrs.size={fontInfo.gsubrs?.size ?? 0}");

// Find 'i' and dump its charstring bytes
int gi = fontInfo.stbtt_FindGlyphIndex('i');
Console.WriteLine($"'i' gid={gi}");

if (gi > 0)
{
    var cs = fontInfo.charstrings.stbtt__cff_index_get(gi);
    Console.WriteLine($"  charstring size={cs.size}");
    Console.Write("  bytes: ");
    for (int i = 0; i < cs.size && i < 64; i++)
        Console.Write($"{cs.data[i]:X2} ");
    Console.WriteLine();
    Console.WriteLine();

    // Try to run it and see where it fails
    stbtt_vertex[] verts;
    int nv = fontInfo.stbtt_GetGlyphShape(gi, out verts);
    Console.WriteLine($"  GetGlyphShape => {nv} vertices");
}