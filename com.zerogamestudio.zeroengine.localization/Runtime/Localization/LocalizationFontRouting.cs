using System;
using System.Collections.Generic;

namespace ZeroEngine.Localization
{
    public readonly struct LocaleFontRoute<TFont>
    {
        public LocaleFontRoute(string localeCode, string role, TFont font)
        {
            LocaleCode = localeCode;
            Role = role;
            Font = font;
        }

        public string LocaleCode { get; }
        public string Role { get; }
        public TFont Font { get; }
    }

    public interface ILocaleFontResolver<TFont>
    {
        bool TryResolve(string localeCode, string role, out TFont font);
    }

    /// <summary>
    /// Locale/role font routing without a dependency on TMP or UnityEngine.
    /// Consumers can provide a Unity-aware validity predicate when needed.
    /// </summary>
    public sealed class LocaleFontRouter<TFont> : ILocaleFontResolver<TFont>
    {
        private const string DefaultRole = "default";
        private readonly Dictionary<string, Dictionary<string, TFont>> _routes =
            new Dictionary<string, Dictionary<string, TFont>>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<TFont, bool> _isValidFont;

        public LocaleFontRouter(Func<TFont, bool> isValidFont = null)
        {
            _isValidFont = isValidFont ?? IsNonDefault;
        }

        public bool TryRegister(string localeCode, string role, TFont font)
        {
            string locale = NormalizeRouteLocale(localeCode);
            string normalizedRole = NormalizeRole(role);
            if (locale.Length == 0 || normalizedRole.Length == 0 || !_isValidFont(font))
            {
                return false;
            }

            Dictionary<string, TFont> roleMap;
            if (!_routes.TryGetValue(locale, out roleMap))
            {
                roleMap = new Dictionary<string, TFont>(StringComparer.OrdinalIgnoreCase);
                _routes.Add(locale, roleMap);
            }

            if (roleMap.ContainsKey(normalizedRole))
            {
                return false;
            }

            roleMap.Add(normalizedRole, font);
            return true;
        }

        public void Register(string localeCode, string role, TFont font)
        {
            if (!TryRegister(localeCode, role, font))
            {
                throw new ArgumentException("Font route is invalid or already registered.");
            }
        }

        public bool TryResolve(string localeCode, string role, out TFont font)
        {
            font = default(TFont);
            string requestedLocale = LocaleCode.Normalize(localeCode);
            string requestedRole = NormalizeRole(role);
            if (requestedLocale.Length == 0 || requestedRole.Length == 0)
            {
                return false;
            }

            var localeCandidates = new List<string>();
            AddLocaleParents(localeCandidates, requestedLocale);
            localeCandidates.Add("*");
            for (int localeIndex = 0; localeIndex < localeCandidates.Count; localeIndex++)
            {
                Dictionary<string, TFont> roleMap;
                if (!_routes.TryGetValue(localeCandidates[localeIndex], out roleMap))
                {
                    continue;
                }

                if (TryGetValid(roleMap, requestedRole, out font) ||
                    TryGetValid(roleMap, DefaultRole, out font) ||
                    TryGetValid(roleMap, "*", out font))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetValid(
            Dictionary<string, TFont> roleMap,
            string role,
            out TFont font)
        {
            if (roleMap.TryGetValue(role, out font) && _isValidFont(font))
            {
                return true;
            }

            font = default(TFont);
            return false;
        }

        private static string NormalizeRouteLocale(string localeCode)
        {
            string normalized = LocaleCode.Normalize(localeCode);
            return normalized.Length == 0 && string.Equals(localeCode, "*", StringComparison.Ordinal)
                ? "*"
                : normalized;
        }

        private static string NormalizeRole(string role)
        {
            string normalized = role == null ? string.Empty : role.Trim();
            if (normalized == "*")
            {
                return normalized;
            }

            return normalized;
        }

        private static void AddLocaleParents(List<string> candidates, string localeCode)
        {
            string candidate = localeCode;
            while (candidate.Length > 0)
            {
                candidates.Add(candidate);
                int separator = candidate.LastIndexOf('-');
                if (separator < 0)
                {
                    break;
                }

                candidate = candidate.Substring(0, separator);
            }
        }

        private static bool IsNonDefault(TFont font)
        {
            return !EqualityComparer<TFont>.Default.Equals(font, default(TFont));
        }
    }

    public enum FontRouteValidationIssueCode
    {
        EmptyLocale,
        EmptyRole,
        InvalidFont,
        DuplicateRoute,
        MissingRequiredRoute
    }

    public readonly struct FontRouteValidationIssue
    {
        public FontRouteValidationIssue(
            FontRouteValidationIssueCode code,
            string localeCode,
            string role)
        {
            Code = code;
            LocaleCode = localeCode;
            Role = role;
        }

        public FontRouteValidationIssueCode Code { get; }
        public string LocaleCode { get; }
        public string Role { get; }
    }

    public readonly struct FontRouteValidationResult
    {
        internal FontRouteValidationResult(IReadOnlyList<FontRouteValidationIssue> issues)
        {
            Issues = issues;
        }

        public IReadOnlyList<FontRouteValidationIssue> Issues { get; }
        public bool IsValid => Issues == null || Issues.Count == 0;
    }

    public static class LocaleFontRouteValidator
    {
        public static FontRouteValidationResult Validate<TFont>(
            IEnumerable<LocaleFontRoute<TFont>> routes,
            IEnumerable<string> requiredLocales,
            IEnumerable<string> requiredRoles,
            Func<TFont, bool> isValidFont = null)
        {
            Func<TFont, bool> validity = isValidFont ?? IsNonDefault;
            var issues = new List<FontRouteValidationIssue>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (routes != null)
            {
                foreach (LocaleFontRoute<TFont> route in routes)
                {
                    string locale = route.LocaleCode == "*" ? "*" : LocaleCode.Normalize(route.LocaleCode);
                    string role = route.Role == null ? string.Empty : route.Role.Trim();
                    if (locale.Length == 0)
                    {
                        issues.Add(new FontRouteValidationIssue(
                            FontRouteValidationIssueCode.EmptyLocale,
                            route.LocaleCode,
                            role));
                    }

                    if (role.Length == 0)
                    {
                        issues.Add(new FontRouteValidationIssue(
                            FontRouteValidationIssueCode.EmptyRole,
                            locale,
                            route.Role));
                    }

                    if (!validity(route.Font))
                    {
                        issues.Add(new FontRouteValidationIssue(
                            FontRouteValidationIssueCode.InvalidFont,
                            locale,
                            role));
                    }

                    string routeKey = locale + "\n" + role;
                    if (!seen.Add(routeKey))
                    {
                        issues.Add(new FontRouteValidationIssue(
                            FontRouteValidationIssueCode.DuplicateRoute,
                            locale,
                            role));
                    }
                }
            }

            if (requiredLocales != null && requiredRoles != null)
            {
                foreach (string requiredLocale in requiredLocales)
                {
                    string locale = LocaleCode.Normalize(requiredLocale);
                    foreach (string requiredRole in requiredRoles)
                    {
                        string role = requiredRole == null ? string.Empty : requiredRole.Trim();
                        if (!seen.Contains(locale + "\n" + role))
                        {
                            issues.Add(new FontRouteValidationIssue(
                                FontRouteValidationIssueCode.MissingRequiredRoute,
                                locale,
                                role));
                        }
                    }
                }
            }

            return new FontRouteValidationResult(issues.ToArray());
        }

        private static bool IsNonDefault<TFont>(TFont font)
        {
            return !EqualityComparer<TFont>.Default.Equals(font, default(TFont));
        }
    }
}
