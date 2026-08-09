using System;
using System.IO;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    internal static class ConfigPathGuard
    {
        public static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            return Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath) ||
                relativePath.IndexOf('\\') >= 0)
            {
                throw new ArgumentException("Path must be normalized and project-relative.");
            }

            string[] segments = relativePath.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    throw new ArgumentException("Path traversal and empty segments are forbidden.");
                }
            }

            return string.Join("/", segments);
        }

        public static string ResolveInside(string projectRoot, string relativePath)
        {
            string root = NormalizeProjectRoot(projectRoot);
            string normalized = NormalizeRelativePath(relativePath);
            string absolute = Path.GetFullPath(
                Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Resolved path escapes the project root.");
            }

            RejectExistingReparsePoints(root, absolute);
            return absolute;
        }

        private static void RejectExistingReparsePoints(string root, string target)
        {
            string relative = target.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = root;
            foreach (string segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((Directory.Exists(current) || File.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "Config paths cannot traverse reparse points: " + current);
                }
            }
        }
    }
}
