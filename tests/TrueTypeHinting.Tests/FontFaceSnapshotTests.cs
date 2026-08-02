using System;
using System.IO;
using StbTrueTypeSharp.TrueTypeHinting;
using Xunit;

namespace TrueTypeHinting.Tests
{
    public sealed class FontFaceSnapshotTests
    {
        [Fact]
        public void RobotoSnapshotExposesBoundedFontProgramsAndGlyphData()
        {
            byte[] trueTypeFontFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var trueTypeYHintingEngine = new TrueTypeYHintingEngine();

            bool snapshotCreated = trueTypeYHintingEngine.TryCreateFontFace(trueTypeFontFileBytes,
                new TrueTypeFaceIndex(0), out var trueTypeHintingFontFace, out var trueTypeHintingFontFaceFailure);

            Assert.True(snapshotCreated, trueTypeHintingFontFaceFailure.ToString());
            Assert.Equal(2048, trueTypeHintingFontFace.UnitsPerEm.Value);
            Assert.True(trueTypeHintingFontFace.GlyphCount.Value > 0);
            Assert.True(trueTypeHintingFontFace.MaximumProfile.MaximumPointCount.Value > 0);
            Assert.True(trueTypeHintingFontFace.FontProgram.HasFontProgram);
            Assert.True(trueTypeHintingFontFace.FontProgram.HasControlValueProgram);
            Assert.True(trueTypeHintingFontFace.FontProgram.ControlValueTable.ByteLength > 0);

            Assert.True(trueTypeHintingFontFace.TryGetRawGlyphData(new TrueTypeGlyphIndex(0),
                out var trueTypeRawGlyphData, out var trueTypeRawGlyphDataFailure), trueTypeRawGlyphDataFailure.ToString());
            Assert.True(trueTypeRawGlyphData.ByteLength >= 0);

            var sizeInstanceResult = trueTypeYHintingEngine.CreateSizeInstance(trueTypeHintingFontFace, new DevicePpemY(14));
            Assert.True(sizeInstanceResult.Succeeded, sizeInstanceResult.Failure.ToString());
            TrueTypeYHintingResult trueTypeYHintingResult = trueTypeYHintingEngine.HintGlyph(sizeInstanceResult.SizeInstance, new TrueTypeGlyphIndex(0));
            Assert.True(trueTypeYHintingResult.Succeeded, trueTypeYHintingResult.Failure.ToString());
        }

        [Fact]
        public void SnapshotOwnsCopiesRatherThanCallerMutableFontBytes()
        {
            byte[] trueTypeFontFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var trueTypeYHintingEngine = new TrueTypeYHintingEngine();
            Assert.True(trueTypeYHintingEngine.TryCreateFontFace(trueTypeFontFileBytes, new TrueTypeFaceIndex(0),
                out var trueTypeHintingFontFace, out var trueTypeHintingFontFaceFailure), trueTypeHintingFontFaceFailure.ToString());

            byte originalFontProgramFirstByte = trueTypeHintingFontFace.FontProgram.FontProgram.ToByteArray()[0];
            Array.Clear(trueTypeFontFileBytes, 0, trueTypeFontFileBytes.Length);

            Assert.Equal(originalFontProgramFirstByte, trueTypeHintingFontFace.FontProgram.FontProgram.ToByteArray()[0]);
            Assert.True(trueTypeHintingFontFace.TryGetRawGlyphData(new TrueTypeGlyphIndex(0),
                out _, out var trueTypeRawGlyphDataFailure), trueTypeRawGlyphDataFailure.ToString());
        }

        [Fact]
        public void TruncatedSfntDirectoryFailsWithoutThrowing()
        {
            byte[] truncatedTrueTypeFontFileBytes = { 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
            var trueTypeYHintingEngine = new TrueTypeYHintingEngine();

            bool snapshotCreated = trueTypeYHintingEngine.TryCreateFontFace(truncatedTrueTypeFontFileBytes,
                new TrueTypeFaceIndex(0), out _, out var trueTypeHintingFontFaceFailure);

            Assert.False(snapshotCreated);
            Assert.Equal(TrueTypeHintingFailureCode.InvalidSfntDirectory, trueTypeHintingFontFaceFailure.FailureCode);
        }

        [Fact]
        public void TableRangeOutsideFontFailsWithoutThrowing()
        {
            byte[] malformedTrueTypeFontFileBytes =
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                (byte)'h', (byte)'e', (byte)'a', (byte)'d', 0, 0, 0, 0,
                0x7F, 0xFF, 0xFF, 0xF0, 0, 0, 0, 54,
            };
            var trueTypeYHintingEngine = new TrueTypeYHintingEngine();

            bool snapshotCreated = trueTypeYHintingEngine.TryCreateFontFace(malformedTrueTypeFontFileBytes,
                new TrueTypeFaceIndex(0), out _, out var trueTypeHintingFontFaceFailure);

            Assert.False(snapshotCreated);
            Assert.Equal(TrueTypeHintingFailureCode.TruncatedTable, trueTypeHintingFontFaceFailure.FailureCode);
        }

        [Fact]
        public void StandaloneFontRejectsNonzeroFaceIndex()
        {
            byte[] trueTypeFontFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var trueTypeYHintingEngine = new TrueTypeYHintingEngine();

            bool snapshotCreated = trueTypeYHintingEngine.TryCreateFontFace(trueTypeFontFileBytes,
                new TrueTypeFaceIndex(1), out _, out var trueTypeHintingFontFaceFailure);

            Assert.False(snapshotCreated);
            Assert.Equal(TrueTypeHintingFailureCode.InvalidFaceIndex, trueTypeHintingFontFaceFailure.FailureCode);
        }
    }
}