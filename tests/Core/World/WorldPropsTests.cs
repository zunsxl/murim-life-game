using System;
using System.Collections.Generic;
using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class WorldPropsTests
    {
        private static readonly byte[] RuleVariantByIndex =
        {
            PropCatalog.RockOutcrop,
            PropCatalog.RockBoulder,
            PropCatalog.BambooDense,
            PropCatalog.PineTall,
            PropCatalog.PineLeaning,
            PropCatalog.PineYoung,
            PropCatalog.BambooEdge,
            PropCatalog.BambooYoung,
            PropCatalog.RockRidged,
            PropCatalog.RockRiverstone,
            PropCatalog.RockCluster,
            PropCatalog.BankLip,
            PropCatalog.FloraShrub,
            PropCatalog.FloraFern,
            PropCatalog.FloraWildflower,
            PropCatalog.FloraTussock,
        };

        // T-CORE-11
        [Test]
        public void Props_Sweep_MeetCountsPositionsSpacingAndOrderSpecs()
        {
            foreach (ValleyWorld world in WorldTestHelpers.GetSweepWorlds())
            {
                AssertPropInvariants(world, checkDefaultRanges: true, minPerVariant: 3);
            }
        }

        // T-CORE-17
        [Test]
        public void PropOrder_BackToFront_SortsDeterministicallyWithoutMutatingInput()
        {
            ValleyWorld[] sweep = WorldTestHelpers.GetSweepWorlds();
            for (int seed = 0; seed < 8; seed++)
            {
                ValleyWorld world = sweep[seed];
                var copy = new List<PropInstance>(world.Props);
                int[] order1 = PropOrder.BackToFront(world.Props);
                int[] order2 = PropOrder.BackToFront(world.Props);

                Assert.That(world.Props, Is.EqualTo(copy), $"seed={seed} input mutated");
                Assert.That(order2, Is.EqualTo(order1), $"seed={seed} repeatability");
                AssertOrderValid(world.Props, order1, $"seed={seed}");

                int lipCount = 0;
                int nonLipCount = 0;
                foreach (PropInstance p in world.Props)
                {
                    if (p.Variant == PropCatalog.BankLip)
                    {
                        lipCount++;
                    }
                    else
                    {
                        nonLipCount++;
                    }
                }

                Assert.That(lipCount, Is.LessThanOrEqualTo(500), $"seed={seed} lip cap");
                Assert.That(nonLipCount, Is.LessThanOrEqualTo(13600), $"seed={seed} non-lip cap");
            }

            PropInstance[] synthetic =
            {
                new PropInstance(PropCatalog.RockBoulder, 500, 100, 1000, 100, 95),
                new PropInstance(PropCatalog.PineTall, 500, 100, 1000, 100, 96),
                new PropInstance(PropCatalog.RockCluster, 300, 100, 1000, 100, 97),
                new PropInstance(PropCatalog.FloraFern, 900, 100, 2000, 100, 98),
                new PropInstance(PropCatalog.FloraShrub, 100, 100, 500, 100, 99),
            };
            int[] synOrder = PropOrder.BackToFront(synthetic);
            Assert.That(synOrder, Is.EqualTo(new[] { 3, 2, 0, 1, 4 }));
            AssertOrderValid(synthetic, synOrder, "synthetic");
        }

        internal static void AssertPropInvariants(ValleyWorld world, bool checkDefaultRanges, int minPerVariant)
        {
            int[] famCounts = new int[5];
            int[] varCounts = new int[PropCatalog.VariantCount];
            var solids = new List<PropInstance>(2048);

            AssertAnchors(world);
            AssertPlacementOrder(world);

            for (int i = 0; i < world.Props.Count; i++)
            {
                PropInstance p = world.Props[i];
                PropVariantSpec spec = PropCatalog.Get(p.Variant);
                famCounts[(int)spec.Family]++;
                varCounts[p.Variant]++;

                Assert.That(p.XCm, Is.InRange(200, 51000), $"seed={world.Seed} prop={i} XCm");
                Assert.That(p.ZCm, Is.InRange(200, 51000), $"seed={world.Seed} prop={i} ZCm");
                Assert.That(p.ScalePct, Is.InRange(spec.MinScalePct, spec.MaxScalePct), $"seed={world.Seed} prop={i} Scale");
                Assert.That(p.TonePct, Is.InRange((byte)92, (byte)100), $"seed={world.Seed} prop={i} Tone");
                Assert.That(p.YCm, Is.EqualTo(ExpectedYCm(world.HeightCm, p.XCm, p.ZCm, spec.YRule)), $"seed={world.Seed} prop={i} YCm");

                if (spec.Family == PropFamily.Pine || spec.Family == PropFamily.Bamboo)
                {
                    int wd = WorldTestHelpers.WaterEdgeDistanceCm(world, p.XCm, p.ZCm);
                    Assert.That(wd, Is.GreaterThanOrEqualTo(200), $"seed={world.Seed} prop={i} tree near water");
                }

                if (spec.Solid)
                {
                    solids.Add(p);
                }
            }

            AssertSolidSeparation(world.Seed, solids);

            for (int v = 0; v < PropCatalog.VariantCount; v++)
            {
                Assert.That(varCounts[v], Is.GreaterThanOrEqualTo(minPerVariant), $"seed={world.Seed} variant={v}");
                Assert.That(varCounts[v], Is.LessThanOrEqualTo(PropCatalog.GetVariantCap((byte)v)), $"seed={world.Seed} variant cap {v}");
            }

            Assert.That(varCounts[PropCatalog.PineAncient], Is.EqualTo(3), $"seed={world.Seed} pine_ancient");
            AssertFamilyCounts(world.Seed, famCounts, varCounts, checkDefaultRanges);
        }

        private static void AssertFamilyCounts(uint seed, int[] famCounts, int[] varCounts, bool checkDefaultRanges)
        {
            for (int f = 0; f < 5; f++)
            {
                Assert.That(famCounts[f], Is.LessThanOrEqualTo(PropCatalog.GetFamilyCap((PropFamily)f)), $"seed={seed} fam cap {f}");
            }

            if (!checkDefaultRanges)
            {
                return;
            }

            Assert.That(famCounts[(int)PropFamily.Pine], Is.InRange(250, 1200), $"seed={seed} Pine");
            Assert.That(famCounts[(int)PropFamily.Bamboo], Is.InRange(150, 800), $"seed={seed} Bamboo");
            Assert.That(famCounts[(int)PropFamily.Rock], Is.InRange(450, 1600), $"seed={seed} Rock");
            Assert.That(famCounts[(int)PropFamily.Lip], Is.InRange(50, 500), $"seed={seed} Lip");
            Assert.That(famCounts[(int)PropFamily.Flora], Is.InRange(3500, 10000), $"seed={seed} Flora");
            Assert.That(varCounts[PropCatalog.FloraWildflower], Is.GreaterThanOrEqualTo(600), $"seed={seed} Wildflower");
        }

        private static void AssertAnchors(ValleyWorld world)
        {
            ValleyLayout l = world.Layout;
            int zTop2 = ComputeZTop2(world);

            Assert.That(world.Props.Count, Is.GreaterThanOrEqualTo(9), $"seed={world.Seed}");
            AssertAnchor(world, 0, PropCatalog.PineAncient, (l.WfX - 18) * 100, (l.FallZ + 9) * 100, 105);
            AssertAnchor(world, 1, PropCatalog.PineAncient, (l.WfX + 18) * 100, (l.FallZ + 9) * 100, 105);
            AssertAnchor(world, 2, PropCatalog.PineAncient, l.Slope2X * 100, (zTop2 + 10) * 100, 105);
            AssertAnchor(world, 3, PropCatalog.PineLeaning, (l.WfX - 9) * 100, (l.FallZ + 4) * 100, 100);
            AssertAnchor(world, 4, PropCatalog.PineLeaning, (l.WfX + 9) * 100, (l.FallZ + 4) * 100, 100);
            AssertAnchor(world, 5, PropCatalog.RockOutcrop, (l.WfX - 7) * 100, (l.FallZ - 1) * 100, 100);
            AssertAnchor(world, 6, PropCatalog.RockOutcrop, (l.WfX + 7) * 100, (l.FallZ - 1) * 100, 100);
            AssertAnchor(world, 7, PropCatalog.BambooYoung, (l.WfX - 14) * 100, (l.ZPool - 2) * 100, 100);
            AssertAnchor(world, 8, PropCatalog.BambooYoung, (l.WfX + 14) * 100, (l.ZPool - 2) * 100, 100);
        }

        private static void AssertAnchor(ValleyWorld world, int index, byte variant, int xCm, int zCm, ushort scalePct)
        {
            PropInstance p = world.Props[index];
            Assert.That(p.Variant, Is.EqualTo(variant), $"seed={world.Seed} anchor={index} variant");
            Assert.That(p.XCm, Is.EqualTo(xCm), $"seed={world.Seed} anchor={index} XCm");
            Assert.That(p.ZCm, Is.EqualTo(zCm), $"seed={world.Seed} anchor={index} ZCm");
            Assert.That(p.ScalePct, Is.EqualTo(scalePct), $"seed={world.Seed} anchor={index} ScalePct");
            Assert.That(p.TonePct, Is.EqualTo((byte)100), $"seed={world.Seed} anchor={index} TonePct");
        }

        private static int ComputeZTop2(ValleyWorld world)
        {
            ValleyLayout l = world.Layout;
            int x = l.Slope2X;
            int dx = x - l.AxisX;
            int absDx = dx < 0 ? -dx : dx;
            int bowP = (int)Primitives.FloorDiv((long)l.BowK * 100L * dx * dx, 30976L);
            long slopeQ = Primitives.FloorDiv(2L * l.BowK * absDx * 1024L, 30976L);
            long g = Primitives.Isqrt(1048576L + slopeQ * slopeQ);
            int mid = world.Config.MidTierHeightM * 100;
            int upper = world.Config.UpperTierHeightM * 100;
            int hw2 = Primitives.FloorDiv((upper - mid) * 5, 2);
            int hwv2 = (int)((hw2 * g) >> 10);
            int v2 = (l.HeadZ + l.MidDepth) * 100;
            uint salt1 = Primitives.Salt(1, world.Seed);
            uint salt2 = Primitives.Salt(2, world.Seed);

            for (int z = 0; z < ValleyWorld.VertsPerSide; z++)
            {
                int wob = ((Primitives.Noise(x, z, 64, salt1) * 1200) >> 10)
                    + ((Primitives.Noise(x, z, 24, salt2) * 250) >> 10);
                int v = 100 * z + bowP + wob;
                int s2 = Primitives.S(Primitives.Clamp01((int)Primitives.FloorDiv((long)(v - v2 + hwv2) * 1024L, 2L * hwv2)));
                if (s2 >= 1000)
                {
                    return z;
                }
            }

            return ValleyWorld.SizeM;
        }

        private static void AssertPlacementOrder(ValleyWorld world)
        {
            int ruleCursor = 0;
            for (int i = 9; i < world.Props.Count; i++)
            {
                byte variant = world.Props[i].Variant;
                while (ruleCursor < RuleVariantByIndex.Length && RuleVariantByIndex[ruleCursor] != variant)
                {
                    ruleCursor++;
                }

                Assert.That(ruleCursor, Is.LessThan(RuleVariantByIndex.Length), $"seed={world.Seed} prop={i} out of rule order");
            }
        }

        private static void AssertSolidSeparation(uint seed, List<PropInstance> solids)
        {
            for (int i = 0; i < solids.Count; i++)
            {
                PropInstance a = solids[i];
                int fa = PropCatalog.Get(a.Variant).FootprintCm;
                for (int j = i + 1; j < solids.Count; j++)
                {
                    PropInstance b = solids[j];
                    int fb = PropCatalog.Get(b.Variant).FootprintCm;
                    long minDist = Math.Min(fa, fb);
                    long dx = a.XCm - b.XCm;
                    long dz = a.ZCm - b.ZCm;
                    Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(minDist * minDist), $"seed={seed} solid overlap ({i},{j})");
                }
            }
        }

        private static int ExpectedYCm(IReadOnlyList<short> heightCm, int xCm, int zCm, PropYRule rule)
        {
            int cx = Primitives.Clamp(Primitives.FloorDiv(xCm, 100), 0, ValleyWorld.SizeM - 1);
            int cz = Primitives.Clamp(Primitives.FloorDiv(zCm, 100), 0, ValleyWorld.SizeM - 1);
            int i00 = cz * ValleyWorld.VertsPerSide + cx;
            int h00 = heightCm[i00];
            int h10 = heightCm[i00 + 1];
            int h01 = heightCm[i00 + ValleyWorld.VertsPerSide];
            int h11 = heightCm[i00 + ValleyWorld.VertsPerSide + 1];

            if (rule == PropYRule.CellMin)
            {
                return Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
            }

            int fx = xCm - cx * 100;
            int fz = zCm - cz * 100;
            int h0 = h00 + Primitives.FloorDiv((h10 - h00) * fx, 100);
            int h1 = h01 + Primitives.FloorDiv((h11 - h01) * fx, 100);
            return h0 + Primitives.FloorDiv((h1 - h0) * fz, 100);
        }

        private static void AssertOrderValid(IReadOnlyList<PropInstance> props, int[] order, string label)
        {
            Assert.That(order.Length, Is.EqualTo(props.Count), label);
            var seen = new bool[props.Count];
            for (int k = 0; k < order.Length; k++)
            {
                int idx = order[k];
                Assert.That(idx, Is.InRange(0, props.Count - 1), label);
                Assert.That(seen[idx], Is.False, $"{label} duplicate index {idx}");
                seen[idx] = true;

                if (k > 0)
                {
                    int a = order[k - 1];
                    int b = order[k];
                    PropInstance pa = props[a];
                    PropInstance pb = props[b];
                    bool valid = pa.ZCm > pb.ZCm
                        || (pa.ZCm == pb.ZCm && pa.XCm < pb.XCm)
                        || (pa.ZCm == pb.ZCm && pa.XCm == pb.XCm && a < b);
                    Assert.That(valid, Is.True, $"{label} order violation at {k}: ({a},{b})");
                }
            }
        }
    }
}
