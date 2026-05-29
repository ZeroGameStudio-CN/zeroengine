using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    internal sealed class SafeOdinAssetInspector : IDisposable
    {
        private static readonly Type PropertyTreeType =
            Type.GetType("Sirenix.OdinInspector.Editor.PropertyTree, Sirenix.OdinInspector.Editor");

        private static readonly Type InspectorPropertyType =
            Type.GetType("Sirenix.OdinInspector.Editor.InspectorProperty, Sirenix.OdinInspector.Editor");

        private static readonly Type PropertyChildrenType =
            Type.GetType("Sirenix.OdinInspector.Editor.PropertyChildren, Sirenix.OdinInspector.Editor");

        private static readonly MethodInfo CreateMethod = PropertyTreeType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "Create" && method.GetParameters().Length == 1);

        private static readonly MethodInfo BeginDrawMethod = PropertyTreeType?.GetMethod("BeginDraw", new[] { typeof(bool) });
        private static readonly MethodInfo EndDrawMethod = PropertyTreeType?.GetMethod("EndDraw", Type.EmptyTypes);
        private static readonly PropertyInfo RootProperty = PropertyTreeType?.GetProperty("RootProperty");
        private static readonly PropertyInfo ChildrenProperty = InspectorPropertyType?.GetProperty("Children");
        private static readonly PropertyInfo NameProperty = InspectorPropertyType?.GetProperty("Name");
        private static readonly PropertyInfo NiceNameProperty = InspectorPropertyType?.GetProperty("NiceName");
        private static readonly PropertyInfo PathProperty = InspectorPropertyType?.GetProperty("Path");
        private static readonly PropertyInfo UnityPropertyPathProperty = InspectorPropertyType?.GetProperty("UnityPropertyPath");
        private static readonly PropertyInfo DeepReflectionPathProperty = InspectorPropertyType?.GetProperty("DeepReflectionPath");
        private static readonly MethodInfo DrawMethod = InspectorPropertyType?.GetMethod("Draw", Type.EmptyTypes);
        private static readonly PropertyInfo ChildCountProperty = PropertyChildrenType?.GetProperty("Count");
        private static readonly MethodInfo GetChildMethod = PropertyChildrenType?.GetMethod("Get", new[] { typeof(int) });

        private readonly List<string> skippedPropertyNames = new();

        private DataToolkitSafeOdinInspectorRule rule;
        private object propertyTree;
        private Object target;

        public bool CanInspect(Object asset)
        {
            return asset != null &&
                   PropertyTreeType != null &&
                   InspectorPropertyType != null &&
                   PropertyChildrenType != null &&
                   CreateMethod != null &&
                   BeginDrawMethod != null &&
                   EndDrawMethod != null &&
                   RootProperty != null &&
                   ChildrenProperty != null &&
                   DrawMethod != null &&
                   ChildCountProperty != null &&
                   GetChildMethod != null;
        }

        public void SetTarget(Object asset, DataToolkitSafeOdinInspectorRule rule)
        {
            if (target == asset && ReferenceEquals(this.rule, rule))
            {
                return;
            }

            Dispose();
            target = asset;
            this.rule = rule;
            if (asset != null && rule != null && CanInspect(asset))
            {
                propertyTree = CreateMethod.Invoke(null, new object[] { asset });
            }
        }

        public void Draw()
        {
            if (propertyTree == null || rule == null)
            {
                return;
            }

            skippedPropertyNames.Clear();
            var rootProperty = RootProperty.GetValue(propertyTree);
            var children = rootProperty == null ? null : ChildrenProperty.GetValue(rootProperty);
            if (children == null)
            {
                return;
            }

            BeginDrawMethod.Invoke(propertyTree, new object[] { true });
            try
            {
                var childCount = (int)ChildCountProperty.GetValue(children);
                for (int i = 0; i < childCount; i++)
                {
                    var property = GetChildMethod.Invoke(children, new object[] { i });
                    if (property == null)
                    {
                        continue;
                    }

                    if (IsExcludedProperty(property))
                    {
                        skippedPropertyNames.Add(GetDisplayName(property));
                        continue;
                    }

                    DrawMethod.Invoke(property, null);
                }
            }
            finally
            {
                EndDrawMethod.Invoke(propertyTree, null);
            }

            DrawSkippedPropertySummary();
        }

        public void Dispose()
        {
            if (propertyTree is IDisposable disposable)
            {
                disposable.Dispose();
            }

            propertyTree = null;
            target = null;
            rule = null;
            skippedPropertyNames.Clear();
        }

        private bool IsExcludedProperty(object property)
        {
            foreach (var excludedPropertyPath in rule.ExcludedPropertyPaths)
            {
                var normalizedExcludedPath = NormalizePropertyPath(excludedPropertyPath);
                if (string.IsNullOrEmpty(normalizedExcludedPath))
                {
                    continue;
                }

                foreach (var candidate in GetPropertyPathCandidates(property))
                {
                    var normalizedCandidate = NormalizePropertyPath(candidate);
                    if (normalizedCandidate == normalizedExcludedPath ||
                        normalizedCandidate.EndsWith("." + normalizedExcludedPath, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private IEnumerable<string> GetPropertyPathCandidates(object property)
        {
            yield return NameProperty?.GetValue(property) as string;
            yield return PathProperty?.GetValue(property) as string;
            yield return UnityPropertyPathProperty?.GetValue(property) as string;
            yield return DeepReflectionPathProperty?.GetValue(property) as string;
        }

        private string GetDisplayName(object property)
        {
            return NiceNameProperty?.GetValue(property) as string ??
                   StripBackingFieldName(NameProperty?.GetValue(property) as string) ??
                   "(hidden)";
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
