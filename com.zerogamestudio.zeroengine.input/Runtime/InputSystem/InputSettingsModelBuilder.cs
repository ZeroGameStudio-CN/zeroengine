using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace ZeroEngine.InputSystem
{
    public readonly struct InputSettingsBindingRow
    {
        public InputSettingsBindingRow(
            string actionId,
            InputActionKey actionKey,
            string bindingGroup,
            string displayNameKey,
            string categoryKey,
            string conflictScope,
            int sortOrder,
            string displayName,
            string effectivePath,
            bool required)
        {
            ActionId = actionId ?? string.Empty;
            ActionKey = actionKey;
            BindingGroup = bindingGroup ?? string.Empty;
            DisplayNameKey = displayNameKey ?? string.Empty;
            CategoryKey = categoryKey ?? string.Empty;
            ConflictScope = conflictScope ?? string.Empty;
            SortOrder = sortOrder;
            DisplayName = displayName ?? string.Empty;
            EffectivePath = effectivePath ?? string.Empty;
            Required = required;
        }

        public string ActionId { get; }
        public InputActionKey ActionKey { get; }
        public string BindingGroup { get; }
        public string DisplayNameKey { get; }
        public string CategoryKey { get; }
        public string ConflictScope { get; }
        public int SortOrder { get; }
        public string DisplayName { get; }
        public string EffectivePath { get; }
        public bool Required { get; }
    }

    public readonly struct InputSettingsModel
    {
        public InputSettingsModel(IReadOnlyList<InputSettingsBindingRow> rows)
        {
            Rows = rows ?? Array.Empty<InputSettingsBindingRow>();
        }

        public IReadOnlyList<InputSettingsBindingRow> Rows { get; }
    }

    public static class InputSettingsModelBuilder
    {
        public static InputSettingsModel Build(
            InputActionAsset asset,
            IReadOnlyList<InputActionCatalogEntry> entries)
        {
            var rows = new List<InputSettingsBindingRow>();
            if (asset == null || entries == null)
            {
                return new InputSettingsModel(rows);
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!entry.Configurable)
                {
                    continue;
                }

                for (var groupIndex = 0; groupIndex < entry.SupportedBindingGroups.Count; groupIndex++)
                {
                    var bindingGroup = entry.SupportedBindingGroups[groupIndex];
                    var bindingKey = new InputBindingKey(
                        entry.ActionKey.ActionMapName,
                        entry.ActionKey.ActionName,
                        bindingGroup);
                    var display = InputBindingDisplayService.GetDisplayName(asset, bindingKey);
                    if (!display.Success)
                    {
                        continue;
                    }

                    rows.Add(new InputSettingsBindingRow(
                        entry.ActionId,
                        entry.ActionKey,
                        bindingGroup,
                        entry.DisplayNameKey,
                        entry.CategoryKey,
                        entry.ConflictScope,
                        entry.SortOrder,
                        display.DisplayName,
                        display.EffectivePath,
                        entry.Required));
                }
            }

            rows.Sort(CompareRows);
            return new InputSettingsModel(rows);
        }

        private static int CompareRows(InputSettingsBindingRow left, InputSettingsBindingRow right)
        {
            var category = string.Compare(left.CategoryKey, right.CategoryKey, StringComparison.Ordinal);
            if (category != 0)
            {
                return category;
            }

            var sortOrder = left.SortOrder.CompareTo(right.SortOrder);
            if (sortOrder != 0)
            {
                return sortOrder;
            }

            var action = string.Compare(left.ActionId, right.ActionId, StringComparison.Ordinal);
            return action != 0
                ? action
                : string.Compare(left.BindingGroup, right.BindingGroup, StringComparison.Ordinal);
        }
    }
}
