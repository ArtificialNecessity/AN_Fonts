using StbTrueTypeSharp.ColorBitmap;

namespace StbTrueTypeSharp
{
    public partial class FontInfo
    {
        private ColorBitmapStrikeTable _colorBitmapStrikeTable;
        private ColorBitmapTableFailure _colorBitmapStrikeTableFailure;
        private bool _colorBitmapStrikeTableResolved;

        /// <summary>
        /// Parsed CBLC/CBDT strike table (plans/color_glyph_fonts.md §4.1), parsed ONCE on first request and
        /// cached for the face lifetime. Returns null when the face has no CBLC+CBDT or they are malformed —
        /// the reason is in <see cref="stbtt_GetColorBitmapStrikeTableFailure"/>. Never throws.
        /// </summary>
        public ColorBitmapStrikeTable stbtt_GetColorBitmapStrikeTable()
        {
            if (_colorBitmapStrikeTableResolved)
                return _colorBitmapStrikeTable;
            _colorBitmapStrikeTableResolved = true;
            if (this.cblc == 0 || this.cbdt == 0)
            {
                _colorBitmapStrikeTableFailure = new ColorBitmapTableFailure(ColorBitmapTableFailureCode.TablesAbsent, "no CBLC/CBDT");
                return null;
            }
            byte[] fontData = this.data.UnderlyingArray;
            int cblcLength = stbtt__find_table_length("CBLC");
            int cbdtLength = stbtt__find_table_length("CBDT");
            if (!ColorBitmapStrikeTable.TryParse(fontData, this.cblc, cblcLength, this.cbdt, cbdtLength,
                    out _colorBitmapStrikeTable, out _colorBitmapStrikeTableFailure))
            {
                _colorBitmapStrikeTable = null;
            }
            return _colorBitmapStrikeTable;
        }

        /// <summary>Why <see cref="stbtt_GetColorBitmapStrikeTable"/> returned null (None when it succeeded or was never called).</summary>
        public ColorBitmapTableFailure stbtt_GetColorBitmapStrikeTableFailure()
            => _colorBitmapStrikeTableResolved ? _colorBitmapStrikeTableFailure : ColorBitmapTableFailure.None;

        /// <summary>
        /// True when <paramref name="glyph_index"/> has a colour bitmap in SOME colour strike of this face.
        /// The per-glyph source classification primitive (plans/color_glyph_fonts.md §2): a font may carry
        /// bitmaps for only part of its glyph set.
        /// </summary>
        public bool stbtt_GlyphHasColorBitmap(int glyph_index)
        {
            ColorBitmapStrikeTable table = stbtt_GetColorBitmapStrikeTable();
            if (table == null) return false;
            for (int s = 0; s < table.StrikeCount; s++)
            {
                var index = new ColorBitmapStrikeIndex(s);
                if (!table.GetStrike(index).IsColour) continue;
                if (table.TryLocateGlyph(index, glyph_index, out _, out _)) return true;
            }
            return false;
        }
    }
}