using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem
{
    public enum InputBindingConflictSeverity
    {
        Warning,
        Blocking
    }

    public readonly struct InputBindingConflictDescriptor
    {
        public InputBindingConflictDescriptor(InputBindingKey bindingKey, string conflictScope)
        {
            BindingKey = bindingKey;
            ConflictScope = string.IsNullOrWhiteSpace(conflictScope) ? "default" : conflictScope.Trim();
        }

        public InputBindingKey BindingKey { get; }
        public string ConflictScope { get; }
    }

    public readonly struct InputBindingConflict
    {
        public InputBindingConflict(
            InputBindingConflictDescriptor first,
            InputBindingConflictDescriptor second,
            string effectivePath,
            InputBindingConflictSeverity severity)
        {
            First = first;
            Second = second;
            EffectivePath = effectivePath ?? string.Empty;
            Severity = severity;
        }

        public InputBindingConflictDescriptor First { get; }
        public InputBindingConflictDescriptor Second { get; }
        public string EffectivePath { get; }
        public InputBindingConflictSeverity Severity { get; }
    }

    public static class InputBindingConflictValidator
    {
        public static IReadOnlyList<InputBindingConflict> Validate(
            InputActionAsset asset,
            IReadOnlyList<InputBindingConflictDescriptor> descriptors)
        {
            var conflicts = new List<InputBindingConflict>();
            if (asset == null || descriptors == null || descriptors.Count < 2)
            {
                return conflicts;
            }

            var resolved = new List<ResolvedBinding>(descriptors.Count);
            for (var i = 0; i < descriptors.Count; i++)
            {
                var binding = InputActionLookup.FindBinding(asset, descriptors[i].BindingKey);
                if (!binding.Success || string.IsNullOrWhiteSpace(binding.EffectivePath))
                {
                    continue;
                }

                resolved.Add(new ResolvedBinding(descriptors[i], NormalizePath(binding.EffectivePath)));
            }

            for (var i = 0; i < resolved.Count; i++)
            {
                for (var j = i + 1; j < resolved.Count; j++)
                {
                    if (!string.Equals(resolved[i].EffectivePath, resolved[j].EffectivePath, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.Equals(
                            resolved[i].Descriptor.BindingKey.BindingGroup,
                            resolved[j].Descriptor.BindingKey.BindingGroup,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.Equals(
                            resolved[i].Descriptor.ConflictScope,
                            resolved[j].Descriptor.ConflictScope,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    conflicts.Add(new InputBindingConflict(
                        resolved[i].Descriptor,
                        resolved[j].Descriptor,
                        resolved[i].EffectivePath,
                        InputBindingConflictSeverity.Blocking));
                }
            }

            return conflicts;
        }

        private static string NormalizePath(string path)
        {
            return path?.Trim() ?? string.Empty;
        }

        private readonly struct ResolvedBinding
        {
            public ResolvedBinding(InputBindingConflictDescriptor descriptor, string effectivePath)
            {
                Descriptor = descriptor;
                EffectivePath = effectivePath;
            }

            public InputBindingConflictDescriptor Descriptor { get; }
            public string EffectivePath { get; }
        }
    }
}
