using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class WorldConfigSweepTests
    {
        // T-CORE-13
        [Test]
        public void Generate_HeightAndDensityCorners_PreserveAllInvariants()
        {
            WorldConfig[] configs =
            {
                WorldTestHelpers.MakeConfig(8, 18, 20, 22, 25, 25, 25),
                WorldTestHelpers.MakeConfig(16, 34, 40, 32, 150, 150, 150),
                WorldTestHelpers.MakeConfig(16, 26, 20, 32),
                WorldTestHelpers.MakeConfig(8, 26, 40, 22),
            };

            foreach (WorldConfig cfg in configs)
            {
                for (uint seed = 0; seed < 8; seed++)
                {
                    ValleyWorld world = ValleyGenerator.Generate(seed, cfg);
                    WorldTerrainTests.AssertTierInvariants(world);
                    WorldTerrainTests.AssertEmbankmentAndRampInvariants(world);
                    WorldWaterTests.AssertWaterInvariants(world);
                    WorldTerrainTests.AssertBoundsAndRidges(world);
                    ProjectionTests.AssertViewFrameInvariants(world);
                    WorldPropsTests.AssertPropInvariants(world, checkDefaultRanges: false, minPerVariant: 2);
                }
            }
        }
    }
}
