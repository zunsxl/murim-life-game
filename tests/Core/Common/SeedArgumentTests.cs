using Murim.Core.Common;
using NUnit.Framework;

namespace Murim.Core.Tests.Common
{
    [TestFixture]
    public class SeedArgumentTests
    {
        // T-CORE-15
        [Test]
        public void TryParse_MatchesSpecificationTable()
        {
            Assert.That(SeedArgument.TryParse(new string[0], out uint sEmpty), Is.EqualTo(SeedParseStatus.Absent));
            Assert.That(sEmpty, Is.EqualTo(0u));

            Assert.That(SeedArgument.TryParse(new[] { "-seed", "0" }, out uint sZero), Is.EqualTo(SeedParseStatus.Valid));
            Assert.That(sZero, Is.EqualTo(0u));

            Assert.That(SeedArgument.TryParse(new[] { "-seed", "4294967295" }, out uint sMax), Is.EqualTo(SeedParseStatus.Valid));
            Assert.That(sMax, Is.EqualTo(4294967295u));

            Assert.That(SeedArgument.TryParse(new[] { "-seed", "007" }, out uint sLeadingZero), Is.EqualTo(SeedParseStatus.Valid));
            Assert.That(sLeadingZero, Is.EqualTo(7u));

            Assert.That(SeedArgument.TryParse(new[] { "-seed" }, out _), Is.EqualTo(SeedParseStatus.Invalid));
            Assert.That(SeedArgument.TryParse(new[] { "-seed", "-1" }, out _), Is.EqualTo(SeedParseStatus.Invalid));
            Assert.That(SeedArgument.TryParse(new[] { "-seed", "4294967296" }, out _), Is.EqualTo(SeedParseStatus.Invalid));
            Assert.That(SeedArgument.TryParse(new[] { "-seed", "1e3" }, out _), Is.EqualTo(SeedParseStatus.Invalid));
            Assert.That(SeedArgument.TryParse(new[] { "-seed", "12345678901" }, out _), Is.EqualTo(SeedParseStatus.Invalid));
            Assert.That(SeedArgument.TryParse(new[] { "-seed", "" }, out _), Is.EqualTo(SeedParseStatus.Invalid));

            Assert.That(SeedArgument.TryParse(new[] { "-SEED", "5" }, out _), Is.EqualTo(SeedParseStatus.Absent));
        }

        // T-CORE-15
        [Test]
        public void TryParse_FirstSeedFlagWins()
        {
            Assert.That(
                SeedArgument.TryParse(new[] { "game.exe", "-seed", "42", "-seed", "99" }, out uint firstValid),
                Is.EqualTo(SeedParseStatus.Valid));
            Assert.That(firstValid, Is.EqualTo(42u));

            Assert.That(
                SeedArgument.TryParse(new[] { "game.exe", "-seed", "bad", "-seed", "99" }, out _),
                Is.EqualTo(SeedParseStatus.Invalid));
        }

        // T-CORE-15
        [Test]
        public void TryParse_NullArgsOrNullValue_HandledSafely()
        {
            Assert.That(SeedArgument.TryParse(null, out _), Is.EqualTo(SeedParseStatus.Absent));
            Assert.That(SeedArgument.TryParse(new string[] { "-seed", null }, out _), Is.EqualTo(SeedParseStatus.Invalid));
        }
    }
}
