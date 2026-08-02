using System;

namespace StbTrueTypeSharp.TrueTypeHinting.FontFace
{
    /// <summary>Four-byte SFNT table identifier.</summary>
    internal readonly struct TrueTypeTableTag : IEquatable<TrueTypeTableTag>
    {
        internal TrueTypeTableTag(uint trueTypeTableTagValue) => Value = trueTypeTableTagValue;
        internal uint Value { get; }
        public bool Equals(TrueTypeTableTag other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TrueTypeTableTag other && Equals(other);
        public override int GetHashCode() => unchecked((int)Value);
        public override string ToString() => new string(new[]
        {
            (char)(Value >> 24), (char)(Value >> 16), (char)(Value >> 8), (char)Value,
        });

        internal static TrueTypeTableTag FromAscii(string trueTypeTableTagText)
        {
            if (trueTypeTableTagText == null || trueTypeTableTagText.Length != 4)
                throw new ArgumentException("An SFNT tag must contain exactly four characters.", nameof(trueTypeTableTagText));
            return new TrueTypeTableTag((uint)(trueTypeTableTagText[0] << 24 | trueTypeTableTagText[1] << 16 |
                trueTypeTableTagText[2] << 8 | trueTypeTableTagText[3]));
        }
    }

    /// <summary>Immutable, bounds-validated copy of one SFNT table.</summary>
    public sealed class TrueTypeTableBytes
    {
        private readonly byte[] _trueTypeTableBytes;

        internal TrueTypeTableBytes(byte[] trueTypeTableBytes)
            => _trueTypeTableBytes = trueTypeTableBytes ?? throw new ArgumentNullException(nameof(trueTypeTableBytes));

        public int ByteLength => _trueTypeTableBytes.Length;
        public byte[] ToByteArray() => (byte[])_trueTypeTableBytes.Clone();
        internal byte[] CloneBytes() => (byte[])_trueTypeTableBytes.Clone();
    }

    internal readonly struct TrueTypeTableRange
    {
        internal TrueTypeTableRange(int trueTypeTableByteOffset, int trueTypeTableByteLength)
        {
            ByteOffset = trueTypeTableByteOffset;
            ByteLength = trueTypeTableByteLength;
        }

        internal int ByteOffset { get; }
        internal int ByteLength { get; }
    }
}