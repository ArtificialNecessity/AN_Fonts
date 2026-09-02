using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.Variations;
using Xunit;

namespace Variations.Tests
{
	/// <summary>
	/// The fixture set (Fonts/FIXTURES.md): each instanced twin is "{stem}.instanced.{tagValue}[_{tagValue}].ttf" or
	/// "{stem}.instanced.default.ttf"; the source VF for a stem is fixed here. The design position is parsed
	/// from the file name, so adding a position to generate_instanced_fixtures.py needs no test edit.
	/// </summary>
	public static class VariationFixtureCatalog
	{
		public static readonly string FontsDirectory = Path.Combine(AppContext.BaseDirectory, "Fonts");

		private static readonly Dictionary<string, string> SourceVariableFontByStem = new Dictionary<string, string>
		{
			{ "Inter.var.subset", "Inter.var.subset.ttf" },
			{ "NotoSansSymbols-VariableFont_wght", "NotoSansSymbols-VariableFont_wght.ttf" },
			{ "RobotoFlex", "RobotoFlex-VariableFont_GRAD,XOPQ,XTRA,YOPQ,YTAS,YTDE,YTFI,YTLC,YTUC,opsz,slnt,wdth,wght.ttf" },
		};

		/// <summary>xunit MemberData: every *.instanced.*.ttf in Fonts/.</summary>
		public static IEnumerable<object[]> AllInstancedFixtures()
		{
			var files = Directory.GetFiles(FontsDirectory, "*.instanced.*.ttf");
			Array.Sort(files, StringComparer.Ordinal);
			foreach (var file in files)
				yield return new object[] { Path.GetFileName(file) };
		}

		public static string SourceVariableFontFileFor(string instancedFileName)
		{
			string stem = instancedFileName.Substring(0, instancedFileName.IndexOf(".instanced.", StringComparison.Ordinal));
			return SourceVariableFontByStem[stem];
		}

		/// <summary>"wght700_wdth75_opsz36_slnt-5" -> user axis values; "default" -> empty.</summary>
		public static FontVariationUserAxisValue[] DesignPositionOf(string instancedFileName)
		{
			int start = instancedFileName.IndexOf(".instanced.", StringComparison.Ordinal) + ".instanced.".Length;
			string position = instancedFileName.Substring(start, instancedFileName.Length - start - ".ttf".Length);
			if (position == "default")
				return new FontVariationUserAxisValue[0];
			var parts = position.Split('_');
			var values = new FontVariationUserAxisValue[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				string tag = parts[i].Substring(0, 4);
				double value = double.Parse(parts[i].Substring(4), CultureInfo.InvariantCulture);
				values[i] = new FontVariationUserAxisValue(OpenTypeAxisTag.FromString(tag), value);
			}
			return values;
		}

		public static FontInfo LoadFont(string fileName)
		{
			byte[] bytes = File.ReadAllBytes(Path.Combine(FontsDirectory, fileName));
			var info = new FontInfo();
			Assert.True(info.stbtt_InitFont(bytes, 0) != 0, "stbtt_InitFont failed for " + fileName);
			return info;
		}

		public static byte[] LoadFontBytes(string fileName) => File.ReadAllBytes(Path.Combine(FontsDirectory, fileName));
	}
}