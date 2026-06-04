using System;

namespace ZeroEngine.InputSystem
{
    public readonly struct InputControlSchemeResolveResult
    {
        private InputControlSchemeResolveResult(bool success, string bindingGroup, string diagnostic)
        {
            Success = success;
            BindingGroup = bindingGroup ?? string.Empty;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public string BindingGroup { get; }
        public string Diagnostic { get; }

        public static InputControlSchemeResolveResult Resolved(string bindingGroup)
        {
            return new InputControlSchemeResolveResult(true, bindingGroup, string.Empty);
        }

        public static InputControlSchemeResolveResult Failed(string diagnostic)
        {
            return new InputControlSchemeResolveResult(false, string.Empty, diagnostic);
        }
    }

    public static class InputControlSchemeResolver
    {
        public const string KeyboardMouseBindingGroup = "Keyboard&Mouse";
        public const string GamepadBindingGroup = "Gamepad";

        public static InputControlSchemeResolveResult ResolveBindingGroup(string controlPath)
        {
            if (string.IsNullOrWhiteSpace(controlPath))
            {
                return InputControlSchemeResolveResult.Failed("Control path is empty.");
            }

            var normalized = controlPath.Trim();
            if (StartsWithDevice(normalized, "Gamepad"))
            {
                return InputControlSchemeResolveResult.Resolved(GamepadBindingGroup);
            }

            if (StartsWithDevice(normalized, "Keyboard") || StartsWithDevice(normalized, "Mouse"))
            {
                return InputControlSchemeResolveResult.Resolved(KeyboardMouseBindingGroup);
            }

            return InputControlSchemeResolveResult.Failed(
                $"Control path '{controlPath}' does not map to a supported binding group.");
        }

        private static bool StartsWithDevice(string controlPath, string deviceName)
        {
            return controlPath.StartsWith($"<{deviceName}>", StringComparison.OrdinalIgnoreCase);
        }
    }
}
