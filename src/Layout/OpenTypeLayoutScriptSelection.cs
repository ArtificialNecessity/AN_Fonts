using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Layout
{
	/// <summary>
	/// The ONE script/language-system selection policy for GSUB and GPOS (OpenType Layout Common Table Formats §"Scripts
	/// and languages"): prefer the 'latn' Script, fall back to 'DFLT', and use the script's DEFAULT LangSys. Extracted from
	/// the GPOS pair-kerning kernel (Part D) so glyph substitution and glyph positioning can never disagree about which
	/// LangSys is in force. Latin-centric by design (README "Limitations"): no per-language systems, no complex scripts.
	/// </summary>
	public static class OpenTypeLayoutScriptSelection
	{
		/// <summary>
		/// <paramref name="scriptList"/> is the ScriptList table start. Returns false when neither 'latn' nor 'DFLT' exists or
		/// the chosen Script has a NULL defaultLangSysOffset (nothing to apply). Bounds: scriptCount records are checked
		/// against <paramref name="bytesAvailable"/> (bytes from scriptList to the end of the parent table).
		/// </summary>
		public static bool TryGetDefaultLangSys(FakePtr<byte> scriptList, int bytesAvailable, out FakePtr<byte> langSys)
		{
			langSys = FakePtr<byte>.Null;
			if (bytesAvailable < 2) return false;
			int scriptCount = ttUSHORT(scriptList);
			if (2 + scriptCount * 6L > bytesAvailable) return false;
			var latnScriptTable = FakePtr<byte>.Null;
			var dfltScriptTable = FakePtr<byte>.Null;
			for (int si = 0; si < scriptCount; si++)
			{
				var rec = scriptList + 2 + 6 * si;
				int scriptOffset = ttUSHORT(rec + 4);
				if (scriptOffset + 4 > bytesAvailable) continue; // Script table header (defaultLangSysOffset + langSysCount) must fit
				if (rec[0] == 'l' && rec[1] == 'a' && rec[2] == 't' && rec[3] == 'n')
					latnScriptTable = scriptList + scriptOffset;
				else if (rec[0] == 'D' && rec[1] == 'F' && rec[2] == 'L' && rec[3] == 'T')
					dfltScriptTable = scriptList + scriptOffset;
			}
			var scriptTable = latnScriptTable.IsNull ? dfltScriptTable : latnScriptTable;
			if (scriptTable.IsNull) return false;
			int defaultLangSysOffset = ttUSHORT(scriptTable);
			if (defaultLangSysOffset == 0) return false;
			var candidate = scriptTable + defaultLangSysOffset;
			// LangSys header: lookupOrderOffset(u16) requiredFeatureIndex(u16) featureIndexCount(u16) featureIndices[].
			long langSysStartFromList = (long)(candidate.Offset - scriptList.Offset);
			if (langSysStartFromList + 6 > bytesAvailable) return false;
			if (langSysStartFromList + 6 + ttUSHORT(candidate + 4) * 2L > bytesAvailable) return false;
			langSys = candidate;
			return true;
		}

		/// <summary>LangSys.requiredFeatureIndex, or -1 when 0xFFFF (none).</summary>
		public static int GetRequiredFeatureIndex(FakePtr<byte> langSys)
		{
			int required = ttUSHORT(langSys + 2);
			return required == 0xFFFF ? -1 : required;
		}

		public static int GetFeatureIndexCount(FakePtr<byte> langSys) => ttUSHORT(langSys + 4);

		public static int GetFeatureIndex(FakePtr<byte> langSys, int i) => ttUSHORT(langSys + 6 + 2 * i);
	}
}