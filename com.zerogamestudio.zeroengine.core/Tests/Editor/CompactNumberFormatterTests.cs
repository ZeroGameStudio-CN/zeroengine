using System.Globalization;
using System.Numerics;
using NUnit.Framework;

namespace ZeroEngine.Tests
{
    public sealed class CompactNumberFormatterTests
    {
        [TestCase(0d, "0")]
        [TestCase(-0.001d, "0")]
        [TestCase(42.125d, "42.13")]
        [TestCase(999.994d, "999.99")]
        [TestCase(999.995d, "1K")]
        [TestCase(1_000d, "1K")]
        [TestCase(1_240d, "1.24K")]
        [TestCase(-1_240d, "-1.24K")]
        [TestCase(273_654d, "273.65K")]
        [TestCase(999_994d, "999.99K")]
        [TestCase(999_995d, "1M")]
        [TestCase(1_230_000d, "1.23M")]
        [TestCase(2_300_000_000d, "2.3B")]
        [TestCase(6_000_000_000_000d, "6T")]
        [TestCase(999_995_000_000_000d, "1E+15")]
        [TestCase(1.234e28, "1.23E+28")]
        [TestCase(double.MaxValue, "1.8E+308")]
        [TestCase(double.NaN, "—")]
        [TestCase(double.PositiveInfinity, "∞")]
        [TestCase(double.NegativeInfinity, "-∞")]
        public void FormatsFloatingPointValues(double value, string expected)
        {
            Assert.AreEqual(expected, CompactNumberFormatter.Format(value, CultureInfo.InvariantCulture));
        }

        [Test]
        public void FormatsIntegralValuesAtDisplayBoundaries()
        {
            var culture = CultureInfo.InvariantCulture;
            Assert.AreEqual("2.15B", CompactNumberFormatter.Format(int.MaxValue, culture));
            Assert.AreEqual("1M", CompactNumberFormatter.Format(999_995L, culture));
            Assert.AreEqual("-9.22E+18", CompactNumberFormatter.Format(long.MinValue, culture));
        }

        [Test]
        public void FormatsIntegersBeyondFloatingPointRange()
        {
            var culture = CultureInfo.InvariantCulture;
            var magnitude = 123 * BigInteger.Pow(10, 400);
            Assert.AreEqual("1.23E+402", CompactNumberFormatter.Format(magnitude, culture));
            Assert.AreEqual("-1.23E+402", CompactNumberFormatter.Format(-magnitude, culture));
            Assert.AreEqual("1M", CompactNumberFormatter.Format(new BigInteger(999_995), culture));
        }

        [Test]
        public void UsesTheRequestedCultureForDecimalSeparatorsAndSigns()
        {
            var culture = CultureInfo.GetCultureInfo("de-DE");
            Assert.AreEqual("1,24K", CompactNumberFormatter.Format(1_240L, culture));
            Assert.AreEqual("1,23E+28", CompactNumberFormatter.Format(1.234e28, culture));
            Assert.AreEqual("1,23E+402", CompactNumberFormatter.Format(123 * BigInteger.Pow(10, 400), culture));

            var custom = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            custom.NegativeSign = "−";
            Assert.AreEqual("−1.24K", CompactNumberFormatter.Format(-1_240L, custom));
        }
    }
}
