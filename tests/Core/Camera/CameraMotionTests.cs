using System;
using Murim.Core.Camera;
using NUnit.Framework;

namespace Murim.Core.Tests.Camera
{
    [TestFixture]
    public class CameraMotionTests
    {
        private const float Dt60Hz = 1f / 60f;
        private const float Aspect16x9 = 16f / 9f;
        private static readonly ViewFrame SyntheticFrame = new ViewFrame(320, 34800, 25600, 14000);

        // T-CORE-21
        [Test]
        public void T_CORE_21_KeyboardPanSpeedDiagonalCancelAndReleaseDecay()
        {
            float[] heights = { 12f, 44f, 88f };
            foreach (float h in heights)
            {
                CameraRig rig = CreateRigAtHeight(h);
                float expectedSpeed = 0.9f * h;

                // Hold East for 2 s (120 ticks at 60 Hz).
                for (int i = 0; i < 120; i++)
                {
                    CameraInput east = BaseInput();
                    east.PanEast = true;
                    rig.Tick(in east, Dt60Hz);
                }

                Assert.That(rig.Vu, Is.EqualTo(expectedSpeed).Within(expectedSpeed * 0.01f));
                Assert.That(rig.Vv, Is.EqualTo(0f));

                // Release East and measure coasting distance, monotonic speed decrease, and no sign reversal.
                float releaseSpeed = rig.Vu;
                float releaseCu = rig.Cu;
                float prevSpeed = releaseSpeed;

                for (int i = 0; i < 180; i++)
                {
                    CameraInput idle = BaseInput();
                    rig.Tick(in idle, Dt60Hz);
                    Assert.That(rig.Vu, Is.GreaterThanOrEqualTo(0f));
                    Assert.That(rig.Vu, Is.LessThanOrEqualTo(prevSpeed + 1e-6f));
                    prevSpeed = rig.Vu;
                }

                float travel = rig.Cu - releaseCu;
                float expectedTravel = 0.22f * releaseSpeed;
                Assert.That(travel, Is.EqualTo(expectedTravel).Within(expectedTravel * 0.05f));
                Assert.That(rig.Vu, Is.EqualTo(0f));
            }

            // Diagonal speed matches cardinal with equal components.
            CameraRig diagRig = CreateRigAtHeight(44f);
            for (int i = 0; i < 120; i++)
            {
                CameraInput ne = BaseInput();
                ne.PanNorth = true;
                ne.PanEast = true;
                diagRig.Tick(in ne, Dt60Hz);
            }

            float diagSpeed = MathF.Sqrt(diagRig.Vu * diagRig.Vu + diagRig.Vv * diagRig.Vv);
            Assert.That(diagSpeed, Is.EqualTo(0.9f * 44f).Within(0.9f * 44f * 0.01f));
            Assert.That(diagRig.Vu, Is.EqualTo(diagRig.Vv).Within(1e-4f));

            // Opposite keys East+West with North gives Vu = 0 exactly while Vv matches North alone.
            CameraRig cancelRig = CreateRigAtHeight(44f);
            CameraRig northOnlyRig = CreateRigAtHeight(44f);
            for (int i = 0; i < 120; i++)
            {
                CameraInput ewNorth = BaseInput();
                ewNorth.PanEast = true;
                ewNorth.PanWest = true;
                ewNorth.PanNorth = true;
                cancelRig.Tick(in ewNorth, Dt60Hz);

                CameraInput northOnly = BaseInput();
                northOnly.PanNorth = true;
                northOnlyRig.Tick(in northOnly, Dt60Hz);

                Assert.That(cancelRig.Vu, Is.EqualTo(0f));
                Assert.That(cancelRig.Vv, Is.EqualTo(northOnlyRig.Vv).Within(1e-5f));
            }
        }

        // T-CORE-22
        [Test]
        public void T_CORE_22_RightMouseDragTracksPointerIgnoresKeysAndReleases()
        {
            float[] heights = { 12f, 44f, 88f };
            foreach (float h in heights)
            {
                CameraRig dragRig = CreateRigAtHeight(h);
                CameraRig dragWithKeyRig = CreateRigAtHeight(h);

                CameraInput start = BaseInput();
                start.DragHeld = true;
                start.PointerNdcX = 0.2f;
                start.PointerNdcY = -0.3f;
                (float grabU, float grabV) = dragRig.Pose.ScreenToPlane(Aspect16x9, 0.2f, -0.3f);

                dragRig.Tick(in start, Dt60Hz);
                CameraInput startWithKey = start;
                startWithKey.PanEast = true;
                dragWithKeyRig.Tick(in startWithKey, Dt60Hz);

                for (int i = 1; i <= 20; i++)
                {
                    float nx = 0.2f - 0.015f * i;
                    float ny = -0.3f + 0.010f * i;

                    CameraInput step = BaseInput();
                    step.DragHeld = true;
                    step.PointerNdcX = nx;
                    step.PointerNdcY = ny;
                    dragRig.Tick(in step, Dt60Hz);

                    CameraInput stepKey = step;
                    stepKey.PanEast = true;
                    dragWithKeyRig.Tick(in stepKey, Dt60Hz);

                    (float underU, float underV) = dragRig.Pose.ScreenToPlane(Aspect16x9, nx, ny);
                    Assert.That(underU, Is.EqualTo(grabU).Within(1e-3f));
                    Assert.That(underV, Is.EqualTo(grabV).Within(1e-3f));

                    // Holding East during drag changes nothing.
                    Assert.That(dragWithKeyRig.Cu, Is.EqualTo(dragRig.Cu).Within(1e-6f));
                    Assert.That(dragWithKeyRig.Cv, Is.EqualTo(dragRig.Cv).Within(1e-6f));
                }

                // Release without keys: decays to 0.
                for (int i = 0; i < 180; i++)
                {
                    CameraInput idle = BaseInput();
                    dragRig.Tick(in idle, Dt60Hz);
                }

                Assert.That(dragRig.Vu, Is.EqualTo(0f));
                Assert.That(dragRig.Vv, Is.EqualTo(0f));

                // Release with East held: approaches 0.9 * H east without exceeding max(releaseSpeed, 0.9 * H).
                CameraInput releaseEast = BaseInput();
                releaseEast.DragHeld = false;
                releaseEast.PanEast = true;
                dragWithKeyRig.Tick(in releaseEast, Dt60Hz);
                float preReleaseSpeed = MathF.Sqrt(
                    dragWithKeyRig.Vu * dragWithKeyRig.Vu + dragWithKeyRig.Vv * dragWithKeyRig.Vv);

                float steadySpeed = 0.9f * h;
                float speedCap = MathF.Max(preReleaseSpeed, steadySpeed) + 1e-3f;
                for (int i = 0; i < 60; i++)
                {
                    dragWithKeyRig.Tick(in releaseEast, Dt60Hz);
                    float currentSpeed = MathF.Sqrt(
                        dragWithKeyRig.Vu * dragWithKeyRig.Vu + dragWithKeyRig.Vv * dragWithKeyRig.Vv);
                    Assert.That(currentSpeed, Is.LessThanOrEqualTo(speedCap));
                }

                Assert.That(dragWithKeyRig.Vu, Is.EqualTo(steadySpeed).Within(steadySpeed * 0.01f));
                Assert.That(dragWithKeyRig.Vv, Is.EqualTo(0f).Within(1e-2f));
            }
        }

        // T-CORE-23
        [Test]
        public void T_CORE_23_ScrollZoomAnchorOutsideWindowLimitsAndPoseContract()
        {
            // Verify CameraPose consists of (Cu, Cv, H) only.
            var poseProps = typeof(CameraPose).GetProperties();
            Assert.That(poseProps.Length, Is.EqualTo(3));
            Assert.That(typeof(CameraPose).GetProperty("Cu"), Is.Not.Null);
            Assert.That(typeof(CameraPose).GetProperty("Cv"), Is.Not.Null);
            Assert.That(typeof(CameraPose).GetProperty("H"), Is.Not.Null);

            // Scroll +1 and -1 with pointer inside window, anchored even if pointer shifts afterwards.
            var rig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            const float sx = -0.4f;
            const float sy = 0.35f;
            (float expectedAnchorU, float expectedAnchorV) = rig.Pose.ScreenToPlane(Aspect16x9, sx, sy);

            CameraInput scrollIn = BaseInput();
            scrollIn.ScrollNotches = 1f;
            scrollIn.PointerNdcX = sx;
            scrollIn.PointerNdcY = sy;
            float prevH = rig.H;
            rig.Tick(in scrollIn, Dt60Hz);

            Assert.That(rig.TargetViewHeight, Is.EqualTo(44f / 1.15f).Within(1e-5f));

            for (int i = 0; i < 90; i++)
            {
                CameraInput settle = BaseInput();
                // Move pointer elsewhere; captured anchor position must remain under (sx, sy).
                settle.PointerNdcX = 0.7f;
                settle.PointerNdcY = -0.6f;
                rig.Tick(in settle, Dt60Hz);

                Assert.That(rig.H, Is.LessThanOrEqualTo(prevH + 1e-6f));
                Assert.That(rig.H, Is.GreaterThanOrEqualTo(rig.TargetViewHeight - 1e-6f));
                prevH = rig.H;

                (float curU, float curV) = rig.Pose.ScreenToPlane(Aspect16x9, sx, sy);
                Assert.That(curU, Is.EqualTo(expectedAnchorU).Within(1e-3f));
                Assert.That(curV, Is.EqualTo(expectedAnchorV).Within(1e-3f));
            }

            Assert.That(rig.H, Is.EqualTo(rig.TargetViewHeight).Within(1e-5f));

            // Scroll -1 out.
            CameraInput scrollOut = BaseInput();
            scrollOut.ScrollNotches = -1f;
            scrollOut.PointerNdcX = 0.25f;
            scrollOut.PointerNdcY = -0.25f;
            float targetBeforeOut = rig.TargetViewHeight;
            rig.Tick(in scrollOut, Dt60Hz);
            Assert.That(rig.TargetViewHeight, Is.EqualTo(targetBeforeOut * 1.15f).Within(1e-4f));

            // Scroll with pointer outside window does not move C.
            var outsideRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            float startCu = outsideRig.Cu;
            float startCv = outsideRig.Cv;
            for (int i = 0; i < 60; i++)
            {
                CameraInput outScroll = BaseInput();
                outScroll.PointerInWindow = false;
                outScroll.PointerNdcX = 0.8f;
                outScroll.PointerNdcY = 0.8f;
                outScroll.ScrollNotches = i < 3 ? 1f : 0f;
                outsideRig.Tick(in outScroll, Dt60Hz);
                Assert.That(outsideRig.Cu, Is.EqualTo(startCu).Within(1e-6f));
                Assert.That(outsideRig.Cv, Is.EqualTo(startCv).Within(1e-6f));
            }

            // 40 consecutive +1 ticks, then 40 consecutive -1 ticks stop exactly at 12 and 88.
            var limitRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            for (int i = 0; i < 40; i++)
            {
                CameraInput stepIn = BaseInput();
                stepIn.ScrollNotches = 1f;
                limitRig.Tick(in stepIn, Dt60Hz);
                Assert.That(limitRig.H, Is.GreaterThanOrEqualTo(12f));
            }

            for (int i = 0; i < 120; i++)
            {
                CameraInput idle = BaseInput();
                limitRig.Tick(in idle, Dt60Hz);
                Assert.That(limitRig.H, Is.GreaterThanOrEqualTo(12f));
            }

            Assert.That(limitRig.H, Is.EqualTo(12f));

            for (int i = 0; i < 40; i++)
            {
                CameraInput stepOut = BaseInput();
                stepOut.ScrollNotches = -1f;
                limitRig.Tick(in stepOut, Dt60Hz);
                Assert.That(limitRig.H, Is.LessThanOrEqualTo(88f));
            }

            for (int i = 0; i < 120; i++)
            {
                CameraInput idle = BaseInput();
                limitRig.Tick(in idle, Dt60Hz);
                Assert.That(limitRig.H, Is.LessThanOrEqualTo(88f));
            }

            Assert.That(limitRig.H, Is.EqualTo(88f));
        }

        // T-CORE-24
        [Test]
        public void T_CORE_24_ResetConvergenceInterruptionAndHeldKeySuppression()
        {
            // Pan and zoom away, press reset -> converges monotonically >= 95 % in 1.2 s, never in 1 tick.
            CameraRig rig = CreatePannedAndZoomedRig();
            float startDist = DistanceToVista(rig);
            float startHDiff = MathF.Abs(44f - rig.H);

            CameraInput reset = BaseInput();
            reset.ResetPressed = true;
            rig.Tick(in reset, Dt60Hz);

            Assert.That(rig.Mode, Is.EqualTo(CameraMode.Resetting));
            Assert.That(DistanceToVista(rig), Is.GreaterThan(0.5f));
            float prevDist = DistanceToVista(rig);
            float prevHDiff = MathF.Abs(44f - rig.H);

            int ticksFor1200Ms = 72;
            for (int i = 1; i < ticksFor1200Ms; i++)
            {
                CameraInput idle = BaseInput();
                rig.Tick(in idle, Dt60Hz);
                float d = DistanceToVista(rig);
                float hd = MathF.Abs(44f - rig.H);
                Assert.That(d, Is.LessThanOrEqualTo(prevDist + 1e-6f));
                Assert.That(hd, Is.LessThanOrEqualTo(prevHDiff + 1e-6f));
                prevDist = d;
                prevHDiff = hd;
            }

            Assert.That(DistanceToVista(rig), Is.LessThanOrEqualTo(0.05f * startDist));
            Assert.That(MathF.Abs(44f - rig.H), Is.LessThanOrEqualTo(0.05f * startHDiff));

            // Reset mid-pan zeroes velocity on that tick and suppresses held key until released and re-pressed.
            var midPanRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            for (int i = 0; i < 60; i++)
            {
                CameraInput pan = BaseInput();
                pan.PanEast = true;
                midPanRig.Tick(in pan, Dt60Hz);
            }

            Assert.That(midPanRig.Vu, Is.GreaterThan(10f));
            CameraInput resetWhileHeld = BaseInput();
            resetWhileHeld.PanEast = true;
            resetWhileHeld.ResetPressed = true;
            midPanRig.Tick(in resetWhileHeld, Dt60Hz);

            Assert.That(midPanRig.Vu, Is.EqualTo(0f));
            Assert.That(midPanRig.Vv, Is.EqualTo(0f));
            Assert.That(midPanRig.Mode, Is.EqualTo(CameraMode.Resetting));

            // Keep PanEast held across reset; it must neither cancel reset nor pan after reset completes.
            for (int i = 0; i < 240; i++)
            {
                CameraInput keepHeld = BaseInput();
                keepHeld.PanEast = true;
                midPanRig.Tick(in keepHeld, Dt60Hz);
            }

            Assert.That(midPanRig.Mode, Is.EqualTo(CameraMode.Free));
            Assert.That(midPanRig.Cu, Is.EqualTo(SyntheticFrame.VistaU).Within(1e-3f));
            Assert.That(midPanRig.Vu, Is.EqualTo(0f));

            // Release and re-press works.
            CameraInput release = BaseInput();
            midPanRig.Tick(in release, Dt60Hz);
            CameraInput repress = BaseInput();
            repress.PanEast = true;
            midPanRig.Tick(in repress, Dt60Hz);
            Assert.That(midPanRig.Vu, Is.GreaterThan(0f));

            // Interrupting reset with: (a) new pan key, (b) new drag, (c) scroll.
            AssertResetInterruptedBy(i => { i.PanWest = true; return i; });
            AssertResetInterruptedBy(i => { i.DragHeld = true; i.PointerNdcX = 0.1f; i.PointerNdcY = 0.1f; return i; });
            AssertResetInterruptedBy(i => { i.ScrollNotches = 1f; i.PointerNdcX = 0f; i.PointerNdcY = 0f; return i; });
        }

        // T-CORE-27
        [Test]
        public void T_CORE_27_CancelAndSuppressionAcrossBlockedFocusLostPausedAndPointerExit()
        {
            AssertCancelTrigger(CancelTrigger.Blocked);
            AssertCancelTrigger(CancelTrigger.FocusLost);
            AssertCancelTrigger(CancelTrigger.AppPaused);
            AssertCancelTrigger(CancelTrigger.PointerExit);

            // While Blocked, scroll/keys/drag/reset change nothing.
            var blockedRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            float startCu = blockedRig.Cu;
            float startCv = blockedRig.Cv;
            float startH = blockedRig.H;

            for (int i = 0; i < 30; i++)
            {
                CameraInput blockedAll = BaseInput();
                blockedAll.Blocked = true;
                blockedAll.PanEast = true;
                blockedAll.PanNorth = true;
                blockedAll.DragHeld = true;
                blockedAll.ResetPressed = true;
                blockedAll.ScrollNotches = 2f;
                blockedAll.PointerNdcX = 0.4f;
                blockedAll.PointerNdcY = 0.4f;
                blockedRig.Tick(in blockedAll, Dt60Hz);

                Assert.That(blockedRig.Cu, Is.EqualTo(startCu));
                Assert.That(blockedRig.Cv, Is.EqualTo(startCv));
                Assert.That(blockedRig.H, Is.EqualTo(startH));
                Assert.That(blockedRig.IsDragging, Is.False);
            }
        }

        private enum CancelTrigger
        {
            Blocked,
            FocusLost,
            AppPaused,
            PointerExit,
        }

        private static void AssertCancelTrigger(CancelTrigger trigger)
        {
            // Case 1: While panning.
            var panRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            CameraInput panInput = BaseInput();
            panInput.PanEast = true;
            for (int i = 0; i < 60; i++)
            {
                panRig.Tick(in panInput, Dt60Hz);
            }

            ApplyTriggerAndVerifySilence(panRig, panInput, trigger);

            // Case 2: While dragging.
            var dragRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            for (int i = 0; i < 15; i++)
            {
                CameraInput dragStep = BaseInput();
                dragStep.DragHeld = true;
                dragStep.PointerNdcX = -0.1f * (i / 15f);
                dragStep.PointerNdcY = 0f;
                dragRig.Tick(in dragStep, Dt60Hz);
            }

            CameraInput heldDrag = BaseInput();
            heldDrag.DragHeld = true;
            heldDrag.PointerNdcX = -0.1f;
            heldDrag.PointerNdcY = 0f;
            ApplyTriggerAndVerifySilence(dragRig, heldDrag, trigger);
        }

        private static void ApplyTriggerAndVerifySilence(CameraRig rig, CameraInput heldInput, CancelTrigger trigger)
        {
            CameraInput triggerInput = heldInput;
            if (trigger == CancelTrigger.Blocked)
            {
                triggerInput.Blocked = true;
            }
            else if (trigger == CancelTrigger.PointerExit)
            {
                triggerInput.PointerInWindow = false;
            }
            else if (trigger == CancelTrigger.FocusLost)
            {
                rig.OnFocusLost();
            }
            else if (trigger == CancelTrigger.AppPaused)
            {
                rig.OnApplicationPaused();
            }

            rig.Tick(in triggerInput, Dt60Hz);
            Assert.That(rig.Vu, Is.EqualTo(0f));
            Assert.That(rig.Vv, Is.EqualTo(0f));
            Assert.That(rig.IsDragging, Is.False);

            float frozenCu = rig.Cu;
            float frozenCv = rig.Cv;
            float frozenH = rig.H;

            // Even if pointer moves or Blocked stays/clears while input remains held, pose is constant for 300 ticks.
            for (int i = 0; i < 300; i++)
            {
                CameraInput stillHeld = triggerInput;
                stillHeld.PointerNdcX = 0.3f;
                stillHeld.PointerNdcY = -0.2f;
                if (trigger == CancelTrigger.FocusLost || trigger == CancelTrigger.AppPaused)
                {
                    stillHeld.PointerInWindow = true;
                }

                rig.Tick(in stillHeld, Dt60Hz);
                Assert.That(rig.Cu, Is.EqualTo(frozenCu));
                Assert.That(rig.Cv, Is.EqualTo(frozenCv));
                Assert.That(rig.H, Is.EqualTo(frozenH));
                Assert.That(rig.IsDragging, Is.False);
            }

            // Release all inputs (and unblock / return pointer to window), then re-press works.
            CameraInput released = BaseInput();
            rig.Tick(in released, Dt60Hz);

            CameraInput repressed = BaseInput();
            repressed.PanEast = heldInput.PanEast;
            repressed.DragHeld = heldInput.DragHeld;
            repressed.PointerNdcX = 0f;
            repressed.PointerNdcY = 0f;
            rig.Tick(in repressed, Dt60Hz);

            if (heldInput.PanEast)
            {
                Assert.That(rig.Vu, Is.GreaterThan(0f));
            }
            else if (heldInput.DragHeld)
            {
                Assert.That(rig.IsDragging, Is.True);
            }
        }

        private static void AssertResetInterruptedBy(Func<CameraInput, CameraInput> buildInterrupt)
        {
            CameraRig rig = CreatePannedAndZoomedRig();
            CameraInput reset = BaseInput();
            reset.ResetPressed = true;
            rig.Tick(in reset, Dt60Hz);
            for (int i = 0; i < 10; i++)
            {
                CameraInput idle = BaseInput();
                rig.Tick(in idle, Dt60Hz);
            }

            Assert.That(rig.Mode, Is.EqualTo(CameraMode.Resetting));
            float beforeCu = rig.Cu;
            float beforeCv = rig.Cv;
            float beforeH = rig.H;

            CameraInput interrupt = buildInterrupt(BaseInput());
            rig.Tick(in interrupt, Dt60Hz);

            Assert.That(rig.Mode, Is.EqualTo(CameraMode.Free));
            float deltaC = MathF.Sqrt(
                (rig.Cu - beforeCu) * (rig.Cu - beforeCu) + (rig.Cv - beforeCv) * (rig.Cv - beforeCv));
            Assert.That(deltaC, Is.LessThan(0.01f * beforeH));
        }

        private static CameraRig CreatePannedAndZoomedRig()
        {
            var rig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            for (int i = 0; i < 60; i++)
            {
                CameraInput input = BaseInput();
                input.PanEast = true;
                input.PanNorth = true;
                input.ScrollNotches = i < 4 ? 1f : 0f;
                rig.Tick(in input, Dt60Hz);
            }

            return rig;
        }

        private static CameraRig CreateRigAtHeight(float targetH)
        {
            CameraConfigData data = CameraConfig.Default.ToData();
            data.DefaultViewHeightM = targetH;
            Assert.That(CameraConfig.TryCreate(in data, out CameraConfig config, out _), Is.True);
            return new CameraRig(SyntheticFrame, config, Aspect16x9, 1080);
        }

        private static float DistanceToVista(CameraRig rig)
        {
            float du = rig.Cu - SyntheticFrame.VistaU;
            float dv = rig.Cv - SyntheticFrame.VistaV;
            return MathF.Sqrt(du * du + dv * dv);
        }

        private static CameraInput BaseInput()
        {
            return new CameraInput
            {
                PointerInWindow = true,
                Aspect = Aspect16x9,
                ScreenHeightPx = 1080,
            };
        }
    }
}
