using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Quest.Editor
{
    [CustomPropertyDrawer(typeof(QuestIdDropdownAttribute))]
    public sealed class QuestIdDropdownDrawer : PropertyDrawer
    {
        private const float VerticalSpacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var dropdownRect = position;
            dropdownRect.height = EditorGUIUtility.singleLineHeight;

            var manualRect = position;
            manualRect.y = dropdownRect.yMax + VerticalSpacing;
            manualRect.height = EditorGUIUtility.singleLineHeight;

            DrawQuestIdDropdown(dropdownRect, property, label);
            property.stringValue = EditorGUI.DelayedTextField(manualRect, "Manual", property.stringValue);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2f + VerticalSpacing;
        }

        private static void DrawQuestIdDropdown(Rect position, SerializedProperty property, GUIContent label)
        {
            var current = property.stringValue?.Trim() ?? string.Empty;
            var ids = QuestEditorQuestIdProvider.GetQuestIds();
            var options = new List<string> { "(None)" };

            var currentFound = string.IsNullOrWhiteSpace(current);
            foreach (var id in ids)
            {
                options.Add(id);
                if (id == current)
                    currentFound = true;
            }

            if (!currentFound)
                options.Add($"Missing: {current}");

            var currentIndex = 0;
            if (!string.IsNullOrWhiteSpace(current))
            {
                currentIndex = options.IndexOf(current);
                if (currentIndex < 0)
                    currentIndex = options.Count - 1;
            }

            var nextIndex = EditorGUI.Popup(position, label.text, currentIndex, options.ToArray());
            if (nextIndex == currentIndex)
                return;

            var next = options[nextIndex];
            property.stringValue = next == "(None)"
                ? string.Empty
                : next.StartsWith("Missing: ", StringComparison.Ordinal)
                    ? current
                    : next;
        }
    }
}
