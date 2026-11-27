using System;
using System.Collections.Generic;

namespace ZeroEngine.Builds
{
    public readonly struct BuildTagSource
    {
        public BuildTagSource(string id, IReadOnlyList<string> tags)
        {
            Id = id;
            Tags = tags;
        }

        public string Id { get; }
        public IReadOnlyList<string> Tags { get; }
    }

    /// <summary>Copies the tags on committed choices for one reward request.</summary>
    public sealed class BuildTagSnapshot
    {
        public static BuildTagSnapshot Empty { get; } = new(Array.Empty<BuildTagSource>());

        private readonly Dictionary<string, HashSet<string>> tagsBySource = new(StringComparer.Ordinal);

        public BuildTagSnapshot(IEnumerable<BuildTagSource> sources)
        {
            if (sources == null) return;

            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source.Id) || source.Tags == null || source.Tags.Count == 0)
                    continue;

                if (!tagsBySource.TryGetValue(source.Id, out var tags))
                    tagsBySource.Add(source.Id, tags = new HashSet<string>(BuildTagCatalog.Comparer));

                AddTags(tags, source.Tags);
            }
        }

        /// <summary>Each distinct matching source adds one bonus, up to the multiplier cap.</summary>
        public float GetMultiplier(IReadOnlyList<string> candidateTags, float bonusPerSource = 0.5f,
            float maximumMultiplier = 2f)
        {
            if (tagsBySource.Count == 0 || candidateTags == null || candidateTags.Count == 0 ||
                !WeightedSelection.IsFinite(bonusPerSource) || bonusPerSource <= 0 ||
                !WeightedSelection.IsFinite(maximumMultiplier) || maximumMultiplier <= 1)
                return 1f;

            var tags = new HashSet<string>(BuildTagCatalog.Comparer);
            AddTags(tags, candidateTags);
            double multiplier = 1d;
            foreach (var source in tagsBySource.Values)
            {
                if (!source.Overlaps(tags)) continue;
                multiplier += bonusPerSource;
                if (multiplier >= maximumMultiplier)
                    return maximumMultiplier;
            }

            return (float)multiplier;
        }

        private static void AddTags(HashSet<string> set, IReadOnlyList<string> tags)
        {
            foreach (string value in tags)
            {
                string tag = BuildTagCatalog.Normalize(value);
                if (tag != null) set.Add(tag);
            }
        }
    }
}
