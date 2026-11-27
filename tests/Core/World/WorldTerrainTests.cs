using System;
using System.Collections.Generic;
using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class WorldTerrainTests
    {
        // T-CORE-06
        [Test]
        public void TierField_Sweep_HasThreePlateausAndMonotonicNorthward()
        {
            foreach (ValleyWorld world in WorldTestHelpers.GetSweepWorlds())
            {
                AssertTierInvariants(world);
            }
        }

        // T-CORE-07
        [Test]
        public void EmbankmentsAndRamps_Sweep_MeetSteepnessAndSlopeSpecs()
        {
            foreach (ValleyWorld world in WorldTestHelpers.GetSweepWorlds())
            {
                AssertEmbankmentAndRampInvariants(world);
            }
        }

        // T-CORE-09
        [Test]
        public void BoundsAndRidges_Sweep_MeetHeightAndRidgeSpecs()
        {
            foreach (ValleyWorld world in WorldTestHelpers.GetSweepWorlds())
            {
                AssertBoundsAndRidges(world);
            }
        }

        internal static void AssertTierInvariants(ValleyWorld world)
        {
            var basin = new List<int>(40000);
            var mid = new List<int>(40000);
            var upper = new List<int>(80000);

            for (int z = 56; z < ValleyWorld.VertsPerSide; z++)
            {
                int row = z * ValleyWorld.VertsPerSide;
                for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
                {
                    int idx = row + x;
                    if (world.RidgePct[idx] != 0)
                    {
                        continue;
                    }

                    byte tl = world.TierLevelPct[idx];
                    short h = world.HeightCm[idx];
                    if (tl <= 5)
                    {
                        basin.Add(h);
                    }
                    else if (tl >= 95 && tl <= 105)
                    {
                        mid.Add(h);
                    }
                    else if (tl >= 195)
                    {
                        upper.Add(h);
                    }
                }
            }

            Assert.That(basin.Count, Is.GreaterThanOrEqualTo(15000), $"seed={world.Seed} basin count");
            Assert.That(mid.Count, Is.GreaterThanOrEqualTo(15000), $"seed={world.Seed} mid count");
            Assert.That(upper.Count, Is.GreaterThanOrEqualTo(15000), $"seed={world.Seed} upper count");

            int medBasin = WorldTestHelpers.Median(basin);
            int medMid = WorldTestHelpers.Median(mid);
            int medUpper = WorldTestHelpers.Median(upper);

            int midCm = world.Config.MidTierHeightM * 100;
            int stepCm = (world.Config.UpperTierHeightM - world.Config.MidTierHeightM) * 100;
            Assert.That(medMid - medBasin, Is.InRange(midCm - 100, midCm + 200), $"seed={world.Seed} mid-basin");
            Assert.That(medUpper - medMid, Is.InRange(stepCm - 100, stepCm + 100), $"seed={world.Seed} upper-mid");

            for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
            {
                for (int z = 1; z < ValleyWorld.VertsPerSide; z++)
                {
                    byte prev = world.TierLevelPct[(z - 1) * ValleyWorld.VertsPerSide + x];
                    byte curr = world.TierLevelPct[z * ValleyWorld.VertsPerSide + x];
                    Assert.That(curr, Is.GreaterThanOrEqualTo(prev), $"seed={world.Seed} x={x} z={z}");
                }
            }
        }

        internal static void AssertEmbankmentAndRampInvariants(ValleyWorld world)
        {
            int steep1 = 0;
            int steep2 = 0;
            for (int z = 56; z < ValleyWorld.VertsPerSide; z++)
            {
                int row = z * ValleyWorld.VertsPerSide;
                for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
                {
                    int idx = row + x;
                    if (world.RidgePct[idx] != 0)
                    {
                        continue;
                    }

                    int grade = WorldTestHelpers.ComputeGradePct(world.HeightCm, x, z);
                    if (grade < 100)
                    {
                        continue;
                    }

                    byte tl = world.TierLevelPct[idx];
                    if (tl > 5 && tl < 95)
                    {
                        steep1++;
                    }
                    else if (tl > 105 && tl < 195)
                    {
                        steep2++;
                    }
                }
            }

            Assert.That(steep1, Is.GreaterThanOrEqualTo(800), $"seed={world.Seed} steep1");
            Assert.That(steep2, Is.GreaterThanOrEqualTo(800), $"seed={world.Seed} steep2");

            for (int k = 0; k < 2; k++)
            {
                SlopeSite s = world.Slopes[k];
                int sx = s.BottomXCm / 100;
                int zBot = s.BottomZCm / 100;
                int zTop = s.TopZCm / 100;

                Assert.That(Math.Abs(sx - world.Layout.WfX), Is.GreaterThanOrEqualTo(60), $"seed={world.Seed} ramp{k} dist from wfX");

                byte tlBot = world.TierLevelPct[zBot * ValleyWorld.VertsPerSide + sx];
                byte tlTop = world.TierLevelPct[zTop * ValleyWorld.VertsPerSide + sx];
                if (k == 0)
                {
                    Assert.That(tlBot, Is.LessThanOrEqualTo(5), $"seed={world.Seed} ramp0 bottom");
                    Assert.That(tlTop, Is.GreaterThanOrEqualTo(95), $"seed={world.Seed} ramp0 top");
                }
                else
                {
                    Assert.That(tlBot, Is.LessThanOrEqualTo(105), $"seed={world.Seed} ramp1 bottom");
                    Assert.That(tlTop, Is.GreaterThanOrEqualTo(195), $"seed={world.Seed} ramp1 top");
                }

                for (int z = zBot; z <= zTop; z++)
                {
                    int waterDist = WorldTestHelpers.WaterEdgeDistanceCm(world, sx * 100, z * 100);
                    Assert.That(waterDist, Is.GreaterThanOrEqualTo(1000), $"seed={world.Seed} ramp{k} z={z} waterDist");

                    if (z > zBot)
                    {
                        int hPrev = world.HeightCm[(z - 1) * ValleyWorld.VertsPerSide + sx];
                        int hCurr = world.HeightCm[z * ValleyWorld.VertsPerSide + sx];
                        Assert.That(hPrev - hCurr, Is.LessThanOrEqualTo(40), $"seed={world.Seed} ramp{k} z={z} drop");
                    }

                    if (z + 4 <= zTop)
                    {
                        int h0 = world.HeightCm[z * ValleyWorld.VertsPerSide + sx];
                        int h4 = world.HeightCm[(z + 4) * ValleyWorld.VertsPerSide + sx];
                        double grad4 = Math.Abs(h4 - h0) / 400.0;
                        Assert.That(grad4, Is.LessThanOrEqualTo(0.42), $"seed={world.Seed} ramp{k} z={z} grad4");
                    }
                }
            }
        }

        internal static void AssertBoundsAndRidges(ValleyWorld world)
        {
            int maxSouth = 0;
            for (int i = 0; i < world.HeightCm.Count; i++)
            {
                Assert.That(world.HeightCm[i], Is.InRange((short)0, (short)12000), $"seed={world.Seed} idx={i}");
            }

            for (int x = 0; x < ValleyWorld.VertsPerSide; x++)
            {
                if (world.HeightCm[x] > maxSouth)
                {
                    maxSouth = world.HeightCm[x];
                }
            }

            Assert.That(maxSouth, Is.LessThanOrEqualTo(400), $"seed={world.Seed} south edge max");

            long sumX0 = 0;
            long sumX72 = 0;
            long sumX512 = 0;
            long sumX440 = 0;
            int colCount = 512 - 200 + 1;
            for (int z = 200; z <= 512; z++)
            {
                int row = z * ValleyWorld.VertsPerSide;
                sumX0 += world.HeightCm[row + 0];
                sumX72 += world.HeightCm[row + 72];
                sumX512 += world.HeightCm[row + 512];
                sumX440 += world.HeightCm[row + 440];
            }

            double sideRidgeCm = world.Config.SideRidgeHeightM * 100.0;
            Assert.That((sumX0 - sumX72) / (double)colCount, Is.GreaterThanOrEqualTo(0.75 * sideRidgeCm), $"seed={world.Seed} west ridge");
            Assert.That((sumX512 - sumX440) / (double)colCount, Is.GreaterThanOrEqualTo(0.75 * sideRidgeCm), $"seed={world.Seed} east ridge");

            long sumZ512 = 0;
            long sumZ432 = 0;
            int rowCount = 440 - 72 + 1;
            int r512 = 512 * ValleyWorld.VertsPerSide;
            int r432 = 432 * ValleyWorld.VertsPerSide;
            for (int x = 72; x <= 440; x++)
            {
                sumZ512 += world.HeightCm[r512 + x];
                sumZ432 += world.HeightCm[r432 + x];
            }

            double northRidgeCm = world.Config.NorthRidgeHeightM * 100.0;
            Assert.That((sumZ512 - sumZ432) / (double)rowCount, Is.GreaterThanOrEqualTo(0.8 * northRidgeCm), $"seed={world.Seed} north ridge");
        }
    }
}
