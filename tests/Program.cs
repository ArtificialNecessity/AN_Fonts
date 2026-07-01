using System;
using System.IO;
using StbTrueTypeSharp;
using static StbTrueTypeSharp.Common;

// Test OTF/CFF font loading with SafeStbTrueTypeSharp

string testDir = AppContext.BaseDirectory;
string[] testFonts = [
    Path.Combine(testDir, "Lato-Regular.otf"),
    @"C:\PROJECTS\AN_SafePDF\tests\SampleOTFFonts\Lato-Regular.otf",
    @"C:\PROJECTS\AN_SafePDF\tests\SampleOTFFonts\debug_generated_cff.otf",
];

Console.WriteLine("=== SafeStbTrueTypeSharp OTF/CFF Loading Test ===");
Console.WriteLine();

int passed = 0, failed = 0;

foreach (var fontPath in testFonts)
{
    string name = Path.GetFileName(fontPath);
    
    if (!File.Exists(fontPath))
    {
        Console.WriteLine($"  SKIP  {name} (file not found: {fontPath})");
        continue;
    }

    byte[] data = File.ReadAllBytes(fontPath);
    var fontInfo = new FontInfo();
    
    int offset = stbtt_GetFontOffsetForIndex(data, 0);
    int result = fontInfo.stbtt_InitFont(data, offset);

    // Gather diagnostic info
    string first4 = data.Length >= 4 
        ? $"{data[0]:X2} {data[1]:X2} {data[2]:X2} {data[3]:X2}"
        : "???";

    // Detailed CFF debugging - manually walk the init path
    if (result == 0 && data.Length >= 4 && data[0] == 'O')
    {
        var ptr = new FakePtr<byte>(data);
        uint cffOffset = stbtt__find_table(ptr, (uint)offset, "CFF ");
        Console.WriteLine($"        CFF table offset: {cffOffset}");
        if (cffOffset != 0)
        {
            var cffBuf = new Buf(new FakePtr<byte>(ptr, (int)cffOffset), 512 * 1024 * 1024);
            var b = cffBuf.Clone();
            Console.WriteLine($"        CFF header bytes: {data[cffOffset]:X2} {data[cffOffset+1]:X2} {data[cffOffset+2]:X2} {data[cffOffset+3]:X2}");
            b.stbtt__buf_skip(2); // skip major/minor
            int hdrSize = b.stbtt__buf_get8();
            Console.WriteLine($"        hdrSize={hdrSize}, cursor after skip2+get8={b.cursor}");
            b.stbtt__buf_seek(hdrSize);
            Console.WriteLine($"        cursor after seek(hdrSize)={b.cursor}");
            var nameIdx = b.stbtt__cff_get_index(); // Name INDEX
            Console.WriteLine($"        Name INDEX: cursor={b.cursor}, nameIdx.size={nameIdx.size}");
            var topdictidx = b.stbtt__cff_get_index(); // Top DICT INDEX
            Console.WriteLine($"        TopDict INDEX: cursor={b.cursor}, topdictidx.size={topdictidx.size}");
            var topdict = topdictidx.stbtt__cff_index_get(0);
            Console.WriteLine($"        topdict: size={topdict.size}, cursor={topdict.cursor}");
            // Try to get charstrings offset
            uint charstrings = 0;
            topdict.stbtt__dict_get_ints(17, out charstrings);
            Console.WriteLine($"        dict_get_ints(17) => charstrings offset={charstrings}");
        }
    }

    bool isOTF = data.Length >= 4 && data[0] == 'O' && data[1] == 'T' && data[2] == 'T' && data[3] == 'O';

    if (result == 1)
    {
        Console.WriteLine($"  PASS  {name} (first4={first4}, isOTF={isOTF}, numGlyphs={fontInfo.numGlyphs}, index_map={fontInfo.index_map})");
        passed++;
    }
    else
    {
        Console.WriteLine($"  FAIL  {name} (first4={first4}, isOTF={isOTF}, result={result})");
        Console.WriteLine($"        cff.size={fontInfo.cff?.size ?? -1}, charstrings.size={fontInfo.charstrings?.size ?? -1}");
        failed++;
    }
}

Console.WriteLine();
Console.WriteLine($"Results: {passed} passed, {failed} failed");
Environment.Exit(failed > 0 ? 1 : 0);