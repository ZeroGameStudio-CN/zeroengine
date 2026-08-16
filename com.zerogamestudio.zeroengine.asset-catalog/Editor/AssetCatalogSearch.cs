using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.AssetCatalog
{
    public static class AssetCatalogSearch
    {
        private const int ReciprocalRankConstant = 60;

        public static List<AssetCatalogSearchResult> SearchLocal(AssetCatalogSnapshot snapshot, AssetCatalogTaxonomy taxonomy, AssetCatalogSearchQuery query, int limit = 200)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (query == null) query = new AssetCatalogSearchQuery();
            if (limit < 1 || limit > AssetCatalogContracts.MaxPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
            string[] terms = SplitTerms(query.text);
            List<ScoredRecord> matches = new List<ScoredRecord>();
            foreach (AssetCatalogSnapshotRecord item in snapshot.records ?? Array.Empty<AssetCatalogSnapshotRecord>())
            {
                if (item?.record == null || !MatchesFilters(item, query)) continue;
                int score = TextScore(item, taxonomy, terms);
                if (terms.Length > 0 && score == 0) continue;
                matches.Add(new ScoredRecord { item = item, score = score });
            }
            return matches
                .OrderByDescending(item => item.score)
                .ThenBy(item => item.item.record.path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.item.record.identity.StableKey, StringComparer.Ordinal)
                .Take(limit)
                .Select((item, index) => new AssetCatalogSearchResult
                {
                    record = item.item.record,
                    approvedRevision = item.item.approvedRevision,
                    fullTextRank = item.score > 0 ? index + 1 : 0,
                    semanticRank = 0,
                    fusedRank = index + 1,
                    semanticPending = false
                })
                .ToList();
        }

        public static Dictionary<string, double> FuseRankings(IEnumerable<string> fullTextIdentityKeys, IEnumerable<string> semanticIdentityKeys, IEnumerable<string> exactTagIdentityKeys)
        {
            Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.Ordinal);
            AddRanking(result, fullTextIdentityKeys, 1d);
            AddRanking(result, semanticIdentityKeys, 1d);
            AddRanking(result, exactTagIdentityKeys, 2d);
            return result;
        }

        private static void AddRanking(IDictionary<string, double> scores, IEnumerable<string> identities, double weight)
        {
            int rank = 1;
            foreach (string identity in identities ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(identity))
                {
                    scores.TryGetValue(identity, out double existing);
                    scores[identity] = existing + weight / (ReciprocalRankConstant + rank);
                }
                rank++;
            }
        }

        private static bool MatchesFilters(AssetCatalogSnapshotRecord item, AssetCatalogSearchQuery query)
        {
            AssetCatalogRecord record = item.record;
            if (!string.IsNullOrWhiteSpace(query.assetType) && !string.Equals(record.assetType, query.assetType, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrWhiteSpace(query.facet) && !(record.facets ?? Array.Empty<string>()).Contains(query.facet, StringComparer.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(query.reviewStatus) && !string.Equals(record.reviewStatus, query.reviewStatus, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrWhiteSpace(query.tag))
            {
                string[] tags = item.approvedRevision == null ? Array.Empty<string>() : item.approvedRevision.controlledTags.Concat(item.approvedRevision.freeTags).ToArray();
                if (!tags.Contains(query.tag, StringComparer.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static int TextScore(AssetCatalogSnapshotRecord item, AssetCatalogTaxonomy taxonomy, IEnumerable<string> terms)
        {
            string[] allTerms = terms.ToArray();
            if (allTerms.Length == 0) return 1;
            AssetCatalogSemanticRevision revision = item.approvedRevision;
            string path = item.record.path ?? string.Empty;
            string descriptionZh = revision?.descriptionZh ?? string.Empty;
            string descriptionEn = revision?.descriptionEn ?? string.Empty;
            string[] tags = revision == null ? Array.Empty<string>() : revision.controlledTags.Concat(revision.freeTags).ToArray();
            Dictionary<string, AssetCatalogTagDefinition> taxonomyTags = (taxonomy?.tagDefinitions ?? Array.Empty<AssetCatalogTagDefinition>())
                .Where(tag => tag != null && !string.IsNullOrWhiteSpace(tag.tagId))
                .GroupBy(tag => tag.tagId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            int score = 0;
            foreach (string term in allTerms)
            {
                if (tags.Any(tag => string.Equals(tag, term, StringComparison.OrdinalIgnoreCase))) score += 20;
                if (Contains(descriptionZh, term) || Contains(descriptionEn, term)) score += 8;
                if (Contains(path, term)) score += 1;
                foreach (string tag in tags)
                {
                    if (!taxonomyTags.TryGetValue(tag, out AssetCatalogTagDefinition definition)) continue;
                    if (Contains(definition.nameZh, term) || Contains(definition.nameEn, term) || (definition.aliases ?? Array.Empty<string>()).Any(alias => Contains(alias, term))) score += 12;
                }
            }
            return score;
        }

        private static string[] SplitTerms(string query)
        {
            return (query ?? string.Empty).Trim()
                .Split(new[] { ' ', '\t', '\r', '\n', ',', '，', '/', '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool Contains(string text, string term)
        {
            return !string.IsNullOrWhiteSpace(term) && (text ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class ScoredRecord
        {
            public AssetCatalogSnapshotRecord item;
            public int score;
        }
    }
}
