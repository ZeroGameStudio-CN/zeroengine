using System;

namespace ZeroEngine.InputSystem
{
    public readonly struct InputActionKey
    {
        public InputActionKey(string actionMapName, string actionName)
        {
            ActionMapName = actionMapName?.Trim() ?? string.Empty;
            ActionName = actionName?.Trim() ?? string.Empty;
        }

        public string ActionMapName { get; }
        public string ActionName { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(ActionMapName) && !string.IsNullOrWhiteSpace(ActionName);

        public override string ToString()
        {
            return $"{ActionMapName}/{ActionName}";
        }
    }

    public readonly struct InputBindingKey
    {
        public InputBindingKey(string actionMapName, string actionName, string bindingGroup)
        {
            ActionMapName = actionMapName?.Trim() ?? string.Empty;
            ActionName = actionName?.Trim() ?? string.Empty;
            BindingGroup = bindingGroup?.Trim() ?? string.Empty;
        }

        public string ActionMapName { get; }
        public string ActionName { get; }
        public string BindingGroup { get; }
        public InputActionKey ActionKey => new(ActionMapName, ActionName);
        public bool IsValid => ActionKey.IsValid && !string.IsNullOrWhiteSpace(BindingGroup);

        public override string ToString()
        {
            return $"{ActionMapName}/{ActionName} [{BindingGroup}]";
        }
    }

    public readonly struct InputActionLookupResult
    {
        private InputActionLookupResult(bool success, UnityEngine.InputSystem.InputAction action, string diagnostic)
        {
            Success = success;
            Action = action;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public UnityEngine.InputSystem.InputAction Action { get; }
        public string Diagnostic { get; }

        public static InputActionLookupResult Found(UnityEngine.InputSystem.InputAction action)
        {
            return new InputActionLookupResult(true, action, string.Empty);
        }

        public static InputActionLookupResult Missing(string diagnostic)
        {
            return new InputActionLookupResult(false, null, diagnostic);
        }
    }

    public readonly struct InputBindingLookupResult
    {
        private InputBindingLookupResult(
            bool success,
            UnityEngine.InputSystem.InputAction action,
            int bindingIndex,
            string diagnostic)
        {
            Success = success;
            Action = action;
            BindingIndex = bindingIndex;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public UnityEngine.InputSystem.InputAction Action { get; }
        public int BindingIndex { get; }
        public string Diagnostic { get; }

        public UnityEngine.InputSystem.InputBinding Binding =>
            Success ? Action.bindings[BindingIndex] : default;

        public string EffectivePath
        {
            get
            {
                if (!Success)
                {
                    return string.Empty;
                }

                var binding = Binding;
                return !string.IsNullOrWhiteSpace(binding.overridePath)
                    ? binding.overridePath
                    : binding.path ?? string.Empty;
            }
        }

        public static InputBindingLookupResult Found(UnityEngine.InputSystem.InputAction action, int bindingIndex)
        {
            return new InputBindingLookupResult(true, action, bindingIndex, string.Empty);
        }

        public static InputBindingLookupResult Missing(string diagnostic)
        {
            return new InputBindingLookupResult(false, null, -1, diagnostic);
        }
    }

    public readonly struct InputBindingChangeResult
    {
        private InputBindingChangeResult(bool success, int bindingIndex, string diagnostic)
        {
            Success = success;
            BindingIndex = bindingIndex;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public int BindingIndex { get; }
        public string Diagnostic { get; }

        public static InputBindingChangeResult Changed(int bindingIndex)
        {
            return new InputBindingChangeResult(true, bindingIndex, string.Empty);
        }

        public static InputBindingChangeResult Failed(string diagnostic)
        {
            return new InputBindingChangeResult(false, -1, diagnostic);
        }
    }

    public readonly struct InputBindingDisplayResult
    {
        private InputBindingDisplayResult(
            bool success,
            string displayName,
            string effectivePath,
            string diagnostic)
        {
            Success = success;
            DisplayName = displayName ?? string.Empty;
            EffectivePath = effectivePath ?? string.Empty;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public string DisplayName { get; }
        public string EffectivePath { get; }
        public string Diagnostic { get; }

        public static InputBindingDisplayResult Found(string displayName, string effectivePath)
        {
            return new InputBindingDisplayResult(true, displayName, effectivePath, string.Empty);
        }

        public static InputBindingDisplayResult Missing(string diagnostic)
        {
            return new InputBindingDisplayResult(false, string.Empty, string.Empty, diagnostic);
        }
    }
}
