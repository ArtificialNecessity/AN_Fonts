using System;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.ColorBitmap;
using Xunit;

namespace ColorBitmap.Tests
{
    /// <summary>CBLC/CBDT parser against the real NotoColorEmoji.ttf (plans/color_glyph_fonts.md §4.1 tests).</summary>
    public sealed class ColorBitmapStrikeTableTests
    {
        private static FontInfo LoadNoto()
        {
            byte[] fontBytes = File.ReadAllBytes("NotoColorEmoji.ttf");
            var face = new FontInfo();
            Assert.Equal(1, face.stbtt_InitFont(fontBytes, 0));
            return face;
        }

        [Fact]
        public void Noto_Parses_One_Colour_Strike_At_109ppem()
        {
            FontInfo face = LoadNoto();
            ColorBitmapStrikeTable table = face.stbtt_GetColorBitmapStrikeTable();
            Assert.NotNull(table);
            Assert.False(face.stbtt_GetColorBitmapStrikeTableFailure().IsFailure);
            Assert.Equal(1, table.StrikeCount);
            ColorBitmapStrikeTable.Strike strike = table.GetStrike(new ColorBitmapStrikeIndex(0));
            Assert.Equal(109, strike.PpemX);
            Assert.Equal(109, strike.PpemY);
            Assert.Equal(32, strike.BitDepth);
            Assert.True(strike.IsColour);
            Assert.True(strike.IndexSubtableCount > 0);
            // Same object on repeat: parsed once, cached.
            Assert.Same(table, face.stbtt_GetColorBitmapStrikeTable());
        }

        [Fact]
        public void Noto_Every_Glyph_In_Strike_Range_Locates_Or_Is_Cleanly_Absent()
        {
            FontInfo face = LoadNoto();
            ColorBitmapStrikeTable table = face.stbtt_GetColorBitmapStrikeTable()!;
            var strikeIndex = new ColorBitmapStrikeIndex(0);
            ColorBitmapStrikeTable.Strike strike = table.GetStrike(strikeIndex);
            int located = 0, absent = 0;
            for (int glyph = strike.StartGlyphIndex; glyph <= strike.EndGlyphIndex; glyph++)
            {
                if (table.TryLocateGlyph(strikeIndex, glyph, out ColorBitmapGlyphRecord record, out ColorBitmapTableFailure failure))
                {
                    located++;
                    Assert.True(record.IsPng, $"glyph {glyph}: Noto CBDT is PNG-only (format {record.ImageFormat})");
                    Assert.True(record.Metrics.Width > 0 && record.Metrics.Height > 0, $"glyph {glyph}: empty metrics");
                    Assert.True(record.PngLength > 8, $"glyph {glyph}: PNG too short");
                }
                else
                {
                    // The ONLY acceptable miss is a clean "not in strike" — never a structural failure.
                    Assert.Equal(ColorBitmapTableFailureCode.GlyphNotInStrike, failure.FailureCode);
                    absent++;
                }
            }
            Assert.True(located > 3000, $"expected thousands of emoji bitmaps, located {located} (absent {absent})");
        }

        [Fact]
        public void Noto_Png_Ihdr_Dimensions_Match_Glyph_Metrics()
        {
            // Spec (CBDT §Compressed color bitmaps): "The individual images must have the same size as expected
            // by the table in the bitmap metrics." IHDR: 8-byte signature, 4 len, 'IHDR', width u32, height u32.
            FontInfo face = LoadNoto();
            ColorBitmapStrikeTable table = face.stbtt_GetColorBitmapStrikeTable()!;
            var strikeIndex = new ColorBitmapStrikeIndex(0);
            ColorBitmapStrikeTable.Strike strike = table.GetStrike(strikeIndex);
            int checkedCount = 0;
            for (int glyph = strike.StartGlyphIndex; glyph <= strike.EndGlyphIndex; glyph++)
            {
                if (!table.TryLocateGlyph(strikeIndex, glyph, out ColorBitmapGlyphRecord record, out _)) continue;
                ReadOnlySpan<byte> png = table.GetPngBytes(record);
                Assert.Equal(0x89, png[0]); Assert.Equal((byte)'P', png[1]); Assert.Equal((byte)'N', png[2]); Assert.Equal((byte)'G', png[3]);
                Assert.Equal((byte)'I', png[12]); Assert.Equal((byte)'H', png[13]); Assert.Equal((byte)'D', png[14]); Assert.Equal((byte)'R', png[15]);
                int ihdrWidth = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
                int ihdrHeight = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
                Assert.Equal(record.Metrics.Width, ihdrWidth);
                Assert.Equal(record.Metrics.Height, ihdrHeight);
                checkedCount++;
            }
            Assert.True(checkedCount > 3000);
        }

        [Fact]
        public void Noto_Grinning_Face_Has_Sensible_Metrics_And_Hmtx_Agrees()
        {
            FontInfo face = LoadNoto();
            ColorBitmapStrikeTable table = face.stbtt_GetColorBitmapStrikeTable()!;
            int grinning = face.stbtt_FindGlyphIndex(0x1F600);
            Assert.True(table.TryLocateGlyph(new ColorBitmapStrikeIndex(0), grinning, out ColorBitmapGlyphRecord record, out _));
            // A 109 ppem emoji: roughly square, ~1 em wide.
            Assert.InRange((int)record.Metrics.Width, 100, 140);
            Assert.InRange((int)record.Metrics.Height, 100, 140);
            Assert.True(record.Metrics.HoriBearingY > 0, "top edge is above the baseline");
            // hmtx (font units) scaled to 109 ppem should agree with the strike advance within a pixel or so.
            int advance = 0, lsb = 0;
            face.stbtt_GetGlyphHMetrics(grinning, ref advance, ref lsb);
            float unitsPerEm = face.stbtt_GetUnitsPerEm();
            float hmtxAdvanceAtStrike = advance * 109f / unitsPerEm;
            Assert.InRange(hmtxAdvanceAtStrike, record.Metrics.HoriAdvance - 2f, record.Metrics.HoriAdvance + 2f);
        }

        [Fact]
        public void Glyph_Classification_Is_Per_Glyph()
        {
            FontInfo face = LoadNoto();
            Assert.True(face.stbtt_GlyphHasColorBitmap(face.stbtt_FindGlyphIndex(0x1F600)));
            Assert.False(face.stbtt_GlyphHasColorBitmap(0));          // .notdef has no bitmap
            Assert.False(face.stbtt_GlyphHasColorBitmap(65535));      // outside every range
        }

        [Theory]
        [InlineData(1f, 0)]      // tiny request → the only (larger) strike
        [InlineData(109f, 0)]    // exact
        [InlineData(300f, 0)]    // larger than every strike → the largest
        public void Strike_Selection_Prefers_Smallest_Strike_At_Or_Above_Request(float ppem, int expectedStrike)
        {
            FontInfo face = LoadNoto();
            ColorBitmapStrikeTable table = face.stbtt_GetColorBitmapStrikeTable()!;
            Assert.Equal(expectedStrike, table.SelectStrikeIndexForPpem(ppem).Value);
        }

        [Fact]
        public void Png_Record_Refuses_Raw_Decode_With_Clean_Code()
        {
            FontInfo face = LoadNoto();
            ColorBitmapStrikeTable table = face.stbtt_GetColorBitmapStrikeTable()!;
            int grinning = face.stbtt_FindGlyphIndex(0x1F600);
            Assert.True(table.TryLocateGlyph(new ColorBitmapStrikeIndex(0), grinning, out ColorBitmapGlyphRecord record, out _));
            Assert.False(table.TryDecodeRawPixels(record, out _, out ColorBitmapTableFailure failure));
            Assert.Equal(ColorBitmapTableFailureCode.ImageFormatIsPngNotRaw, failure.FailureCode);
        }

        // ── hostile input: every failure path returns a code, never throws ──

        private static (byte[] data, int cblcOffset, int cblcLength, int cbdtOffset, int cbdtLength) NotoTables()
        {
            byte[] data = File.ReadAllBytes("NotoColorEmoji.ttf");
            int numTables = (data[4] << 8) | data[5];
            int cblcOffset = 0, cblcLength = 0, cbdtOffset = 0, cbdtLength = 0;
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + i * 16;
                string tag = System.Text.Encoding.ASCII.GetString(data, rec, 4);
                int off = (data[rec + 8] << 24) | (data[rec + 9] << 16) | (data[rec + 10] << 8) | data[rec + 11];
                int len = (data[rec + 12] << 24) | (data[rec + 13] << 16) | (data[rec + 14] << 8) | data[rec + 15];
                if (tag == "CBLC") { cblcOffset = off; cblcLength = len; }
                if (tag == "CBDT") { cbdtOffset = off; cbdtLength = len; }
            }
            return (data, cblcOffset, cblcLength, cbdtOffset, cbdtLength);
        }

        [Fact]
        public void Fuzz_Bad_Cblc_Version_Is_Rejected()
        {
            var t = NotoTables();
            byte[] data = (byte[])t.data.Clone();
            data[t.cblcOffset] = 0; data[t.cblcOffset + 1] = 2; // 2.0 (EBLC) instead of 3.0
            Assert.False(ColorBitmapStrikeTable.TryParse(data, t.cblcOffset, t.cblcLength, t.cbdtOffset, t.cbdtLength, out _, out var failure));
            Assert.Equal(ColorBitmapTableFailureCode.CblcUnsupportedVersion, failure.FailureCode);
        }

        [Fact]
        public void Fuzz_Hostile_Strike_Count_Is_Rejected_Without_Iterating()
        {
            var t = NotoTables();
            byte[] data = (byte[])t.data.Clone();
            data[t.cblcOffset + 4] = 0xFF; data[t.cblcOffset + 5] = 0xFF; data[t.cblcOffset + 6] = 0xFF; data[t.cblcOffset + 7] = 0xFF;
            Assert.False(ColorBitmapStrikeTable.TryParse(data, t.cblcOffset, t.cblcLength, t.cbdtOffset, t.cbdtLength, out _, out var failure));
            Assert.Equal(ColorBitmapTableFailureCode.CblcStrikeCountUnreasonable, failure.FailureCode);
        }

        [Fact]
        public void Fuzz_Truncated_Cblc_Length_Is_Rejected()
        {
            var t = NotoTables();
            Assert.False(ColorBitmapStrikeTable.TryParse(t.data, t.cblcOffset, 7, t.cbdtOffset, t.cbdtLength, out _, out var f1));
            Assert.Equal(ColorBitmapTableFailureCode.CblcTruncatedHeader, f1.FailureCode);
            // Header intact but the BitmapSize record does not fit.
            Assert.False(ColorBitmapStrikeTable.TryParse(t.data, t.cblcOffset, 20, t.cbdtOffset, t.cbdtLength, out _, out var f2));
            Assert.Equal(ColorBitmapTableFailureCode.CblcBitmapSizeRecordOutOfBounds, f2.FailureCode);
        }

        [Fact]
        public void Fuzz_IndexSubtableList_Pointing_Outside_Cblc_Is_Rejected()
        {
            var t = NotoTables();
            byte[] data = (byte[])t.data.Clone();
            int rec = t.cblcOffset + 8; // BitmapSize[0].indexSubtableListOffset
            data[rec] = 0x7F; data[rec + 1] = 0xFF; data[rec + 2] = 0xFF; data[rec + 3] = 0xFF;
            Assert.False(ColorBitmapStrikeTable.TryParse(data, t.cblcOffset, t.cblcLength, t.cbdtOffset, t.cbdtLength, out _, out var failure));
            Assert.Equal(ColorBitmapTableFailureCode.CblcIndexSubtableListOutOfBounds, failure.FailureCode);
        }

        [Fact]
        public void Fuzz_Glyph_Image_Beyond_Cbdt_Is_Rejected_At_Locate()
        {
            // Parse succeeds (directory is fine) but a shrunken CBDT length makes every image out of bounds.
            var t = NotoTables();
            Assert.True(ColorBitmapStrikeTable.TryParse(t.data, t.cblcOffset, t.cblcLength, t.cbdtOffset, 64, out var table, out _));
            var face = new FontInfo();
            face.stbtt_InitFont(t.data, 0);
            int grinning = face.stbtt_FindGlyphIndex(0x1F600);
            Assert.False(table!.TryLocateGlyph(new ColorBitmapStrikeIndex(0), grinning, out _, out var failure));
            Assert.Equal(ColorBitmapTableFailureCode.GlyphImageDataOutOfBounds, failure.FailureCode);
        }

        [Fact]
        public void Fuzz_Absent_Tables_Report_TablesAbsent()
        {
            Assert.False(ColorBitmapStrikeTable.TryParse(new byte[64], 0, 0, 0, 0, out _, out var failure));
            Assert.Equal(ColorBitmapTableFailureCode.TablesAbsent, failure.FailureCode);
        }
    }
}