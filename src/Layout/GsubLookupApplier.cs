using System;
using StbTrueTypeSharp.Variations;
using static StbTrueTypeSharp.Common;

namespace StbTrueTypeSharp.Layout
{
	/// <summary>
	/// GSUB buffer applier (SilkyNvg plans/color_glyph_fonts.md §5.2; ground truth
	/// _EXTERNAL_APIS/opentype_gsub_lookup_types.md). Applies resolved lookups to a <see cref="GsubGlyphBuffer"/> in
	/// ascending LookupList order, one full pass per lookup, mutating in place — later lookups see earlier output.
	/// At each position the FIRST subtable whose Coverage + context matches applies, then the pass advances past the
	/// consumed input (HarfBuzz semantics). Supported: types 1, 2 (incl. EMPTY sequence = delete), 3, 4,
	/// 5 (fmt 1/2/3), 6 (fmt 1/2/3), 7 (any inner type). Type 8 is counted at resolve time, never applied.
	/// Malformed subtables leave the buffer unchanged — never throws on the text hot path.
	/// One instance per FontInfo, built lazily; NOT thread-safe (scratch arrays are reused across calls).
	/// </summary>
	public sealed class GsubLookupApplier
	{
		/// <summary>Nested SequenceLookupRecord recursion cap (HarfBuzz HB_MAX_NESTING_LEVEL is 64; contexts that
		/// legitimately nest deeper than this do not exist in text fonts).</summary>
		private const int MaxNestingDepth = 8;
		/// <summary>Longest matchable input/backtrack/lookahead sequence (wild maximum seen: 9-component tag flags).</summary>
		private const int MaxSequenceLength = 64;

		private readonly OpenTypeGsubTable _gsub;
		private readonly OpenTypeGdefTable _gdef;   // may be null (no GDEF): flags degrade to no-skip

		private readonly int[] _matchPositions = new int[MaxSequenceLength];        // top-level context match
		private readonly int[] _nestedMatchPositions = new int[MaxSequenceLength];  // ligature-inside-context etc.
		private readonly ushort[] _sequenceScratch = new ushort[MaxSequenceLength]; // multiple-subst output

		public GsubLookupApplier(OpenTypeGsubTable gsub, OpenTypeGdefTable gdef)
		{
			_gsub = gsub;
			_gdef = gdef;
		}

		/// <summary>Feature-tag driven lookup resolution (default LangSys + required feature + FeatureVariations).</summary>
		public GsubResolvedLookupSet ResolveLookups(ReadOnlySpan<OpenTypeFeatureTag> features, in FontVariationNormalizedCoordinates coords)
			=> _gsub.ResolveLookupsForFeatures(features, coords);

		/// <summary>Applies every resolved lookup as one pass over the buffer, ascending LookupList order, in place.</summary>
		public void Apply(in GsubResolvedLookupSet lookups, GsubGlyphBuffer buffer)
		{
			var indices = lookups.LookupIndicesAscending;
			for (int k = 0; k < indices.Length; k++)
				ApplyLookupPass(indices[k], buffer);
		}

		private void ApplyLookupPass(int lookupIndex, GsubGlyphBuffer buffer)
		{
			var skipper = GetSkipper(lookupIndex);
			int pos = 0;
			while (pos < buffer.Length)
			{
				if (skipper.ShouldSkip(buffer.GlyphIds[pos])) { pos++; continue; }
				if (TryApplyLookupAtPosition(lookupIndex, buffer, pos, 0, out int advance) && advance > 0)
					pos += advance;
				else
					pos++;
			}
		}

		/// <summary>LookupFlag 0 / flags needing no GDEF filtering -> allocation-free singleton (Noto emoji path).</summary>
		private IGsubGlyphSkipper GetSkipper(int lookupIndex)
		{
			var lookup = _gsub.GetLookupTable(lookupIndex, out int avail);
			if (avail < 6) return GsubNoSkipGlyphSkipper.Instance;
			int lookupFlag = ttUSHORT(lookup + 2);
			if (_gdef == null || !GsubGdefGlyphSkipper.FlagNeedsSkipping(lookupFlag)) return GsubNoSkipGlyphSkipper.Instance;
			int markFilteringSet = 0;
			if ((lookupFlag & GsubGdefGlyphSkipper.FlagUseMarkFilteringSet) != 0)
			{
				int subTableCount = ttUSHORT(lookup + 4);
				if (6 + subTableCount * 2 + 2 <= avail) markFilteringSet = ttUSHORT(lookup + 6 + 2 * subTableCount);
			}
			return new GsubGdefGlyphSkipper(_gdef, lookupFlag, markFilteringSet);
		}

		/// <summary>
		/// Tries the lookup's subtables AT one buffer position (used by the top-level pass and by nested
		/// SequenceLookupRecords). Returns true when a subtable applied; <paramref name="advance"/> is how many buffer
		/// positions the pass moves past (ligature/single/alternate: 1; multiple: output count; context: past the input span).
		/// </summary>
		private bool TryApplyLookupAtPosition(int lookupIndex, GsubGlyphBuffer buffer, int pos, int depth, out int advance)
		{
			advance = 0;
			if (depth > MaxNestingDepth || pos < 0 || pos >= buffer.Length) return false;
			var lookup = _gsub.GetLookupTable(lookupIndex, out int avail);
			if (avail < 6) return false;
			int lookupType = ttUSHORT(lookup);
			int subTableCount = ttUSHORT(lookup + 4);
			if (6 + subTableCount * 2 > avail) return false;
			var skipper = GetSkipper(lookupIndex);

			for (int st = 0; st < subTableCount; st++)
			{
				var subtable = lookup + ttUSHORT(lookup + 6 + 2 * st);
				int effectiveType = lookupType;
				if (lookupType == OpenTypeGsubTable.LookupTypeExtensionSubstitution)
				{
					// Extension fmt1: extensionLookupType (u16), extensionOffset (u32 from extension subtable start).
					if (subtable.RemainingLength < 8 || ttUSHORT(subtable) != 1) continue;
					effectiveType = ttUSHORT(subtable + 2);
					if (effectiveType == OpenTypeGsubTable.LookupTypeExtensionSubstitution) continue; // spec: no nested extensions
					subtable = subtable + (int)ttULONG(subtable + 4);
				}
				if (subtable.RemainingLength < 4) continue;

				bool applied;
				switch (effectiveType)
				{
					case 1: applied = TryApplySingle(subtable, buffer, pos, out advance); break;
					case 2: applied = TryApplyMultiple(subtable, buffer, pos, out advance); break;
					case 3: applied = TryApplyAlternate(subtable, buffer, pos, out advance); break;
					case 4: applied = TryApplyLigature(subtable, buffer, pos, skipper, depth, out advance); break;
					case 5: applied = TryApplySequenceContext(subtable, buffer, pos, skipper, depth, out advance); break;
					case 6: applied = TryApplyChainedContext(subtable, buffer, pos, skipper, depth, out advance); break;
					default: applied = false; break; // type 8 / unknown: counted at resolve time, never applied
				}
				if (applied) return true; // first applying subtable ends this lookup at this position
			}
			return false;
		}

		// ---- Type 1: Single ----

		private static bool TryApplySingle(FakePtr<byte> subtable, GsubGlyphBuffer buffer, int pos, out int advance)
		{
			advance = 0;
			int glyph = buffer.GlyphIds[pos];
			if (subtable.RemainingLength < 6) return false;
			int format = ttUSHORT(subtable);
			int coverageIndex = GetCoverageIndexChecked(subtable, ttUSHORT(subtable + 2), glyph);
			if (coverageIndex < 0) return false;
			switch (format)
			{
				case 1:
					buffer.GlyphIds[pos] = (ushort)((glyph + ttSHORT(subtable + 4)) & 0xFFFF); // spec: addition modulo 65536
					advance = 1;
					return true;
				case 2:
				{
					int glyphCount = ttUSHORT(subtable + 4);
					if (coverageIndex >= glyphCount || subtable.RemainingLength < 6 + glyphCount * 2) return false;
					buffer.GlyphIds[pos] = (ushort)ttUSHORT(subtable + 6 + 2 * coverageIndex);
					advance = 1;
					return true;
				}
				default:
					return false;
			}
		}

		// ---- Type 2: Multiple (glyphCount 0 = DELETE — spec discourages, HarfBuzz honours, Noto uses it) ----

		private bool TryApplyMultiple(FakePtr<byte> subtable, GsubGlyphBuffer buffer, int pos, out int advance)
		{
			advance = 0;
			if (subtable.RemainingLength < 6 || ttUSHORT(subtable) != 1) return false;
			int coverageIndex = GetCoverageIndexChecked(subtable, ttUSHORT(subtable + 2), buffer.GlyphIds[pos]);
			if (coverageIndex < 0) return false;
			int sequenceCount = ttUSHORT(subtable + 4);
			if (coverageIndex >= sequenceCount || subtable.RemainingLength < 6 + sequenceCount * 2) return false;
			var sequence = subtable + ttUSHORT(subtable + 6 + 2 * coverageIndex);
			if (sequence.RemainingLength < 2) return false;
			int glyphCount = ttUSHORT(sequence);
			if (glyphCount > MaxSequenceLength || sequence.RemainingLength < 2 + glyphCount * 2) return false;
			for (int i = 0; i < glyphCount; i++)
				_sequenceScratch[i] = (ushort)ttUSHORT(sequence + 2 + 2 * i);
			buffer.ReplaceRange(pos, 1, new ReadOnlySpan<ushort>(_sequenceScratch, 0, glyphCount));
			advance = glyphCount; // 0 for deletion: the pass re-examines the glyph that shifted into this position
			return true;
		}

		// ---- Type 3: Alternate (alternate index = feature value; default 1 -> first alternate) ----

		private static bool TryApplyAlternate(FakePtr<byte> subtable, GsubGlyphBuffer buffer, int pos, out int advance)
		{
			advance = 0;
			if (subtable.RemainingLength < 6 || ttUSHORT(subtable) != 1) return false;
			int coverageIndex = GetCoverageIndexChecked(subtable, ttUSHORT(subtable + 2), buffer.GlyphIds[pos]);
			if (coverageIndex < 0) return false;
			int alternateSetCount = ttUSHORT(subtable + 4);
			if (coverageIndex >= alternateSetCount || subtable.RemainingLength < 6 + alternateSetCount * 2) return false;
			var alternateSet = subtable + ttUSHORT(subtable + 6 + 2 * coverageIndex);
			if (alternateSet.RemainingLength < 4) return false;
			int glyphCount = ttUSHORT(alternateSet);
			if (glyphCount == 0 || alternateSet.RemainingLength < 2 + glyphCount * 2) return false;
			buffer.GlyphIds[pos] = (ushort)ttUSHORT(alternateSet + 2); // feature value default 1 = first alternate
			advance = 1;
			return true;
		}

		// ---- Type 4: Ligature (first match in set wins; fonts order longest-first — do NOT re-sort) ----

		private bool TryApplyLigature(FakePtr<byte> subtable, GsubGlyphBuffer buffer, int pos, IGsubGlyphSkipper skipper, int depth, out int advance)
		{
			advance = 0;
			if (subtable.RemainingLength < 6 || ttUSHORT(subtable) != 1) return false;
			int coverageIndex = GetCoverageIndexChecked(subtable, ttUSHORT(subtable + 2), buffer.GlyphIds[pos]);
			if (coverageIndex < 0) return false;
			int ligatureSetCount = ttUSHORT(subtable + 4);
			if (coverageIndex >= ligatureSetCount || subtable.RemainingLength < 6 + ligatureSetCount * 2) return false;
			var ligatureSet = subtable + ttUSHORT(subtable + 6 + 2 * coverageIndex);
			if (ligatureSet.RemainingLength < 2) return false;
			int ligatureCount = ttUSHORT(ligatureSet);
			if (ligatureSet.RemainingLength < 2 + ligatureCount * 2) return false;

			var positions = depth == 0 ? _matchPositions : _nestedMatchPositions;
			for (int li = 0; li < ligatureCount; li++)
			{
				var ligature = ligatureSet + ttUSHORT(ligatureSet + 2 + 2 * li);
				if (ligature.RemainingLength < 4) continue;
				int ligatureGlyph = ttUSHORT(ligature);
				int componentCount = ttUSHORT(ligature + 2);
				if (componentCount < 1 || componentCount > MaxSequenceLength || ligature.RemainingLength < 4 + (componentCount - 1) * 2) continue;
				// Match components 2..N against the next buffer glyphs (LookupFlag skipping applies).
				if (!MatchSequenceForward(buffer, skipper, pos, componentCount, SequenceElementKind.Glyphs, ligature + 4, FakePtr<byte>.Null, positions)) continue;
				buffer.Ligate((ushort)ligatureGlyph, new ReadOnlySpan<int>(positions, 0, componentCount));
				advance = 1; // past the ligature glyph
				return true;
			}
			return false;
		}

		// ---- Types 5/6: (Chained)SequenceContext — one shared matcher for all six formats ----

		private enum SequenceElementKind { Glyphs, Classes, Coverages }

		private bool TryApplySequenceContext(FakePtr<byte> subtable, GsubGlyphBuffer buffer, int pos, IGsubGlyphSkipper skipper, int depth, out int advance)
		{
			advance = 0;
			int format = ttUSHORT(subtable);
			int glyph = buffer.GlyphIds[pos];
			switch (format)
			{
				case 1:
				case 2:
				{
					if (subtable.RemainingLength < (format == 1 ? 6 : 8)) return false;
					int coverageIndex = GetCoverageIndexChecked(subtable, ttUSHORT(subtable + 2), glyph);
					if (coverageIndex < 0) return false;
					FakePtr<byte> classDef = FakePtr<byte>.Null;
					int ruleSetIndex;
					int ruleSetArrayBase;
					if (format == 1) { ruleSetIndex = coverageIndex; ruleSetArrayBase = 6; }
					else
					{
						classDef = subtable + ttUSHORT(subtable + 4);
						ruleSetIndex = GlyphClassOf(classDef, glyph);
						ruleSetArrayBase = 8;
					}
					int ruleSetCount = ttUSHORT(subtable + ruleSetArrayBase - 2);
					if (ruleSetIndex < 0 || ruleSetIndex >= ruleSetCount || subtable.RemainingLength < ruleSetArrayBase + ruleSetCount * 2) return false;
					int ruleSetOffset = ttUSHORT(subtable + ruleSetArrayBase + 2 * ruleSetIndex);
					if (ruleSetOffset == 0) return false; // NULL offset allowed (fmt2)
					var ruleSet = subtable + ruleSetOffset;
					if (ruleSet.RemainingLength < 2) return false;
					int ruleCount = ttUSHORT(ruleSet);
					if (ruleSet.RemainingLength < 2 + ruleCount * 2) return false;
					var elementKind = format == 1 ? SequenceElementKind.Glyphs : SequenceElementKind.Classes;
					for (int r = 0; r < ruleCount; r++)
					{
						var rule = ruleSet + ttUSHORT(ruleSet + 2 + 2 * r);
						if (rule.RemainingLength < 4) continue;
						int glyphCount = ttUSHORT(rule);
						int seqLookupCount = ttUSHORT(rule + 2);
						if (glyphCount < 1 || glyphCount > MaxSequenceLength || rule.RemainingLength < 4 + (glyphCount - 1) * 2 + seqLookupCount * 4) continue;
						if (!MatchSequenceForward(buffer, skipper, pos, glyphCount, elementKind, rule + 4, classDef, _matchPositions)) continue;
						advance = ApplySequenceLookupRecords(rule + 4 + (glyphCount - 1) * 2, seqLookupCount, buffer, glyphCount, depth, pos);
						return true;
					}
					return false;
				}
				case 3:
				{
					if (subtable.RemainingLength < 6) return false;
					int glyphCount = ttUSHORT(subtable + 2);
					int seqLookupCount = ttUSHORT(subtable + 4);
					if (glyphCount < 1 || glyphCount > MaxSequenceLength || subtable.RemainingLength < 6 + glyphCount * 2 + seqLookupCount * 4) return false;
					// fmt3: coverageOffsets[glyphCount] — element 0 gates the current glyph, elements 1.. match forward.
					if (GetCoverageIndexChecked(subtable, ttUSHORT(subtable + 6), glyph) < 0) return false;
					if (!MatchSequenceForward(buffer, skipper, pos, glyphCount, SequenceElementKind.Coverages, subtable + 8, subtable, _matchPositions)) return false;
					advance = ApplySequenceLookupRecords(subtable + 6 + glyphCount * 2, seqLookupCount, buffer, glyphCount, depth, pos);
					return true;
				}
				default:
					return false;
			}
		}

		private bool TryApplyChainedContext(FakePtr<byte> subtable, GsubGlyphBuffer buffer, int pos, IGsubGlyphSkipper skipper, int depth, out int advance)
		{
			advance = 0;
			int format = ttUSHORT(subtable);
			int glyph = buffer.GlyphIds[pos];
			switch (format)
			{
				case 1:
				case 2:
				{
					if (subtable.RemainingLength < (format == 1 ? 6 : 12)) return false;
					int coverageIndex = GetCoverageIndexChecked(subtable, ttUSHORT(subtable + 2), glyph);
					if (coverageIndex < 0) return false;
					FakePtr<byte> backtrackClassDef = FakePtr<byte>.Null, inputClassDef = FakePtr<byte>.Null, lookaheadClassDef = FakePtr<byte>.Null;
					int ruleSetIndex, ruleSetArrayBase;
					if (format == 1) { ruleSetIndex = coverageIndex; ruleSetArrayBase = 6; }
					else
					{
						backtrackClassDef = subtable + ttUSHORT(subtable + 4);
						inputClassDef = subtable + ttUSHORT(subtable + 6);
						lookaheadClassDef = subtable + ttUSHORT(subtable + 8);
						ruleSetIndex = GlyphClassOf(inputClassDef, glyph); // indexed by INPUT class of the first glyph
						ruleSetArrayBase = 12;
					}
					int ruleSetCount = ttUSHORT(subtable + ruleSetArrayBase - 2);
					if (ruleSetIndex < 0 || ruleSetIndex >= ruleSetCount || subtable.RemainingLength < ruleSetArrayBase + ruleSetCount * 2) return false;
					int ruleSetOffset = ttUSHORT(subtable + ruleSetArrayBase + 2 * ruleSetIndex);
					if (ruleSetOffset == 0) return false;
					var ruleSet = subtable + ruleSetOffset;
					if (ruleSet.RemainingLength < 2) return false;
					int ruleCount = ttUSHORT(ruleSet);
					if (ruleSet.RemainingLength < 2 + ruleCount * 2) return false;
					var elementKind = format == 1 ? SequenceElementKind.Glyphs : SequenceElementKind.Classes;
					for (int r = 0; r < ruleCount; r++)
					{
						var rule = ruleSet + ttUSHORT(ruleSet + 2 + 2 * r);
						if (rule.RemainingLength < 2) continue;
						int backtrackCount = ttUSHORT(rule);
						if (backtrackCount > MaxSequenceLength || rule.RemainingLength < 2 + backtrackCount * 2 + 2) continue;
						var inputPart = rule + 2 + backtrackCount * 2;
						int inputCount = ttUSHORT(inputPart);
						if (inputCount < 1 || inputCount > MaxSequenceLength || inputPart.RemainingLength < 2 + (inputCount - 1) * 2 + 2) continue;
						var lookaheadPart = inputPart + 2 + (inputCount - 1) * 2;
						int lookaheadCount = ttUSHORT(lookaheadPart);
						if (lookaheadCount > MaxSequenceLength || lookaheadPart.RemainingLength < 2 + lookaheadCount * 2 + 2) continue;
						var recordPart = lookaheadPart + 2 + lookaheadCount * 2;
						int seqLookupCount = ttUSHORT(recordPart);
						if (recordPart.RemainingLength < 2 + seqLookupCount * 4) continue;
						if (!MatchSequenceForward(buffer, skipper, pos, inputCount, elementKind, inputPart + 2, inputClassDef, _matchPositions)) continue;
						if (!MatchBacktrack(buffer, skipper, pos, backtrackCount, elementKind, rule + 2, backtrackClassDef)) continue;
						if (!MatchLookahead(buffer, skipper, _matchPositions[inputCount - 1], lookaheadCount, elementKind, lookaheadPart + 2, lookaheadClassDef)) continue;
						advance = ApplySequenceLookupRecords(recordPart + 2, seqLookupCount, buffer, inputCount, depth, pos);
						return true;
					}
					return false;
				}
				case 3:
				{
					if (subtable.RemainingLength < 4) return false;
					int backtrackCount = ttUSHORT(subtable + 2);
					if (backtrackCount > MaxSequenceLength || subtable.RemainingLength < 4 + backtrackCount * 2 + 2) return false;
					var inputPart = subtable + 4 + backtrackCount * 2;
					int inputCount = ttUSHORT(inputPart);
					if (inputCount < 1 || inputCount > MaxSequenceLength || inputPart.RemainingLength < 2 + inputCount * 2 + 2) return false;
					var lookaheadPart = inputPart + 2 + inputCount * 2;
					int lookaheadCount = ttUSHORT(lookaheadPart);
					if (lookaheadCount > MaxSequenceLength || lookaheadPart.RemainingLength < 2 + lookaheadCount * 2 + 2) return false;
					var recordPart = lookaheadPart + 2 + lookaheadCount * 2;
					int seqLookupCount = ttUSHORT(recordPart);
					if (recordPart.RemainingLength < 2 + seqLookupCount * 4) return false;
					if (GetCoverageIndexChecked(subtable, ttUSHORT(inputPart + 2), glyph) < 0) return false;
					if (!MatchSequenceForward(buffer, skipper, pos, inputCount, SequenceElementKind.Coverages, inputPart + 4, subtable, _matchPositions)) return false;
					if (!MatchBacktrack(buffer, skipper, pos, backtrackCount, SequenceElementKind.Coverages, subtable + 4, subtable)) return false;
					if (!MatchLookahead(buffer, skipper, _matchPositions[inputCount - 1], lookaheadCount, SequenceElementKind.Coverages, lookaheadPart + 2, subtable)) return false;
					advance = ApplySequenceLookupRecords(recordPart + 2, seqLookupCount, buffer, inputCount, depth, pos);
					return true;
				}
				default:
					return false;
			}
		}

		/// <summary>
		/// Nested SequenceLookupRecords in record order; sequenceIndex indexes the matched INPUT positions; after a
		/// nested lookup changes the buffer length, later positions shift by the delta (HarfBuzz apply_lookup law).
		/// Returns the pass advance: past the (delta-adjusted) end of the matched input span, minimum 1.
		/// </summary>
		private int ApplySequenceLookupRecords(FakePtr<byte> records, int recordCount, GsubGlyphBuffer buffer, int inputCount, int depth, int pos)
		{
			for (int r = 0; r < recordCount; r++)
			{
				int sequenceIndex = ttUSHORT(records + 4 * r);
				int lookupListIndex = ttUSHORT(records + 4 * r + 2);
				if (sequenceIndex >= inputCount) continue;
				int p = _matchPositions[sequenceIndex];
				if (p < 0 || p >= buffer.Length) continue;
				int lengthBefore = buffer.Length;
				if (!TryApplyLookupAtPosition(lookupListIndex, buffer, p, depth + 1, out _)) continue;
				int delta = buffer.Length - lengthBefore;
				if (delta != 0)
					for (int j = 0; j < inputCount; j++)
						if (_matchPositions[j] > p) _matchPositions[j] += delta;
				}
			int advance = _matchPositions[inputCount - 1] + 1 - pos;
			return advance < 1 ? 1 : advance;
		}

		// ---- Sequence matching (skipper-aware) ----

		/// <summary>Matches elements 1..count-1 forward from <paramref name="pos"/> (element 0 = current glyph, already
		/// gated by Coverage/class). Fills <paramref name="positions"/>[0..count-1] with the matched buffer positions.</summary>
		private static bool MatchSequenceForward(GsubGlyphBuffer buffer, IGsubGlyphSkipper skipper, int pos, int count,
			SequenceElementKind kind, FakePtr<byte> elements, FakePtr<byte> aux, int[] positions)
		{
			positions[0] = pos;
			int matched = 1;
			int i = pos + 1;
			while (matched < count)
			{
				if (i >= buffer.Length) return false;
				ushort g = buffer.GlyphIds[i];
				if (skipper.ShouldSkip(g)) { i++; continue; }
				if (!ElementMatches(kind, elements, aux, matched - 1, g))
				{
					// HarfBuzz matcher law: a default-ignorable that does NOT match the expected element is
					// skipped (MAYBE-skip) — e.g. an RI pair ligates across an intervening VS16.
					if (buffer.IsDefaultIgnorable[i]) { i++; continue; }
					return false;
				}
				positions[matched++] = i;
				i++;
			}
			return true;
		}

		/// <summary>Backtrack sequences are stored CLOSEST-TO-INPUT FIRST: element 0 compares against the nearest
		/// non-skipped glyph before <paramref name="pos"/>, element 1 against the next one back, and so on.</summary>
		private static bool MatchBacktrack(GsubGlyphBuffer buffer, IGsubGlyphSkipper skipper, int pos, int count,
			SequenceElementKind kind, FakePtr<byte> elements, FakePtr<byte> aux)
		{
			int i = pos - 1;
			for (int matched = 0; matched < count; )
			{
				if (i < 0) return false;
				ushort g = buffer.GlyphIds[i];
				if (skipper.ShouldSkip(g)) { i--; continue; }
				if (!ElementMatches(kind, elements, aux, matched, g))
				{
					if (buffer.IsDefaultIgnorable[i]) { i--; continue; } // HarfBuzz MAYBE-skip
					return false;
				}
				matched++;
				i--;
			}
			return true;
		}

		private static bool MatchLookahead(GsubGlyphBuffer buffer, IGsubGlyphSkipper skipper, int lastInputPos, int count,
			SequenceElementKind kind, FakePtr<byte> elements, FakePtr<byte> aux)
		{
			int i = lastInputPos + 1;
			for (int matched = 0; matched < count; )
			{
				if (i >= buffer.Length) return false;
				ushort g = buffer.GlyphIds[i];
				if (skipper.ShouldSkip(g)) { i++; continue; }
				if (!ElementMatches(kind, elements, aux, matched, g))
				{
					if (buffer.IsDefaultIgnorable[i]) { i++; continue; } // HarfBuzz MAYBE-skip
					return false;
				}
				matched++;
				i++;
			}
			return true;
		}

		private static bool ElementMatches(SequenceElementKind kind, FakePtr<byte> elements, FakePtr<byte> aux, int elementIndex, int glyph)
		{
			switch (kind)
			{
				case SequenceElementKind.Glyphs:
					return ttUSHORT(elements + 2 * elementIndex) == glyph;
				case SequenceElementKind.Classes:
					return GlyphClassOf(aux, glyph) == ttUSHORT(elements + 2 * elementIndex);
				default: // Coverages: aux = subtable base the offsets are relative to
				{
					int coverageOffset = ttUSHORT(elements + 2 * elementIndex);
					return coverageOffset != 0 && GetCoverageIndexChecked(aux, coverageOffset, glyph) >= 0;
				}
			}
		}

		/// <summary>ClassDef class with the spec default: glyphs not covered are class 0 (stbtt helper returns -1).</summary>
		private static int GlyphClassOf(FakePtr<byte> classDef, int glyph)
		{
			if (classDef.IsNull || classDef.RemainingLength < 4) return 0;
			int c = stbtt__GetGlyphClass(classDef, glyph);
			return c < 0 ? 0 : c;
		}

		/// <summary>Coverage lookup with the bounds validation of OpenTypeGsubTable.TryApplySingleSubstSubtable.</summary>
		private static int GetCoverageIndexChecked(FakePtr<byte> tableBase, int coverageOffset, int glyph)
		{
			if (coverageOffset <= 0) return -1;
			var coverage = tableBase + coverageOffset;
			if (coverage.RemainingLength < 4) return -1;
			int coverageFormat = ttUSHORT(coverage);
			int coverageEntries = ttUSHORT(coverage + 2);
			if (coverageFormat == 1 && coverage.RemainingLength < 4 + coverageEntries * 2) return -1;
			if (coverageFormat == 2 && coverage.RemainingLength < 4 + coverageEntries * 6) return -1;
			if (coverageFormat != 1 && coverageFormat != 2) return -1;
			return stbtt__GetCoverageIndex(coverage, glyph);
		}
	}
}