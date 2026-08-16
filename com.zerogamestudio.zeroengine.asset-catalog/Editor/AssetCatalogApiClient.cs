using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.AssetCatalog
{
    [Serializable]
    public sealed class AssetCatalogServiceSettings
    {
        public string endpoint;
        public string projectId;
    }

    [Serializable]
    public sealed class AssetCatalogApiMetadata
    {
        public int apiMajor;
        public int schemaVersion;
        public int taxonomyVersion;
        public string serverUtc;
        public long catalogCursor;
    }

    [Serializable]
    public sealed class AssetCatalogApiError
    {
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class AssetCatalogScanItem
    {
        public string guid;
        public long subAssetKey;
        public string path;
        public string assetType;
        public string[] facets = Array.Empty<string>();
        public string mainObjectType;
        public string dependencyHash;
        // Canonical JSON object text; the API parses and validates it as object data.
        public string technicalMetadata = "{}";
        public int metadataSchemaVersion = 1;
    }

    [Serializable]
    public sealed class AssetCatalogScanRequest
    {
        public AssetCatalogSourceRevision sourceRevision;
        public AssetCatalogScanItem[] items = Array.Empty<AssetCatalogScanItem>();
    }

    public sealed class AssetCatalogApiResponse
    {
        public int StatusCode { get; internal set; }
        public string Etag { get; internal set; }
        public string Body { get; internal set; }
        public AssetCatalogApiMetadata Metadata { get; internal set; }
        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

        public AssetCatalogApiError ReadError()
        {
            try
            {
                AssetCatalogApiErrorEnvelope envelope = JsonUtility.FromJson<AssetCatalogApiErrorEnvelope>(Body);
                return envelope?.error;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private sealed class AssetCatalogApiErrorEnvelope
        {
            public AssetCatalogApiError error = new AssetCatalogApiError();
        }
    }

    public sealed class AssetCatalogApiClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly Uri _endpoint;

        public AssetCatalogApiClient(AssetCatalogServiceSettings settings, HttpMessageHandler handler = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!AssetCatalogContracts.IsEndpointAllowed(settings.endpoint)) throw new ArgumentException("Catalog endpoint must use HTTPS unless it is loopback HTTP.", nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.projectId)) throw new ArgumentException("projectId is required.", nameof(settings));
            _endpoint = new Uri(settings.endpoint.TrimEnd('/') + "/", UriKind.Absolute);
            ProjectId = settings.projectId.Trim();
            _client = handler == null ? new HttpClient() : new HttpClient(handler, false);
            _client.Timeout = TimeSpan.FromSeconds(30);
        }

        public string ProjectId { get; }

        public Task<AssetCatalogApiResponse> GetHealthAsync() => SendAsync(HttpMethod.Get, "v1/health", null, null, null);
        public Task<AssetCatalogApiResponse> GetMeAsync(string accessToken) => SendAsync(HttpMethod.Get, "v1/me", accessToken, null, null);
        public Task<AssetCatalogApiResponse> GetTaxonomyAsync(string accessToken) => SendAsync(HttpMethod.Get, "v1/projects/" + Uri.EscapeDataString(ProjectId) + "/taxonomy", accessToken, null, null);
        public Task<AssetCatalogApiResponse> GetSnapshotAsync(string accessToken) => SendAsync(HttpMethod.Get, "v1/projects/" + Uri.EscapeDataString(ProjectId) + "/snapshot", accessToken, null, null);
        public Task<AssetCatalogApiResponse> SearchAsync(string accessToken, AssetCatalogSearchQuery query)
        {
            query = query ?? new AssetCatalogSearchQuery();
            int pageSize = query.pageSize <= 0 ? AssetCatalogContracts.DefaultPageSize : query.pageSize;
            if (pageSize > AssetCatalogContracts.MaxPageSize) throw new ArgumentOutOfRangeException(nameof(query));
            StringBuilder route = new StringBuilder("v1/projects/").Append(Uri.EscapeDataString(ProjectId)).Append("/search?q=").Append(Uri.EscapeDataString(query.text ?? string.Empty)).Append("&pageSize=").Append(pageSize);
            if (!string.IsNullOrWhiteSpace(query.assetType)) route.Append("&assetType=").Append(Uri.EscapeDataString(query.assetType));
            if (!string.IsNullOrWhiteSpace(query.facet)) route.Append("&facet=").Append(Uri.EscapeDataString(query.facet));
            if (!string.IsNullOrWhiteSpace(query.tag)) route.Append("&tag=").Append(Uri.EscapeDataString(query.tag));
            if (!string.IsNullOrWhiteSpace(query.reviewStatus)) route.Append("&reviewStatus=").Append(Uri.EscapeDataString(query.reviewStatus));
            if (!string.IsNullOrWhiteSpace(query.cursor)) route.Append("&cursor=").Append(Uri.EscapeDataString(query.cursor));
            return SendAsync(HttpMethod.Get, route.ToString(), accessToken, null, null);
        }

        public Task<AssetCatalogApiResponse> GetAssetAsync(string accessToken, AssetCatalogIdentity identity)
        {
            AssetCatalogIdentity normalized = AssetCatalogContracts.CreateIdentity(identity?.projectId, identity?.guid, identity?.subAssetKey ?? 0);
            return SendAsync(HttpMethod.Get, AssetRoute(normalized), accessToken, null, null);
        }

        public Task<AssetCatalogApiResponse> GetChangesAsync(string accessToken, long after, int pageSize = AssetCatalogContracts.DefaultPageSize)
        {
            if (after < 0 || pageSize < 1 || pageSize > AssetCatalogContracts.MaxPageSize) throw new ArgumentOutOfRangeException(nameof(after));
            return SendAsync(HttpMethod.Get, "v1/projects/" + Uri.EscapeDataString(ProjectId) + "/changes?after=" + after + "&pageSize=" + pageSize, accessToken, null, null);
        }

        public Task<AssetCatalogApiResponse> UpsertScanAsync(string accessToken, AssetCatalogScanRequest request, string idempotencyKey)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            foreach (AssetCatalogScanItem item in request.items ?? Array.Empty<AssetCatalogScanItem>()) ValidateScanItem(item);
            AssetCatalogContracts.ValidateSourceRevision(request.sourceRevision);
            return SendAsync(HttpMethod.Post, "v1/projects/" + Uri.EscapeDataString(ProjectId) + "/assets:upsert-scan", accessToken, JsonUtility.ToJson(request), idempotencyKey);
        }

        public Task<AssetCatalogApiResponse> CreateProposalAsync(string accessToken, AssetCatalogIdentity identity, AssetCatalogProposalInput proposal, string idempotencyKey)
        {
            AssetCatalogIdentity normalized = AssetCatalogContracts.CreateIdentity(identity?.projectId, identity?.guid, identity?.subAssetKey ?? 0);
            if (!string.Equals(normalized.projectId, ProjectId, StringComparison.Ordinal)) throw new ArgumentException("identity projectId does not match this client.", nameof(identity));
            return SendAsync(HttpMethod.Post, AssetRoute(normalized) + "/proposals", accessToken, JsonUtility.ToJson(proposal), idempotencyKey);
        }

        public Task<AssetCatalogApiResponse> ApproveProposalAsync(string accessToken, AssetCatalogIdentity identity, string revisionId, string ifMatch, string idempotencyKey)
        {
            AssetCatalogIdentity normalized = AssetCatalogContracts.CreateIdentity(identity?.projectId, identity?.guid, identity?.subAssetKey ?? 0);
            if (string.IsNullOrWhiteSpace(revisionId)) throw new ArgumentException("revisionId is required.", nameof(revisionId));
            return SendAsync(HttpMethod.Post, AssetRoute(normalized) + "/proposals/" + Uri.EscapeDataString(revisionId) + ":approve", accessToken, "{}", idempotencyKey, ifMatch);
        }

        public Task<AssetCatalogApiResponse> UpdateProposalAsync(string accessToken, AssetCatalogIdentity identity, string revisionId, AssetCatalogProposalInput proposal, string ifMatch, string idempotencyKey)
        {
            AssetCatalogIdentity normalized = AssetCatalogContracts.CreateIdentity(identity?.projectId, identity?.guid, identity?.subAssetKey ?? 0);
            if (string.IsNullOrWhiteSpace(revisionId) || proposal == null) throw new ArgumentException("revisionId and proposal are required.");
            return SendAsync(HttpMethod.Put, AssetRoute(normalized) + "/proposals/" + Uri.EscapeDataString(revisionId), accessToken, JsonUtility.ToJson(proposal), idempotencyKey, ifMatch);
        }

        public Task<AssetCatalogApiResponse> RejectProposalAsync(string accessToken, AssetCatalogIdentity identity, string revisionId, string ifMatch, string idempotencyKey)
        {
            AssetCatalogIdentity normalized = AssetCatalogContracts.CreateIdentity(identity?.projectId, identity?.guid, identity?.subAssetKey ?? 0);
            if (string.IsNullOrWhiteSpace(revisionId)) throw new ArgumentException("revisionId is required.", nameof(revisionId));
            return SendAsync(HttpMethod.Post, AssetRoute(normalized) + "/proposals/" + Uri.EscapeDataString(revisionId) + ":reject", accessToken, "{}", idempotencyKey, ifMatch);
        }

        public Task<AssetCatalogApiResponse> RollbackAsync(string accessToken, AssetCatalogIdentity identity, string revisionId, string ifMatch, string idempotencyKey)
        {
            AssetCatalogIdentity normalized = AssetCatalogContracts.CreateIdentity(identity?.projectId, identity?.guid, identity?.subAssetKey ?? 0);
            if (string.IsNullOrWhiteSpace(revisionId)) throw new ArgumentException("revisionId is required.", nameof(revisionId));
            return SendAsync(HttpMethod.Post, AssetRoute(normalized) + ":rollback", accessToken, JsonUtility.ToJson(new RevisionReference { revisionId = revisionId }), idempotencyKey, ifMatch);
        }

        public void Dispose() => _client.Dispose();

        private async Task<AssetCatalogApiResponse> SendAsync(HttpMethod method, string relativeRoute, string accessToken, string json, string idempotencyKey, string ifMatch = null)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, new Uri(_endpoint, relativeRoute)))
            {
                if (!string.IsNullOrWhiteSpace(accessToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                if (!string.IsNullOrWhiteSpace(idempotencyKey)) request.Headers.Add("Idempotency-Key", idempotencyKey);
                if (!string.IsNullOrWhiteSpace(ifMatch)) request.Headers.TryAddWithoutValidation("If-Match", ifMatch.StartsWith("\"") ? ifMatch : "\"" + ifMatch + "\"");
                if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await _client.SendAsync(request))
                {
                    string body = await response.Content.ReadAsStringAsync();
                    AssetCatalogApiMetadata metadata = null;
                    try
                    {
                        metadata = JsonUtility.FromJson<AssetCatalogApiMetadata>(body);
                    }
                    catch (ArgumentException)
                    {
                        if (response.IsSuccessStatusCode) throw new InvalidOperationException("Catalog response could not be parsed.");
                    }
                    if (response.IsSuccessStatusCode && (metadata == null || metadata.apiMajor != AssetCatalogContracts.ApiMajor))
                        throw new InvalidOperationException("Catalog API major is incompatible with this client.");
                    return new AssetCatalogApiResponse
                    {
                        StatusCode = (int)response.StatusCode,
                        Etag = response.Headers.ETag?.Tag,
                        Body = body,
                        Metadata = metadata
                    };
                }
            }
        }

        private string AssetRoute(AssetCatalogIdentity identity)
        {
            if (!string.Equals(identity.projectId, ProjectId, StringComparison.Ordinal)) throw new ArgumentException("identity projectId does not match this client.", nameof(identity));
            return "v1/projects/" + Uri.EscapeDataString(ProjectId) + "/assets/" + identity.guid + "/" + identity.subAssetKey;
        }

        private static void ValidateScanItem(AssetCatalogScanItem item)
        {
            if (item == null) throw new ArgumentException("scan items cannot be null.", nameof(item));
            AssetCatalogContracts.NormalizeGuid(item.guid);
            if (item.subAssetKey < 0) throw new ArgumentOutOfRangeException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.path) || System.IO.Path.IsPathRooted(item.path) || !item.path.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("scan item path must be a Unity-relative Assets/ path.", nameof(item));
            if (string.IsNullOrWhiteSpace(item.dependencyHash)) throw new ArgumentException("scan item dependencyHash is required.", nameof(item));
        }

        [Serializable]
        private sealed class RevisionReference
        {
            public string revisionId;
        }
    }
}
