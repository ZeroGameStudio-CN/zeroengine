using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem
{
    public enum InputBindingConflictPolicy
    {
        Swap,
        Reject,
        Allow
    }

    public enum InputRebindStatus
    {
        Applied,
        Cancelled,
        TimedOut,
        ConflictRejected,
        InvalidAction,
        InvalidBinding,
        IncompatibleControl
    }

    public readonly struct InputRebindResult
    {
        public InputRebindResult(InputRebindStatus status, Guid actionId, Guid bindingId, Guid conflictingBindingId = default)
        {
            Status = status;
            ActionId = actionId;
            BindingId = bindingId;
            ConflictingBindingId = conflictingBindingId;
        }

        public InputRebindStatus Status { get; }
        public Guid ActionId { get; }
        public Guid BindingId { get; }
        public Guid ConflictingBindingId { get; }
        public bool Success => Status == InputRebindStatus.Applied;
    }

    public sealed class InputBindingService
    {
        private readonly InputActionAsset _asset;

        public InputBindingService(InputActionAsset asset)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        }

        public string SaveOverrides() => _asset.SaveBindingOverridesAsJson();

        public string GetBindingDisplayString(Guid actionId, Guid bindingId)
        {
            return TryFindAction(actionId, out var action)
                   && FindBindingIndex(action, bindingId) is var index
                   && index >= 0
                ? action.GetBindingDisplayString(index)
                : string.Empty;
        }

        public string GetEffectivePath(Guid actionId, Guid bindingId)
        {
            return TryFindAction(actionId, out var action)
                   && FindBindingIndex(action, bindingId) is var index
                   && index >= 0
                ? action.bindings[index].effectivePath
                : string.Empty;
        }

        public bool TryLoadOverrides(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                _asset.RemoveAllBindingOverrides();
                return true;
            }

            try
            {
                _asset.LoadBindingOverridesFromJson(json, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public InputRebindResult TryApplyOverride(
            Guid actionId,
            Guid bindingId,
            string controlPath,
            InputBindingConflictPolicy conflictPolicy = InputBindingConflictPolicy.Swap)
        {
            if (!TryFindAction(actionId, out var action))
            {
                return new InputRebindResult(InputRebindStatus.InvalidAction, actionId, bindingId);
            }

            var bindingIndex = FindBindingIndex(action, bindingId);
            if (bindingIndex < 0)
            {
                return new InputRebindResult(InputRebindStatus.InvalidBinding, actionId, bindingId);
            }

            if (!IsBindablePath(controlPath))
            {
                return new InputRebindResult(InputRebindStatus.IncompatibleControl, actionId, bindingId);
            }

            var target = action.bindings[bindingIndex];
            if (!TryFindConflict(action.actionMap, bindingId, target.groups, controlPath, out var conflictAction, out var conflictIndex))
            {
                action.ApplyBindingOverride(bindingIndex, controlPath);
                return new InputRebindResult(InputRebindStatus.Applied, actionId, bindingId);
            }

            var conflict = conflictAction.bindings[conflictIndex];
            if (conflictPolicy == InputBindingConflictPolicy.Reject || !CanSwap(target, conflict))
            {
                return new InputRebindResult(InputRebindStatus.ConflictRejected, actionId, bindingId, conflict.id);
            }

            if (conflictPolicy == InputBindingConflictPolicy.Swap)
            {
                var previousPath = target.effectivePath;
                conflictAction.ApplyBindingOverride(conflictIndex, previousPath);
            }

            action.ApplyBindingOverride(bindingIndex, controlPath);
            return new InputRebindResult(InputRebindStatus.Applied, actionId, bindingId, conflict.id);
        }

        public InputActionRebindingExtensions.RebindingOperation BeginInteractiveRebind(
            Guid actionId,
            Guid bindingId,
            Action<InputRebindResult> completed,
            string expectedControlPath = null,
            float timeoutSeconds = 10f,
            InputBindingConflictPolicy conflictPolicy = InputBindingConflictPolicy.Swap,
            IReadOnlyList<string> excludedControlPaths = null)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            if (!TryFindAction(actionId, out var action))
            {
                completed(new InputRebindResult(InputRebindStatus.InvalidAction, actionId, bindingId));
                return null;
            }

            var bindingIndex = FindBindingIndex(action, bindingId);
            if (bindingIndex < 0)
            {
                completed(new InputRebindResult(InputRebindStatus.InvalidBinding, actionId, bindingId));
                return null;
            }

            var wasEnabled = action.enabled;
            action.Disable();
            var deadline = Time.realtimeSinceStartupAsDouble + Math.Max(0.1f, timeoutSeconds);
            var result = new InputRebindResult(InputRebindStatus.Cancelled, actionId, bindingId);
            var operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithCancelingThrough("<Gamepad>/start")
                .WithTimeout(Math.Max(0.1f, timeoutSeconds))
                .OnApplyBinding((_, path) =>
                {
                    result = TryApplyOverride(actionId, bindingId, path, conflictPolicy);
                })
                .OnCancel(current =>
                {
                    if (wasEnabled)
                    {
                        action.Enable();
                    }

                    var status = Time.realtimeSinceStartupAsDouble >= deadline
                        ? InputRebindStatus.TimedOut
                        : InputRebindStatus.Cancelled;
                    completed(new InputRebindResult(status, actionId, bindingId));
                    current.Dispose();
                })
                .OnComplete(current =>
                {
                    if (wasEnabled)
                    {
                        action.Enable();
                    }

                    completed(result);
                    current.Dispose();
                });
            if (!string.IsNullOrWhiteSpace(expectedControlPath))
            {
                operation.WithControlsHavingToMatchPath(expectedControlPath);
            }

            if (excludedControlPaths != null)
            {
                foreach (var path in excludedControlPaths)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        operation.WithControlsExcluding(path);
                    }
                }
            }

            operation.Start();
            return operation;
        }

        public bool ResetBinding(Guid actionId, Guid bindingId)
        {
            if (!TryFindAction(actionId, out var action))
            {
                return false;
            }

            var index = FindBindingIndex(action, bindingId);
            if (index < 0)
            {
                return false;
            }

            action.RemoveBindingOverride(index);
            return true;
        }

        public void ResetAll() => _asset.RemoveAllBindingOverrides();

        private bool TryFindAction(Guid actionId, out InputAction action)
        {
            foreach (var map in _asset.actionMaps)
            {
                foreach (var candidate in map.actions)
                {
                    if (candidate.id == actionId)
                    {
                        action = candidate;
                        return true;
                    }
                }
            }

            action = null;
            return false;
        }

        private static int FindBindingIndex(InputAction action, Guid bindingId)
        {
            for (var i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].id == bindingId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryFindConflict(
            InputActionMap map,
            Guid ignoredBindingId,
            string bindingGroups,
            string controlPath,
            out InputAction conflictAction,
            out int conflictIndex)
        {
            foreach (var action in map.actions)
            {
                for (var i = 0; i < action.bindings.Count; i++)
                {
                    var candidate = action.bindings[i];
                    if (candidate.id == ignoredBindingId || candidate.isComposite ||
                        !GroupsOverlap(bindingGroups, candidate.groups))
                    {
                        continue;
                    }

                    if (string.Equals(candidate.effectivePath, controlPath, StringComparison.OrdinalIgnoreCase))
                    {
                        conflictAction = action;
                        conflictIndex = i;
                        return true;
                    }
                }
            }

            conflictAction = null;
            conflictIndex = -1;
            return false;
        }

        private static bool GroupsOverlap(string first, string second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
            {
                return true;
            }

            var groups = new HashSet<string>(first.Split(';'), StringComparer.OrdinalIgnoreCase);
            foreach (var group in second.Split(';'))
            {
                if (groups.Contains(group))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanSwap(InputBinding target, InputBinding conflict)
        {
            return !target.isComposite && !conflict.isComposite &&
                   target.isPartOfComposite == conflict.isPartOfComposite;
        }

        private static bool IsBindablePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.IndexOf("/delta", StringComparison.OrdinalIgnoreCase) < 0 &&
                   path.IndexOf("/position", StringComparison.OrdinalIgnoreCase) < 0 &&
                   path.IndexOf("touch", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
