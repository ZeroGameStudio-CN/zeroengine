using System;
using System.Collections.Generic;

namespace ZeroEngine.Localization
{
    /// <summary>
    /// Normalizes locale identifiers at the ZE boundary so providers can use
    /// either BCP-47 hyphens or the common underscore spelling.
    /// </summary>
    public static class LocaleCode
    {
        public static string Normalize(string localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode))
            {
                return string.Empty;
            }

            string[] rawParts = localeCode.Trim().Replace('_', '-').Split('-');
            var parts = new List<string>(rawParts.Length);
            for (int i = 0; i < rawParts.Length; i++)
            {
                string part = rawParts[i].Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (parts.Count == 0)
                {
                    parts.Add(part.ToLowerInvariant());
                }
                else if (part.Length == 4)
                {
                    parts.Add(char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant());
                }
                else if (part.Length == 2 || part.Length == 3)
                {
                    parts.Add(part.ToUpperInvariant());
                }
                else
                {
                    parts.Add(part);
                }
            }

            return string.Join("-", parts.ToArray());
        }

        public static bool Equals(string left, string right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }
    }

    public readonly struct LocaleResolution
    {
        internal LocaleResolution(
            string requestedLocale,
            IReadOnlyList<string> candidates,
            bool usedFallback)
        {
            RequestedLocale = requestedLocale;
            Candidates = candidates;
            UsedFallback = usedFallback;
        }

        public string RequestedLocale { get; }
        public IReadOnlyList<string> Candidates { get; }
        public bool UsedFallback { get; }
        public bool IsResolved => Candidates != null && Candidates.Count > 0;
    }

    /// <summary>
    /// Deterministic locale candidate ordering shared by providers and UI.
    /// A regional locale is attempted before its language parent, followed by
    /// configured fallbacks and the default locale.
    /// </summary>
    public sealed class LocaleFallbackPolicy
    {
        private readonly string _defaultLocale;
        private readonly string[] _fallbackLocales;

        public LocaleFallbackPolicy(string defaultLocale, IReadOnlyList<string> fallbackLocales = null)
        {
            _defaultLocale = LocaleCode.Normalize(defaultLocale);
            _fallbackLocales = NormalizeDistinct(fallbackLocales);
        }

        public string DefaultLocale => _defaultLocale;

        public IReadOnlyList<string> FallbackLocales => _fallbackLocales;

        public LocaleResolution Resolve(
            string requestedLocale,
            IReadOnlyList<string> availableLocales = null)
        {
            string normalizedRequested = LocaleCode.Normalize(requestedLocale);
            var requestedCandidates = new List<string>();
            AddWithParents(requestedCandidates, normalizedRequested);

            for (int i = 0; i < _fallbackLocales.Length; i++)
            {
                AddWithParents(requestedCandidates, _fallbackLocales[i]);
            }

            AddWithParents(requestedCandidates, _defaultLocale);

            var availableMap = BuildAvailableMap(availableLocales);
            var candidates = new List<string>(requestedCandidates.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < requestedCandidates.Count; i++)
            {
                string candidate = requestedCandidates[i];
                string resolved = candidate;
                if (availableMap != null)
                {
                    if (!availableMap.TryGetValue(candidate, out resolved))
                    {
                        continue;
                    }
                }

                if (seen.Add(resolved))
                {
                    candidates.Add(resolved);
                }
            }

            bool usedFallback = candidates.Count > 0 &&
                                !LocaleCode.Equals(candidates[0], normalizedRequested);
            return new LocaleResolution(
                normalizedRequested,
                candidates.ToArray(),
                usedFallback);
        }

        private static string[] NormalizeDistinct(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(values.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < values.Count; i++)
            {
                string value = LocaleCode.Normalize(values[i]);
                if (value.Length > 0 && seen.Add(value))
                {
                    result.Add(value);
                }
            }

            return result.ToArray();
        }

        private static Dictionary<string, string> BuildAvailableMap(IReadOnlyList<string> availableLocales)
        {
            if (availableLocales == null || availableLocales.Count == 0)
            {
                return null;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < availableLocales.Count; i++)
            {
                string original = availableLocales[i];
                string normalized = LocaleCode.Normalize(original);
                if (normalized.Length > 0 && !result.ContainsKey(normalized))
                {
                    result.Add(normalized, original.Trim());
                }
            }

            return result;
        }

        private static void AddWithParents(List<string> candidates, string localeCode)
        {
            string candidate = LocaleCode.Normalize(localeCode);
            while (candidate.Length > 0)
            {
                bool alreadyAdded = false;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (string.Equals(candidates[i], candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded)
                {
                    candidates.Add(candidate);
                }

                int separator = candidate.LastIndexOf('-');
                if (separator < 0)
                {
                    break;
                }

                candidate = candidate.Substring(0, separator);
            }
        }
    }
}
