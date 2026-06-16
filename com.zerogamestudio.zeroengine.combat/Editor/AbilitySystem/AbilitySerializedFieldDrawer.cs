using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.AbilitySystem.Editor
{
    internal static class AbilitySerializedFieldDrawer
    {
        private const BindingFlags FieldFlags = BindingFlags.Instance
                                                | BindingFlags.Public
                                                | BindingFlags.NonPublic
                                                | BindingFlags.DeclaredOnly;

        public static void DrawChildren(SerializedProperty element)
        {
            if (element == null)
            {
                return;
            }

            var component = element.managedReferenceValue;
            if (component == null)
            {
                EditorGUILayout.HelpBox("当前组件引用缺失，无法绘制参数。", MessageType.Warning);
                return;
            }

            var componentType = component.GetType();
            var child = element.Copy();
            var end = child.GetEndProperty();
            var enterChildren = true;

            EditorGUI.indentLevel++;
            try
            {
                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    enterChildren = false;
                    if (child.depth <= element.depth)
                    {
                        break;
                    }

                    if (child.depth != element.depth + 1)
                    {
                        continue;
                    }

                    var field = FindField(componentType, child.name);
                    if (IsHidden(field))
                    {
                        continue;
                    }

                    var doc = AbilityFieldDocUtility.GetFieldDoc(field);
                    var content = string.IsNullOrWhiteSpace(doc.DisplayName)
                        ? new GUIContent(child.displayName, doc.Tooltip)
                        : new GUIContent(doc.DisplayName, doc.Tooltip);
                    EditorGUILayout.PropertyField(child, content, true);
                }
            }
            finally
            {
                EditorGUI.indentLevel--;
            }
        }

        public static bool HasVisibleChildren(SerializedProperty element)
        {
            if (element?.managedReferenceValue == null)
            {
                return false;
            }

            var componentType = element.managedReferenceValue.GetType();
            var child = element.Copy();
            var end = child.GetEndProperty();
            var enterChildren = true;
            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                if (child.depth <= element.depth)
                {
                    break;
                }

                if (child.depth != element.depth + 1)
                {
                    continue;
                }

                if (!IsHidden(FindField(componentType, child.name)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsHidden(FieldInfo field)
        {
            return field != null && field.GetCustomAttribute<HideInInspector>() != null;
        }

        private static FieldInfo FindField(Type componentType, string fieldName)
        {
            for (var type = componentType; type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, FieldFlags);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }
    }
}
