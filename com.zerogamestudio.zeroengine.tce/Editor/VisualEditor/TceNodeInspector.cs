using UnityEditor;
using UnityEngine;

namespace ZeroEngine.TCE.Editor
{
    public static class TceNodeInspector
    {
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
