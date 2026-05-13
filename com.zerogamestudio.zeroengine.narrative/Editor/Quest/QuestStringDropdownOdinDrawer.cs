#if ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Quest.Editor
{
    public sealed class QuestStringDropdownOdinDrawer : OdinAttributeDrawer<QuestStringDropdownAttribute, string>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            var current = ValueEntry.SmartValue?.Trim() ?? string.Empty;
            var options = BuildOptions(Attribute.Kind, current);
            var currentIndex = GetCurrentIndex(options, current);

            EditorGUILayout.BeginHorizontal();
            var nextIndex = EditorGUILayout.Popup(label, currentIndex, options.ToArray());
            if (nextIndex != currentIndex && nextIndex >= 0 && nextIndex < options.Count)
                ValueEntry.SmartValue = ResolveOption(options[nextIndex], current);

            if (GUILayout.Button("Clear", GUILayout.Width(48)))
                ValueEntry.SmartValue = string.Empty;
            EditorGUILayout.EndHorizontal();

            ValueEntry.SmartValue = EditorGUILayout.DelayedTextField("Manual Input", ValueEntry.SmartValue);
        }

        private static List<string> BuildOptions(QuestStringDropdownKind kind, string current)
        {
            var values = QuestStringDropdownProviderRegistry.GetOptions(kind);
            var options = new List<string> { "(None)" };
            var currentFound = string.IsNullOrWhiteSpace(current);

            foreach (var value in values)
            {
                options.Add(value);
                if (value == current)
                    currentFound = true;
            }

            if (!currentFound)
                options.Add($"Missing: {current}");

            return options;
        }

        private static int GetCurrentIndex(List<string> options, string current)
        {
            if (string.IsNullOrWhiteSpace(current))
                return 0;

            var index = options.IndexOf(current);
            return index >= 0 ? index : options.Count - 1;
        }

        private static string ResolveOption(string option, string current)
        {
            if (option == "(None)")
                return string.Empty;

            return option.StartsWith("Missing: ", StringComparison.Ordinal)
                ? current
                : option;
        }
    }
}
#endif
