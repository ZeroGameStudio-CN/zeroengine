using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.Console.Tests
{
    public sealed class ConsoleCollectionTests
    {
        [TestCase(typeof(List<int>))]
        [TestCase(typeof(IList<int>))]
        [TestCase(typeof(ICollection<int>))]
        [TestCase(typeof(IEnumerable<int>))]
        [TestCase(typeof(IReadOnlyList<int>))]
        [TestCase(typeof(IReadOnlyCollection<int>))]
        [TestCase(typeof(Queue<int>))]
        public void Collections_ConvertToTheirRequestedContract(Type type)
        {
            var values = new ConsoleValues();
            Assert.That(values.TryConvert(Token("[1,2,3]"), type, out var result, out var error), Is.True, error);
            Assert.That(type.IsInstanceOfType(result), Is.True);
            Assert.That(((IEnumerable<int>)result).ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void SetAndStack_RetainTheirStandardSemantics()
        {
            var values = new ConsoleValues();
            Assert.That(values.TryConvert(Token("[1,2,2]"), typeof(ISet<int>), out var set, out _), Is.True);
            Assert.That(((ISet<int>)set).SetEquals(new[] { 1, 2 }), Is.True);
            Assert.That(values.TryConvert(Token("[1,2,3]"), typeof(Stack<int>), out var stack, out _), Is.True);
            Assert.That(((Stack<int>)stack).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
        }

        [Test]
        public void NestedCollections_ReuseQuotedScalarAndNullableRules()
        {
            var values = new ConsoleValues();
            Assert.That(values.TryConvert(Token("[[\"中 文\",\"x\"],[\"y\"]]"), typeof(List<string[]>), out var result, out var error), Is.True, error);
            Assert.That(((List<string[]>)result)[0], Is.EqualTo(new[] { "中 文", "x" }));
            Assert.That(values.TryConvert(Token("[null,2]"), typeof(List<int?>), out result, out error), Is.True, error);
            Assert.That((List<int?>)result, Is.EqualTo(new int?[] { null, 2 }));
        }

        [TestCase(typeof(Dictionary<string, int>))]
        [TestCase(typeof(IDictionary<string, int>))]
        [TestCase(typeof(IReadOnlyDictionary<string, int>))]
        public void Dictionary_ConvertsPairsWithoutExecutingProjectCode(Type type)
        {
            var values = new ConsoleValues();
            Assert.That(values.TryConvert(Token("[(\"中 文\",1),(\"x\",2)]"), type, out var result, out var error), Is.True, error);
            Assert.That(type.IsInstanceOfType(result), Is.True);
            Assert.That(((IDictionary)result)["中 文"], Is.EqualTo(1));
            Assert.That(((IDictionary)result)["x"], Is.EqualTo(2));
        }

        [TestCase("[(\"x\",1),(\"x\",2)]")]
        [TestCase("[(\"x\")] ")]
        [TestCase("[(\"x\",1,2)]")]
        public void Dictionary_RejectsInvalidEntries(string input)
        {
            Assert.That(new ConsoleValues().TryConvert(Token(input.Trim()), typeof(Dictionary<string, int>), out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void Dictionary_RejectsNullKeysAfterApplyingExistingScalarRules()
        {
            var values = new ConsoleValues();
            Assert.That(values.TryConvert(Token("[(null,1)]"), typeof(Dictionary<int?, int>), out _, out _), Is.False);
            Assert.That(values.TryConvert(Token("[(null,1)]"), typeof(Dictionary<string, int>), out var value, out _), Is.True);
            Assert.That(((Dictionary<string, int>)value)["null"], Is.EqualTo(1));
        }

        [Test]
        public void InvalidElement_DoesNotReturnAPartiallyPopulatedCollection()
        {
            Assert.That(new ConsoleValues().TryConvert(Token("[1,no,3]"), typeof(List<int>), out var value, out var error), Is.False);
            Assert.That(value, Is.Null);
            Assert.That(error, Does.Contain("Element 1"));
        }

        [Test]
        public void ExplicitConverter_RetainsPrecedenceAndLifetime()
        {
            var values = new ConsoleValues();
            using (values.Register(typeof(List<int>), (ConsoleToken token, Type type, out object value, out string error) =>
                { value = new List<int> { 99 }; error = null; return true; }))
            {
                Assert.That(values.TryConvert(Token("[1,2]"), typeof(List<int>), out var result, out _), Is.True);
                Assert.That((List<int>)result, Is.EqualTo(new[] { 99 }));
            }
            Assert.That(values.TryConvert(Token("[1,2]"), typeof(List<int>), out var restored, out _), Is.True);
            Assert.That((List<int>)restored, Is.EqualTo(new[] { 1, 2 }));
        }

        private static ConsoleToken Token(string raw) => new ConsoleToken(raw, raw, 0);
    }
}
