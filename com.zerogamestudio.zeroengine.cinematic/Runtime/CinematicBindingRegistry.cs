using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Cinematic
{
    public sealed class CinematicBindingRegistry
    {
        private readonly Dictionary<string, List<Object>> _bindings = new();

        public void Register(string bindingKey, Object binding)
        {
            var normalizedBindingKey = NormalizeBindingKey(bindingKey);
            if (string.IsNullOrEmpty(normalizedBindingKey) || binding == null)
            {
                return;
            }

            if (!_bindings.TryGetValue(normalizedBindingKey, out var entries))
            {
                entries = new List<Object>();
                _bindings.Add(normalizedBindingKey, entries);
            }

            entries.Add(binding);
        }

        public bool TryResolve(string bindingKey, out Object binding)
        {
            var normalizedBindingKey = NormalizeBindingKey(bindingKey);
            if (!string.IsNullOrEmpty(normalizedBindingKey) &&
                _bindings.TryGetValue(normalizedBindingKey, out var entries))
            {
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var candidate = entries[i];
                    if (candidate != null)
                    {
                        binding = candidate;
                        return true;
                    }
                }
            }

            binding = null;
            return false;
        }

        public bool Unregister(string bindingKey, Object binding)
        {
            var normalizedBindingKey = NormalizeBindingKey(bindingKey);
            if (string.IsNullOrEmpty(normalizedBindingKey) ||
                binding == null ||
                !_bindings.TryGetValue(normalizedBindingKey, out var entries))
            {
                return false;
            }

            var removed = entries.Remove(binding);
            if (entries.Count == 0)
            {
                _bindings.Remove(normalizedBindingKey);
            }

            return removed;
        }

        public IReadOnlyList<CinematicValidationIssue> Validate()
        {
            var issues = new List<CinematicValidationIssue>();

            foreach (var pair in _bindings)
            {
                if (!CinematicStableId.IsValid(pair.Key))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.InvalidStableId,
                        pair.Key,
                        $"Cinematic binding key '{pair.Key}' is not a valid stable id."));
                }

                var liveCount = 0;
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    if (pair.Value[i] != null)
                    {
                        liveCount++;
                    }
                }

                if (liveCount > 1)
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.DuplicateBindingKey,
                        pair.Key,
                        $"Cinematic binding key '{pair.Key}' has {liveCount} live bindings."));
                }
            }

            return issues;
        }

        private static string NormalizeBindingKey(string bindingKey)
        {
            return string.IsNullOrWhiteSpace(bindingKey)
                ? string.Empty
                : bindingKey.Trim();
        }
    }
}
