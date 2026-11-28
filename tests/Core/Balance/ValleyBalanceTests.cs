using Murim.Core.Tuning;
using Murim.Core.Valley;
using NUnit.Framework;

namespace Murim.Core.Tests.Balance
{
    [TestFixture]
    public sealed class ValleyBalanceTests
    {
        [Test]
        public void T_BAL_01_ValleySpanToDefaultViewWidthRatio()
        {
            ValleyLayout layout = ValleyLayoutData.CreateLayout();
            ExplorationTuning tuning = ExplorationTuning.Default;
            float aspect16By9 = 16f / 9f;
            float viewWidth = 2f * tuning.DefaultOrthoSize * aspect16By9;
            float ratio = layout.Width / viewWidth;

            Assert.That(viewWidth, Is.EqualTo(40f).Within(1e-4f));
            Assert.That(ratio, Is.EqualTo(4.0f).Within(1e-4f));
            Assert.That(ratio, Is.InRange(3.0f, 5.0f));
        }

        [Test]
        public void T_BAL_02_DefaultExplorationTraversalTimings()
        {
            ExplorationTuning tuning = ExplorationTuning.Default;
            float viewWidth = 2f * tuning.DefaultOrthoSize * (16f / 9f);
            float walkAcrossViewSeconds = viewWidth / tuning.WalkSpeed;
            float airBoundsWidth = ValleyConstants.AuthoredAirBounds.MaxX - ValleyConstants.AuthoredAirBounds.MinX;
            float flyAcrossBoundsSeconds = airBoundsWidth / tuning.AirSpeed;
            float layerStep = ValleyConstants.AirLayer2Altitude - ValleyConstants.AirLayer1Altitude;
            float layerChangeSeconds = layerStep / tuning.VerticalSpeed;

            Assert.That(walkAcrossViewSeconds, Is.EqualTo(8.0f).Within(1e-4f));
            Assert.That(flyAcrossBoundsSeconds, Is.EqualTo(18.0f).Within(1e-4f));
            Assert.That(layerChangeSeconds, Is.LessThanOrEqualTo(0.75f + 1e-5f));
        }

        [Test]
        public void T_BAL_03_AirLayerClearancesAndSolidTop()
        {
            // Each layer clears the highest floor strictly below it by >= 2 m
            Assert.That(ValleyConstants.AirLayer1Altitude - 3f, Is.GreaterThanOrEqualTo(2f));
            Assert.That(ValleyConstants.AirLayer2Altitude - 9f, Is.GreaterThanOrEqualTo(2f));
            Assert.That(ValleyConstants.AirLayer3Altitude - 9f, Is.GreaterThanOrEqualTo(2f));

            ValleyLayout layout = ValleyLayoutData.CreateLayout();
            float maxTerrain = 0f;
            float maxSolidTopInBounds = 0f;

            for (int vz = 0; vz < layout.VertexDepth; vz++)
            {
                for (int vx = 0; vx < layout.VertexWidth; vx++)
                {
                    float h = layout.VertexHeight(vx, vz);
                    if (h > maxTerrain)
                    {
                        maxTerrain = h;
                    }
                }
            }

            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    if (layout.AirBounds.ContainsCellCenter(cx, cz))
                    {
                        float st = layout.SolidTop(cx, cz);
                        if (st > maxSolidTopInBounds)
                        {
                            maxSolidTopInBounds = st;
                        }
                    }
                }
            }

            Assert.That(ValleyConstants.AirLayer3Altitude - maxTerrain, Is.GreaterThanOrEqualTo(4f));
            Assert.That(ValleyConstants.AirLayer3Altitude - maxSolidTopInBounds, Is.GreaterThanOrEqualTo(ValleyConstants.MinAirClearance));
        }

        [Test]
        public void T_BAL_04_SlopeGradientAndCliffBandRise()
        {
            ValleyLayout layout = ValleyLayoutData.CreateLayout();

            (int x0, int x1, int z0, int z1)[] slopes =
            {
                (52, 60, 40, 46),
                (100, 106, 40, 46),
                (80, 86, 58, 64),
                (40, 46, 72, 78)
            };

            foreach ((int x0, int x1, int z0, int z1) in slopes)
            {
                for (int cz = z0; cz < z1; cz++)
                {
                    for (int cx = x0; cx < x1; cx++)
                    {
                        float southH = layout.VertexHeight(cx, cz);
                        float northH = layout.VertexHeight(cx, cz + 1);
                        Assert.That(northH - southH, Is.EqualTo(0.5f).Within(1e-5f));
                        Assert.That(layout.VertexHeight(cx + 1, cz), Is.EqualTo(southH).Within(1e-5f));
                        Assert.That(layout.VertexHeight(cx + 1, cz + 1), Is.EqualTo(northH).Within(1e-5f));
                    }
                }
            }

            // Cliff band 1 (z=43..45), 2 (z=61..63), 3 (z=75..77, 24..116 east of West massif and west of East massif) excluding slopes
            AssertCliffBandRise(layout, 16, 124, 43, 0f, 3f);
            AssertCliffBandRise(layout, 24, 116, 61, 3f, 6f);
            AssertCliffBandRise(layout, 24, 116, 75, 6f, 9f);
        }

        private static void AssertCliffBandRise(ValleyLayout layout, int x0, int x1, int czStart, float lowH, float highH)
        {
            for (int cx = x0; cx < x1; cx++)
            {
                if (ValleyKindMetadata.IsSlopeKind(layout.KindAt(cx, czStart))
                    || ValleyKindMetadata.IsSlopeKind(layout.KindAt(cx - 1, czStart))
                    || ValleyKindMetadata.IsSlopeKind(layout.KindAt(cx + 1, czStart)))
                {
                    continue;
                }
                Assert.That(layout.VertexHeight(cx, czStart), Is.EqualTo(lowH).Within(1e-5f));
                Assert.That(layout.VertexHeight(cx + 1, czStart), Is.EqualTo(lowH).Within(1e-5f));
                Assert.That(layout.VertexHeight(cx, czStart + 2), Is.EqualTo(highH).Within(1e-5f));
                Assert.That(layout.VertexHeight(cx + 1, czStart + 2), Is.EqualTo(highH).Within(1e-5f));
                Assert.That(layout.KindAt(cx, czStart), Is.EqualTo(CellKind.Cliff));
                Assert.That(layout.KindAt(cx, czStart + 1), Is.EqualTo(CellKind.Cliff));
            }
        }
    }
}
