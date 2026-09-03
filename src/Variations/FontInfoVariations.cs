using System;
using StbTrueTypeSharp.Variations;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp
{
	/// <summary>
	/// OpenType Font Variations entry points (SilkyNvg plans/font_variable_support_fvar_avar_gvar_hvar.md;
	/// algorithm + fontTools-parity rules in _EXTERNAL_APIS/OpenType/README_variations_implementation_notes.md).
	/// Variation tables are located at InitFont and PARSED LAZILY on first use; a malformed table makes the
	/// font behave as a static font (stbtt_HasVariations == false) with the reason in
	/// stbtt_VariationParseFailure — nothing here ever throws at draw time.
	/// </summary>
	public partial class FontInfo
	{
		// Table directory offsets (0 = absent), resolved in stbtt_InitFont_internal alongside the classic tables.
		public int fvar;
		public int avar;
		public int gvar;
		public int hvar;
		public int mvar;
		/// <summary>GDEF table offset (0 = absent). Only its v1.3 ItemVariationStore is consumed (GPOS VariationIndex deltas).</summary>
		public int gdef;

		private OpenTypeVariationTables _variationTables;
		private bool _variationTablesResolved;
		private FontVariationParseFailure _variationParseFailure = FontVariationParseFailure.None;

		/// <summary>Byte length of a table from the sfnt directory (0 when absent) — needed for bounds-validated parsing.</summary>
		internal int stbtt__find_table_length(string tag)
		{
			int num_tables = ttUSHORT(this.data + this.fontstart + 4);
			var tabledir = this.fontstart + 12;
			for (int i = 0; i < num_tables; ++i)
			{
				var loc = tabledir + 16 * i;
				var p = this.data + loc;
				if (p[0] == tag[0] && p[1] == tag[1] && p[2] == tag[2] && p[3] == tag[3])
					return (int)ttULONG(p + 12);
			}
			return 0;
		}

		/// <summary>True when the font carries a parseable fvar (a functional variable font).</summary>
		public bool stbtt_HasVariations => stbtt__GetVariationTables() != null;

		/// <summary>Why the variation tables were rejected (None when fine or when the font is simply static).</summary>
		public FontVariationParseFailure stbtt_VariationParseFailure
		{
			get { stbtt__GetVariationTables(); return _variationParseFailure; }
		}

		/// <summary>fvar axes in fvar order (empty array for a static font). Consumers use this to decide
		/// "face HAS wght => set the axis" vs "static face => font-synthesis fallback".</summary>
		public FontVariationAxisRecord[] stbtt_GetVariationAxes()
		{
			var tables = stbtt__GetVariationTables();
			return tables == null ? new FontVariationAxisRecord[0] : tables.Fvar.Axes;
		}

		/// <summary>fvar named instances (tooling only; empty for static fonts).</summary>
		public FontVariationNamedInstanceRecord[] stbtt_GetVariationNamedInstances()
		{
			var tables = stbtt__GetVariationTables();
			return tables == null ? new FontVariationNamedInstanceRecord[0] : tables.Fvar.NamedInstances;
		}

		/// <summary>
		/// User-space request -> normalized F2Dot14 tuple (README §1): clamp to [min,max], default-normalize,
		/// avar remap in double, quantize ONCE to F2Dot14 with otRound. Unknown tags are IGNORED (CSS Fonts 4
		/// §11.1); unspecified axes sit at their default. Static font or empty request => Default.
		/// </summary>
		public FontVariationNormalizedCoordinates stbtt_NormalizeVariationRequest(FontVariationUserAxisValue[] userAxisValues)
		{
			var tables = stbtt__GetVariationTables();
			if (tables == null || userAxisValues == null || userAxisValues.Length == 0)
				return FontVariationNormalizedCoordinates.Default;
			var fvarTable = tables.Fvar;
			var f2dot14 = new short[fvarTable.AxisCount];
			bool anyAxisTouched = false;
			for (int i = 0; i < userAxisValues.Length; i++)
			{
				int axisIndex = fvarTable.FindAxisIndex(userAxisValues[i].AxisTag);
				if (axisIndex < 0)
					continue; // unknown tag: ignored, never an error
				f2dot14[axisIndex] = FontVariationNormalization.NormalizeUserValueToF2Dot14(
					userAxisValues[i].UserValue, fvarTable.Axes[axisIndex], tables.Avar, axisIndex);
				anyAxisTouched = true;
			}
			return anyAxisTouched ? new FontVariationNormalizedCoordinates(f2dot14) : FontVariationNormalizedCoordinates.Default;
		}

		/// <summary>Lazily parses fvar (+ avar, gvar, HVAR, MVAR when present). Null => static font or rejected tables.</summary>
		internal OpenTypeVariationTables stbtt__GetVariationTables()
		{
			if (_variationTablesResolved)
				return _variationTables;
			_variationTablesResolved = true;
			_variationTables = null;
			if (this.fvar == 0)
				return null; // static font: no failure, no variations
			if (!OpenTypeFvarTable.TryParse(this.data, this.fvar, stbtt__find_table_length("fvar"), out OpenTypeFvarTable fvarTable, out _variationParseFailure))
				return null;

			OpenTypeAvarTable avarTable = null;
			if (this.avar != 0 && !OpenTypeAvarTable.TryParse(this.data, this.avar, stbtt__find_table_length("avar"), fvarTable.AxisCount, out avarTable, out _variationParseFailure))
				return null;

			OpenTypeGvarTable gvarTable = null;
			if (this.gvar != 0 && !OpenTypeGvarTable.TryParse(this.data, this.gvar, stbtt__find_table_length("gvar"), fvarTable.AxisCount, this.numGlyphs, out gvarTable, out _variationParseFailure))
				return null;

			OpenTypeHvarTable hvarTable = null;
			if (this.hvar != 0 && !OpenTypeHvarTable.TryParse(this.data, this.hvar, stbtt__find_table_length("HVAR"), fvarTable.AxisCount, out hvarTable, out _variationParseFailure))
				return null;

			OpenTypeMvarTable mvarTable = null;
			if (this.mvar != 0 && !OpenTypeMvarTable.TryParse(this.data, this.mvar, stbtt__find_table_length("MVAR"), fvarTable.AxisCount, out mvarTable, out _variationParseFailure))
				return null;

			// GDEF ItemVariationStore is OPTIONAL and fail-soft: a bad store must not turn a working VF static —
			// it only means kerning stays at the default instance. Failure is still recorded for diagnostics.
			OpenTypeItemVariationStore gdefStore = stbtt__TryParseGdefItemVariationStore(fvarTable.AxisCount);

			_variationTables = new OpenTypeVariationTables(fvarTable, avarTable, gvarTable, hvarTable, mvarTable, gdefStore);
			return _variationTables;
		}

		/// <summary>
		/// GDEF header (README §7): majorVersion(u16)=1, minorVersion(u16), then Offset16 ×5 — glyphClassDef, attachList,
		/// ligCaretList, markAttachClassDef, [v1.2+] markGlyphSetsDef — so the v1.3 Offset32 itemVarStoreOffset sits at
		/// byte 14 (4 + 5×2). Bring-up bug: reading it at byte 12 returned markGlyphSetsDef<<16, a garbage offset that
		/// failed the bounds guard SILENTLY and left kerning un-varied — hence the explicit failure record below.
		/// Returns null for no GDEF, version &lt; 1.3, NULL offset, or a store outside the table; a malformed store is
		/// recorded in _variationParseFailure and also yields null.
		/// </summary>
		private OpenTypeItemVariationStore stbtt__TryParseGdefItemVariationStore(int fvarAxisCount)
		{
			if (this.gdef == 0)
				return null;
			int gdefLength = stbtt__find_table_length("GDEF");
			if (gdefLength < 18 || this.gdef + gdefLength > this.data.RemainingLength)
				return null;
			var g = this.data + this.gdef;
			if (ttUSHORT(g) != 1 || ttUSHORT(g + 2) < 3)
				return null;
			long storeOffset = ttULONG(g + 14);
			if (storeOffset == 0)
				return null;
			if (storeOffset >= gdefLength)
			{
				_variationParseFailure = new FontVariationParseFailure(FontVariationParseFailureCode.ItemVariationStoreTruncated, "GDEF", "itemVarStoreOffset " + storeOffset + " beyond GDEF length " + gdefLength);
				return null;
			}
			if (!OpenTypeItemVariationStore.TryParse(this.data, this.gdef + (int)storeOffset, gdefLength - (int)storeOffset, fvarAxisCount, "GDEF",
					out OpenTypeItemVariationStore store, out FontVariationParseFailure failure))
			{
				_variationParseFailure = failure;
				return null;
			}
			return store;
		}

		// \u2500\u2500 outline production at a variation instance (README \u00a73) \u2500\u2500

		/// <summary>", "
		/// stbtt_GetGlyphShape at a normalized variation instance. Default coords, static fonts, fonts without gvar,
		/// and CFF outlines (CFF2 blend = Part B) all route to the UN-VARIED path byte-for-byte (the no-op proof).
		/// </summary>
		public int stbtt_GetGlyphShapeVar(int glyph_index, in FontVariationNormalizedCoordinates coords, out stbtt_vertex[] pvertices)
		{
			if (coords.IsDefault || this.cff.size != 0 || stbtt__GetVariationTables()?.Gvar == null)
				return stbtt_GetGlyphShape(glyph_index, out pvertices);
			return stbtt__GetGlyphShapeTT(glyph_index, coords, out pvertices);
		}

		/// <summary>Varied outline THEN the synthetic-style transform (order fixed by the spec's Design invariant).</summary>
		public int stbtt_GetGlyphShapeVarSynth(int glyph_index, in FontVariationNormalizedCoordinates coords, float skewX, float scaleX,
			float emboldenFontUnits, out stbtt_vertex[] pvertices)
		{
			int num_verts = stbtt_GetGlyphShapeVar(glyph_index, coords, out pvertices);
			if (num_verts <= 0)
				return num_verts;
			return SynthOutline.SynthesizeOutline(ref pvertices, num_verts, skewX, scaleX, emboldenFontUnits);
		}

		/// <summary>
		/// The gvar point list for a glyph = its outline points (or components) followed by the 4 phantom points:
		/// left = (xMin - lsb, 0), right = (left.x + advance, 0), top/bottom = (0, 0) (no vmtx support; vertical
		/// phantoms never influence horizontal layout or outlines). Default positions come from glyf bbox + hmtx.
		/// </summary>
		private void stbtt__FillPhantomPointDefaults(int glyph_index, int glyfOffset, int[] px, int[] py, int firstPhantomIndex)
		{
			int advanceWidth = 0, leftSideBearing = 0;
			stbtt_GetGlyphHMetrics(glyph_index, ref advanceWidth, ref leftSideBearing);
			int xMin = glyfOffset >= 0 ? ttSHORT(this.data + glyfOffset + 2) : 0;
			int leftSideX = xMin - leftSideBearing;
			px[firstPhantomIndex + 0] = leftSideX; py[firstPhantomIndex + 0] = 0;
			px[firstPhantomIndex + 1] = leftSideX + advanceWidth; py[firstPhantomIndex + 1] = 0;
			px[firstPhantomIndex + 2] = 0; py[firstPhantomIndex + 2] = 0;
			px[firstPhantomIndex + 3] = 0; py[firstPhantomIndex + 3] = 0;
		}

		/// <summary>
		/// Applies gvar deltas to a SIMPLE glyph's materialized point arrays (vertices[off + i].x/.y for i in [0, n))
		/// before stbtt__GetGlyphShapeTT emits vmove/vline/vcurve \u2014 flag/contour logic downstream is untouched.
		/// Sum in double over all tuples, round ONCE (otRound), clamp to short. On malformed gvar data the glyph
		/// is left un-varied and the failure is recorded (never thrown).
		/// </summary>
		private void stbtt__ApplyGlyphVariationToSimpleGlyphPoints(int glyph_index, in FontVariationNormalizedCoordinates coords,
			stbtt_vertex[] vertices, int off, int n, FakePtr<byte> endPtsOfContours, int numberOfContours, int glyfOffset)
		{
			var gvarTable = stbtt__GetVariationTables()?.Gvar;
			if (gvarTable == null) return;
			int pointCount = n + OpenTypeGvarTable.PhantomPointCount;
			var px = new int[pointCount];
			var py = new int[pointCount];
			for (int i = 0; i < n; i++) { px[i] = vertices[off + i].x; py[i] = vertices[off + i].y; }
			stbtt__FillPhantomPointDefaults(glyph_index, glyfOffset, px, py, n);
			var contourEnds = new ushort[numberOfContours];
			for (int c = 0; c < numberOfContours; c++)
				contourEnds[c] = ttUSHORT(endPtsOfContours + c * 2);
			var dx = new double[pointCount];
			var dy = new double[pointCount];
			if (!gvarTable.TryComputePointDeltas(glyph_index, coords, px, py, pointCount, contourEnds, dx, dy, out FontVariationParseFailure failure))
			{
				_variationParseFailure = failure;
				return;
			}
			for (int i = 0; i < n; i++)
			{
				vertices[off + i].x = FontVariationNormalization.ClampToShort(FontVariationNormalization.OtRound(px[i] + dx[i]));
				vertices[off + i].y = FontVariationNormalization.ClampToShort(FontVariationNormalization.OtRound(py[i] + dy[i]));
			}
		}

		/// <summary>
		/// Net (x, y) offset deltas for each component of a COMPOSITE glyph (gvar "point" i = component i; no IUP).
		/// Returns null when nothing applies (default coords, no gvar, no data, or malformed \u2014 recorded).
		/// </summary>
		private double[] stbtt__ComputeCompositeComponentOffsetDeltas(int glyph_index, in FontVariationNormalizedCoordinates coords,
			int componentCount, int glyfOffset, out double[] deltaY)
		{
			deltaY = null;
			var gvarTable = stbtt__GetVariationTables()?.Gvar;
			if (gvarTable == null || coords.IsDefault) return null;
			int pointCount = componentCount + OpenTypeGvarTable.PhantomPointCount;
			var px = new int[pointCount];
			var py = new int[pointCount];
			stbtt__FillPhantomPointDefaults(glyph_index, glyfOffset, px, py, componentCount);
			var dx = new double[pointCount];
			var dy = new double[pointCount];
			if (!gvarTable.TryComputePointDeltas(glyph_index, coords, px, py, pointCount, null, dx, dy, out FontVariationParseFailure failure))
			{
				_variationParseFailure = failure;
				return null;
			}
			deltaY = dy;
			return dx;
		}

		/// <summary>
		/// Walks a composite glyph's component records (same layout stbtt__GetGlyphShapeTT decodes) to count them:
		/// flags(2) glyphIndex(2) args(2 or 4: ARG_1_AND_2_ARE_WORDS) [scale 2 | xy-scale 4 | 2x2 8], MORE_COMPONENTS bit 5.
		/// Bounds-checked against the font data; stops at the first record that would overrun.
		/// </summary>
		private int stbtt__CountCompositeComponents(FakePtr<byte> firstComponentRecord)
		{
			int count = 0;
			var comp = firstComponentRecord;
			int more = 1;
			while (more != 0)
			{
				if (comp.RemainingLength < 4) break;
				ushort flags = ttUSHORT(comp);
				int recordSize = 4 + ((flags & 1) != 0 ? 4 : 2);
				if ((flags & (1 << 3)) != 0) recordSize += 2;
				else if ((flags & (1 << 6)) != 0) recordSize += 4;
				else if ((flags & (1 << 7)) != 0) recordSize += 8;
				if (comp.RemainingLength < recordSize) break;
				count++;
				comp += recordSize;
				more = flags & (1 << 5);
			}
			return count;
		}

		// \u2500\u2500 metrics at a variation instance (README \u00a75) \u2500\u2500

		/// <summary>
		/// Horizontal metrics at the instance. Advance: HVAR when present (max(0, default + otRound(delta)), fontTools
		/// rule), else phantom-point derived (otRound(right - left)). Left side bearing: the varied outline's xMin minus
		/// the varied left phantom x (this is what fontTools writes into an instanced hmtx and what the rasterizer
		/// implies). Default coords / static font => the plain hmtx values byte-for-byte.
		/// </summary>
		public void stbtt_GetGlyphHMetricsVar(int glyph_index, in FontVariationNormalizedCoordinates coords, ref int advanceWidth, ref int leftSideBearing)
		{
			stbtt_GetGlyphHMetrics(glyph_index, ref advanceWidth, ref leftSideBearing);
			var tables = stbtt__GetVariationTables();
			if (coords.IsDefault || tables == null || this.cff.size != 0)
				return;
			int defaultAdvance = advanceWidth;
			int defaultLsb = leftSideBearing;
			// Phantom deltas (need them for lsb always, and for advance when there is no HVAR).
			double leftPhantomDeltaX = 0.0, rightPhantomDeltaX = 0.0;
			bool havePhantoms = tables.Gvar != null && stbtt__TryComputePhantomPointDeltas(glyph_index, coords, out leftPhantomDeltaX, out rightPhantomDeltaX);
			if (tables.Hvar != null)
			{
				int adv = defaultAdvance + FontVariationNormalization.OtRound(tables.Hvar.ComputeAdvanceWidthDelta(glyph_index, coords));
				advanceWidth = adv < 0 ? 0 : adv;
			}
			else if (havePhantoms)
			{
				int g = stbtt__GetGlyfOffset(glyph_index);
				int xMin = g >= 0 ? ttSHORT(this.data + g + 2) : 0;
				double leftX = xMin - defaultLsb + leftPhantomDeltaX;
				double rightX = xMin - defaultLsb + defaultAdvance + rightPhantomDeltaX;
				int adv = FontVariationNormalization.OtRound(rightX - leftX);
				advanceWidth = adv < 0 ? 0 : adv;
			}
			if (havePhantoms)
			{
				int g = stbtt__GetGlyfOffset(glyph_index);
				int xMin = g >= 0 ? ttSHORT(this.data + g + 2) : 0;
				double leftX = xMin - defaultLsb + leftPhantomDeltaX;
				if (stbtt_GetGlyphBoxVar(glyph_index, coords, out int vx0, out _, out _, out _))
					leftSideBearing = FontVariationNormalization.OtRound(vx0 - leftX);
			}
		}

		/// <summary>
		/// Advance width at the instance derived ONLY from the gvar phantom points (otRound(default + rightΔ - leftΔ)),
		/// ignoring HVAR. fontTools' instancer DROPS HVAR for fully-pinned glyf fonts and writes phantom-derived
		/// advances into the instanced hmtx, so this is the quantity the instanced fixtures pin exactly. Production
		/// layout uses stbtt_GetGlyphHMetricsVar (HVAR wins when present — spec, FreeType, HarfBuzz); the two differ
		/// only for fonts whose HVAR and gvar phantoms disagree (e.g. Inter.var.subset '.notdef'). False when no gvar data.
		/// </summary>
		internal bool stbtt__TryGetPhantomDerivedAdvanceVar(int glyph_index, in FontVariationNormalizedCoordinates coords, out int advanceWidth)
		{
			int defaultAdvance = 0, defaultLsb = 0;
			stbtt_GetGlyphHMetrics(glyph_index, ref defaultAdvance, ref defaultLsb);
			advanceWidth = defaultAdvance;
			if (coords.IsDefault || this.cff.size != 0) return true;
			if (!stbtt__TryComputePhantomPointDeltas(glyph_index, coords, out double leftDeltaX, out double rightDeltaX)) return false;
			int adv = FontVariationNormalization.OtRound(defaultAdvance + rightDeltaX - leftDeltaX);
			advanceWidth = adv < 0 ? 0 : adv;
			return true;
		}

		/// <summary>Left/right phantom-point x deltas of a glyph (simple or composite) at the instance.</summary>
		private bool stbtt__TryComputePhantomPointDeltas(int glyph_index, in FontVariationNormalizedCoordinates coords, out double leftDeltaX, out double rightDeltaX)
		{
			leftDeltaX = 0.0; rightDeltaX = 0.0;
			var gvarTable = stbtt__GetVariationTables()?.Gvar;
			if (gvarTable == null) return false;
			int g = stbtt__GetGlyfOffset(glyph_index);
			// An EMPTY glyph (no glyf data, e.g. space) still has 4 phantom points whose advance may vary.
			int numberOfContours = g < 0 ? 0 : ttSHORT(this.data + g);
			int pointCount;
			ushort[] contourEnds = null;
			int[] px, py;
			if (numberOfContours >= 0)
			{
				int n = numberOfContours == 0 ? 0 : 1 + ttUSHORT(this.data + g + 10 + numberOfContours * 2 - 2);
				pointCount = n + OpenTypeGvarTable.PhantomPointCount;
				px = new int[pointCount]; py = new int[pointCount];
				// IUP needs the default outline points: re-read them via the un-varied shape's point arrays.
				// Cheaper than a full shape build: decode flags/coords directly (same layout as stbtt__GetGlyphShapeTT).
				if (n > 0 && !stbtt__TryReadSimpleGlyphPoints(g, numberOfContours, n, px, py, out contourEnds))
					return false;
			}
			else
			{
				int componentCount = stbtt__CountCompositeComponents(this.data + g + 10);
				pointCount = componentCount + OpenTypeGvarTable.PhantomPointCount;
				px = new int[pointCount]; py = new int[pointCount];
			}
			stbtt__FillPhantomPointDefaults(glyph_index, g, px, py, pointCount - OpenTypeGvarTable.PhantomPointCount);
			var dx = new double[pointCount];
			var dy = new double[pointCount];
			if (!gvarTable.TryComputePointDeltas(glyph_index, coords, px, py, pointCount, contourEnds, dx, dy, out FontVariationParseFailure failure))
			{
				_variationParseFailure = failure;
				return false;
			}
			leftDeltaX = dx[pointCount - 4];
			rightDeltaX = dx[pointCount - 3];
			return true;
		}

		/// <summary>Decodes a simple glyph's flags + x/y arrays (glyf layout) into integer point arrays, plus contour ends.</summary>
		private bool stbtt__TryReadSimpleGlyphPoints(int g, int numberOfContours, int n, int[] px, int[] py, out ushort[] contourEnds)
		{
			var data = this.data;
			var endPtsOfContours = data + g + 10;
			contourEnds = new ushort[numberOfContours];
			for (int c = 0; c < numberOfContours; c++)
				contourEnds[c] = ttUSHORT(endPtsOfContours + c * 2);
			int ins = ttUSHORT(data + g + 10 + numberOfContours * 2);
			var points = data + g + 10 + numberOfContours * 2 + 2 + ins;
			var flagsPerPoint = new byte[n];
			byte flags = 0; int flagcount = 0;
			for (int i = 0; i < n; ++i)
			{
				if (flagcount == 0)
				{
					if (points.RemainingLength < 1) return false;
					flags = points.GetAndIncrease();
					if ((flags & 8) != 0) { if (points.RemainingLength < 1) return false; flagcount = points.GetAndIncrease(); }
				}
				else --flagcount;
				flagsPerPoint[i] = flags;
			}
			int x = 0;
			for (int i = 0; i < n; ++i)
			{
				flags = flagsPerPoint[i];
				if ((flags & 2) != 0) { if (points.RemainingLength < 1) return false; short dx = points.GetAndIncrease(); x += (flags & 16) != 0 ? dx : -dx; }
				else if ((flags & 16) == 0) { if (points.RemainingLength < 2) return false; x += (short)(points[0] * 256 + points[1]); points += 2; }
				px[i] = (short)x;
			}
			int y = 0;
			for (int i = 0; i < n; ++i)
			{
				flags = flagsPerPoint[i];
				if ((flags & 4) != 0) { if (points.RemainingLength < 1) return false; short dy = points.GetAndIncrease(); y += (flags & 32) != 0 ? dy : -dy; }
				else if ((flags & 32) == 0) { if (points.RemainingLength < 2) return false; y += (short)(points[0] * 256 + points[1]); points += 2; }
				py[i] = (short)y;
			}
			return true;
		}

		/// <summary>
		/// Vertical metrics at the instance: hhea ascent/descent/lineGap + MVAR hasc/hdsc/hlgp deltas (otRound each).
		/// fontTools mirrors the OS/2 typo deltas into hhea when they were equal in the source (verticalMetricsKeptInSync);
		/// FreeType (Chrome) applies hasc/hdsc/hlgp to hhea as well. Default coords => plain hhea.
		/// </summary>
		public void stbtt_GetFontVMetricsVar(in FontVariationNormalizedCoordinates coords, out int ascent, out int descent, out int lineGap)
		{
			stbtt_GetFontVMetrics(out ascent, out descent, out lineGap);
			var tables = stbtt__GetVariationTables();
			if (coords.IsDefault || tables == null || tables.Mvar == null)
				return;
			ascent += FontVariationNormalization.OtRound(tables.Mvar.ComputeDelta(OpenTypeMvarTable.TagHorizontalAscender, coords));
			descent += FontVariationNormalization.OtRound(tables.Mvar.ComputeDelta(OpenTypeMvarTable.TagHorizontalDescender, coords));
			lineGap += FontVariationNormalization.OtRound(tables.Mvar.ComputeDelta(OpenTypeMvarTable.TagHorizontalLineGap, coords));
		}

		// \u2500\u2500 atlas entry points: box + raster from the VARIED (+ optional synth-styled) outline \u2500\u2500

		/// <summary>
		/// Control-polygon bounds of the varied outline in font units (the glyf bbox is STALE for a varied glyph;
		/// fontTools recalcBounds uses all coordinates incl. off-curve points, exactly this). Default coords => glyf bbox.
		/// False for an empty glyph.
		/// </summary>
		public bool stbtt_GetGlyphBoxVar(int glyph_index, in FontVariationNormalizedCoordinates coords, out int x0, out int y0, out int x1, out int y1)
		{
			if (coords.IsDefault)
			{
				int bx0 = 0, by0 = 0, bx1 = 0, by1 = 0;
				bool ok = stbtt_GetGlyphBox(glyph_index, ref bx0, ref by0, ref bx1, ref by1) != 0;
				x0 = bx0; y0 = by0; x1 = bx1; y1 = by1;
				return ok;
			}
			int num_verts = stbtt_GetGlyphShapeVar(glyph_index, coords, out stbtt_vertex[] verts);
			if (num_verts <= 0 || !SynthOutline.ComputeOutlineBounds(verts, num_verts, out float fx0, out float fy0, out float fx1, out float fy1))
			{
				x0 = y0 = x1 = y1 = 0;
				return false;
			}
			x0 = (int)Math.Floor(fx0); y0 = (int)Math.Floor(fy0); x1 = (int)Math.Ceiling(fx1); y1 = (int)Math.Ceiling(fy1);
			return true;
		}

		/// <summary>
		/// Bitmap box of a varied (+ synth-styled) glyph \u2014 the Var analogue of stbtt_GetGlyphBitmapBoxSubpixelSynth.
		/// Default coords AND identity synth style => the classic glyf-bbox path (byte-identical masks).
		/// </summary>
		public void stbtt_GetGlyphBitmapBoxSubpixelVarSynth(int glyph, in FontVariationNormalizedCoordinates coords, float scale_x, float scale_y,
			float shift_x, float shift_y, float skewX, float scaleX, float emboldenFontUnits,
			stbtt_YPixelGridFitRemap y_remap, ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			if (coords.IsDefault)
			{
				stbtt_GetGlyphBitmapBoxSubpixelSynth(glyph, scale_x, scale_y, shift_x, shift_y, skewX, scaleX, emboldenFontUnits, y_remap, ref ix0, ref iy0, ref ix1, ref iy1);
				return;
			}
			int num_verts = stbtt_GetGlyphShapeVarSynth(glyph, coords, skewX, scaleX, emboldenFontUnits, out stbtt_vertex[] verts);
			stbtt__OutlineBitmapBox(verts, num_verts, scale_x, scale_y, shift_x, shift_y, y_remap, ref ix0, ref iy0, ref ix1, ref iy1);
		}

		/// <summary>Rasterizes a varied (+ synth-styled) glyph \u2014 the Var analogue of stbtt_MakeGlyphBitmapSubpixelSynth. Box recomputed identically.</summary>
		public void stbtt_MakeGlyphBitmapSubpixelVarSynth(FakePtr<byte> output, int out_w, int out_h, int out_stride,
			float scale_x, float scale_y, float shift_x, float shift_y, in FontVariationNormalizedCoordinates coords,
			float skewX, float scaleX, float emboldenFontUnits, int glyph, stbtt_YPixelGridFitRemap y_remap = null)
		{
			if (coords.IsDefault)
			{
				stbtt_MakeGlyphBitmapSubpixelSynth(output, out_w, out_h, out_stride, scale_x, scale_y, shift_x, shift_y, skewX, scaleX, emboldenFontUnits, glyph, y_remap);
				return;
			}
			int num_verts = stbtt_GetGlyphShapeVarSynth(glyph, coords, skewX, scaleX, emboldenFontUnits, out stbtt_vertex[] verts);
			if (num_verts <= 0)
				return;
			var ix0 = 0; var iy0 = 0; var ix1 = 0; var iy1 = 0;
			stbtt__OutlineBitmapBox(verts, num_verts, scale_x, scale_y, shift_x, shift_y, y_remap, ref ix0, ref iy0, ref ix1, ref iy1);
			var gbm = new Bitmap();
			gbm.pixels = output;
			gbm.w = out_w;
			gbm.h = out_h;
			gbm.stride = out_stride;
			if (gbm.w != 0 && gbm.h != 0)
				gbm.stbtt_Rasterize(0.35f, verts, num_verts, scale_x, scale_y, shift_x, shift_y, ix0, iy0, 1, y_remap);
		}

		/// <summary>Shared box math from an ALREADY-PRODUCED outline (same formulas as the Synth box functions).</summary>
		private static void stbtt__OutlineBitmapBox(stbtt_vertex[] verts, int num_verts, float scale_x, float scale_y, float shift_x, float shift_y,
			stbtt_YPixelGridFitRemap y_remap, ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			if (num_verts <= 0 || !SynthOutline.ComputeOutlineBounds(verts, num_verts, out float bx0, out float by0, out float bx1, out float by1))
			{
				ix0 = 0; iy0 = 0; ix1 = 0; iy1 = 0;
				return;
			}
			ix0 = (int)Math.Floor(bx0 * scale_x + shift_x);
			ix1 = (int)Math.Ceiling(bx1 * scale_x + shift_x);
			if (y_remap == null)
			{
				iy0 = (int)Math.Floor(-by1 * scale_y + shift_y);
				iy1 = (int)Math.Ceiling(-by0 * scale_y + shift_y);
			}
			else
			{
				iy0 = (int)Math.Floor(-y_remap(by1 * scale_y) + shift_y);
				iy1 = (int)Math.Ceiling(-y_remap(by0 * scale_y) + shift_y);
			}
		}
	}

	namespace Variations
	{
		/// <summary>The parsed variation tables of one face, in one place (fvar is mandatory; the rest optional).</summary>
		public sealed class OpenTypeVariationTables
		{
			public OpenTypeFvarTable Fvar { get; }
			/// <summary>Null when the font has no avar (identity normalization).</summary>
			public OpenTypeAvarTable Avar { get; }
			/// <summary>Null when the font has no gvar (CFF2 fonts, or a static-outline VF) \u2014 outlines then never vary.</summary>
			public OpenTypeGvarTable Gvar { get; }
			/// <summary>Null when absent; when present it WINS over phantom-point advances.</summary>
			public OpenTypeHvarTable Hvar { get; }
			/// <summary>Null when absent (font-wide metrics constant across the space).</summary>
			public OpenTypeMvarTable Mvar { get; }
			/// <summary>
			/// GDEF v1.3 ItemVariationStore (README §7) — the delta source for GPOS VariationIndex tables (varied
			/// kerning, Part C). Null when GDEF is absent, older than 1.3, has a NULL store, or the store is malformed
			/// (recorded in stbtt_VariationParseFailure; kerning then stays at the default instance — fail-soft).
			/// </summary>
			public OpenTypeItemVariationStore GdefItemVariationStore { get; }

			internal OpenTypeVariationTables(OpenTypeFvarTable fvar, OpenTypeAvarTable avar, OpenTypeGvarTable gvar, OpenTypeHvarTable hvar, OpenTypeMvarTable mvar,
				OpenTypeItemVariationStore gdefItemVariationStore)
			{
				Fvar = fvar;
				Avar = avar;
				Gvar = gvar;
				Hvar = hvar;
				Mvar = mvar;
				GdefItemVariationStore = gdefItemVariationStore;
			}
		}

		/// <summary>The canonical normalization math (README §1) — shared by the request path and tests.</summary>
		public static class FontVariationNormalization
		{
			/// <summary>fontTools otRound: floor(x + 0.5) (round half UP, not banker's).</summary>
			public static int OtRound(double value) => (int)Math.Floor(value + 0.5);

			/// <summary>Default normalization only (no avar), in double: clamp, then piecewise over min/default/max.</summary>
			public static double NormalizeUserValueDefault(double userValue, in FontVariationAxisRecord axis)
			{
				double v = userValue;
				if (v < axis.MinValue) v = axis.MinValue;   // clamp FIRST — makes the divisions below safe
				if (v > axis.MaxValue) v = axis.MaxValue;
				if (v == axis.DefaultValue || axis.MinValue == axis.MaxValue)
					return 0.0;
				if (v < axis.DefaultValue)
					return (v - axis.DefaultValue) / (axis.DefaultValue - axis.MinValue);   // -> [-1, 0]
				return (v - axis.DefaultValue) / (axis.MaxValue - axis.DefaultValue);      // -> [0, 1]
			}

			/// <summary>Full pipeline: default-normalize -> avar remap (double) -> F2Dot14 once (otRound, clamped).</summary>
			public static short NormalizeUserValueToF2Dot14(double userValue, in FontVariationAxisRecord axis, OpenTypeAvarTable avar, int axisIndex)
			{
				double normalized = NormalizeUserValueDefault(userValue, axis);
				if (avar != null)
					normalized = avar.Remap(axisIndex, normalized);
				return ToF2Dot14(normalized);
			}

			/// <summary>double in [-1,1] -> F2Dot14 via otRound, clamped to [-16384, 16384].</summary>
			public static short ToF2Dot14(double normalized)
			{
				int q = OtRound(normalized * 16384.0);
				if (q < -16384) q = -16384;
				if (q > 16384) q = 16384;
				return (short)q;
			}

			/// <summary>Saturating int -> short (varied coordinates may leave the FWORD range; saturate, never wrap).</summary>
			public static short ClampToShort(int value)
			{
				if (value < short.MinValue) return short.MinValue;
				if (value > short.MaxValue) return short.MaxValue;
				return (short)value;
			}
		}
	}
}