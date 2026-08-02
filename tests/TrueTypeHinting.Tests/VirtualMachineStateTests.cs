using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;
using Xunit;

namespace TrueTypeHinting.Tests
{
    public sealed class VirtualMachineStateTests
    {
        [Fact]
        public void FunctionDefinitionAndCallUseSharedOperandStack()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 3,
                (byte)TrueTypeInstructionOpcode.FunctionDefinition,
                    0xB0, 1,
                    (byte)TrueTypeInstructionOpcode.Add,
                (byte)TrueTypeInstructionOpcode.EndFunction,
                0xB0, 9,
                0xB0, 3,
                (byte)TrueTypeInstructionOpcode.Call,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 10 }, Values(result));
        }

        [Fact]
        public void LoopCallExecutesFunctionRequestedNumberOfTimes()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 4,
                (byte)TrueTypeInstructionOpcode.FunctionDefinition,
                    0xB0, 2,
                    (byte)TrueTypeInstructionOpcode.Add,
                (byte)TrueTypeInstructionOpcode.EndFunction,
                0xB0, 1,
                0xB1, 3, 4, // repeat count, then function identifier (topmost)
                (byte)TrueTypeInstructionOpcode.LoopCall,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 7 }, Values(result));
        }

        [Fact]
        public void RecursiveFunctionStopsAtCallDepthLimit()
        {
            var interpreter = new TrueTypeInstructionInterpreter(
                TrueTypeExecutionLimits.ForTests(callDepthLimitValue: 3));
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB0, 5,
                (byte)TrueTypeInstructionOpcode.FunctionDefinition,
                    0xB0, 5,
                    (byte)TrueTypeInstructionOpcode.Call,
                (byte)TrueTypeInstructionOpcode.EndFunction,
                0xB0, 5,
                (byte)TrueTypeInstructionOpcode.Call,
            });

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.CallDepthLimitExceeded, result.Failure.FailureCode);
        }

        [Fact]
        public void InstructionDefinitionHandlesOtherwiseUnsupportedOpcode()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0x83,
                (byte)TrueTypeInstructionOpcode.InstructionDefinition,
                    0xB0, 77,
                (byte)TrueTypeInstructionOpcode.EndFunction,
                0x83,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 77 }, Values(result));
        }

        [Fact]
        public void StorageWriteAndReadPersistWithinMachineState()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests(storageCapacity: 4);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 2, 41,
                (byte)TrueTypeInstructionOpcode.WriteStorage,
                0xB0, 2,
                (byte)TrueTypeInstructionOpcode.ReadStorage,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 41 }, Values(result));
            Assert.True(machineState.TryReadStorage(2, out int storedValue, out _));
            Assert.Equal(41, storedValue);
        }

        [Fact]
        public void CvtWriteAndReadUseF26Dot6Values()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests(4, 64, 128);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 96,
                (byte)TrueTypeInstructionOpcode.WriteControlValuePixels,
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.ReadControlValue,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 96 }, Values(result));
        }

        [Fact]
        public void InvalidStorageAndCvtIndicesReturnStructuredFailures()
        {
            TrueTypeVirtualMachineResult storageResult = Interpreter().Execute(new byte[]
            {
                0xB0, 9,
                (byte)TrueTypeInstructionOpcode.ReadStorage,
            }, TrueTypeVirtualMachineState.ForTests(storageCapacity: 1));
            Assert.False(storageResult.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidStorageIndex, storageResult.Failure.FailureCode);

            TrueTypeVirtualMachineResult cvtResult = Interpreter().Execute(new byte[]
            {
                0xB0, 9,
                (byte)TrueTypeInstructionOpcode.ReadControlValue,
            }, TrueTypeVirtualMachineState.ForTests(1, 64));
            Assert.False(cvtResult.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidControlValueIndex, cvtResult.Failure.FailureCode);
        }

        [Fact]
        public void VectorAndRoundingOpcodesUpdateGraphicsState()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                (byte)TrueTypeInstructionOpcode.SetVectorsToYAxis,
                (byte)TrueTypeInstructionOpcode.RoundOff,
                0xB0, 32,
                (byte)TrueTypeInstructionOpcode.SetMinimumDistance,
                (byte)TrueTypeInstructionOpcode.FlipOff,
                (byte)TrueTypeInstructionOpcode.GetProjectionVector,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(0, machineState.GraphicsState.ProjectionVector.HorizontalComponent.Value);
            Assert.Equal(0x4000, machineState.GraphicsState.ProjectionVector.VerticalComponent.Value);
            Assert.Equal(0, machineState.GraphicsState.FreedomVector.HorizontalComponent.Value);
            Assert.Equal(0x4000, machineState.GraphicsState.FreedomVector.VerticalComponent.Value);
            Assert.Equal(TrueTypeRoundingMode.Off, machineState.GraphicsState.RoundingMode);
            Assert.Equal(32, machineState.GraphicsState.MinimumDistanceF26Dot6);
            Assert.False(machineState.GraphicsState.AutoFlip);
            Assert.Equal(new[] { 0, 0x4000 }, Values(result));
        }

        [Fact]
        public void SuperRoundDecodesPeriodPhaseThresholdAndRoundsSignedValues()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0x48, // period=1px, phase=0, threshold=1/2 period
                (byte)TrueTypeInstructionOpcode.SetSuperRound,
                0xB8, 0x00, 0x5F,
                (byte)TrueTypeInstructionOpcode.RoundGrayDistance,
                0xB8, 0xFF, 0xA1,
                (byte)TrueTypeInstructionOpcode.RoundGrayDistance,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(TrueTypeRoundingMode.Super, machineState.GraphicsState.RoundingMode);
            Assert.Equal(64, machineState.GraphicsState.SuperRoundingState.PeriodF26Dot6);
            Assert.Equal(0, machineState.GraphicsState.SuperRoundingState.PhaseF26Dot6);
            Assert.Equal(32, machineState.GraphicsState.SuperRoundingState.ThresholdF26Dot6);
            Assert.Equal(new[] { 64, -64 }, Values(result));
        }

        [Fact]
        public void SuperRoundHonorsQuarterPhaseAndCeilingThreshold()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0x50, // period=1px, phase=1/4, threshold=period-1
                (byte)TrueTypeInstructionOpcode.SetSuperRound,
                0xB8, 0x00, 0x11,
                (byte)TrueTypeInstructionOpcode.RoundGrayDistance,
                0xB8, 0xFF, 0xEF,
                (byte)TrueTypeInstructionOpcode.RoundGrayDistance,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(16, machineState.GraphicsState.SuperRoundingState.PhaseF26Dot6);
            Assert.Equal(63, machineState.GraphicsState.SuperRoundingState.ThresholdF26Dot6);
            Assert.Equal(new[] { 80, -80 }, Values(result));
        }

        [Fact]
        public void Super45RoundUsesSqrtHalfPixelGridPeriod()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0x48,
                (byte)TrueTypeInstructionOpcode.SetSuper45Round,
                0xB8, 0x00, 0x2C,
                (byte)TrueTypeInstructionOpcode.RoundGrayDistance,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(TrueTypeRoundingMode.Super45, machineState.GraphicsState.RoundingMode);
            Assert.Equal(45, machineState.GraphicsState.SuperRoundingState.PeriodF26Dot6);
            Assert.Equal(22, machineState.GraphicsState.SuperRoundingState.ThresholdF26Dot6);
            Assert.Equal(new[] { 45 }, Values(result));
        }

        [Fact]
        public void SuperRoundRejectsReservedPeriodSelector()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0xC8,
                (byte)TrueTypeInstructionOpcode.SetSuperRound,
            });

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidFunctionDefinition, result.Failure.FailureCode);
        }

        [Theory]
        [InlineData(0, 0, 512)]
        [InlineData(3, 0, 64)]
        [InlineData(6, 0, 8)]
        [InlineData(6, 15, 8)]
        public void DeltaControlValueUsesExactLegalDeltaShiftRange(int deltaShift, int encodedStepNibble,
            int expectedMagnitudeF26Dot6)
        {
            int packedDeltaArgument = 0x70 | encodedStepNibble; // 7 + delta_base 9 = 16ppem
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests(4, 100);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, (byte)deltaShift,
                (byte)TrueTypeInstructionOpcode.SetDeltaShift,
                0xB2, (byte)packedDeltaArgument, 0, 1, // argument, CVT index, count
                (byte)TrueTypeInstructionOpcode.DeltaControlValueOne,
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.ReadControlValue,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            int expectedSign = encodedStepNibble < 8 ? -1 : 1;
            Assert.Equal(new[] { 100 + expectedSign * expectedMagnitudeF26Dot6 }, Values(result));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(7)]
        public void SetDeltaShiftRejectsValuesOutsideZeroThroughSix(int invalidDeltaShift)
        {
            byte[] pushInvalidValue = invalidDeltaShift < 0
                ? new byte[] { 0xB8, 0xFF, 0xFF }
                : new byte[] { 0xB0, (byte)invalidDeltaShift };
            var program = new byte[pushInvalidValue.Length + 1];
            System.Buffer.BlockCopy(pushInvalidValue, 0, program, 0, pushInvalidValue.Length);
            program[program.Length - 1] = (byte)TrueTypeInstructionOpcode.SetDeltaShift;

            TrueTypeVirtualMachineResult result = Interpreter().Execute(program);

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidGraphicsStateValue, result.Failure.FailureCode);
        }

        [Fact]
        public void DeltaControlValueConsumesNonMatchingPpemPairWithoutTouchingInvalidIndex()
        {
            TrueTypeVirtualMachineState machineState = TrueTypeVirtualMachineState.ForTests(1, 100);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB2, 0x08, 99, 1, // target 9ppem, invalid CVT index, count
                (byte)TrueTypeInstructionOpcode.DeltaControlValueOne,
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.ReadControlValue,
            }, machineState);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 100 }, Values(result));
        }

        [Fact]
        public void DuplicateDefinitionsFailDeterministically()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.FunctionDefinition,
                (byte)TrueTypeInstructionOpcode.EndFunction,
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.FunctionDefinition,
                (byte)TrueTypeInstructionOpcode.EndFunction,
            });

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.DuplicateFunctionDefinition, result.Failure.FailureCode);
        }

        private static TrueTypeInstructionInterpreter Interpreter()
            => new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());

        private static int[] Values(TrueTypeVirtualMachineResult result)
        {
            var values = new int[result.FinalOperandStack.Length];
            for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
                values[valueIndex] = result.FinalOperandStack[valueIndex].Value;
            return values;
        }
    }
}