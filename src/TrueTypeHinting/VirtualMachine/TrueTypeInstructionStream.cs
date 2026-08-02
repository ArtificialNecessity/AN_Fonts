namespace StbTrueTypeSharp.TrueTypeHinting.VirtualMachine
{
    /// <summary>Bounds-checked instruction reader with explicit relative-jump validation.</summary>
    internal sealed class TrueTypeInstructionStream
    {
        private readonly byte[] _trueTypeInstructionBytes;

        internal TrueTypeInstructionStream(byte[] trueTypeInstructionBytes)
            => _trueTypeInstructionBytes = trueTypeInstructionBytes == null
                ? new byte[0]
                : (byte[])trueTypeInstructionBytes.Clone();

        internal int InstructionBytePosition { get; private set; }
        internal int InstructionByteLength => _trueTypeInstructionBytes.Length;
        internal bool HasRemainingInstructionBytes => InstructionBytePosition < _trueTypeInstructionBytes.Length;

        internal bool TryReadByte(out byte trueTypeInstructionByte,
            out TrueTypeVirtualMachineFailure trueTypeInstructionFailure)
        {
            if (!HasRemainingInstructionBytes)
            {
                trueTypeInstructionByte = 0;
                trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.UnexpectedEndOfInstructionStream,
                    "The TrueType instruction stream ended unexpectedly.");
                return false;
            }
            trueTypeInstructionByte = _trueTypeInstructionBytes[InstructionBytePosition++];
            trueTypeInstructionFailure = default;
            return true;
        }

        internal bool TryReadSignedWord(out int trueTypeSignedWord,
            out TrueTypeVirtualMachineFailure trueTypeInstructionFailure)
        {
            if (!TryReadByte(out byte trueTypeHighByte, out trueTypeInstructionFailure) ||
                !TryReadByte(out byte trueTypeLowByte, out trueTypeInstructionFailure))
            {
                trueTypeSignedWord = 0;
                return false;
            }
            trueTypeSignedWord = unchecked((short)((trueTypeHighByte << 8) | trueTypeLowByte));
            return true;
        }

        internal bool TryReadDefinitionBody(out byte[] trueTypeDefinitionBody,
            out TrueTypeVirtualMachineFailure trueTypeInstructionFailure)
        {
            int definitionBodyStartBytePosition = InstructionBytePosition;
            while (HasRemainingInstructionBytes)
            {
                int opcodeBytePosition = InstructionBytePosition;
                if (!TryReadByte(out byte trueTypeOpcodeByte, out trueTypeInstructionFailure))
                {
                    trueTypeDefinitionBody = null;
                    return false;
                }
                if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.EndFunction)
                {
                    int definitionBodyByteLength = opcodeBytePosition - definitionBodyStartBytePosition;
                    trueTypeDefinitionBody = new byte[definitionBodyByteLength];
                    System.Buffer.BlockCopy(_trueTypeInstructionBytes, definitionBodyStartBytePosition,
                        trueTypeDefinitionBody, 0, definitionBodyByteLength);
                    trueTypeInstructionFailure = default;
                    return true;
                }
                if (!TrySkipPushPayload(trueTypeOpcodeByte, out trueTypeInstructionFailure))
                {
                    trueTypeDefinitionBody = null;
                    return false;
                }
                if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.FunctionDefinition ||
                    trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.InstructionDefinition)
                {
                    trueTypeDefinitionBody = null;
                    trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidFunctionDefinition,
                        "TrueType function and instruction definitions may not be nested.");
                    return false;
                }
            }

            trueTypeDefinitionBody = null;
            trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidFunctionDefinition,
                "The TrueType function or instruction definition has no ENDF terminator.");
            return false;
        }

        internal bool TryJumpRelativeFromCurrentPosition(int relativeInstructionByteOffset,
            out TrueTypeVirtualMachineFailure trueTypeInstructionFailure)
        {
            long targetInstructionBytePosition = (long)InstructionBytePosition + relativeInstructionByteOffset;
            if (targetInstructionBytePosition < 0 || targetInstructionBytePosition > _trueTypeInstructionBytes.Length)
            {
                trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidJumpTarget,
                    "The TrueType relative jump target lies outside the instruction stream.");
                return false;
            }
            InstructionBytePosition = (int)targetInstructionBytePosition;
            trueTypeInstructionFailure = default;
            return true;
        }

        internal bool TrySkipToConditionalBranch(bool seekElseBranch,
            out TrueTypeVirtualMachineFailure trueTypeInstructionFailure)
        {
            int nestedConditionalDepth = 0;
            while (TryReadByte(out byte trueTypeOpcodeByte, out trueTypeInstructionFailure))
            {
                if (!TrySkipPushPayload(trueTypeOpcodeByte, out trueTypeInstructionFailure))
                    return false;
                if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.If)
                    nestedConditionalDepth++;
                else if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.EndIf)
                {
                    if (nestedConditionalDepth == 0) return true;
                    nestedConditionalDepth--;
                }
                else if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.Else &&
                         nestedConditionalDepth == 0 && seekElseBranch)
                    return true;
            }
            trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidConditionalStructure,
                "The TrueType conditional block has no matching branch terminator.");
            return false;
        }

        private bool TrySkipPushPayload(byte trueTypeOpcodeByte,
            out TrueTypeVirtualMachineFailure trueTypeInstructionFailure)
        {
            int pushedByteCount;
            if (trueTypeOpcodeByte >= 0xB0 && trueTypeOpcodeByte <= 0xB7)
                pushedByteCount = trueTypeOpcodeByte - 0xAF;
            else if (trueTypeOpcodeByte >= 0xB8 && trueTypeOpcodeByte <= 0xBF)
                pushedByteCount = (trueTypeOpcodeByte - 0xB7) * 2;
            else if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.PushBytesVariable ||
                     trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.PushWordsVariable)
            {
                if (!TryReadByte(out byte pushedValueCount, out trueTypeInstructionFailure))
                    return false;
                pushedByteCount = pushedValueCount *
                    (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.PushWordsVariable ? 2 : 1);
            }
            else
            {
                trueTypeInstructionFailure = default;
                return true;
            }

            if (InstructionBytePosition > _trueTypeInstructionBytes.Length - pushedByteCount)
            {
                trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.UnexpectedEndOfInstructionStream,
                    "A TrueType PUSH payload extends beyond the instruction stream.");
                return false;
            }
            InstructionBytePosition += pushedByteCount;
            trueTypeInstructionFailure = default;
            return true;
        }

        internal static bool TryValidateConditionalStructure(byte[] trueTypeInstructionBytes,
            out TrueTypeVirtualMachineFailure trueTypeInstructionFailure)
        {
            var validationStream = new TrueTypeInstructionStream(trueTypeInstructionBytes);
            var conditionalElseSeenStack = new System.Collections.Generic.Stack<bool>();
            while (validationStream.HasRemainingInstructionBytes)
            {
                if (!validationStream.TryReadByte(out byte trueTypeOpcodeByte, out trueTypeInstructionFailure) ||
                    !validationStream.TrySkipPushPayload(trueTypeOpcodeByte, out trueTypeInstructionFailure))
                    return false;

                if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.If)
                {
                    conditionalElseSeenStack.Push(false);
                }
                else if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.Else)
                {
                    if (conditionalElseSeenStack.Count == 0 || conditionalElseSeenStack.Peek())
                    {
                        trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidConditionalStructure,
                            "The TrueType ELSE has no matching IF or duplicates an ELSE branch.");
                        return false;
                    }
                    conditionalElseSeenStack.Pop();
                    conditionalElseSeenStack.Push(true);
                }
                else if (trueTypeOpcodeByte == (byte)TrueTypeInstructionOpcode.EndIf)
                {
                    if (conditionalElseSeenStack.Count == 0)
                    {
                        trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidConditionalStructure,
                            "The TrueType EIF has no matching IF.");
                        return false;
                    }
                    conditionalElseSeenStack.Pop();
                }
            }

            if (conditionalElseSeenStack.Count != 0)
            {
                trueTypeInstructionFailure = Failure(TrueTypeVirtualMachineFailureCode.InvalidConditionalStructure,
                    "The TrueType instruction stream ends inside an IF block.");
                return false;
            }
            trueTypeInstructionFailure = default;
            return true;
        }

        private static TrueTypeVirtualMachineFailure Failure(TrueTypeVirtualMachineFailureCode failureCode, string failureMessage)
            => new TrueTypeVirtualMachineFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }
}