using Murim.Core.Exploration.Session;
using Murim.Core.Tests.Valley;
using Murim.Core.Tuning;
using Murim.Core.Valley;
using NUnit.Framework;

namespace Murim.Core.Tests.Session
{
    [TestFixture]
    public sealed class CameraModelTests
    {
        // T-CORE-24
        [Test]
        public void T_CORE_24_EdgeClampingAtAllZoomsAndAspects()
        {
            float[] aspects = { 4f / 3f, 16f / 9f, 21f / 9f };
            float[] targetZooms =
            {
                ExplorationTuning.DefaultMinOrthoSize,
                ExplorationTuning.DefaultDefaultOrthoSize,
                ExplorationTuning.DefaultMaxOrthoSize
            };

            foreach (float aspect in aspects)
            {
                // Verify effective max OrthoSize is 28.125 at all three aspects.
                CameraModel zoomProbe = new CameraModel(ExplorationTuning.Default, aspect);
                for (int i = 0; i < 60; i++)
                {
                    zoomProbe.ZoomNotch(-1, aspect);
                }
                Assert.That(zoomProbe.OrthoSize, Is.EqualTo(28.125f).Within(1e-5f));

                foreach (float zoom in targetZooms)
                {
                    VerifyEdgeLimitsForAspectAndZoom(aspect, zoom);
                }
            }
        }

        // T-CORE-25
        [Test]
        public void T_CORE_25_PanCancellationDiagonalNormalizationAndDragOverride()
        {
            CameraModel camera = new CameraModel(ExplorationTuning.Default, CameraModel.DefaultAspect);
            float dt = 0.05f;
            float v = ExplorationTuning.DefaultPanSpeed * 2f * camera.OrthoSize;

            // Hold left + right + up: horizontal cancels, vertical continues.
            Vec2 start = camera.Focus;
            camera.PanKeys(new PanInput(1, 1, 1, 0), dt, CameraModel.DefaultAspect);
            Assert.That(camera.Focus.X, Is.EqualTo(start.X).Within(1e-5f));
            Assert.That(camera.Focus.Y - start.Y, Is.EqualTo(v * dt * ValleyConstants.Sqrt2).Within(1e-4f));

            // Hold up + right: diagonal moves v * dt / Sqrt2 on each screen axis.
            Vec2 beforeDiag = camera.Focus;
            camera.PanKeys(new PanInput(0, 1, 1, 0), dt, CameraModel.DefaultAspect);
            float expectedScreenStep = v * dt / ValleyConstants.Sqrt2;
            Assert.That(camera.Focus.X - beforeDiag.X, Is.EqualTo(expectedScreenStep).Within(1e-4f));
            Assert.That((camera.Focus.Y - beforeDiag.Y) / ValleyConstants.Sqrt2, Is.EqualTo(expectedScreenStep).Within(1e-4f));

            // Start drag while key is held: pan keys ignored during drag, then resume from dragged focus.
            camera.BeginDrag();
            Vec2 duringDragStart = camera.Focus;
            camera.PanKeys(new PanInput(0, 1, 0, 0), dt, CameraModel.DefaultAspect);
            Assert.That(camera.Focus, Is.EqualTo(duringDragStart));

            camera.Drag(100f, 0f, 1080f, CameraModel.DefaultAspect);
            Vec2 afterDrag = camera.Focus;
            Assert.That(afterDrag.X, Is.LessThan(duringDragStart.X));

            camera.EndDrag();
            camera.PanKeys(new PanInput(0, 1, 0, 0), dt, CameraModel.DefaultAspect);
            Assert.That(camera.Focus.X - afterDrag.X, Is.EqualTo(v * dt).Within(1e-4f));
            Assert.That(camera.Focus.Y, Is.EqualTo(afterDrag.Y).Within(1e-5f));
        }

        // T-CORE-26
        [Test]
        public void T_CORE_26_DragDeltaMatchesMetersPerPixel()
        {
            CameraModel camera = new CameraModel(ExplorationTuning.Default, CameraModel.DefaultAspect);
            Vec2 start = camera.Focus;

            camera.Drag(100f, 50f, 1080f, CameraModel.DefaultAspect);

            Assert.That(start.X - camera.Focus.X, Is.EqualTo(2.0833f).Within(1e-3f));
            Assert.That(start.Y - camera.Focus.Y, Is.EqualTo(1.4731f).Within(1e-3f));
        }

        // T-CORE-27
        [Test]
        public void T_CORE_27_ScrollZoomInAndOutClampingAndStepFactor()
        {
            CameraModel camera = new CameraModel(ExplorationTuning.Default, CameraModel.DefaultAspect);

            for (int i = 0; i < 30; i++)
            {
                float prev = camera.OrthoSize;
                camera.ZoomNotch(1, CameraModel.DefaultAspect);
                if (prev / ExplorationTuning.DefaultZoomStep >= ExplorationTuning.DefaultMinOrthoSize)
                {
                    Assert.That(prev / camera.OrthoSize, Is.EqualTo(ExplorationTuning.DefaultZoomStep).Within(1e-5f));
                }
            }
            Assert.That(camera.OrthoSize, Is.EqualTo(4.5f));

            for (int i = 0; i < 60; i++)
            {
                float prev = camera.OrthoSize;
                camera.ZoomNotch(-1, CameraModel.DefaultAspect);
                if (prev * ExplorationTuning.DefaultZoomStep <= ExplorationTuning.DefaultMaxOrthoSize)
                {
                    Assert.That(camera.OrthoSize / prev, Is.EqualTo(ExplorationTuning.DefaultZoomStep).Within(1e-5f));
                }
            }
            Assert.That(camera.OrthoSize, Is.EqualTo(28.125f));
        }

        // T-CORE-28
        [Test]
        public void T_CORE_28_SpaceCentersOnPlaceholderAndHomeResetsViewOnly()
        {
            ValleyLayout tier12 = ValleyTestFixtures.CreateTier12();
            ExplorationSession session = new ExplorationSession(tier12, ExplorationTuning.Default);

            // Move placeholder, zoom, pan, and slice down.
            session.Placeholder.Position = new Vec2(50f, 30f);
            session.Placeholder.Elevation = 5f;
            session.Placeholder.AirLayer = 1;
            session.Placeholder.LogicalHeight = 5f;
            session.Placeholder.TargetElevation = 5f;
            session.ZoomNotch(1, CameraModel.DefaultAspect);
            session.StepFloorDown();
            session.StepFloorDown();

            session.CenterOnPlaceholder(CameraModel.DefaultAspect);
            Assert.That(session.Camera.Focus.X, Is.EqualTo(50f).Within(1e-5f));
            Assert.That(session.Camera.Focus.Y, Is.EqualTo(35f).Within(1e-5f));
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(3));

            session.ResetView(CameraModel.DefaultAspect);
            Assert.That(session.Camera.Focus.X, Is.EqualTo(70f));
            Assert.That(session.Camera.Focus.Y, Is.EqualTo(29f));
            Assert.That(session.Camera.OrthoSize, Is.EqualTo(11.25f));
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            Assert.That(session.Placeholder.Position, Is.EqualTo(new Vec2(50f, 30f)));
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(5f));
            Assert.That(session.Placeholder.AirLayer, Is.EqualTo(1));
        }

        // T-CORE-33
        [Test]
        public void T_CORE_33_NorthAirBoundCapsuleAndCrestVertexRemainVisibleAtAllZooms()
        {
            ValleyLayout authored = ValleyLayoutData.CreateLayout();
            float aspect = 16f / 9f;
            Vec3 capsulePos = new Vec3(80f, 17f, 89f);
            Vec3 crestVertex = new Vec3(80f, 13f, 96f);

            int[] zoomStepsFromDefault = { -15, 0, 15 };
            foreach (int steps in zoomStepsFromDefault)
            {
                ExplorationSession session = new ExplorationSession(authored, ExplorationTuning.Default, aspect);
                ApplyZoomSteps(session.Camera, steps, aspect);

                session.Placeholder.Position = new Vec2(capsulePos.X, capsulePos.Z);
                session.Placeholder.Elevation = capsulePos.Y;
                session.Placeholder.AirLayer = 3;
                session.Placeholder.LogicalHeight = capsulePos.Y;
                session.Placeholder.TargetElevation = capsulePos.Y;

                session.CenterOnPlaceholder(aspect);
                Vec2 capsuleScreen = session.Camera.WorldToScreenOffset(capsulePos);
                float clearanceBelowTop = session.Camera.OrthoSize - capsuleScreen.Y;
                Assert.That(clearanceBelowTop, Is.GreaterThanOrEqualTo(2.1f));

                // Pan fully north and verify crest vertex (80, 13, 96) is on screen.
                for (int i = 0; i < 50; i++)
                {
                    session.Camera.PanKeys(new PanInput(0, 0, 1, 0), 0.1f, aspect);
                }

                Vec2 crestScreen = session.Camera.WorldToScreenOffset(crestVertex);
                Assert.That(System.MathF.Abs(crestScreen.X), Is.LessThanOrEqualTo(session.Camera.OrthoSize * aspect + 1e-4f));
                Assert.That(System.MathF.Abs(crestScreen.Y), Is.LessThanOrEqualTo(session.Camera.OrthoSize + 1e-4f));
            }
        }

        // Camera ray projection, dt clamp, and boundary guards (supports CE-1, CE-3, CE-8)
        [Test]
        public void CameraRayAndBoundaryGuards()
        {
            ValleyLayout flat8 = ValleyTestFixtures.CreateFlat8();
            ExplorationSession session = new ExplorationSession(flat8, ExplorationTuning.Default);
            CameraModel cam = session.Camera;

            // Center viewport ray intersects y = 0 at Focus.
            CameraRay centerRay = cam.ViewportPointToRay(0.5f, 0.5f, CameraModel.DefaultAspect);
            float tCenter = -centerRay.Origin.Y / centerRay.Direction.Y;
            Vec3 centerHit = centerRay.Origin + centerRay.Direction * tCenter;
            Assert.That(centerHit.X, Is.EqualTo(cam.Focus.X).Within(1e-4f));
            Assert.That(centerHit.Z, Is.EqualTo(cam.Focus.Y).Within(1e-4f));

            // Top viewport ray intersects y = 0 at Focus.Z + OrthoSize * Sqrt2.
            CameraRay topRay = cam.ViewportPointToRay(0.5f, 1f, CameraModel.DefaultAspect);
            float tTop = -topRay.Origin.Y / topRay.Direction.Y;
            Vec3 topHit = topRay.Origin + topRay.Direction * tTop;
            Assert.That(topHit.Z, Is.EqualTo(cam.Focus.Y + cam.OrthoSize * ValleyConstants.Sqrt2).Within(1e-4f));

            // dt > 0.1s clamps to 0.1s; dt <= 0 and ZoomNotch(0) are no-ops.
            Vec2 startFocus = cam.Focus;
            cam.PanKeys(new PanInput(0, 1, 0, 0), 0.5f, CameraModel.DefaultAspect);
            float expectedDelta = ExplorationTuning.DefaultPanSpeed * 2f * cam.OrthoSize * 0.1f;
            Assert.That(cam.Focus.X - startFocus.X, Is.EqualTo(expectedDelta).Within(1e-4f));

            Vec2 beforeZeroDt = cam.Focus;
            cam.PanKeys(new PanInput(0, 1, 0, 0), 0f, CameraModel.DefaultAspect);
            Assert.That(cam.Focus, Is.EqualTo(beforeZeroDt));

            float beforeZeroZoom = cam.OrthoSize;
            cam.ZoomNotch(0, CameraModel.DefaultAspect);
            Assert.That(cam.OrthoSize, Is.EqualTo(beforeZeroZoom));

            // Invalid aspect throws ArgumentOutOfRangeException.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => cam.Clamp(0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => cam.Clamp(float.NaN));

            // ClickViewport routes to Placeholder and sets MarkerVisible.
            session.ClickViewport(0.5f, 0.5f, CameraModel.DefaultAspect);
            Assert.That(session.MarkerVisible, Is.True);
            Assert.That(session.Marker.Sequence, Is.EqualTo(1));
        }

        private static void VerifyEdgeLimitsForAspectAndZoom(float aspect, float targetZoom)
        {
            CameraModel cam = new CameraModel(ExplorationTuning.Default, aspect);
            while (cam.OrthoSize > targetZoom + 1e-4f)
            {
                float prev = cam.OrthoSize;
                cam.ZoomNotch(1, aspect);
                if (cam.OrthoSize == prev)
                {
                    break;
                }
            }
            while (cam.OrthoSize < targetZoom - 1e-4f)
            {
                float prev = cam.OrthoSize;
                cam.ZoomNotch(-1, aspect);
                if (cam.OrthoSize == prev)
                {
                    break;
                }
            }

            float halfH = cam.OrthoSize;
            float halfW = halfH * aspect;
            float halfZ = halfH * ValleyConstants.Sqrt2;

            PanToEdge(cam, new PanInput(1, 0, 0, 0), aspect);
            Assert.That(cam.Focus.X, Is.EqualTo(halfW).Within(1e-4f));

            PanToEdge(cam, new PanInput(0, 1, 0, 0), aspect);
            Assert.That(cam.Focus.X, Is.EqualTo(160f - halfW).Within(1e-4f));

            PanToEdge(cam, new PanInput(0, 0, 0, 1), aspect);
            Assert.That(cam.Focus.Y, Is.EqualTo(halfZ).Within(1e-4f));
            Assert.That(cam.Focus.Y - halfZ, Is.GreaterThanOrEqualTo(-1e-4f));

            PanToEdge(cam, new PanInput(0, 0, 1, 0), aspect);
            Assert.That(cam.Focus.Y, Is.EqualTo(109f - halfZ).Within(1e-4f));
            Assert.That(cam.Focus.Y + halfZ, Is.LessThanOrEqualTo(109f + 1e-4f));
        }

        private static void PanToEdge(CameraModel cam, PanInput input, float aspect)
        {
            for (int i = 0; i < 200; i++)
            {
                cam.PanKeys(input, 0.1f, aspect);
            }
        }

        private static void ApplyZoomSteps(CameraModel cam, int signedNotches, float aspect)
        {
            int dir = signedNotches > 0 ? -1 : 1;
            int count = System.Math.Abs(signedNotches);
            for (int i = 0; i < count; i++)
            {
                cam.ZoomNotch(dir, aspect);
            }
        }
    }
}
