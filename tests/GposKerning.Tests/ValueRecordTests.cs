using System.Collections.Generic;
using StbTrueTypeSharp;
using Xunit;

namespace GposKerning.Tests;

// Synthetic PairPos subtable blobs exercising ValueRecord layouts that the four
// target fonts never use (they are all vf1=0x0004/vf2=0). Validates the exact
// formulas from the plan:
//   record size      = 2 x popcount(valueFormat)   (device-table bits count too)
//   xAdvance offset  = 2 x popcount(valueFormat & 0x0003)
// Tests stbtt__GPOSPairSubtableApply directly — no font file needed.
public class ValueRecordTests
{
    // ---- blob builders (big-endian) ----
    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }

    private static int PopCount(int v) => System.Numerics.BitOperations.PopCount((uint)v);

    // Fills a ValueRecord: every 2-byte field gets `filler`, except the XAdvance
    // field (if present) which gets `xAdvance`. Device-table bits get offset 0.
    private static void ValueRecord(List<byte> b, int valueFormat, int xAdvance, int filler)
    {
        for (int bit = 0; bit < 8; bit++)
        {
            if ((valueFormat & (1 << bit)) == 0) continue;
            if (bit == 2) U16(b, xAdvance & 0xFFFF);
            else if (bit >= 4) U16(b, 0);       // device-table offset: 0 = none
            else U16(b, filler & 0xFFFF);       // XPlacement/YPlacement/YAdvance junk
        }
    }

    // Coverage format 1 listing the given glyphs (must be sorted).
    private static List<byte> Coverage(params int[] glyphs)
    {
        var b = new List<byte>();
        U16(b, 1); U16(b, glyphs.Length);
        foreach (var g in glyphs) U16(b, g);
        return b;
    }

    // PairPos format 1 with ONE pairSet for glyph1 containing (glyph2 -> xAdvance).
    private static byte[] PairPosFmt1(int vf1, int vf2, int g1, int g2, int xAdv, int filler)
    {
        var b = new List<byte>();
        U16(b, 1);            // posFormat
        int covOffPos = b.Count; U16(b, 0);   // coverageOffset (patched below)
        U16(b, vf1); U16(b, vf2);
        U16(b, 1);            // pairSetCount
        int ps0Pos = b.Count; U16(b, 0);      // pairSetOffset[0] (patched below)
        // pairSet
        int pairSetStart = b.Count;
        b[ps0Pos] = (byte)(pairSetStart >> 8); b[ps0Pos + 1] = (byte)pairSetStart;
        U16(b, 1);            // pairValueCount
        U16(b, g2);           // secondGlyph
        ValueRecord(b, vf1, xAdv, filler);
        ValueRecord(b, vf2, 0, filler);
        // coverage
        int covStart = b.Count;
        b[covOffPos] = (byte)(covStart >> 8); b[covOffPos + 1] = (byte)covStart;
        b.AddRange(Coverage(g1));
        return b.ToArray();
    }

    // PairPos format 2 with class1Count=class2Count=2; value only in cell (1,1).
    // ClassDef format 1: g1 -> class 1, g2 -> class 1 (others default class 0).
    private static byte[] PairPosFmt2(int vf1, int vf2, int g1, int g2, int xAdv, int filler)
    {
        var b = new List<byte>();
        U16(b, 2);            // posFormat
        int covOffPos = b.Count; U16(b, 0);
        U16(b, vf1); U16(b, vf2);
        int cd1Pos = b.Count; U16(b, 0);
        int cd2Pos = b.Count; U16(b, 0);
        U16(b, 2); U16(b, 2); // class1Count, class2Count
        // class1Records: 2x2 cells
        for (int c1 = 0; c1 < 2; c1++)
        for (int c2 = 0; c2 < 2; c2++)
        {
            ValueRecord(b, vf1, (c1 == 1 && c2 == 1) ? xAdv : 0, filler);
            ValueRecord(b, vf2, 0, filler);
        }
        int cd1 = b.Count;
        b[cd1Pos] = (byte)(cd1 >> 8); b[cd1Pos + 1] = (byte)cd1;
        U16(b, 1); U16(b, g1); U16(b, 1); U16(b, 1); // ClassDef fmt1: [g1] -> class 1
        int cd2 = b.Count;
        b[cd2Pos] = (byte)(cd2 >> 8); b[cd2Pos + 1] = (byte)cd2;
        U16(b, 1); U16(b, g2); U16(b, 1); U16(b, 1); // ClassDef fmt1: [g2] -> class 1
        int cov = b.Count;
        b[covOffPos] = (byte)(cov >> 8); b[covOffPos + 1] = (byte)cov;
        b.AddRange(Coverage(g1));
        return b.ToArray();
    }

    private static int Apply(byte[] blob, int g1, int g2, out bool applied)
    {
        applied = FontInfo.stbtt__GPOSPairSubtableApply(new FakePtr<byte>(blob), g1, g2, out var xAdv);
        return xAdv;
    }

    // valueFormats to exercise: plain, placement+advance, all-scalars, device bits, vf2 nonzero
    public static TheoryData<int, int> Formats => new()
    {
        { 0x0004, 0x0000 }, // XAdvance only (what real fonts use)
        { 0x0005, 0x0000 }, // XPlacement | XAdvance
        { 0x0007, 0x0000 }, // XPlacement | YPlacement | XAdvance
        { 0x000F, 0x0000 }, // all four scalars
        { 0x0054, 0x0000 }, // XAdvance + XPlaDevice + XAdvDevice (device bits size-only)
        { 0x00F7, 0x0000 }, // everything except YAdvance
        { 0x0004, 0x0004 }, // second-glyph record present (must not shift stride wrongly)
        { 0x0005, 0x00F7 }, // fat second record
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void Fmt1_XAdvanceReadAtCorrectOffset(int vf1, int vf2)
    {
        var blob = PairPosFmt1(vf1, vf2, g1: 30, g2: 40, xAdv: -123, filler: 0x2222);
        var xAdv = Apply(blob, 30, 40, out var applied);
        Assert.True(applied);
        Assert.Equal(-123, xAdv);
        // pair not in pairSet -> NOT applied (fall-through contract)
        Apply(blob, 30, 41, out var missApplied);
        Assert.False(missApplied);
        // glyph1 not covered -> not applied
        Apply(blob, 31, 40, out var uncovered);
        Assert.False(uncovered);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Fmt2_XAdvanceReadAtCorrectOffset(int vf1, int vf2)
    {
        var blob = PairPosFmt2(vf1, vf2, g1: 30, g2: 40, xAdv: -77, filler: 0x3333);
        var xAdv = Apply(blob, 30, 40, out var applied);
        Assert.True(applied);
        Assert.Equal(-77, xAdv);
        // class-0 second glyph: cell (1,0) = 0, but subtable still APPLIES (fmt2 semantics)
        var xAdv2 = Apply(blob, 30, 99, out var appliedZero);
        Assert.True(appliedZero);
        Assert.Equal(0, xAdv2);
    }

    [Fact]
    public void Fmt1_NoXAdvanceBit_AppliesWithZero()
    {
        // vf1 = XPlacement only: subtable applies (pair record exists) but xAdvance = 0
        var blob = PairPosFmt1(0x0001, 0x0000, g1: 30, g2: 40, xAdv: 0, filler: 0x1111);
        var xAdv = Apply(blob, 30, 40, out var applied);
        Assert.True(applied);
        Assert.Equal(0, xAdv);
    }
}