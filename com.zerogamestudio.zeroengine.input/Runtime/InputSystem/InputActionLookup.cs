using System;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem
{
    public static class InputActionLookup
    {
        public static InputActionLookupResult FindAction(InputActionAsset asset, InputActionKey key)
        {
            if (asset == null)
            {
                return InputActionLookupResult.Missing("InputActionAsset is null.");
            }

            if (!key.IsValid)
            {
                return InputActionLookupResult.Missing($"Input action key is invalid: {key}.");
            }

            var map = asset.FindActionMap(key.ActionMapName, throwIfNotFound: false);
            if (map == null)
            {
                return InputActionLookupResult.Missing($"Input action map '{key.ActionMapName}' was not found.");
            }

            var action = map.FindAction(key.ActionName, throwIfNotFound: false);
            if (action == null)
            {
                return InputActionLookupResult.Missing(
                    $"Input action '{key.ActionName}' was not found in map '{key.ActionMapName}'.");
            }

            return InputActionLookupResult.Found(action);
        }

        public static InputBindingLookupResult FindBinding(InputActionAsset asset, InputBindingKey key)
        {
            if (!key.IsValid)
            {
                return InputBindingLookupResult.Missing($"Input binding key is invalid: {key}.");
            }

            var actionResult = FindAction(asset, key.ActionKey);
            if (!actionResult.Success)
            {
                return InputBindingLookupResult.Missing(actionResult.Diagnostic);
            }

            var action = actionResult.Action;
            for (var i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (binding.isComposite)
                {
                    continue;
                }

                if (MatchesGroup(binding, key.BindingGroup))
                {
                    return InputBindingLookupResult.Found(action, i);
                }
            }

            return InputBindingLookupResult.Missing(
                $"Input binding group '{key.BindingGroup}' was not found on action '{key.ActionKey}'.");
        }

        internal static bool MatchesGroup(InputBinding binding, string bindingGroup)
        {
            if (string.IsNullOrWhiteSpace(bindingGroup))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(binding.groups))
            {
                return false;
            }

            var groups = binding.groups.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < groups.Length; i++)
            {
                if (string.Equals(groups[i].Trim(), bindingGroup, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
