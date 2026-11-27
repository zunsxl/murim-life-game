using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class ProjectionTests
    {
        // T-CORE-12
        [Test]
        public void Projection_MatchesExactVectors()
        {
            Assert.That(Projection.PixelsPerMetre, Is.EqualTo(128));

            Assert.That(Projection.PlaneVCm(0, 0), Is.EqualTo(0));
            Assert.That(Projection.PlaneVCm(51200, 0), Is.EqualTo(30720));
            Assert.That(Projection.PlaneVCm(100, 100), Is.EqualTo(140));
            Assert.That(Projection.PlaneVCm(21500, 1380), Is.EqualTo(14004));
            Assert.That(Projection.PlaneVCm(1, 1), Is.EqualTo(1));
            Assert.That(Projection.PlaneVCm(-400, 100), Is.EqualTo(-160));
            Assert.That(Projection.PlaneVCm(-1, 0), Is.EqualTo(-1));

            Assert.That(Projection.ReliefQ(0, 0), Is.EqualTo(1024));
            Assert.That(Projection.ReliefQ(60, 0), Is.EqualTo(1204));
            Assert.That(Projection.ReliefQ(100, 0), Is.EqualTo(1204));
            Assert.That(Projection.ReliefQ(-60, 0), Is.EqualTo(844));
            Assert.That(Projection.ReliefQ(0, 50), Is.EqualTo(924));
            Assert.That(Projection.ReliefQ(20, 30), Is.EqualTo(1024));
            Assert.That(Projection.ReliefQ(0, -200), Is.EqualTo(1204));
        }

        // T-CORE-12
        [Test]
        public void ViewFrame_Sweep_MatchesProjectionAndWaterfallBounds()
        {
            foreach (ValleyWorld world in WorldTestHelpers.GetSweepWorlds())
            {
                AssertViewFrameInvariants(world);
            }
        }

        internal static void AssertViewFrameInvariants(ValleyWorld world)
        {
            int expectedBottom = int.MinValue;
            int expectedCrest = int.MaxValue;
            int northRow = ValleyWorld.SizeM * ValleyWorld.VertsPerSide;

            for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
            {
                int b = Projection.PlaneVCm(0, world.HeightCm[x]);
                if (b > expectedBottom)
                {
                    expectedBottom = b;
                }

                int c = Projection.PlaneVCm(51200, world.HeightCm[northRow + x]);
                if (c < expectedCrest)
                {
                    expectedCrest = c;
                }
            }

            Assert.That(world.PlaneBottomCm, Is.EqualTo(expectedBottom), $"seed={world.Seed}");
            Assert.That(world.PlaneCrestCm, Is.EqualTo(expectedCrest), $"seed={world.Seed}");
            Assert.That(world.PlaneBottomCm, Is.LessThanOrEqualTo(320), $"seed={world.Seed}");
            Assert.That(world.PlaneCrestCm, Is.GreaterThanOrEqualTo(30720), $"seed={world.Seed}");

            Assert.That(world.VistaUCm, Is.EqualTo(world.Layout.WfX * 100), $"seed={world.Seed}");
            int expectedVistaV = Projection.PlaneVCm((world.Layout.FallZ + 3) * 100, world.Brook[0].LevelCm) - 440;
            Assert.That(world.VistaVCm, Is.EqualTo(expectedVistaV), $"seed={world.Seed}");
            Assert.That(world.VistaVCm, Is.InRange(world.PlaneBottomCm + 2200, world.PlaneCrestCm - 2200), $"seed={world.Seed}");

            FallNode first = world.Waterfall[0];
            FallNode last = world.Waterfall[world.Waterfall.Count - 1];
            int firstV = Projection.PlaneVCm(first.ZCm, first.YCm);
            int lastV = Projection.PlaneVCm(last.ZCm, last.YCm);
            Assert.That(firstV, Is.InRange(world.VistaVCm - 2200, world.VistaVCm + 2200), $"seed={world.Seed}");
            Assert.That(lastV, Is.InRange(world.VistaVCm - 2200, world.VistaVCm + 2200), $"seed={world.Seed}");
        }
    }
}
