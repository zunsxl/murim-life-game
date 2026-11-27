using System;
using System.Collections.Generic;
using Murim.Core.World;
using Murim.Tests.Shared;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class WorldDeterminismTests
    {
        // T-CORE-03
        [Test]
        public void Layout_MatchesExactReferenceVectors()
        {
            AssertLayout(0u, 272, 228, 113, 165, 277, 378, 171, 295);
            AssertLayout(1u, 280, 198, 106, 154, 281, 387, 180, 332);
            AssertLayout(20261127u, 277, 227, 97, 163, 282, 172, 393, 291);
        }

        // T-CORE-04
        [Test]
        public void Generate_SweepSeeds_IsDeterministicAndDistinct()
        {
            var hashes = new HashSet<ulong>();
            ValleyWorld[] cached = WorldTestHelpers.GetSweepWorlds();
            for (int i = 0; i < WorldTestHelpers.SweepSeeds.Length; i++)
            {
                uint seed = WorldTestHelpers.SweepSeeds[i];
                ValleyWorld a = cached[i];
                ValleyWorld b = ValleyGenerator.Generate(seed, WorldConfig.Default);

                Assert.That(b.Hash, Is.EqualTo(a.Hash), $"Hash mismatch on repeat for seed={seed}");
                Assert.That(b.HeightCm, Is.EqualTo(a.HeightCm), $"HeightCm mismatch for seed={seed}");
                Assert.That(b.TierLevelPct, Is.EqualTo(a.TierLevelPct), $"TierLevelPct mismatch for seed={seed}");
                Assert.That(b.RidgePct, Is.EqualTo(a.RidgePct), $"RidgePct mismatch for seed={seed}");
                Assert.That(b.SurfaceWeights, Is.EqualTo(a.SurfaceWeights), $"SurfaceWeights mismatch for seed={seed}");
                Assert.That(b.Stream, Is.EqualTo(a.Stream), $"Stream mismatch for seed={seed}");
                Assert.That(b.Brook, Is.EqualTo(a.Brook), $"Brook mismatch for seed={seed}");
                Assert.That(b.Waterfall, Is.EqualTo(a.Waterfall), $"Waterfall mismatch for seed={seed}");
                Assert.That(b.Props, Is.EqualTo(a.Props), $"Props mismatch for seed={seed}");
                Assert.That(hashes.Add(a.Hash), Is.True, $"Duplicate hash encountered at seed={seed}");
            }
        }

        // T-CORE-05
        [Test]
        public void Generate_CanonicalSeeds_MatchGoldenHashes()
        {
            ulong h1 = ValleyGenerator.Generate(1u, WorldConfig.Default).Hash;
            ulong h2 = ValleyGenerator.Generate(2u, WorldConfig.Default).Hash;
            ulong h20261127 = ValleyGenerator.Generate(20261127u, WorldConfig.Default).Hash;
            Assert.Multiple(() =>
            {
                Assert.That(h1, Is.EqualTo(GoldenHashes.Seed1), $"Seed1=0x{h1:X16}UL");
                Assert.That(h2, Is.EqualTo(GoldenHashes.Seed2), $"Seed2=0x{h2:X16}UL");
                Assert.That(h20261127, Is.EqualTo(GoldenHashes.Seed20261127), $"Seed20261127=0x{h20261127:X16}UL");
            });
        }

        // T-CORE-04
        [Test]
        public void ValleyWorldAndPropCatalog_ExposeReadOnlyNonArrayCollections()
        {
            ValleyWorld world = ValleyGenerator.Generate(1u, WorldConfig.Default);

            AssertReadOnlyNonArray(world.HeightCm);
            AssertReadOnlyNonArray(world.TierLevelPct);
            AssertReadOnlyNonArray(world.RidgePct);
            AssertReadOnlyNonArray(world.SurfaceWeights);
            AssertReadOnlyNonArray(world.Stream);
            AssertReadOnlyNonArray(world.Brook);
            AssertReadOnlyNonArray(world.Waterfall);
            AssertReadOnlyNonArray(world.Slopes);
            AssertReadOnlyNonArray(world.Props);
            AssertReadOnlyNonArray(PropCatalog.Variants);
        }

        private static void AssertReadOnlyNonArray<T>(IReadOnlyList<T> collection)
        {
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection, Is.Not.InstanceOf<T[]>());
            var asList = collection as IList<T>;
            Assert.That(asList, Is.Not.Null);
            Assert.That(asList.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => asList[0] = asList[0]);
        }

        private static void AssertLayout(
            uint seed,
            int axisX,
            int headZ,
            int midDepth,
            int bowK,
            int wfX,
            int slope1X,
            int slope2X,
            int exitX)
        {
            ValleyWorld world = ValleyGenerator.Generate(seed, WorldConfig.Default);
            ValleyLayout l = world.Layout;
            Assert.That(l.AxisX, Is.EqualTo(axisX), $"seed={seed} AxisX");
            Assert.That(l.HeadZ, Is.EqualTo(headZ), $"seed={seed} HeadZ");
            Assert.That(l.MidDepth, Is.EqualTo(midDepth), $"seed={seed} MidDepth");
            Assert.That(l.BowK, Is.EqualTo(bowK), $"seed={seed} BowK");
            Assert.That(l.WfX, Is.EqualTo(wfX), $"seed={seed} WfX");
            Assert.That(l.Slope1X, Is.EqualTo(slope1X), $"seed={seed} Slope1X");
            Assert.That(l.Slope2X, Is.EqualTo(slope2X), $"seed={seed} Slope2X");
            Assert.That(l.ExitX, Is.EqualTo(exitX), $"seed={seed} ExitX");
        }
    }
}
