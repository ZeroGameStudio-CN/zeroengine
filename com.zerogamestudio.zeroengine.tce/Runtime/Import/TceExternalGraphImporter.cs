using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ZeroEngine.TCE
{
    public sealed class TceExternalGraphImportResult
    {
        private TceExternalGraphImportResult(bool succeeded, string graphId, TceGraph graph, IReadOnlyList<TceValidationIssue> issues)
        {
            Succeeded = succeeded;
            GraphId = graphId ?? string.Empty;
            Graph = graph;
            Issues = issues ?? Array.Empty<TceValidationIssue>();
        }

        public bool Succeeded { get; }
        public string GraphId { get; }
        public TceGraph Graph { get; }
        public IReadOnlyList<TceValidationIssue> Issues { get; }

        public static TceExternalGraphImportResult Success(string graphId, TceGraph graph)
        {
            return new TceExternalGraphImportResult(true, graphId, graph, Array.Empty<TceValidationIssue>());
        }

        public static TceExternalGraphImportResult Failed(string graphId, TceGraph graph, IReadOnlyList<TceValidationIssue> issues)
        {
            return new TceExternalGraphImportResult(false, graphId, graph, issues);
        }
    }

    public static class TceExternalGraphImporter
    {
        public static TceExternalGraphImportResult Import(TceExternalGraphDocument document, TceComponentRegistry componentRegistry)
        {
            return Import(document, componentRegistry, new TceGraphMigrationRegistry());
        }

        public static TceExternalGraphImportResult Import(
            TceExternalGraphDocument document,
            TceComponentRegistry componentRegistry,
            TceGraphMigrationRegistry migrationRegistry)
        {
            if (document == null)
            {
                return TceExternalGraphImportResult.Failed(string.Empty, null, new[]
                {
                    new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.NullGraph, "graph", "Graph document must not be null.")
                });
            }

            if (!string.Equals(document.Format, TceGraphSchema.Format, StringComparison.Ordinal))
            {
                return TceExternalGraphImportResult.Failed(document.GraphId, null, new[]
                {
                    new TceValidationIssue(
                        TceValidationSeverity.Error,
                        TceValidationCodes.GraphFormatUnsupported,
                        "format",
                        $"Graph format '{document.Format}' is not supported.")
                });
            }

            if (document.SchemaVersion != TceGraphSchema.CurrentVersion)
            {
                TceGraphMigrationResult migrationResult = (migrationRegistry ?? new TceGraphMigrationRegistry()).Migrate(document);
                if (!migrationResult.Succeeded)
                    return TceExternalGraphImportResult.Failed(document.GraphId, null, migrationResult.Issues);

                document = migrationResult.Document;
            }

            componentRegistry ??= TceComponentRegistry.Create(Array.Empty<Type>());

            var graph = new TceGraph();
            var issues = new List<TceValidationIssue>();
            ImportLane(document.Triggers, "triggers", TceComponentDocCategory.Trigger, componentRegistry, graph, issues);
            ImportLane(document.Conditions, "conditions", TceComponentDocCategory.Condition, componentRegistry, graph, issues);
            ImportLane(document.Effects, "effects", TceComponentDocCategory.Effect, componentRegistry, graph, issues);
            issues.AddRange(TceGraphValidator.Validate(graph));

            return issues.Any(issue => issue.Severity == TceValidationSeverity.Error)
                ? TceExternalGraphImportResult.Failed(document.GraphId, graph, issues)
                : TceExternalGraphImportResult.Success(document.GraphId, graph);
        }

        private static void ImportLane(
            IReadOnlyList<TceExternalGraphNode> nodes,
            string laneName,
            TceComponentDocCategory expectedCategory,
            TceComponentRegistry componentRegistry,
            TceGraph graph,
            List<TceValidationIssue> issues)
        {
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                string path = $"{laneName}[{i}]";
                TceExternalGraphNode node = nodes[i];
                if (node == null)
                {
                    issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.NullComponent, path, "External graph node must not be null."));
                    continue;
                }

                if (!componentRegistry.TryGet(node.ComponentId, out TceComponentRegistryEntry entry))
                {
                    issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.UnsupportedComponent, $"{path}.componentId", $"Component ID '{node.ComponentId}' is not allowed."));
                    continue;
                }

                if (entry.Category != expectedCategory)
                {
                    issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.RuntimeTypeMismatch, path, $"{entry.ComponentId} cannot be used in {laneName}."));
                    continue;
                }

                var data = (TceComponentData)Activator.CreateInstance(entry.DataType);
                ApplyFields(data, node.Fields, $"{path}.fields", issues);

                switch (expectedCategory)
                {
                    case TceComponentDocCategory.Trigger when data is TceTriggerData trigger:
                        graph.AddTrigger(trigger);
                        break;
                    case TceComponentDocCategory.Condition when data is TceConditionData condition:
                        graph.AddCondition(condition);
                        break;
                    case TceComponentDocCategory.Effect when data is TceEffectData effect:
                        graph.AddEffect(effect);
                        break;
                    default:
                        issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.RuntimeTypeMismatch, path, $"{entry.DataType.FullName} does not match {expectedCategory}."));
                        break;
                }
            }
        }

        private static void ApplyFields(TceComponentData data, IReadOnlyDictionary<string, object> fields, string path, List<TceValidationIssue> issues)
        {
            if (fields == null || fields.Count == 0)
                return;

            Dictionary<string, FieldInfo> fieldMap = GetSerializableFields(data.GetType());
            foreach (KeyValuePair<string, object> field in fields)
            {
                string fieldPath = $"{path}.{field.Key}";
                if (!fieldMap.TryGetValue(field.Key, out FieldInfo fieldInfo))
                {
                    issues.Add(new TceValidationIssue(TceValidationSeverity.Error, TceValidationCodes.InvalidField, fieldPath, $"Field '{field.Key}' is not allowed."));
                    continue;
                }

                if (!TryConvertFieldValue(field.Value, fieldInfo.FieldType, out object convertedValue, out string error))
                {
                    string code = fieldInfo.FieldType.IsEnum ? TceValidationCodes.InvalidEnumValue : TceValidationCodes.InvalidField;
                    issues.Add(new TceValidationIssue(TceValidationSeverity.Error, code, fieldPath, error));
                    continue;
                }

                fieldInfo.SetValue(data, convertedValue);
            }
        }

        private static Dictionary<string, FieldInfo> GetSerializableFields(Type dataType)
        {
            var fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            for (Type current = dataType; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
                        continue;

                    fields[field.Name] = field;
                }
            }

            return fields;
        }

        private static bool TryConvertFieldValue(object value, Type fieldType, out object convertedValue, out string error)
        {
            convertedValue = null;
            error = string.Empty;

            if (fieldType == typeof(string))
                return TryConvertString(value, out convertedValue, out error);

            if (fieldType == typeof(bool))
                return TryConvertExact<bool>(value, out convertedValue, out error);

            if (fieldType == typeof(int))
                return TryConvertInt(value, out convertedValue, out error);

            if (fieldType == typeof(float))
                return TryConvertFloat(value, out convertedValue, out error);

            if (fieldType.IsEnum)
                return TryConvertEnum(value, fieldType, out convertedValue, out error);

            error = $"Field type {fieldType.FullName} is not supported by external graph import.";
            return false;
        }

        private static bool TryConvertString(object value, out object convertedValue, out string error)
        {
            if (value is string stringValue)
            {
                convertedValue = stringValue;
                error = string.Empty;
                return true;
            }

            convertedValue = null;
            error = "Value must be a string.";
            return false;
        }

        private static bool TryConvertExact<T>(object value, out object convertedValue, out string error)
        {
            if (value is T typedValue)
            {
                convertedValue = typedValue;
                error = string.Empty;
                return true;
            }

            convertedValue = null;
            error = $"Value must be a {typeof(T).Name}.";
            return false;
        }

        private static bool TryConvertInt(object value, out object convertedValue, out string error)
        {
            if (value is int intValue)
            {
                convertedValue = intValue;
                error = string.Empty;
                return true;
            }

            if (value is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                convertedValue = (int)longValue;
                error = string.Empty;
                return true;
            }

            convertedValue = null;
            error = "Value must be an integer.";
            return false;
        }

        private static bool TryConvertFloat(object value, out object convertedValue, out string error)
        {
            if (value is float floatValue)
            {
                convertedValue = floatValue;
                error = string.Empty;
                return true;
            }

            if (value is double doubleValue)
            {
                convertedValue = (float)doubleValue;
                error = string.Empty;
                return true;
            }

            if (value is int intValue)
            {
                convertedValue = (float)intValue;
                error = string.Empty;
                return true;
            }

            if (value is long longValue)
            {
                convertedValue = (float)longValue;
                error = string.Empty;
                return true;
            }

            convertedValue = null;
            error = "Value must be numeric.";
            return false;
        }

        private static bool TryConvertEnum(object value, Type enumType, out object convertedValue, out string error)
        {
            if (value is string stringValue && Enum.IsDefined(enumType, stringValue))
            {
                convertedValue = Enum.Parse(enumType, stringValue);
                error = string.Empty;
                return true;
            }

            convertedValue = null;
            error = $"Value must be a valid {enumType.Name} name.";
            return false;
        }
    }
}
