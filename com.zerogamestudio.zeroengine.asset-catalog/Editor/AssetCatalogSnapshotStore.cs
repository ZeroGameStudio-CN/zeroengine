using System;
using System.Collections.Generic;
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
            RecordEnvelope envelope = new RecordEnvelope { records = (records ?? Array.Empty<AssetCatalogSnapshotRecord>()).ToArray() };
            string json = JsonUtility.ToJson(envelope, false);
            using (SHA256 algorithm = SHA256.Create())
            {
                return string.Concat(algorithm.ComputeHash(Encoding.UTF8.GetBytes(json)).Select(value => value.ToString("x2")));
            }
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

        [Serializable]
        private sealed class RecordEnvelope
        {
            public AssetCatalogSnapshotRecord[] records;
        }
    }
}
