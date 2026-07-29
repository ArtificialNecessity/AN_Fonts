using System;
using System.Collections.Generic;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp
{
	/// <summary>
	/// Synthetic style outline preprocessing (SilkyNvg
	/// plans/crisp_text_synthetic_styles.md §A.2/§A.3): fake-bold stroke-union
	/// emboldening, fake-italic shear and Tz horizontal scaling, applied ONCE to
	/// the stbtt_vertex[] outline in FONT UNITS, upstream of rasterization.
	/// Both SilkyNvg raster tiers consume this same output, so they are
	/// pixel-consistent by construction (the Design invariant).
	/// </summary>
	public partial class FontInfo
	{
		/// <summary>
		/// stbtt_GetGlyphShape followed by the synthetic-style transform:
		///   1. embolden: append stroke-union rings at half-width
		///      <paramref name="emboldenFontUnits"/> (winding-normalized — see
		///      SynthesizeOutline), NON-ZERO winding does the union at raster time;
		///   2. shear/scale: x' = (x + y·skewX) · scaleX (font units, y-up).
		/// Identity style returns the raw shape untouched.
		/// </summary>
		public int stbtt_GetGlyphShapeSynth(int glyph_index, float skewX, float scaleX,
			float emboldenFontUnits, out stbtt_vertex[] pvertices)
		{
			int num_verts = stbtt_GetGlyphShape(glyph_index, out pvertices);
			if (num_verts <= 0)
				return num_verts;
			return SynthOutline.SynthesizeOutline(ref pvertices, num_verts, skewX, scaleX, emboldenFontUnits);
		}

		/// <summary>
		/// Bitmap box for a synth-styled glyph — the synth analogue of
		/// stbtt_GetGlyphBitmapBoxSubpixelYRemap (pass y_remap null for the plain
		/// variant). The box is computed from the TRANSFORMED outline's control
		/// polygon (conservative for quadratics, exact for lines) — mask and box
		/// must agree, so RenderGlyphBitmap-side callers pass identical arguments.
		/// </summary>
		public void stbtt_GetGlyphBitmapBoxSubpixelSynth(int glyph, float scale_x, float scale_y,
			float shift_x, float shift_y, float skewX, float scaleX, float emboldenFontUnits,
			stbtt_YPixelGridFitRemap y_remap, ref int ix0, ref int iy0, ref int ix1, ref int iy1)
		{
			int num_verts = stbtt_GetGlyphShapeSynth(glyph, skewX, scaleX, emboldenFontUnits, out stbtt_vertex[] verts);
			if (num_verts <= 0 || !SynthOutline.ComputeOutlineBounds(verts, num_verts, out float bx0, out float by0, out float bx1, out float by1))
			{
				ix0 = 0; iy0 = 0; ix1 = 0; iy1 = 0;
				return;
			}
			if (y_remap == null)
			{
				ix0 = (int)Math.Floor(bx0 * scale_x + shift_x);
				iy0 = (int)Math.Floor(-by1 * scale_y + shift_y);
				ix1 = (int)Math.Ceiling(bx1 * scale_x + shift_x);
				iy1 = (int)Math.Ceiling(-by0 * scale_y + shift_y);
			}
			else
			{
				// Same y-UP-pixel-space remap contract as stbtt_GetGlyphBitmapBoxSubpixelYRemap.
				ix0 = (int)Math.Floor(bx0 * scale_x + shift_x);
				iy0 = (int)Math.Floor(-y_remap(by1 * scale_y) + shift_y);
				ix1 = (int)Math.Ceiling(bx1 * scale_x + shift_x);
				iy1 = (int)Math.Ceiling(-y_remap(by0 * scale_y) + shift_y);
			}
		}

		/// <summary>
		/// Rasterizes a synth-styled glyph — the synth analogue of
		/// stbtt_MakeGlyphBitmapSubpixelYRemap (y_remap null = plain). The box is
		/// recomputed here with EXACTLY the box function above so mask placement
		/// and the caller's box agree.
		/// </summary>
		public void stbtt_MakeGlyphBitmapSubpixelSynth(FakePtr<byte> output, int out_w,
			int out_h, int out_stride, float scale_x, float scale_y, float shift_x, float shift_y,
			float skewX, float scaleX, float emboldenFontUnits, int glyph,
			stbtt_YPixelGridFitRemap y_remap = null)
		{
			int num_verts = stbtt_GetGlyphShapeSynth(glyph, skewX, scaleX, emboldenFontUnits, out stbtt_vertex[] verts);
			if (num_verts <= 0)
				return;
			var ix0 = 0; var iy0 = 0; var ix1 = 0; var iy1 = 0;
			stbtt_GetGlyphBitmapBoxSubpixelSynth(glyph, scale_x, scale_y, shift_x, shift_y,
				skewX, scaleX, emboldenFontUnits, y_remap, ref ix0, ref iy0, ref ix1, ref iy1);
			var gbm = new Bitmap();
			gbm.pixels = output;
			gbm.w = out_w;
			gbm.h = out_h;
			gbm.stride = out_stride;
			if (gbm.w != 0 && gbm.h != 0)
				gbm.stbtt_Rasterize(0.35f, verts, num_verts, scale_x, scale_y, shift_x, shift_y, ix0, iy0, 1, y_remap);
		}
	}

	/// <summary>
	/// The synthetic-style outline transform (font units, y-up). Separated from
	/// FontInfo so SilkyNvg's sweeper preprocessor can reuse it on raw vertex
	/// lists (shared code = pixel-consistent tiers).
	/// </summary>
	public static class SynthOutline
	{
		/// <summary>
		/// Flattening tolerance for embolden ring construction, in font units.
		/// 1/256 em at 2048 upem = 8 units; rings are re-consumed by the standard
		/// rasterizer flattening anyway, so this only bounds ring-shape error.
		/// </summary>
		private const float RingFlattenToleranceFontUnits = 2f;

		/// <summary>Miter limit for ring joins, as a multiple of the embolden
		/// half-width. Beyond this the join falls back to a bevel (Skia default 4).</summary>
		private const float MiterLimit = 4f;

		/// <summary>
		/// LEGACY A/B SWITCH (default false = single-loop mode). The original A2
		/// construction emitted a ±d loop PAIR per contour; the −d loop of a flesh
		/// contour is region-theoretically redundant (it only cancels one layer over
		/// the eroded interior) and is exactly the loop that SELF-CROSSES wherever
		/// the local stroke width t &lt; 2d (script tails, thin connectors) — its
		/// inverted lobes broke the cancellation and left 0-winding (uncovered)
		/// patches INSIDE thin features (David's diagnosis, 2026-07-28: the
		/// lightened Miama 'e' tail, identical on both tiers). Single-loop mode
		/// offsets EVERY contour by −fillSign·d (away from ink), preserving source
		/// orientation: flesh grows, holes shrink, one uniform formula; inward
		/// loops never exist, and outward self-crossings at concave corners are
		/// double-POSITIVE lobes — winding-safe under both nonzero and |signed|.
		/// </summary>
		public static bool EmboldenLoopPairLegacyMode = false;

		/// <summary>
		/// Applies the full synth transform IN PLACE (array may be reallocated):
		///   1. embolden > 0: flatten each contour, build ±d offset rings with
		///      miter/bevel joins, append as line contours. WINDING NORMALIZATION
		///      (spec §A.2 REQUIRED): the glyph's dominant fill sign w =
		///      sign(Σ signed areas); each annulus is emitted with its LARGER ring
		///      wound w and its SMALLER ring wound −w, so the stroke band adds ink
		///      of the flesh sign everywhere and the annulus interior nets zero —
		///      inner rims never erase (the erase bug), holes shrink correctly,
		///      collapsed counters saturate solid (never cancel).
		///   2. shear/scale: x' = (x + y·skewX)·scaleX on ALL coordinates
		///      including control points (linear map — quadratics stay quadratics).
		/// Returns the new vertex count.
		/// </summary>
		public static int SynthesizeOutline(ref stbtt_vertex[] vertices, int numVerts,
			float skewX, float scaleX, float emboldenFontUnits)
		{
			if (numVerts <= 0 || vertices == null)
				return numVerts;

			if (emboldenFontUnits > 0f)
				numVerts = AppendEmboldenRings(ref vertices, numVerts, emboldenFontUnits);

			if (skewX != 0f || scaleX != 1f)
			{
				for (int i = 0; i < numVerts; i++)
				{
					ref stbtt_vertex v = ref vertices[i];
					v.x = ShearScaleX(v.x, v.y, skewX, scaleX);
					if (v.type == STBTT_vcurve || v.type == STBTT_vcubic)
						v.cx = ShearScaleX(v.cx, v.cy, skewX, scaleX);
					if (v.type == STBTT_vcubic)
						v.cx1 = ShearScaleX(v.cx1, v.cy1, skewX, scaleX);
				}
			}

			return numVerts;
		}

		private static short ShearScaleX(short x, short y, float skewX, float scaleX)
		{
			float transformed = (x + y * skewX) * scaleX;
			return (short)ClampToShort(transformed);
		}

		// netstandard2.0 compatibility helpers (no MathF / Math.Clamp there).
		private static float Sqrtf(float v) => (float)Math.Sqrt(v);

		private static int ClampToShort(float v)
		{
			var rounded = (int)Math.Round(v);
			if (rounded < short.MinValue) return short.MinValue;
			if (rounded > short.MaxValue) return short.MaxValue;
			return rounded;
		}

		/// <summary>Min/max over all coordinates incl. control points (control
		/// polygon bounds — conservative for curves, exact for lines).</summary>
		public static bool ComputeOutlineBounds(stbtt_vertex[] vertices, int numVerts,
			out float x0, out float y0, out float x1, out float y1)
		{
			x0 = float.MaxValue; y0 = float.MaxValue;
			x1 = float.MinValue; y1 = float.MinValue;
			bool any = false;
			for (int i = 0; i < numVerts; i++)
			{
				stbtt_vertex v = vertices[i];
				Accumulate(v.x, v.y, ref x0, ref y0, ref x1, ref y1, ref any);
				if (v.type == STBTT_vcurve || v.type == STBTT_vcubic)
					Accumulate(v.cx, v.cy, ref x0, ref y0, ref x1, ref y1, ref any);
				if (v.type == STBTT_vcubic)
					Accumulate(v.cx1, v.cy1, ref x0, ref y0, ref x1, ref y1, ref any);
			}
			return any;
		}

		private static void Accumulate(float px, float py, ref float x0, ref float y0,
			ref float x1, ref float y1, ref bool any)
		{
			if (px < x0) x0 = px;
			if (py < y0) y0 = py;
			if (px > x1) x1 = px;
			if (py > y1) y1 = py;
			any = true;
		}

		// ── embolden ring construction ──

		private static int AppendEmboldenRings(ref stbtt_vertex[] vertices, int numVerts, float halfWidth)
		{
			List<List<(float x, float y)>> contours = FlattenToContours(vertices, numVerts);
			if (contours.Count == 0)
				return numVerts;

			// Dominant fill sign w over ALL contours (non-zero winding glyphs:
			// outer contours dominate holes by area). y-up: CCW area > 0.
			float totalArea = 0f;
			foreach (List<(float x, float y)> contour in contours)
				totalArea += SignedArea(contour);
			float fillSign = totalArea >= 0f ? 1f : -1f;

			var ringVerts = new List<stbtt_vertex>();
			foreach (List<(float x, float y)> contour in contours)
			{
				if (contour.Count < 3)
					continue;
				if (!EmboldenLoopPairLegacyMode)
				{
					// SINGLE-LOOP MODE (default; see EmboldenLoopPairLegacyMode doc):
					// offset AWAY FROM INK by d, preserving source orientation.
					// Uniform for flesh AND holes: away-from-ink = −fillSign·d
					// (left normal is the interior side for CCW traversal; the
					// algebra collapses both cases to one formula).
					List<(float x, float y)> awayFromInk = OffsetContour(contour, -fillSign * halfWidth);
					if (awayFromInk.Count < 3)
						continue;
					// Preserve traversal orientation EXACTLY (OffsetContour is
					// order-preserving): a collapse-inverted hole loop keeps its
					// inverted sense, which is what makes collapsed counters
					// SATURATE solid rather than re-punch a tiny hole.
					float emittedSign = SignedArea(awayFromInk) >= 0f ? 1f : -1f;
					EmitRing(ringVerts, awayFromInk, emittedSign);
				}
				else
				{
					List<(float x, float y)> offsetOut = OffsetContour(contour, +halfWidth);
					List<(float x, float y)> offsetIn = OffsetContour(contour, -halfWidth);
					if (offsetOut.Count < 3 || offsetIn.Count < 3)
						continue;
					// Larger-|area| ring carries the flesh sign, smaller carries the
					// opposite — the annulus adds ±w band ink, nets 0 inside.
					float areaOut = Math.Abs(SignedArea(offsetOut));
					float areaIn = Math.Abs(SignedArea(offsetIn));
					List<(float x, float y)> larger = areaOut >= areaIn ? offsetOut : offsetIn;
					List<(float x, float y)> smaller = areaOut >= areaIn ? offsetIn : offsetOut;
					EmitRing(ringVerts, larger, fillSign);
					EmitRing(ringVerts, smaller, -fillSign);
				}
			}

			if (ringVerts.Count == 0)
				return numVerts;

			var merged = new stbtt_vertex[numVerts + ringVerts.Count];
			Array.Copy(vertices, merged, numVerts);
			for (int i = 0; i < ringVerts.Count; i++)
				merged[numVerts + i] = ringVerts[i];
			vertices = merged;
			return merged.Length;
		}

		/// <summary>Emits a polyline ring as vmove + vline vertices with the
		/// REQUESTED winding sign (reverses the point order when needed).</summary>
		private static void EmitRing(List<stbtt_vertex> output, List<(float x, float y)> ring, float requiredSign)
		{
			float currentSign = SignedArea(ring) >= 0f ? 1f : -1f;
			int count = ring.Count;
			bool reverse = currentSign != requiredSign;

			var v = new stbtt_vertex();
			for (int i = 0; i < count; i++)
			{
				(float x, float y) p = ring[reverse ? count - 1 - i : i];
				v.type = (byte)(i == 0 ? STBTT_vmove : STBTT_vline);
				v.x = (short)ClampToShort(p.x);
				v.y = (short)ClampToShort(p.y);
				v.cx = 0; v.cy = 0; v.cx1 = 0; v.cy1 = 0;
				output.Add(v);
			}
			// Close the ring explicitly (contour-end vline back to the start — the
			// rasterizer closes on the next vmove, but an explicit edge is exact).
			(float x, float y) first = ring[reverse ? count - 1 : 0];
			v.type = (byte)STBTT_vline;
			v.x = (short)ClampToShort(first.x);
			v.y = (short)ClampToShort(first.y);
			output.Add(v);
		}

		private static float SignedArea(List<(float x, float y)> ring)
		{
			float area2 = 0f;
			for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
				area2 += (ring[j].x * ring[i].y) - (ring[i].x * ring[j].y);
			return area2 * 0.5f;
		}

		/// <summary>Flattens the outline into closed polyline contours (font
		/// units) at ring tolerance. Quadratic + cubic subdivision by flatness of
		/// the control polygon (same criterion family as stbtt__tesselate_curve).</summary>
		private static List<List<(float x, float y)>> FlattenToContours(stbtt_vertex[] vertices, int numVerts)
		{
			var contours = new List<List<(float x, float y)>>();
			List<(float x, float y)> current = null;
			float px = 0f, py = 0f;

			for (int i = 0; i < numVerts; i++)
			{
				stbtt_vertex v = vertices[i];
				switch (v.type)
				{
					case STBTT_vmove:
						current = new List<(float x, float y)> { (v.x, v.y) };
						contours.Add(current);
						px = v.x; py = v.y;
						break;
					case STBTT_vline:
						current?.Add((v.x, v.y));
						px = v.x; py = v.y;
						break;
					case STBTT_vcurve:
						if (current != null)
							FlattenQuad(current, px, py, v.cx, v.cy, v.x, v.y, 0);
						px = v.x; py = v.y;
						break;
					case STBTT_vcubic:
						if (current != null)
							FlattenCubic(current, px, py, v.cx, v.cy, v.cx1, v.cy1, v.x, v.y, 0);
						px = v.x; py = v.y;
						break;
				}
			}

			// Drop closing duplicates + degenerate contours.
			for (int c = contours.Count - 1; c >= 0; c--)
			{
				List<(float x, float y)> contour = contours[c];
				while (contour.Count > 1 && NearlyEqual(contour[0], contour[contour.Count - 1]))
					contour.RemoveAt(contour.Count - 1);
				if (contour.Count < 3)
					contours.RemoveAt(c);
			}
			return contours;
		}

		private static bool NearlyEqual((float x, float y) a, (float x, float y) b)
			=> Math.Abs(a.x - b.x) < 0.5f && Math.Abs(a.y - b.y) < 0.5f;

		private static void FlattenQuad(List<(float x, float y)> output, float x0, float y0,
			float cx, float cy, float x1, float y1, int depth)
		{
			float mx = (x0 + 2f * cx + x1) * 0.25f;
			float my = (y0 + 2f * cy + y1) * 0.25f;
			float dx = (x0 + x1) * 0.5f - mx;
			float dy = (y0 + y1) * 0.5f - my;
			if (depth < 16 && dx * dx + dy * dy > RingFlattenToleranceFontUnits * RingFlattenToleranceFontUnits)
			{
				FlattenQuad(output, x0, y0, (x0 + cx) * 0.5f, (y0 + cy) * 0.5f, mx, my, depth + 1);
				FlattenQuad(output, mx, my, (x1 + cx) * 0.5f, (y1 + cy) * 0.5f, x1, y1, depth + 1);
			}
			else
			{
				output.Add((x1, y1));
			}
		}

		private static void FlattenCubic(List<(float x, float y)> output, float x0, float y0,
			float cx0, float cy0, float cx1, float cy1, float x1, float y1, int depth)
		{
			// Control-polygon flatness (same criterion as stbtt__tesselate_cubic).
			float dx0 = cx0 - x0, dy0 = cy0 - y0;
			float dx1 = cx1 - cx0, dy1 = cy1 - cy0;
			float dx2 = x1 - cx1, dy2 = y1 - cy1;
			float dx = x1 - x0, dy = y1 - y0;
			float longlen = Sqrtf(dx0 * dx0 + dy0 * dy0) + Sqrtf(dx1 * dx1 + dy1 * dy1) + Sqrtf(dx2 * dx2 + dy2 * dy2);
			float shortlen = Sqrtf(dx * dx + dy * dy);
			float flatness_squared = longlen * longlen - shortlen * shortlen;
			if (depth < 16 && flatness_squared > RingFlattenToleranceFontUnits * RingFlattenToleranceFontUnits)
			{
				float mx0 = (x0 + cx0) * 0.5f, my0 = (y0 + cy0) * 0.5f;
				float mc = (cx0 + cx1) * 0.5f, myc = (cy0 + cy1) * 0.5f;
				float mx1 = (cx1 + x1) * 0.5f, my1 = (cy1 + y1) * 0.5f;
				float ax = (mx0 + mc) * 0.5f, ay = (my0 + myc) * 0.5f;
				float bx = (mc + mx1) * 0.5f, by = (myc + my1) * 0.5f;
				float qx = (ax + bx) * 0.5f, qy = (ay + by) * 0.5f;
				FlattenCubic(output, x0, y0, mx0, my0, ax, ay, qx, qy, depth + 1);
				FlattenCubic(output, qx, qy, bx, by, mx1, my1, x1, y1, depth + 1);
			}
			else
			{
				output.Add((x1, y1));
			}
		}

		/// <summary>
		/// Offsets a closed polyline by <paramref name="distance"/> along each
		/// edge's LEFT normal relative to traversal (sign of distance flips side).
		/// Miter joins clamped at MiterLimit×|distance| → bevel fallback.
		/// Self-intersections are permitted — non-zero winding absorbs them at
		/// raster time (the whole point of the stroke-union technique).
		/// </summary>
		private static List<(float x, float y)> OffsetContour(List<(float x, float y)> contour, float distance)
		{
			int n = contour.Count;
			var result = new List<(float x, float y)>(n + 8);
			float d = Math.Abs(distance);
			float side = Math.Sign(distance);

			for (int i = 0; i < n; i++)
			{
				(float x, float y) prev = contour[(i - 1 + n) % n];
				(float x, float y) cur = contour[i];
				(float x, float y) next = contour[(i + 1) % n];

				float e0x = cur.x - prev.x, e0y = cur.y - prev.y;
				float e1x = next.x - cur.x, e1y = next.y - cur.y;
				float l0 = Sqrtf(e0x * e0x + e0y * e0y);
				float l1 = Sqrtf(e1x * e1x + e1y * e1y);
				if (l0 < 1e-6f && l1 < 1e-6f)
					continue;
				if (l0 < 1e-6f) { e0x = e1x; e0y = e1y; l0 = l1; }
				if (l1 < 1e-6f) { e1x = e0x; e1y = e0y; l1 = l0; }

				// Left normals (y-up): edge (ex,ey) → n = (-ey, ex)/len.
				float n0x = -e0y / l0 * side, n0y = e0x / l0 * side;
				float n1x = -e1y / l1 * side, n1y = e1x / l1 * side;

				// Miter direction = normalized normal sum.
				float mxd = n0x + n1x, myd = n0y + n1y;
				float mlen = Sqrtf(mxd * mxd + myd * myd);
				if (mlen < 1e-6f)
				{
					// 180° reversal: bevel with both edge normals.
					result.Add((cur.x + n0x * d, cur.y + n0y * d));
					result.Add((cur.x + n1x * d, cur.y + n1y * d));
					continue;
				}
				mxd /= mlen; myd /= mlen;
				// Miter length: d / cos(θ/2) where cos(θ/2) = m·n0.
				float cosHalf = mxd * n0x + myd * n0y;
				if (cosHalf < 1e-3f || 1f / cosHalf > MiterLimit)
				{
					// Bevel: two offset points, one per edge normal.
					result.Add((cur.x + n0x * d, cur.y + n0y * d));
					result.Add((cur.x + n1x * d, cur.y + n1y * d));
				}
				else
				{
					float miterLen = d / cosHalf;
					result.Add((cur.x + mxd * miterLen, cur.y + myd * miterLen));
				}
			}
			return result;
		}
	}
}