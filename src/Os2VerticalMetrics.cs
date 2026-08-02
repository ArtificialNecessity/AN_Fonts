namespace StbTrueTypeSharp
{
    /// <summary>Vertical design metrics and selection metadata from an OpenType OS/2 table.</summary>
    public readonly struct Os2VerticalMetrics
    {
        public Os2VerticalMetrics(int tableVersion, int fsSelection, int typographicAscent,
            int typographicDescent, int typographicLineGap, int windowsAscent, int windowsDescent)
        {
            TableVersion = tableVersion;
            FsSelection = fsSelection;
            TypographicAscent = typographicAscent;
            TypographicDescent = typographicDescent;
            TypographicLineGap = typographicLineGap;
            WindowsAscent = windowsAscent;
            WindowsDescent = windowsDescent;
        }

        public int TableVersion { get; }
        public int FsSelection { get; }
        public bool UseTypographicMetrics => (FsSelection & 0x0080) != 0;
        public int TypographicAscent { get; }
        public int TypographicDescent { get; }
        public int TypographicLineGap { get; }
        public int WindowsAscent { get; }
        public int WindowsDescent { get; }
    }
}