namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Bounds-checked forward reader over a byte buffer for WOFF2 parsing
    /// (W3C WOFF2 spec §5/§6.1: UIntBase128 and 255UInt16 variable-length integers,
    /// verified against 3P_woff2/src/variable_length.cc). All reads fail softly
    /// at end-of-buffer — the caller maps false to a failure code.
    /// </summary>
    internal struct Woff2StreamCursor
    {
        private readonly byte[] _streamBytes;
        private readonly int _streamEndOffset;   // exclusive
        public int CurrentOffset;

        public Woff2StreamCursor(byte[] streamBytes, int startOffset, int endOffsetExclusive)
        {
            _streamBytes = streamBytes;
            _streamEndOffset = endOffsetExclusive;
            CurrentOffset = startOffset;
        }

        public int RemainingByteCount => _streamEndOffset - CurrentOffset;

        public bool TryReadU8(out byte value)
        {
            if (CurrentOffset + 1 > _streamEndOffset) { value = 0; return false; }
            value = _streamBytes[CurrentOffset];
            CurrentOffset += 1;
            return true;
        }

        public bool TryReadU16(out ushort value)
        {
            if (CurrentOffset + 2 > _streamEndOffset) { value = 0; return false; }
            value = (ushort)((_streamBytes[CurrentOffset] << 8) | _streamBytes[CurrentOffset + 1]);
            CurrentOffset += 2;
            return true;
        }

        public bool TryReadS16(out short value)
        {
            bool readSucceeded = TryReadU16(out ushort unsignedValue);
            value = (short)unsignedValue;
            return readSucceeded;
        }

        public bool TryReadU32(out uint value)
        {
            if (CurrentOffset + 4 > _streamEndOffset) { value = 0; return false; }
            value = ((uint)_streamBytes[CurrentOffset] << 24)
                  | ((uint)_streamBytes[CurrentOffset + 1] << 16)
                  | ((uint)_streamBytes[CurrentOffset + 2] << 8)
                  | _streamBytes[CurrentOffset + 3];
            CurrentOffset += 4;
            return true;
        }

        /// <summary>
        /// UIntBase128 (WOFF2 §5.2): 1-5 bytes, 7 bits each, MSB-first, high bit =
        /// continuation. Leading 0x80 (redundant leading zero) and 32-bit overflow
        /// are invalid per spec.
        /// </summary>
        public bool TryReadUIntBase128(out uint value)
        {
            value = 0;
            uint accumulator = 0;
            for (int byteIndex = 0; byteIndex < 5; byteIndex++)
            {
                if (!TryReadU8(out byte codeByte)) return false;
                if (byteIndex == 0 && codeByte == 0x80) return false;      // leading zeros invalid
                if ((accumulator & 0xFE000000) != 0) return false;          // would overflow u32
                accumulator = (accumulator << 7) | (uint)(codeByte & 0x7F);
                if ((codeByte & 0x80) == 0)
                {
                    value = accumulator;
                    return true;
                }
            }
            return false; // more than 5 bytes
        }

        /// <summary>
        /// 255UInt16 (WOFF2 §5.1 / MicroType Express §6.1.1): code 253 = u16 word,
        /// 255 = next byte + 253, 254 = next byte + 506, else the code itself.
        /// </summary>
        public bool TryRead255UInt16(out int value)
        {
            value = 0;
            if (!TryReadU8(out byte codeByte)) return false;
            switch (codeByte)
            {
                case 253:
                    if (!TryReadU16(out ushort wordValue)) return false;
                    value = wordValue;
                    return true;
                case 255:
                    if (!TryReadU8(out byte plus253)) return false;
                    value = plus253 + 253;
                    return true;
                case 254:
                    if (!TryReadU8(out byte plus506)) return false;
                    value = plus506 + 506;
                    return true;
                default:
                    value = codeByte;
                    return true;
            }
        }
    }
}