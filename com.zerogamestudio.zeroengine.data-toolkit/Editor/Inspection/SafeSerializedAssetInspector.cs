using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    internal sealed class SafeSerializedAssetInspector : IDisposable
    {
        private readonly List<string> skippedPropertyNames = new();
        private DataToolkitSafeInspectorRule rule;
        private SerializedObject serializedObject;
        private Object target;

        public bool CanInspect(Object asset)
        {
            return asset != null;
        }

        public void SetTarget(Object asset, DataToolkitSafeInspectorRule rule)
        {
            if (target == asset && ReferenceEquals(this.rule, rule))
            {
                return;
            }

            Dispose();
            target = asset;
            this.rule = rule;
            if (asset != null && rule != null)
            {
                serializedObject = new SerializedObject(asset);
            }
        }

        public void Draw()
        {
            if (serializedObject == null || rule == null)
            {
                return;
            }

            skippedPropertyNames.Clear();
            serializedObject.UpdateIfRequiredOrScript();
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (IsExcludedProperty(iterator))
                {
                    skippedPropertyNames.Add(GetDisplayName(iterator));
                    continue;
                }

                using (new EditorGUI.DisabledScope(iterator.propertyPath == "m_Script"))
                {
                    EditorGUILayout.PropertyField(iterator, BuildLabel(iterator), includeChildren: true);
                }
            }

            serializedObject.ApplyModifiedProperties();
            DrawSkippedPropertySummary();
        }

        public void Dispose()
        {
            serializedObject?.Dispose();
            serializedObject = null;
            target = null;
            rule = null;
            skippedPropertyNames.Clear();
        }

        private bool IsExcludedProperty(SerializedProperty property)
        {
            foreach (var excludedPropertyPath in rule.ExcludedPropertyPaths)
            {
                var normalizedExcludedPath = NormalizePropertyPath(excludedPropertyPath);
                if (string.IsNullOrEmpty(normalizedExcludedPath))
                {
                    continue;
                }

                var normalizedPropertyName = NormalizePropertyPath(property.name);
                var normalizedPropertyPath = NormalizePropertyPath(property.propertyPath);
                if (normalizedPropertyName == normalizedExcludedPath ||
                    normalizedPropertyPath == normalizedExcludedPath ||
                    normalizedPropertyPath.EndsWith("." + normalizedExcludedPath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private GUIContent BuildLabel(SerializedProperty property)
        {
            var displayName = StripBackingFieldName(property.name);
            return string.IsNullOrWhiteSpace(displayName)
                ? new GUIContent(property.displayName)
                : new GUIContent(ObjectNames.NicifyVariableName(displayName));
        }

        private string GetDisplayName(SerializedProperty property)
        {
            var displayName = StripBackingFieldName(property.name);
            return string.IsNullOrWhiteSpace(displayName)
                ? property.displayName
                : ObjectNames.NicifyVariableName(displayName);
        }

        private void DrawSkippedPropertySummary()
        {
            if (skippedPropertyNames.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                rule.Summary ?? "Some configured heavy fields are hidden to keep this Data Manager view responsive.",
                MessageType.Info);
            EditorGUILayout.LabelField("Hidden Fields", string.Join(", ", skippedPropertyNames.Distinct(StringComparer.Ordinal)));
        }

        private static string NormalizePropertyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalizedParts = path
                .Replace('\\', '.')
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(StripBackingFieldName)
                .Where(part => !string.IsNullOrWhiteSpace(part));

            return string.Join(".", normalizedParts);
        }

        private static string StripBackingFieldName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var trimmed = name.Trim();
            if (trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                var backingFieldSuffixIndex = trimmed.IndexOf(">k__BackingField", StringComparison.Ordinal);
                if (backingFieldSuffixIndex > 1)
                {
                    return trimmed.Substring(1, backingFieldSuffixIndex - 1);
                }
            }

            return trimmed;
        }
    }
}
