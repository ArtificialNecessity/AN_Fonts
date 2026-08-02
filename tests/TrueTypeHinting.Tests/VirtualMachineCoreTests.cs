using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;
using Xunit;

namespace TrueTypeHinting.Tests
{
    public sealed class VirtualMachineCoreTests
    {
        [Fact]
        public void FixedAndVariablePushesPreserveSignedOperands()
        {
            var interpreter = Interpreter();
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB2, 7, 8, 9,
                (byte)TrueTypeInstructionOpcode.PushWordsVariable, 2, 0xFF, 0xFE, 0x01, 0x00,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 7, 8, 9, -2, 256 }, Values(result));
        }

        [Fact]
        public void StackAndArithmeticOpcodesProduceExpectedValues()
        {
            var interpreter = Interpreter();
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB1, 10, 3,
                (byte)TrueTypeInstructionOpcode.Subtract,
                (byte)TrueTypeInstructionOpcode.Duplicate,
                0xB0, 5,
                (byte)TrueTypeInstructionOpcode.Add,
                (byte)TrueTypeInstructionOpcode.Swap,
                (byte)TrueTypeInstructionOpcode.Depth,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 12, 7, 2 }, Values(result));
        }

        [Fact]
        public void OddAndEvenApplyCurrentRoundStateBeforeParityTest()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                (byte)TrueTypeInstructionOpcode.RoundToGrid,
                0xB8, 0x00, 0x5F, // 95/64px rounds to 1px: odd
                (byte)TrueTypeInstructionOpcode.Odd,
                0xB8, 0x00, 0x61, // 97/64px rounds to 2px: even
                (byte)TrueTypeInstructionOpcode.Even,
                (byte)TrueTypeInstructionOpcode.RoundToDoubleGrid,
                0xB8, 0x00, 0x30, // 48/64px rounds to 1px under double-grid
                (byte)TrueTypeInstructionOpcode.Odd,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 1, 1, 1 }, Values(result));
        }

        [Fact]
        public void OddAndEvenHandleNegativeRoundedValuesByIntegerParity()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB8, 0xFF, 0xA1, // -95/64px rounds to -1px
                (byte)TrueTypeInstructionOpcode.Odd,
                0xB8, 0xFF, 0x9F, // -97/64px rounds to -2px
                (byte)TrueTypeInstructionOpcode.Even,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 1, 1 }, Values(result));
        }

        [Fact]
        public void DebugConsumesItsOperandWithoutChangingExecution()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 7, 99,
                (byte)TrueTypeInstructionOpcode.Debug,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 7 }, Values(result));
        }

        [Fact]
        public void DebugWithoutOperandFailsWithStackUnderflow()
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                (byte)TrueTypeInstructionOpcode.Debug,
            });

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.OperandStackUnderflow, result.Failure.FailureCode);
        }

        [Fact]
        public void FalseIfExecutesElseBranchAndSkipsNestedPushPayloads()
        {
            var interpreter = Interpreter();
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.If,
                    0xB0, 99,
                    (byte)TrueTypeInstructionOpcode.If, 0xB0, 77, (byte)TrueTypeInstructionOpcode.EndIf,
                (byte)TrueTypeInstructionOpcode.Else,
                    0xB0, 42,
                (byte)TrueTypeInstructionOpcode.EndIf,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 42 }, Values(result));
        }

        [Fact]
        public void TrueIfSkipsElseBranch()
        {
            var interpreter = Interpreter();
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.If,
                    0xB0, 12,
                (byte)TrueTypeInstructionOpcode.Else,
                    0xB0, 99,
                (byte)TrueTypeInstructionOpcode.EndIf,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 12 }, Values(result));
        }

        [Fact]
        public void RelativeJumpSkipsInstructionBytes()
        {
            var interpreter = Interpreter();
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB0, 2,
                (byte)TrueTypeInstructionOpcode.JumpRelative,
                0xB0, 99,
                0xB0, 7,
            });

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(new[] { 7 }, Values(result));
        }

        [Fact]
        public void BackwardJumpStopsAtInstructionBudget()
        {
            var interpreter = new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests(instructionExecutionBudgetValue: 8));
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[]
            {
                0xB8, 0xFF, 0xFC, // signed word -4: jump from after JMPR back to byte zero
                (byte)TrueTypeInstructionOpcode.JumpRelative,
            });

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InstructionExecutionBudgetExceeded, result.Failure.FailureCode);
        }

        [Fact]
        public void StackOverflowFailsWithoutGrowingStorage()
        {
            var interpreter = new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests(operandStackCapacityValue: 2));
            TrueTypeVirtualMachineResult result = interpreter.Execute(new byte[] { 0xB2, 1, 2, 3 });

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.OperandStackOverflow, result.Failure.FailureCode);
        }

        [Theory]
        [InlineData(new byte[] { (byte)TrueTypeInstructionOpcode.Add }, (int)TrueTypeVirtualMachineFailureCode.OperandStackUnderflow)]
        [InlineData(new byte[] { 0xB1, 64, 0, (byte)TrueTypeInstructionOpcode.Divide }, (int)TrueTypeVirtualMachineFailureCode.DivisionByZero)]
        [InlineData(new byte[] { 0xB0, 127, (byte)TrueTypeInstructionOpcode.JumpRelative }, (int)TrueTypeVirtualMachineFailureCode.InvalidJumpTarget)]
        [InlineData(new byte[] { 0x83 }, (int)TrueTypeVirtualMachineFailureCode.UnsupportedOpcode)]
        public void InvalidProgramsReturnStructuredFailure(byte[] program, int expectedFailureCodeValue)
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(program);
            Assert.False(result.Succeeded);
            Assert.Equal((TrueTypeVirtualMachineFailureCode)expectedFailureCodeValue, result.Failure.FailureCode);
        }

        [Theory]
        [InlineData(new byte[] { (byte)TrueTypeInstructionOpcode.EndIf })]
        [InlineData(new byte[] { (byte)TrueTypeInstructionOpcode.Else })]
        [InlineData(new byte[] { 0xB0, 1, (byte)TrueTypeInstructionOpcode.If, 0xB0, 7 })]
        public void MalformedConditionalStructureReturnsStructuredFailure(byte[] program)
        {
            TrueTypeVirtualMachineResult result = Interpreter().Execute(program);
            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidConditionalStructure, result.Failure.FailureCode);
            Assert.Equal(0, result.ExecutedInstructionCount);
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