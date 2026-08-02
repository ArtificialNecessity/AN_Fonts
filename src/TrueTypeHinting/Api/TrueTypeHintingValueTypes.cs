using System;

namespace StbTrueTypeSharp.TrueTypeHinting
{
    public readonly struct TrueTypeFaceIndex
    {
        public TrueTypeFaceIndex(int trueTypeFaceIndexValue)
        {
            if (trueTypeFaceIndexValue < 0) throw new ArgumentOutOfRangeException(nameof(trueTypeFaceIndexValue));
            Value = trueTypeFaceIndexValue;
        }
        public int Value { get; }
    }

    public readonly struct TrueTypeUnitsPerEm
    {
        public TrueTypeUnitsPerEm(int trueTypeUnitsPerEmValue)
        {
            if (trueTypeUnitsPerEmValue <= 0) throw new ArgumentOutOfRangeException(nameof(trueTypeUnitsPerEmValue));
            Value = trueTypeUnitsPerEmValue;
        }
        public int Value { get; }
    }

    public readonly struct TrueTypeGlyphCount
    {
        public TrueTypeGlyphCount(int trueTypeGlyphCountValue)
        {
            if (trueTypeGlyphCountValue < 0) throw new ArgumentOutOfRangeException(nameof(trueTypeGlyphCountValue));
            Value = trueTypeGlyphCountValue;
        }
        public int Value { get; }
    }

    public readonly struct TrueTypeHintingFailureMessage
    {
        public TrueTypeHintingFailureMessage(string trueTypeHintingFailureMessageValue)
            => Value = trueTypeHintingFailureMessageValue ?? string.Empty;
        public string Value { get; }
        public override string ToString() => Value ?? string.Empty;
    }
}