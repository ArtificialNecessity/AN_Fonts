using System;
using System.IO;
using StbTrueTypeSharp.TrueTypeHinting;
using StbTrueTypeSharp.TrueTypeHinting.Diagnostics;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.SizeInstance;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;
using Xunit;
using Xunit.Abstractions;

namespace TrueTypeHinting.Tests
{
    public sealed class SizeInstanceLifecycleTests
    {
        private readonly ITestOutputHelper _trueTypeHintingTestOutput;

        public SizeInstanceLifecycleTests(ITestOutputHelper trueTypeHintingTestOutput)
            => _trueTypeHintingTestOutput = trueTypeHintingTestOutput;

        [Fact]
        public void ControlValuesScaleFromFontUnitsToF26Dot6AtDevicePpem()
        {
            byte[] controlValueTableBytes =
            {
                0x04, 0x00, // +1024 font units
                0xFC, 0x00, // -1024 font units
                0x00, 0x01, // +1 font unit
            };

            bool controlValuesScaled = TrueTypeHintingSizeInstanceFactory.TryScaleControlValues(
                controlValueTableBytes, trueTypeUnitsPerEm: 2048, devicePpemY: 14,
                out int[] scaledControlValues, out TrueTypeYHintingFailure controlValueScalingFailure);

            Assert.True(controlValuesScaled, controlValueScalingFailure.ToString());
            Assert.Equal(new[] { 448, -448, 0 }, scaledControlValues);
        }

        [Fact]
        public void OddLengthControlValueTableFailsWithoutPartialValues()
        {
            bool controlValuesScaled = TrueTypeHintingSizeInstanceFactory.TryScaleControlValues(
                new byte[] { 0x00 }, trueTypeUnitsPerEm: 2048, devicePpemY: 14,
                out int[] scaledControlValues, out TrueTypeYHintingFailure controlValueScalingFailure);

            Assert.False(controlValuesScaled);
            Assert.Null(scaledControlValues);
            Assert.Equal(TrueTypeHintingFailureCode.MalformedControlValueTable, controlValueScalingFailure.FailureCode);
        }

        [Fact]
        public void OpcodeAnalyzerExcludesPushPayloadBytes()
        {
            var instructionTable = new TrueTypeTableBytes(new byte[]
            {
                0xB2, 0x00, 0x2C, 0x89,
                0x40, 2, 0x2D, 0x83,
                0x2B,
            });

            TrueTypeInstructionUsageReport usageReport = TrueTypeInstructionUsageAnalyzer.Analyze(instructionTable);

            Assert.True(usageReport.Succeeded, usageReport.AnalysisFailure.ToString());
            Assert.Equal(3, usageReport.OpcodeUsageEntries.Count);
            Assert.Equal(0x2B, usageReport.OpcodeUsageEntries[0].TrueTypeOpcode.Value);
            Assert.Equal(0x40, usageReport.OpcodeUsageEntries[1].TrueTypeOpcode.Value);
            Assert.Equal(0xB2, usageReport.OpcodeUsageEntries[2].TrueTypeOpcode.Value);
        }

        [Fact]
        public void RealRobotoProgramInventoryAndPreparationAreDiagnosable()
        {
            byte[] trueTypeFontFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var trueTypeYHintingEngine = new TrueTypeYHintingEngine();
            Assert.True(trueTypeYHintingEngine.TryCreateFontFace(trueTypeFontFileBytes, new TrueTypeFaceIndex(0),
                out TrueTypeHintingFontFace trueTypeHintingFontFace, out TrueTypeYHintingFailure fontFaceFailure),
                fontFaceFailure.ToString());

            TrueTypeInstructionUsageReport fontProgramUsage = TrueTypeInstructionUsageAnalyzer.Analyze(
                trueTypeHintingFontFace.FontProgram.FontProgram);
            TrueTypeInstructionUsageReport controlValueProgramUsage = TrueTypeInstructionUsageAnalyzer.Analyze(
                trueTypeHintingFontFace.FontProgram.ControlValueProgram);
            Assert.True(fontProgramUsage.Succeeded, fontProgramUsage.AnalysisFailure.ToString());
            Assert.True(controlValueProgramUsage.Succeeded, controlValueProgramUsage.AnalysisFailure.ToString());
            Assert.NotEmpty(fontProgramUsage.OpcodeUsageEntries);
            Assert.NotEmpty(controlValueProgramUsage.OpcodeUsageEntries);

            _trueTypeHintingTestOutput.WriteLine("Roboto fpgm opcodes: " + FormatOpcodeUsage(fontProgramUsage));
            _trueTypeHintingTestOutput.WriteLine("Roboto prep opcodes: " + FormatOpcodeUsage(controlValueProgramUsage));

            TrueTypeHintingSizeInstanceResult sizeInstanceResult = trueTypeYHintingEngine.CreateSizeInstance(
                trueTypeHintingFontFace, new DevicePpemY(14));
            Assert.True(sizeInstanceResult.Succeeded, sizeInstanceResult.Failure.ToString());
            _trueTypeHintingTestOutput.WriteLine("Roboto 14ppem preparation succeeded.");
            Assert.Equal(14, sizeInstanceResult.SizeInstance.DevicePpemY.Value);
            Assert.True(sizeInstanceResult.SizeInstance.ScaledControlValueCount > 0);
            Assert.True(sizeInstanceResult.SizeInstance.FontProgramInstructionCount > 0);
            Assert.True(sizeInstanceResult.SizeInstance.ControlValueProgramInstructionCount > 0);
        }

        [Fact]
        public void FaceRuntimeExecutesFontProgramOnceAndCachesPreparedPpemInstances()
        {
            byte[] trueTypeFontFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.ttf"));
            var trueTypeYHintingEngine = new TrueTypeYHintingEngine();
            Assert.True(trueTypeYHintingEngine.TryCreateFontFace(trueTypeFontFileBytes, new TrueTypeFaceIndex(0),
                out TrueTypeHintingFontFace trueTypeHintingFontFace, out TrueTypeYHintingFailure fontFaceFailure),
                fontFaceFailure.ToString());
            Assert.True(trueTypeYHintingEngine.TryGetOrCreateFaceRuntime(trueTypeHintingFontFace,
                out TrueTypeHintingFaceRuntime firstFaceRuntime, out TrueTypeYHintingFailure firstRuntimeFailure),
                firstRuntimeFailure.ToString());
            Assert.True(trueTypeYHintingEngine.TryGetOrCreateFaceRuntime(trueTypeHintingFontFace,
                out TrueTypeHintingFaceRuntime secondFaceRuntime, out TrueTypeYHintingFailure secondRuntimeFailure),
                secondRuntimeFailure.ToString());

            Assert.Same(firstFaceRuntime, secondFaceRuntime);
            Assert.True(firstFaceRuntime.FontProgramInstructionCount > 0);
            Assert.Equal(0, firstFaceRuntime.CachedSizeInstanceCount);

            TrueTypeHintingSizeInstanceResult firstFourteenPpemResult =
                firstFaceRuntime.GetOrCreateSizeInstance(new DevicePpemY(14));
            TrueTypeHintingSizeInstanceResult secondFourteenPpemResult =
                firstFaceRuntime.GetOrCreateSizeInstance(new DevicePpemY(14));
            TrueTypeHintingSizeInstanceResult sixteenPpemResult =
                firstFaceRuntime.GetOrCreateSizeInstance(new DevicePpemY(16));

            Assert.True(firstFourteenPpemResult.Succeeded, firstFourteenPpemResult.Failure.ToString());
            Assert.True(sixteenPpemResult.Succeeded, sixteenPpemResult.Failure.ToString());
            Assert.Same(firstFourteenPpemResult, secondFourteenPpemResult);
            Assert.Same(firstFourteenPpemResult.SizeInstance, secondFourteenPpemResult.SizeInstance);
            Assert.NotSame(firstFourteenPpemResult.SizeInstance, sixteenPpemResult.SizeInstance);
            Assert.Equal(2, firstFaceRuntime.CachedSizeInstanceCount);
            Assert.Equal(firstFaceRuntime.FontProgramInstructionCount,
                firstFourteenPpemResult.SizeInstance.FontProgramInstructionCount);
            Assert.Equal(firstFaceRuntime.FontProgramInstructionCount,
                sixteenPpemResult.SizeInstance.FontProgramInstructionCount);
        }

        [Fact]
        public void GlyphExecutionStateClonesPreparedDefaultsAndResetsGlyphLocalReferences()
        {
            TrueTypeVirtualMachineState preparedState = TrueTypeVirtualMachineState.ForTests(4, 64, 128);
            preparedState.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            preparedState.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            preparedState.GraphicsState.RoundingMode = TrueTypeRoundingMode.Off;
            preparedState.GraphicsState.MinimumDistanceF26Dot6 = 32;
            preparedState.GraphicsState.ReferencePointZero = new TrueTypeReferencePointIndex(17);
            preparedState.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            preparedState.GraphicsState.LoopCount = new TrueTypeLoopCount(9);
            Assert.True(preparedState.TryWriteStorage(2, 71, out _));

            TrueTypeVirtualMachineState firstGlyphState = preparedState.CloneForGlyphExecution();
            TrueTypeVirtualMachineState secondGlyphState = preparedState.CloneForGlyphExecution();

            Assert.Equal(0, firstGlyphState.GraphicsState.ReferencePointZero.Value);
            Assert.Equal(1, firstGlyphState.GraphicsState.ZonePointerZero.Value);
            Assert.Equal(1, firstGlyphState.GraphicsState.LoopCount.Value);
            Assert.Equal(TrueTypeUnitVector.Vertical.VerticalComponent.Value,
                firstGlyphState.GraphicsState.ProjectionVector.VerticalComponent.Value);
            Assert.Equal(TrueTypeRoundingMode.Off, firstGlyphState.GraphicsState.RoundingMode);
            Assert.Equal(32, firstGlyphState.GraphicsState.MinimumDistanceF26Dot6);
            Assert.True(firstGlyphState.TryReadStorage(2, out int clonedStorageValue, out _));
            Assert.Equal(71, clonedStorageValue);

            firstGlyphState.GraphicsState.ReferencePointZero = new TrueTypeReferencePointIndex(99);
            Assert.True(firstGlyphState.TryWriteStorage(2, 100, out _));
            Assert.Equal(0, secondGlyphState.GraphicsState.ReferencePointZero.Value);
            Assert.True(secondGlyphState.TryReadStorage(2, out int isolatedStorageValue, out _));
            Assert.Equal(71, isolatedStorageValue);
        }

        [Fact]
        public void DirectWriteGetInfoReportsVersion40AndGrayscaleSymmetricCapabilities()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests();
            var interpreter = new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB8, 0x1C, 0x41,
                (byte)TrueTypeInstructionOpcode.GetInformation,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(40 | (1 << 13) | (1 << 17) | (1 << 18) | (1 << 19),
                result.FinalOperandStack[0].Value);
        }

        [Fact]
        public void DeviceMeasurementsAndPrepControlsUsePreparedEnvironment()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests();
            var interpreter = new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                (byte)TrueTypeInstructionOpcode.MeasurePixelsPerEm,
                (byte)TrueTypeInstructionOpcode.MeasurePointSize,
                0xB0, 12, (byte)TrueTypeInstructionOpcode.SetDeltaBase,
                0xB0, 4, (byte)TrueTypeInstructionOpcode.SetDeltaShift,
                0xB8, 0x01, 0xFF, (byte)TrueTypeInstructionOpcode.ScanControl,
                0xB0, 5, (byte)TrueTypeInstructionOpcode.ScanType,
                0xB1, 1, 1, (byte)TrueTypeInstructionOpcode.InstructionControl,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 16, 768 }, Values(result));
            Assert.Equal(12, machineState.GraphicsState.DeltaBasePpem);
            Assert.Equal(4, machineState.GraphicsState.DeltaShift);
            Assert.Equal(0x01FF, machineState.GraphicsState.ScanControlFlags);
            Assert.Equal(5, machineState.GraphicsState.ScanType);
            Assert.Equal(1, machineState.GraphicsState.InstructionControlFlags & 1);
        }

        private static string FormatOpcodeUsage(TrueTypeInstructionUsageReport usageReport)
        {
            var usageParts = new string[usageReport.OpcodeUsageEntries.Count];
            for (int usageEntryIndex = 0; usageEntryIndex < usageParts.Length; usageEntryIndex++)
            {
                TrueTypeOpcodeUsageEntry usageEntry = usageReport.OpcodeUsageEntries[usageEntryIndex];
                usageParts[usageEntryIndex] = usageEntry.TrueTypeOpcode + "=" + usageEntry.OccurrenceCount.Value;
            }
            return string.Join(",", usageParts);
        }

        private static int[] Values(TrueTypeVirtualMachineResult result)
        {
            var values = new int[result.FinalOperandStack.Length];
            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                values[valueIndex] = result.FinalOperandStack[valueIndex].Value;
            return values;
        }
    }
}