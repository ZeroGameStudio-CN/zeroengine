using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAssetFieldChangeService
    {
        public static DataAssetChangePreview PreviewChanges(
            IEnumerable<DataAssetFieldUpdate> updates,
            IReadOnlyDictionary<string, Object> assetsByPath)
        {
            var changes = new List<DataAssetFieldChange>();
            foreach (var update in updates ?? Array.Empty<DataAssetFieldUpdate>())
            {
                changes.Add(PreviewChange(update, assetsByPath));
            }

            return new DataAssetChangePreview(changes);
        }

        public static int Apply(DataAssetChangePreview preview, string undoName)
        {
            if (preview == null)
            {
                return 0;
            }

            var appliedCount = 0;
            foreach (var change in preview.Changes.Where(change => change.CanApply))
            {
                var serializedObject = new SerializedObject(change.Asset);
                var property = serializedObject.FindProperty(change.FieldPath);
                if (property == null)
                {
                    continue;
                }

                Undo.RecordObject(change.Asset, string.IsNullOrWhiteSpace(undoName) ? "Apply Data Asset Changes" : undoName);
                if (!DataAssetSerializedPropertyUtility.TrySetValue(property, change.NewValue, out _))
                {
                    continue;
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(change.Asset);
                change.MarkApplied();
                appliedCount++;
            }

            if (appliedCount > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return appliedCount;
        }

        private static DataAssetFieldChange PreviewChange(
            DataAssetFieldUpdate update,
            IReadOnlyDictionary<string, Object> assetsByPath)
        {
            if (update == null)
            {
                return CreateBlockedChange(null, null, null, null, null, null, DataAssetFieldChangeState.MissingAsset, "Update row is empty.");
            }

            var asset = ResolveAsset(update.AssetPath, assetsByPath);
            if (asset == null)
            {
                return CreateBlockedChange(
                    null,
                    update.AssetPath,
                    string.Empty,
                    string.Empty,
                    update.FieldPath,
                    update.NewValue,
                    DataAssetFieldChangeState.MissingAsset,
                    $"Asset '{update.AssetPath}' could not be loaded.");
            }

            var serializedObject = new SerializedObject(asset);
            var property = serializedObject.FindProperty(update.FieldPath);
            if (property == null)
            {
                return CreateBlockedChange(
                    asset,
                    update.AssetPath,
                    asset.GetType().Name,
                    asset.name,
                    update.FieldPath,
                    update.NewValue,
                    DataAssetFieldChangeState.MissingField,
                    $"Field '{update.FieldPath}' was not found.");
            }

            if (!DataAssetSerializedPropertyUtility.TryReadValue(property, out var currentValue))
            {
                return CreateBlockedChange(
                    asset,
                    update.AssetPath,
                    asset.GetType().Name,
                    asset.name,
                    update.FieldPath,
                    update.NewValue,
                    DataAssetFieldChangeState.UnsupportedFieldType,
                    $"Field type '{property.propertyType}' is not supported.");
            }

            if (!DataAssetSerializedPropertyUtility.TryValidateValue(property, update.NewValue, out var error))
            {
                return CreateBlockedChange(
                    asset,
                    update.AssetPath,
                    asset.GetType().Name,
                    asset.name,
                    update.FieldPath,
                    update.NewValue,
                    DataAssetFieldChangeState.InvalidValue,
                    error);
            }

            var normalizedNewValue = update.NewValue ?? string.Empty;
            var state = string.Equals(currentValue, normalizedNewValue, StringComparison.Ordinal)
                ? DataAssetFieldChangeState.Unchanged
                : DataAssetFieldChangeState.Pending;
            var message = state == DataAssetFieldChangeState.Unchanged ? "No change." : "Ready to apply.";
            return new DataAssetFieldChange(
                asset,
                update.AssetPath,
                asset.GetType().Name,
                asset.name,
                update.FieldPath,
                currentValue,
                update.NewValue,
                state,
                message);
        }

        private static Object ResolveAsset(string assetPath, IReadOnlyDictionary<string, Object> assetsByPath)
        {
            assetPath = NormalizePath(assetPath);
            if (!string.IsNullOrEmpty(assetPath) &&
                assetsByPath != null &&
                assetsByPath.TryGetValue(assetPath, out var assetFromMap))
            {
                return assetFromMap;
            }

            return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<Object>(assetPath);
        }

        private static DataAssetFieldChange CreateBlockedChange(
            Object asset,
            string assetPath,
            string typeName,
            string assetName,
            string fieldPath,
            string newValue,
            DataAssetFieldChangeState state,
            string message)
        {
            return new DataAssetFieldChange(
                asset,
                assetPath,
                typeName,
                assetName,
                fieldPath,
                string.Empty,
                newValue,
                state,
                message);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
