namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// The 63-entry known-table-tags array from WOFF2 §5.2 ("Known Table Tags"),
    /// transcribed from 3P_woff2/src/table_tags.cc. Directory flag-byte values
    /// 0-62 index this array; 63 means an explicit u32 tag follows.
    /// </summary>
    internal static class Woff2KnownTableTags
    {
        private static uint Tag(char a, char b, char c, char d)
            => ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d;

        internal static readonly uint[] KnownTableTags =
        {
            Tag('c','m','a','p'), // 0
            Tag('h','e','a','d'), // 1
            Tag('h','h','e','a'), // 2
            Tag('h','m','t','x'), // 3
            Tag('m','a','x','p'), // 4
            Tag('n','a','m','e'), // 5
            Tag('O','S','/','2'), // 6
            Tag('p','o','s','t'), // 7
            Tag('c','v','t',' '), // 8
            Tag('f','p','g','m'), // 9
            Tag('g','l','y','f'), // 10
            Tag('l','o','c','a'), // 11
            Tag('p','r','e','p'), // 12
            Tag('C','F','F',' '), // 13
            Tag('V','O','R','G'), // 14
            Tag('E','B','D','T'), // 15
            Tag('E','B','L','C'), // 16
            Tag('g','a','s','p'), // 17
            Tag('h','d','m','x'), // 18
            Tag('k','e','r','n'), // 19
            Tag('L','T','S','H'), // 20
            Tag('P','C','L','T'), // 21
            Tag('V','D','M','X'), // 22
            Tag('v','h','e','a'), // 23
            Tag('v','m','t','x'), // 24
            Tag('B','A','S','E'), // 25
            Tag('G','D','E','F'), // 26
            Tag('G','P','O','S'), // 27
            Tag('G','S','U','B'), // 28
            Tag('E','B','S','C'), // 29
            Tag('J','S','T','F'), // 30
            Tag('M','A','T','H'), // 31
            Tag('C','B','D','T'), // 32
            Tag('C','B','L','C'), // 33
            Tag('C','O','L','R'), // 34
            Tag('C','P','A','L'), // 35
            Tag('S','V','G',' '), // 36
            Tag('s','b','i','x'), // 37
            Tag('a','c','n','t'), // 38
            Tag('a','v','a','r'), // 39
            Tag('b','d','a','t'), // 40
            Tag('b','l','o','c'), // 41
            Tag('b','s','l','n'), // 42
            Tag('c','v','a','r'), // 43
            Tag('f','d','s','c'), // 44
            Tag('f','e','a','t'), // 45
            Tag('f','m','t','x'), // 46
            Tag('f','v','a','r'), // 47
            Tag('g','v','a','r'), // 48
            Tag('h','s','t','y'), // 49
            Tag('j','u','s','t'), // 50
            Tag('l','c','a','r'), // 51
            Tag('m','o','r','t'), // 52
            Tag('m','o','r','x'), // 53
            Tag('o','p','b','d'), // 54
            Tag('p','r','o','p'), // 55
            Tag('t','r','a','k'), // 56
            Tag('Z','a','p','f'), // 57
            Tag('S','i','l','f'), // 58
            Tag('G','l','a','t'), // 59
            Tag('G','l','o','c'), // 60
            Tag('F','e','a','t'), // 61
            Tag('S','i','l','l'), // 62
        };

        internal const uint GlyfTableTag = 0x676C7966; // 'glyf'
        internal const uint LocaTableTag = 0x6C6F6361; // 'loca'
        internal const uint HmtxTableTag = 0x686D7478; // 'hmtx'
        internal const uint HeadTableTag = 0x68656164; // 'head'
        internal const uint TtcFontFlavor = 0x74746366; // 'ttcf'
    }
}