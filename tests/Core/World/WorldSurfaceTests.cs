using System;
using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class WorldSurfaceTests
    {
        // T-CORE-10
        [Test]
        public void SurfaceWeights_Sweep_MeetLayerAndSumSpecs()
        {
            foreach (ValleyWorld world in WorldTestHelpers.GetSweepWorlds())
            {
                AssertSurfaceInvariants(world);
            }
        }

        // T-CORE-10
        [Test]
        public void ComputeGradientAndGradePct_MatchFormulaAndBoundaryClamping()
        {
            var heights = new short[ValleyWorld.VertexCount];
            heights[20 * ValleyWorld.VertsPerSide + 9] = 100;
            heights[20 * ValleyWorld.VertsPerSide + 11] = 220;
            heights[19 * ValleyWorld.VertsPerSide + 10] = 50;
            heights[21 * ValleyWorld.VertsPerSide + 10] = 210;

            ValleySurface.ComputeGradient(heights, 10, 20, out int gx, out int gz);
            Assert.That(gx, Is.EqualTo(120));
            Assert.That(gz, Is.EqualTo(160));
            Assert.That(ValleySurface.ComputeGradePct(heights, 10, 20), Is.EqualTo(100));

            heights[0] = 10;
            heights[1] = 70;
            heights[ValleyWorld.VertsPerSide] = 90;
            ValleySurface.ComputeGradient(heights, 0, 0, out int edgeGx, out int edgeGz);
            Assert.That(edgeGx, Is.EqualTo(60));
            Assert.That(edgeGz, Is.EqualTo(80));
            Assert.That(ValleySurface.ComputeGradePct(heights, 0, 0), Is.EqualTo(50));

            int maxRow = ValleyWorld.SizeM * ValleyWorld.VertsPerSide;
            heights[maxRow + ValleyWorld.SizeM - 1] = 100;
            heights[maxRow + ValleyWorld.SizeM] = 400;
            heights[(ValleyWorld.SizeM - 1) * ValleyWorld.VertsPerSide + ValleyWorld.SizeM] = 100;
            ValleySurface.ComputeGradient(heights, ValleyWorld.SizeM, ValleyWorld.SizeM, out int topGx, out int topGz);
            Assert.That(topGx, Is.EqualTo(300));
            Assert.That(topGz, Is.EqualTo(300));
            Assert.That(ValleySurface.ComputeGradePct(heights, ValleyWorld.SizeM, ValleyWorld.SizeM), Is.EqualTo(212));

            Assert.Throws<ArgumentNullException>(() => ValleySurface.ComputeGradient(null, 0, 0, out _, out _));
            Assert.Throws<ArgumentNullException>(() => ValleySurface.ComputeGradePct(null, 0, 0));
        }

        private static void AssertSurfaceInvariants(ValleyWorld world)
        {
            int plateauCount = 0;
            int plateauTurfCount = 0;

            for (int z = 0; z < ValleyWorld.VertsPerSide; z++)
            {
                int row = z * ValleyWorld.VertsPerSide;
                for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
                {
                    int idx = row + x;
                    int wBase = idx * ValleyWorld.SurfaceLayerCount;
                    int sum = 0;
                    for (int k = 0; k < ValleyWorld.SurfaceLayerCount; k++)
                    {
                        sum += world.SurfaceWeights[wBase + k];
                    }

                    Assert.That(sum, Is.EqualTo(255), $"seed={world.Seed} ({x},{z}) weight sum");

                    int grade = WorldTestHelpers.ComputeGradePct(world.HeightCm, x, z);
                    int wd = WorldTestHelpers.WaterEdgeDistanceCm(world, x * 100, z * 100);
                    bool dry = wd >= 350;

                    if (dry && world.RidgePct[idx] >= 80 && grade < 40)
                    {
                        Assert.That(world.SurfaceWeights[wBase + 7], Is.GreaterThanOrEqualTo(200), $"seed={world.Seed} ({x},{z}) ridge forest");
                    }

                    if (dry && grade >= 100)
                    {
                        int bankSum = world.SurfaceWeights[wBase + 4] + world.SurfaceWeights[wBase + 5];
                        Assert.That(bankSum, Is.GreaterThanOrEqualTo(180), $"seed={world.Seed} ({x},{z}) bank face");
                    }

                    if (z >= 56 && world.RidgePct[idx] == 0)
                    {
                        byte tl = world.TierLevelPct[idx];
                        if (tl <= 5 || (tl >= 95 && tl <= 105))
                        {
                            plateauCount++;
                            if (world.SurfaceWeights[wBase + 0] >= 128)
                            {
                                plateauTurfCount++;
                            }
                        }
                    }
                }
            }

            foreach (WaterNode n in world.Stream)
            {
                if (n.XCm >= 0 && n.XCm <= 51200 && n.ZCm >= 0 && n.ZCm <= 51200)
                {
                    int vx = Primitives.Clamp(Primitives.FloorDiv(n.XCm + 50, 100), 0, ValleyWorld.SizeM);
                    int vz = Primitives.Clamp(Primitives.FloorDiv(n.ZCm + 50, 100), 0, ValleyWorld.SizeM);
                    int wBase = (vz * ValleyWorld.VertsPerSide + vx) * ValleyWorld.SurfaceLayerCount;
                    Assert.That(world.SurfaceWeights[wBase + 3], Is.GreaterThanOrEqualTo(200), $"seed={world.Seed} pebble bank");
                }
            }

            Assert.That(plateauTurfCount * 100.0 / plateauCount, Is.GreaterThanOrEqualTo(35.0), $"seed={world.Seed} plateau turf pct");
        }
    }
}
