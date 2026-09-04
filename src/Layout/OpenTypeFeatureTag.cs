using System;

namespace StbTrueTypeSharp.Layout
{
	/// <summary>Branded 4-byte OpenType feature tag (big-endian packed). Prevents bare-uint tag soup in signatures.</summary>
	public readonly struct OpenTypeFeatureTag : IEquatable<OpenTypeFeatureTag>
	{
		public readonly uint Value;

		public OpenTypeFeatureTag(char c0, char c1, char c2, char c3)
			=> Value = ((uint)(byte)c0 << 24) | ((uint)(byte)c1 << 16) | ((uint)(byte)c2 << 8) | (byte)c3;

		/// <summary>Glyph composition/decomposition — the emoji sequence feature.</summary>
		public static readonly OpenTypeFeatureTag Ccmp = new OpenTypeFeatureTag('c', 'c', 'm', 'p');
		public static readonly OpenTypeFeatureTag Liga = new OpenTypeFeatureTag('l', 'i', 'g', 'a');
		public static readonly OpenTypeFeatureTag Rlig = new OpenTypeFeatureTag('r', 'l', 'i', 'g');
		public static readonly OpenTypeFeatureTag Clig = new OpenTypeFeatureTag('c', 'l', 'i', 'g');
		/// <summary>Required Variation Alternates (folded into the same pass — ONE GSUB applier in the tree).</summary>
		public static readonly OpenTypeFeatureTag Rvrn = new OpenTypeFeatureTag('r', 'v', 'r', 'n');

		public bool Equals(OpenTypeFeatureTag other) => Value == other.Value;
		public override bool Equals(object obj) => obj is OpenTypeFeatureTag t && t.Value == Value;
		public override int GetHashCode() => (int)Value;
		public override string ToString()
			=> new string(new[] { (char)(Value >> 24), (char)((Value >> 16) & 0xFF), (char)((Value >> 8) & 0xFF), (char)(Value & 0xFF) });
	}
}