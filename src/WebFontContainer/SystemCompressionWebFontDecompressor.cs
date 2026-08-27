using System;
using System.IO;
using System.IO.Compression;

namespace StbTrueTypeSharp.WebFontContainer
{
    /// <summary>
    /// Bringup implementation of the decompression seam using System.IO.Compression
    /// (plans/woff_web_fonts.md: BCL for bringup, safe-managed swap later). This is
    /// the ONLY file in the WebFontContainer area that references the BCL codecs.
    /// zlib framing (RFC 1950 header + Adler-32 trailer) is parsed/verified here in
    /// safe code; only the raw-deflate body goes through DeflateStream so the class
    /// works on netstandard2.0 (which lacks ZLibStream).
    /// </summary>
    public sealed class SystemCompressionWebFontDecompressor : IWebFontDecompressor
    {
        public static readonly SystemCompressionWebFontDecompressor Shared = new SystemCompressionWebFontDecompressor();

        public bool TryInflateZlibFramedStream(byte[] sourceBytes, int sourceOffset, int sourceLength,
            byte[] destinationBytes, out WebFontContainerDecodeFailure inflateFailure)
        {
            // RFC 1950 framing: 2-byte header + deflate body + 4-byte Adler-32 trailer.
            const int ZlibHeaderByteCount = 2;
            const int Adler32TrailerByteCount = 4;
            if (sourceLength < ZlibHeaderByteCount + Adler32TrailerByteCount)
            {
                inflateFailure = new WebFontContainerDecodeFailure(
                    WebFontContainerDecodeFailureCode.ZlibHeaderInvalid,
                    $"zlib stream too short: {sourceLength} bytes");
                return false;
            }

            byte compressionMethodFlags = sourceBytes[sourceOffset];       // CMF
            byte flagsCheckBits = sourceBytes[sourceOffset + 1];           // FLG
            bool isDeflateMethod = (compressionMethodFlags & 0x0F) == 8;   // CM == 8
            bool headerCheckValid = ((compressionMethodFlags << 8) | flagsCheckBits) % 31 == 0;
            if (!isDeflateMethod || !headerCheckValid)
            {
                inflateFailure = new WebFontContainerDecodeFailure(
                    WebFontContainerDecodeFailureCode.ZlibHeaderInvalid,
                    $"CMF=0x{compressionMethodFlags:X2} FLG=0x{flagsCheckBits:X2}");
                return false;
            }
            if ((flagsCheckBits & 0x20) != 0) // FDICT: preset dictionary — never valid in WOFF
            {
                inflateFailure = new WebFontContainerDecodeFailure(
                    WebFontContainerDecodeFailureCode.ZlibDictionaryFlagUnsupported,
                    "FDICT preset-dictionary flag set");
                return false;
            }

            int deflateBodyOffset = sourceOffset + ZlibHeaderByteCount;
            int deflateBodyLength = sourceLength - ZlibHeaderByteCount - Adler32TrailerByteCount;
            int totalInflatedByteCount = 0;
            try
            {
                using (var deflateBodyStream = new MemoryStream(sourceBytes, deflateBodyOffset, deflateBodyLength, writable: false))
                using (var inflaterStream = new DeflateStream(deflateBodyStream, CompressionMode.Decompress))
                {
                    while (totalInflatedByteCount < destinationBytes.Length)
                    {
                        int bytesRead = inflaterStream.Read(destinationBytes, totalInflatedByteCount,
                            destinationBytes.Length - totalInflatedByteCount);
                        if (bytesRead <= 0)
                        {
                            break;
                        }
                        totalInflatedByteCount += bytesRead;
                    }

                    if (totalInflatedByteCount == destinationBytes.Length)
                    {
                        // Destination full — the stream must be EXACTLY finished now.
                        byte[] overflowProbe = new byte[1];
                        if (inflaterStream.Read(overflowProbe, 0, 1) != 0)
                        {
                            inflateFailure = new WebFontContainerDecodeFailure(
                                WebFontContainerDecodeFailureCode.ZlibInflatedLengthMismatch,
                                $"inflated data exceeds declared origLength {destinationBytes.Length}");
                            return false;
                        }
                    }
                }
            }
            catch (InvalidDataException invalidDataException)
            {
                inflateFailure = new WebFontContainerDecodeFailure(
                    WebFontContainerDecodeFailureCode.ZlibInflateTruncatedOrCorrupt,
                    invalidDataException.Message);
                return false;
            }

            if (totalInflatedByteCount != destinationBytes.Length)
            {
                inflateFailure = new WebFontContainerDecodeFailure(
                    WebFontContainerDecodeFailureCode.ZlibInflatedLengthMismatch,
                    $"inflated {totalInflatedByteCount} bytes, expected {destinationBytes.Length}");
                return false;
            }

            // Adler-32 trailer (big-endian) over the DECOMPRESSED bytes.
            uint declaredAdler32 = ReadBigEndianUInt32(sourceBytes, sourceOffset + sourceLength - Adler32TrailerByteCount);
            uint computedAdler32 = ComputeAdler32(destinationBytes);
            if (declaredAdler32 != computedAdler32)
            {
                inflateFailure = new WebFontContainerDecodeFailure(
                    WebFontContainerDecodeFailureCode.ZlibAdler32Mismatch,
                    $"declared 0x{declaredAdler32:X8}, computed 0x{computedAdler32:X8}");
                return false;
            }

            inflateFailure = WebFontContainerDecodeFailure.None;
            return true;
        }

        public bool TryDecodeBrotliStream(byte[] sourceBytes, int sourceOffset, int sourceLength,
            byte[] destinationBytes, out WebFontContainerDecodeFailure brotliFailure)
        {
#if NET
            var brotliDecoder = new System.IO.Compression.BrotliDecoder();
            try
            {
                var operationStatus = brotliDecoder.Decompress(
                    new ReadOnlySpan<byte>(sourceBytes, sourceOffset, sourceLength),
                    destinationBytes,
                    out int compressedBytesConsumed, out int decodedByteCount);
                if (operationStatus != System.Buffers.OperationStatus.Done
                    || compressedBytesConsumed != sourceLength)
                {
                    brotliFailure = new WebFontContainerDecodeFailure(
                        WebFontContainerDecodeFailureCode.Woff2BrotliStreamCorrupt,
                        $"status={operationStatus}, consumed {compressedBytesConsumed}/{sourceLength}");
                    return false;
                }
                if (decodedByteCount != destinationBytes.Length)
                {
                    brotliFailure = new WebFontContainerDecodeFailure(
                        WebFontContainerDecodeFailureCode.Woff2BrotliDecodedLengthMismatch,
                        $"decoded {decodedByteCount} bytes, expected {destinationBytes.Length}");
                    return false;
                }
            }
            finally
            {
                brotliDecoder.Dispose();
            }
            brotliFailure = WebFontContainerDecodeFailure.None;
            return true;
#else
            brotliFailure = new WebFontContainerDecodeFailure(
                WebFontContainerDecodeFailureCode.Woff2BrotliUnsupportedOnThisTargetFramework,
                "BrotliDecoder requires the net9.0 build of SafeStbTrueTypeSharp");
            return false;
#endif
        }

        /// <summary>RFC 1950 Adler-32 over the full buffer (safe scalar implementation).</summary>
        internal static uint ComputeAdler32(byte[] dataBytes)
        {
            const uint AdlerModulus = 65521;
            uint accumulatorA = 1, accumulatorB = 0;
            int index = 0;
            while (index < dataBytes.Length)
            {
                // Process in chunks small enough that B cannot overflow before the modulo.
                int chunkEnd = Math.Min(index + 5552, dataBytes.Length);
                for (; index < chunkEnd; index++)
                {
                    accumulatorA += dataBytes[index];
                    accumulatorB += accumulatorA;
                }
                accumulatorA %= AdlerModulus;
                accumulatorB %= AdlerModulus;
            }
            return (accumulatorB << 16) | accumulatorA;
        }

        internal static uint ReadBigEndianUInt32(byte[] sourceBytes, int offset)
            => ((uint)sourceBytes[offset] << 24)
             | ((uint)sourceBytes[offset + 1] << 16)
             | ((uint)sourceBytes[offset + 2] << 8)
             | sourceBytes[offset + 3];
    }
}