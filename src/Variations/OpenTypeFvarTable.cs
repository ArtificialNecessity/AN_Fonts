using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Variations
{
	/// <summary>One fvar VariationAxisRecord: user-scale range + flags. Values are Fixed 16.16 in the file.</summary>
	public readonly struct FontVariationAxisRecord
	{
		public OpenTypeAxisTag AxisTag { get; }
		public double MinValue { get; }
		public double DefaultValue { get; }
		public double MaxValue { get; }
		/// <summary>fvar flags bit 0 HIDDEN_AXIS: the font developer asks UIs not to expose this axis.</summary>
		public bool IsHidden { get; }
		public ushort AxisNameId { get; }

		public FontVariationAxisRecord(OpenTypeAxisTag axisTag, double minValue, double defaultValue, double maxValue, bool isHidden, ushort axisNameId)
		{
			AxisTag = axisTag;
			MinValue = minValue;
			DefaultValue = defaultValue;
			MaxValue = maxValue;
			IsHidden = isHidden;
			AxisNameId = axisNameId;
		}
	}

	/// <summary>One fvar InstanceRecord (named instance) — exposed for tooling only; the engine never selects by name.</summary>
	public readonly struct FontVariationNamedInstanceRecord
	{
		public ushort SubfamilyNameId { get; }
		/// <summary>User-scale coordinates in fvar axis order.</summary>
		public double[] UserCoordinates { get; }
		/// <summary>0xFFFF when the record carries no PostScript name id (or the optional field is absent).</summary>
		public ushort PostScriptNameId { get; }

		public FontVariationNamedInstanceRecord(ushort subfamilyNameId, double[] userCoordinates, ushort postScriptNameId)
		{
			SubfamilyNameId = subfamilyNameId;
			UserCoordinates = userCoordinates;
			PostScriptNameId = postScriptNameId;
		}
	}

	/// <summary>
	/// Parsed 'fvar' table (OpenType 1.9.1 — _EXTERNAL_APIS/OpenType/fvar.md). Axis order here IS the
	/// order every other variation table (gvar tuples, avar segment maps, ItemVariationStore regions) uses.
	/// </summary>
	public sealed class OpenTypeFvarTable
	{
		public FontVariationAxisRecord[] Axes { get; }
		public FontVariationNamedInstanceRecord[] NamedInstances { get; }
		public int AxisCount => Axes.Length;

		private OpenTypeFvarTable(FontVariationAxisRecord[] axes, FontVariationNamedInstanceRecord[] namedInstances)
		{
			Axes = axes;
			NamedInstances = namedInstances;
		}

		/// <summary>Index of the axis with this tag in fvar order, or -1 (unknown tags are IGNORED by callers, never an error).</summary>
		public int FindAxisIndex(OpenTypeAxisTag axisTag)
		{
			for (int i = 0; i < Axes.Length; i++)
				if (Axes[i].AxisTag == axisTag)
					return i;
			return -1;
		}

		/// <summary>Fixed 16.16 big-endian to double.</summary>
		internal static double ReadFixed16Dot16(FakePtr<byte> p) => ttLONG(p) / 65536.0;

		public static bool TryParse(FakePtr<byte> fontData, int tableOffset, int tableLength,
			out OpenTypeFvarTable table, out FontVariationParseFailure failure)
		{
			table = null;
			if (tableLength < 16 || tableOffset < 0 || tableOffset + tableLength > fontData.RemainingLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.FvarTruncatedHeader, "fvar", "table shorter than the 16-byte header or outside the font data");
				return false;
			}
			var t = fontData + tableOffset;
			ushort majorVersion = ttUSHORT(t);
			if (majorVersion != 1)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.FvarUnsupportedVersion, "fvar", "majorVersion " + majorVersion);
				return false;
			}
			int axesArrayOffset = ttUSHORT(t + 4);
			int axisCount = ttUSHORT(t + 8);
			int axisSize = ttUSHORT(t + 10);
			int instanceCount = ttUSHORT(t + 12);
			int instanceSize = ttUSHORT(t + 14);
			if (axisCount == 0)
			{
				// Spec: axisCount 0 => not functional as a variable font; treat as static.
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.FvarAxisCountZero, "fvar", "axisCount is 0");
				return false;
			}
			if (axisSize < 20)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.FvarAxisRecordOutOfBounds, "fvar", "axisSize " + axisSize + " < 20");
				return false;
			}
			long axesEnd = (long)axesArrayOffset + (long)axisCount * axisSize;
			if (axesEnd > tableLength)
			{
				failure = new FontVariationParseFailure(FontVariationParseFailureCode.FvarAxisRecordOutOfBounds, "fvar", "axis array ends at " + axesEnd + " > tableLength " + tableLength);
				return false;
			}

			var axes = new FontVariationAxisRecord[axisCount];
			for (int i = 0; i < axisCount; i++)
			{
				var a = t + axesArrayOffset + i * axisSize;
				var tag = new OpenTypeAxisTag(ttULONG(a));
				double min = ReadFixed16Dot16(a + 4);
				double def = ReadFixed16Dot16(a + 8);
				double max = ReadFixed16Dot16(a + 12);
				ushort flags = ttUSHORT(a + 16);
				ushort nameId = ttUSHORT(a + 18);
				if (!(min <= def && def <= max))
				{
					failure = new FontVariationParseFailure(FontVariationParseFailureCode.FvarAxisRangeInvalid, "fvar", "axis '" + tag + "' has min/default/max " + min + "/" + def + "/" + max);
					return false;
				}
				axes[i] = new FontVariationAxisRecord(tag, min, def, max, (flags & 0x0001) != 0, nameId);
			}

			// Named instances: instanceSize is axisCount*4 + 4 (no psNameId) or + 6 (with). Records follow the axes array.
			var instances = new FontVariationNamedInstanceRecord[0];
			if (instanceCount > 0)
			{
				int minimumInstanceSize = axisCount * 4 + 4;
				long instancesEnd = axesEnd + (long)instanceCount * instanceSize;
				if (instanceSize < minimumInstanceSize || instancesEnd > tableLength)
				{
					// Named instances are tooling-only: a malformed instance array must NOT reject the font's axes.
					instances = new FontVariationNamedInstanceRecord[0];
				}
				else
				{
					bool hasPostScriptNameId = instanceSize >= axisCount * 4 + 6;
					instances = new FontVariationNamedInstanceRecord[instanceCount];
					for (int i = 0; i < instanceCount; i++)
					{
						var r = t + (int)axesEnd + i * instanceSize;
						ushort subfamilyNameId = ttUSHORT(r);
						var coords = new double[axisCount];
						for (int a = 0; a < axisCount; a++)
							coords[a] = ReadFixed16Dot16(r + 4 + a * 4);
						ushort psNameId = hasPostScriptNameId ? ttUSHORT(r + 4 + axisCount * 4) : (ushort)0xFFFF;
						instances[i] = new FontVariationNamedInstanceRecord(subfamilyNameId, coords, psNameId);
					}
				}
			}

			table = new OpenTypeFvarTable(axes, instances);
			failure = FontVariationParseFailure.None;
			return true;
		}
	}
}