using Murim.Core.Exploration.Placeholder;
using Murim.Core.Exploration.Session;
using Murim.Core.Tests.Valley;
using Murim.Core.Tuning;
using Murim.Core.Valley;
using NUnit.Framework;

namespace Murim.Core.Tests.Session
{
    [TestFixture]
    public sealed class ExplorationSessionControlTests
    {
        private static readonly Vec3 CameraRayDir = new Vec3(0f, -ValleyConstants.Sin45, ValleyConstants.Cos45);

        private static DestinationMarker ClickAt45(ExplorationSession session, Vec3 target)
        {
            Vec3 origin = target - CameraRayDir * 30f;
            return session.Click(origin, CameraRayDir);
        }

        // T-CORE-29
        [Test]
        public void T_CORE_29_PauseMenuInterruptsWalkHoldsAscentAndResetsViewOnMenuAction()
        {
            VerifyMenuMidWalkAndEveryInputIgnored();
            VerifyMenuMidAscentAndResetViewFromMenu();
        }

        // T-CORE-30
        [Test]
        public void T_CORE_30_FocusLossCancelsMoveEndsDragDisarmsKeysHoldsAscentKeepsAmbient()
        {
            VerifyFocusLossMidWalkWithPanAndDrag();
            VerifyFocusLossMidAscent();
        }

        // T-CORE-31
        [Test]
        public void T_CORE_31_LabelsOnlyForSixDesignatedKeysAndMonotonicSequence()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            Assert.That(session.LabelVisible, Is.False);
            Assert.That(session.Label.Sequence, Is.EqualTo(0));

            // Non-label inputs must not emit any label.
            session.PanKeys(new PanInput(0, 1, 0, 0), 0.05f, CameraModel.DefaultAspect);
            session.BeginDrag();
            session.Drag(10f, 10f, 1080f, CameraModel.DefaultAspect);
            session.EndDrag();
            session.ZoomNotch(1, CameraModel.DefaultAspect);
            session.CenterOnPlaceholder(CameraModel.DefaultAspect);
            session.ResetView(CameraModel.DefaultAspect);
            ClickAt45(session, new Vec3(5.5f, 0f, 1.5f));
            session.Tick();
            Assert.That(session.LabelVisible, Is.False);
            Assert.That(session.Label.Sequence, Is.EqualTo(0));

            // Six designated keys: PageDown, [, PageUp, ], R, F.
            FloorLabel l1 = session.StepFloorDown(); // PageDown -> GroundFloor 4
            AssertLabel(l1, FloorLabelKind.GroundFloor, 4, 1);

            FloorLabel l2 = session.StepFloorDown(); // [ -> GroundFloor 3
            AssertLabel(l2, FloorLabelKind.GroundFloor, 3, 2);

            FloorLabel l3 = session.StepFloorUp(); // PageUp -> GroundFloor 4
            AssertLabel(l3, FloorLabelKind.GroundFloor, 4, 3);

            FloorLabel l4 = session.StepFloorUp(); // ] -> FullView 0
            AssertLabel(l4, FloorLabelKind.FullView, 0, 4);

            FloorLabel l5 = session.Ascend(); // R -> AirLayer 1
            AssertLabel(l5, FloorLabelKind.AirLayer, 1, 5);

            FloorLabel l6 = session.Descend(); // F -> GroundFloor 1
            AssertLabel(l6, FloorLabelKind.GroundFloor, 1, 6);
        }

        private static void VerifyMenuMidWalkAndEveryInputIgnored()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            ClickAt45(session, new Vec3(7.5f, 0f, 1.5f));
            session.Tick();
            session.StepFloorDown();
            Assert.That(session.LabelVisible, Is.True);
            Assert.That(session.MarkerVisible, Is.True);

            Vec2 posBeforePause = session.Placeholder.Position;
            Vec2 focusBeforePause = session.Camera.Focus;
            float orthoBeforePause = session.Camera.OrthoSize;
            int floorBeforePause = session.FloorView.ViewedFloor;

            session.OpenMenu();
            Assert.That(session.Paused, Is.True);
            Assert.That(session.AmbientRunning, Is.False);
            Assert.That(session.LabelVisible, Is.False);
            Assert.That(session.MarkerVisible, Is.False);
            Assert.That(session.Label.Sequence, Is.EqualTo(0));
            Assert.That(session.Marker.Sequence, Is.EqualTo(0));
            Assert.That(session.Placeholder.HasDestination, Is.False);

            SendAllExplorationInputsWhileInactive(session);

            Assert.That(session.Placeholder.Position, Is.EqualTo(posBeforePause));
            Assert.That(session.Camera.Focus, Is.EqualTo(focusBeforePause));
            Assert.That(session.Camera.OrthoSize, Is.EqualTo(orthoBeforePause));
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(floorBeforePause));
            Assert.That(session.LabelVisible, Is.False);
            Assert.That(session.MarkerVisible, Is.False);

            session.Resume();
            Assert.That(session.Paused, Is.False);
            Assert.That(session.AmbientRunning, Is.True);
            for (int i = 0; i < 10; i++)
            {
                session.Tick();
            }
            Assert.That(session.Placeholder.Position, Is.EqualTo(posBeforePause));
            Assert.That(session.Placeholder.Motion, Is.EqualTo(PlaceholderMotion.GroundedStationary));
        }

        private static void VerifyMenuMidAscentAndResetViewFromMenu()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            session.Ascend();
            session.Tick();
            session.Tick();
            float elevAtPause = session.Placeholder.Elevation;
            Assert.That(elevAtPause, Is.GreaterThan(0f));
            Assert.That(elevAtPause, Is.LessThan(5f));

            session.OpenMenu();
            SendAllExplorationInputsWhileInactive(session);
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(elevAtPause));

            session.Resume();
            for (int i = 0; i < 60; i++)
            {
                session.Tick();
            }
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(5f));
            Assert.That(session.Placeholder.Motion, Is.EqualTo(PlaceholderMotion.AirborneStationary));

            // Repeat with ResetViewFromMenu after moving camera & viewed floor mid-ascent.
            session.Ascend(); // to layer 2 (11 m)
            session.Tick();
            session.ZoomNotch(1, CameraModel.DefaultAspect);
            session.PanKeys(new PanInput(0, 1, 0, 0), 0.1f, CameraModel.DefaultAspect);
            session.StepFloorDown();
            float elevBeforeResetMenu = session.Placeholder.Elevation;
            Vec2 posBeforeResetMenu = session.Placeholder.Position;

            session.OpenMenu();
            session.ResetViewFromMenu(CameraModel.DefaultAspect);

            Assert.That(session.Paused, Is.False);
            Assert.That(session.AmbientRunning, Is.True);
            Assert.That(session.Camera.Focus, Is.EqualTo(new Vec2(70f, 29f)));
            Assert.That(session.Camera.OrthoSize, Is.EqualTo(11.25f));
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            Assert.That(session.Placeholder.Position, Is.EqualTo(posBeforeResetMenu));
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(elevBeforeResetMenu));
            Assert.That(session.Placeholder.AirLayer, Is.EqualTo(2));

            for (int i = 0; i < 60; i++)
            {
                session.Tick();
            }
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(11f));
        }

        private static void VerifyFocusLossMidWalkWithPanAndDrag()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            ClickAt45(session, new Vec3(7.5f, 0f, 1.5f));
            session.Tick();
            session.PanKeys(new PanInput(1, 0, 0, 0), 0.05f, CameraModel.DefaultAspect);
            session.BeginDrag();

            Vec2 posBeforeUnfocus = session.Placeholder.Position;
            Vec2 focusBeforeUnfocus = session.Camera.Focus;

            session.SetFocused(false);
            Assert.That(session.Focused, Is.False);
            Assert.That(session.AmbientRunning, Is.True);
            Assert.That(session.Camera.IsDragging, Is.False);
            Assert.That(session.Placeholder.HasDestination, Is.False);

            for (int i = 0; i < 120; i++)
            {
                session.Tick();
            }
            session.Drag(50f, 50f, 1080f, CameraModel.DefaultAspect);
            session.PanKeys(new PanInput(1, 0, 0, 0), 0.05f, CameraModel.DefaultAspect);

            Assert.That(session.Placeholder.Position, Is.EqualTo(posBeforeUnfocus));
            Assert.That(session.Camera.Focus, Is.EqualTo(focusBeforeUnfocus));
            Assert.That(session.AmbientRunning, Is.True);

            session.SetFocused(true);
            session.Tick();
            Assert.That(session.Placeholder.Position, Is.EqualTo(posBeforeUnfocus));

            // Held left key must NOT pan until a fresh key-down edge; drag must NOT move without BeginDrag.
            session.Drag(50f, 50f, 1080f, CameraModel.DefaultAspect);
            session.PanKeys(new PanInput(1, 0, 0, 0), 0.05f, CameraModel.DefaultAspect);
            Assert.That(session.Camera.Focus, Is.EqualTo(focusBeforeUnfocus));

            // Release key, then fresh key-down edge pans.
            session.PanKeys(new PanInput(0, 0, 0, 0), 0.05f, CameraModel.DefaultAspect);
            session.PanKeys(new PanInput(1, 0, 0, 0), 0.05f, CameraModel.DefaultAspect);
            Assert.That(session.Camera.Focus.X, Is.LessThan(focusBeforeUnfocus.X));
        }

        private static void VerifyFocusLossMidAscent()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            session.Ascend();
            session.Tick();
            session.Tick();
            float elevBeforeUnfocus = session.Placeholder.Elevation;

            session.SetFocused(false);
            for (int i = 0; i < 120; i++)
            {
                session.Tick();
            }
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(elevBeforeUnfocus));
            Assert.That(session.AmbientRunning, Is.True);

            session.SetFocused(true);
            for (int i = 0; i < 60; i++)
            {
                session.Tick();
            }
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(5f));
            Assert.That(session.Placeholder.Motion, Is.EqualTo(PlaceholderMotion.AirborneStationary));
        }

        private static void SendAllExplorationInputsWhileInactive(ExplorationSession session)
        {
            ClickAt45(session, new Vec3(6.5f, 0f, 1.5f));
            session.Ascend();
            session.Descend();
            session.StepFloorUp();
            session.StepFloorDown();
            session.CenterOnPlaceholder(CameraModel.DefaultAspect);
            session.ResetView(CameraModel.DefaultAspect);
            session.PanKeys(new PanInput(0, 1, 1, 0), 0.1f, CameraModel.DefaultAspect);
            session.BeginDrag();
            session.Drag(100f, 50f, 1080f, CameraModel.DefaultAspect);
            session.ZoomNotch(1, CameraModel.DefaultAspect);
            session.Tick();
        }

        private static void AssertLabel(FloorLabel label, FloorLabelKind kind, int index, int sequence)
        {
            Assert.That(label.Kind, Is.EqualTo(kind));
            Assert.That(label.Index, Is.EqualTo(index));
            Assert.That(label.Sequence, Is.EqualTo(sequence));
        }
    }
}
