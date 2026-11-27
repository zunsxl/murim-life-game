using System;
using Murim.Core.Camera;
using NUnit.Framework;

namespace Murim.Core.Tests.Camera
{
    [TestFixture]
    public class CameraViewTests
    {
        private const float Dt60Hz = 1f / 60f;
        private static readonly ViewFrame SyntheticFrame = new ViewFrame(320, 34800, 25600, 14000);

        // T-CORE-20
        [Test]
        public void T_CORE_20_ViewFrameAndScreenToPlaneMapping()
        {
            Assert.That(SyntheticFrame.BottomV, Is.EqualTo(3.2f).Within(1e-6f));
            Assert.That(SyntheticFrame.CrestV, Is.EqualTo(348f).Within(1e-6f));
            Assert.That(SyntheticFrame.VistaU, Is.EqualTo(256f).Within(1e-6f));
            Assert.That(SyntheticFrame.VistaV, Is.EqualTo(140f).Within(1e-6f));
            Assert.That(ViewFrame.WorldWidthM, Is.EqualTo(512f));
            Assert.That(ViewFrame.SkyFraction, Is.EqualTo(0.30f));
            Assert.That(ViewFrame.MaxPixelsPerMetre, Is.EqualTo(160f));

            float[] heights = { 12f, 44f, 88f };
            float[] aspects = { 4f / 3f, 16f / 9f, 32f / 9f };
            (float sx, float sy)[] corners =
            {
                (0f, 0f),
                (-1f, -1f),
                (1f, -1f),
                (-1f, 1f),
                (1f, 1f),
            };

            foreach (float h in heights)
            {
                foreach (float aspect in aspects)
                {
                    var pose = new CameraPose(256f, 140f, h);
                    float w = h * aspect;

                    foreach ((float sx, float sy) in corners)
                    {
                        (float u, float v) = pose.ScreenToPlane(aspect, sx, sy);
                        Assert.That(u, Is.EqualTo(pose.Cu + sx * (w * 0.5f)).Within(1e-4f));
                        Assert.That(v, Is.EqualTo(pose.Cv + sy * (h * 0.5f)).Within(1e-4f));
                    }

                    const float anchorSx = 0.35f;
                    const float anchorSy = -0.45f;
                    (float anchorU, float anchorV) = pose.ScreenToPlane(aspect, anchorSx, anchorSy);

                    float newH = h * 0.7f;
                    float newW = newH * aspect;
                    var anchoredPose = new CameraPose(
                        anchorU - anchorSx * (newW * 0.5f),
                        anchorV - anchorSy * (newH * 0.5f),
                        newH);
                    (float remappedU, float remappedV) = anchoredPose.ScreenToPlane(aspect, anchorSx, anchorSy);
                    Assert.That(remappedU, Is.EqualTo(anchorU).Within(1e-4f));
                    Assert.That(remappedV, Is.EqualTo(anchorV).Within(1e-4f));
                }
            }
        }

        // T-CORE-25
        [Test]
        public void T_CORE_25_BoundsClampAllEdgesAndAspects()
        {
            float[] heights = { 12f, 44f, 88f };
            float[] aspects = { 4f / 3f, 16f / 9f, 21f / 9f, 32f / 9f, 6f };

            CameraConfigData fastPanData = CameraConfig.Default.ToData();
            fastPanData.PanSpeedPerViewHeight = 3.0f;
            Assert.That(CameraConfig.TryCreate(in fastPanData, out CameraConfig fastConfig, out _), Is.True);

            foreach (float targetH in heights)
            {
                foreach (float aspect in aspects)
                {
                    AssertCornerClamping(fastConfig, targetH, aspect, -1, -1);
                    AssertCornerClamping(fastConfig, targetH, aspect, 1, -1);
                    AssertCornerClamping(fastConfig, targetH, aspect, -1, 1);
                    AssertCornerClamping(fastConfig, targetH, aspect, 1, 1);
                }
            }
        }

        // T-CORE-25
        [Test]
        public void T_CORE_25_AspectChangeInPlace_ReclampsNextTick()
        {
            var rig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            // Zoom out to 88 and push north-east corner at 16:9.
            ZoomToTarget(rig, 16f / 9f, 1080, -4f, 40);
            PanWithKeys(rig, 16f / 9f, 1080, north: true, east: true, ticks: 240);

            float[] aspectSequence = { 32f / 9f, 6f, 4f / 3f, 21f / 9f };
            foreach (float aspect in aspectSequence)
            {
                var input = new CameraInput
                {
                    Aspect = aspect,
                    ScreenHeightPx = 1080,
                    PointerInWindow = true,
                };
                rig.Tick(in input, Dt60Hz);

                AssertWithinFrameBounds(rig.Pose, SyntheticFrame, aspect, 1080);
                if (aspect >= 5.9f)
                {
                    Assert.That(rig.H, Is.LessThanOrEqualTo(ViewFrame.WorldWidthM / aspect + 1e-4f));
                }
            }
        }

        // T-CORE-26
        [Test]
        public void T_CORE_26_ScreenHeightZoomFloorAndResizeTo4320()
        {
            (int heightPx, float expectedHMin)[] cases =
            {
                (540, 12f),
                (1080, 12f),
                (1440, 12f),
                (1920, 12f),
                (2160, 13.5f),
                (4320, 27f),
            };

            foreach ((int heightPx, float expectedHMin) in cases)
            {
                var rig = new CameraRig(SyntheticFrame, CameraConfig.Default);
                Assert.That(rig.ComputeHMinEff(heightPx), Is.EqualTo(expectedHMin).Within(1e-4f));

                for (int i = 0; i < 40; i++)
                {
                    var input = new CameraInput
                    {
                        ScrollNotches = 1f,
                        PointerNdcX = 0f,
                        PointerNdcY = 0f,
                        PointerInWindow = true,
                        Aspect = 16f / 9f,
                        ScreenHeightPx = heightPx,
                    };
                    rig.Tick(in input, Dt60Hz);
                    Assert.That(heightPx / rig.H, Is.LessThanOrEqualTo(160f + 1e-3f));
                }

                for (int i = 0; i < 120; i++)
                {
                    var settle = new CameraInput
                    {
                        PointerInWindow = true,
                        Aspect = 16f / 9f,
                        ScreenHeightPx = heightPx,
                    };
                    rig.Tick(in settle, Dt60Hz);
                    Assert.That(heightPx / rig.H, Is.LessThanOrEqualTo(160f + 1e-3f));
                }

                Assert.That(rig.H, Is.EqualTo(expectedHMin).Within(1e-4f));
            }

            // Now zoom to H = 12 at 1080p, then raise ScreenHeightPx to 4320 and check next tick.
            var resizeRig = new CameraRig(SyntheticFrame, CameraConfig.Default);
            ZoomToTarget(resizeRig, 16f / 9f, 1080, 4f, 180);
            Assert.That(resizeRig.H, Is.EqualTo(12f).Within(1e-4f));

            var raisedInput = new CameraInput
            {
                PointerInWindow = true,
                Aspect = 16f / 9f,
                ScreenHeightPx = 4320,
            };
            resizeRig.Tick(in raisedInput, Dt60Hz);

            Assert.That(resizeRig.H, Is.EqualTo(27f).Within(1e-4f));
            AssertWithinFrameBounds(resizeRig.Pose, SyntheticFrame, 16f / 9f, 4320);
        }

        private static void AssertCornerClamping(
            CameraConfig fastConfig,
            float targetH,
            float aspect,
            int dirU,
            int dirV)
        {
            CameraConfigData data = fastConfig.ToData();
            data.DefaultViewHeightM = targetH;
            Assert.That(CameraConfig.TryCreate(in data, out CameraConfig cfg, out _), Is.True);

            var rig = new CameraRig(SyntheticFrame, cfg, aspect, 1080);
            float expectedH = MathF.Min(targetH, ViewFrame.WorldWidthM / aspect);
            if (aspect >= 5.9f)
            {
                Assert.That(rig.H, Is.LessThanOrEqualTo(ViewFrame.WorldWidthM / aspect + 1e-4f));
            }

            PanWithKeys(
                rig,
                aspect,
                1080,
                north: dirV > 0,
                east: dirU > 0,
                ticks: 600,
                south: dirV < 0,
                west: dirU < 0);

            float h = rig.H;
            Assert.That(h, Is.EqualTo(expectedH).Within(1e-4f));

            float w = h * aspect;
            float expectedCu = dirU < 0 ? w * 0.5f : ViewFrame.WorldWidthM - w * 0.5f;
            float expectedCv = dirV < 0
                ? SyntheticFrame.BottomV + h * 0.5f
                : SyntheticFrame.CrestV + ViewFrame.SkyFraction * h - h * 0.5f;

            Assert.That(rig.Cu, Is.EqualTo(expectedCu).Within(1e-4f));
            Assert.That(rig.Cv, Is.EqualTo(expectedCv).Within(1e-4f));

            if (dirV > 0)
            {
                float skyRatio = (rig.Cv + h * 0.5f - SyntheticFrame.CrestV) / h;
                Assert.That(skyRatio, Is.EqualTo(0.30f).Within(1e-4f));
            }

            // Velocity component into the bound is 0 on the next tick and does not oscillate over 120 ticks.
            var release = new CameraInput
            {
                Aspect = aspect,
                ScreenHeightPx = 1080,
                PointerInWindow = true,
            };
            rig.Tick(in release, Dt60Hz);
            Assert.That(rig.Vu, Is.EqualTo(0f));
            Assert.That(rig.Vv, Is.EqualTo(0f));

            float settledCu = rig.Cu;
            float settledCv = rig.Cv;
            for (int i = 0; i < 120; i++)
            {
                rig.Tick(in release, Dt60Hz);
                Assert.That(rig.Cu, Is.EqualTo(settledCu).Within(1e-6f));
                Assert.That(rig.Cv, Is.EqualTo(settledCv).Within(1e-6f));
            }
        }

        private static void ZoomToTarget(CameraRig rig, float aspect, int heightPx, float scrollNotches, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                var input = new CameraInput
                {
                    ScrollNotches = i < 20 ? scrollNotches : 0f,
                    PointerInWindow = false,
                    Aspect = aspect,
                    ScreenHeightPx = heightPx,
                };
                rig.Tick(in input, Dt60Hz);
            }
        }

        private static void PanWithKeys(
            CameraRig rig,
            float aspect,
            int heightPx,
            bool north,
            bool east,
            int ticks,
            bool south = false,
            bool west = false)
        {
            for (int i = 0; i < ticks; i++)
            {
                var input = new CameraInput
                {
                    PanNorth = north,
                    PanSouth = south,
                    PanWest = west,
                    PanEast = east,
                    PointerInWindow = true,
                    Aspect = aspect,
                    ScreenHeightPx = heightPx,
                };
                rig.Tick(in input, Dt60Hz);
            }
        }

        private static void AssertWithinFrameBounds(CameraPose pose, ViewFrame frame, float aspect, int heightPx)
        {
            float w = pose.ViewWidth(aspect);
            float h = pose.H;
            Assert.That(heightPx / h, Is.LessThanOrEqualTo(ViewFrame.MaxPixelsPerMetre + 1e-3f));
            Assert.That(pose.Cu - w * 0.5f, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(pose.Cu + w * 0.5f, Is.LessThanOrEqualTo(ViewFrame.WorldWidthM + 1e-4f));
            Assert.That(pose.Cv - h * 0.5f, Is.GreaterThanOrEqualTo(frame.BottomV - 1e-4f));
            Assert.That(pose.Cv + h * 0.5f, Is.LessThanOrEqualTo(frame.CrestV + ViewFrame.SkyFraction * h + 1e-4f));
        }
    }
}
