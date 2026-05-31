using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.StatSystem
{
    [CreateAssetMenu(fileName = "StatCatalog", menuName = "ZeroEngine/StatSystem/Stat Catalog")]
    public sealed class StatCatalogSO : ScriptableObject
    {
        [SerializeField] private List<StatDefinition> _definitions = new();

        public IReadOnlyList<StatDefinition> Definitions => _definitions;

        public IReadOnlyList<StatDefinition> GetOrderedDefinitions(bool includeHidden = true)
        {
            return _definitions
                .Where(definition => definition != null && (includeHidden || definition.ShowInCharacterEditor))
                .OrderBy(definition => definition.SortOrder)
                .ThenBy(definition => definition.Group)
                .ThenBy(definition => definition.Id.Value)
                .ToArray();
        }

        public bool TryGetDefinition(StatId id, out StatDefinition definition)
        {
            for (var i = 0; i < _definitions.Count; i++)
            {
                var candidate = _definitions[i];
                if (candidate != null && candidate.Id.Equals(id))
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public bool TryFindByExcelColumn(string excelColumn, out StatDefinition definition)
        {
            var normalized = NormalizeExcelColumn(excelColumn);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                definition = null;
                return false;
            }

            for (var i = 0; i < _definitions.Count; i++)
            {
                var candidate = _definitions[i];
                if (candidate != null && NormalizeExcelColumn(candidate.ExcelColumn) == normalized)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public IReadOnlyList<string> ValidateUniqueKeys()
        {
            var issues = new List<string>();
            var ids = new HashSet<StatId>();
            var columns = new HashSet<string>();

            for (var i = 0; i < _definitions.Count; i++)
            {
                var definition = _definitions[i];
                if (definition == null)
                {
                    issues.Add($"Stat definition at index {i} is missing.");
                    continue;
                }

                if (definition.Id.IsEmpty)
                {
                    issues.Add($"Stat definition at index {i} has an empty id.");
                }
                else if (!ids.Add(definition.Id))
                {
                    issues.Add($"Duplicate stat id: {definition.Id.Value}");
                }

                var column = NormalizeExcelColumn(definition.ExcelColumn);
                if (string.IsNullOrWhiteSpace(column))
                {
                    continue;
                }

                if (!columns.Add(column))
                {
                    issues.Add($"Duplicate stat excel column: {definition.ExcelColumn}");
                }
            }

            return issues;
        }

        public void SetDefinitionsForTests(params StatDefinition[] definitions)
        {
            _definitions = definitions != null
                ? new List<StatDefinition>(definitions)
                : new List<StatDefinition>();
        }

        private static string NormalizeExcelColumn(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
    }
}
