using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.TCE.Editor
{
    public static class TceNodeInspector
    {
        public static string BuildSummary(TceComponentData data)
        {
            if (data == null)
                return "Select a component.";

            TceComponentCatalogEntry entry = TceComponentCatalogBuilder.Build()
                .FirstOrDefault(item => item.DataType == data.GetType());

            if (entry == null)
                return $"{data.GetType().Name} ({data.GetType().BaseType?.Name ?? "Component"})";

            return string.IsNullOrEmpty(entry.ShortDescription)
                ? $"{entry.DisplayName} ({entry.Category})"
                : $"{entry.DisplayName} ({entry.Category}): {entry.ShortDescription}";
        }

        public static void DrawSelected(SerializedProperty componentProperty)
        {
            if (componentProperty == null)
            {
                EditorGUILayout.HelpBox("Select a component.", MessageType.Info);
                return;
            }

            EditorGUILayout.PropertyField(componentProperty, includeChildren: true);
        }
    }
}
