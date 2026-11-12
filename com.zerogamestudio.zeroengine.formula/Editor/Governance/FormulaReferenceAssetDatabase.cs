using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace ZeroEngine.Formula.Editor
{
    public static class FormulaReferenceAssetDatabase
    {
        private static readonly HashSet<string> SupportedTextExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".asset",
                ".prefab",
                ".unity",
                ".mat",
                ".json",
                ".asmdef",
                ".asmref",
                ".txt",
                ".md",
                ".uxml",
                ".uss",
            };

        public static bool IsSupportedTextAssetPath(string assetPath)
        {
            return SupportedTextExtensions.Contains(Path.GetExtension(assetPath ?? string.Empty));
        }

        public static IReadOnlyList<FormulaAssetReference> FindGuidReferences(
            string formulaGuid,
            FormulaEditorProfile profile)
        {
            var documents = CollectTextDocuments(profile);
            var options = new FormulaReferenceSearchOptions(
                profile?.ReferenceRoots,
                profile?.ExcludedReferenceRoots);
            return new List<FormulaAssetReference>(
                FormulaReferenceIndexer.FindGuidReferences(formulaGuid, documents, options));
        }

        public static IReadOnlyList<FormulaReferenceTextDocument> CollectTextDocuments(FormulaEditorProfile profile)
        {
            var documents = new List<FormulaReferenceTextDocument>();
            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!IsSupportedTextAssetPath(assetPath))
                    continue;

                if (!IsIncluded(assetPath, profile))
                    continue;

                var fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath))
                    continue;

                documents.Add(new FormulaReferenceTextDocument(assetPath, File.ReadAllText(fullPath)));
            }

            return documents;
        }

        internal static IReadOnlyList<string> CollectCandidateAssetPaths(FormulaEditorProfile profile)
        {
            return AssetDatabase.GetAllAssetPaths()
                .Where(assetPath => IsSupportedTextAssetPath(assetPath) && IsIncluded(assetPath, profile))
                .Select(FormulaReferenceIndexer.NormalizePath)
                .ToArray();
        }

        internal static IReadOnlyList<FormulaReferenceFileSnapshot> CollectFileSnapshots(
            IReadOnlyList<string> assetPaths)
        {
            var snapshots = new List<FormulaReferenceFileSnapshot>();
            foreach (var assetPath in assetPaths ?? Array.Empty<string>())
            {
                var fullPath = Path.GetFullPath(assetPath);
                var file = new FileInfo(fullPath);
                if (!file.Exists)
                    continue;

                snapshots.Add(new FormulaReferenceFileSnapshot(
                    FormulaReferenceIndexer.NormalizePath(assetPath),
                    fullPath,
                    file.Length,
                    file.LastWriteTimeUtc.Ticks));
            }

            return snapshots;
        }

        private static bool IsIncluded(string assetPath, FormulaEditorProfile profile)
        {
            var options = new FormulaReferenceSearchOptions(
                profile?.ReferenceRoots,
                profile?.ExcludedReferenceRoots);
            return FormulaReferenceIndexer.IsPathIncluded(assetPath, options);
        }
    }

    internal sealed class FormulaReferenceFileSnapshot
    {
        internal FormulaReferenceFileSnapshot(
            string assetPath,
            string fullPath,
            long length,
            long lastWriteUtcTicks)
        {
            AssetPath = assetPath ?? string.Empty;
            FullPath = fullPath ?? string.Empty;
            Length = length;
            LastWriteUtcTicks = lastWriteUtcTicks;
        }

        internal string AssetPath { get; }
        internal string FullPath { get; }
        internal long Length { get; }
        internal long LastWriteUtcTicks { get; }
    }

    internal sealed class FormulaReferenceIndexDocument
    {
        internal FormulaReferenceIndexDocument(
            string assetPath,
            long length,
            long lastWriteUtcTicks,
            IReadOnlyList<string> formulaGuids)
        {
            AssetPath = assetPath ?? string.Empty;
            Length = length;
            LastWriteUtcTicks = lastWriteUtcTicks;
            FormulaGuids = formulaGuids ?? Array.Empty<string>();
        }

        internal string AssetPath { get; }
        internal long Length { get; }
        internal long LastWriteUtcTicks { get; }
        internal IReadOnlyList<string> FormulaGuids { get; }
    }

    internal sealed class FormulaReferenceIndexData
    {
        internal FormulaReferenceIndexData(
            int generation,
            string profileFingerprint,
            long updatedUtcTicks,
            IReadOnlyDictionary<string, FormulaReferenceIndexDocument> documents)
        {
            Generation = generation;
            ProfileFingerprint = profileFingerprint ?? string.Empty;
            UpdatedUtcTicks = updatedUtcTicks;
            Documents = documents ?? new Dictionary<string, FormulaReferenceIndexDocument>();
        }

        internal int Generation { get; }
        internal string ProfileFingerprint { get; }
        internal long UpdatedUtcTicks { get; }
        internal IReadOnlyDictionary<string, FormulaReferenceIndexDocument> Documents { get; }

        internal IReadOnlyList<FormulaAssetReference> CreateReferences()
        {
            var references = new List<FormulaAssetReference>();
            foreach (var document in Documents.Values.OrderBy(value => value.AssetPath, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var guid in document.FormulaGuids)
                    references.Add(new FormulaAssetReference(document.AssetPath, guid, "guid"));
            }

            return references;
        }
    }

    internal static class FormulaReferenceIndexCache
    {
        private const string Header = "ZEROENGINE_FORMULA_REFERENCE_INDEX";
        private const int SchemaVersion = 1;
        private static readonly object FileGate = new();

        internal static FormulaReferenceIndexData Build(
            int generation,
            string profileFingerprint,
            IReadOnlyList<FormulaReferenceFileSnapshot> snapshots,
            IReadOnlyList<string> formulaGuids,
            FormulaReferenceIndexData cached,
            bool forceFull,
            Func<string, string> readText)
        {
            var canonicalGuids = (formulaGuids ?? Array.Empty<string>())
                .Where(guid => !string.IsNullOrWhiteSpace(guid))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(guid => guid, guid => guid, StringComparer.OrdinalIgnoreCase);
            var documents = new Dictionary<string, FormulaReferenceIndexDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in snapshots ?? Array.Empty<FormulaReferenceFileSnapshot>())
            {
                if (snapshot == null || string.IsNullOrEmpty(snapshot.AssetPath))
                    continue;

                FormulaReferenceIndexDocument document = null;
                if (!forceFull
                    && cached?.Documents.TryGetValue(snapshot.AssetPath, out var previous) == true
                    && previous.Length == snapshot.Length
                    && previous.LastWriteUtcTicks == snapshot.LastWriteUtcTicks)
                {
                    var retainedGuids = previous.FormulaGuids
                        .Where(canonicalGuids.ContainsKey)
                        .Select(guid => canonicalGuids[guid])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    document = new FormulaReferenceIndexDocument(
                        snapshot.AssetPath,
                        snapshot.Length,
                        snapshot.LastWriteUtcTicks,
                        retainedGuids);
                }
                else
                {
                    string text;
                    try
                    {
                        text = readText(snapshot.FullPath) ?? string.Empty;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    document = new FormulaReferenceIndexDocument(
                        snapshot.AssetPath,
                        snapshot.Length,
                        snapshot.LastWriteUtcTicks,
                        FormulaReferenceIndexer.FindKnownFormulaGuids(text, canonicalGuids));
                }

                documents[document.AssetPath] = document;
            }

            return new FormulaReferenceIndexData(
                generation,
                profileFingerprint,
                DateTime.UtcNow.Ticks,
                documents);
        }

        internal static bool IsCurrent(
            FormulaReferenceIndexData data,
            int generation,
            string profileFingerprint)
        {
            return data != null
                && data.Generation == generation
                && string.Equals(data.ProfileFingerprint, profileFingerprint, StringComparison.Ordinal);
        }

        internal static FormulaReferenceIndexData Load(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
                return null;

            try
            {
                string[] lines;
                lock (FileGate)
                    lines = File.ReadAllLines(cachePath);
                if (lines.Length == 0)
                    return null;

                var header = lines[0].Split('\t');
                if (header.Length != 5
                    || !string.Equals(header[0], Header, StringComparison.Ordinal)
                    || !int.TryParse(header[1], out var schemaVersion)
                    || schemaVersion != SchemaVersion
                    || !int.TryParse(header[2], out var generation)
                    || !long.TryParse(header[3], out var updatedUtcTicks))
                    return null;

                var documents = new Dictionary<string, FormulaReferenceIndexDocument>(StringComparer.OrdinalIgnoreCase);
                for (var index = 1; index < lines.Length; index++)
                {
                    var parts = lines[index].Split('\t');
                    if (parts.Length != 5
                        || !string.Equals(parts[0], "D", StringComparison.Ordinal)
                        || !long.TryParse(parts[1], out var length)
                        || !long.TryParse(parts[2], out var lastWriteUtcTicks))
                        continue;

                    string assetPath;
                    try
                    {
                        assetPath = Encoding.UTF8.GetString(Convert.FromBase64String(parts[4]));
                    }
                    catch (FormatException)
                    {
                        continue;
                    }

                    var guids = string.IsNullOrEmpty(parts[3])
                        ? Array.Empty<string>()
                        : parts[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    documents[assetPath] = new FormulaReferenceIndexDocument(
                        assetPath,
                        length,
                        lastWriteUtcTicks,
                        guids);
                }

                return new FormulaReferenceIndexData(
                    generation,
                    header[4],
                    updatedUtcTicks,
                    documents);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        internal static void Save(string cachePath, FormulaReferenceIndexData data)
        {
            if (string.IsNullOrEmpty(cachePath) || data == null)
                return;

            var lines = new List<string>(data.Documents.Count + 1)
            {
                string.Join("\t", Header, SchemaVersion, data.Generation, data.UpdatedUtcTicks, data.ProfileFingerprint)
            };
            foreach (var document in data.Documents.Values.OrderBy(value => value.AssetPath, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(string.Join(
                    "\t",
                    "D",
                    document.Length,
                    document.LastWriteUtcTicks,
                    string.Join(",", document.FormulaGuids),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(document.AssetPath))));
            }

            var directory = Path.GetDirectoryName(cachePath);
            if (string.IsNullOrEmpty(directory))
                return;

            try
            {
                lock (FileGate)
                {
                    Directory.CreateDirectory(directory);
                    var temporaryPath = cachePath + ".tmp";
                    File.WriteAllLines(temporaryPath, lines);
                    File.Copy(temporaryPath, cachePath, true);
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        internal static string GetCachePath(string profileId)
        {
            var safeProfileId = new string((profileId ?? "default")
                .Select(character => char.IsLetterOrDigit(character) || character == '-' || character == '_'
                    ? character
                    : '_')
                .ToArray());
            return Path.Combine(GetCacheDirectory(), safeProfileId + ".index");
        }

        internal static int GetGeneration()
        {
            try
            {
                lock (FileGate)
                {
                    var path = GetGenerationPath();
                    return File.Exists(path) && int.TryParse(File.ReadAllText(path), out var generation)
                        ? generation
                        : 0;
                }
            }
            catch (IOException)
            {
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return 0;
            }
        }

        internal static void IncrementGeneration()
        {
            try
            {
                lock (FileGate)
                {
                    var directory = GetCacheDirectory();
                    Directory.CreateDirectory(directory);
                    var path = GetGenerationPath();
                    var generation = File.Exists(path) && int.TryParse(File.ReadAllText(path), out var current)
                        ? current + 1
                        : 1;
                    File.WriteAllText(path, generation.ToString());
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        internal static string CreateProfileFingerprint(
            FormulaEditorProfile profile,
            IReadOnlyList<FormulaCatalogAssetRecord> records)
        {
            var values = new List<string>
            {
                profile?.ProfileId ?? string.Empty,
                profile?.DefaultSearchRoot ?? string.Empty,
            };
            values.AddRange((profile?.ReferenceRoots ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            values.Add("--excluded--");
            values.AddRange((profile?.ExcludedReferenceRoots ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            values.Add("--formulas--");
            values.AddRange((records ?? Array.Empty<FormulaCatalogAssetRecord>())
                .Select(record => $"{record.FormulaGuid}:{record.AssetPath}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

            ulong hash = 14695981039346656037UL;
            foreach (var value in values)
            {
                foreach (var character in value ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }

                hash ^= '\n';
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16");
        }

        private static string GetCacheDirectory()
        {
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "Library",
                "ZeroEngine",
                "Formula",
                "ReferenceIndex");
        }

        private static string GetGenerationPath()
        {
            return Path.Combine(GetCacheDirectory(), "generation.txt");
        }
    }

    internal sealed class FormulaReferenceIndexAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (ContainsSupportedPath(importedAssets)
                || ContainsSupportedPath(deletedAssets)
                || ContainsSupportedPath(movedAssets)
                || ContainsSupportedPath(movedFromAssetPaths))
                FormulaReferenceIndexCache.IncrementGeneration();
        }

        private static bool ContainsSupportedPath(IEnumerable<string> paths)
        {
            return paths?.Any(FormulaReferenceAssetDatabase.IsSupportedTextAssetPath) == true;
        }
    }
}
