namespace StbTrueTypeSharp.TrueTypeHinting.FontFace
{
    /// <summary>Immutable copies of the font-wide TrueType hinting programs and control values.</summary>
    public sealed class TrueTypeHintingFontProgram
    {
        internal TrueTypeHintingFontProgram(TrueTypeTableBytes controlValueTable,
            TrueTypeTableBytes fontProgram, TrueTypeTableBytes controlValueProgram)
        {
            ControlValueTable = controlValueTable;
            FontProgram = fontProgram;
            ControlValueProgram = controlValueProgram;
        }

        /// <summary>The optional cvt table as big-endian FWORD values.</summary>
        public TrueTypeTableBytes ControlValueTable { get; }
        /// <summary>The optional fpgm instruction stream.</summary>
        public TrueTypeTableBytes FontProgram { get; }
        /// <summary>The optional prep instruction stream.</summary>
        public TrueTypeTableBytes ControlValueProgram { get; }
        public bool HasFontProgram => FontProgram.ByteLength != 0;
        public bool HasControlValueProgram => ControlValueProgram.ByteLength != 0;
    }
}