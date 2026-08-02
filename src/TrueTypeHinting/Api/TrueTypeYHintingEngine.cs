using System;
using System.Collections.Generic;
using StbTrueTypeSharp.TrueTypeHinting.FontFace;
using StbTrueTypeSharp.TrueTypeHinting.SizeInstance;

namespace StbTrueTypeSharp.TrueTypeHinting
{
    /// <summary>Public entry point for the isolated TrueType Y-hinting subsystem.</summary>
    public sealed class TrueTypeYHintingEngine
    {
        private readonly Dictionary<TrueTypeHintingFontFace, TrueTypeHintingFaceRuntime> _trueTypeHintingFaceRuntimes =
            new Dictionary<TrueTypeHintingFontFace, TrueTypeHintingFaceRuntime>();

        /// <summary>Creates an immutable, bounds-validated font-program snapshot.</summary>
        public bool TryCreateFontFace(byte[] trueTypeFontFileBytes, TrueTypeFaceIndex trueTypeFaceIndex,
            out TrueTypeHintingFontFace trueTypeHintingFontFace,
            out TrueTypeYHintingFailure trueTypeHintingFontFaceFailure)
        {
            trueTypeHintingFontFace = null;
            if (!TrueTypeHintingTableReader.TryCreate(trueTypeFontFileBytes, trueTypeFaceIndex,
                    out TrueTypeHintingTableReader trueTypeTableReader, out trueTypeHintingFontFaceFailure))
                return false;

            TrueTypeTableTag headTableTag = TrueTypeTableTag.FromAscii("head");
            TrueTypeTableTag maximumProfileTableTag = TrueTypeTableTag.FromAscii("maxp");
            TrueTypeTableTag glyphDataTableTag = TrueTypeTableTag.FromAscii("glyf");
            TrueTypeTableTag glyphLocationTableTag = TrueTypeTableTag.FromAscii("loca");

            if (!trueTypeTableReader.TryCopyRequiredTable(headTableTag, 54,
                    out TrueTypeTableBytes headTableBytes, out trueTypeHintingFontFaceFailure) ||
                !trueTypeTableReader.TryCopyRequiredTable(maximumProfileTableTag, 32,
                    out TrueTypeTableBytes maximumProfileTableBytes, out trueTypeHintingFontFaceFailure) ||
                !trueTypeTableReader.TryCopyRequiredTable(glyphDataTableTag, 0,
                    out TrueTypeTableBytes glyphDataTableBytes, out trueTypeHintingFontFaceFailure) ||
                !trueTypeTableReader.TryCopyRequiredTable(glyphLocationTableTag, 2,
                    out TrueTypeTableBytes glyphLocationTableBytes, out trueTypeHintingFontFaceFailure))
                return false;

            byte[] headBytes = headTableBytes.CloneBytes();
            byte[] maximumProfileBytes = maximumProfileTableBytes.CloneBytes();
            uint maximumProfileVersion = TrueTypeHintingTableReader.ReadUInt32(maximumProfileBytes, 0);
            if (maximumProfileVersion != 0x00010000)
            {
                trueTypeHintingFontFaceFailure = Failure(TrueTypeHintingFailureCode.UnsupportedMaxpVersion,
                    "TrueType hinting requires a maxp version-1 table.");
                return false;
            }

            ushort trueTypeUnitsPerEmValue = TrueTypeHintingTableReader.ReadUInt16(headBytes, 18);
            short glyphLocationFormat = TrueTypeHintingTableReader.ReadInt16(headBytes, 50);
            if (trueTypeUnitsPerEmValue == 0 || (glyphLocationFormat != 0 && glyphLocationFormat != 1))
            {
                trueTypeHintingFontFaceFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory,
                    "The head table contains invalid unitsPerEm or indexToLocFormat values.");
                return false;
            }

            ushort trueTypeGlyphCountValue = TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 4);
            long requiredGlyphLocationTableByteLength = (long)(trueTypeGlyphCountValue + 1) * (glyphLocationFormat == 0 ? 2 : 4);
            if (glyphLocationTableBytes.ByteLength < requiredGlyphLocationTableByteLength)
            {
                trueTypeHintingFontFaceFailure = Failure(TrueTypeHintingFailureCode.TruncatedTable,
                    "The loca table is too short for the maxp glyph count.");
                return false;
            }

            var maximumProfile = new TrueTypeHintingMaximumProfile(
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 6),
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 8),
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 14),
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 16),
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 18),
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 20),
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 22),
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 24),
                TrueTypeHintingTableReader.ReadUInt16(maximumProfileBytes, 26));

            var fontProgram = new TrueTypeHintingFontProgram(
                trueTypeTableReader.CopyOptionalTable(TrueTypeTableTag.FromAscii("cvt ")),
                trueTypeTableReader.CopyOptionalTable(TrueTypeTableTag.FromAscii("fpgm")),
                trueTypeTableReader.CopyOptionalTable(TrueTypeTableTag.FromAscii("prep")));

            trueTypeHintingFontFace = new TrueTypeHintingFontFace(trueTypeFaceIndex,
                new TrueTypeUnitsPerEm(trueTypeUnitsPerEmValue), new TrueTypeGlyphCount(trueTypeGlyphCountValue),
                maximumProfile, fontProgram, glyphDataTableBytes.CloneBytes(),
                glyphLocationTableBytes.CloneBytes(), glyphLocationFormat,
                trueTypeTableReader.CopyOptionalTable(TrueTypeTableTag.FromAscii("gasp")));
            trueTypeHintingFontFaceFailure = default;
            return true;
        }

        /// <summary>
        /// Returns one reusable face runtime. The font program executes exactly once per
        /// face owned by this engine; the runtime caches prepared size instances by ppem.
        /// </summary>
        public bool TryGetOrCreateFaceRuntime(TrueTypeHintingFontFace trueTypeHintingFontFace,
            out TrueTypeHintingFaceRuntime trueTypeHintingFaceRuntime,
            out TrueTypeYHintingFailure trueTypeHintingFaceRuntimeFailure)
        {
            if (trueTypeHintingFontFace == null) throw new ArgumentNullException(nameof(trueTypeHintingFontFace));
            if (_trueTypeHintingFaceRuntimes.TryGetValue(trueTypeHintingFontFace, out trueTypeHintingFaceRuntime))
            {
                trueTypeHintingFaceRuntimeFailure = default;
                return true;
            }
            if (!TrueTypeHintingFaceRuntime.TryCreate(trueTypeHintingFontFace,
                    out trueTypeHintingFaceRuntime, out trueTypeHintingFaceRuntimeFailure))
                return false;
            _trueTypeHintingFaceRuntimes.Add(trueTypeHintingFontFace, trueTypeHintingFaceRuntime);
            return true;
        }

        /// <summary>Returns a cached ppem instance after scaling the CVT and executing prep.</summary>
        public TrueTypeHintingSizeInstanceResult CreateSizeInstance(
            TrueTypeHintingFontFace trueTypeHintingFontFace, DevicePpemY devicePpemY)
            => TryGetOrCreateFaceRuntime(trueTypeHintingFontFace, out TrueTypeHintingFaceRuntime trueTypeHintingFaceRuntime,
                    out TrueTypeYHintingFailure trueTypeHintingFaceRuntimeFailure)
                ? trueTypeHintingFaceRuntime.GetOrCreateSizeInstance(devicePpemY)
                : TrueTypeHintingSizeInstanceResult.Failed(trueTypeHintingFaceRuntimeFailure);

        /// <summary>Placeholder until VM phases land; fails explicitly rather than silently ignoring instructions.</summary>
        public TrueTypeYHintingResult HintGlyph(TrueTypeHintingFontFace trueTypeHintingFontFace,
            DevicePpemY devicePpemY, TrueTypeGlyphIndex trueTypeGlyphIndex)
        {
            if (trueTypeHintingFontFace == null) throw new ArgumentNullException(nameof(trueTypeHintingFontFace));
            _ = devicePpemY;
            _ = trueTypeGlyphIndex;
            return TrueTypeYHintingResult.Failed(Failure(TrueTypeHintingFailureCode.InterpreterNotImplemented,
                "The TrueType instruction interpreter has not been implemented yet."));
        }

        private static TrueTypeYHintingFailure Failure(TrueTypeHintingFailureCode failureCode, string failureMessage)
            => new TrueTypeYHintingFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }

    public sealed class TrueTypeYHintingResult
    {
        private TrueTypeYHintingResult(bool trueTypeYHintingSucceeded, TrueTypeYHintingFailure trueTypeYHintingFailure)
        {
            Succeeded = trueTypeYHintingSucceeded;
            Failure = trueTypeYHintingFailure;
        }

        public bool Succeeded { get; }
        public TrueTypeYHintingFailure Failure { get; }
        internal static TrueTypeYHintingResult Failed(TrueTypeYHintingFailure failure)
            => new TrueTypeYHintingResult(false, failure);
    }
}