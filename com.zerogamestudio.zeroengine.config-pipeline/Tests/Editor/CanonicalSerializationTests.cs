using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class CanonicalSerializationTests
    {
        [TestCase(0f, "0")]
        [TestCase(-0f, "0")]
        [TestCase(1f, "1")]
        [TestCase(1.5f, "1.5")]
        [TestCase(1000000f, "1e6")]
        public void Float32_UsesCanonicalRoundTripText(float value, string expected)
        {
            Assert.That(CanonicalNumberWriter.Write(value), Is.EqualTo(expected));
        }

        [Test]
        public void Float64_UsesLowercaseNormalizedExponent()
        {
            string value = CanonicalNumberWriter.Write(1.25e-12);

            Assert.That(value, Does.Not.Contain("E"));
            Assert.That(value, Does.Not.Contain("e-0"));
            Assert.That(
                BitConverter.GetBytes(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
                Is.EqualTo(BitConverter.GetBytes(1.25e-12)));
        }

        [Test]
        public void NonFiniteNumbers_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CanonicalNumberWriter.Write(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CanonicalNumberWriter.Write(double.PositiveInfinity));
        }

        [Test]
        public void FiniteExtremesAndSubnormals_RoundTripExactBits()
        {
            foreach (float value in new[]
                     {
                         float.Epsilon,
                         -float.Epsilon,
                         float.MaxValue,
                         float.MinValue,
                         1.17549435e-38f
                     })
            {
                string text = CanonicalNumberWriter.Write(value);
                float parsed = float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(BitConverter.GetBytes(parsed), Is.EqualTo(BitConverter.GetBytes(value)), text);
            }

            foreach (double value in new[]
                     {
                         double.Epsilon,
                         -double.Epsilon,
                         double.MaxValue,
                         double.MinValue,
                         2.2250738585072014e-308
                     })
            {
                string text = CanonicalNumberWriter.Write(value);
                double parsed = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(BitConverter.GetBytes(parsed), Is.EqualTo(BitConverter.GetBytes(value)), text);
            }
        }

        [Test]
        public void IntegerBoundaries_AreInvariant()
        {
            Assert.That(CanonicalNumberWriter.Write(long.MinValue), Is.EqualTo("-9223372036854775808"));
            Assert.That(CanonicalNumberWriter.Write(long.MaxValue), Is.EqualTo("9223372036854775807"));
        }

        [Test]
        public void JsonWriter_PreservesDeclaredOrderAndUsesLfWithoutBom()
        {
            var node = new ConfigObjectNode(new[]
            {
                new ConfigProperty("z", new ConfigIntegerNode(2)),
                new ConfigProperty(
                    "a",
                    new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigStringNode("行\n"),
                        new ConfigBooleanNode(true)
                    }))
            });

            byte[] bytes = CanonicalJsonWriter.WriteUtf8(node);
            string text = new UTF8Encoding(false, true).GetString(bytes);

            Assert.That(bytes[0], Is.Not.EqualTo(0xef));
            Assert.That(text, Is.EqualTo(
                "{\n" +
                "  \"z\": 2,\n" +
                "  \"a\": [\n" +
                "    \"行\\n\",\n" +
                "    true\n" +
                "  ]\n" +
                "}\n"));
            Assert.That(text, Does.Not.Contain("\r"));
        }

        [Test]
        public void JsonParser_RejectsDuplicatePropertiesAndComments()
        {
            Exception duplicate = Assert.Catch(
                () => ConfigJsonParser.Parse("{\"a\":1,\"a\":2}"));
            Exception comment = Assert.Catch(
                () => ConfigJsonParser.Parse("{/*comment*/\"a\":1}"));

            Assert.That(duplicate.GetType().FullName, Is.EqualTo("Newtonsoft.Json.JsonReaderException"));
            Assert.That(comment.GetType().FullName, Is.EqualTo("Newtonsoft.Json.JsonReaderException"));
        }
    }
}
