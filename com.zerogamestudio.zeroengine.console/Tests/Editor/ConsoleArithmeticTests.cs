using System;
using NUnit.Framework;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsoleArithmeticTests
    {
        [TestCase("1+2", 3)]
        [TestCase("2*3+4", 10)]
        [TestCase("(2+3)*4", 20)]
        [TestCase("3/2*2", 2)]
        [TestCase("17%5", 2)]
        [TestCase("-(2+3)", -5)]
        public void Arithmetic_IsExplicitAndUsesIntegralOperatorSemantics(string expression, int expected)
        {
            var values = new ConsoleValues { EnableArithmetic = true };
            Assert.That(values.TryConvert(Token(expression), typeof(int), out var result, out var error), Is.True, error);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void ExactAndFloatingTargets_PreserveTheirNumericRange()
        {
            var values = new ConsoleValues { EnableArithmetic = true };
            Assert.That(values.TryConvert(Token("9223372036854775807-1"), typeof(long), out var result, out var error), Is.True, error);
            Assert.That(result, Is.EqualTo(long.MaxValue - 1));
            Assert.That(values.TryConvert(Token("1.25/2"), typeof(decimal), out result, out error), Is.True, error);
            Assert.That(result, Is.EqualTo(.625m));
            Assert.That(values.TryConvert(Token("1e100*2"), typeof(double), out result, out error), Is.True, error);
            Assert.That(result, Is.EqualTo(2e100));
        }

        [TestCase("2147483647+1")]
        [TestCase("1/0")]
        [TestCase("1+")]
        [TestCase("(1+2")]
        [TestCase("1+2 trailing")]
        [TestCase("1.5+1")]
        public void InvalidOrOverflowingInput_IsRejected(string expression)
        {
            Assert.That(new ConsoleValues { EnableArithmetic = true }.TryConvert(Token(expression), typeof(int), out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void DefaultParserAndCustomConverters_KeepTheirExistingPrecedence()
        {
            var values = new ConsoleValues();
            Assert.That(values.TryConvert(Token("1+2"), typeof(int), out _, out _), Is.False);
            values.EnableArithmetic = true;
            using (values.Register(typeof(int), (ConsoleToken token, Type target, out object value, out string error) =>
                { value = 99; error = null; return true; }))
            {
                Assert.That(values.TryConvert(Token("1+2"), typeof(int), out var value, out _), Is.True);
                Assert.That(value, Is.EqualTo(99));
            }
        }

        [Test]
        public void Parser_RejectsExcessiveNestingAndRecovers()
        {
            var values = new ConsoleValues { EnableArithmetic = true };
            string expression = new string('(', 64) + "1" + new string(')', 64);
            Assert.That(values.TryConvert(Token(expression), typeof(int), out _, out _), Is.False);
            Assert.That(values.TryConvert(Token("1+2"), typeof(int), out var result, out _), Is.True);
            Assert.That(result, Is.EqualTo(3));
        }

        private static ConsoleToken Token(string text) => new ConsoleToken(text, text, 0);
    }
}
