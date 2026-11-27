using System;
using Murim.Core.Camera;
using NUnit.Framework;

namespace Murim.Core.Tests.Camera
{
    [TestFixture]
    public class CameraBalanceTests
    {
        private const float Aspect16x9 = 16f / 9f;
        private static readonly ViewFrame SyntheticFrame = new ViewFrame(320, 34800, 25600, 14000);

        // T-BAL-01
        [TestCase(60)]
        [TestCase(144)]
        public void T_BAL_01_FeelMetricsMatchSpecBudgets(int hz)
        {
            float dt = 1f / hz;

            // 1. Screen-width crossing time 1.7778 / 0.9 = 1.975 s ± 3 % at H = 12, 44, 88.
            float[] heights = { 12f, 44f, 88f };
            const float expectedCrossTime = (16f / 9f) / 0.9f;
            foreach (float h in heights)
            {
                CameraRig rig = CreateRigAtHeight(h);
                // First pan west so there is ample unclamped room east of the camera at all zooms (including H = 88).
                for (int i = 0; i < hz * 2; i++)
                {
                    CameraInput west = BaseInput();
                    west.PanWest = true;
                    rig.Tick(in west, dt);
                }

                // Now accelerate east to steady speed before timing one screen width.
                for (int i = 0; i < hz; i++)
                {
                    CameraInput east = BaseInput();
                    east.PanEast = true;
                    rig.Tick(in east, dt);
                }

                float startCu = rig.Cu;
                float screenW = h * Aspect16x9;
                int ticks = 0;
                while (rig.Cu - startCu < screenW && ticks < hz * 5)
                {
                    CameraInput east = BaseInput();
                    east.PanEast = true;
                    rig.Tick(in east, dt);
                    ticks++;
                }

                float crossTime = ticks * dt;
                Assert.That(crossTime, Is.EqualTo(expectedCrossTime).Within(expectedCrossTime * 0.03f));
            }

            // 2. Pan 95 % rise in 0.32 s ± 0.05, and stop within 5 % in 0.68 s ± 0.08.
            var panRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            float steadyV = 0.9f * 44f;
            int riseTicks = 0;
            while (panRig.Vu < 0.95f * steadyV && riseTicks < hz * 2)
            {
                CameraInput east = BaseInput();
                east.PanEast = true;
                panRig.Tick(in east, dt);
                riseTicks++;
            }

            Assert.That(riseTicks * dt, Is.EqualTo(0.32f).Within(0.05f));

            for (int i = 0; i < hz * 2; i++)
            {
                CameraInput east = BaseInput();
                east.PanEast = true;
                panRig.Tick(in east, dt);
            }

            float releaseV = panRig.Vu;
            int stopTicks = 0;
            while (panRig.Vu > 0.05f * releaseV && stopTicks < hz * 3)
            {
                CameraInput idle = BaseInput();
                panRig.Tick(in idle, dt);
                stopTicks++;
            }

            Assert.That(stopTicks * dt, Is.EqualTo(0.68f).Within(0.08f));

            // 3. Zoom step 95 % in 0.38 s ± 0.06.
            var zoomRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            float hBeforeZoom = zoomRig.H;
            CameraInput scroll = BaseInput();
            scroll.ScrollNotches = 1f;
            zoomRig.Tick(in scroll, dt);
            int zoomTicks = 1;
            float totalZoomDelta = hBeforeZoom - zoomRig.TargetViewHeight;
            while ((zoomRig.H - zoomRig.TargetViewHeight) > 0.05f * totalZoomDelta && zoomTicks < hz * 2)
            {
                CameraInput idle = BaseInput();
                zoomRig.Tick(in idle, dt);
                zoomTicks++;
            }

            Assert.That(zoomTicks * dt, Is.EqualTo(0.38f).Within(0.06f));

            // 4. Reset 95 % in 1.07 s ± 0.12.
            var resetRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            for (int i = 0; i < hz; i++)
            {
                CameraInput pan = BaseInput();
                pan.PanEast = true;
                pan.ScrollNotches = i < 4 ? 1f : 0f;
                resetRig.Tick(in pan, dt);
            }

            float startResetDist = MathF.Abs(resetRig.Cu - SyntheticFrame.VistaU);
            CameraInput resetReq = BaseInput();
            resetReq.ResetPressed = true;
            resetRig.Tick(in resetReq, dt);
            int resetTicks = 1;
            while (MathF.Abs(resetRig.Cu - SyntheticFrame.VistaU) > 0.05f * startResetDist && resetTicks < hz * 3)
            {
                CameraInput idle = BaseInput();
                resetRig.Tick(in idle, dt);
                resetTicks++;
            }

            Assert.That(resetTicks * dt, Is.EqualTo(1.07f).Within(0.12f));

            // 5. Detent counts: full range 88 <-> 12 in 15 detents; from default 44, 10 in and 5 out.
            AssertDetentCount(88f, +1f, 15, 12f, dt);
            AssertDetentCount(12f, -1f, 15, 88f, dt);
            AssertDetentCount(44f, +1f, 10, 12f, dt);
            AssertDetentCount(44f, -1f, 5, 88f, dt);
        }

        private static void AssertDetentCount(
            float startH,
            float notchPerDetent,
            int expectedDetents,
            float expectedTargetH,
            float dt)
        {
            CameraRig rig = CreateRigAtHeight(startH);
            for (int d = 1; d <= expectedDetents; d++)
            {
                CameraInput scroll = BaseInput();
                scroll.ScrollNotches = notchPerDetent;
                rig.Tick(in scroll, dt);
                if (d < expectedDetents)
                {
                    Assert.That(rig.TargetViewHeight, Is.Not.EqualTo(expectedTargetH));
                }
            }

            Assert.That(rig.TargetViewHeight, Is.EqualTo(expectedTargetH));
        }

        private static CameraRig CreateRigAtHeight(float targetH)
        {
            CameraConfigData data = CameraConfig.Default.ToData();
            data.DefaultViewHeightM = targetH;
            Assert.That(CameraConfig.TryCreate(in data, out CameraConfig config, out _), Is.True);
            return new CameraRig(SyntheticFrame, config, Aspect16x9, 1080);
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
