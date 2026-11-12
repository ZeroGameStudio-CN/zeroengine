namespace ZeroEngine.Cinematic
{
    public static class CinematicStableId
    {
        public static bool IsValid(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var normalizedId = id.Trim();
            if (!IsLowercaseAsciiLetterOrDigit(normalizedId[0]) ||
                !IsLowercaseAsciiLetterOrDigit(normalizedId[normalizedId.Length - 1]))
            {
                return false;
            }

            for (var i = 1; i < normalizedId.Length - 1; i++)
            {
                var c = normalizedId[i];
                if (!IsLowercaseAsciiLetterOrDigit(c) &&
                    c != '.' &&
                    c != '_' &&
                    c != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsLowercaseAsciiLetterOrDigit(char c)
        {
            return c >= 'a' && c <= 'z' ||
                   c >= '0' && c <= '9';
        }
    }
}
