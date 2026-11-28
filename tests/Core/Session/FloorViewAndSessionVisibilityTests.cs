using Murim.Core.Exploration.Placeholder;
using Murim.Core.Exploration.Session;
using Murim.Core.Tests.Valley;
using Murim.Core.Tuning;
using Murim.Core.Valley;
using NUnit.Framework;

namespace Murim.Core.Tests.Session
{
    [TestFixture]
    public sealed class FloorViewAndSessionVisibilityTests
    {
        private static readonly Vec3 CameraRayDir = new Vec3(0f, -ValleyConstants.Sin45, ValleyConstants.Cos45);

        private static DestinationMarker ClickAt45(ExplorationSession session, Vec3 target)
        {
            Vec3 origin = target - CameraRayDir * 30f;
            return session.Click(origin, CameraRayDir);
        }

        // T-CORE-22
        [Test]
        public void T_CORE_22_FloorSteppingDownAndUpEmitsTenLabelsAndExactCutY()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();
            ExplorationSession session = new ExplorationSession(flat8, ExplorationTuning.Default);

            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            Assert.That(session.FloorView.CutY, Is.EqualTo(float.PositiveInfinity));
            Assert.That(session.LabelVisible, Is.False);

            int[] expectedDownFloors = { 4, 3, 2, 1, 1 };
            float[] expectedDownCutY = { 11.98f, 8.98f, 5.98f, 2.98f, 2.98f };
            for (int i = 0; i < expectedDownFloors.Length; i++)
            {
                FloorLabel label = session.StepFloorDown();
                Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(expectedDownFloors[i]));
                Assert.That(session.FloorView.CutY, Is.EqualTo(expectedDownCutY[i]).Within(1e-5f));
                Assert.That(label.Kind, Is.EqualTo(FloorLabelKind.GroundFloor));
                Assert.That(label.Index, Is.EqualTo(expectedDownFloors[i]));
                Assert.That(label.Sequence, Is.EqualTo(i + 1));
                Assert.That(session.LabelVisible, Is.True);
            }

            int[] expectedUpFloors = { 2, 3, 4, 0, 0 };
            float[] expectedUpCutY = { 5.98f, 8.98f, 11.98f, float.PositiveInfinity, float.PositiveInfinity };
            for (int i = 0; i < expectedUpFloors.Length; i++)
            {
                FloorLabel label = session.StepFloorUp();
                int expectedFloor = expectedUpFloors[i];
                Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(expectedFloor));
                if (expectedFloor == 0)
                {
                    Assert.That(session.FloorView.CutY, Is.EqualTo(float.PositiveInfinity));
                    Assert.That(label.Kind, Is.EqualTo(FloorLabelKind.FullView));
                }
                else
                {
                    Assert.That(session.FloorView.CutY, Is.EqualTo(expectedUpCutY[i]).Within(1e-5f));
                    Assert.That(label.Kind, Is.EqualTo(FloorLabelKind.GroundFloor));
                }
                Assert.That(label.Index, Is.EqualTo(expectedFloor));
                Assert.That(label.Sequence, Is.EqualTo(6 + i));
            }
        }

        // T-CORE-23
        [Test]
        public void T_CORE_23_VisibilityRulesForSpaceAscendDescendAndSlopeWalk()
        {
            VerifyFullViewAndSpaceAndAscendEnsureVisible();
            VerifyDescendNeverLowersAndLandingClickProjectsToLandingFloor();
            VerifySlopeWalkRaisesViewWithoutLabelAndHiddenStaysHidden();
        }

        private static void VerifyFullViewAndSpaceAndAscendEnsureVisible()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            // In Full View, R, F, and Space leave Full View.
            session.Ascend();
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            session.Descend();
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            session.CenterOnPlaceholder(CameraModel.DefaultAspect);
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));

            // Space with placeholder in band 3 (6 m) and view 1 gives view 3.
            session.Placeholder.Position = new Vec2(5.5f, 10.5f);
            session.Placeholder.Elevation = 6f;
            session.Placeholder.TargetElevation = 6f;
            session.Placeholder.LogicalHeight = 6f;
            SetViewedFloor(session, 1);
            session.CenterOnPlaceholder(CameraModel.DefaultAspect);
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(3));

            // Reset to 0 m ground, view 1: R to layer 1 -> view 2 on press tick; R to layer 2 -> view 4; R to layer 3 -> Full View.
            ExplorationSession s2 = new ExplorationSession(tier12, ExplorationTuning.Default);
            SetViewedFloor(s2, 1);
            s2.Ascend();
            Assert.That(s2.Placeholder.AirLayer, Is.EqualTo(1));
            Assert.That(s2.FloorView.ViewedFloor, Is.EqualTo(2));

            s2.Ascend();
            Assert.That(s2.Placeholder.AirLayer, Is.EqualTo(2));
            Assert.That(s2.FloorView.ViewedFloor, Is.EqualTo(4));

            s2.Ascend();
            Assert.That(s2.Placeholder.AirLayer, Is.EqualTo(3));
            Assert.That(s2.FloorView.ViewedFloor, Is.EqualTo(0));
        }

        private static void VerifyDescendNeverLowersAndLandingClickProjectsToLandingFloor()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            // Place airborne at layer 2 over 3 m terrace (4.5, 7.5), set view to 4 -> F to layer 1 keeps view 4.
            session.Placeholder.Position = new Vec2(4.5f, 7.5f);
            session.Ascend(); // layer 1 (5 m)
            session.Ascend(); // layer 2 (11 m)
            SetViewedFloor(session, 4);

            session.Descend(); // layer 1 (5 m)
            Assert.That(session.Placeholder.AirLayer, Is.EqualTo(1));
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(4));

            // Set view to 3, descend to land on 3 m terrace -> view stays 3 (F never lowers view).
            SetViewedFloor(session, 3);
            session.Placeholder.Elevation = 5f;
            session.Descend();
            Assert.That(session.Placeholder.AirLayer, Is.EqualTo(0));
            Assert.That(session.Placeholder.LogicalBand, Is.EqualTo(2));
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(3));

            // Blocked click during landing (elevation still descending > 3 m) projects onto landing floor (floor 2 = 3 m).
            Assert.That(session.Placeholder.Motion, Is.EqualTo(PlaceholderMotion.Descending));
            DestinationMarker marker = ClickAt45(session, new Vec3(0.5f, 3f, 7.5f));
            Assert.That(marker.Kind, Is.EqualTo(MarkerKind.Unreachable));
            Assert.That(session.Placeholder.HasDestination, Is.True);
            int destCx = (int)System.MathF.Floor(session.Placeholder.Destination.X);
            int destCz = (int)System.MathF.Floor(session.Placeholder.Destination.Y);
            Assert.That(tier12.IsFlatFloor(destCx, destCz, out int destFloor), Is.True);
            Assert.That(destFloor, Is.EqualTo(2));
            Assert.That(tier12.HeightAt(session.Placeholder.Destination.X, session.Placeholder.Destination.Y), Is.EqualTo(3f));
        }

        private static void VerifySlopeWalkRaisesViewWithoutLabelAndHiddenStaysHidden()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            // Start walk up Slope 1 to 3 m terrace in Full View, then step down to floor 1 before reaching 3 m.
            DestinationMarker m1 = ClickAt45(session, new Vec3(4.5f, 3f, 7.5f));
            Assert.That(m1.Kind, Is.EqualTo(MarkerKind.Reachable));
            SetViewedFloor(session, 1);
            int seqBeforeWalk = session.Label.Sequence;

            int raisedAtTick = -1;
            for (int tick = 1; tick <= 600; tick++)
            {
                int bandBefore = session.Placeholder.LogicalBand;
                session.Tick();
                if (bandBefore == 1 && session.Placeholder.LogicalBand == 2)
                {
                    raisedAtTick = tick;
                    Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(2));
                    Assert.That(session.Label.Sequence, Is.EqualTo(seqBeforeWalk));
                    break;
                }
            }
            Assert.That(raisedAtTick, Is.GreaterThan(0));

            // Now move to 3 m terrace, step view down to 1 (placeholder hidden), and walk up Slope 2 to 6 m.
            for (int i = 0; i < 600 && session.Placeholder.Motion != PlaceholderMotion.GroundedStationary; i++)
            {
                session.Tick();
            }

            session.StepFloorUp(); // ensure Full View so click on 6 m terrace hits Ground
            session.StepFloorUp();
            DestinationMarker m2 = ClickAt45(session, new Vec3(5.5f, 6f, 10.5f));
            Assert.That(m2.Kind, Is.EqualTo(MarkerKind.Reachable));
            SetViewedFloor(session, 1); // placeholder is at band 2 > 1, already hidden

            for (int tick = 1; tick <= 800; tick++)
            {
                session.Tick();
                Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(1));
            }
            Assert.That(session.Placeholder.LogicalBand, Is.EqualTo(3));
        }

        private static void SetViewedFloor(ExplorationSession session, int targetFloor)
        {
            while (session.FloorView.ViewedFloor != targetFloor)
            {
                if (session.FloorView.ViewedFloor == 0 || session.FloorView.ViewedFloor > targetFloor)
                {
                    session.StepFloorDown();
                }
                else
                {
                    session.StepFloorUp();
                }
            }
        }
    }
}
