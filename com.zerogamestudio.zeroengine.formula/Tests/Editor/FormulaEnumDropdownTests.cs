using NUnit.Framework;
using ZeroEngine.Formula.Editor;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaEnumDropdownTests
    {
        private enum SparseEnum
        {
            First = 7,
            Second = 42,
            Last = 999,
        }

        [Test]
        public void CreateOptions_WithSparseEnum_PreservesNamesAndSerializedValues()
        {
            var options = FormulaEnumDropdownUtility.CreateOptions(typeof(SparseEnum));

            Assert.AreEqual(3, options.Length);
            Assert.AreEqual("First", options[0].Name);
            Assert.AreEqual(7, options[0].Value);
            Assert.AreEqual("Second", options[1].Name);
            Assert.AreEqual(42, options[1].Value);
            Assert.AreEqual("Last", options[2].Name);
            Assert.AreEqual(999, options[2].Value);
        }

        [Test]
        public void FindSelectedIndex_WithSparseValue_ReturnsMatchingOption()
        {
            var options = FormulaEnumDropdownUtility.CreateOptions(typeof(SparseEnum));

            Assert.AreEqual(1, FormulaEnumDropdownUtility.FindSelectedIndex(options, 42));
        }

        [TestCase(19, false)]
        [TestCase(20, true)]
        [TestCase(494, true)]
        public void ShouldUseSearchableDropdown_UsesLargeEnumThreshold(int optionCount, bool expected)
        {
            Assert.AreEqual(expected, FormulaEnumDropdownUtility.ShouldUseSearchableDropdown(optionCount));
        }
    }
}
