using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.ParticleCatalog
{
    public static class ParticleCatalogStore
    {
        public static ParticleCatalogManifest LoadFromFile(string path)
        {
            return !File.Exists(path) ? new ParticleCatalogManifest() : LoadFromJson(File.ReadAllText(path));
        }

        public static ParticleCatalogManifest LoadFromJson(string json)
        {
            ParticleCatalogManifest manifest = JsonUtility.FromJson<ParticleCatalogManifest>(json);
            if (manifest == null) throw new InvalidDataException("Particle catalog JSON could not be parsed.");
            if (manifest.schemaVersion > ParticleCatalogManifest.CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Particle catalog schema {manifest.schemaVersion} is newer than supported {ParticleCatalogManifest.CurrentSchemaVersion}.");
            }
            manifest.entries = manifest.entries ?? new List<ParticleCatalogEntry>();
            if (manifest.schemaVersion < 2) ApplyLegacySummaries(manifest, json);
            Normalize(manifest);
            return manifest;
        }

        public static void SaveToPath(ParticleCatalogManifest manifest, string path, bool preferAtomicReplace = true)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            Normalize(manifest);
            manifest.schemaVersion = ParticleCatalogManifest.CurrentSchemaVersion;
            manifest.classifierVersion = ParticleCatalogManifest.CurrentClassifierVersion;
            manifest.generatedAtUtc = DateTime.UtcNow.ToString("O");
            manifest.entries = manifest.entries.OrderBy(entry => entry.path, StringComparer.OrdinalIgnoreCase).ToList();

            string tempPath = path + ".tmp";
            string backupPath = path + ".bak";
            File.WriteAllText(tempPath, JsonUtility.ToJson(manifest, true));
            ValidateSavedManifest(tempPath, manifest.entries.Count);
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                ValidateSavedManifest(path, manifest.entries.Count);
                return;
            }

            try
            {
                File.Copy(path, backupPath, true);
                if (preferAtomicReplace)
                {
                    try
                    {
                        File.Replace(tempPath, path, null);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is PlatformNotSupportedException)
                    {
                        File.Copy(tempPath, path, true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Copy(tempPath, path, true);
                    File.Delete(tempPath);
                }
                ValidateSavedManifest(path, manifest.entries.Count);
                File.Delete(backupPath);
            }
            catch
            {
                if (File.Exists(backupPath)) File.Copy(backupPath, path, true);
                throw;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
        }

        public static List<ParticleCatalogEntry> Search(ParticleCatalogManifest manifest, string query, int limit = 200)
        {
            string normalized = ParticleCatalogTaxonomy.NormalizeQuery(query);
            string[] tokens = normalized.Split(new[] { ' ', '\t', ',', '，', '/', '+' }, StringSplitOptions.RemoveEmptyEntries);
            List<ParticleCatalogEntry> entries = manifest?.entries ?? new List<ParticleCatalogEntry>();
            if (tokens.Length == 0) return entries.Take(limit).ToList();

            return entries.Select(entry => new { Entry = entry, Score = Score(entry, tokens) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Entry.path, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(item => item.Entry)
                .ToList();
        }

        public static List<ParticleCatalogEntry> GetRequiredClassificationEntries(ParticleCatalogManifest manifest, int limit = int.MaxValue)
        {
            return (manifest?.entries ?? new List<ParticleCatalogEntry>())
                .Where(entry => entry.NeedsBilingualBackfill)
                .OrderBy(entry => entry.path, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }

        private static void Normalize(ParticleCatalogManifest manifest)
        {
            foreach (ParticleCatalogEntry entry in manifest.entries)
            {
                entry.purposes = entry.purposes ?? Array.Empty<string>();
                entry.elements = entry.elements ?? Array.Empty<string>();
                entry.shapes = entry.shapes ?? Array.Empty<string>();
                entry.motions = entry.motions ?? Array.Empty<string>();
                entry.colors = entry.colors ?? Array.Empty<string>();
                entry.timings = entry.timings ?? Array.Empty<string>();
                entry.styles = entry.styles ?? Array.Empty<string>();
                entry.performance = entry.performance ?? Array.Empty<string>();
                if (!string.IsNullOrWhiteSpace(entry.classifiedBy) && entry.classifiedBy.StartsWith("ai:", StringComparison.Ordinal))
                {
                    entry.classifierModel = entry.classifiedBy.Substring(3);
                    entry.classifiedBy = "ollama";
                }
                else if (!string.IsNullOrWhiteSpace(entry.classifiedBy) && entry.classifiedBy.StartsWith("rules", StringComparison.OrdinalIgnoreCase))
                {
                    entry.classifiedBy = "rules";
                }
                if (string.IsNullOrWhiteSpace(entry.classifierVersion)) entry.classifierVersion = manifest.schemaVersion < 2 ? "legacy-v1" : manifest.classifierVersion;
            }
            manifest.schemaVersion = ParticleCatalogManifest.CurrentSchemaVersion;
            if (string.IsNullOrWhiteSpace(manifest.classifierVersion)) manifest.classifierVersion = ParticleCatalogManifest.CurrentClassifierVersion;
        }

        private static int Score(ParticleCatalogEntry entry, string[] tokens)
        {
            string path = (entry.path ?? string.Empty).ToLowerInvariant();
            string summary = ((entry.summaryZh ?? string.Empty) + " " + (entry.summaryEn ?? string.Empty)).ToLowerInvariant();
            string tags = string.Join(" ", EnumerateTags(entry)).ToLowerInvariant();
            int score = 0;
            foreach (string token in tokens)
            {
                if (tags.Contains(token)) score += 6;
                if (summary.Contains(token)) score += 4;
                if (path.Contains(token)) score += 2;
            }
            return score;
        }

        private static IEnumerable<string> EnumerateTags(ParticleCatalogEntry entry)
        {
            return (entry.purposes ?? Array.Empty<string>()).Concat(entry.elements ?? Array.Empty<string>())
                .Concat(entry.shapes ?? Array.Empty<string>()).Concat(entry.motions ?? Array.Empty<string>())
                .Concat(entry.colors ?? Array.Empty<string>()).Concat(entry.timings ?? Array.Empty<string>())
                .Concat(entry.styles ?? Array.Empty<string>()).Concat(entry.performance ?? Array.Empty<string>());
        }

        private static void ApplyLegacySummaries(ParticleCatalogManifest manifest, string json)
        {
            LegacySummaryManifest legacy = JsonUtility.FromJson<LegacySummaryManifest>(json);
            if (legacy?.entries == null) return;
            Dictionary<string, string> summaries = legacy.entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.guid) && !string.IsNullOrWhiteSpace(entry.summary))
                .GroupBy(entry => entry.guid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().summary, StringComparer.OrdinalIgnoreCase);
            foreach (ParticleCatalogEntry entry in manifest.entries)
            {
                if (!summaries.TryGetValue(entry.guid ?? string.Empty, out string summary)) continue;
                if (summary.Any(character => character >= '\u3400' && character <= '\u9fff')) entry.summaryZh = summary;
                else entry.summaryEn = summary;
            }
        }

        private static void ValidateSavedManifest(string path, int expectedEntryCount)
        {
            ParticleCatalogManifest saved = LoadFromJson(File.ReadAllText(path));
            if (saved.schemaVersion != ParticleCatalogManifest.CurrentSchemaVersion || saved.entries.Count != expectedEntryCount)
                throw new InvalidDataException("Particle catalog validation failed after write.");
            int uniqueGuids = saved.entries.Where(entry => !string.IsNullOrWhiteSpace(entry.guid))
                .Select(entry => entry.guid).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (uniqueGuids != saved.entries.Count) throw new InvalidDataException("Particle catalog contains empty or duplicate GUIDs.");
        }

        [Serializable]
        private sealed class LegacySummaryManifest
        {
            public List<LegacySummaryEntry> entries;
        }

        [Serializable]
        private sealed class LegacySummaryEntry
        {
            public string guid;
            public string summary;
        }
    }
}
