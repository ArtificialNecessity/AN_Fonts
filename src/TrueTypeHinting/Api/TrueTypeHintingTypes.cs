using System;

namespace StbTrueTypeSharp.TrueTypeHinting
{
    /// <summary>Vertical device pixels per em used to create one hinting size instance.</summary>
    public readonly struct DevicePpemY : IEquatable<DevicePpemY>
    {
        public DevicePpemY(int devicePixelsPerEmY)
        {
            if (devicePixelsPerEmY <= 0 || devicePixelsPerEmY > 0xFFFF)
                throw new ArgumentOutOfRangeException(nameof(devicePixelsPerEmY));
            Value = devicePixelsPerEmY;
        }

        public int Value { get; }
        public bool Equals(DevicePpemY other) => Value == other.Value;
        public override bool Equals(object obj) => obj is DevicePpemY other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value + " ppem";
    }

    /// <summary>A glyph index in one specific TrueType font face.</summary>
    public readonly struct TrueTypeGlyphIndex : IEquatable<TrueTypeGlyphIndex>
    {
        public TrueTypeGlyphIndex(int glyphIndex)
        {
            if (glyphIndex < 0 || glyphIndex > 0xFFFF)
                throw new ArgumentOutOfRangeException(nameof(glyphIndex));
            Value = glyphIndex;
        }

        public int Value { get; }
        public bool Equals(TrueTypeGlyphIndex other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TrueTypeGlyphIndex other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => "glyph " + Value;
    }
}