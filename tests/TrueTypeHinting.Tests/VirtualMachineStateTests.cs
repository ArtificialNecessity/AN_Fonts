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