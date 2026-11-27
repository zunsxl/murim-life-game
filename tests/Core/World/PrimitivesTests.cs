using Murim.Core.World;
using NUnit.Framework;

namespace Murim.Core.Tests.World
{
    [TestFixture]
    public class PrimitivesTests
    {
        // T-CORE-01
        [Test]
        public void Pcg32_MatchesReferenceVectorsAndBounds()
        {
            var rng = new Pcg32(42UL, 54UL);
            uint[] expected42 =
            {
                0xA15C02B7u,
                0x7B47F409u,
                0xBA1D3330u,
                0x83D2F293u,
                0xBFA4784Bu,
                0xCBED606Eu,
            };

            for (int i = 0; i < expected42.Length; i++)
            {
                Assert.That(rng.Next(), Is.EqualTo(expected42[i]), $"Pcg32(42, 54) step {i}");
            }

            for (int i = 0; i < 10000; i++)
            {
                int draw = rng.R(-40, 40);
                Assert.That(draw, Is.InRange(-40, 40));
            }

            var rngOne = new Pcg32(1UL, 1UL);
            Assert.That(rngOne.Next(), Is.EqualTo(0xC9828F91u));
            Assert.That(rngOne.Next(), Is.EqualTo(0x1592E274u));
            Assert.That(rngOne.Next(), Is.EqualTo(0xC0262657u));

            var fresh42 = new Pcg32(42UL, 54UL);
            int[] expectedDraws = { -7, 8, -20, 39, 36 };
            for (int i = 0; i < expectedDraws.Length; i++)
            {
                Assert.That(fresh42.R(-40, 40), Is.EqualTo(expectedDraws[i]), $"Pcg32(42, 54).R(-40, 40) #{i}");
            }
        }

        // T-CORE-02
        [Test]
        public void Primitives_MatchExactReferenceVectors()
        {
            Assert.That(Primitives.Hash32(1, 2, 3u), Is.EqualTo(0xA99CD169u));
            Assert.That(Primitives.Hash32(-5, 7, 0xDEADBEEFu), Is.EqualTo(0x405D176Eu));
            Assert.That(Primitives.Hash32(512, 512, 1u), Is.EqualTo(0x9A962ED7u));

            int[] sInputs = { 0, 100, 256, 512, 768, 1000, 1024 };
            int[] sExpected = { 0, 27, 160, 512, 864, 1022, 1024 };
            for (int i = 0; i < sInputs.Length; i++)
            {
                Assert.That(Primitives.S(sInputs[i]), Is.EqualTo(sExpected[i]), $"S({sInputs[i]})");
            }

            Assert.That(Primitives.Noise(0, 0, 64, 12345u), Is.EqualTo(1020));
            Assert.That(Primitives.Noise(10, 20, 64, 12345u), Is.EqualTo(758));
            Assert.That(Primitives.Noise(100, 300, 64, 12345u), Is.EqualTo(-88));
            Assert.That(Primitives.Noise(511, 511, 64, 12345u), Is.EqualTo(597));
            Assert.That(Primitives.Noise(64, 64, 64, 12345u), Is.EqualTo(80));

            Assert.That(Primitives.Noise(0, 0, 8, 7u), Is.EqualTo(-75));
            Assert.That(Primitives.Noise(3, 5, 8, 7u), Is.EqualTo(-216));
            Assert.That(Primitives.Noise(200, 13, 8, 7u), Is.EqualTo(660));

            Assert.That(Primitives.Isqrt(0L), Is.EqualTo(0L));
            Assert.That(Primitives.Isqrt(1L), Is.EqualTo(1L));
            Assert.That(Primitives.Isqrt(2L), Is.EqualTo(1L));
            Assert.That(Primitives.Isqrt(3L), Is.EqualTo(1L));
            Assert.That(Primitives.Isqrt(4L), Is.EqualTo(2L));
            Assert.That(Primitives.Isqrt(1L << 31), Is.EqualTo(46340L));
            Assert.That(Primitives.Isqrt((1L << 31) - 1L), Is.EqualTo(46340L));
            Assert.That(Primitives.Isqrt(1L << 62), Is.EqualTo(1L << 31));
            Assert.That(Primitives.Isqrt((1L << 62) - 1L), Is.EqualTo((1L << 31) - 1L));
            Assert.That(Primitives.Isqrt(long.MaxValue), Is.EqualTo(3037000499L));

            Assert.That(Primitives.FloorDiv(-1, 5), Is.EqualTo(-1));
            Assert.That(Primitives.FloorDiv(-5, 5), Is.EqualTo(-1));
            Assert.That(Primitives.FloorDiv(-6, 5), Is.EqualTo(-2));
            Assert.That(Primitives.FloorDiv(-1L, 5L), Is.EqualTo(-1L));
        }
    }
}
