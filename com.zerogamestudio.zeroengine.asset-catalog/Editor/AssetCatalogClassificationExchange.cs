using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ZeroEngine.AssetCatalog
{
    [Serializable]
    public sealed class AssetCatalogClassificationRun
    {
        public int schemaVersion = 1;
        public string runId;
        public string createdAtUtc;
        public string classifierVersion;
        public string classifierModel;
        public string classifierModelDigest;
        public int taxonomyVersion;
        public string previewProfileVersion;
        public AssetCatalogSourceRevision sourceRevision;
    }

    [Serializable]
    public sealed class AssetCatalogClassificationItem
    {
        public AssetCatalogIdentity identity;
        public string path;
        public string assetType;
        public string[] facets = Array.Empty<string>();
        public string dependencyHash;
        public AssetCatalogSourceRevision sourceRevision;
        public string previewProfileVersion;
        public string previewRelativePath;
    }

    [Serializable]
    public sealed class AssetCatalogClassificationResult
    {
        public int schemaVersion = 1;
        public string runId;
        public AssetCatalogIdentity identity;
        public string dependencyHash;
        public string descriptionZh;
        public string descriptionEn;
        public string[] controlledTags = Array.Empty<string>();
        public string[] freeTags = Array.Empty<string>();
        public float confidence;
        public string modelLabel;
        public string modelDigest;
        public string promptVersion;
        public string classifierVersion;
        public int taxonomyVersion;
    }

    public static class AssetCatalogClassificationExchange
    {
        public const int SchemaVersion = 1;
        public const string RunFileName = "run.json";
        public const string ItemsFileName = "items.jsonl";
        public const string ResultsFileName = "results.jsonl";

        public static string CreateRunDirectory(string rootDirectory, AssetCatalogClassificationRun run, IEnumerable<AssetCatalogClassificationItem> items)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathRooted(rootDirectory)) throw new ArgumentException("rootDirectory must be an absolute user-local path.", nameof(rootDirectory));
            ValidateRun(run);
            string runDirectory = Path.Combine(rootDirectory, run.runId);
            if (Directory.Exists(runDirectory)) throw new IOException("Classification run directory already exists.");
            Directory.CreateDirectory(runDirectory);
            try
            {
                File.WriteAllText(Path.Combine(runDirectory, RunFileName), JsonUtility.ToJson(run, true), new UTF8Encoding(false));
                AssetCatalogClassificationItem[] ordered = (items ?? Array.Empty<AssetCatalogClassificationItem>())
                    .OrderBy(item => item?.identity?.StableKey, StringComparer.Ordinal)
                    .ToArray();
                using (StreamWriter writer = new StreamWriter(Path.Combine(runDirectory, ItemsFileName), false, new UTF8Encoding(false)))
                {
                    foreach (AssetCatalogClassificationItem item in ordered)
                    {
                        ValidateItem(item, run);
                        writer.WriteLine(JsonUtility.ToJson(item, false));
                    }
                }
                File.WriteAllText(Path.Combine(runDirectory, ResultsFileName), string.Empty, new UTF8Encoding(false));
                return runDirectory;
            }
            catch
            {
                if (Directory.Exists(runDirectory)) Directory.Delete(runDirectory, true);
                throw;
            }
        }

        public static void AppendResult(string runDirectory, AssetCatalogClassificationResult result)
        {
            if (string.IsNullOrWhiteSpace(runDirectory) || !Path.IsPathRooted(runDirectory)) throw new ArgumentException("runDirectory must be an absolute user-local path.", nameof(runDirectory));
            AssetCatalogClassificationRun run = LoadRun(runDirectory);
            if (result == null || !string.Equals(result.runId, run.runId, StringComparison.Ordinal)) throw new ArgumentException("Result runId does not match the exchange run.", nameof(result));
            string resultsPath = Path.Combine(runDirectory, ResultsFileName);
            if (!File.Exists(resultsPath)) throw new FileNotFoundException("Classification results file was not found.", resultsPath);
            using (StreamWriter writer = new StreamWriter(resultsPath, true, new UTF8Encoding(false))) writer.WriteLine(JsonUtility.ToJson(result, false));
        }

        public static AssetCatalogClassificationRun LoadRun(string runDirectory)
        {
            string path = Path.Combine(runDirectory ?? string.Empty, RunFileName);
            if (!File.Exists(path)) throw new FileNotFoundException("Classification run file was not found.", path);
            AssetCatalogClassificationRun run = JsonUtility.FromJson<AssetCatalogClassificationRun>(File.ReadAllText(path, Encoding.UTF8));
            ValidateRun(run);
            return run;
        }

        public static List<AssetCatalogClassificationItem> ReadItems(string runDirectory)
        {
            AssetCatalogClassificationRun run = LoadRun(runDirectory);
            string path = Path.Combine(runDirectory, ItemsFileName);
            List<AssetCatalogClassificationItem> items = new List<AssetCatalogClassificationItem>();
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                AssetCatalogClassificationItem item = JsonUtility.FromJson<AssetCatalogClassificationItem>(line);
                ValidateItem(item, run);
                items.Add(item);
            }
            return items;
        }

        public static List<AssetCatalogClassificationResult> ReadAndValidateResults(string runDirectory, AssetCatalogTaxonomy taxonomy)
        {
            AssetCatalogClassificationRun run = LoadRun(runDirectory);
            Dictionary<string, AssetCatalogClassificationItem> byIdentity = ReadItems(runDirectory)
                .ToDictionary(item => item.identity.StableKey, StringComparer.Ordinal);
            string path = Path.Combine(runDirectory, ResultsFileName);
            List<AssetCatalogClassificationResult> results = new List<AssetCatalogClassificationResult>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                AssetCatalogClassificationResult result = JsonUtility.FromJson<AssetCatalogClassificationResult>(line);
                if (result?.identity == null || !byIdentity.TryGetValue(result.identity.StableKey, out AssetCatalogClassificationItem item))
                    throw new InvalidDataException("Classification result has an unknown asset identity.");
                if (!seen.Add(result.identity.StableKey)) throw new InvalidDataException("Classification results contain duplicate asset identities.");
                ValidateResult(result, item, run, taxonomy);
                results.Add(result);
            }
            return results;
        }

        public static AssetCatalogProposalInput ToProposal(AssetCatalogClassificationResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return new AssetCatalogProposalInput
            {
                descriptionZh = result.descriptionZh,
                descriptionEn = result.descriptionEn,
                controlledTags = result.controlledTags,
                freeTags = result.freeTags,
                confidence = result.confidence,
                source = AssetCatalogRevisionSource.Model,
                modelLabel = result.modelLabel,
                modelDigest = result.modelDigest,
                promptVersion = result.promptVersion,
                classifierVersion = result.classifierVersion,
                taxonomyVersion = result.taxonomyVersion,
                basedOnDependencyHash = result.dependencyHash
            };
        }

        private static void ValidateRun(AssetCatalogClassificationRun run)
        {
            if (run == null || run.schemaVersion != SchemaVersion || string.IsNullOrWhiteSpace(run.runId) || run.runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Classification run is invalid.", nameof(run));
            if (string.IsNullOrWhiteSpace(run.classifierVersion) || string.IsNullOrWhiteSpace(run.classifierModel) || string.IsNullOrWhiteSpace(run.previewProfileVersion) || run.taxonomyVersion < 1)
                throw new ArgumentException("Classification run provenance is incomplete.", nameof(run));
            AssetCatalogContracts.ValidateSourceRevision(run.sourceRevision);
        }

        private static void ValidateItem(AssetCatalogClassificationItem item, AssetCatalogClassificationRun run)
        {
            if (item?.identity == null) throw new ArgumentException("Classification item identity is required.", nameof(item));
            AssetCatalogContracts.CreateIdentity(item.identity.projectId, item.identity.guid, item.identity.subAssetKey);
            if (string.IsNullOrWhiteSpace(item.path) || Path.IsPathRooted(item.path) || !item.path.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Classification item path must be Unity-relative.", nameof(item));
            if (string.IsNullOrWhiteSpace(item.dependencyHash) || !string.Equals(item.previewProfileVersion, run.previewProfileVersion, StringComparison.Ordinal))
                throw new ArgumentException("Classification item hash or preview profile is invalid.", nameof(item));
            AssetCatalogContracts.ValidateSourceRevision(item.sourceRevision);
        }

        private static void ValidateResult(AssetCatalogClassificationResult result, AssetCatalogClassificationItem item, AssetCatalogClassificationRun run, AssetCatalogTaxonomy taxonomy)
        {
            if (result == null || result.schemaVersion != SchemaVersion || !string.Equals(result.runId, run.runId, StringComparison.Ordinal) || !string.Equals(result.dependencyHash, item.dependencyHash, StringComparison.Ordinal))
                throw new InvalidDataException("Classification result identity or dependency hash is stale.");
            if (string.IsNullOrWhiteSpace(result.modelLabel) || string.IsNullOrWhiteSpace(result.promptVersion) || string.IsNullOrWhiteSpace(result.classifierVersion))
                throw new InvalidDataException("Classification result model provenance is incomplete.");
            AssetCatalogProposalInput proposal = ToProposal(result);
            AssetCatalogContracts.ValidateProposal(proposal, taxonomy, item.dependencyHash);
        }
    }
}
