using NUnit.Framework;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    [TestFixture]
    [Category("Unit")]
    public sealed class SourceTextRegionExtractorTests
    {
        [Test]
        [Category("Boundary")]
        public void ExtractMethodRegion_SkipsBracesInsideLineComment()
        {
            const string source = @"
public static void Example()
{
    // } should not close the method
    var marker = 1;
}

private static void Other()
{
}
";

            var method = SourceTextRegionExtractor.ExtractMethodRegion(source, "public static void Example()");

            Assert.That(method, Does.Contain("var marker = 1;"));
            Assert.That(method, Does.Not.Contain("private static void Other"));
        }

        [Test]
        [Category("Boundary")]
        public void ExtractMethodRegion_SkipsBracesInsideBlockComment()
        {
            const string source = @"
public static void Example()
{
    /* } should not close the method */
    var marker = 2;
}

private static void Other()
{
}
";

            var method = SourceTextRegionExtractor.ExtractMethodRegion(source, "public static void Example()");

            Assert.That(method, Does.Contain("var marker = 2;"));
            Assert.That(method, Does.Not.Contain("private static void Other"));
        }

        [Test]
        [Category("Boundary")]
        public void ExtractMethodRegion_SkipsBracesInsideCharLiterals()
        {
            const string source = @"
public static void Example()
{
    var closeBrace = '}';
    var escapedQuote = '\'';
    var escapedSlash = '\\';
    var marker = 3;
}

private static void Other()
{
}
";

            var method = SourceTextRegionExtractor.ExtractMethodRegion(source, "public static void Example()");

            Assert.That(method, Does.Contain("var marker = 3;"));
            Assert.That(method, Does.Not.Contain("private static void Other"));
        }

        [Test]
        [Category("Boundary")]
        public void ExtractMethodRegion_SkipsBracesInsideRawStringLiteral()
        {
            const string source = @"
public static void Example()
{
    var text = """"""
{
    ""value"": ""}""
}
"""""";
    var marker = 4;
}

private static void Other()
{
}
";

            var method = SourceTextRegionExtractor.ExtractMethodRegion(source, "public static void Example()");

            Assert.That(method, Does.Contain("var marker = 4;"));
            Assert.That(method, Does.Not.Contain("private static void Other"));
        }

        [Test]
        [Category("Boundary")]
        public void ExtractMethodRegion_SkipsBracesInsideVerbatimStringLiteral()
        {
            const string source = @"
public static void Example()
{
    var text = @""{ """"value"""": """"}"""" }"";
    var marker = 5;
}

private static void Other()
{
}
";

            var method = SourceTextRegionExtractor.ExtractMethodRegion(source, "public static void Example()");

            Assert.That(method, Does.Contain("var marker = 5;"));
            Assert.That(method, Does.Not.Contain("private static void Other"));
        }

        [Test]
        public void ExtractInvocation_NestedArguments_ReturnsCompleteInvocation()
        {
            const string source = "before Policy.Evaluate(first, Nested(second, third)) after";

            var invocation = SourceTextRegionExtractor.ExtractInvocation(source, "Policy.Evaluate");

            Assert.That(invocation, Is.EqualTo("Policy.Evaluate(first, Nested(second, third))"));
        }

        [Test]
        public void ExtractInvocation_MissingMarker_ThrowsAssertionException()
        {
            Assert.Throws<AssertionException>(() =>
                SourceTextRegionExtractor.ExtractInvocation("Policy.Evaluate(first)", "Missing.Evaluate"));
        }

        [Test]
        public void ExtractInvocation_UnclosedArguments_ThrowsAssertionException()
        {
            Assert.Throws<AssertionException>(() =>
                SourceTextRegionExtractor.ExtractInvocation("Policy.Evaluate(first", "Policy.Evaluate"));
        }

        [Test]
        public void AssertArgumentsInOrder_OrderedArguments_Passes()
        {
            SourceTextRegionExtractor.AssertArgumentsInOrder(
                "Policy.Evaluate(first, second, third)",
                "first",
                "second",
                "third");
        }

        [Test]
        public void AssertArgumentsInOrder_OutOfOrder_ThrowsAssertionException()
        {
            Assert.Throws<AssertionException>(() =>
                SourceTextRegionExtractor.AssertArgumentsInOrder(
                    "Policy.Evaluate(second, first)",
                    "first",
                    "second"));
        }
    }
}


