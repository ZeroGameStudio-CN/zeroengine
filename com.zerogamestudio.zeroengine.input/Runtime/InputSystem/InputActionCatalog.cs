using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem
{
    public readonly struct InputActionCatalogEntry
    {
        public InputActionCatalogEntry(
            string actionId,
            InputActionKey actionKey,
            IReadOnlyList<string> bindingGroups,
            string conflictScope,
            bool required,
            bool configurable,
            string displayNameKey,
            string categoryKey,
            int sortOrder)
        {
            ActionId = actionId?.Trim() ?? string.Empty;
            ActionKey = actionKey;
            SupportedBindingGroups = CopyBindingGroups(bindingGroups);
            ConflictScope = string.IsNullOrWhiteSpace(conflictScope) ? "default" : conflictScope.Trim();
            Required = required;
            Configurable = configurable;
            DisplayNameKey = displayNameKey?.Trim() ?? string.Empty;
            CategoryKey = categoryKey?.Trim() ?? string.Empty;
            SortOrder = sortOrder;
        }

        public string ActionId { get; }
        public InputActionKey ActionKey { get; }
        public IReadOnlyList<string> SupportedBindingGroups { get; }
        public IReadOnlyList<string> BindingGroups => SupportedBindingGroups;
        public string ConflictScope { get; }
        public bool Required { get; }
        public bool Configurable { get; }
        public string DisplayNameKey { get; }
        public string CategoryKey { get; }
        public int SortOrder { get; }

        private static string[] CopyBindingGroups(IReadOnlyList<string> bindingGroups)
        {
            if (bindingGroups == null || bindingGroups.Count == 0)
            {
                return Array.Empty<string>();
            }

            var copy = new string[bindingGroups.Count];
            for (var i = 0; i < bindingGroups.Count; i++)
            {
                copy[i] = bindingGroups[i]?.Trim() ?? string.Empty;
            }

            return copy;
        }
    }

    public enum InputActionCatalogValidationIssueType
    {
        DuplicateActionId,
        MissingAction,
        MissingBindingGroup
    }

    public readonly struct InputActionCatalogValidationIssue
    {
        public InputActionCatalogValidationIssue(
            InputActionCatalogValidationIssueType issueType,
            string actionId,
            string bindingGroup,
            string diagnostic)
        {
            IssueType = issueType;
            ActionId = actionId ?? string.Empty;
            BindingGroup = bindingGroup ?? string.Empty;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public InputActionCatalogValidationIssueType IssueType { get; }
        public string ActionId { get; }
        public string BindingGroup { get; }
        public string Diagnostic { get; }
    }

    public readonly struct InputActionCatalogValidationResult
    {
        public InputActionCatalogValidationResult(IReadOnlyList<InputActionCatalogValidationIssue> issues)
        {
            Issues = issues ?? Array.Empty<InputActionCatalogValidationIssue>();
        }

        public bool Success => Issues.Count == 0;
        public IReadOnlyList<InputActionCatalogValidationIssue> Issues { get; }
    }

    public static class InputActionCatalogValidator
    {
        public static InputActionCatalogValidationResult Validate(
            InputActionAsset asset,
            IReadOnlyList<InputActionCatalogEntry> entries)
        {
            var issues = new List<InputActionCatalogValidationIssue>();
            if (entries == null)
            {
                return new InputActionCatalogValidationResult(issues);
            }

            var seenActionIds = new HashSet<string>();
            var duplicateActionIds = new HashSet<string>();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!string.IsNullOrWhiteSpace(entry.ActionId) &&
                    !seenActionIds.Add(entry.ActionId) &&
                    duplicateActionIds.Add(entry.ActionId))
                {
                    issues.Add(new InputActionCatalogValidationIssue(
                        InputActionCatalogValidationIssueType.DuplicateActionId,
                        entry.ActionId,
                        string.Empty,
                        $"Input action id '{entry.ActionId}' is duplicated."));
                }

                var action = InputActionLookup.FindAction(asset, entry.ActionKey);
                if (!action.Success)
                {
                    issues.Add(new InputActionCatalogValidationIssue(
                        InputActionCatalogValidationIssueType.MissingAction,
                        entry.ActionId,
                        string.Empty,
                        action.Diagnostic));
                    continue;
                }

                for (var bindingGroupIndex = 0; bindingGroupIndex < entry.SupportedBindingGroups.Count; bindingGroupIndex++)
                {
                    var bindingGroup = entry.SupportedBindingGroups[bindingGroupIndex];
                    var binding = InputActionLookup.FindBinding(
                        asset,
                        new InputBindingKey(
                            entry.ActionKey.ActionMapName,
                            entry.ActionKey.ActionName,
                            bindingGroup));

                    if (binding.Success)
                    {
                        continue;
                    }

                    issues.Add(new InputActionCatalogValidationIssue(
                        InputActionCatalogValidationIssueType.MissingBindingGroup,
                        entry.ActionId,
                        bindingGroup,
                        binding.Diagnostic));
                }
            }

            return new InputActionCatalogValidationResult(issues);
        }
    }
}
