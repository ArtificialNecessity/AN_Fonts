using System;
using System.IO;
using StbTrueTypeSharp.WebFontContainer;
using Xunit;

namespace WebFontContainerTests
{
    /// <summary>
    /// Hostile/malformed WOFF1 inputs must fail with the specific failure code and
    /// never throw (plans/woff_web_fonts.md hardening rules).
    /// </summary>
    public class Woff1MalformedInputTests
    {
        private static byte[] ReadValidWoffBytes()
            => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Roboto-Regular.generated.woff"));

        private static WebFontContainerDecodeFailure DecodeExpectingFailure(byte[] mutatedWoffBytes)
        {
            bool decodeSucceeded = WebFontContainerDecoder.TryDecodeToSfnt(mutatedWoffBytes,
                out _, out WebFontContainerDecodeFailure decodeFailure);
            Assert.False(decodeSucceeded);
            return decodeFailure;
        }

        [Theory]
        [InlineData(4)]
        [InlineData(43)]
        public void TruncatedHeaderFails(int truncatedByteCount)
        {
            byte[] truncatedBytes = new byte[truncatedByteCount];
            Array.Copy(ReadValidWoffBytes(), truncatedBytes, truncatedByteCount);
            Assert.Equal(WebFontContainerDecodeFailureCode.TruncatedContainerHeader,
                DecodeExpectingFailure(truncatedBytes).FailureCode);
        }

        [Fact]
        public void TruncatedBodyFailsWithoutThrowing()
        {
            byte[] validWoffBytes = ReadValidWoffBytes();
            // Cut the file in half — declared length no longer matches.
            byte[] truncatedBytes = new byte[validWoffBytes.Length / 2];
            Array.Copy(validWoffBytes, truncatedBytes, truncatedBytes.Length);
            Assert.Equal(WebFontContainerDecodeFailureCode.Woff1DeclaredLengthMismatch,
                DecodeExpectingFailure(truncatedBytes).FailureCode);
        }

        [Fact]
        public void NonZeroReservedFieldFails()
        {
            byte[] mutatedBytes = ReadValidWoffBytes();
            mutatedBytes[15] = 1; // reserved u16 at offset 14
            Assert.Equal(WebFontContainerDecodeFailureCode.Woff1ReservedFieldNonZero,
                DecodeExpectingFailure(mutatedBytes).FailureCode);
        }

        [Fact]
        public void OversizedTotalSfntSizeFailsAgainstCap()
        {
            byte[] mutatedBytes = ReadValidWoffBytes();
            // totalSfntSize u32 at offset 16 → 0x40000000 (1 GiB), over the 30 MiB cap.
            mutatedBytes[16] = 0x40; mutatedBytes[17] = 0; mutatedBytes[18] = 0; mutatedBytes[19] = 0;
            Assert.Equal(WebFontContainerDecodeFailureCode.ReconstructedSfntSizeExceedsCap,
                DecodeExpectingFailure(mutatedBytes).FailureCode);
        }

        [Fact]
        public void TableOffsetPastEndOfFileFails()
        {
            byte[] mutatedBytes = ReadValidWoffBytes();
            // First directory entry's offset field (header 44 + tag 4 = offset 48) → huge.
            mutatedBytes[48] = 0x7F; mutatedBytes[49] = 0xFF; mutatedBytes[50] = 0xFF; mutatedBytes[51] = 0xFF;
            Assert.Equal(WebFontContainerDecodeFailureCode.Woff1TableOffsetOutOfBounds,
                DecodeExpectingFailure(mutatedBytes).FailureCode);
        }

        [Fact]
        public void CorruptedZlibStreamFailsWithZlibCode()
        {
            byte[] validWoffBytes = ReadValidWoffBytes();
            // Find the first COMPRESSED table (compLength < origLength) and corrupt its body middle.
            int tableCount = (validWoffBytes[12] << 8) | validWoffBytes[13];
            for (int tableIndex = 0; tableIndex < tableCount; tableIndex++)
            {
                int entryOffset = 44 + tableIndex * 20;
                uint dataOffset = ReadU32(validWoffBytes, entryOffset + 4);
                uint compressedByteCount = ReadU32(validWoffBytes, entryOffset + 8);
                uint originalByteCount = ReadU32(validWoffBytes, entryOffset + 12);
                if (compressedByteCount < originalByteCount && compressedByteCount > 64)
                {
                    byte[] mutatedBytes = (byte[])validWoffBytes.Clone();
                    int corruptionOffset = (int)(dataOffset + compressedByteCount / 2);
                    mutatedBytes[corruptionOffset] ^= 0xFF;
                    mutatedBytes[corruptionOffset + 1] ^= 0xFF;
                    WebFontContainerDecodeFailure decodeFailure = DecodeExpectingFailure(mutatedBytes);
                    Assert.True(
                        decodeFailure.FailureCode == WebFontContainerDecodeFailureCode.ZlibInflateTruncatedOrCorrupt
                        || decodeFailure.FailureCode == WebFontContainerDecodeFailureCode.ZlibInflatedLengthMismatch
                        || decodeFailure.FailureCode == WebFontContainerDecodeFailureCode.ZlibAdler32Mismatch,
                        $"unexpected failure: {decodeFailure}");
                    return;
                }
            }
            Assert.Fail("test artifact has no compressed table large enough to corrupt");
        }

        private static uint ReadU32(byte[] bytes, int offset)
            => ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    }
}