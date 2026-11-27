using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class WorldWaterTests
    {
        // T-CORE-08
        [Test]
        public void Water_Sweep_MeetsStreamBrookPoolAndBankSpecs()
        {
            foreach (ValleyWorld world in WorldTestHelpers.GetSweepWorlds())
            {
                AssertWaterInvariants(world);
            }
        }

        internal static void AssertWaterInvariants(ValleyWorld world)
        {
            Assert.That(world.Stream.Length, Is.GreaterThanOrEqualTo(80), $"seed={world.Seed}");
            Assert.That(world.Brook.Length, Is.EqualTo(16), $"seed={world.Seed}");
            Assert.That(world.Waterfall.Length, Is.EqualTo(13), $"seed={world.Seed}");
            Assert.That(ValleyWorld.WaterfallWidthCm, Is.EqualTo(300));

            int brookLevel = world.Brook[0].LevelCm;
            int plungeLevel = world.PlungePool.LevelCm;
            int midCm = world.Config.MidTierHeightM * 100;
            Assert.That(brookLevel - plungeLevel, Is.InRange(midCm - 100, midCm - 30), $"seed={world.Seed}");

            Assert.That(world.Stream[world.Stream.Length - 1].ZCm, Is.LessThanOrEqualTo(0), $"seed={world.Seed}");

            for (int i = 0; i < world.Stream.Length; i++)
            {
                WaterNode n = world.Stream[i];
                if (i > 0)
                {
                    Assert.That(n.LevelCm, Is.LessThanOrEqualTo(world.Stream[i - 1].LevelCm), $"seed={world.Seed} node={i}");
                }

                if (n.XCm >= 0 && n.XCm <= 51200 && n.ZCm >= 0 && n.ZCm <= 51200)
                {
                    int vx = Primitives.Clamp(Primitives.FloorDiv(n.XCm + 50, 100), 0, ValleyWorld.SizeM);
                    int vz = Primitives.Clamp(Primitives.FloorDiv(n.ZCm + 50, 100), 0, ValleyWorld.SizeM);
                    int idx = vz * ValleyWorld.VertsPerSide + vx;
                    Assert.That(world.TierLevelPct[idx], Is.LessThanOrEqualTo(5), $"seed={world.Seed} stream tier node={i}");
                    Assert.That(world.HeightCm[idx], Is.LessThanOrEqualTo(n.LevelCm - 50), $"seed={world.Seed} stream bed node={i}");
                }
            }

            AssertStreamBankHeights(world);
        }

        private static void AssertStreamBankHeights(ValleyWorld world)
        {
            int streamMinX = int.MaxValue;
            int streamMaxX = int.MinValue;
            int streamMinZ = int.MaxValue;
            int streamMaxZ = int.MinValue;
            foreach (WaterNode n in world.Stream)
            {
                if (n.XCm < streamMinX) streamMinX = n.XCm;
                if (n.XCm > streamMaxX) streamMaxX = n.XCm;
                if (n.ZCm < streamMinZ) streamMinZ = n.ZCm;
                if (n.ZCm > streamMaxZ) streamMaxZ = n.ZCm;
            }

            int xStart = Primitives.Clamp(Primitives.FloorDiv(streamMinX - 1000, 100), 0, ValleyWorld.SizeM);
            int xEnd = Primitives.Clamp(Primitives.FloorDiv(streamMaxX + 1000, 100), 0, ValleyWorld.SizeM);
            int zStart = Primitives.Clamp(Primitives.FloorDiv(streamMinZ - 1000, 100), 0, ValleyWorld.SizeM);
            int zEnd = Primitives.Clamp(Primitives.FloorDiv(streamMaxZ + 1000, 100), 0, ValleyWorld.SizeM);

            for (int z = zStart; z <= zEnd; z++)
            {
                int pz = z * 100;
                int row = z * ValleyWorld.VertsPerSide;
                for (int x = xStart; x <= xEnd; x++)
                {
                    int px = x * 100;
                    int bestE = int.MaxValue;
                    int bestLevel = 0;
                    for (int i = 0; i < world.Stream.Length - 1; i++)
                    {
                        WaterNode a = world.Stream[i];
                        WaterNode b = world.Stream[i + 1];
                        int maxHw = a.HalfWidthCm > b.HalfWidthCm ? a.HalfWidthCm : b.HalfWidthCm;
                        int segBound = (bestE < 600 ? bestE : 600) + maxHw;
                        int minX = a.XCm < b.XCm ? a.XCm : b.XCm;
                        int maxX = a.XCm > b.XCm ? a.XCm : b.XCm;
                        int minZ = a.ZCm < b.ZCm ? a.ZCm : b.ZCm;
                        int maxZ = a.ZCm > b.ZCm ? a.ZCm : b.ZCm;
                        if (px < minX - segBound || px > maxX + segBound || pz < minZ - segBound || pz > maxZ + segBound)
                        {
                            continue;
                        }

                        int e = WorldTestHelpers.SegmentEdgeDistanceCm(px, pz, a, b, out int lvl);
                        if (e < bestE)
                        {
                            bestE = e;
                            bestLevel = lvl;
                        }
                    }

                    if (bestE >= 300 && bestE <= 600)
                    {
                        long poolDx = px - world.PlungePool.XCm;
                        long poolDz = pz - world.PlungePool.ZCm;
                        int poolE = (int)Primitives.Isqrt(poolDx * poolDx + poolDz * poolDz) - world.PlungePool.RadiusCm;
                        if (poolE >= 300)
                        {
                            Assert.That(world.HeightCm[row + x], Is.GreaterThanOrEqualTo(bestLevel + 15), $"seed={world.Seed} ({x},{z})");
                        }
                    }
                }
            }
        }
    }
}
