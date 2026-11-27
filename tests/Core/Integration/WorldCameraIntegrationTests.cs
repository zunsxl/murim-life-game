using System;
using Murim.Core.Camera;
using Murim.Core.Tests.World;
using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.Integration
{
    [TestFixture]
    public class WorldCameraIntegrationTests
    {
        private const float Dt60Hz = 1f / 60f;
        private static readonly uint[] DefaultSeeds = { 0u, 1u, 2u, 3u, 4u, 5u, 6u, 20261127u };
        private static readonly float[] TestHeights = { 12f, 30f, 44f, 88f };
        private static readonly float[] TestAspects = { 4f / 3f, 16f / 10f, 16f / 9f, 21f / 9f, 32f / 9f };
        private static readonly int[] TestScreenHeights = { 540, 1080, 1440, 2160 };

        private static readonly (float DirU, float DirV)[] ExtremeRequests =
        {
            (0f, 0f),
            (-1f, 0f),
            (1f, 0f),
            (0f, -1f),
            (0f, 1f),
            (-1f, -1f),
            (1f, -1f),
            (-1f, 1f),
            (1f, 1f),
            (-1f, 0.5f),
            (1f, 0.5f),
            (-1f, -0.5f),
            (1f, -0.5f),
        };

        // T-CORE-29
        [Test]
        public void T_CORE_29_ExtremeRequestsAndRandomWalks_NeverExposeVoid()
        {
            ValleyWorld[] valleys = BuildIntegrationValleys();

            foreach (ValleyWorld world in valleys)
            {
                var frame = new ViewFrame(world.PlaneBottomCm, world.PlaneCrestCm, world.VistaUCm, world.VistaVCm);
                VerifyExtremeRequests(world, frame);
                VerifyRandomWalk(world, frame);
            }
        }

        // T-BAL-02
        [Test]
        public void T_BAL_02_ValleyFloorScaleNorthBackdropAndVistaPose_MatchBalanceBudgets()
        {
            const float aspect16x9 = 16f / 9f;
            float floorWidthM = ValleyWorld.FloorEastM - ValleyWorld.FloorWestM;
            float floorScreens = floorWidthM / (CameraConfig.Default.MaxViewHeightM * aspect16x9);
            Assert.That(floorScreens, Is.InRange(2.0f, 3.0f));

            CameraConfigData northConfigData = CameraConfig.Default.ToData();
            northConfigData.DefaultViewHeightM = 88f;
            northConfigData.PanSpeedPerViewHeight = 3.0f;
            Assert.That(CameraConfig.TryCreate(in northConfigData, out CameraConfig northConfig, out _), Is.True);

            foreach (uint seed in DefaultSeeds)
            {
                ValleyWorld world = ValleyGenerator.Generate(seed, WorldConfig.Default);
                var frame = new ViewFrame(world.PlaneBottomCm, world.PlaneCrestCm, world.VistaUCm, world.VistaVCm);

                // Vista pose at H = 44 lies strictly within [PlaneBottomCm, PlaneCrestCm].
                var vistaRig = new CameraRig(frame, CameraConfig.Default, aspect16x9, 1080);
                Assert.That(vistaRig.H, Is.EqualTo(44f).Within(1e-4f));
                Assert.That(vistaRig.Cv - vistaRig.H * 0.5f, Is.GreaterThanOrEqualTo(world.PlaneBottomCm / 100f - 1e-4f));
                Assert.That(vistaRig.Cv + vistaRig.H * 0.5f, Is.LessThanOrEqualTo(world.PlaneCrestCm / 100f + 1e-4f));

                // North limit at H = 88 and Cu = 256: centre column backdrop fraction in [0.20, 0.30].
                var northRig = new CameraRig(frame, northConfig, aspect16x9, 1080);
                // Move Cu to 256 via drag, then pan straight north to the north limit.
                DragToward(northRig, aspect16x9, 1080, 256f - northRig.Cu, 0f);
                for (int i = 0; i < 180; i++)
                {
                    var panNorth = new CameraInput
                    {
                        PanNorth = true,
                        PointerInWindow = true,
                        Aspect = aspect16x9,
                        ScreenHeightPx = 1080,
                    };
                    northRig.Tick(in panNorth, Dt60Hz);
                }

                int centreCrestHeightCm = world.HeightCm[ValleyWorld.SizeM * ValleyWorld.VertsPerSide + 256];
                float centreCrestV = Projection.PlaneVCm(ValleyWorld.SizeM * 100, centreCrestHeightCm) / 100f;
                float viewTop = northRig.Cv + northRig.H * 0.5f;
                float backdropFraction = (viewTop - centreCrestV) / northRig.H;
                Assert.That(backdropFraction, Is.InRange(0.20f, 0.30f));
            }
        }

        private static ValleyWorld[] BuildIntegrationValleys()
        {
            var valleys = new ValleyWorld[DefaultSeeds.Length + 2];
            for (int i = 0; i < DefaultSeeds.Length; i++)
            {
                valleys[i] = ValleyGenerator.Generate(DefaultSeeds[i], WorldConfig.Default);
            }

            WorldConfig minConfig = WorldTestHelpers.MakeConfig(8, 18, 20, 22, 25, 25, 25);
            WorldConfig maxConfig = WorldTestHelpers.MakeConfig(16, 34, 40, 32, 150, 150, 150);
            valleys[DefaultSeeds.Length] = ValleyGenerator.Generate(1u, minConfig);
            valleys[DefaultSeeds.Length + 1] = ValleyGenerator.Generate(2u, maxConfig);
            return valleys;
        }

        private static void VerifyExtremeRequests(ValleyWorld world, ViewFrame frame)
        {
            foreach (float h in TestHeights)
            {
                CameraConfigData cfgData = CameraConfig.Default.ToData();
                cfgData.DefaultViewHeightM = h;
                cfgData.PanSpeedPerViewHeight = 3.0f;
                Assert.That(CameraConfig.TryCreate(in cfgData, out CameraConfig cfg, out _), Is.True);

                foreach (float aspect in TestAspects)
                {
                    foreach ((float dirU, float dirV) in ExtremeRequests)
                    {
                        var rig = new CameraRig(frame, cfg, aspect, 1080);
                        PushToExtreme(rig, aspect, dirU, dirV);
                        AssertViewInsideWorld(world, frame, rig.Pose, aspect);
                    }
                }
            }
        }

        private static void PushToExtreme(CameraRig rig, float aspect, float dirU, float dirV)
        {
            if (dirU != 0f || dirV != 0f)
            {
                for (int step = 0; step < 35; step++)
                {
                    var grab = new CameraInput
                    {
                        DragHeld = true,
                        PointerNdcX = 0f,
                        PointerNdcY = 0f,
                        PointerInWindow = true,
                        Aspect = aspect,
                        ScreenHeightPx = 1080,
                    };
                    rig.Tick(in grab, Dt60Hz);

                    var pull = new CameraInput
                    {
                        DragHeld = true,
                        PointerNdcX = -dirU,
                        PointerNdcY = -dirV,
                        PointerInWindow = true,
                        Aspect = aspect,
                        ScreenHeightPx = 1080,
                    };
                    rig.Tick(in pull, Dt60Hz);

                    var release = new CameraInput
                    {
                        DragHeld = false,
                        PointerInWindow = true,
                        Aspect = aspect,
                        ScreenHeightPx = 1080,
                    };
                    rig.Tick(in release, Dt60Hz);
                }
            }

            var settle = new CameraInput
            {
                PointerInWindow = true,
                Aspect = aspect,
                ScreenHeightPx = 1080,
            };
            for (int i = 0; i < 45; i++)
            {
                rig.Tick(in settle, Dt60Hz);
            }
        }

        private static void VerifyRandomWalk(ValleyWorld world, ViewFrame frame)
        {
            var rng = new Pcg32(world.Seed + 1000u, 1u);
            float aspect = 16f / 9f;
            int screenH = 1080;
            var rig = new CameraRig(frame, CameraConfig.Default, aspect, screenH);

            for (int tick = 0; tick < 5000; tick++)
            {
                if (rng.Bounded(50u) == 0u)
                {
                    aspect = TestAspects[(int)rng.Bounded((uint)TestAspects.Length)];
                    screenH = TestScreenHeights[(int)rng.Bounded((uint)TestScreenHeights.Length)];
                }

                float ndcX = (rng.R(-120, 120)) / 100f;
                float ndcY = (rng.R(-120, 120)) / 100f;
                bool inWin = MathF.Abs(ndcX) <= 1f && MathF.Abs(ndcY) <= 1f;

                var input = new CameraInput
                {
                    PanNorth = rng.Bounded(4u) == 0u,
                    PanSouth = rng.Bounded(4u) == 0u,
                    PanWest = rng.Bounded(4u) == 0u,
                    PanEast = rng.Bounded(4u) == 0u,
                    DragHeld = rng.Bounded(6u) == 0u,
                    ResetPressed = rng.Bounded(120u) == 0u,
                    ScrollNotches = rng.Bounded(10u) == 0u ? rng.R(-2, 2) : 0f,
                    PointerNdcX = ndcX,
                    PointerNdcY = ndcY,
                    PointerInWindow = inWin,
                    Aspect = aspect,
                    ScreenHeightPx = screenH,
                    Blocked = rng.Bounded(60u) == 0u,
                };

                rig.Tick(in input, Dt60Hz);
                AssertViewInsideWorld(world, frame, rig.Pose, aspect);
            }
        }

        private static void DragToward(CameraRig rig, float aspect, int screenH, float deltaU, float deltaV)
        {
            float w = rig.H * aspect;
            float ndcShiftU = -2f * deltaU / w;
            float ndcShiftV = -2f * deltaV / rig.H;
            var grab = new CameraInput
            {
                DragHeld = true,
                PointerNdcX = 0f,
                PointerNdcY = 0f,
                PointerInWindow = true,
                Aspect = aspect,
                ScreenHeightPx = screenH,
            };
            rig.Tick(in grab, Dt60Hz);

            var move = new CameraInput
            {
                DragHeld = true,
                PointerNdcX = ndcShiftU,
                PointerNdcY = ndcShiftV,
                PointerInWindow = true,
                Aspect = aspect,
                ScreenHeightPx = screenH,
            };
            rig.Tick(in move, Dt60Hz);

            var release = new CameraInput
            {
                PointerInWindow = true,
                Aspect = aspect,
                ScreenHeightPx = screenH,
            };
            for (int i = 0; i < 60; i++)
            {
                rig.Tick(in release, Dt60Hz);
            }
        }

        private static void AssertViewInsideWorld(ValleyWorld world, ViewFrame frame, CameraPose pose, float aspect)
        {
            float halfW = pose.ViewWidth(aspect) * 0.5f;
            float halfH = pose.H * 0.5f;
            float uLeft = pose.Cu - halfW;
            float uRight = pose.Cu + halfW;
            float vBottom = pose.Cv - halfH;
            float vTop = pose.Cv + halfH;

            Assert.That(uLeft, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(uRight, Is.LessThanOrEqualTo(ValleyWorld.SizeM + 1e-4f));

            float maxTop = world.PlaneCrestCm / 100f + ViewFrame.SkyFraction * pose.H;
            Assert.That(vTop, Is.LessThanOrEqualTo(maxTop + 1e-4f));

            int xStart = (int)MathF.Ceiling(MathF.Max(0f, uLeft));
            int xEnd = (int)MathF.Floor(MathF.Min(ValleyWorld.SizeM, uRight));
            for (int x = xStart; x <= xEnd; x++)
            {
                float groundBottomV = Projection.PlaneVCm(0, world.HeightCm[x]) / 100f;
                Assert.That(vBottom, Is.GreaterThanOrEqualTo(groundBottomV - 1e-4f));
            }
        }
    }
}
