using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigPipelinePlanBuilder
    {
        public ConfigPipelinePlan Build(
            string projectRoot,
            string configSetId,
            string packageIdentity,
            IEnumerable<string> inputRelativePaths,
            IReadOnlyList<ConfigArtifact> artifacts,
            IEnumerable<string> deleteRelativePaths = null)
        {
            string normalizedRoot = ConfigPathGuard.NormalizeProjectRoot(projectRoot);
            if (string.IsNullOrWhiteSpace(configSetId))
            {
                throw new ArgumentException("Config set ID is required.", nameof(configSetId));
            }

            if (string.IsNullOrWhiteSpace(packageIdentity))
            {
                throw new ArgumentException("Package identity is required.", nameof(packageIdentity));
            }

            var inputHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string input in (inputRelativePaths ?? Array.Empty<string>())
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                string normalized = ConfigPathGuard.NormalizeRelativePath(input);
                string absolute = ConfigPathGuard.ResolveInside(normalizedRoot, normalized);
                if (!File.Exists(absolute))
                {
                    throw new FileNotFoundException("Plan input is missing.", absolute);
                }

                if (inputHashes.ContainsKey(normalized))
                {
                    throw new InvalidOperationException("Duplicate Plan input '" + normalized + "'.");
                }

                inputHashes.Add(normalized, HashFile(absolute));
            }

            var artifactsByPath = new Dictionary<string, ConfigArtifact>(StringComparer.Ordinal);
            foreach (ConfigArtifact artifact in artifacts ??
                     throw new ArgumentNullException(nameof(artifacts)))
            {
                string normalized = ConfigPathGuard.NormalizeRelativePath(artifact.RelativePath);
                if (artifactsByPath.ContainsKey(normalized))
                {
                    throw new InvalidOperationException(
                        "Multiple artifacts target '" + normalized + "'.");
                }

                artifactsByPath.Add(
                    normalized,
                    new ConfigArtifact(normalized, artifact.Content));
            }

            var deletes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in deleteRelativePaths ?? Array.Empty<string>())
            {
                string normalized = ConfigPathGuard.NormalizeRelativePath(path);
                if (artifactsByPath.ContainsKey(normalized) || !deletes.Add(normalized))
                {
                    throw new InvalidOperationException(
                        "Delete path conflicts with another planned path '" + normalized + "'.");
                }
            }

            ValidateUnityPairs(normalizedRoot, artifactsByPath.Keys, deletes);
            var entries = new List<ConfigPlanEntry>();
            foreach (KeyValuePair<string, ConfigArtifact> artifact in artifactsByPath.OrderBy(
                         pair => pair.Key,
                         StringComparer.Ordinal))
            {
                string absolute = ConfigPathGuard.ResolveInside(normalizedRoot, artifact.Key);
                string existingHash = File.Exists(absolute) ? HashFile(absolute) : null;
                string plannedHash = ConfigHash.Sha256(artifact.Value.Content);
                entries.Add(new ConfigPlanEntry(
                    artifact.Key,
                    existingHash == null
                        ? ConfigPlanAction.Create
                        : string.Equals(existingHash, plannedHash, StringComparison.Ordinal)
                            ? ConfigPlanAction.Unchanged
                            : ConfigPlanAction.Update,
                    existingHash,
                    plannedHash));
            }

            foreach (string delete in deletes.OrderBy(value => value, StringComparer.Ordinal))
            {
                string absolute = ConfigPathGuard.ResolveInside(normalizedRoot, delete);
                string existingHash = File.Exists(absolute) ? HashFile(absolute) : null;
                entries.Add(new ConfigPlanEntry(
                    delete,
                    existingHash == null ? ConfigPlanAction.Unchanged : ConfigPlanAction.Delete,
                    existingHash,
                    null));
            }

            byte[] planIdentity = BuildIdentity(configSetId, packageIdentity, inputHashes, entries);
            return new ConfigPipelinePlan(
                ConfigHash.Sha256(planIdentity),
                configSetId,
                packageIdentity,
                inputHashes,
                entries);
        }

        internal static string HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                return ConfigHash.Sha256(stream);
            }
        }

        private static byte[] BuildIdentity(
            string configSetId,
            string packageIdentity,
            IReadOnlyDictionary<string, string> inputHashes,
            IReadOnlyList<ConfigPlanEntry> entries)
        {
            var inputs = inputHashes
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ConfigProperty(pair.Key, new ConfigStringNode(pair.Value)));
            var entryNodes = entries.Select(entry => (ConfigNode)new ConfigObjectNode(new[]
            {
                new ConfigProperty("path", new ConfigStringNode(entry.RelativePath)),
                new ConfigProperty("action", new ConfigStringNode(entry.Action.ToString())),
                new ConfigProperty(
                    "existingHash",
                    new ConfigStringNode(entry.ExistingHash ?? string.Empty)),
                new ConfigProperty(
                    "plannedHash",
                    new ConfigStringNode(entry.PlannedHash ?? string.Empty))
            }));
            return CanonicalJsonWriter.WriteUtf8(new ConfigObjectNode(new[]
            {
                new ConfigProperty("configSetId", new ConfigStringNode(configSetId)),
                new ConfigProperty("packageIdentity", new ConfigStringNode(packageIdentity)),
                new ConfigProperty("inputs", new ConfigObjectNode(inputs)),
                new ConfigProperty("entries", new ConfigArrayNode(entryNodes))
            }));
        }

        private static void ValidateUnityPairs(
            string projectRoot,
            IEnumerable<string> writes,
            ISet<string> deletes)
        {
            var writeSet = new HashSet<string>(writes, StringComparer.Ordinal);
            foreach (string path in writeSet)
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    path.EndsWith(".meta", StringComparison.Ordinal))
                {
                    continue;
                }

                string absolute = ConfigPathGuard.ResolveInside(projectRoot, path);
                if (!File.Exists(absolute) && !writeSet.Contains(path + ".meta"))
                {
                    throw new InvalidOperationException(
                        "New Unity artifact requires paired .meta: " + path);
                }
            }

            foreach (string path in deletes)
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    path.EndsWith(".meta", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!deletes.Contains(path + ".meta"))
                {
                    throw new InvalidOperationException(
                        "Deleted Unity artifact requires paired .meta: " + path);
                }
            }
        }
    }
}
