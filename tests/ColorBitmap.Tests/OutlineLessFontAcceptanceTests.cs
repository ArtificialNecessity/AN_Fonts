using System.IO;
using StbTrueTypeSharp;
using Xunit;

namespace ColorBitmap.Tests
{
    /// <summary>
    /// plans/color_glyph_fonts.md §3.4: a font whose glyphs come ONLY from colour bitmap strikes
    /// (Noto Color Emoji CBDT build: CBLC+CBDT, no glyf/loca/CFF/CFF2) must load, and every outline
    /// entry point must report "empty glyph" instead of indexing a missing table.
    /// </summary>
    public sealed class OutlineLessFontAcceptanceTests
    {
        private static FontInfo LoadFace(string fileName)
        {
            byte[] fontBytes = File.ReadAllBytes(fileName);
            var face = new FontInfo();
            int ok = face.stbtt_InitFont(fontBytes, Common.stbtt_GetFontOffsetForIndex(fontBytes, 0));
            Assert.Equal(1, ok);
            return face;
        }

        [Fact]
        public void NotoColorEmoji_Cbdt_Loads_As_OutlineLess()
        {
            FontInfo face = LoadFace("NotoColorEmoji.ttf");
            Assert.Equal(FontInfo.TrueTypeOutlineFormat.None, face.outlineFormat);
            Assert.False(face.HasOutlines);
            Assert.True(face.HasColorBitmapStrikeTables);
            Assert.NotEqual(0, face.cblc);
            Assert.NotEqual(0, face.cbdt);
            Assert.Equal(0, face.glyf);
            Assert.Equal(0, face.loca);
            Assert.True(face.cff.size == 0, "no CFF buffer for an outline-less font");
            Assert.Equal(4027, face.numGlyphs); // maxp of the 2026 Noto CBDT build
        }

        [Fact]
        public void OutlineLess_Cmap_And_Hmtx_Still_Work()
        {
            FontInfo face = LoadFace("NotoColorEmoji.ttf");
            int grinning = face.stbtt_FindGlyphIndex(0x1F600); // 😀
            Assert.NotEqual(0, grinning);
            int advance = 0, lsb = 0;
            face.stbtt_GetGlyphHMetrics(grinning, ref advance, ref lsb);
            Assert.True(advance > 0, "hmtx advance must be authoritative for bitmap glyphs (§3.5)");
        }

        [Fact]
        public void OutlineLess_Every_Outline_Query_Is_Empty_And_Does_Not_Throw()
        {
            FontInfo face = LoadFace("NotoColorEmoji.ttf");
            int grinning = face.stbtt_FindGlyphIndex(0x1F600);

            Assert.Equal(-1, face.stbtt__GetGlyfOffset(grinning));
            Assert.Equal(-1, face.stbtt__GetGlyfOffset(0));

            int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
            Assert.Equal(0, face.stbtt_GetGlyphBox(grinning, ref x0, ref y0, ref x1, ref y1));
            Assert.Equal(1, face.stbtt_IsGlyphEmpty(grinning));

            int vertexCount = face.stbtt_GetGlyphShape(grinning, out Common.stbtt_vertex[] vertices);
            Assert.Equal(0, vertexCount);
            Assert.Null(vertices);

            // Bitmap box + raster of an empty outline: zero-size box, nothing written.
            int bx0 = 0, by0 = 0, bx1 = 0, by1 = 0;
            face.stbtt_GetGlyphBitmapBoxSubpixel(grinning, 0.01f, 0.01f, 0f, 0f, ref bx0, ref by0, ref bx1, ref by1);
            Assert.Equal(bx0, bx1);
            Assert.Equal(by0, by1);

            int edgeCount = face.stbtt_GetGlyphHorizontalLineEdges(grinning, 10, out float[] edgeYs, out bool[] tops);
            Assert.Equal(0, edgeCount);
            Assert.Null(edgeYs);
            Assert.Null(tops);
        }

        [Fact]
        public void OutlineLess_Vertical_Metrics_Are_Readable()
        {
            FontInfo face = LoadFace("NotoColorEmoji.ttf");
            Assert.True(face.stbtt_GetFontVerticalMetrics(out int vAscent, out int vDescent, out int vLineGap));
            Assert.True(vAscent != 0 || vDescent != 0 || vLineGap != 0);
            int grinning = face.stbtt_FindGlyphIndex(0x1F600);
            Assert.True(face.stbtt_GetGlyphVMetrics(grinning, out int advanceHeight, out int topSideBearing));
            Assert.True(advanceHeight > 0);
            _ = topSideBearing;
        }

        [Fact]
        public void Outline_Font_Is_Classified_TrueTypeGlyf_And_Has_No_Strikes()
        {
            FontInfo face = LoadFace("Roboto-Regular.ttf");
            Assert.Equal(FontInfo.TrueTypeOutlineFormat.TrueTypeGlyf, face.outlineFormat);
            Assert.True(face.HasOutlines);
            Assert.False(face.HasColorBitmapStrikeTables);
            Assert.False(face.stbtt_GetFontVerticalMetrics(out _, out _, out _));
            Assert.False(face.stbtt_GetGlyphVMetrics(face.stbtt_FindGlyphIndex('A'), out _, out _));
        }

        [Fact]
        public void Font_With_Neither_Outlines_Nor_Strikes_Is_Rejected()
        {
            // Strip CBLC/CBDT from the Noto directory by renaming their tags: the font now has NO glyph source.
            byte[] fontBytes = File.ReadAllBytes("NotoColorEmoji.ttf");
            int numTables = (fontBytes[4] << 8) | fontBytes[5];
            for (int i = 0; i < numTables; i++)
            {
                int recordOffset = 12 + i * 16;
                string tag = System.Text.Encoding.ASCII.GetString(fontBytes, recordOffset, 4);
                if (tag == "CBLC" || tag == "CBDT")
                    fontBytes[recordOffset] = (byte)'x'; // 'xBLC' / 'xBDT' — unknown tags, ignored by the lookup
            }
            var face = new FontInfo();
            Assert.Equal(0, face.stbtt_InitFont(fontBytes, 0));
        }
    }
}