using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZeroEngine.AssetCatalog
{
    [Serializable]
    public sealed class AssetCatalogIdentity : IEquatable<AssetCatalogIdentity>
    {
        public string projectId;
        public string guid;
        public long subAssetKey;

        public string StableKey => projectId + ":" + guid + ":" + subAssetKey.ToString(CultureInfo.InvariantCulture);

        public bool Equals(AssetCatalogIdentity other)
        {
            return other != null &&
                   string.Equals(projectId, other.projectId, StringComparison.Ordinal) &&
                   string.Equals(guid, other.guid, StringComparison.OrdinalIgnoreCase) &&
                   subAssetKey == other.subAssetKey;
        }

        public override bool Equals(object obj) => Equals(obj as AssetCatalogIdentity);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(StableKey);
    }

    [Serializable]
    public sealed class AssetCatalogSourceRevision
    {
        public string repository;
        public string branch;
        public string changeset;
    }

    [Serializable]
    public sealed class AssetCatalogRecord
    {
        public AssetCatalogIdentity identity = new AssetCatalogIdentity();
        public string path;
        public string assetType;
        public string[] facets = Array.Empty<string>();
        public string mainObjectType;
        public string dependencyHash;
        public string technicalMetadataJson = "{}";
        public int metadataSchemaVersion = 1;
        public AssetCatalogSourceRevision sourceRevision = new AssetCatalogSourceRevision();
        public string firstSeenAtUtc;
        public string lastSeenAtUtc;
        public string deletedAtUtc;
        public string currentApprovedRevisionId;
        public string reviewStatus = AssetCatalogReviewStatus.Pending;
        public int recordRevision = 1;
    }

    [Serializable]
    public sealed class AssetCatalogSemanticRevision
    {
        public string revisionId;
        public string descriptionZh;
        public string descriptionEn;
        public string[] controlledTags = Array.Empty<string>();
        public string[] freeTags = Array.Empty<string>();
        public float confidence;
        public string source;
        public string modelLabel;
        public string modelDigest;
        public string promptVersion;
        public string classifierVersion;
        public int taxonomyVersion;
        public string basedOnDependencyHash;
        public string createdByAccountId;
        public string createdByDisplayName;
        public string createdAtUtc;
        public string approvedByAccountId;
        public string approvedByDisplayName;
        public string approvedAtUtc;
        public string supersedesRevisionId;
        public string status = AssetCatalogRevisionStatus.Proposal;
        public string etag;
    }

    [Serializable]
    public sealed class AssetCatalogTagDefinition
    {
        public string tagId;
        public string axis;
        public string nameZh;
        public string nameEn;
        public string[] aliases = Array.Empty<string>();
        public bool enabled = true;
    }

    [Serializable]
    public sealed class AssetCatalogTaxonomy
    {
        public int version;
        public AssetCatalogTagDefinition[] tagDefinitions = Array.Empty<AssetCatalogTagDefinition>();
    }

    [Serializable]
    public sealed class AssetCatalogProposalInput
    {
        public string descriptionZh;
        public string descriptionEn;
        public string[] controlledTags = Array.Empty<string>();
        public string[] freeTags = Array.Empty<string>();
        public float confidence;
        public string source = AssetCatalogRevisionSource.Human;
        public string modelLabel;
        public string modelDigest;
        public string promptVersion;
        public string classifierVersion;
        public int taxonomyVersion;
        public string basedOnDependencyHash;
    }

    [Serializable]
    public sealed class AssetCatalogSearchQuery
    {
        public string text;
        public string assetType;
        public string facet;
        public string tag;
        public string reviewStatus;
        public string cursor;
        public int pageSize = 50;
    }

    [Serializable]
    public sealed class AssetCatalogSource
    {
        public string projectId;
        public string displayName;
        public string sourceKind;
        public string repository;
        public string allowedBranch;
        public string[] scanRoots = Array.Empty<string>();
        public string ownerAccountId;
        public string visibility;
        public string sourceStatus;
        public string previewPolicy;
        public string role;
    }

    [Serializable]
    public sealed class AssetCatalogSourceDirectory
    {
        public AssetCatalogSource[] sources = Array.Empty<AssetCatalogSource>();
    }

    [Serializable]
    public sealed class AssetCatalogGlobalSearchQuery
    {
        public string scope = "all";
        public string[] projectIds = Array.Empty<string>();
        public string text;
        public string assetType;
        public string facet;
        public string tag;
        public string reviewStatus;
        public string cursor;
        public int pageSize = 50;
    }

    [Serializable]
    public sealed class AssetCatalogSearchResult
    {
        public AssetCatalogRecord record;
        public AssetCatalogSemanticRevision approvedRevision;
        public int fullTextRank;
        public int semanticRank;
        public int fusedRank;
        public bool semanticPending;
    }

    public static class AssetCatalogAssetType
    {
        public const string Prefab = "prefab";
        public const string Texture = "texture";
        public const string Sprite = "sprite";
        public const string Material = "material";
        public const string Shader = "shader";
        public const string Model = "model";
        public const string Animation = "animation";
        public const string Audio = "audio";
        public const string Video = "video";
        public const string Font = "font";
        public const string Scene = "scene";
        public const string Data = "data";
        public const string Other = "other";

        public static readonly string[] All =
        {
            Prefab, Texture, Sprite, Material, Shader, Model, Animation, Audio,
            Video, Font, Scene, Data, Other
        };
    }

    public static class AssetCatalogReviewStatus
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string NeedsReview = "needsReview";
        public const string Rejected = "rejected";
    }

    public static class AssetCatalogRevisionStatus
    {
        public const string Proposal = "proposal";
        public const string Approved = "approved";
        public const string Rejected = "rejected";
        public const string Superseded = "superseded";
    }

    public static class AssetCatalogRevisionSource
    {
        public const string Human = "human";
        public const string Model = "model";
        public const string Migrated = "migrated";
        public const string Rule = "rule";
    }

    public static class AssetCatalogContracts
    {
        public const int ApiMajor = 1;
        public const int SchemaVersion = 1;
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;
        public const int MaxAiCandidates = 40;
        private static readonly Regex GuidPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TagPattern = new Regex("^[a-z][a-z0-9._:-]{1,95}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static AssetCatalogIdentity CreateIdentity(string projectId, string guid, long subAssetKey)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("projectId is required.", nameof(projectId));
            if (subAssetKey < 0) throw new ArgumentOutOfRangeException(nameof(subAssetKey), "subAssetKey cannot be negative.");
            string normalizedGuid = NormalizeGuid(guid);
            return new AssetCatalogIdentity { projectId = projectId.Trim(), guid = normalizedGuid, subAssetKey = subAssetKey };
        }

        public static string NormalizeGuid(string guid)
        {
            string normalized = (guid ?? string.Empty).Trim().ToLowerInvariant();
            if (!GuidPattern.IsMatch(normalized)) throw new ArgumentException("guid must be a lowercase 32-character hexadecimal Unity GUID.", nameof(guid));
            return normalized;
        }

        public static void ValidateRecord(AssetCatalogRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.identity = CreateIdentity(record.identity?.projectId, record.identity?.guid, record.identity?.subAssetKey ?? 0);
            if (string.IsNullOrWhiteSpace(record.path) || !record.path.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("path must be a Unity-relative Assets/ path.", nameof(record));
            record.path = record.path.Replace('\\', '/');
            if (!AssetCatalogAssetType.All.Contains(record.assetType ?? string.Empty, StringComparer.Ordinal))
                throw new ArgumentException("assetType is not supported.", nameof(record));
            if (string.IsNullOrWhiteSpace(record.dependencyHash)) throw new ArgumentException("dependencyHash is required.", nameof(record));
            if (record.metadataSchemaVersion < 1) throw new ArgumentException("metadataSchemaVersion must be positive.", nameof(record));
            ValidateSourceRevision(record.sourceRevision);
            record.facets = NormalizeValues(record.facets, 64);
        }

        public static void ValidateSourceRevision(AssetCatalogSourceRevision sourceRevision)
        {
            if (sourceRevision == null || string.IsNullOrWhiteSpace(sourceRevision.repository) || string.IsNullOrWhiteSpace(sourceRevision.branch) || string.IsNullOrWhiteSpace(sourceRevision.changeset))
                throw new ArgumentException("sourceRevision repository, branch, and changeset are required.", nameof(sourceRevision));
            if (!string.Equals(sourceRevision.branch.Trim(), "/main", StringComparison.Ordinal))
                throw new ArgumentException("Only the authoritative /main source revision can publish catalog facts.", nameof(sourceRevision));
        }

        public static void ValidateProposal(AssetCatalogProposalInput proposal, AssetCatalogTaxonomy taxonomy, string expectedDependencyHash)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (string.IsNullOrWhiteSpace(proposal.descriptionZh) || string.IsNullOrWhiteSpace(proposal.descriptionEn))
                throw new ArgumentException("Both descriptionZh and descriptionEn are required.", nameof(proposal));
            if (proposal.confidence < 0f || proposal.confidence > 1f) throw new ArgumentOutOfRangeException(nameof(proposal), "confidence must be between zero and one.");
            if (taxonomy == null || proposal.taxonomyVersion != taxonomy.version) throw new ArgumentException("proposal taxonomyVersion is stale.", nameof(proposal));
            if (!string.Equals(proposal.basedOnDependencyHash, expectedDependencyHash, StringComparison.Ordinal))
                throw new ArgumentException("proposal basedOnDependencyHash is stale.", nameof(proposal));
            if (!IsRevisionSource(proposal.source)) throw new ArgumentException("proposal source is not supported.", nameof(proposal));
            if (string.Equals(proposal.source, AssetCatalogRevisionSource.Model, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(proposal.modelLabel) || string.IsNullOrWhiteSpace(proposal.promptVersion) || string.IsNullOrWhiteSpace(proposal.classifierVersion)))
                throw new ArgumentException("model proposals require modelLabel, promptVersion, and classifierVersion.", nameof(proposal));
            HashSet<string> enabledTags = new HashSet<string>((taxonomy.tagDefinitions ?? Array.Empty<AssetCatalogTagDefinition>())
                .Where(item => item != null && item.enabled)
                .Select(item => item.tagId), StringComparer.Ordinal);
            proposal.controlledTags = NormalizeValues(proposal.controlledTags, 32);
            if (proposal.controlledTags.Any(tag => !enabledTags.Contains(tag))) throw new ArgumentException("proposal contains a disabled or unknown controlled tag.", nameof(proposal));
            proposal.freeTags = NormalizeValues(proposal.freeTags, 32);
        }

        public static void ValidateTaxonomy(AssetCatalogTaxonomy taxonomy, int expectedVersion)
        {
            if (taxonomy == null) throw new ArgumentNullException(nameof(taxonomy));
            if (taxonomy.version != expectedVersion + 1) throw new ArgumentException("taxonomy version must increase by exactly one.", nameof(taxonomy));
            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssetCatalogTagDefinition definition in taxonomy.tagDefinitions ?? Array.Empty<AssetCatalogTagDefinition>())
            {
                if (definition == null || !TagPattern.IsMatch(definition.tagId ?? string.Empty) || !known.Add(definition.tagId))
                    throw new ArgumentException("taxonomy contains an invalid or duplicate tagId.", nameof(taxonomy));
                if (string.IsNullOrWhiteSpace(definition.axis) || string.IsNullOrWhiteSpace(definition.nameZh) || string.IsNullOrWhiteSpace(definition.nameEn))
                    throw new ArgumentException("taxonomy tag definitions require axis, nameZh, and nameEn.", nameof(taxonomy));
                definition.aliases = NormalizeValues(definition.aliases, 32);
            }
        }

        public static bool IsEndpointAllowed(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)) return false;
            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                   (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));
        }

        public static string[] NormalizeValues(IEnumerable<string> values, int maximum)
        {
            string[] result = (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (result.Length > maximum) throw new ArgumentException("Too many values.", nameof(values));
            return result;
        }

        public static bool IsRevisionSource(string value)
        {
            return string.Equals(value, AssetCatalogRevisionSource.Human, StringComparison.Ordinal) ||
                   string.Equals(value, AssetCatalogRevisionSource.Model, StringComparison.Ordinal) ||
                   string.Equals(value, AssetCatalogRevisionSource.Migrated, StringComparison.Ordinal) ||
                   string.Equals(value, AssetCatalogRevisionSource.Rule, StringComparison.Ordinal);
        }
    }
}
