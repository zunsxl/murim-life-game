using System;
using Murim.Core.Exploration.Placeholder;
using Murim.Core.Tests.Valley;
using Murim.Core.Tuning;
using Murim.Core.Valley;
using NUnit.Framework;

namespace Murim.Core.Tests.Placeholder
{
    [TestFixture]
    public sealed class PlaceholderMovementTests
    {
        private static readonly Vec3 CameraRayDir = new Vec3(0f, -ValleyConstants.Sin45, ValleyConstants.Cos45);

        private static DestinationMarker ClickAt45(PlaceholderController controller, Vec3 target, float cutY = ValleyConstants.FullViewCutY)
        {
            Vec3 origin = target - CameraRayDir * 30f;
            return controller.Click(origin, CameraRayDir, cutY);
        }

        // T-CORE-10
        [Test]
        public void T_CORE_10_Flat8WalkArrivesIn36Ticks()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();
            PlaceholderController controller = new PlaceholderController(flat8, ExplorationTuning.Default);
            Vec3 target = new Vec3(4.5f, 0f, 1.5f);

            DestinationMarker marker = ClickAt45(controller, target);
            Assert.That(marker.Kind, Is.EqualTo(MarkerKind.Reachable));
            Assert.That(marker.Sequence, Is.EqualTo(1));

            for (int tick = 1; tick <= 35; tick++)
            {
                controller.Tick();
                Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.GroundedTranslating), $"Tick {tick}");
            }

            controller.Tick();
            Assert.That(controller.State.Position.X, Is.EqualTo(4.5f));
            Assert.That(controller.State.Position.Y, Is.EqualTo(1.5f));
            Assert.That(controller.State.Elevation, Is.EqualTo(0f));
            Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.GroundedStationary));
        }

        // T-CORE-11
        [Test]
        public void T_CORE_11_Tier12WalksUpSlopeToThreeMetreTerrace()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            PlaceholderController controller = new PlaceholderController(tier12, ExplorationTuning.Default);
            Vec3 target = new Vec3(4.5f, 3f, 7.5f);

            DestinationMarker marker = ClickAt45(controller, target);
            Assert.That(marker.Kind, Is.EqualTo(MarkerKind.Reachable));

            bool visitedSlope = false;
            int safety = 0;
            while (controller.State.Motion != PlaceholderMotion.GroundedStationary && safety++ < 1000)
            {
                controller.Tick();
                Vec2 pos = controller.State.Position;
                int cx = (int)MathF.Floor(pos.X);
                int cz = (int)MathF.Floor(pos.Y);
                Assert.That(tier12.IsWalkable(cx, cz), Is.True, $"Non-walkable cell ({cx}, {cz}) at tick {safety}");
                Assert.That(controller.State.Elevation, Is.EqualTo(tier12.HeightAt(pos.X, pos.Y)).Within(1e-6f));
                if (ValleyKindMetadata.IsSlopeKind(tier12.KindAt(cx, cz)))
                {
                    visitedSlope = true;
                }
            }

            Assert.That(visitedSlope, Is.True);
            Assert.That(controller.State.Position.X, Is.EqualTo(4.5f).Within(1e-5f));
            Assert.That(controller.State.Position.Y, Is.EqualTo(7.5f).Within(1e-5f));
            Assert.That(controller.State.Elevation, Is.EqualTo(3f).Within(1e-5f));
        }

        // T-CORE-12
        [Test]
        public void T_CORE_12_BlockedTerraceAndCliffEndOnFlatFloor0_3mFromEdge()
        {
            // Subcase 1: Tier12NoSlope, click 3 m terrace
            ValleyLayout noSlope = ValleyTestFixtures.CreateTier12NoSlope();
            PlaceholderController ctrlNoSlope = new PlaceholderController(noSlope, ExplorationTuning.Default);
            DestinationMarker m1 = ClickAt45(ctrlNoSlope, new Vec3(4.5f, 3f, 5.5f));
            Assert.That(m1.Kind, Is.EqualTo(MarkerKind.Unreachable));
            RunToStop(ctrlNoSlope, noSlope);
            AssertOnFlatZeroBeforeCliff(ctrlNoSlope, noSlope, new Vec3(4.5f, 3f, 5.5f));

            // Subcase 2: Tier12, click cliff face two cells beside slope (6, 2)
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            PlaceholderController ctrlCliff = new PlaceholderController(tier12, ExplorationTuning.Default);
            DestinationMarker m2 = ClickAt45(ctrlCliff, new Vec3(6.5f, 1.5f, 2.5f));
            Assert.That(m2.Kind, Is.EqualTo(MarkerKind.Unreachable));
            RunToStop(ctrlCliff, tier12);
            AssertOnFlatZeroBeforeCliff(ctrlCliff, tier12, new Vec3(6.5f, 1.5f, 2.5f));

            // Subcase 3: Tier12, click boulder on 3 m terrace (5, 5) from 0 m ground
            PlaceholderController ctrlBoulder = new PlaceholderController(tier12, ExplorationTuning.Default);
            DestinationMarker m3 = ClickAt45(ctrlBoulder, new Vec3(5.5f, 3f, 5.5f));
            Assert.That(m3.Kind, Is.EqualTo(MarkerKind.Unreachable));
            RunToStop(ctrlBoulder, tier12);
            AssertOnFlatZeroBeforeCliff(ctrlBoulder, tier12, new Vec3(5.5f, 3f, 5.5f));
        }

        // T-CORE-13
        [Test]
        public void T_CORE_13_RiverBoulderCliffAndNearEdgeReachable()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();

            // River click
            AssertBlockedClickStopsOnFlatWalkable(tier12, new Vec3(10.5f, 0f, 1.5f));
            // Low boulder cell (8, 1) click
            AssertBlockedClickStopsOnFlatWalkable(tier12, new Vec3(8.5f, 0f, 1.5f));
            // Cliff cell (5, 2) click
            AssertBlockedClickStopsOnFlatWalkable(tier12, new Vec3(5.5f, 1.5f, 2.5f));

            // Reachable ground 0.05 m west of boulder (8, 1) -> in cell (7, 1) at x = 7.95
            PlaceholderController controller = new PlaceholderController(tier12, ExplorationTuning.Default);
            DestinationMarker marker = ClickAt45(controller, new Vec3(7.95f, 0f, 1.5f));
            Assert.That(marker.Kind, Is.EqualTo(MarkerKind.Reachable));
            RunToStop(controller, tier12);
            Assert.That(controller.State.Position.X, Is.EqualTo(7.7f).Within(1e-5f));
            Assert.That(controller.State.Position.Y, Is.EqualTo(1.5f).Within(1e-5f));
        }

        // T-CORE-14
        [Test]
        public void T_CORE_14_AirborneClickClearBlockedBoundsAndAdjacent()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            PlaceholderController controller = new PlaceholderController(tier12, ExplorationTuning.Default);

            // Ascend to layer 1 (5 m)
            controller.Ascend();
            RunTicks(controller, 40);
            Assert.That(controller.State.AirLayer, Is.EqualTo(1));
            Assert.That(controller.State.Elevation, Is.EqualTo(5f));

            // 1. Click across open ground -> (4.5, 6.5) on 3 m terrace (clear at layer 1)
            DestinationMarker clearMarker = ClickAt45(controller, new Vec3(4.5f, 5f, 6.5f));
            Assert.That(clearMarker.Kind, Is.EqualTo(MarkerKind.Reachable));
            int ticks = 0;
            while (controller.State.Motion != PlaceholderMotion.AirborneStationary && ticks < 500)
            {
                controller.Tick();
                ticks++;
                if (ticks < 38)
                {
                    Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.AirborneTranslating));
                }
            }
            Assert.That(ticks, Is.EqualTo(38)); // 5 m at 8/60 m/tick = 37.5 -> 38 ticks
            Assert.That(controller.State.Position.X, Is.EqualTo(4.5f).Within(1e-5f));
            Assert.That(controller.State.Position.Y, Is.EqualTo(6.5f).Within(1e-5f));

            // 2. Click toward the 6 m terrace (cliff2 at cz=8 not air-clear at layer 1)
            DestinationMarker cliffMarker = ClickAt45(controller, new Vec3(4.5f, 5f, 9.5f));
            Assert.That(cliffMarker.Kind, Is.EqualTo(MarkerKind.Unreachable));
            RunUntilStationary(controller, 500);
            Assert.That(controller.State.Position.X, Is.EqualTo(4.5f).Within(1e-5f));
            Assert.That(controller.State.Position.Y, Is.EqualTo(7.7f).Within(1e-5f));

            // 3. Click past the bounds (AirBounds [1, 11] x [1, 11]) -> south/west past x = 1
            DestinationMarker boundsMarker = ClickAt45(controller, new Vec3(-2f, 5f, 7.7f));
            Assert.That(boundsMarker.Kind, Is.EqualTo(MarkerKind.Unreachable));
            RunUntilStationary(controller, 500);
            Assert.That(controller.State.Position.X, Is.EqualTo(1.3f).Within(1e-5f));
            Assert.That(controller.State.Position.Y, Is.EqualTo(7.7f).Within(1e-5f));

            // 4. Blocking cell adjacent: move to (4.5, 7.85) which is 0.15 m south of blocked cell (4, 8), then click north
            ClickAt45(controller, new Vec3(4.5f, 5f, 7.7f));
            RunUntilStationary(controller, 500);
            // Move into (4, 7) at 7.85 (still clear in (4, 7))
            DestinationMarker nudge = ClickAt45(controller, new Vec3(4.5f, 5f, 7.85f));
            Assert.That(nudge.Kind, Is.EqualTo(MarkerKind.Reachable));
            RunUntilStationary(controller, 100);
            Assert.That(controller.State.Position.Y, Is.EqualTo(7.85f).Within(1e-5f));

            DestinationMarker adj = ClickAt45(controller, new Vec3(4.5f, 5f, 9.5f));
            Assert.That(adj.Kind, Is.EqualTo(MarkerKind.Unreachable));
            RunTicks(controller, 10);
            Assert.That(controller.State.Position.Y, Is.EqualTo(7.85f).Within(1e-5f));
            Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.AirborneStationary));
        }

        // T-CORE-15
        [Test]
        public void T_CORE_15_AscendDescendLadderAndSpecialSurfaces()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            PlaceholderController controller = new PlaceholderController(tier12, ExplorationTuning.Default);
            int expectedSeq = 0;

            // Worked ladder on 0 m ground: R->5, R->11, R->17, R->unchanged, F->11, F->5, F->0
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 1, ++expectedSeq);
            Assert.That(controller.State.TargetElevation, Is.EqualTo(5f));
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 2, ++expectedSeq);
            Assert.That(controller.State.TargetElevation, Is.EqualTo(11f));
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 3, ++expectedSeq);
            Assert.That(controller.State.TargetElevation, Is.EqualTo(17f));
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 3, ++expectedSeq);
            Assert.That(controller.State.AirLayer, Is.EqualTo(3));

            AssertLabel(controller.Descend(), PlaceholderLabelKind.AirLayer, 2, ++expectedSeq);
            Assert.That(controller.State.TargetElevation, Is.EqualTo(11f));
            AssertLabel(controller.Descend(), PlaceholderLabelKind.AirLayer, 1, ++expectedSeq);
            Assert.That(controller.State.TargetElevation, Is.EqualTo(5f));
            AssertLabel(controller.Descend(), PlaceholderLabelKind.GroundFloor, 1, ++expectedSeq);
            Assert.That(controller.State.AirLayer, Is.EqualTo(0));
            Assert.That(controller.State.TargetElevation, Is.EqualTo(0f));

            // F over river at layer 1 is unchanged
            RunTicks(controller, 40);
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 1, ++expectedSeq);
            RunTicks(controller, 40);
            ClickAt45(controller, new Vec3(10.5f, 5f, 1.5f));
            RunUntilStationary(controller, 500);
            AssertLabel(controller.Descend(), PlaceholderLabelKind.AirLayer, 1, ++expectedSeq);
            Assert.That(controller.State.AirLayer, Is.EqualTo(1));
            Assert.That(controller.State.TargetElevation, Is.EqualTo(5f));

            // Over 9 m terrace (2, 10) at layer 2: F lands directly at 9 m (layer 1 fails: 5 < 9 + 1.5)
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 2, ++expectedSeq);
            RunTicks(controller, 50);
            ClickAt45(controller, new Vec3(2.5f, 11f, 10.5f));
            RunUntilStationary(controller, 500);
            AssertLabel(controller.Descend(), PlaceholderLabelKind.GroundFloor, 4, ++expectedSeq);
            Assert.That(controller.State.AirLayer, Is.EqualTo(0));
            Assert.That(controller.State.TargetElevation, Is.EqualTo(9f));
            RunTicks(controller, 20);
            Assert.That(controller.State.Elevation, Is.EqualTo(9f));

            // Over slope S1 (1.5, 4.5) at layer 1 -> F lands at HeightAt
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 2, ++expectedSeq);
            RunTicks(controller, 20);
            ClickAt45(controller, new Vec3(1.5f, 11f, 4.5f));
            RunUntilStationary(controller, 500);
            AssertLabel(controller.Descend(), PlaceholderLabelKind.AirLayer, 1, ++expectedSeq);
            RunTicks(controller, 50);
            float expectedSlopeH = tier12.HeightAt(1.5f, 4.5f);
            AssertLabel(controller.Descend(), PlaceholderLabelKind.GroundFloor, ValleyConstants.BandOf(expectedSlopeH), ++expectedSeq);
            Assert.That(controller.State.AirLayer, Is.EqualTo(0));
            Assert.That(controller.State.TargetElevation, Is.EqualTo(expectedSlopeH));
            RunTicks(controller, 40);
            Assert.That(controller.State.Elevation, Is.EqualTo(expectedSlopeH));

            // Over boulder on 3 m terrace (5, 5) at layer 2: F stays at layer 2 (layer 1 not clear, boulder not walkable)
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 1, ++expectedSeq);
            RunTicks(controller, 40);
            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 2, ++expectedSeq);
            RunTicks(controller, 50);
            ClickAt45(controller, new Vec3(5.5f, 11f, 5.5f));
            RunUntilStationary(controller, 500);
            AssertLabel(controller.Descend(), PlaceholderLabelKind.AirLayer, 2, ++expectedSeq);
            Assert.That(controller.State.AirLayer, Is.EqualTo(2));

            // F from layer 1 over 3 m terrace (4, 7), then R two ticks later -> returns to layer 1, not layer 2
            ClickAt45(controller, new Vec3(4.5f, 11f, 7.5f));
            RunUntilStationary(controller, 500);
            AssertLabel(controller.Descend(), PlaceholderLabelKind.AirLayer, 1, ++expectedSeq);
            RunTicks(controller, 50);
            Assert.That(controller.State.AirLayer, Is.EqualTo(1));
            Assert.That(controller.State.Elevation, Is.EqualTo(5f));

            AssertLabel(controller.Descend(), PlaceholderLabelKind.GroundFloor, 2, ++expectedSeq);
            Assert.That(controller.State.AirLayer, Is.EqualTo(0));
            RunTicks(controller, 2);
            Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.Descending));

            AssertLabel(controller.Ascend(), PlaceholderLabelKind.AirLayer, 1, ++expectedSeq);
            Assert.That(controller.State.AirLayer, Is.EqualTo(1));
            Assert.That(controller.State.TargetElevation, Is.EqualTo(5f));
            RunTicks(controller, 5);
            Assert.That(controller.State.Elevation, Is.EqualTo(5f));
        }

        // T-CORE-16
        [Test]
        public void T_CORE_16_AscendFromUpperSlopeAt4_5mTargetsLayer2()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            PlaceholderController controller = new PlaceholderController(tier12, ExplorationTuning.Default);

            // Fly to (7.5, 7.0) on upper slope S2 (HeightAt == 4.5 m) and land on it
            controller.Ascend();
            RunTicks(controller, 40);
            controller.Ascend();
            RunTicks(controller, 50);
            ClickAt45(controller, new Vec3(7.5f, 11f, 7.0f));
            RunUntilStationary(controller, 500);
            controller.Descend(); // layer 1 (5 m) fails because 5 < 4.5 + 1.5, so lands directly on slope at 4.5 m
            RunTicks(controller, 60);

            Assert.That(controller.State.AirLayer, Is.EqualTo(0));
            Assert.That(controller.State.Elevation, Is.EqualTo(4.5f).Within(1e-5f));

            PlaceholderLabel label = controller.Ascend();
            Assert.That(controller.State.AirLayer, Is.EqualTo(2));
            Assert.That(controller.State.TargetElevation, Is.EqualTo(11f));
            Assert.That(label.Kind, Is.EqualTo(PlaceholderLabelKind.AirLayer));
            Assert.That(label.Index, Is.EqualTo(2));
        }

        // T-CORE-17
        [Test]
        public void T_CORE_17_RapidReclickReplacesPathAndIncrementsMarkerSequence()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();
            PlaceholderController controller = new PlaceholderController(flat8, ExplorationTuning.Default);

            DestinationMarker m1 = ClickAt45(controller, new Vec3(6.5f, 0f, 1.5f));
            Assert.That(m1.Sequence, Is.EqualTo(1));

            RunTicks(controller, 10);
            Vec2 posAtReclick = controller.State.Position;
            Assert.That(posAtReclick.X, Is.GreaterThan(1.5f));
            Assert.That(posAtReclick.X, Is.LessThan(6.5f));

            DestinationMarker m2 = ClickAt45(controller, new Vec3(posAtReclick.X, 0f, 5.5f));
            Assert.That(m2.Sequence, Is.EqualTo(2));

            RunUntilStationary(controller, 500);
            Assert.That(controller.State.Position.X, Is.EqualTo(posAtReclick.X).Within(1e-5f));
            Assert.That(controller.State.Position.Y, Is.EqualTo(5.5f).Within(1e-5f));
        }

        // T-CORE-18
        [Test]
        public void T_CORE_18_AscendDuringWalkAndRapidDoubleAscend()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();
            PlaceholderController controller = new PlaceholderController(flat8, ExplorationTuning.Default);

            ClickAt45(controller, new Vec3(6.5f, 0f, 1.5f));
            RunTicks(controller, 6);
            Vec2 posBeforeAscend = controller.State.Position;

            controller.Ascend();
            Assert.That(controller.State.HasDestination, Is.False);
            Assert.That(controller.State.AirLayer, Is.EqualTo(1));
            Assert.That(controller.State.TargetElevation, Is.EqualTo(5f));
            Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.Ascending));

            float vStep = ExplorationTuning.Default.VerticalSpeed * ValleyConstants.TickSeconds;
            controller.Tick();
            Assert.That(controller.State.Position, Is.EqualTo(posBeforeAscend));
            Assert.That(controller.State.Elevation, Is.EqualTo(vStep).Within(1e-6f));

            controller.Tick();
            Assert.That(controller.State.Position, Is.EqualTo(posBeforeAscend));
            Assert.That(controller.State.Elevation, Is.EqualTo(2f * vStep).Within(1e-6f));

            // Second R within 2 ticks retargets to layer 2 without overshoot
            controller.Ascend();
            Assert.That(controller.State.AirLayer, Is.EqualTo(2));
            Assert.That(controller.State.TargetElevation, Is.EqualTo(11f));

            float maxElev = controller.State.Elevation;
            for (int i = 0; i < 120; i++)
            {
                float prev = controller.State.Elevation;
                controller.Tick();
                Assert.That(controller.State.Position, Is.EqualTo(posBeforeAscend));
                Assert.That(controller.State.Elevation - prev, Is.LessThanOrEqualTo(vStep + 1e-6f));
                if (controller.State.Elevation > maxElev)
                {
                    maxElev = controller.State.Elevation;
                }
            }
            Assert.That(maxElev, Is.EqualTo(11f));
            Assert.That(controller.State.Elevation, Is.EqualTo(11f));
            Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.AirborneStationary));
        }

        // T-CORE-19
        [Test]
        public void T_CORE_19_InterruptCancelsWalkAndPreservesAscent()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();

            // Interrupt during walk
            PlaceholderController walkCtrl = new PlaceholderController(flat8, ExplorationTuning.Default);
            ClickAt45(walkCtrl, new Vec3(6.5f, 0f, 1.5f));
            RunTicks(walkCtrl, 5);
            Vec2 posAtInterrupt = walkCtrl.State.Position;
            walkCtrl.Interrupt();
            Assert.That(walkCtrl.State.HasDestination, Is.False);
            Assert.That(walkCtrl.State.Motion, Is.EqualTo(PlaceholderMotion.GroundedStationary));
            RunTicks(walkCtrl, 30);
            Assert.That(walkCtrl.State.Position, Is.EqualTo(posAtInterrupt));

            // Interrupt during ascent
            PlaceholderController ascCtrl = new PlaceholderController(flat8, ExplorationTuning.Default);
            ascCtrl.Ascend();
            RunTicks(ascCtrl, 10);
            float elevAtInterrupt = ascCtrl.State.Elevation;
            ascCtrl.Interrupt();
            Assert.That(ascCtrl.State.HasDestination, Is.False);
            Assert.That(ascCtrl.State.Motion, Is.EqualTo(PlaceholderMotion.Ascending));
            Assert.That(ascCtrl.State.Elevation, Is.EqualTo(elevAtInterrupt));
            RunTicks(ascCtrl, 40);
            Assert.That(ascCtrl.State.Elevation, Is.EqualTo(5f));
            Assert.That(ascCtrl.State.Motion, Is.EqualTo(PlaceholderMotion.AirborneStationary));
        }

        // T-CORE-20 and T-CORE-21
        [Test]
        public void T_CORE_20_And_21_AuthoredDeterminismAndInvariantsOver3000Ticks()
        {
            ValleyLayout authored = ValleyLayoutData.CreateLayout();
            TickSnapshot[] runA = RunScriptedSession(authored, verifyInvariants: true);
            TickSnapshot[] runB = RunScriptedSession(authored, verifyInvariants: false);

            Assert.That(runA.Length, Is.EqualTo(3000));
            for (int i = 0; i < 3000; i++)
            {
                Assert.That(BitConverter.SingleToInt32Bits(runA[i].PosX), Is.EqualTo(BitConverter.SingleToInt32Bits(runB[i].PosX)), $"PosX tick {i}");
                Assert.That(BitConverter.SingleToInt32Bits(runA[i].PosZ), Is.EqualTo(BitConverter.SingleToInt32Bits(runB[i].PosZ)), $"PosZ tick {i}");
                Assert.That(BitConverter.SingleToInt32Bits(runA[i].Elev), Is.EqualTo(BitConverter.SingleToInt32Bits(runB[i].Elev)), $"Elev tick {i}");
                Assert.That(runA[i].AirLayer, Is.EqualTo(runB[i].AirLayer), $"AirLayer tick {i}");
                Assert.That(runA[i].Motion, Is.EqualTo(runB[i].Motion), $"Motion tick {i}");
            }
        }

        [Test]
        public void CapHitMarksUnreachableAtCapPoint()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            PlaceholderController controller = new PlaceholderController(tier12, ExplorationTuning.Default);
            // Cut at floor 1 (2.98 m) and click the 3 m terrace -> ray hits Cap at y = 2.98
            DestinationMarker marker = ClickAt45(controller, new Vec3(4.5f, 3f, 7.5f), ValleyConstants.CutYForFloor(1));
            Assert.That(marker.Kind, Is.EqualTo(MarkerKind.Unreachable));
            Assert.That(marker.Point.Y, Is.EqualTo(ValleyConstants.CutYForFloor(1)).Within(1e-5f));
        }

        [Test]
        public void ClickDuringLandingWaitsUntilTouchdownThenWalks()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();
            PlaceholderController controller = new PlaceholderController(flat8, ExplorationTuning.Default);
            controller.Ascend();
            RunTicks(controller, 40);
            controller.Descend();
            RunTicks(controller, 5);
            Assert.That(controller.State.AirLayer, Is.EqualTo(0));
            Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.Descending));

            Vec2 posBefore = controller.State.Position;
            DestinationMarker marker = ClickAt45(controller, new Vec3(4.5f, 0f, 1.5f));
            Assert.That(marker.Kind, Is.EqualTo(MarkerKind.Reachable));
            Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.Descending));

            // Still descending on next tick: horizontal position stays fixed
            controller.Tick();
            Assert.That(controller.State.Position, Is.EqualTo(posBefore));

            RunUntilStationary(controller, 300);
            Assert.That(controller.State.Position.X, Is.EqualTo(4.5f).Within(1e-5f));
            Assert.That(controller.State.Position.Y, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(controller.State.Elevation, Is.EqualTo(0f));
        }

        private static void AssertBlockedClickStopsOnFlatWalkable(ValleyLayout layout, Vec3 clickTarget)
        {
            PlaceholderController controller = new PlaceholderController(layout, ExplorationTuning.Default);
            DestinationMarker marker = ClickAt45(controller, clickTarget);
            Assert.That(marker.Kind, Is.EqualTo(MarkerKind.Unreachable));
            RunToStop(controller, layout);
            int cx = (int)MathF.Floor(controller.State.Position.X);
            int cz = (int)MathF.Floor(controller.State.Position.Y);
            Assert.That(layout.IsWalkable(cx, cz), Is.True);
            Assert.That(layout.IsFlatFloor(cx, cz, out int floor), Is.True);
            Assert.That(floor, Is.EqualTo(1));
        }

        private static void AssertOnFlatZeroBeforeCliff(PlaceholderController controller, ValleyLayout layout, Vec3 clickTarget)
        {
            Vec2 pos = controller.State.Position;
            int cx = (int)MathF.Floor(pos.X);
            int cz = (int)MathF.Floor(pos.Y);
            Assert.That(layout.IsFlatFloor(cx, cz, out int floor), Is.True);
            Assert.That(floor, Is.EqualTo(1));
            Assert.That(ValleyKindMetadata.IsSlopeKind(layout.KindAt(cx, cz)), Is.False);
            Assert.That(controller.State.Elevation, Is.EqualTo(0f));

            // At least one neighboring cell edge is non-walkable and inset >= 0.3 m
            bool bordersBlocked = !layout.IsWalkable(cx, cz + 1)
                || !layout.IsWalkable(cx + 1, cz)
                || !layout.IsWalkable(cx - 1, cz)
                || !layout.IsWalkable(cx, cz - 1);
            Assert.That(bordersBlocked, Is.True);
            if (!layout.IsWalkable(cx, cz + 1))
            {
                Assert.That(pos.Y, Is.LessThanOrEqualTo(cz + 0.7f + 1e-5f));
            }
            if (!layout.IsWalkable(cx + 1, cz))
            {
                Assert.That(pos.X, Is.LessThanOrEqualTo(cx + 0.7f + 1e-5f));
            }

            // Clicking again from there leaves it in place
            ClickAt45(controller, clickTarget);
            RunToStop(controller, layout);
            Assert.That(controller.State.Position, Is.EqualTo(pos));
        }

        private static void RunToStop(PlaceholderController controller, ValleyLayout layout)
        {
            int safety = 0;
            while (controller.State.Motion != PlaceholderMotion.GroundedStationary && safety++ < 1000)
            {
                controller.Tick();
                int cx = (int)MathF.Floor(controller.State.Position.X);
                int cz = (int)MathF.Floor(controller.State.Position.Y);
                Assert.That(layout.IsWalkable(cx, cz), Is.True, $"Tick {safety} entered blocked cell ({cx}, {cz})");
            }
            Assert.That(controller.State.Motion, Is.EqualTo(PlaceholderMotion.GroundedStationary));
        }

        private static int RunUntilStationary(PlaceholderController controller, int maxTicks)
        {
            for (int t = 1; t <= maxTicks; t++)
            {
                controller.Tick();
                if (controller.State.Motion == PlaceholderMotion.AirborneStationary
                    || controller.State.Motion == PlaceholderMotion.GroundedStationary)
                {
                    return t;
                }
            }
            Assert.Fail("Controller did not reach stationary state.");
            return maxTicks;
        }

        private static void RunTicks(PlaceholderController controller, int count)
        {
            for (int i = 0; i < count; i++)
            {
                controller.Tick();
            }
        }

        private static void AssertLabel(PlaceholderLabel label, PlaceholderLabelKind kind, int index, int sequence)
        {
            Assert.That(label.Kind, Is.EqualTo(kind));
            Assert.That(label.Index, Is.EqualTo(index));
            Assert.That(label.Sequence, Is.EqualTo(sequence));
        }

        private static TickSnapshot[] RunScriptedSession(ValleyLayout authored, bool verifyInvariants)
        {
            PlaceholderController controller = new PlaceholderController(authored, ExplorationTuning.Default);
            ScriptedCommand[] commands = Build40Commands();
            Assert.That(commands.Length, Is.EqualTo(40));

            TickSnapshot[] snapshots = new TickSnapshot[3000];
            float maxVerticalStep = ExplorationTuning.Default.VerticalSpeed * ValleyConstants.TickSeconds + 1e-6f;
            int cmdIdx = 0;

            for (int tick = 0; tick < 3000; tick++)
            {
                int prevLayer = controller.State.AirLayer;
                float prevElev = controller.State.Elevation;
                bool commandIssuedLayerInput = false;

                while (cmdIdx < commands.Length && commands[cmdIdx].Tick == tick)
                {
                    ScriptedCommand cmd = commands[cmdIdx++];
                    if (cmd.Kind == ScriptedCommandKind.Ascend || cmd.Kind == ScriptedCommandKind.Descend)
                    {
                        commandIssuedLayerInput = true;
                    }
                    ExecuteCommand(controller, cmd);
                }

                if (verifyInvariants && !commandIssuedLayerInput)
                {
                    Assert.That(controller.State.AirLayer, Is.EqualTo(prevLayer), $"AirLayer changed without R/F at tick {tick}");
                }

                controller.Tick();
                PlaceholderState s = controller.State;

                if (verifyInvariants)
                {
                    bool onFootNotLanding = s.AirLayer == 0 && s.Motion != PlaceholderMotion.Descending;
                    if (onFootNotLanding)
                    {
                        Assert.That(s.Elevation, Is.EqualTo(authored.HeightAt(s.Position.X, s.Position.Y)).Within(1e-6f), $"Tick {tick}");
                    }
                    else
                    {
                        Assert.That(MathF.Abs(s.Elevation - prevElev), Is.LessThanOrEqualTo(maxVerticalStep), $"Vertical delta exceeded at tick {tick}");
                    }
                }

                snapshots[tick] = new TickSnapshot(s.Position.X, s.Position.Y, s.Elevation, s.AirLayer, s.Motion);
            }
            return snapshots;
        }

        private static void ExecuteCommand(PlaceholderController controller, ScriptedCommand cmd)
        {
            switch (cmd.Kind)
            {
                case ScriptedCommandKind.Click:
                    ClickAt45(controller, cmd.Target, cmd.CutY);
                    break;
                case ScriptedCommandKind.Ascend:
                    controller.Ascend();
                    break;
                case ScriptedCommandKind.Descend:
                    controller.Descend();
                    break;
                case ScriptedCommandKind.Interrupt:
                    controller.Interrupt();
                    break;
            }
        }

        private static ScriptedCommand[] Build40Commands()
        {
            float cut1 = ValleyConstants.CutYForFloor(1);
            float cut2 = ValleyConstants.CutYForFloor(2);
            float full = ValleyConstants.FullViewCutY;
            return new[]
            {
                ScriptedCommand.ClickCmd(0, new Vec3(56f, 0f, 36f), full),
                ScriptedCommand.ClickCmd(80, new Vec3(56f, 3f, 50f), full),
                ScriptedCommand.ClickCmd(240, new Vec3(83f, 3f, 55f), full),
                ScriptedCommand.ClickCmd(420, new Vec3(83f, 6f, 68f), full),
                ScriptedCommand.ClickCmd(600, new Vec3(43f, 6f, 70f), full),
                ScriptedCommand.ClickCmd(750, new Vec3(43f, 9f, 80f), full),
                ScriptedCommand.ClickCmd(900, new Vec3(100f, 9f, 80f), full),
                ScriptedCommand.ClickCmd(960, new Vec3(110f, 0f, 5f), full),
                ScriptedCommand.ClickCmd(1020, new Vec3(60f, 6f, 65f), cut2),
                ScriptedCommand.InterruptCmd(1050),
                ScriptedCommand.ClickCmd(1060, new Vec3(90f, 9f, 79f), full),
                ScriptedCommand.AscendCmd(1120),
                ScriptedCommand.ClickCmd(1125, new Vec3(66f, 11f, 86f), full),
                ScriptedCommand.AscendCmd(1200),
                ScriptedCommand.ClickCmd(1250, new Vec3(66f, 17f, 87f), full),
                ScriptedCommand.DescendCmd(1400),
                ScriptedCommand.DescendCmd(1450),
                ScriptedCommand.DescendCmd(1500),
                ScriptedCommand.ClickCmd(1560, new Vec3(70f, 9f, 87f), full),
                ScriptedCommand.AscendCmd(1600),
                ScriptedCommand.AscendCmd(1602),
                ScriptedCommand.InterruptCmd(1620),
                ScriptedCommand.ClickCmd(1660, new Vec3(33f, 11f, 53f), full),
                ScriptedCommand.DescendCmd(1800),
                ScriptedCommand.DescendCmd(1860),
                ScriptedCommand.ClickCmd(1920, new Vec3(35f, 6f, 52f), full),
                ScriptedCommand.AscendCmd(1960),
                ScriptedCommand.ClickCmd(2000, new Vec3(70f, 11f, 8f), full),
                ScriptedCommand.DescendCmd(2200),
                ScriptedCommand.DescendCmd(2220),
                ScriptedCommand.ClickCmd(2260, new Vec3(70f, 5f, 28f), full),
                ScriptedCommand.DescendCmd(2400),
                ScriptedCommand.ClickCmd(2460, new Vec3(60f, 0f, 28f), full),
                ScriptedCommand.ClickCmd(2520, new Vec3(56f, 1.5f, 43f), full),
                ScriptedCommand.AscendCmd(2620),
                ScriptedCommand.DescendCmd(2640),
                ScriptedCommand.ClickCmd(2700, new Vec3(75f, 0f, 20f), cut1),
                ScriptedCommand.ClickCmd(2760, new Vec3(75f, 0f, 20f), full),
                ScriptedCommand.AscendCmd(2840),
                ScriptedCommand.DescendCmd(2842)
            };
        }

        private enum ScriptedCommandKind : byte
        {
            Click,
            Ascend,
            Descend,
            Interrupt
        }

        private readonly struct ScriptedCommand
        {
            public readonly int Tick;
            public readonly ScriptedCommandKind Kind;
            public readonly Vec3 Target;
            public readonly float CutY;

            private ScriptedCommand(int tick, ScriptedCommandKind kind, Vec3 target, float cutY)
            {
                Tick = tick;
                Kind = kind;
                Target = target;
                CutY = cutY;
            }

            public static ScriptedCommand ClickCmd(int tick, Vec3 target, float cutY) => new ScriptedCommand(tick, ScriptedCommandKind.Click, target, cutY);
            public static ScriptedCommand AscendCmd(int tick) => new ScriptedCommand(tick, ScriptedCommandKind.Ascend, Vec3.Zero, 0f);
            public static ScriptedCommand DescendCmd(int tick) => new ScriptedCommand(tick, ScriptedCommandKind.Descend, Vec3.Zero, 0f);
            public static ScriptedCommand InterruptCmd(int tick) => new ScriptedCommand(tick, ScriptedCommandKind.Interrupt, Vec3.Zero, 0f);
        }

        private readonly struct TickSnapshot
        {
            public readonly float PosX;
            public readonly float PosZ;
            public readonly float Elev;
            public readonly int AirLayer;
            public readonly PlaceholderMotion Motion;

            public TickSnapshot(float posX, float posZ, float elev, int airLayer, PlaceholderMotion motion)
            {
                PosX = posX;
                PosZ = posZ;
                Elev = elev;
                AirLayer = airLayer;
                Motion = motion;
            }
        }
    }
}
