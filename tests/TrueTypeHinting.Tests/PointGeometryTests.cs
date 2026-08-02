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
        public void LineDerivedVectorRejectsCoincidentPoints()
        {
            TrueTypeHintingExecutionZones zones = Zones(
                new TrueTypeHintingPoint(64, 64, true), new TrueTypeHintingPoint(64, 64, true));
            TrueTypeVirtualMachineResult result = Interpreter().Execute(new byte[]
            {
                0xB1, 1, 0,
                (byte)TrueTypeInstructionOpcode.SetFreedomVectorParallelToLine,
            }, TrueTypeVirtualMachineState.ForTests(), zones);

            Assert.False(result.Succeeded);
            Assert.Equal(TrueTypeVirtualMachineFailureCode.InvalidFreedomProjectionVectors, result.Failure.FailureCode);
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
        }

        [Fact]
        public void ShiftZoneMovesGlyphPhantomPointsAlongWithOutlinePoints()
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
            Assert.Equal(144, topPhantomPoint.CurrentVerticalF26Dot6);
            Assert.Equal(-112, bottomPhantomPoint.CurrentVerticalF26Dot6);
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