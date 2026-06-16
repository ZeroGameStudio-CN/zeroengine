using System;
using System.IO;

namespace ZeroEngine.ModSystem
{
    public static class ModPathResolver
    {
        public static bool TryResolveRelativePath(
            string rootPath,
            string relativePath,
            out string fullPath,
            out string error)
        {
            fullPath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                error = "Mod root path must not be empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                error = "Mod content path must be relative and non-empty.";
                return false;
            }

            if (Path.IsPathRooted(relativePath))
            {
                error = "Mod content path must be relative.";
                return false;
            }

            string normalizedRoot = Path.GetFullPath(rootPath);
            string normalizedCandidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
            string rootWithSeparator = EnsureTrailingSeparator(normalizedRoot);

            if (!normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                error = "Mod content path must stay inside the mod root.";
                return false;
            }

            fullPath = normalizedCandidate;
            return true;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;

            return path + Path.DirectorySeparatorChar;
        }
    }
}
