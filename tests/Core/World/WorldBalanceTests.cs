using System;
using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class WorldBalanceTests
    {
        // T-BAL-03
        [Test]
        public void PropBalance_16Seeds_MeetsCanopyBambooAndWildflowerTargets()
        {
            ValleyWorld[] sweep = WorldTestHelpers.GetSweepWorlds();
            for (int seed = 0; seed < 16; seed++)
            {
                ValleyWorld world = sweep[seed];

                int plateauCount = 0;
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
                        if (tl <= 5 || (tl >= 95 && tl <= 105) || tl >= 195)
                        {
                            plateauCount++;
                        }
                    }
                }

                double plateauAreaM2 = plateauCount;
                double plateauCanopyAreaM2 = 0.0;
                int bambooTotal = 0;
                int bambooOnRidge = 0;
                int wildflowerTotal = 0;
                int wildflowerOnBasinOrMidPlateau = 0;

                foreach (PropInstance p in world.Props)
                {
                    PropVariantSpec spec = PropCatalog.Get(p.Variant);
                    int vx = Primitives.Clamp(Primitives.FloorDiv(p.XCm + 50, 100), 0, ValleyWorld.SizeM);
                    int vz = Primitives.Clamp(Primitives.FloorDiv(p.ZCm + 50, 100), 0, ValleyWorld.SizeM);
                    int idx = vz * ValleyWorld.VertsPerSide + vx;
                    byte ridge = world.RidgePct[idx];
                    byte tl = world.TierLevelPct[idx];

                    if (spec.Family == PropFamily.Pine || spec.Family == PropFamily.Bamboo)
                    {
                        bool onPlateau = vz >= 56 && ridge == 0 && (tl <= 5 || (tl >= 95 && tl <= 105) || tl >= 195);
                        if (onPlateau)
                        {
                            double rM = spec.CanopyRadiusCm / 100.0;
                            plateauCanopyAreaM2 += Math.PI * rM * rM;
                        }
                    }

                    if (spec.Family == PropFamily.Bamboo)
                    {
                        bambooTotal++;
                        if (ridge >= 25)
                        {
                            bambooOnRidge++;
                        }
                    }

                    if (p.Variant == PropCatalog.FloraWildflower)
                    {
                        wildflowerTotal++;
                        bool onBasinOrMid = vz >= 56 && ridge == 0 && (tl <= 5 || (tl >= 95 && tl <= 105));
                        if (onBasinOrMid)
                        {
                            wildflowerOnBasinOrMidPlateau++;
                        }
                    }
                }

                double canopyPct = plateauCanopyAreaM2 * 100.0 / plateauAreaM2;
                double bambooRidgePct = bambooOnRidge * 100.0 / bambooTotal;
                double wildflowerPlateauPct = wildflowerOnBasinOrMidPlateau * 100.0 / wildflowerTotal;

                Assert.That(canopyPct, Is.LessThanOrEqualTo(12.0), $"seed={seed} canopy={canopyPct:F2}%");
                Assert.That(bambooRidgePct, Is.GreaterThanOrEqualTo(60.0), $"seed={seed} bambooRidge={bambooRidgePct:F2}%");
                Assert.That(wildflowerPlateauPct, Is.GreaterThanOrEqualTo(50.0), $"seed={seed} wildflowerPlateau={wildflowerPlateauPct:F2}%");
            }
        }
    }
}
