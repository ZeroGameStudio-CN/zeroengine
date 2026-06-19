using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAssetValidationService
    {
        private static readonly HashSet<string> StableIdNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "guid",
            "key",
            "configId",
            "dataId",
            "assetId"
        };

        private static readonly HashSet<string> DisplayTextNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "displayName",
            "displayTitle",
            "displayLabel",
            "title",
            "label"
        };

        private static readonly string[] NonNegativeNameFragments =
        {
            "cost",
            "price",
            "count",
            "level",
            "duration",
            "cooldown",
            "weight",
            "chance",
            "probability",
            "volume"
        };

        private static readonly string[] NormalizedRangeNameFragments =
        {
            "chance",
            "probability",
            "volume"
        };

        public static DataValidationReport ValidateAssets(
            IEnumerable<Type> types,
            DataToolkitProjectSettings settings,
            IEnumerable<IDataToolkitValidationProvider> validationProviders = null)
        {
            if (settings == null)
            {
                return DataValidationReport.Empty;
            }

            var entries = new List<AssetEntry>();
            foreach (var type in types ?? Array.Empty<Type>())
            {
                if (!ManageableDataTypeDiscovery.IsManageableScriptableObjectType(type))
                {
                    continue;
                }

                foreach (var assetPath in AssetDiscoveryService.GetAssetPathsForType(type, settings))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    if (asset != null)
                    {
                        entries.Add(new AssetEntry(type, assetPath, asset));
                    }
                }
            }

            return ValidateLoadedAssets(entries, settings, validationProviders);
        }

        public static DataValidationReport ValidateAsset(
            Object asset,
            string assetPath = null,
            IEnumerable<IDataToolkitValidationProvider> validationProviders = null)
        {
            if (asset == null)
            {
                return new DataValidationReport(new[]
                {
                    new DataValidationIssue(
                        DataValidationSeverity.Error,
                        string.Empty,
                        string.Empty,
                        assetPath,
                        string.Empty,
                        "Asset is missing or could not be loaded.")
                });
            }

            return ValidateLoadedAssets(
                new[] { new AssetEntry(asset.GetType(), assetPath, asset) },
                null,
                validationProviders);
        }

        public static DataValidationReport ValidateLoadedObjects(
            IEnumerable<Object> assets,
            IEnumerable<IDataToolkitValidationProvider> validationProviders = null)
        {
            var entries = (assets ?? Array.Empty<Object>())
                .Where(asset => asset != null)
                .Select(asset => new AssetEntry(asset.GetType(), AssetDatabase.GetAssetPath(asset), asset));

            return ValidateLoadedAssets(entries, null, validationProviders);
        }

        private static DataValidationReport ValidateLoadedAssets(
            IEnumerable<AssetEntry> entries,
            DataToolkitProjectSettings settings,
            IEnumerable<IDataToolkitValidationProvider> validationProviders)
        {
            var materializedEntries = (entries ?? Array.Empty<AssetEntry>())
                .Where(entry => entry.Asset != null)
                .ToArray();
            var issues = new List<DataValidationIssue>();
            var idRecordsByType = new Dictionary<Type, List<StableIdRecord>>();

            foreach (var entry in materializedEntries)
            {
                ValidateSingleAsset(entry, issues, idRecordsByType);
            }

            AddDuplicateStableIdIssues(idRecordsByType, issues);
            AddProviderIssues(materializedEntries, settings, validationProviders, issues);
            return new DataValidationReport(issues);
        }

        private static void ValidateSingleAsset(
            AssetEntry entry,
            ICollection<DataValidationIssue> issues,
            IDictionary<Type, List<StableIdRecord>> idRecordsByType)
        {
            SerializedObject serializedObject;
            try
            {
                serializedObject = new SerializedObject(entry.Asset);
            }
            catch (Exception exception)
            {
                issues.Add(CreateIssue(
                    DataValidationSeverity.Error,
                    entry,
                    string.Empty,
                    $"Could not inspect asset: {exception.Message}"));
                return;
            }

            var iterator = serializedObject.GetIterator();
            var enterChildren = true;
            StableIdRecord stableIdRecord = null;
            var rootNumericRecords = new Dictionary<string, NumericFieldRecord>(StringComparer.OrdinalIgnoreCase);

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                if (IsBrokenObjectReference(iterator))
                {
                    issues.Add(CreateIssue(
                        DataValidationSeverity.Error,
                        entry,
                        iterator.propertyPath,
                        "Object reference is broken and points to a missing asset."));
                }

                if (IsEmptyObjectReferenceCollectionItem(iterator))
                {
                    issues.Add(CreateIssue(
                        DataValidationSeverity.Warning,
                        entry,
                        iterator.propertyPath,
                        "Object reference collection contains an empty entry."));
                }

                if (stableIdRecord == null && IsRootStableIdString(iterator))
                {
                    var idValue = iterator.stringValue?.Trim();
                    stableIdRecord = new StableIdRecord(entry, iterator.propertyPath, idValue);
                    if (string.IsNullOrEmpty(idValue))
                    {
                        issues.Add(CreateIssue(
                            DataValidationSeverity.Warning,
                            entry,
                            iterator.propertyPath,
                            "Stable ID field is empty."));
                    }
                    else if (!string.Equals(iterator.stringValue, idValue, StringComparison.Ordinal))
                    {
                        issues.Add(CreateIssue(
                            DataValidationSeverity.Warning,
                            entry,
                            iterator.propertyPath,
                            "Stable ID has leading or trailing whitespace."));
                    }
                }

                if (IsRootDisplayTextString(iterator) && string.IsNullOrWhiteSpace(iterator.stringValue))
                {
                    issues.Add(CreateIssue(
                        DataValidationSeverity.Warning,
                        entry,
                        iterator.propertyPath,
                        "Designer-facing display text is empty."));
                }

                if (TryGetNumericValue(iterator, out var numericValue))
                {
                    if (IsRootProperty(iterator))
                    {
                        rootNumericRecords[NormalizeFieldName(iterator.name)] = new NumericFieldRecord(
                            entry,
                            iterator.propertyPath,
                            iterator.name,
                            numericValue);
                    }

                    if (numericValue < 0d && IsClearlyNonNegativeNumber(iterator.name))
                    {
                        issues.Add(CreateIssue(
                            DataValidationSeverity.Error,
                            entry,
                            iterator.propertyPath,
                            "Designer-facing numeric value must not be negative."));
                    }

                    if ((numericValue < 0d || numericValue > 1d) && IsClearlyNormalizedNumber(iterator.name))
                    {
                        issues.Add(CreateIssue(
                            DataValidationSeverity.Error,
                            entry,
                            iterator.propertyPath,
                            "Normalized value must be between 0 and 1."));
                    }
                }
            }

            AddInvalidMinMaxIssues(rootNumericRecords, issues);

            if (stableIdRecord != null && !string.IsNullOrEmpty(stableIdRecord.Value))
            {
                if (!idRecordsByType.TryGetValue(entry.Type, out var records))
                {
                    records = new List<StableIdRecord>();
                    idRecordsByType.Add(entry.Type, records);
                }

                records.Add(stableIdRecord);
            }
        }

        private static bool IsBrokenObjectReference(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.ObjectReference &&
                   property.objectReferenceInstanceIDValue != 0 &&
                   property.objectReferenceValue == null;
        }

        private static bool IsEmptyObjectReferenceCollectionItem(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.ObjectReference &&
                   property.objectReferenceInstanceIDValue == 0 &&
                   property.objectReferenceValue == null &&
                   property.propertyPath.IndexOf(".Array.data[", StringComparison.Ordinal) >= 0;
        }

        private static bool IsRootStableIdString(SerializedProperty property)
        {
            if (!IsRootProperty(property) || property.propertyType != SerializedPropertyType.String)
            {
                return false;
            }

            return StableIdNames.Contains(property.name) ||
                   property.name.EndsWith("Id", StringComparison.Ordinal) ||
                   property.name.EndsWith("ID", StringComparison.Ordinal);
        }

        private static bool IsRootDisplayTextString(SerializedProperty property)
        {
            return IsRootProperty(property) &&
                   property.propertyType == SerializedPropertyType.String &&
                   DisplayTextNames.Contains(property.name);
        }

        private static bool IsRootProperty(SerializedProperty property)
        {
            return property != null && property.depth == 0;
        }

        private static bool TryGetNumericValue(SerializedProperty property, out double value)
        {
            value = 0d;
            if (property == null)
            {
                return false;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    value = property.intValue;
                    return true;
                case SerializedPropertyType.Float:
                    value = property.floatValue;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsClearlyNonNegativeNumber(string fieldName)
        {
            return ContainsAnyFragment(fieldName, NonNegativeNameFragments);
        }

        private static bool IsClearlyNormalizedNumber(string fieldName)
        {
            return ContainsAnyFragment(fieldName, NormalizedRangeNameFragments);
        }

        private static bool ContainsAnyFragment(string fieldName, IEnumerable<string> fragments)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            foreach (var fragment in fragments)
            {
                if (fieldName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddInvalidMinMaxIssues(
            IReadOnlyDictionary<string, NumericFieldRecord> rootNumericRecords,
            ICollection<DataValidationIssue> issues)
        {
            foreach (var record in rootNumericRecords.Values)
            {
                var normalizedName = NormalizeFieldName(record.FieldName);
                if (!normalizedName.StartsWith("min", StringComparison.OrdinalIgnoreCase) ||
                    normalizedName.Length <= 3)
                {
                    continue;
                }

                var pairedMaxName = "max" + normalizedName.Substring(3);
                if (!rootNumericRecords.TryGetValue(pairedMaxName, out var maxRecord) ||
                    record.Value <= maxRecord.Value)
                {
                    continue;
                }

                issues.Add(CreateIssue(
                    DataValidationSeverity.Error,
                    record.Entry,
                    maxRecord.FieldPath,
                    $"Max value '{maxRecord.FieldName}' is lower than min value '{record.FieldName}'."));
            }
        }

        private static string NormalizeFieldName(string fieldName)
        {
            return string.IsNullOrWhiteSpace(fieldName)
                ? string.Empty
                : fieldName.TrimStart('_').Replace(" ", string.Empty);
        }

        private static void AddDuplicateStableIdIssues(
            IDictionary<Type, List<StableIdRecord>> idRecordsByType,
            ICollection<DataValidationIssue> issues)
        {
            foreach (var records in idRecordsByType.Values)
            {
                foreach (var duplicateGroup in records.GroupBy(record => record.Value, StringComparer.OrdinalIgnoreCase))
                {
                    var duplicates = duplicateGroup.ToArray();
                    if (duplicates.Length <= 1)
                    {
                        continue;
                    }

                    foreach (var duplicate in duplicates)
                    {
                        issues.Add(CreateIssue(
                            DataValidationSeverity.Error,
                            duplicate.Entry,
                            duplicate.FieldPath,
                            $"Stable ID '{duplicate.Value}' is duplicated in {duplicates.Length} assets of type {duplicate.Entry.Type.Name}."));
                    }
                }
            }
        }

        private static void AddProviderIssues(
            IEnumerable<AssetEntry> entries,
            DataToolkitProjectSettings settings,
            IEnumerable<IDataToolkitValidationProvider> validationProviders,
            ICollection<DataValidationIssue> issues)
        {
            var providers = (validationProviders ?? Array.Empty<IDataToolkitValidationProvider>())
                .Where(provider => provider != null)
                .OrderBy(provider => provider.Order)
                .ToArray();
            if (providers.Length == 0)
            {
                return;
            }

            var validationAssets = entries
                .Select(entry => new DataValidationAsset(entry.Type, entry.AssetPath, entry.Asset))
                .ToArray();
            var context = new DataToolkitValidationContext(settings, validationAssets);

            foreach (var provider in providers)
            {
                try
                {
                    foreach (var issue in provider.Validate(context) ?? Array.Empty<DataValidationIssue>())
                    {
                        if (issue != null)
                        {
                            issues.Add(issue);
                        }
                    }
                }
                catch (Exception exception)
                {
                    issues.Add(new DataValidationIssue(
                        DataValidationSeverity.Error,
                        provider.GetType().Name,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        $"Validation provider failed: {exception.Message}"));
                }
            }
        }

        private static DataValidationIssue CreateIssue(
            DataValidationSeverity severity,
            AssetEntry entry,
            string fieldPath,
            string message)
        {
            return new DataValidationIssue(
                severity,
                entry.Type?.Name,
                entry.Asset == null ? string.Empty : entry.Asset.name,
                entry.AssetPath,
                fieldPath,
                message);
        }

        private sealed class AssetEntry
        {
            public AssetEntry(Type type, string assetPath, Object asset)
            {
                Type = type;
                AssetPath = string.IsNullOrWhiteSpace(assetPath) ? AssetDatabase.GetAssetPath(asset) : assetPath.Replace('\\', '/');
                Asset = asset;
            }

            public Type Type { get; }
            public string AssetPath { get; }
            public Object Asset { get; }
        }

        private sealed class StableIdRecord
        {
            public StableIdRecord(AssetEntry entry, string fieldPath, string value)
            {
                Entry = entry;
                FieldPath = fieldPath;
                Value = value;
            }

            public AssetEntry Entry { get; }
            public string FieldPath { get; }
            public string Value { get; }
        }

        private sealed class NumericFieldRecord
        {
            public NumericFieldRecord(AssetEntry entry, string fieldPath, string fieldName, double value)
            {
                Entry = entry;
                FieldPath = fieldPath;
                FieldName = fieldName;
                Value = value;
            }

            public AssetEntry Entry { get; }
            public string FieldPath { get; }
            public string FieldName { get; }
            public double Value { get; }
        }
    }
}
