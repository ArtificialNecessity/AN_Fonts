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
            TrueTypeTableTag horizontalHeaderTableTag = TrueTypeTableTag.FromAscii("hhea");
            TrueTypeTableTag horizontalMetricsTableTag = TrueTypeTableTag.FromAscii("hmtx");

            if (!trueTypeTableReader.TryCopyRequiredTable(headTableTag, 54,
                    out TrueTypeTableBytes headTableBytes, out trueTypeHintingFontFaceFailure) ||
                !trueTypeTableReader.TryCopyRequiredTable(maximumProfileTableTag, 32,
                    out TrueTypeTableBytes maximumProfileTableBytes, out trueTypeHintingFontFaceFailure) ||
                !trueTypeTableReader.TryCopyRequiredTable(glyphDataTableTag, 0,
                    out TrueTypeTableBytes glyphDataTableBytes, out trueTypeHintingFontFaceFailure) ||
                !trueTypeTableReader.TryCopyRequiredTable(glyphLocationTableTag, 2,
                    out TrueTypeTableBytes glyphLocationTableBytes, out trueTypeHintingFontFaceFailure) ||
                !trueTypeTableReader.TryCopyRequiredTable(horizontalHeaderTableTag, 36,
                    out TrueTypeTableBytes horizontalHeaderTableBytes, out trueTypeHintingFontFaceFailure) ||
                !trueTypeTableReader.TryCopyRequiredTable(horizontalMetricsTableTag, 4,
                    out TrueTypeTableBytes horizontalMetricsTableBytes, out trueTypeHintingFontFaceFailure))
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

            byte[] horizontalHeaderBytes = horizontalHeaderTableBytes.CloneBytes();
            int horizontalLongMetricCount = TrueTypeHintingTableReader.ReadUInt16(horizontalHeaderBytes, 34);
            if (!ValidateMetricTable(horizontalMetricsTableBytes, horizontalLongMetricCount, trueTypeGlyphCountValue,
                    "horizontal", out trueTypeHintingFontFaceFailure))
                return false;

            TrueTypeTableBytes verticalHeaderTableBytes = trueTypeTableReader.CopyOptionalTable(TrueTypeTableTag.FromAscii("vhea"));
            TrueTypeTableBytes verticalMetricsTableBytes = trueTypeTableReader.CopyOptionalTable(TrueTypeTableTag.FromAscii("vmtx"));
            if ((verticalHeaderTableBytes.ByteLength == 0) != (verticalMetricsTableBytes.ByteLength == 0))
            {
                trueTypeHintingFontFaceFailure = Failure(TrueTypeHintingFailureCode.MissingRequiredTable,
                    "TrueType vertical phantom metrics require both 'vhea' and 'vmtx' when either table is present.");
                return false;
            }
            int verticalLongMetricCount = 0;
            if (verticalHeaderTableBytes.ByteLength > 0)
            {
                if (verticalHeaderTableBytes.ByteLength < 36)
                {
                    trueTypeHintingFontFaceFailure = Failure(TrueTypeHintingFailureCode.TruncatedTable,
                        "The vhea table is shorter than 36 bytes.");
                    return false;
                }
                verticalLongMetricCount = TrueTypeHintingTableReader.ReadUInt16(verticalHeaderTableBytes.CloneBytes(), 34);
                if (!ValidateMetricTable(verticalMetricsTableBytes, verticalLongMetricCount, trueTypeGlyphCountValue,
                        "vertical", out trueTypeHintingFontFaceFailure))
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

            int defaultAscenderFontUnits = TrueTypeHintingTableReader.ReadInt16(horizontalHeaderBytes, 4);
            int defaultDescenderFontUnits = TrueTypeHintingTableReader.ReadInt16(horizontalHeaderBytes, 6);
            TrueTypeTableBytes operatingSystemMetricsTableBytes =
                trueTypeTableReader.CopyOptionalTable(TrueTypeTableTag.FromAscii("OS/2"));
            if (operatingSystemMetricsTableBytes.ByteLength >= 72)
            {
                byte[] operatingSystemMetricsBytes = operatingSystemMetricsTableBytes.CloneBytes();
                defaultAscenderFontUnits = TrueTypeHintingTableReader.ReadInt16(operatingSystemMetricsBytes, 68);
                defaultDescenderFontUnits = TrueTypeHintingTableReader.ReadInt16(operatingSystemMetricsBytes, 70);
            }
            var glyphMetricSource = new TrueTypeHintingGlyphMetricSource(
                horizontalMetricsTableBytes.CloneBytes(), horizontalLongMetricCount,
                verticalMetricsTableBytes.CloneBytes(), verticalLongMetricCount,
                defaultAscenderFontUnits, defaultDescenderFontUnits);

            trueTypeHintingFontFace = new TrueTypeHintingFontFace(trueTypeFaceIndex,
                new TrueTypeUnitsPerEm(trueTypeUnitsPerEmValue), new TrueTypeGlyphCount(trueTypeGlyphCountValue),
                maximumProfile, fontProgram, glyphMetricSource, glyphDataTableBytes.CloneBytes(),
                glyphLocationTableBytes.CloneBytes(), glyphLocationFormat,
                trueTypeTableReader.CopyOptionalTable(TrueTypeTableTag.FromAscii("gasp")));
            trueTypeHintingFontFaceFailure = default;
            return true;
        }

        private static bool ValidateMetricTable(TrueTypeTableBytes metricTableBytes, int longMetricCount,
            int glyphCount, string metricDirection, out TrueTypeYHintingFailure metricTableFailure)
        {
            if (longMetricCount <= 0 || longMetricCount > glyphCount)
            {
                metricTableFailure = Failure(TrueTypeHintingFailureCode.InvalidSfntDirectory,
                    "The " + metricDirection + " long-metric count is outside the glyph count.");
                return false;
            }
            long requiredMetricTableByteLength = 4L * longMetricCount + 2L * (glyphCount - longMetricCount);
            if (metricTableBytes.ByteLength < requiredMetricTableByteLength)
            {
                metricTableFailure = Failure(TrueTypeHintingFailureCode.TruncatedTable,
                    "The " + metricDirection + " metric table is too short for the glyph count.");
                return false;
            }
            metricTableFailure = default;
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

        /// <summary>Parses and executes one simple TrueType glyph using a prepared ppem instance.</summary>
        public TrueTypeYHintingResult HintGlyph(TrueTypeHintingSizeInstance trueTypeHintingSizeInstance,
            TrueTypeGlyphIndex trueTypeGlyphIndex)
        {
            if (trueTypeHintingSizeInstance == null) throw new ArgumentNullException(nameof(trueTypeHintingSizeInstance));
            if (!Geometry.TrueTypeSimpleGlyphParser.TryParse(trueTypeHintingSizeInstance.TrueTypeHintingFontFace,
                    trueTypeHintingSizeInstance.DevicePpemY, trueTypeGlyphIndex,
                    out Geometry.TrueTypeHintingGlyphInput trueTypeHintingGlyphInput,
                    out TrueTypeYHintingFailure trueTypeGlyphParsingFailure))
                return TrueTypeYHintingResult.Failed(trueTypeGlyphParsingFailure);

            VirtualMachine.TrueTypeVirtualMachineState glyphVirtualMachineState =
                trueTypeHintingSizeInstance.CreateGlyphExecutionState();
            Geometry.TrueTypeHintingExecutionZones executionZones = Geometry.TrueTypeHintingExecutionZones.Create(
                trueTypeHintingGlyphInput.GlyphZone,
                trueTypeHintingSizeInstance.TrueTypeHintingFontFace.MaximumProfile.MaximumTwilightPointCount.Value);
            var interpreter = new VirtualMachine.TrueTypeInstructionInterpreter(
                VirtualMachine.TrueTypeExecutionLimits.FromMaximumProfile(
                    trueTypeHintingSizeInstance.TrueTypeHintingFontFace.MaximumProfile));
            VirtualMachine.TrueTypeVirtualMachineResult glyphExecutionResult = interpreter.Execute(
                trueTypeHintingGlyphInput.GlyphInstructionBytes.ToByteArray(), glyphVirtualMachineState, executionZones);
            if (!glyphExecutionResult.Succeeded)
                return TrueTypeYHintingResult.Failed(Failure(TrueTypeHintingFailureCode.InterpreterNotImplemented,
                    "Glyph instruction execution failed: " + glyphExecutionResult.Failure));
            return TrueTypeYHintingResult.Successful(executionZones.GlyphZone.ClonePointsForResult());
        }

        private static TrueTypeYHintingFailure Failure(TrueTypeHintingFailureCode failureCode, string failureMessage)
            => new TrueTypeYHintingFailure(failureCode, new TrueTypeHintingFailureMessage(failureMessage));
    }

    public sealed class TrueTypeYHintingResult
    {
        private TrueTypeYHintingResult(bool trueTypeYHintingSucceeded, TrueTypeYHintingFailure trueTypeYHintingFailure,
            Geometry.TrueTypeHintingPoint[] trueTypeHintedPoints)
        {
            Succeeded = trueTypeYHintingSucceeded;
            Failure = trueTypeYHintingFailure;
            TrueTypeHintedPoints = trueTypeHintedPoints ?? new Geometry.TrueTypeHintingPoint[0];
        }

        public bool Succeeded { get; }
        public TrueTypeYHintingFailure Failure { get; }
        internal Geometry.TrueTypeHintingPoint[] TrueTypeHintedPoints { get; }
        internal static TrueTypeYHintingResult Failed(TrueTypeYHintingFailure failure)
            => new TrueTypeYHintingResult(false, failure, null);
        internal static TrueTypeYHintingResult Successful(Geometry.TrueTypeHintingPoint[] trueTypeHintedPoints)
            => new TrueTypeYHintingResult(true, default, trueTypeHintedPoints);
    }
}