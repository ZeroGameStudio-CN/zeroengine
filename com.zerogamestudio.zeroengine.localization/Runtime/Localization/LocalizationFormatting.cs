using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZeroEngine.Localization
{
    public static class MissingKeyFormatter
    {
        public static string Format(string key)
        {
            return "[" + (string.IsNullOrEmpty(key) ? "key" : key) + "]";
        }
    }

    public static class LocalizationFormatter
    {
        public static bool TryFormat(
            string template,
            IReadOnlyList<object> arguments,
            out string formatted,
            out LocalizationFormatDiagnostic diagnostic)
        {
            formatted = template ?? string.Empty;
            diagnostic = default(LocalizationFormatDiagnostic);
            if (arguments == null || arguments.Count == 0 || string.IsNullOrEmpty(template))
            {
                return true;
            }

            var values = new object[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
            {
                values[i] = arguments[i];
            }

            try
            {
                formatted = string.Format(CultureInfo.InvariantCulture, template, values);
                return true;
            }
            catch (FormatException exception)
            {
                diagnostic = new LocalizationFormatDiagnostic(
                    "invalid-format",
                    arguments.Count,
                    exception.GetType().Name);
                formatted = MissingKeyFormatter.Format(template);
                return false;
            }
            catch (Exception exception)
            {
                diagnostic = new LocalizationFormatDiagnostic(
                    "format-exception",
                    arguments.Count,
                    exception.GetType().Name);
                formatted = MissingKeyFormatter.Format(template);
                return false;
            }
        }
    }

    public enum LocalizationPlaceholderIssueCode
    {
        InvalidFormat,
        MissingPlaceholder,
        UnexpectedPlaceholder
    }

    public readonly struct LocalizationPlaceholderIssue
    {
        public LocalizationPlaceholderIssue(
            string localeCode,
            LocalizationPlaceholderIssueCode code,
            string placeholder)
        {
            LocaleCode = localeCode;
            Code = code;
            Placeholder = placeholder;
        }

        public string LocaleCode { get; }
        public LocalizationPlaceholderIssueCode Code { get; }
        public string Placeholder { get; }
    }

    public readonly struct LocalizationPlaceholderValidationResult
    {
        internal LocalizationPlaceholderValidationResult(IReadOnlyList<LocalizationPlaceholderIssue> issues)
        {
            Issues = issues;
        }

        public IReadOnlyList<LocalizationPlaceholderIssue> Issues { get; }
        public bool IsValid => Issues == null || Issues.Count == 0;
    }

    /// <summary>
    /// Validates that translated entries preserve the source format tokens.
    /// It deliberately exposes only token names and locale codes, never user
    /// supplied formatting arguments.
    /// </summary>
    public static class LocalizationPlaceholderValidator
    {
        public static LocalizationPlaceholderValidationResult Validate(
            string sourceText,
            IReadOnlyDictionary<string, string> translations)
        {
            var issues = new List<LocalizationPlaceholderIssue>();
            var sourceTokens = new HashSet<string>(StringComparer.Ordinal);
            if (!TryReadTokens(sourceText, sourceTokens))
            {
                issues.Add(new LocalizationPlaceholderIssue(
                    string.Empty,
                    LocalizationPlaceholderIssueCode.InvalidFormat,
                    string.Empty));
            }

            if (translations != null)
            {
                foreach (KeyValuePair<string, string> translation in translations)
                {
                    var translatedTokens = new HashSet<string>(StringComparer.Ordinal);
                    if (!TryReadTokens(translation.Value, translatedTokens))
                    {
                        issues.Add(new LocalizationPlaceholderIssue(
                            translation.Key,
                            LocalizationPlaceholderIssueCode.InvalidFormat,
                            string.Empty));
                        continue;
                    }

                    foreach (string token in sourceTokens)
                    {
                        if (!translatedTokens.Contains(token))
                        {
                            issues.Add(new LocalizationPlaceholderIssue(
                                translation.Key,
                                LocalizationPlaceholderIssueCode.MissingPlaceholder,
                                token));
                        }
                    }

                    foreach (string token in translatedTokens)
                    {
                        if (!sourceTokens.Contains(token))
                        {
                            issues.Add(new LocalizationPlaceholderIssue(
                                translation.Key,
                                LocalizationPlaceholderIssueCode.UnexpectedPlaceholder,
                                token));
                        }
                    }
                }
            }

            return new LocalizationPlaceholderValidationResult(issues.ToArray());
        }

        private static bool TryReadTokens(string text, HashSet<string> tokens)
        {
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            for (int index = 0; index < text.Length; index++)
            {
                char current = text[index];
                if (current == '{')
                {
                    if (index + 1 < text.Length && text[index + 1] == '{')
                    {
                        index++;
                        continue;
                    }

                    int close = text.IndexOf('}', index + 1);
                    if (close < 0)
                    {
                        return false;
                    }

                    string token = text.Substring(index + 1, close - index - 1);
                    int separator = token.IndexOfAny(new[] { ',', ':' });
                    if (separator >= 0)
                    {
                        token = token.Substring(0, separator);
                    }

                    token = token.Trim();
                    if (token.Length == 0)
                    {
                        return false;
                    }

                    tokens.Add(token);
                    index = close;
                }
                else if (current == '}')
                {
                    if (index + 1 < text.Length && text[index + 1] == '}')
                    {
                        index++;
                        continue;
                    }

                    return false;
                }
            }

            return true;
        }
    }
}
