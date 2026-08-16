using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ZeroEngine.AssetCatalog
{
    [Serializable]
    public sealed class AssetCatalogSnapshotManifest
    {
        public int apiMajor = AssetCatalogContracts.ApiMajor;
        public int schemaVersion = AssetCatalogContracts.SchemaVersion;
        public int taxonomyVersion;
        public long catalogCursor;
        public string exportedAtUtc;
        public int recordCount;
        public string recordsSha256;
    }

    [Serializable]
    public sealed class AssetCatalogSnapshotRecord
    {
        public AssetCatalogRecord record = new AssetCatalogRecord();
        public AssetCatalogSemanticRevision approvedRevision;
    }

    [Serializable]
    public sealed class AssetCatalogSnapshot
    {
        public AssetCatalogSnapshotManifest manifest = new AssetCatalogSnapshotManifest();
        public AssetCatalogSnapshotRecord[] records = Array.Empty<AssetCatalogSnapshotRecord>();
    }

    public static class AssetCatalogSnapshotStore
    {
        public static string ToStableJson(AssetCatalogSnapshot snapshot, bool prettyPrint = false)
        {
            Normalize(snapshot, true);
            return JsonUtility.ToJson(snapshot, prettyPrint);
        }

        public static AssetCatalogSnapshot FromJson(string json)
        {
            AssetCatalogSnapshot snapshot = JsonUtility.FromJson<AssetCatalogSnapshot>(json);
            if (snapshot == null || snapshot.manifest == null) throw new InvalidDataException("Asset Catalog snapshot could not be parsed.");
            if (snapshot.manifest.apiMajor != AssetCatalogContracts.ApiMajor)
                throw new InvalidDataException("Asset Catalog API major is incompatible with this client.");
            if (snapshot.manifest.schemaVersion > AssetCatalogContracts.SchemaVersion)
                throw new InvalidDataException("Asset Catalog snapshot schema is newer than this client.");
            int declaredCount = snapshot.manifest.recordCount;
            string declaredHash = snapshot.manifest.recordsSha256;
            Normalize(snapshot, false);
            if (declaredCount != snapshot.records.Length || !string.Equals(declaredHash, ComputeRecordsSha256(snapshot.records), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Asset Catalog snapshot manifest does not match its records.");
            return snapshot;
        }

        public static AssetCatalogSnapshot LoadFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Asset Catalog snapshot was not found.", path);
            return FromJson(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void SaveToPath(AssetCatalogSnapshot snapshot, string path)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("path must include a directory.", nameof(path));
            Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, ToStableJson(snapshot, true), new UTF8Encoding(false));
                FromJson(File.ReadAllText(temporaryPath, Encoding.UTF8));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporaryPath, path, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public static string ComputeRecordsSha256(IEnumerable<AssetCatalogSnapshotRecord> records)
        {
            StringBuilder canonical = new StringBuilder();
            foreach (AssetCatalogSnapshotRecord item in (records ?? Array.Empty<AssetCatalogSnapshotRecord>())
                         .Where(candidate => candidate != null && candidate.record != null)
                         .OrderBy(candidate => candidate.record.identity?.projectId, StringComparer.Ordinal)
                         .ThenBy(candidate => candidate.record.identity?.guid, StringComparer.Ordinal)
                         .ThenBy(candidate => candidate.record.identity?.subAssetKey ?? 0))
            {
                AppendRecord(canonical, item.record);
                AppendRevision(canonical, item.approvedRevision);
            }
            using (SHA256 algorithm = SHA256.Create())
            {
                return string.Concat(algorithm.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())).Select(value => value.ToString("x2")));
            }
        }

        private static void AppendRecord(StringBuilder builder, AssetCatalogRecord record)
        {
            AppendValue(builder, record.identity?.projectId);
            AppendValue(builder, record.identity?.guid);
            AppendNumber(builder, record.identity?.subAssetKey ?? 0);
            AppendValue(builder, record.path);
            AppendValue(builder, record.assetType);
            AppendValues(builder, AssetCatalogContracts.NormalizeValues(record.facets, 64));
            AppendValue(builder, record.mainObjectType);
            AppendValue(builder, record.dependencyHash);
            AppendValue(builder, record.technicalMetadataJson);
            AppendNumber(builder, record.metadataSchemaVersion);
            AppendValue(builder, record.sourceRevision?.repository);
            AppendValue(builder, record.sourceRevision?.branch);
            AppendValue(builder, record.sourceRevision?.changeset);
            AppendValue(builder, record.firstSeenAtUtc);
            AppendValue(builder, record.lastSeenAtUtc);
            AppendValue(builder, record.deletedAtUtc);
            AppendValue(builder, record.currentApprovedRevisionId);
            AppendValue(builder, record.reviewStatus);
            AppendNumber(builder, record.recordRevision);
        }

        private static void AppendRevision(StringBuilder builder, AssetCatalogSemanticRevision revision)
        {
            AppendValue(builder, revision == null ? null : "revision");
            if (revision == null) return;
            AppendValue(builder, revision.revisionId);
            AppendValue(builder, revision.descriptionZh);
            AppendValue(builder, revision.descriptionEn);
            AppendValues(builder, AssetCatalogContracts.NormalizeValues(revision.controlledTags, 32));
            AppendValues(builder, AssetCatalogContracts.NormalizeValues(revision.freeTags, 32));
            AppendNumber(builder, (long)Math.Floor(Math.Max(0f, revision.confidence) * 1000000f + 0.5f));
            AppendValue(builder, revision.source);
            AppendValue(builder, revision.modelLabel);
            AppendValue(builder, revision.modelDigest);
            AppendValue(builder, revision.promptVersion);
            AppendValue(builder, revision.classifierVersion);
            AppendNumber(builder, revision.taxonomyVersion);
            AppendValue(builder, revision.basedOnDependencyHash);
            AppendValue(builder, revision.createdByAccountId);
            AppendValue(builder, revision.createdByDisplayName);
            AppendValue(builder, revision.createdAtUtc);
            AppendValue(builder, revision.approvedByAccountId);
            AppendValue(builder, revision.approvedByDisplayName);
            AppendValue(builder, revision.approvedAtUtc);
            AppendValue(builder, revision.supersedesRevisionId);
            AppendValue(builder, revision.status);
            AppendValue(builder, revision.etag);
        }

        private static void AppendValues(StringBuilder builder, IEnumerable<string> values)
        {
            string[] normalized = (values ?? Array.Empty<string>()).ToArray();
            AppendNumber(builder, normalized.Length);
            foreach (string value in normalized) AppendValue(builder, value);
        }

        private static void AppendNumber(StringBuilder builder, long value)
        {
            AppendValue(builder, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendValue(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(Encoding.UTF8.GetByteCount(normalized).ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append('|');
        }

        private static void Normalize(AssetCatalogSnapshot snapshot, bool refreshManifest)
        {
            snapshot.records = (snapshot.records ?? Array.Empty<AssetCatalogSnapshotRecord>())
                .Where(item => item != null && item.record != null)
                .OrderBy(item => item.record.identity?.projectId, StringComparer.Ordinal)
                .ThenBy(item => item.record.identity?.guid, StringComparer.Ordinal)
                .ThenBy(item => item.record.identity?.subAssetKey ?? 0)
                .ToArray();
            foreach (AssetCatalogSnapshotRecord item in snapshot.records)
            {
                AssetCatalogContracts.ValidateRecord(item.record);
                // JsonUtility materializes an omitted/null serializable object as a default
                // instance. An approved revision is optional, so restore that distinction
                // before computing or validating the cross-client manifest hash.
                if (IsMissingApprovedRevision(item.approvedRevision)) item.approvedRevision = null;
                if (item.approvedRevision != null)
                {
                    item.approvedRevision.controlledTags = AssetCatalogContracts.NormalizeValues(item.approvedRevision.controlledTags, 32);
                    item.approvedRevision.freeTags = AssetCatalogContracts.NormalizeValues(item.approvedRevision.freeTags, 32);
                }
            }
            if (refreshManifest)
            {
                snapshot.manifest.apiMajor = AssetCatalogContracts.ApiMajor;
                snapshot.manifest.schemaVersion = AssetCatalogContracts.SchemaVersion;
                snapshot.manifest.recordCount = snapshot.records.Length;
                snapshot.manifest.recordsSha256 = ComputeRecordsSha256(snapshot.records);
            }
        }

        private static bool IsMissingApprovedRevision(AssetCatalogSemanticRevision revision)
        {
            if (revision == null) return false;
            return string.IsNullOrEmpty(revision.revisionId) &&
                   string.IsNullOrEmpty(revision.descriptionZh) &&
                   string.IsNullOrEmpty(revision.descriptionEn) &&
                   (revision.controlledTags == null || revision.controlledTags.Length == 0) &&
                   (revision.freeTags == null || revision.freeTags.Length == 0) &&
                   Math.Abs(revision.confidence) < 0.000001f &&
                   string.IsNullOrEmpty(revision.source) &&
                   string.IsNullOrEmpty(revision.modelLabel) &&
                   string.IsNullOrEmpty(revision.modelDigest) &&
                   string.IsNullOrEmpty(revision.promptVersion) &&
                   string.IsNullOrEmpty(revision.classifierVersion) &&
                   revision.taxonomyVersion == 0 &&
                   string.IsNullOrEmpty(revision.basedOnDependencyHash) &&
                   string.IsNullOrEmpty(revision.createdByAccountId) &&
                   string.IsNullOrEmpty(revision.createdByDisplayName) &&
                   string.IsNullOrEmpty(revision.createdAtUtc) &&
                   string.IsNullOrEmpty(revision.approvedByAccountId) &&
                   string.IsNullOrEmpty(revision.approvedByDisplayName) &&
                   string.IsNullOrEmpty(revision.approvedAtUtc) &&
                   string.IsNullOrEmpty(revision.supersedesRevisionId) &&
                   string.Equals(revision.status, AssetCatalogRevisionStatus.Proposal, StringComparison.Ordinal) &&
                   string.IsNullOrEmpty(revision.etag);
        }
    }
}
