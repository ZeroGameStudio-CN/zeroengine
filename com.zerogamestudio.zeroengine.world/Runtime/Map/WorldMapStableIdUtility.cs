using System;
using System.Text;

namespace ZeroEngine.World.Map
{
    public static class WorldMapStableIdUtility
    {
        public static bool IsStableId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
            {
                return false;
            }

            if (!IsStableIdAlphaNumeric(id[0]) || !IsStableIdAlphaNumeric(id[id.Length - 1]))
            {
                return false;
            }

            foreach (var character in id)
            {
                if (IsStableIdAlphaNumeric(character)
                    || character == '.'
                    || character == '_'
                    || character == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        public static string ToStableIdSegment(string value, string fallback = "marker")
        {
            var builder = new StringBuilder();
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                for (var i = 0; i < trimmed.Length; i++)
                {
                    var character = char.ToLowerInvariant(trimmed[i]);
                    if (IsStableIdAlphaNumeric(character))
                    {
                        builder.Append(character);
                    }
                    else if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                    {
                        builder.Append('-');
                    }
                }
            }

            var segment = TrimSeparators(builder.ToString());
            if (!string.IsNullOrEmpty(segment))
            {
                return segment;
            }

            var fallbackSegment = TrimSeparators(ToStableIdSegmentWithoutFallback(fallback));
            return string.IsNullOrEmpty(fallbackSegment) ? "marker" : fallbackSegment;
        }

        public static string CreateStableId(string prefix, string value, string fallbackSegment = "marker")
        {
            var stablePrefix = IsStableId(prefix)
                ? prefix
                : ToStableIdSegment(prefix, "map");
            var segment = ToStableIdSegment(value, fallbackSegment);
            return string.IsNullOrEmpty(stablePrefix) ? segment : $"{stablePrefix}.{segment}";
        }

        private static string ToStableIdSegmentWithoutFallback(string value)
        {
            var builder = new StringBuilder();
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return string.Empty;
            }

            for (var i = 0; i < trimmed.Length; i++)
            {
                var character = char.ToLowerInvariant(trimmed[i]);
                if (IsStableIdAlphaNumeric(character))
                {
                    builder.Append(character);
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }
            }

            return builder.ToString();
        }

        private static string TrimSeparators(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Trim('.', '_', '-');
        }

        private static bool IsStableIdAlphaNumeric(char character)
        {
            return character >= 'a' && character <= 'z'
                   || character >= '0' && character <= '9';
        }
    }
}
