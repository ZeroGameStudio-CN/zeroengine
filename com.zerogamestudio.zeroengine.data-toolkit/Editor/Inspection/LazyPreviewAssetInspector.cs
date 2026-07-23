using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    internal sealed class LazyPreviewAssetInspector : IDisposable
    {
        private const int PageSize = 50;
        private const int MaxSummaryDepth = 3;
        private static readonly string[] PreferredSummaryNames =
        {
            "Level",
            "RoomType",
            "Id",
            "ID",
            "Key",
            "Name",
            "DisplayName",
            "Title",
            "LocalizeKey",
            "ClipName",
            "RandomClipsName",
            "ClipReference"
        };

        private readonly Dictionary<string, bool> foldouts = new();
        private readonly Dictionary<string, int> pages = new();
        private SerializedObject serializedObject;
        private Object target;

        public void SetTarget(Object asset)
        {
            if (target == asset)
            {
                return;
            }

            Dispose();
            target = asset;
            if (asset != null)
            {
                serializedObject = new SerializedObject(asset);
            }
        }

        public void Draw()
        {
            if (serializedObject == null)
            {
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                using (new EditorGUI.DisabledScope(iterator.propertyPath == "m_Script"))
                {
                    if (IsPreviewCollection(iterator))
                    {
                        DrawCollectionPreview(iterator.Copy());
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(iterator, BuildLabel(iterator), includeChildren: true);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        public void Dispose()
        {
            serializedObject?.Dispose();
            serializedObject = null;
            target = null;
            foldouts.Clear();
            pages.Clear();
        }

        private static bool IsPreviewCollection(SerializedProperty property)
        {
            return property.isArray && property.propertyType != SerializedPropertyType.String;
        }

        private void DrawCollectionPreview(SerializedProperty property)
        {
            var key = property.propertyPath;
            var count = property.arraySize;
            var expanded = foldouts.TryGetValue(key, out var value) && value;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            expanded = EditorGUILayout.Foldout(expanded, BuildLabel(property), true);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{count} items", GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();
            foldouts[key] = expanded;

            if (expanded)
            {
                var pageCount = Mathf.Max(1, Mathf.CeilToInt(count / (float)PageSize));
                var page = Mathf.Clamp(pages.TryGetValue(key, out var storedPage) ? storedPage : 0, 0, pageCount - 1);
                DrawPageControls(key, page, pageCount);
                page = pages.TryGetValue(key, out storedPage) ? storedPage : page;

                var start = page * PageSize;
                var end = Mathf.Min(start + PageSize, count);
                for (var i = start; i < end; i++)
                {
                    DrawCollectionElement(property.GetArrayElementAtIndex(i), i);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPageControls(string key, int page, int pageCount)
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(page <= 0))
            {
                if (GUILayout.Button("Prev", GUILayout.Width(64f)))
                {
                    page--;
                }
            }

            EditorGUILayout.LabelField($"Page {page + 1}/{pageCount}", GUILayout.Width(110f));

            using (new EditorGUI.DisabledScope(page >= pageCount - 1))
            {
                if (GUILayout.Button("Next", GUILayout.Width(64f)))
                {
                    page++;
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            pages[key] = Mathf.Clamp(page, 0, pageCount - 1);
        }

        private static void DrawCollectionElement(SerializedProperty element, int index)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(element, BuildElementLabel(index, BuildElementSummary(element)), includeChildren: true);
            EditorGUILayout.EndVertical();
        }

        private static GUIContent BuildElementLabel(int index, string summary)
        {
            return string.IsNullOrWhiteSpace(summary)
                ? new GUIContent($"Element {index}")
                : new GUIContent($"Element {index}: {summary}");
        }

        private static string BuildElementSummary(SerializedProperty element)
        {
            if (!element.hasVisibleChildren)
            {
                return ReadLeafValue(element);
            }

            var values = new List<string>();
            foreach (var preferredName in PreferredSummaryNames)
            {
                var child = FindDescendantByNormalizedName(element, preferredName);
                if (child == null)
                {
                    continue;
                }

                var value = ReadLeafValue(child);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add($"{preferredName}: {value}");
                }
            }

            return values.Count == 0 ? "(expand for details)" : string.Join(" | ", values.Distinct());
        }

        private static SerializedProperty FindDescendantByNormalizedName(SerializedProperty parent, string name)
        {
            var copy = parent.Copy();
            var end = copy.GetEndProperty();
            var enterChildren = true;
            var rootDepth = parent.depth;
            var normalizedName = NormalizeName(name);

            while (copy.NextVisible(enterChildren) && !SerializedProperty.EqualContents(copy, end))
            {
                var relativeDepth = copy.depth - rootDepth;
                if (relativeDepth > MaxSummaryDepth)
                {
                    enterChildren = false;
                    continue;
                }

                enterChildren = relativeDepth < MaxSummaryDepth;
                if (NormalizeName(copy.name) == normalizedName)
                {
                    return copy.Copy();
                }
            }

            return null;
        }

        private static string ReadLeafValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Boolean => property.boolValue.ToString(),
                SerializedPropertyType.Enum => FormatEnumValue(property),
                SerializedPropertyType.Integer => property.intValue.ToString(),
                SerializedPropertyType.Float => property.floatValue.ToString("0.###"),
                SerializedPropertyType.ObjectReference => property.objectReferenceValue == null ? string.Empty : property.objectReferenceValue.name,
                _ => ReadKnownNestedValue(property)
            };
        }

        private static string FormatEnumValue(SerializedProperty property)
        {
            return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                ? property.enumDisplayNames[property.enumValueIndex]
                : property.intValue.ToString();
        }

        private static string ReadKnownNestedValue(SerializedProperty property)
        {
            var guidProperty = property.FindPropertyRelative("m_AssetGUID")
                               ?? property.FindPropertyRelative("AssetGUID")
                               ?? property.FindPropertyRelative("assetGUID");
            if (guidProperty != null && guidProperty.propertyType == SerializedPropertyType.String)
            {
                return guidProperty.stringValue;
            }

            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                var stringPreview = ReadStringArrayPreview(property);
                return string.IsNullOrEmpty(stringPreview) ? $"{property.arraySize} items" : stringPreview;
            }

            return string.Empty;
        }

        private static string ReadStringArrayPreview(SerializedProperty property)
        {
            if (!property.isArray || property.propertyType == SerializedPropertyType.String)
            {
                return string.Empty;
            }

            var values = new List<string>();
            var count = Mathf.Min(property.arraySize, 3);
            for (var i = 0; i < count; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                if (element.propertyType == SerializedPropertyType.String && !string.IsNullOrWhiteSpace(element.stringValue))
                {
                    values.Add(element.stringValue);
                }
            }

            if (values.Count == 0)
            {
                return string.Empty;
            }

            return property.arraySize > values.Count
                ? string.Join(", ", values) + ", ..."
                : string.Join(", ", values);
        }

        private static GUIContent BuildLabel(SerializedProperty property)
        {
            return new GUIContent(ObjectNames.NicifyVariableName(StripBackingFieldName(property.name)));
        }

        private static string NormalizeName(string name)
        {
            return StripBackingFieldName(name).Replace(" ", string.Empty).Trim();
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
                var suffixIndex = trimmed.IndexOf(">k__BackingField", StringComparison.Ordinal);
                if (suffixIndex > 1)
                {
                    return trimmed.Substring(1, suffixIndex - 1);
                }
            }

            return trimmed;
        }
    }
}
