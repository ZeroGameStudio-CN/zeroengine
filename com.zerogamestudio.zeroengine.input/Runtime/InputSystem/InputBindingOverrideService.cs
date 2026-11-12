using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem
{
    public static class InputBindingOverrideService
    {
        public static InputBindingChangeResult ApplyOverride(
            InputActionAsset asset,
            InputBindingKey key,
            string controlPath)
        {
            if (string.IsNullOrWhiteSpace(controlPath))
            {
                return InputBindingChangeResult.Failed("Control path is empty.");
            }

            var binding = InputActionLookup.FindBinding(asset, key);
            if (!binding.Success)
            {
                return InputBindingChangeResult.Failed(binding.Diagnostic);
            }

            binding.Action.ApplyBindingOverride(binding.BindingIndex, controlPath.Trim());
            return InputBindingChangeResult.Changed(binding.BindingIndex);
        }

        public static InputBindingChangeResult ResetBinding(InputActionAsset asset, InputBindingKey key)
        {
            var binding = InputActionLookup.FindBinding(asset, key);
            if (!binding.Success)
            {
                return InputBindingChangeResult.Failed(binding.Diagnostic);
            }

            binding.Action.RemoveBindingOverride(binding.BindingIndex);
            return InputBindingChangeResult.Changed(binding.BindingIndex);
        }

        public static void ResetAll(InputActionAsset asset)
        {
            asset?.RemoveAllBindingOverrides();
        }

        public static string SaveOverridesAsJson(InputActionAsset asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            var overrides = new BindingOverrideRecord[CountOverrides(asset)];
            var overrideIndex = 0;
            foreach (var map in asset.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    for (var i = 0; i < action.bindings.Count; i++)
                    {
                        var binding = action.bindings[i];
                        var overridePath = GetOverridePath(binding);
                        if (string.IsNullOrWhiteSpace(overridePath)
                            || string.IsNullOrWhiteSpace(binding.groups))
                        {
                            continue;
                        }

                        overrides[overrideIndex] = new BindingOverrideRecord
                        {
                            actionMapName = map.name,
                            actionName = action.name,
                            bindingGroup = binding.groups,
                            overridePath = overridePath
                        };
                        overrideIndex++;
                    }
                }
            }

            var set = new BindingOverrideSet { overrides = overrides };
            return JsonUtility.ToJson(set);
        }

        public static void LoadOverridesFromJson(InputActionAsset asset, string json)
        {
            if (asset == null || string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var set = JsonUtility.FromJson<BindingOverrideSet>(json);
            if (set?.overrides == null)
            {
                return;
            }

            for (var i = 0; i < set.overrides.Length; i++)
            {
                var record = set.overrides[i];
                if (string.IsNullOrWhiteSpace(record.overridePath))
                {
                    continue;
                }

                ApplyOverride(
                    asset,
                    new InputBindingKey(record.actionMapName, record.actionName, record.bindingGroup),
                    record.overridePath);
            }
        }

        [Serializable]
        private sealed class BindingOverrideSet
        {
            public BindingOverrideRecord[] overrides = Array.Empty<BindingOverrideRecord>();
        }

        [Serializable]
        private sealed class BindingOverrideRecord
        {
            public string actionMapName;
            public string actionName;
            public string bindingGroup;
            public string overridePath;
        }

        private static int CountOverrides(InputActionAsset asset)
        {
            var count = 0;
            foreach (var map in asset.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    for (var i = 0; i < action.bindings.Count; i++)
                    {
                        var binding = action.bindings[i];
                        if (!string.IsNullOrWhiteSpace(GetOverridePath(binding))
                            && !string.IsNullOrWhiteSpace(binding.groups))
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        private static string GetOverridePath(InputBinding binding)
        {
            if (!string.IsNullOrWhiteSpace(binding.overridePath))
            {
                return binding.overridePath;
            }

            if (!binding.hasOverrides)
            {
                return string.Empty;
            }

            return string.Equals(binding.effectivePath, binding.path, StringComparison.Ordinal)
                ? string.Empty
                : binding.effectivePath;
        }
    }
}
