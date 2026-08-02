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
        public void IupInterpolatesUntouchedPointBetweenMovedEndpoints()
        {
            var firstPoint = new TrueTypeHintingPoint(0, 0, true) { CurrentVerticalF26Dot6 = 64, TouchFlags = TrueTypePointTouchFlags.Vertical };
            var middlePoint = new TrueTypeHintingPoint(0, 64, true);
            var lastPoint = new TrueTypeHintingPoint(0, 128, true) { CurrentVerticalF26Dot6 = 256, TouchFlags = TrueTypePointTouchFlags.Vertical };
            TrueTypeHintingExecutionZones zones = Zones(firstPoint, middlePoint, lastPoint);

            TrueTypeHintingGeometryOperations.InterpolateUntouchedPoints(zones.GlyphZone, verticalAxis: true);

            Assert.True(zones.GlyphZone.TryGetPoint(1, out TrueTypeHintingPoint interpolatedPoint));
            Assert.Equal(160, interpolatedPoint.CurrentVerticalF26Dot6);
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

        private static TrueTypeHintingExecutionZones Zones(params TrueTypeHintingPoint[] points)
            => new TrueTypeHintingExecutionZones(
                new TrueTypeHintingZone(new TrueTypeHintingPoint[0], new TrueTypeContourEndPointIndex[0]),
                new TrueTypeHintingZone(points, points.Length == 0
                    ? new TrueTypeContourEndPointIndex[0]
                    : new[] { new TrueTypeContourEndPointIndex(points.Length - 1) }));

        private static TrueTypeInstructionInterpreter Interpreter()
            => new TrueTypeInstructionInterpreter(TrueTypeExecutionLimits.ForTests());
    }
}