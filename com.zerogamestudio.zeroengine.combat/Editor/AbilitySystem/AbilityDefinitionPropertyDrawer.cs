using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZeroEngine.AbilitySystem.Editor
{
    [CustomPropertyDrawer(typeof(AbilityDefinition))]
    public sealed class AbilityDefinitionPropertyDrawer : PropertyDrawer
    {
        private const string ImguiFallbackMessage =
            "Full Ability editor UI is available in UI Toolkit inspectors, or from IMGUI custom editors via AbilityDefinitionEditorDrawer.Draw(...).";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            return new IMGUIContainer(() =>
            {
                if (property == null)
                {
                    return;
                }

                property.serializedObject.UpdateIfRequiredOrScript();
                AbilityDefinitionEditorDrawer.Draw(property.serializedObject, property, AbilityEditorOptions.Default());
                property.serializedObject.ApplyModifiedProperties();
            });
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.HelpBox(position, ImguiFallbackMessage, MessageType.Info);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 3f + EditorGUIUtility.standardVerticalSpacing * 2f;
        }
    }
}
