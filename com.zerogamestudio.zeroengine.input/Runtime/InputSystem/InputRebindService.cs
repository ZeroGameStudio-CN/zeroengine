using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem
{
    public readonly struct InputRebindOptions
    {
        public InputRebindOptions(string cancelControlPath, IReadOnlyList<string> excludedControlPaths)
        {
            CancelControlPath = cancelControlPath ?? string.Empty;
            ExcludedControlPaths = excludedControlPaths ?? Array.Empty<string>();
        }

        public string CancelControlPath { get; }
        public IReadOnlyList<string> ExcludedControlPaths { get; }

        public static InputRebindOptions Default { get; } =
            new("<Keyboard>/escape", Array.Empty<string>());
    }

    public sealed class InputRebindResult : IDisposable
    {
        private readonly Action _disposeAction;

        private InputRebindResult(
            bool success,
            InputActionRebindingExtensions.RebindingOperation operation,
            string diagnostic,
            Action disposeAction)
        {
            Success = success;
            Operation = operation;
            Diagnostic = diagnostic ?? string.Empty;
            _disposeAction = disposeAction;
        }

        public bool Success { get; }
        public InputActionRebindingExtensions.RebindingOperation Operation { get; }
        public string Diagnostic { get; }
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            Operation?.Dispose();
            _disposeAction?.Invoke();
            IsDisposed = true;
        }

        public static InputRebindResult Started(
            InputActionRebindingExtensions.RebindingOperation operation,
            Action disposeAction)
        {
            return new InputRebindResult(true, operation, string.Empty, disposeAction);
        }

        public static InputRebindResult Failed(string diagnostic)
        {
            return new InputRebindResult(false, null, diagnostic, null);
        }
    }

    public static class InputRebindService
    {
        public static InputRebindResult Start(
            InputActionAsset asset,
            InputBindingKey key,
            InputRebindOptions options)
        {
            var binding = InputActionLookup.FindBinding(asset, key);
            if (!binding.Success)
            {
                return InputRebindResult.Failed(binding.Diagnostic);
            }

            var action = binding.Action;
            var wasEnabled = action.enabled;
            if (wasEnabled)
            {
                action.Disable();
            }

            try
            {
                var operation = action.PerformInteractiveRebinding(binding.BindingIndex);
                if (!string.IsNullOrWhiteSpace(options.CancelControlPath))
                {
                    operation.WithCancelingThrough(options.CancelControlPath);
                }

                for (var i = 0; i < options.ExcludedControlPaths.Count; i++)
                {
                    var excluded = options.ExcludedControlPaths[i];
                    if (!string.IsNullOrWhiteSpace(excluded))
                    {
                        operation.WithControlsExcluding(excluded);
                    }
                }

                operation.Start();
                return InputRebindResult.Started(
                    operation,
                    () =>
                    {
                        if (wasEnabled)
                        {
                            action.Enable();
                        }
                    });
            }
            catch (Exception ex)
            {
                if (wasEnabled)
                {
                    action.Enable();
                }

                return InputRebindResult.Failed(ex.Message);
            }
        }
    }
}
