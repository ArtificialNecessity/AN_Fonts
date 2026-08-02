using System.Collections.Generic;

namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    internal readonly struct TrueTypeFunctionIdentifier
    {
        internal TrueTypeFunctionIdentifier(int trueTypeFunctionIdentifierValue) => Value = trueTypeFunctionIdentifierValue;
        internal int Value { get; }
    }

    internal sealed class TrueTypeFunctionDefinitions
    {
        private readonly Dictionary<int, byte[]> _trueTypeFunctionBodies = new Dictionary<int, byte[]>();
        private readonly Dictionary<byte, byte[]> _trueTypeInstructionDefinitionBodies = new Dictionary<byte, byte[]>();

        internal bool TryDefineFunction(TrueTypeFunctionIdentifier trueTypeFunctionIdentifier, byte[] trueTypeFunctionBody,
            out TrueTypeVirtualMachineFailure trueTypeFunctionFailure)
        {
            if (_trueTypeFunctionBodies.ContainsKey(trueTypeFunctionIdentifier.Value))
            {
                trueTypeFunctionFailure = Failure(TrueTypeVirtualMachineFailureCode.DuplicateFunctionDefinition,
                    "The TrueType function identifier is already defined.");
                return false;
            }
            _trueTypeFunctionBodies.Add(trueTypeFunctionIdentifier.Value, (byte[])trueTypeFunctionBody.Clone());
            trueTypeFunctionFailure = default;
            return true;
        }

        internal bool TryGetFunction(TrueTypeFunctionIdentifier trueTypeFunctionIdentifier, out byte[] trueTypeFunctionBody,
            out TrueTypeVirtualMachineFailure trueTypeFunctionFailure)
        {
            if (!_trueTypeFunctionBodies.TryGetValue(trueTypeFunctionIdentifier.Value, out byte[] storedTrueTypeFunctionBody))
            {
                trueTypeFunctionBody = null;
                trueTypeFunctionFailure = Failure(TrueTypeVirtualMachineFailureCode.UndefinedFunction,
                    "The requested TrueType function identifier is undefined.");
                return false;
            }
            trueTypeFunctionBody = (byte[])storedTrueTypeFunctionBody.Clone();
            trueTypeFunctionFailure = default;
            return true;
        }

        internal bool TryDefineInstruction(byte trueTypeInstructionOpcodeByte, byte[] trueTypeInstructionDefinitionBody,
            out TrueTypeVirtualMachineFailure trueTypeInstructionDefinitionFailure)
        {
            if (_trueTypeInstructionDefinitionBodies.ContainsKey(trueTypeInstructionOpcodeByte))
            {
                trueTypeInstructionDefinitionFailure = Failure(TrueTypeVirtualMachineFailureCode.DuplicateInstructionDefinition,
                    "The TrueType instruction opcode already has an IDEF definition.");
                return false;
            }
            _trueTypeInstructionDefinitionBodies.Add(trueTypeInstructionOpcodeByte, (byte[])trueTypeInstructionDefinitionBody.Clone());
            trueTypeInstructionDefinitionFailure = default;
            return true;
        }

        internal bool TryGetInstruction(byte trueTypeInstructionOpcodeByte, out byte[] trueTypeInstructionDefinitionBody)
        {
            if (!_trueTypeInstructionDefinitionBodies.TryGetValue(trueTypeInstructionOpcodeByte, out byte[] storedInstructionDefinitionBody))
            {
                trueTypeInstructionDefinitionBody = null;
                return false;
            }
            trueTypeInstructionDefinitionBody = (byte[])storedInstructionDefinitionBody.Clone();
            return true;
        }

        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage)
            => new TrueTypeVirtualMachineFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}