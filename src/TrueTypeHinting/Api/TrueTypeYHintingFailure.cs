namespace StbTrueTypeSharp.TrueTypeHinting
{
    public enum TrueTypeHintingFailureCode
    {
        None,
        NullFontData,
        InvalidFaceIndex,
        InvalidSfntDirectory,
        MissingRequiredTable,
        TruncatedTable,
        UnsupportedMaxpVersion,
        InvalidGlyphIndex,
        MalformedGlyphData,
        MalformedControlValueTable,
        FontProgramExecutionFailed,
        ControlValueProgramExecutionFailed,
        InterpreterNotImplemented,
    }

    public readonly struct TrueTypeYHintingFailure
    {
        public TrueTypeYHintingFailure(TrueTypeHintingFailureCode failureCode, TrueTypeHintingFailureMessage failureMessage)
        {
            FailureCode = failureCode;
            FailureMessage = failureMessage;
        }

        public TrueTypeHintingFailureCode FailureCode { get; }
        public TrueTypeHintingFailureMessage FailureMessage { get; }
        public bool HasFailure => FailureCode != TrueTypeHintingFailureCode.None;
        public override string ToString() => HasFailure ? FailureCode + ": " + FailureMessage : "None";
    }
}