using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ZeroEngine.EditorTools.SourceGuards
{
    public static class SourceGuardScanner
    {
        public static IReadOnlyList<SourceGuardLineMatch> FindLineMatches(
            string searchRoot,
            string relativeRoot,
            Regex linePattern,
            Func<string, bool> excludeRelativePath = null)
        {
            if (string.IsNullOrWhiteSpace(searchRoot))
            {
                throw new ArgumentException("Search root is required.", nameof(searchRoot));
            }

            if (string.IsNullOrWhiteSpace(relativeRoot))
            {
                throw new ArgumentException("Relative root is required.", nameof(relativeRoot));
            }

            if (linePattern == null)
            {
                throw new ArgumentNullException(nameof(linePattern));
            }

            var matches = new List<SourceGuardLineMatch>();
            foreach (var path in Directory.EnumerateFiles(searchRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = NormalizeRelativePath(relativeRoot, path);
                if (excludeRelativePath != null && excludeRelativePath(relativePath))
                {
                    continue;
                }

                var lines = File.ReadAllLines(path);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!linePattern.IsMatch(lines[i]))
                    {
                        continue;
                    }

                    matches.Add(new SourceGuardLineMatch(relativePath, i + 1, NormalizeLine(lines[i])));
                }
            }

            return matches;
        }

        public static string NormalizeRelativePath(string relativeRoot, string path)
        {
            return Path.GetRelativePath(relativeRoot, path).Replace('\\', '/');
        }

        public static string NormalizeLine(string line)
        {
            return Regex.Replace(line.Trim(), @"\s+", " ");
        }
    }

    public readonly struct SourceGuardLineMatch
    {
        public SourceGuardLineMatch(string path, int lineNumber, string line)
        {
            Path = path;
            LineNumber = lineNumber;
            Line = line;
        }

        public string Path { get; }

        public int LineNumber { get; }

        public string Line { get; }

        public string Key => $"{Path} :: {Line}";
    }
}
