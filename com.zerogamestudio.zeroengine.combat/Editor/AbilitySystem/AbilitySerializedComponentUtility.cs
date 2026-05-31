using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.AbilitySystem.Editor
{
    public static class AbilitySerializedComponentUtility
    {
        public static void AddComponent(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            Type componentType)
        {
            ValidateMutation(serializedObject, owner, listProperty);
            if (componentType == null)
            {
                throw new ArgumentNullException(nameof(componentType));
            }

            if (componentType.IsAbstract || componentType.IsInterface)
            {
                throw new ArgumentException("Ability component type must be concrete.", nameof(componentType));
            }

            Undo.RecordObject(owner, $"Add {componentType.Name}");
            listProperty.arraySize++;
            var element = listProperty.GetArrayElementAtIndex(listProperty.arraySize - 1);
            element.managedReferenceValue = Activator.CreateInstance(componentType);
            Apply(serializedObject, owner);
        }

        public static void DuplicateComponent(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            int index)
        {
            ValidateMutation(serializedObject, owner, listProperty);
            ValidateIndex(listProperty, index);

            var source = listProperty.GetArrayElementAtIndex(index).managedReferenceValue;
            if (source == null)
            {
                return;
            }

            Undo.RecordObject(owner, $"Duplicate {source.GetType().Name}");
            listProperty.InsertArrayElementAtIndex(index + 1);
            var element = listProperty.GetArrayElementAtIndex(index + 1);
            var clone = Activator.CreateInstance(source.GetType());
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), clone);
            element.managedReferenceValue = clone;
            Apply(serializedObject, owner);
        }

        public static void MoveComponent(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            int fromIndex,
            int toIndex)
        {
            ValidateMutation(serializedObject, owner, listProperty);
            ValidateIndex(listProperty, fromIndex);
            ValidateIndex(listProperty, toIndex);

            if (fromIndex == toIndex)
            {
                return;
            }

            Undo.RecordObject(owner, "Move Ability Component");
            listProperty.MoveArrayElement(fromIndex, toIndex);
            Apply(serializedObject, owner);
        }

        public static void RemoveComponent(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            int index)
        {
            ValidateMutation(serializedObject, owner, listProperty);
            ValidateIndex(listProperty, index);

            Undo.RecordObject(owner, "Remove Ability Component");
            listProperty.DeleteArrayElementAtIndex(index);
            Apply(serializedObject, owner);
        }

        private static void ValidateMutation(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty)
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (listProperty == null)
            {
                throw new ArgumentNullException(nameof(listProperty));
            }

            if (!listProperty.isArray)
            {
                throw new ArgumentException("Ability component property must be an array or list.", nameof(listProperty));
            }
        }

        private static void ValidateIndex(SerializedProperty listProperty, int index)
        {
            if (index < 0 || index >= listProperty.arraySize)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Ability component index is outside the list.");
            }
        }

        private static void Apply(SerializedObject serializedObject, Object owner)
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(owner);
        }
    }
}
