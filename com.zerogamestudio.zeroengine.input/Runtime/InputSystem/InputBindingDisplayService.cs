using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem
{
    public static class InputBindingDisplayService
    {
        public static InputBindingDisplayResult GetDisplayName(InputActionAsset asset, InputBindingKey key)
        {
            var binding = InputActionLookup.FindBinding(asset, key);
            if (!binding.Success)
            {
                return InputBindingDisplayResult.Missing(binding.Diagnostic);
            }

            var displayName = binding.Action.GetBindingDisplayString(
                binding.BindingIndex,
                out _,
                out _,
                InputBinding.DisplayStringOptions.DontIncludeInteractions);

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = binding.EffectivePath;
            }

            return InputBindingDisplayResult.Found(displayName, binding.EffectivePath);
        }
    }
}
