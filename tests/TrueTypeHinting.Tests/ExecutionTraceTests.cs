using System;
using System.IO;
using StbTrueTypeSharp;
using StbTrueTypeSharp.TrueTypeHinting;
using StbTrueTypeSharp.TrueTypeHinting.Diagnostics;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.Geometry;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;
using Xunit;
using Xunit.Abstractions;
using static StbTrueTypeSharp.Common;

namespace TrueTypeHinting.Tests
{
    public sealed class ExecutionTraceTests
    {
        private readonly ITestOutputHelper _testOutput;

        public ExecutionTraceTests(ITestOutputHelper testOutput) => _testOutput = testOutput;

        [Fact]
        public void TraceCapturesPostInstructionStackGraphicsStateAndPointMovement()
        {
            var point = new TrueTypeHintingPoint(10, 90, true);
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]),
                new TrueTypeHintingZone(new[] { point }, new[] { new TrueTypeContourEndPointIndex(0) }));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            var trace = new TrueTypeHintingExecutionTrace(TrueTypeExecutionTraceLimits.ForTests());
            var interpreter = new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());

            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.MoveDirectAbsolutePointWithRounding,
            }, state, zones, trace);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(2, trace.Entries.Count);
            TrueTypeInstructionTraceEntry pushEntry = trace.Entries[0];
            Assert.Equal(0, pushEntry.InstructionBytePosition);
            Assert.Equal(0xB0, pushEntry.Opcode);
            Assert.Equal(new[] { 0 }, pushEntry.OperandStackValues);
            Assert.Equal(90, pushEntry.GlyphPoints[0].CurrentVerticalF26Dot6);
            TrueTypeInstructionTraceEntry moveEntry = trace.Entries[1];
            Assert.Equal(2, moveEntry.InstructionBytePosition);
            Assert.Equal((byte)TrueTypeInstructionOpcode.MoveDirectAbsolutePointWithRounding, moveEntry.Opcode);
            Assert.Empty(moveEntry.OperandStackValues);
            Assert.Equal(64, moveEntry.GlyphPoints[0].CurrentVerticalF26Dot6);
            Assert.Equal(TrueTypePointTouchFlags.Vertical, moveEntry.GlyphPoints[0].TouchFlags);
            Assert.Equal(TrueTypeRoundingMode.ToGrid, moveEntry.RoundingMode);
        }

        [Fact]
        public void TraceRecordsNestedFunctionCallDepthAndDeterministicText()
        {
            var trace = new TrueTypeHintingExecutionTrace(TrueTypeExecutionTraceLimits.ForTests());
            var interpreter = new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());
            byte[] program =
            {
                0xB0, 3,
                (byte)TrueTypeInstructionOpcode.FunctionDefinition,
                    0xB0, 7,
                (byte)TrueTypeInstructionOpcode.EndFunction,
                0xB0, 3,
                (byte)TrueTypeInstructionOpcode.Call,
            };

            TrueTypeVirtualMachineResult result = interpreter.Execute(program,
                TrueTypeVirtualMachineState.ForTests(), null, trace);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Contains(trace.Entries, entry => entry.CallDepth == 1 && entry.Opcode == 0xB0);
            string firstText = trace.ToDeterministicText();
            string secondText = trace.ToDeterministicText();
            Assert.Equal(firstText, secondText);
            Assert.Contains("depth=1", firstText);
            Assert.Contains("op=0xB0", firstText);
        }

        [Fact]
        public void TraceLimitsCapEntriesStackAndPointsWithoutChangingExecution()
        {
            var points = new[]
            {
                new TrueTypeHintingPoint(0, 0, true),
                new TrueTypeHintingPoint(64, 0, true),
                new TrueTypeHintingPoint(128, 0, true),
            };
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]),
                new TrueTypeHintingZone(points, new[] { new TrueTypeContourEndPointIndex(2) }));
            var trace = new TrueTypeHintingExecutionTrace(new TrueTypeExecutionTraceLimits(
                maximumEntryCount: 1, maximumStackValueCountPerEntry: 1, maximumPointCountPerZonePerEntry: 2));
            var interpreter = new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());

            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB1, 1, 2,
                (byte)TrueTypeInstructionOpcode.Add,
            }, TrueTypeVirtualMachineState.ForTests(), zones, trace);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 3 }, Array.ConvertAll(result.FinalOperandStack, operand => operand.Value));
            Assert.Single(trace.Entries);
            Assert.Single(trace.Entries[0].OperandStackValues);
            Assert.Equal(2, trace.Entries[0].GlyphPoints.Length);
            Assert.True(trace.WasTruncated);
            Assert.Contains("[trace-truncated]", trace.ToDeterministicText());
        }

        [Theory]
        [InlineData('T')]
        [InlineData('h')]
        [InlineData('g')]
        public void RobotoPriorityGlyphProducesBoundedDifferentialTraceArtifact(char probeCharacter)
        {
            byte[] fontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var fontInfo = new FontInfo();
            Assert.NotEqual(0, fontInfo.stbtt_InitFont(fontBytes, stbtt_GetFontOffsetForIndex(fontBytes, 0)));
            int glyphIndex = fontInfo.stbtt_FindGlyphIndex(probeCharacter);
            var engine = new TrueTypeYHintingEngine();
            Assert.True(engine.TryCreateFontFace(fontBytes, new TrueTypeFaceIndex(0),
                out TrueTypeHintingFontFace fontFace, out TrueTypeYHintingFailure faceFailure), faceFailure.ToString());
            var sizeResult = engine.CreateSizeInstance(fontFace, new DevicePpemY(14));
            Assert.True(sizeResult.Succeeded, sizeResult.Failure.ToString());
            var trace = new TrueTypeHintingExecutionTrace(new TrueTypeExecutionTraceLimits(
                maximumEntryCount: 20000, maximumStackValueCountPerEntry: 512, maximumPointCountPerZonePerEntry: 2048));

            TrueTypeYHintingResult hintingResult = engine.HintGlyph(sizeResult.SizeInstance,
                new TrueTypeGlyphIndex(glyphIndex), trace);

            Assert.True(hintingResult.Succeeded, hintingResult.Failure.ToString());
            Assert.NotEmpty(trace.Entries);
            Assert.False(trace.WasTruncated);
            string traceArtifact = trace.ToDeterministicText();
            Assert.Contains("z1[", traceArtifact);
            _testOutput.WriteLine("Roboto '{0}' glyph {1}: {2} traced instructions, {3} chars.",
                probeCharacter, glyphIndex, trace.Entries.Count, traceArtifact.Length);
        }
    }
}