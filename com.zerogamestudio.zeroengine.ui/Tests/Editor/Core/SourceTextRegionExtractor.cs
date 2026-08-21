using NUnit.Framework;

namespace ZeroEngine.UI.Tests.Editor.Core
{
    internal static class SourceTextRegionExtractor
    {
        public static string ExtractMethodRegion(string source, string signature)
        {
            var signatureStart = source.IndexOf(signature, System.StringComparison.Ordinal);
            Assert.That(signatureStart, Is.GreaterThanOrEqualTo(0), $"Missing method signature: {signature}");

            var bodyStart = source.IndexOf('{', signatureStart + signature.Length);
            Assert.That(bodyStart, Is.GreaterThan(signatureStart), $"Missing method body after signature: {signature}");

            var depth = 0;
            for (var index = bodyStart; index < source.Length; index++)
            {
                if (IsLineCommentStart(source, index))
                {
                    index = AdvancePastLineComment(source, index);
                    continue;
                }

                if (IsBlockCommentStart(source, index))
                {
                    index = AdvancePastBlockComment(source, index);
                    continue;
                }

                if (source[index] == '"')
                {
                    index = AdvancePastStringLiteral(source, index);
                    continue;
                }

                if (source[index] == '\'')
                {
                    index = AdvancePastCharLiteral(source, index);
                    continue;
                }

                if (source[index] == '{')
                {
                    depth++;
                    continue;
                }

                if (source[index] != '}')
                {
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    return source.Substring(signatureStart, index - signatureStart + 1);
                }
            }

            Assert.Fail($"Missing method end for signature: {signature}");
            return string.Empty;
        }

        public static string ExtractInvocation(string source, string marker)
        {
            var start = source.IndexOf(marker, System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing invocation marker: {marker}");

            var openParen = source.IndexOf('(', start + marker.Length);
            Assert.That(openParen, Is.GreaterThan(start), $"Missing invocation arguments after marker: {marker}");

            var depth = 0;
            for (var index = openParen; index < source.Length; index++)
            {
                if (source[index] == '(')
                {
                    depth++;
                    continue;
                }

                if (source[index] != ')')
                {
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, index - start + 1);
                }
            }

            Assert.Fail($"Missing invocation end for marker: {marker}");
            return string.Empty;
        }

        public static void AssertArgumentsInOrder(string invocation, params string[] arguments)
        {
            var previousIndex = -1;
            foreach (var argument in arguments)
            {
                var index = invocation.IndexOf(argument, System.StringComparison.Ordinal);
                Assert.That(index, Is.GreaterThan(previousIndex),
                    $"Expected {argument} after previous argument in {invocation}");
                previousIndex = index;
            }
        }

        private static bool IsLineCommentStart(string source, int index)
        {
            return index + 1 < source.Length && source[index] == '/' && source[index + 1] == '/';
        }

        private static bool IsBlockCommentStart(string source, int index)
        {
            return index + 1 < source.Length && source[index] == '/' && source[index + 1] == '*';
        }

        private static int AdvancePastLineComment(string source, int start)
        {
            var newline = source.IndexOf('\n', start + 2);
            return newline >= 0 ? newline : source.Length - 1;
        }

        private static int AdvancePastBlockComment(string source, int start)
        {
            var end = source.IndexOf("*/", start + 2, System.StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThanOrEqualTo(0), "Unterminated block comment while scanning method region.");
            return end + 1;
        }

        private static int AdvancePastStringLiteral(string source, int quoteIndex)
        {
            var isVerbatim = IsVerbatimStringLiteral(source, quoteIndex);
            var quoteCount = CountConsecutiveQuotes(source, quoteIndex);
            if (!isVerbatim && quoteCount >= 3)
            {
                return AdvancePastRawStringLiteral(source, quoteIndex, quoteCount);
            }

            for (var index = quoteIndex + 1; index < source.Length; index++)
            {
                if (isVerbatim)
                {
                    if (source[index] != '"')
                    {
                        continue;
                    }

                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index++;
                        continue;
                    }

                    return index;
                }

                if (source[index] == '\\')
                {
                    index++;
                    continue;
                }

                if (source[index] == '"')
                {
                    return index;
                }
            }

            Assert.Fail("Unterminated string literal while scanning method region.");
            return source.Length - 1;
        }

        private static int AdvancePastRawStringLiteral(string source, int quoteIndex, int quoteCount)
        {
            for (var index = quoteIndex + quoteCount; index <= source.Length - quoteCount; index++)
            {
                if (CountConsecutiveQuotes(source, index) >= quoteCount)
                {
                    return index + quoteCount - 1;
                }
            }

            Assert.Fail("Unterminated raw string literal while scanning method region.");
            return source.Length - 1;
        }

        private static int AdvancePastCharLiteral(string source, int quoteIndex)
        {
            for (var index = quoteIndex + 1; index < source.Length; index++)
            {
                if (source[index] == '\\')
                {
                    index++;
                    continue;
                }

                if (source[index] == '\'')
                {
                    return index;
                }
            }

            Assert.Fail("Unterminated char literal while scanning method region.");
            return source.Length - 1;
        }

        private static bool IsVerbatimStringLiteral(string source, int quoteIndex)
        {
            return (quoteIndex > 0 && source[quoteIndex - 1] == '@')
                || (quoteIndex > 1 && source[quoteIndex - 2] == '@' && source[quoteIndex - 1] == '$');
        }

        private static int CountConsecutiveQuotes(string source, int start)
        {
            var count = 0;
            for (var index = start; index < source.Length && source[index] == '"'; index++)
            {
                count++;
            }

            return count;
        }
    }
}


