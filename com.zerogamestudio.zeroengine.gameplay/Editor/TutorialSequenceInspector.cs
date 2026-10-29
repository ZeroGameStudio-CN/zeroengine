using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZeroEngine.Tutorial.Editor
{
    [CustomEditor(typeof(TutorialSequenceSO))]
    public sealed class TutorialSequenceInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var toolbar = new VisualElement(); toolbar.style.flexDirection = FlexDirection.Row;
            var add = new Button(() =>
            {
                var menu = new GenericMenu();
                foreach (var type in TypeCache.GetTypesDerivedFrom<TutorialStep>().Where(type => !type.IsAbstract && type.IsSerializable &&
                    type.GetConstructor(Type.EmptyTypes) != null && !(type.Namespace ?? string.Empty).Contains(".Tests")).OrderBy(type => type.Name))
                {
                    var selected = type;
                    menu.AddItem(new GUIContent(type.Name), false, () => Add(selected));
                }
                menu.ShowAsContext();
            }) { text = "Add step" };
            add.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
            toolbar.Add(add);
            var validation = new HelpBox("", HelpBoxMessageType.Info);
            toolbar.Add(new Button(() =>
            {
                var sequence = (TutorialSequenceSO)target;
                bool valid = sequence.Validate(out var errors);
                validation.text = valid ? "The sequence is valid. Target availability and project bindings are checked at runtime." : string.Join("\n", errors);
                validation.messageType = valid ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
            }) { text = "Validate" });
            root.Add(toolbar); root.Add(validation);
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            return root;
        }
        private void Add(Type type)
        {
            Undo.RecordObject(target, "Add tutorial step");
            serializedObject.Update();
            var steps = serializedObject.FindProperty("Steps");
            int index = steps.arraySize; steps.InsertArrayElementAtIndex(index);
            var step = (TutorialStep)Activator.CreateInstance(type); step.StepId = Guid.NewGuid().ToString("N");
            steps.GetArrayElementAtIndex(index).managedReferenceValue = step;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }
}
