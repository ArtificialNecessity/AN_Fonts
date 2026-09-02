using System;

namespace StbTrueTypeSharp.Variations
{
	/// <summary>
	/// A four-byte OpenType design-variation axis tag ("wght", "wdth", "slnt", "ital", "opsz", or a
	/// foundry tag such as "GRAD"), packed big-endian exactly as stored in fvar. Value type; equality is
	/// the packed value. Registered tags are lowercase; foundry tags are uppercase (OT spec).
	/// </summary>
	public readonly struct OpenTypeAxisTag : IEquatable<OpenTypeAxisTag>
	{
		public uint PackedValue { get; }

		public OpenTypeAxisTag(uint packedValue) => PackedValue = packedValue;

		public static OpenTypeAxisTag FromString(string fourCharacterTag)
		{
			if (fourCharacterTag == null || fourCharacterTag.Length != 4)
				throw new ArgumentException("An OpenType axis tag must be exactly four characters.", nameof(fourCharacterTag));
			return new OpenTypeAxisTag(
				((uint)(byte)fourCharacterTag[0] << 24) | ((uint)(byte)fourCharacterTag[1] << 16) |
				((uint)(byte)fourCharacterTag[2] << 8) | (byte)fourCharacterTag[3]);
		}

		public static readonly OpenTypeAxisTag Weight = FromString("wght");
		public static readonly OpenTypeAxisTag Width = FromString("wdth");
		public static readonly OpenTypeAxisTag Slant = FromString("slnt");
		public static readonly OpenTypeAxisTag Italic = FromString("ital");
		public static readonly OpenTypeAxisTag OpticalSize = FromString("opsz");

		public bool Equals(OpenTypeAxisTag other) => PackedValue == other.PackedValue;
		public override bool Equals(object obj) => obj is OpenTypeAxisTag other && Equals(other);
		public override int GetHashCode() => unchecked((int)PackedValue);
		public static bool operator ==(OpenTypeAxisTag left, OpenTypeAxisTag right) => left.PackedValue == right.PackedValue;
		public static bool operator !=(OpenTypeAxisTag left, OpenTypeAxisTag right) => left.PackedValue != right.PackedValue;

		public override string ToString() => new string(new[]
		{
			(char)((PackedValue >> 24) & 0xFF), (char)((PackedValue >> 16) & 0xFF),
			(char)((PackedValue >> 8) & 0xFF), (char)(PackedValue & 0xFF),
		});
	}
}