using System;
using System.Collections.Generic;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaPreviewValue
    {
        public FormulaPreviewValue(string key, float value)
        {
            Key = key ?? string.Empty;
            Value = value;
        }

        public string Key { get; }
        public float Value { get; }
    }

    public sealed class FormulaPreviewValueSet
    {
        private readonly IReadOnlyList<FormulaPreviewValue> values;

        public FormulaPreviewValueSet(IEnumerable<FormulaPreviewValue> values)
        {
            this.values = values == null
                ? Array.Empty<FormulaPreviewValue>()
                : new List<FormulaPreviewValue>(values).AsReadOnly();
        }

        public IReadOnlyList<FormulaPreviewValue> Values => values;

        public bool TryGetValue(string key, out float value)
        {
            foreach (var entry in values)
            {
                if (entry == null || entry.Key != key)
                    continue;

                value = entry.Value;
                return true;
            }

            value = 0f;
            return false;
        }

        public IReadOnlyDictionary<string, float> ToDictionary()
        {
            var dictionary = new Dictionary<string, float>();
            foreach (var entry in values)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.Key))
                    dictionary[entry.Key] = entry.Value;
            }

            return dictionary;
        }
    }

    public sealed class FormulaPreviewCase
    {
        public FormulaPreviewCase(
            string id,
            string displayName,
            FormulaPreviewValueSet values,
            string description)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Values = values ?? new FormulaPreviewValueSet(null);
            Description = description ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public FormulaPreviewValueSet Values { get; }
        public string Description { get; }
    }

    public sealed class FormulaRuntimeSnapshot
    {
        public FormulaRuntimeSnapshot(
            string profileId,
            string sourceLabel,
            string capturedAtUtc,
            FormulaPreviewValueSet values)
        {
            ProfileId = profileId ?? string.Empty;
            SourceLabel = sourceLabel ?? string.Empty;
            CapturedAtUtc = capturedAtUtc ?? string.Empty;
            Values = values ?? new FormulaPreviewValueSet(null);
        }

        public string ProfileId { get; }
        public string SourceLabel { get; }
        public string CapturedAtUtc { get; }
        public FormulaPreviewValueSet Values { get; }
    }
}
