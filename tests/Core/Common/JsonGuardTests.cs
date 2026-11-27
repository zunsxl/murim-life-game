using Murim.Core.Common;
using NUnit.Framework;

namespace Murim.Core.Tests.Common
{
    [TestFixture]
    public class JsonGuardTests
    {
        // T-CORE-16
        [Test]
        public void MaxDepth_ValidObjects_ReturnsExpectedDepth()
        {
            Assert.That(JsonGuard.MaxDepth("{}"), Is.EqualTo(1));
            Assert.That(JsonGuard.MaxDepth("{\"a\":[1,{\"b\":2}]}"), Is.EqualTo(3));
        }

        // T-CORE-16
        [Test]
        public void MaxDepth_BracesAndEscapedQuotesInStrings_AreIgnored()
        {
            string json = "{\"a\":\"{[\\\"inner\\\"]}\",\"b\":{\"c\":\"}\"}}";
            Assert.That(JsonGuard.MaxDepth(json), Is.EqualTo(2));
        }

        // T-CORE-16
        [Test]
        public void MaxDepth_UnbalancedOrMalformed_ReturnsNegative()
        {
            Assert.That(JsonGuard.MaxDepth(null), Is.LessThan(0));
            Assert.That(JsonGuard.MaxDepth(""), Is.LessThan(0));
            Assert.That(JsonGuard.MaxDepth("{"), Is.LessThan(0));
            Assert.That(JsonGuard.MaxDepth("}"), Is.LessThan(0));
            Assert.That(JsonGuard.MaxDepth("{]"), Is.LessThan(0));
            Assert.That(JsonGuard.MaxDepth("{\"a\":[1}"), Is.LessThan(0));
            Assert.That(JsonGuard.MaxDepth("{\"a\":\"unterminated}"), Is.LessThan(0));
        }

        // T-CORE-16
        [Test]
        public void MaxDepth_DeeplyNestedBrackets_DoesNotOverflowStack()
        {
            string openOnly = new string('[', 65536);
            Assert.That(JsonGuard.MaxDepth(openOnly), Is.LessThan(0));

            string balancedDeep = new string('[', 65536) + new string(']', 65536);
            Assert.That(JsonGuard.MaxDepth(balancedDeep), Is.EqualTo(65536));
        }
    }
}
