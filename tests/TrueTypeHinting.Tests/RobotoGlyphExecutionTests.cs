using System;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.TrueTypeHinting;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.SizeInstance;
using Xunit;
using Xunit.Abstractions;
using static StbTrueTypeSharp.Common;

namespace TrueTypeHinting.Tests
{
    public sealed class RobotoGlyphExecutionTests
    {
        private readonly ITestOutputHelper _testOutput;
        public RobotoGlyphExecutionTests(ITestOutputHelper testOutput) => _testOutput = testOutput;

        [Theory]
        [InlineData('T')]
        [InlineData('h')]
        [InlineData('g')]
        public void RobotoPriorityGlyphExecutesAt14PpemOrReportsRemainingOpcode(char probeCharacter)
        {
            byte[] fontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var fontInfo = new FontInfo();
            Assert.NotEqual(0, fontInfo.stbtt_InitFont(fontBytes, stbtt_GetFontOffsetForIndex(fontBytes, 0)));
            int glyphIndex = fontInfo.stbtt_FindGlyphIndex(probeCharacter);
            var engine = new TrueTypeYHintingEngine();
            Assert.True(engine.TryCreateFontFace(fontBytes, new TrueTypeFaceIndex(0),
                out TrueTypeHintingFontFace fontFace, out TrueTypeYHintingFailure faceFailure), faceFailure.ToString());
            TrueTypeHintingSizeInstanceResult sizeResult = engine.CreateSizeInstance(fontFace, new DevicePpemY(14));
            Assert.True(sizeResult.Succeeded, sizeResult.Failure.ToString());

            TrueTypeYHintingResult hintingResult = engine.HintGlyph(sizeResult.SizeInstance, new TrueTypeGlyphIndex(glyphIndex));
            _testOutput.WriteLine(hintingResult.Succeeded
                ? $"Roboto '{probeCharacter}' glyph {glyphIndex} executed at 14ppem."
                : $"Roboto '{probeCharacter}' stopped safely: {hintingResult.Failure}");
            Assert.True(hintingResult.Succeeded, hintingResult.Failure.ToString());
            Assert.NotEmpty(hintingResult.TrueTypeHintedPoints);
        }
    }
}