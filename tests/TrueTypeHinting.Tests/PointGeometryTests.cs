using StbTrueTypeSharp.TrueTypeHinting.Geometry;
using StbTrueTypeSharp.TrueTypeHinting.VirtualMachine;
using Xunit;

namespace TrueTypeHinting.Tests
{
    public sealed class PointGeometryTests
    {
        [Fact]
        public void MdapRoundsAndMovesOnlyAlongFreedomVector()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(25, 90, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.MoveDirectAbsolutePointWithRounding,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint point));
            Assert.Equal(25, point.CurrentHorizontalF26Dot6);
            Assert.Equal(64, point.CurrentVerticalF26Dot6);
            Assert.True(point.IsTouchedVertically);
            Assert.False(point.IsTouchedHorizontally);
            Assert.Equal(0, state.GraphicsState.ReferencePointZero.Value);
            Assert.Equal(0, state.GraphicsState.ReferencePointOne.Value);
        }

        [Theory]
        [InlineData((int)TrueTypeRoundingMode.ToGrid, 31, 0)]
        [InlineData((int)TrueTypeRoundingMode.ToGrid, 32, 64)]
        [InlineData((int)TrueTypeRoundingMode.ToGrid, -31, 0)]
        [InlineData((int)TrueTypeRoundingMode.ToGrid, -32, -64)]
        [InlineData((int)TrueTypeRoundingMode.ToHalfGrid, 0, 32)]
        [InlineData((int)TrueTypeRoundingMode.ToHalfGrid, 31, 32)]
        [InlineData((int)TrueTypeRoundingMode.ToHalfGrid, 32, 32)]
        [InlineData((int)TrueTypeRoundingMode.ToHalfGrid, 63, 32)]
        [InlineData((int)TrueTypeRoundingMode.ToHalfGrid, 64, 96)]
        [InlineData((int)TrueTypeRoundingMode.ToHalfGrid, -31, -32)]
        [InlineData((int)TrueTypeRoundingMode.ToHalfGrid, -64, -96)]
        [InlineData((int)TrueTypeRoundingMode.ToDoubleGrid, 15, 0)]
        [InlineData((int)TrueTypeRoundingMode.ToDoubleGrid, 16, 32)]
        [InlineData((int)TrueTypeRoundingMode.ToDoubleGrid, -15, 0)]
        [InlineData((int)TrueTypeRoundingMode.ToDoubleGrid, -16, -32)]
        [InlineData((int)TrueTypeRoundingMode.DownToGrid, 63, 0)]
        [InlineData((int)TrueTypeRoundingMode.DownToGrid, 64, 64)]
        [InlineData((int)TrueTypeRoundingMode.DownToGrid, -63, 0)]
        [InlineData((int)TrueTypeRoundingMode.DownToGrid, -64, -64)]
        [InlineData((int)TrueTypeRoundingMode.UpToGrid, 0, 0)]
        [InlineData((int)TrueTypeRoundingMode.UpToGrid, 1, 64)]
        [InlineData((int)TrueTypeRoundingMode.UpToGrid, 64, 64)]
        [InlineData((int)TrueTypeRoundingMode.UpToGrid, -1, -64)]
        [InlineData((int)TrueTypeRoundingMode.UpToGrid, -64, -64)]
        public void PredefinedRoundingModesHandleSignedBoundaryValues(
            int trueTypeRoundingModeValue, int inputF26Dot6, int expectedF26Dot6)
        {
            TrueTypeVirtualMachineState trueTypeVirtualMachineState = TrueTypeVirtualMachineState.ForTests();
            trueTypeVirtualMachineState.GraphicsState.RoundingMode =
                (TrueTypeRoundingMode)trueTypeRoundingModeValue;

            int roundedF26Dot6 = TrueTypeHintingGeometryOperations.RoundF26Dot6(
                inputF26Dot6, trueTypeVirtualMachineState.GraphicsState);

            Assert.Equal(expectedF26Dot6, roundedF26Dot6);
        }

        [Fact]
        public void RoundingOffPreservesSignedFractionalValueExactly()
        {
            TrueTypeVirtualMachineState trueTypeVirtualMachineState = TrueTypeVirtualMachineState.ForTests();
            trueTypeVirtualMachineState.GraphicsState.RoundingMode = TrueTypeRoundingMode.Off;

            Assert.Equal(-37, TrueTypeHintingGeometryOperations.RoundF26Dot6(
                -37, trueTypeVirtualMachineState.GraphicsState));
        }

        [Fact]
        public void MiapUsesCvtAndSetsReferencePoints()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests(4, 128);
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(0, 40, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 0, // point, CVT index
                (byte)TrueTypeInstructionOpcode.MoveIndirectAbsolutePointWithoutRounding,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint point));
            Assert.Equal(128, point.CurrentVerticalF26Dot6);
            Assert.Equal(0, state.GraphicsState.ReferencePointZero.Value);
            Assert.Equal(0, state.GraphicsState.ReferencePointOne.Value);
        }

        [Fact]
        public void MiapTwilightPointKeepsUnroundedCvtAsOriginalAndRoundedCurrentCoordinate()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests(4, 90);
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[] { new TrueTypeHintingPoint(0, 0, true) },
                    new[] { new TrueTypeContourEndPointIndex(0) }),
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]));

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 0,
                (byte)TrueTypeInstructionOpcode.MoveIndirectAbsolutePointWithRounding,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.TwilightZone.TryGetPoint(0, out TrueTypeHintingPoint twilightPoint));
            Assert.Equal(90, twilightPoint.OriginalVerticalF26Dot6);
            Assert.Equal(64, twilightPoint.CurrentVerticalF26Dot6);
            Assert.True(twilightPoint.IsTouchedVertically);
        }

        [Fact]
        public void MiapTwilightInitializationHonorsDistinctProjectionAndFreedomVectors()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests(4, 64);
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Horizontal;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Horizontal;
            state.GraphicsState.FreedomVector = new TrueTypeUnitVector(
                new TrueTypeVectorComponent(0x2D41), new TrueTypeVectorComponent(0x2D41));
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[] { new TrueTypeHintingPoint(0, 0, true) },
                    new[] { new TrueTypeContourEndPointIndex(0) }),
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]));

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 0,
                (byte)TrueTypeInstructionOpcode.MoveIndirectAbsolutePointWithoutRounding,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.TwilightZone.TryGetPoint(0, out TrueTypeHintingPoint twilightPoint));
            Assert.Equal(64, twilightPoint.OriginalHorizontalF26Dot6);
            Assert.Equal(64, twilightPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(64, twilightPoint.OriginalVerticalF26Dot6);
            Assert.Equal(64, twilightPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void MirpInitializesTwilightOriginalRelativeToReferenceBeforeRounding()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests(4, 90);
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(0);
            var referencePoint = new TrueTypeHintingPoint(0, 32, true);
            var targetPoint = new TrueTypeHintingPoint(0, 0, true);
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[] { referencePoint, targetPoint },
                    new[] { new TrueTypeContourEndPointIndex(1) }),
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]));

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                0xE4,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.TwilightZone.TryGetPoint(1, out TrueTypeHintingPoint twilightPoint));
            Assert.Equal(122, twilightPoint.OriginalVerticalF26Dot6);
            Assert.Equal(96, twilightPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void MsirpInitializesTwilightOriginalAndCurrentRelativeToReference()
        {
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(0);
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[]
                {
                    new TrueTypeHintingPoint(0, 32, true),
                    new TrueTypeHintingPoint(0, 0, true),
                }, new[] { new TrueTypeContourEndPointIndex(1) }),
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]));

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 70, // point, distance
                (byte)TrueTypeInstructionOpcode.MoveStackIndirectRelativePointKeepReference,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.TwilightZone.TryGetPoint(1, out TrueTypeHintingPoint twilightPoint));
            Assert.Equal(102, twilightPoint.OriginalVerticalF26Dot6);
            Assert.Equal(102, twilightPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void ScfsCopiesMovedTwilightCurrentCoordinateIntoOriginalCoordinate()
        {
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ZonePointerTwo = new TrueTypeZonePointerIndex(0);
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[] { new TrueTypeHintingPoint(0, 10, true) },
                    new[] { new TrueTypeContourEndPointIndex(0) }),
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]));

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 77, // point, coordinate
                (byte)TrueTypeInstructionOpcode.SetCoordinateFromStack,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.TwilightZone.TryGetPoint(0, out TrueTypeHintingPoint twilightPoint));
            Assert.Equal(77, twilightPoint.OriginalVerticalF26Dot6);
            Assert.Equal(77, twilightPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void MdrpAppliesSingleWidthCutInBeforeRounding()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.SingleWidthValueF26Dot6 = 80;
            state.GraphicsState.SingleWidthCutInF26Dot6 = 10;
            state.GraphicsState.RoundingMode = TrueTypeRoundingMode.Off;
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(0, 86, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                (byte)TrueTypeInstructionOpcode.SetVectorsToYAxis,
                0xB0, 1,
                0xC0, // MDRP: no rp0 update, minimum distance, or rounding
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(80, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void MdrpSingleWidthCutInPreservesOriginalDistanceSign()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.SingleWidthValueF26Dot6 = 80;
            state.GraphicsState.SingleWidthCutInF26Dot6 = 10;
            state.GraphicsState.RoundingMode = TrueTypeRoundingMode.Off;
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(0, -86, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                (byte)TrueTypeInstructionOpcode.SetVectorsToYAxis,
                0xB0, 1,
                0xC0,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(-80, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void MirpAppliesSingleWidthThenControlValueCutInThenRounding()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests(4, 84);
            state.GraphicsState.SingleWidthValueF26Dot6 = 80;
            state.GraphicsState.SingleWidthCutInF26Dot6 = 10;
            state.GraphicsState.ControlValueCutInF26Dot6 = 8;
            state.GraphicsState.RoundingMode = TrueTypeRoundingMode.Off;
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(0, 100, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                (byte)TrueTypeInstructionOpcode.SetVectorsToYAxis,
                0xB1, 1, 0, // point, CVT index
                0xE4, // MIRP with round/cut-in enabled
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint movedPoint));
            // CVT 84 first regularizes to single-width 80; cut-in against original 100 then selects 100.
            Assert.Equal(100, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void MirpSkipsControlValueCutInWhenReferenceAndPointZonesDiffer()
        {
            var twilightPoints = new[] { new TrueTypeHintingPoint(0, 0, true) };
            var glyphPoints = new[] { new TrueTypeHintingPoint(0, 100, true) };
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(twilightPoints, new[] { new TrueTypeContourEndPointIndex(0) }),
                new TrueTypeHintingZone(glyphPoints, new[] { new TrueTypeContourEndPointIndex(0) }));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests(4, 64);
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(1);
            state.GraphicsState.ControlValueCutInF26Dot6 = 1;
            state.GraphicsState.RoundingMode = TrueTypeRoundingMode.Off;
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 0,
                0xE4,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(64, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void MirpAutoFlipAppliesAfterSingleWidthRegularization()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests(4, 84);
            state.GraphicsState.SingleWidthValueF26Dot6 = 80;
            state.GraphicsState.SingleWidthCutInF26Dot6 = 10;
            state.GraphicsState.RoundingMode = TrueTypeRoundingMode.Off;
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(0, -100, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                (byte)TrueTypeInstructionOpcode.SetVectorsToYAxis,
                0xB1, 1, 0,
                0xE0, // no rounding/cut-in flag; auto-flip still applies
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(-80, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void OrthogonalProjectionAndFreedomVectorsFailInsteadOfMovingWrongAxis()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Horizontal;
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(0, 90, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.MoveDirectAbsolutePointWithRounding,
            }, state, zones);

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidFreedomProjectionVectors, result.Failure.FailureCode);
        }

        [Fact]
        public void IupInterpolatesAndShiftsWrappedUntouchedContourPointsExactly()
        {
            var beforeFirstTouchedPoint = new TrueTypeHintingPoint(0, -64, true);
            var firstTouchedPoint = new TrueTypeHintingPoint(0, 0, true)
                { CurrentVerticalF26Dot6 = 64, TouchFlags = TrueTypePointTouchFlags.Vertical };
            var betweenTouchedPoint = new TrueTypeHintingPoint(0, 64, true);
            var secondTouchedPoint = new TrueTypeHintingPoint(0, 128, true)
                { CurrentVerticalF26Dot6 = 256, TouchFlags = TrueTypePointTouchFlags.Vertical };
            var afterSecondTouchedPoint = new TrueTypeHintingPoint(0, 192, true);
            TrueTypeHintingExecutionZones zones = Zones(beforeFirstTouchedPoint, firstTouchedPoint,
                betweenTouchedPoint, secondTouchedPoint, afterSecondTouchedPoint);

            TrueTypeHintingGeometryOperations.InterpolateUntouchedPoints(zones.GlyphZone, verticalAxis: true);

            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint shiftedBeforePoint));
            Assert.True(zones.GlyphZone.TryGetPoint(2, out TrueTypeHintingPoint interpolatedPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(4, out TrueTypeHintingPoint shiftedAfterPoint));
            Assert.Equal(0, shiftedBeforePoint.CurrentVerticalF26Dot6);
            Assert.Equal(160, interpolatedPoint.CurrentVerticalF26Dot6);
            Assert.Equal(320, shiftedAfterPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IupLeavesContourWithoutTouchedPointsUnchanged()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(0, 64, true));

            TrueTypeHintingGeometryOperations.InterpolateUntouchedPoints(zones.GlyphZone, verticalAxis: true);

            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint firstPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint secondPoint));
            Assert.Equal(0, firstPoint.CurrentVerticalF26Dot6);
            Assert.Equal(64, secondPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void DeltaPointAppliesOnlyAtTargetPpem()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests(); // 16ppem, delta base 9
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(0, 0, true));
            // High nibble 7 + delta base 9 = 16ppem; low nibble 8 maps to +1 delta step.
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB2, 0x78, 0, 1,
                (byte)TrueTypeInstructionOpcode.DeltaPointOne,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint point));
            Assert.Equal(8, point.CurrentVerticalF26Dot6); // default delta_shift=3 => 1/8px
        }

        [Fact]
        public void GetOriginalCoordinateUsesDualProjectionVector()
        {
            var point = new TrueTypeHintingPoint(64, 128, true)
            {
                CurrentHorizontalF26Dot6 = 256,
                CurrentVerticalF26Dot6 = 512,
            };
            TrueTypeHintingExecutionZones zones = Zones(point);
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Horizontal;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.GetOriginalCoordinate,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Single(result.FinalOperandStack);
            Assert.Equal(128, result.FinalOperandStack[0].Value);
        }

        [Fact]
        public void GetCurrentCoordinateUsesProjectionVector()
        {
            var point = new TrueTypeHintingPoint(64, 128, true)
            {
                CurrentHorizontalF26Dot6 = 256,
                CurrentVerticalF26Dot6 = 512,
            };
            TrueTypeHintingExecutionZones zones = Zones(point);
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Horizontal;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.GetCurrentCoordinate,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Single(result.FinalOperandStack);
            Assert.Equal(256, result.FinalOperandStack[0].Value);
        }

        [Fact]
        public void MeasureOriginalDistanceUsesDualProjectionAndSpecifiedZoneOrder()
        {
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[]
                {
                    new TrueTypeHintingPoint(0, 10, true),
                    new TrueTypeHintingPoint(0, 20, true),
                }, new[] { new TrueTypeContourEndPointIndex(1) }),
                new TrueTypeHintingZone(new[]
                {
                    new TrueTypeHintingPoint(100, 100, true),
                    new TrueTypeHintingPoint(200, 200, true),
                }, new[] { new TrueTypeContourEndPointIndex(1) }));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(1);
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Horizontal;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0, // zp1 point 1, then zp0 point 0 on top
                (byte)TrueTypeInstructionOpcode.MeasureDistanceOriginal,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Single(result.FinalOperandStack);
            Assert.Equal(190, result.FinalOperandStack[0].Value);
        }

        [Fact]
        public void MeasureCurrentDistanceUsesProjectionAndReversingOperandsChangesSign()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(10, 0, true), new TrueTypeHintingPoint(70, 0, true));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Horizontal;

            TrueTypeVirtualMachineResult forwardResult = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.MeasureDistanceCurrent,
            }, state, zones);
            TrueTypeVirtualMachineResult reverseResult = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 1,
                (byte)TrueTypeInstructionOpcode.MeasureDistanceCurrent,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(forwardResult.Succeeded, forwardResult.Failure.ToString());
            Assert.True(reverseResult.Succeeded, reverseResult.Failure.ToString());
            Assert.Equal(60, forwardResult.FinalOperandStack[0].Value);
            Assert.Equal(-60, reverseResult.FinalOperandStack[0].Value);
        }

        [Fact]
        public void OriginalMeasurementsUseInitializedTwilightCoordinates()
        {
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[]
                {
                    new TrueTypeHintingPoint(0, 20, true),
                    new TrueTypeHintingPoint(0, 90, true),
                }, new[] { new TrueTypeContourEndPointIndex(1) }),
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]));
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerTwo = new TrueTypeZonePointerIndex(0);

            TrueTypeVirtualMachineResult coordinateResult = Interpreter().Execute(new byte[]
            {
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.GetOriginalCoordinate,
            }, state, zones);
            TrueTypeVirtualMachineResult distanceResult = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.MeasureDistanceOriginal,
            }, state, zones);

            Assert.True(coordinateResult.Succeeded, coordinateResult.Failure.ToString());
            Assert.True(distanceResult.Succeeded, distanceResult.Failure.ToString());
            Assert.Equal(90, coordinateResult.FinalOperandStack[0].Value);
            Assert.Equal(70, distanceResult.FinalOperandStack[0].Value);
        }

        [Fact]
        public void IupEqualOriginalReferencesUseLowerAndUpperReferenceDeltas()
        {
            var firstTouchedPoint = new TrueTypeHintingPoint(0, 64, true)
                { CurrentVerticalF26Dot6 = 96, TouchFlags = TrueTypePointTouchFlags.Vertical };
            var untouchedPoint = new TrueTypeHintingPoint(0, 64, true);
            var secondTouchedPoint = new TrueTypeHintingPoint(0, 64, true)
                { CurrentVerticalF26Dot6 = 128, TouchFlags = TrueTypePointTouchFlags.Vertical };
            TrueTypeHintingExecutionZones zones = Zones(firstTouchedPoint, untouchedPoint, secondTouchedPoint);

            TrueTypeHintingGeometryOperations.InterpolateUntouchedPoints(zones.GlyphZone, verticalAxis: true);

            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint interpolatedPoint));
            Assert.Equal(96, interpolatedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IupInteriorInterpolationRoundsF26Dot6DivisionToNearest()
        {
            var firstTouchedPoint = new TrueTypeHintingPoint(0, 0, true)
                { CurrentVerticalF26Dot6 = 0, TouchFlags = TrueTypePointTouchFlags.Vertical };
            var untouchedPoint = new TrueTypeHintingPoint(0, 1, true);
            var secondTouchedPoint = new TrueTypeHintingPoint(0, 3, true)
                { CurrentVerticalF26Dot6 = 2, TouchFlags = TrueTypePointTouchFlags.Vertical };
            TrueTypeHintingExecutionZones zones = Zones(firstTouchedPoint, untouchedPoint, secondTouchedPoint);

            TrueTypeHintingGeometryOperations.InterpolateUntouchedPoints(zones.GlyphZone, verticalAxis: true);

            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint interpolatedPoint));
            Assert.Equal(1, interpolatedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IupSingleTouchedPointShiftsEveryOtherContourPointByItsDelta()
        {
            var firstPoint = new TrueTypeHintingPoint(0, -64, true);
            var touchedPoint = new TrueTypeHintingPoint(0, 0, true)
                { CurrentVerticalF26Dot6 = 32, TouchFlags = TrueTypePointTouchFlags.Vertical };
            var lastPoint = new TrueTypeHintingPoint(0, 64, true);
            TrueTypeHintingExecutionZones zones = Zones(firstPoint, touchedPoint, lastPoint);

            TrueTypeHintingGeometryOperations.InterpolateUntouchedPoints(zones.GlyphZone, verticalAxis: true);

            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint shiftedFirstPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(2, out TrueTypeHintingPoint shiftedLastPoint));
            Assert.Equal(-32, shiftedFirstPoint.CurrentVerticalF26Dot6);
            Assert.Equal(96, shiftedLastPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IpInterpolatesPointByCurrentToOriginalReferenceRangeRatio()
        {
            var firstReferencePoint = new TrueTypeHintingPoint(0, 0, true) { CurrentVerticalF26Dot6 = 32 };
            var interpolatedPoint = new TrueTypeHintingPoint(0, 1, true);
            var secondReferencePoint = new TrueTypeHintingPoint(0, 3, true) { CurrentVerticalF26Dot6 = 34 };
            TrueTypeHintingExecutionZones zones = Zones(firstReferencePoint, interpolatedPoint, secondReferencePoint);
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(0);
            state.GraphicsState.ReferencePointTwo = new TrueTypeReferencePointIndex(2);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.InterpolatePoints,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(33, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IpEqualOriginalReferencesPreserveOriginalDistanceFromCurrentRp1()
        {
            var firstReferencePoint = new TrueTypeHintingPoint(0, 64, true) { CurrentVerticalF26Dot6 = 96 };
            var interpolatedPoint = new TrueTypeHintingPoint(0, 80, true) { CurrentVerticalF26Dot6 = 500 };
            var secondReferencePoint = new TrueTypeHintingPoint(0, 64, true) { CurrentVerticalF26Dot6 = 128 };
            TrueTypeHintingExecutionZones zones = Zones(firstReferencePoint, interpolatedPoint, secondReferencePoint);
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(0);
            state.GraphicsState.ReferencePointTwo = new TrueTypeReferencePointIndex(2);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.InterpolatePoints,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(112, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IpPointAtOriginalRp1MovesExactlyToCurrentRp1()
        {
            var firstReferencePoint = new TrueTypeHintingPoint(0, 64, true) { CurrentVerticalF26Dot6 = 96 };
            var interpolatedPoint = new TrueTypeHintingPoint(0, 64, true) { CurrentVerticalF26Dot6 = 500 };
            var secondReferencePoint = new TrueTypeHintingPoint(0, 64, true) { CurrentVerticalF26Dot6 = 128 };
            TrueTypeHintingExecutionZones zones = Zones(firstReferencePoint, interpolatedPoint, secondReferencePoint);
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(0);
            state.GraphicsState.ReferencePointTwo = new TrueTypeReferencePointIndex(2);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.InterpolatePoints,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(96, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IpUsesTwilightOriginalCoordinatesWhenAnyParticipatingZoneIsTwilight()
        {
            var twilightPoints = new[]
            {
                new TrueTypeHintingPoint(0, 10, true) { CurrentVerticalF26Dot6 = 20 },
                new TrueTypeHintingPoint(0, 30, true) { CurrentVerticalF26Dot6 = 60 },
            };
            var glyphPoint = new TrueTypeHintingPoint(0, 20, true) { CurrentVerticalF26Dot6 = 500 };
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(twilightPoints, new[] { new TrueTypeContourEndPointIndex(1) }),
                new TrueTypeHintingZone(new[] { glyphPoint }, new[] { new TrueTypeContourEndPointIndex(0) }));
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerTwo = new TrueTypeZonePointerIndex(1);
            state.GraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(0);
            state.GraphicsState.ReferencePointTwo = new TrueTypeReferencePointIndex(1);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.InterpolatePoints,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(40, movedPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IpConsumesLoopedPointsAndResetsLoop()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true) { CurrentVerticalF26Dot6 = 32 },
                new TrueTypeHintingPoint(0, 1, true),
                new TrueTypeHintingPoint(0, 2, true),
                new TrueTypeHintingPoint(0, 3, true) { CurrentVerticalF26Dot6 = 35 });
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(0);
            state.GraphicsState.ReferencePointTwo = new TrueTypeReferencePointIndex(3);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 2, (byte)TrueTypeInstructionOpcode.SetLoopCount,
                0xB1, 1, 2,
                (byte)TrueTypeInstructionOpcode.InterpolatePoints,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint firstMovedPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(2, out TrueTypeHintingPoint secondMovedPoint));
            Assert.Equal(33, firstMovedPoint.CurrentVerticalF26Dot6);
            Assert.Equal(34, secondMovedPoint.CurrentVerticalF26Dot6);
            Assert.Equal(1, state.GraphicsState.LoopCount.Value);
        }

        [Fact]
        public void SpvtlBuildsParallelAndCounterClockwisePerpendicularUnitVectors()
        {
            TrueTypeHintingExecutionZones parallelZones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(0, 128, true));
            TrueTypeVirtualMachineState parallelState = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult parallelResult = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0, // push p2 then p1 so p1=0 is popped first
                (byte)TrueTypeInstructionOpcode.SetProjectionVectorParallelToLine,
            }, parallelState, parallelZones);

            Assert.True(parallelResult.Succeeded, parallelResult.Failure.ToString());
            Assert.Equal(0, parallelState.GraphicsState.ProjectionVector.HorizontalComponent.Value);
            Assert.Equal(0x4000, parallelState.GraphicsState.ProjectionVector.VerticalComponent.Value);
            Assert.Equal(0x4000, parallelState.GraphicsState.DualProjectionVector.VerticalComponent.Value);

            TrueTypeHintingExecutionZones perpendicularZones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(0, 128, true));
            TrueTypeVirtualMachineState perpendicularState = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult perpendicularResult = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.SetProjectionVectorPerpendicularToLine,
            }, perpendicularState, perpendicularZones);

            Assert.True(perpendicularResult.Succeeded, perpendicularResult.Failure.ToString());
            Assert.Equal(-0x4000, perpendicularState.GraphicsState.ProjectionVector.HorizontalComponent.Value);
            Assert.Equal(0, perpendicularState.GraphicsState.ProjectionVector.VerticalComponent.Value);
        }

        [Fact]
        public void SdpvtlDerivesCurrentProjectionAndOriginalDualProjectionSeparately()
        {
            var firstPoint = new TrueTypeHintingPoint(0, 0, true);
            var secondPoint = new TrueTypeHintingPoint(128, 0, true)
            {
                CurrentHorizontalF26Dot6 = 0,
                CurrentVerticalF26Dot6 = 128,
            };
            TrueTypeHintingExecutionZones zones = Zones(firstPoint, secondPoint);
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.SetDualProjectionVectorsParallelToLine,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(0, state.GraphicsState.ProjectionVector.HorizontalComponent.Value);
            Assert.Equal(0x4000, state.GraphicsState.ProjectionVector.VerticalComponent.Value);
            Assert.Equal(0x4000, state.GraphicsState.DualProjectionVector.HorizontalComponent.Value);
            Assert.Equal(0, state.GraphicsState.DualProjectionVector.VerticalComponent.Value);
        }

        [Fact]
        public void AlignPtsMovesBothPointsToTheirProjectedMidpoint()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(128, 0, true));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.AlignPoints,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint firstPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint secondPoint));
            Assert.Equal(64, firstPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(64, secondPoint.CurrentHorizontalF26Dot6);
            Assert.True(firstPoint.IsTouchedHorizontally);
            Assert.True(secondPoint.IsTouchedHorizontally);
        }

        [Fact]
        public void AlignPtsOddPositiveDistanceUsesSymmetricTruncatedHalfMovement()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(3, 0, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.AlignPoints,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint zoneZeroPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint zoneOnePoint));
            Assert.Equal(1, zoneZeroPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(2, zoneOnePoint.CurrentHorizontalF26Dot6);
        }

        [Fact]
        public void AlignPtsOddNegativeDistanceUsesSymmetricTruncatedHalfMovement()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(3, 0, true), new TrueTypeHintingPoint(0, 0, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.AlignPoints,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint zoneZeroPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint zoneOnePoint));
            Assert.Equal(2, zoneZeroPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(1, zoneOnePoint.CurrentHorizontalF26Dot6);
        }

        [Fact]
        public void AlignPtsUsesZoneOneForTopOperandAndZoneZeroForNextOperand()
        {
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[]
                {
                    new TrueTypeHintingPoint(100, 0, true),
                    new TrueTypeHintingPoint(200, 0, true),
                }, new[] { new TrueTypeContourEndPointIndex(1) }),
                new TrueTypeHintingZone(new[]
                {
                    new TrueTypeHintingPoint(0, 0, true),
                    new TrueTypeHintingPoint(10, 0, true),
                }, new[] { new TrueTypeContourEndPointIndex(1) }));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(1);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0, // zone-zero point 1, then zone-one point 0 on top
                (byte)TrueTypeInstructionOpcode.AlignPoints,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.TwilightZone.TryGetPoint(1, out TrueTypeHintingPoint zoneZeroPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint zoneOnePoint));
            Assert.Equal(100, zoneZeroPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(100, zoneOnePoint.CurrentHorizontalF26Dot6);
        }

        [Fact]
        public void SpvtlCoincidentPointsFallBackToHorizontalIgnoringPerpendicularVariant()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(64, 64, true), new TrueTypeHintingPoint(64, 64, true));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.SetProjectionVectorPerpendicularToLine,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(0x4000, state.GraphicsState.ProjectionVector.HorizontalComponent.Value);
            Assert.Equal(0, state.GraphicsState.ProjectionVector.VerticalComponent.Value);
            Assert.Equal(0x4000, state.GraphicsState.DualProjectionVector.HorizontalComponent.Value);
        }

        [Fact]
        public void SfvtlCoincidentPointsFallBackToHorizontalIgnoringPerpendicularVariant()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(64, 64, true), new TrueTypeHintingPoint(64, 64, true));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.SetFreedomVectorPerpendicularToLine,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(0x4000, state.GraphicsState.FreedomVector.HorizontalComponent.Value);
            Assert.Equal(0, state.GraphicsState.FreedomVector.VerticalComponent.Value);
        }

        [Fact]
        public void SdpvtlFallsBackPerCoordinateSpaceWhenOnlyCurrentPointsCoincide()
        {
            var firstPoint = new TrueTypeHintingPoint(0, 0, true);
            var secondPoint = new TrueTypeHintingPoint(0, 128, true)
            {
                CurrentHorizontalF26Dot6 = 0,
                CurrentVerticalF26Dot6 = 0,
            };
            TrueTypeHintingExecutionZones zones = Zones(firstPoint, secondPoint);
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.SetDualProjectionVectorsPerpendicularToLine,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.Equal(0x4000, state.GraphicsState.ProjectionVector.HorizontalComponent.Value);
            Assert.Equal(0, state.GraphicsState.ProjectionVector.VerticalComponent.Value);
            Assert.Equal(-0x4000, state.GraphicsState.DualProjectionVector.HorizontalComponent.Value);
            Assert.Equal(0, state.GraphicsState.DualProjectionVector.VerticalComponent.Value);
        }

        [Fact]
        public void ShiftContourMovesOnlySelectedContourAndExcludesReferencePoint()
        {
            var referencePoint = new TrueTypeHintingPoint(0, 0, true)
            {
                CurrentVerticalF26Dot6 = 64,
                TouchFlags = TrueTypePointTouchFlags.Vertical,
            };
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]),
                new TrueTypeHintingZone(new[]
                {
                    referencePoint,
                    new TrueTypeHintingPoint(0, 10, true),
                    new TrueTypeHintingPoint(0, 20, true),
                    new TrueTypeHintingPoint(0, 30, true),
                }, new[] { new TrueTypeContourEndPointIndex(1), new TrueTypeContourEndPointIndex(3) }));
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(0);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.ShiftContourUsingReferencePointOne,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint pointZero));
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint pointOne));
            Assert.True(zones.GlyphZone.TryGetPoint(2, out TrueTypeHintingPoint pointTwo));
            Assert.True(zones.GlyphZone.TryGetPoint(3, out TrueTypeHintingPoint pointThree));
            Assert.Equal(64, pointZero.CurrentVerticalF26Dot6);
            Assert.Equal(74, pointOne.CurrentVerticalF26Dot6);
            Assert.Equal(20, pointTwo.CurrentVerticalF26Dot6);
            Assert.Equal(30, pointThree.CurrentVerticalF26Dot6);
            Assert.True(pointOne.IsTouchedVertically);
            Assert.False(pointTwo.IsTouchedVertically);
            Assert.False(pointThree.IsTouchedVertically);
        }

        [Fact]
        public void ShpixIgnoresOrthogonalProjectionVectorAndMovesAlongFreedomVector()
        {
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(10, 20, true));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Horizontal;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Horizontal;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 70,
                (byte)TrueTypeInstructionOpcode.ShiftPointsByPixelAmount,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(10, movedPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(90, movedPoint.CurrentVerticalF26Dot6);
            Assert.False(movedPoint.IsTouchedHorizontally);
            Assert.True(movedPoint.IsTouchedVertically);
        }

        [Fact]
        public void ShpixDiagonalFreedomVectorUsesFreedomDistanceWithoutProjectionScaling()
        {
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(0, 0, true));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Horizontal;
            state.GraphicsState.FreedomVector = new TrueTypeUnitVector(
                new TrueTypeVectorComponent(0x2D41), new TrueTypeVectorComponent(0x2D41));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 64,
                (byte)TrueTypeInstructionOpcode.ShiftPointsByPixelAmount,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(45, movedPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(45, movedPoint.CurrentVerticalF26Dot6);
            Assert.True(movedPoint.IsTouchedHorizontally);
            Assert.True(movedPoint.IsTouchedVertically);
        }

        [Fact]
        public void ShpixConsumesOneAmountAfterLoopedPointsAndResetsLoop()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(0, 0, true), new TrueTypeHintingPoint(0, 64, true));
            TrueTypeVirtualMachineState state = VerticalState();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 2, (byte)TrueTypeInstructionOpcode.SetLoopCount,
                0xB2, 0, 1, 16,
                (byte)TrueTypeInstructionOpcode.ShiftPointsByPixelAmount,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint firstPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint secondPoint));
            Assert.Equal(16, firstPoint.CurrentVerticalF26Dot6);
            Assert.Equal(80, secondPoint.CurrentVerticalF26Dot6);
            Assert.Equal(1, state.GraphicsState.LoopCount.Value);
        }

        [Fact]
        public void ShpixSupportsNegativeFreedomDistance()
        {
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(100, 0, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                0xB8, 0xFF, 0xE0,
                (byte)TrueTypeInstructionOpcode.ShiftPointsByPixelAmount,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint movedPoint));
            Assert.Equal(68, movedPoint.CurrentHorizontalF26Dot6);
        }

        [Fact]
        public void ShiftZoneUsesStackSelectedZoneAndExcludesReferencePoint()
        {
            var referencePoint = new TrueTypeHintingPoint(0, 0, true)
            {
                CurrentVerticalF26Dot6 = 32,
                TouchFlags = TrueTypePointTouchFlags.Vertical,
            };
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[]
                {
                    referencePoint,
                    new TrueTypeHintingPoint(0, 10, true),
                    new TrueTypeHintingPoint(0, 20, true),
                }, new[] { new TrueTypeContourEndPointIndex(2) }),
                new TrueTypeHintingZone(new[] { new TrueTypeHintingPoint(0, 100, true) },
                    new[] { new TrueTypeContourEndPointIndex(0) }));
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(0);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.ShiftZoneUsingReferencePointOne,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.TwilightZone.TryGetPoint(0, out TrueTypeHintingPoint pointZero));
            Assert.True(zones.TwilightZone.TryGetPoint(1, out TrueTypeHintingPoint pointOne));
            Assert.True(zones.TwilightZone.TryGetPoint(2, out TrueTypeHintingPoint pointTwo));
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint glyphPoint));
            Assert.Equal(32, pointZero.CurrentVerticalF26Dot6);
            Assert.Equal(42, pointOne.CurrentVerticalF26Dot6);
            Assert.Equal(52, pointTwo.CurrentVerticalF26Dot6);
            Assert.Equal(100, glyphPoint.CurrentVerticalF26Dot6);
            Assert.True(pointZero.IsTouchedVertically);
            Assert.False(pointOne.IsTouchedVertically);
            Assert.False(pointTwo.IsTouchedVertically);
        }

        [Fact]
        public void ShiftZoneMovesOnlyGlyphOutlinePointsWithoutTouchingOrMovingPhantoms()
        {
            var referencePoint = new TrueTypeHintingPoint(0, 0, true)
            {
                CurrentVerticalF26Dot6 = 16,
                TouchFlags = TrueTypePointTouchFlags.Vertical,
            };
            var glyphPoints = new[]
            {
                new TrueTypeHintingPoint(0, 100, true),
                new TrueTypeHintingPoint(0, 0, false),
                new TrueTypeHintingPoint(64, 0, false),
                new TrueTypeHintingPoint(0, 128, false),
                new TrueTypeHintingPoint(0, -128, false),
            };
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new[] { referencePoint }, new[] { new TrueTypeContourEndPointIndex(0) }),
                new TrueTypeHintingZone(glyphPoints, new[] { new TrueTypeContourEndPointIndex(0) }));
            TrueTypeVirtualMachineState state = VerticalState();
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ReferencePointOne = new TrueTypeReferencePointIndex(0);

            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.ShiftZoneUsingReferencePointOne,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint outlinePoint));
            Assert.True(zones.GlyphZone.TryGetPoint(3, out TrueTypeHintingPoint topPhantomPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(4, out TrueTypeHintingPoint bottomPhantomPoint));
            Assert.Equal(116, outlinePoint.CurrentVerticalF26Dot6);
            Assert.Equal(128, topPhantomPoint.CurrentVerticalF26Dot6);
            Assert.Equal(-128, bottomPhantomPoint.CurrentVerticalF26Dot6);
            Assert.False(outlinePoint.IsTouchedVertically);
            Assert.False(topPhantomPoint.IsTouchedVertically);
            Assert.False(bottomPhantomPoint.IsTouchedVertically);
        }

        [Fact]
        public void ShiftContourRejectsInvalidContourIndex()
        {
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(0, 0, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 1,
                (byte)TrueTypeInstructionOpcode.ShiftContourUsingReferencePointTwo,
            }, VerticalState(), zones);

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidContourIndex, result.Failure.FailureCode);
        }

        [Fact]
        public void UntouchPointClearsOnlyAxesSelectedByFreedomVector()
        {
            var sourcePoint = new TrueTypeHintingPoint(0, 0, true)
            {
                TouchFlags = TrueTypePointTouchFlags.Horizontal | TrueTypePointTouchFlags.Vertical,
            };
            TrueTypeHintingExecutionZones zones = Zones(sourcePoint);
            TrueTypeVirtualMachineState verticalState = VerticalState();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.UntouchPoint,
            }, verticalState, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint point));
            Assert.True(point.IsTouchedHorizontally);
            Assert.False(point.IsTouchedVertically);
        }

        [Fact]
        public void UntouchPointClearsBothAxesForDiagonalFreedomVector()
        {
            var sourcePoint = new TrueTypeHintingPoint(0, 0, true)
            {
                TouchFlags = TrueTypePointTouchFlags.Horizontal | TrueTypePointTouchFlags.Vertical,
            };
            TrueTypeHintingExecutionZones zones = Zones(sourcePoint);
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.FreedomVector = new TrueTypeUnitVector(
                new TrueTypeVectorComponent(0x2D41), new TrueTypeVectorComponent(0x2D41));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 0,
                (byte)TrueTypeInstructionOpcode.UntouchPoint,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint point));
            Assert.Equal(TrueTypePointTouchFlags.None, point.TouchFlags);
        }

        [Fact]
        public void FlipPointsUsesLoopAndDoesNotAlterTouchFlags()
        {
            var firstPoint = new TrueTypeHintingPoint(0, 0, true) { TouchFlags = TrueTypePointTouchFlags.Vertical };
            var secondPoint = new TrueTypeHintingPoint(64, 0, false) { TouchFlags = TrueTypePointTouchFlags.Horizontal };
            TrueTypeHintingExecutionZones zones = Zones(firstPoint, secondPoint);
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB0, 2,
                (byte)TrueTypeInstructionOpcode.SetLoopCount,
                0xB1, 0, 1,
                (byte)TrueTypeInstructionOpcode.FlipPoints,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint flippedFirstPoint));
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint flippedSecondPoint));
            Assert.False(flippedFirstPoint.IsOnCurve);
            Assert.True(flippedSecondPoint.IsOnCurve);
            Assert.Equal(TrueTypePointTouchFlags.Vertical, flippedFirstPoint.TouchFlags);
            Assert.Equal(TrueTypePointTouchFlags.Horizontal, flippedSecondPoint.TouchFlags);
            Assert.Equal(1, state.GraphicsState.LoopCount.Value);
        }

        [Fact]
        public void FlipRangeSetsInclusiveCurveStateWithoutTouchingPoints()
        {
            var points = new[]
            {
                new TrueTypeHintingPoint(0, 0, false),
                new TrueTypeHintingPoint(64, 0, false) { TouchFlags = TrueTypePointTouchFlags.Vertical },
                new TrueTypeHintingPoint(128, 0, false),
            };
            TrueTypeHintingExecutionZones zones = Zones(points);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 1, // low, high
                (byte)TrueTypeInstructionOpcode.SetPointRangeOnCurve,
                0xB1, 1, 2,
                (byte)TrueTypeInstructionOpcode.SetPointRangeOffCurve,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(0, out TrueTypeHintingPoint pointZero));
            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint pointOne));
            Assert.True(zones.GlyphZone.TryGetPoint(2, out TrueTypeHintingPoint pointTwo));
            Assert.True(pointZero.IsOnCurve);
            Assert.False(pointOne.IsOnCurve);
            Assert.False(pointTwo.IsOnCurve);
            Assert.Equal(TrueTypePointTouchFlags.Vertical, pointOne.TouchFlags);
        }

        [Fact]
        public void FlipRangeRejectsReversedAndOutOfRangeEndpoints()
        {
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(0, 0, true));
            TrueTypeVirtualMachineResult reversedResult = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.SetPointRangeOnCurve,
            }, TrueTypeVirtualMachineState.ForTests(), zones);
            Assert.False(reversedResult.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidPointIndex, reversedResult.Failure.FailureCode);

            TrueTypeVirtualMachineResult outOfRangeResult = Interpreter().Execute(new byte[]
            {
                0xB1, 0, 1,
                (byte)TrueTypeInstructionOpcode.SetPointRangeOffCurve,
            }, TrueTypeVirtualMachineState.ForTests(), zones);
            Assert.False(outOfRangeResult.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidPointIndex, outOfRangeResult.Failure.FailureCode);
        }

        [Fact]
        public void IntersectLinesMovesPointToExactIntersectionIgnoringFreedomVector()
        {
            var points = new[]
            {
                new TrueTypeHintingPoint(0, 0, true),
                new TrueTypeHintingPoint(128, 128, true),
                new TrueTypeHintingPoint(0, 128, true),
                new TrueTypeHintingPoint(128, 0, true),
                new TrueTypeHintingPoint(999, 999, true),
            };
            TrueTypeHintingExecutionZones zones = Zones(points);
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB4, 4, 3, 2, 1, 0, // p, b1, b0, a1, a0; a0 is popped first
                (byte)TrueTypeInstructionOpcode.IntersectLines,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(4, out TrueTypeHintingPoint intersectionPoint));
            Assert.Equal(64, intersectionPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(64, intersectionPoint.CurrentVerticalF26Dot6);
            Assert.True(intersectionPoint.IsTouchedHorizontally);
            Assert.True(intersectionPoint.IsTouchedVertically);
        }

        [Fact]
        public void IntersectLinesUsesAllThreeZonePointers()
        {
            var twilightPoints = new[]
            {
                new TrueTypeHintingPoint(64, -64, true),
                new TrueTypeHintingPoint(64, 192, true),
            };
            var glyphPoints = new[]
            {
                new TrueTypeHintingPoint(0, 32, true),
                new TrueTypeHintingPoint(128, 32, true),
                new TrueTypeHintingPoint(500, 500, true),
            };
            var zones = new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(twilightPoints, new[] { new TrueTypeContourEndPointIndex(1) }),
                new TrueTypeHintingZone(glyphPoints, new[] { new TrueTypeContourEndPointIndex(2) }));
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ZonePointerZero = new TrueTypeZonePointerIndex(0);
            state.GraphicsState.ZonePointerOne = new TrueTypeZonePointerIndex(1);
            state.GraphicsState.ZonePointerTwo = new TrueTypeZonePointerIndex(1);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB4, 2, 1, 0, 1, 0,
                (byte)TrueTypeInstructionOpcode.IntersectLines,
            }, state, zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(2, out TrueTypeHintingPoint intersectionPoint));
            Assert.Equal(64, intersectionPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(32, intersectionPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IntersectParallelLinesUsesMeanOfFourEndpoints()
        {
            var points = new[]
            {
                new TrueTypeHintingPoint(0, 0, true),
                new TrueTypeHintingPoint(128, 0, true),
                new TrueTypeHintingPoint(0, 64, true),
                new TrueTypeHintingPoint(128, 64, true),
                new TrueTypeHintingPoint(0, 0, true),
            };
            TrueTypeHintingExecutionZones zones = Zones(points);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB4, 4, 3, 2, 1, 0,
                (byte)TrueTypeInstructionOpcode.IntersectLines,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(4, out TrueTypeHintingPoint intersectionPoint));
            Assert.Equal(64, intersectionPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(32, intersectionPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IntersectNearParallelLinesUseStableMeanFallbackInsteadOfFarIntersection()
        {
            var points = new[]
            {
                new TrueTypeHintingPoint(0, 0, true),
                new TrueTypeHintingPoint(1000, 0, true),
                new TrueTypeHintingPoint(0, 64, true),
                new TrueTypeHintingPoint(1000, 65, true),
                new TrueTypeHintingPoint(0, 0, true),
            };
            TrueTypeHintingExecutionZones zones = Zones(points);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB4, 4, 3, 2, 1, 0,
                (byte)TrueTypeInstructionOpcode.IntersectLines,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(4, out TrueTypeHintingPoint intersectionPoint));
            Assert.Equal(500, intersectionPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(32, intersectionPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IntersectFallbackMeanUsesSignedIntegerTruncation()
        {
            var points = new[]
            {
                new TrueTypeHintingPoint(-2, -2, true),
                new TrueTypeHintingPoint(-1, -2, true),
                new TrueTypeHintingPoint(-2, -1, true),
                new TrueTypeHintingPoint(-1, -1, true),
                new TrueTypeHintingPoint(0, 0, true),
            };
            TrueTypeHintingExecutionZones zones = Zones(points);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB4, 4, 3, 2, 1, 0,
                (byte)TrueTypeInstructionOpcode.IntersectLines,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(4, out TrueTypeHintingPoint intersectionPoint));
            Assert.Equal(-1, intersectionPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(-1, intersectionPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IntersectClearlyNonParallelLinesStillUseExactIntersection()
        {
            var points = new[]
            {
                new TrueTypeHintingPoint(0, 0, true),
                new TrueTypeHintingPoint(1000, 0, true),
                new TrueTypeHintingPoint(500, -1000, true),
                new TrueTypeHintingPoint(501, 1000, true),
                new TrueTypeHintingPoint(0, 0, true),
            };
            TrueTypeHintingExecutionZones zones = Zones(points);
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB4, 4, 3, 2, 1, 0,
                (byte)TrueTypeInstructionOpcode.IntersectLines,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.True(result.Succeeded, result.Failure.ToString());
            Assert.True(zones.GlyphZone.TryGetPoint(4, out TrueTypeHintingPoint intersectionPoint));
            Assert.Equal(501, intersectionPoint.CurrentHorizontalF26Dot6);
            Assert.Equal(0, intersectionPoint.CurrentVerticalF26Dot6);
        }

        [Fact]
        public void IntersectLinesFailsStructurallyForInvalidEndpoint()
        {
            TrueTypeHintingExecutionZones zones = Zones(new TrueTypeHintingPoint(0, 0, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB4, 0, 0, 0, 0, 99,
                (byte)TrueTypeInstructionOpcode.IntersectLines,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidPointIndex, result.Failure.FailureCode);
        }

        private static TrueTypeHintingExecutionZones Zones(params TrueTypeHintingPoint[] points)
            => new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]),
                new TrueTypeHintingZone(points, points.Length == 0
                    ? new TrueTypeContourEndPointIndex[0]
                    : new[] { new TrueTypeContourEndPointIndex(points.Length - 1) }));

        private static TrueTypeVirtualMachineState VerticalState()
        {
            TrueTypeVirtualMachineState state = TrueTypeVirtualMachineState.ForTests();
            state.GraphicsState.ProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.DualProjectionVector = TrueTypeUnitVector.Vertical;
            state.GraphicsState.FreedomVector = TrueTypeUnitVector.Vertical;
            return state;
        }

        private static TrueTypeInstructionInterpreter Interpreter()
            => new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());
    }
}