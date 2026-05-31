using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.AbilitySystem.Editor
{
    internal static class AbilityComponentPickerDrawer
    {
        public static void Draw<TComponent>(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            AbilityEditorOptions options,
            AbilityEditorState state,
            string title,
            ref string search,
            ref Vector2 scroll)
        {
            if (listProperty == null)
            {
                EditorGUILayout.HelpBox($"{title}: 缺少序列化列表。", MessageType.Error);
                return;
            }

            options ??= AbilityEditorOptions.Default();
            var currentSearch = search;
            var currentScroll = scroll;
            AbilityAuthoringStyles.DrawPanel(() =>
            {
                EditorGUILayout.LabelField(title, AbilityAuthoringStyles.ComponentHeader);
                DrawConfiguredComponents(serializedObject, owner, listProperty, options, state);
                DrawAddSection<TComponent>(serializedObject, owner, listProperty, options, state, title, ref currentSearch, ref currentScroll);
            });
            search = currentSearch;
            scroll = currentScroll;
        }

        private static void DrawConfiguredComponents(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            AbilityEditorOptions options,
            AbilityEditorState state)
        {
            if (listProperty.arraySize == 0)
            {
                DrawEmptyState($"{options.Labels.Configured}：{options.Labels.EmptyConfigured}");
                return;
            }

            for (var i = 0; i < listProperty.arraySize; i++)
            {
                var element = listProperty.GetArrayElementAtIndex(i);
                var component = element.managedReferenceValue;
                var type = component?.GetType();
                var doc = AbilityComponentDocUtility.GetDoc(type);
                var title = type == null || string.IsNullOrWhiteSpace(doc.DisplayName)
                    ? "<缺失组件>"
                    : doc.DisplayName;

                using (new EditorGUILayout.VerticalScope(AbilityAuthoringStyles.ComponentCard))
                {
                    if (DrawComponentHeader(
                        serializedObject,
                        owner,
                        listProperty,
                        options,
                        state,
                        component,
                        i,
                        title,
                        doc))
                    {
                        break;
                    }

                    DrawExpandedDoc(state, $"configured:{listProperty.propertyPath}:{i}:{type?.FullName}", doc);
                    if (component == null)
                    {
                        EditorGUILayout.HelpBox("当前列表包含缺失的 managed reference。", MessageType.Error);
                    }
                    else
                    {
                        if (AbilitySerializedFieldDrawer.HasVisibleChildren(element))
                        {
                            if (options.CompactComponentRows)
                            {
                                var foldoutKey = $"component-fields:{listProperty.propertyPath}:{i}:{type?.FullName}";
                                if (!state.Foldouts.TryGetValue(foldoutKey, out var open))
                                {
                                    open = false;
                                }

                                open = EditorGUILayout.Foldout(open, "参数设置", true);
                                state.Foldouts[foldoutKey] = open;
                                if (open)
                                {
                                    AbilitySerializedFieldDrawer.DrawChildren(element);
                                }
                            }
                            else
                            {
                                AbilitySerializedFieldDrawer.DrawChildren(element);
                            }
                        }
                    }
                }
            }
        }

        private static bool DrawComponentHeader(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            AbilityEditorOptions options,
            AbilityEditorState state,
            object component,
            int index,
            string title,
            AbilityComponentDocInfo doc)
        {
            var actionClicked = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(title, AbilityAuthoringStyles.ComponentHeader);
                    if (!string.IsNullOrWhiteSpace(doc.ShortDescription))
                    {
                        EditorGUILayout.LabelField(doc.ShortDescription, AbilityAuthoringStyles.ComponentDescription);
                    }
                }

                DrawInfoButton(options, state, $"configured:{listProperty.propertyPath}:{index}:{component?.GetType().FullName}", doc);

                if (options.ShowComponentActionsInMenu)
                {
                    DrawActionsMenu(serializedObject, owner, listProperty, options, component, index);
                }
                else
                {
                    actionClicked = DrawInlineActions(serializedObject, owner, listProperty, options, component, index);
                }
            }

            return actionClicked;
        }

        private static bool DrawInlineActions(
            SerializedObject currentSerializedObject,
            Object currentOwner,
            SerializedProperty currentListProperty,
            AbilityEditorOptions options,
            object component,
            int componentIndex)
        {
            using (new EditorGUI.DisabledScope(component == null))
            {
                if (GUILayout.Button(options.Labels.Duplicate, GUILayout.Width(56f)))
                {
                    AbilitySerializedComponentUtility.DuplicateComponent(
                        currentSerializedObject,
                        currentOwner,
                        currentListProperty,
                        componentIndex);
                    return true;
                }
            }

            using (new EditorGUI.DisabledScope(componentIndex <= 0))
            {
                if (GUILayout.Button(options.Labels.MoveUp, GUILayout.Width(42f)))
                {
                    AbilitySerializedComponentUtility.MoveComponent(
                        currentSerializedObject,
                        currentOwner,
                        currentListProperty,
                        componentIndex,
                        componentIndex - 1);
                    return true;
                }
            }

            using (new EditorGUI.DisabledScope(componentIndex >= currentListProperty.arraySize - 1))
            {
                if (GUILayout.Button(options.Labels.MoveDown, GUILayout.Width(42f)))
                {
                    AbilitySerializedComponentUtility.MoveComponent(
                        currentSerializedObject,
                        currentOwner,
                        currentListProperty,
                        componentIndex,
                        componentIndex + 1);
                    return true;
                }
            }

            if (GUILayout.Button(options.Labels.Remove, GUILayout.Width(42f)))
            {
                AbilitySerializedComponentUtility.RemoveComponent(
                    currentSerializedObject,
                    currentOwner,
                    currentListProperty,
                    componentIndex);
                return true;
            }

            return false;
        }

        private static void DrawActionsMenu(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            AbilityEditorOptions options,
            object component,
            int index)
        {
            if (!GUILayout.Button(new GUIContent(options.Labels.Actions, "组件操作"), EditorStyles.miniButton, GUILayout.Width(32f)))
            {
                return;
            }

            var listPath = listProperty.propertyPath;
            var menu = new GenericMenu();
            if (component == null)
            {
                menu.AddDisabledItem(new GUIContent(options.Labels.Duplicate));
            }
            else
            {
                menu.AddItem(new GUIContent(options.Labels.Duplicate), false, () =>
                    ExecuteWithReacquiredList(owner, listPath, (currentSerializedObject, currentListProperty) =>
                    {
                        if (index < currentListProperty.arraySize)
                        {
                            AbilitySerializedComponentUtility.DuplicateComponent(
                                currentSerializedObject,
                                owner,
                                currentListProperty,
                                index);
                        }
                    }));
            }

            if (index <= 0)
            {
                menu.AddDisabledItem(new GUIContent(options.Labels.MoveUp));
            }
            else
            {
                menu.AddItem(new GUIContent(options.Labels.MoveUp), false, () =>
                    ExecuteWithReacquiredList(owner, listPath, (currentSerializedObject, currentListProperty) =>
                    {
                        if (index < currentListProperty.arraySize)
                        {
                            AbilitySerializedComponentUtility.MoveComponent(
                                currentSerializedObject,
                                owner,
                                currentListProperty,
                                index,
                                index - 1);
                        }
                    }));
            }

            if (index >= listProperty.arraySize - 1)
            {
                menu.AddDisabledItem(new GUIContent(options.Labels.MoveDown));
            }
            else
            {
                menu.AddItem(new GUIContent(options.Labels.MoveDown), false, () =>
                    ExecuteWithReacquiredList(owner, listPath, (currentSerializedObject, currentListProperty) =>
                    {
                        if (index < currentListProperty.arraySize - 1)
                        {
                            AbilitySerializedComponentUtility.MoveComponent(
                                currentSerializedObject,
                                owner,
                                currentListProperty,
                                index,
                                index + 1);
                        }
                    }));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(options.Labels.Remove), false, () =>
                ExecuteWithReacquiredList(owner, listPath, (currentSerializedObject, currentListProperty) =>
                {
                    if (index < currentListProperty.arraySize)
                    {
                        AbilitySerializedComponentUtility.RemoveComponent(
                            currentSerializedObject,
                            owner,
                            currentListProperty,
                            index);
                    }
                }));
            menu.ShowAsContext();
        }

        private static void ExecuteWithReacquiredList(
            Object owner,
            string listPath,
            Action<SerializedObject, SerializedProperty> action)
        {
            if (!ReacquireListProperty(owner, listPath, out var serializedObject, out var listProperty))
            {
                return;
            }

            action(serializedObject, listProperty);
        }

        private static bool ReacquireListProperty(
            Object owner,
            string listPath,
            out SerializedObject serializedObject,
            out SerializedProperty listProperty)
        {
            serializedObject = null;
            listProperty = null;
            if (owner == null || string.IsNullOrWhiteSpace(listPath))
            {
                return false;
            }

            serializedObject = new SerializedObject(owner);
            serializedObject.Update();
            listProperty = serializedObject.FindProperty(listPath);
            return listProperty != null && listProperty.isArray;
        }

        private static void DrawSearch(AbilityEditorOptions options, ref string search)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(options.Labels.Search, GUILayout.Width(56f));
                search = EditorGUILayout.TextField(search);
                if (GUILayout.Button(options.Labels.Clear, GUILayout.Width(56f)))
                {
                    search = string.Empty;
                }
            }
        }

        private static void DrawAddSection<TComponent>(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            AbilityEditorOptions options,
            AbilityEditorState state,
            string title,
            ref string search,
            ref Vector2 scroll)
        {
            var key = $"add-section:{listProperty.propertyPath}:{typeof(TComponent).FullName}";
            var defaultOpen = !options.CollapseAddSectionsByDefault;
            if (!state.Foldouts.TryGetValue(key, out var open))
            {
                open = defaultOpen;
            }

            open = EditorGUILayout.Foldout(open, $"+ {title}", true);
            state.Foldouts[key] = open;
            if (!open)
            {
                return;
            }

            DrawSearch(options, ref search);
            DrawAddList<TComponent>(serializedObject, owner, listProperty, options, state, search, ref scroll);
        }

        private static void DrawAddList<TComponent>(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            AbilityEditorOptions options,
            AbilityEditorState state,
            string search,
            ref Vector2 scroll)
        {
            var existingTypes = GetExistingTypes(listProperty);
            var filteredTypes = AbilityComponentTypeCache.GetComponentTypes(typeof(TComponent))
                .Where(type => options.AllowDuplicateComponentTypes || !existingTypes.Contains(type))
                .Where(options.AllowsComponent)
                .Where(type => MatchesSearch(type, search))
                .ToList();

            var maxHeight = Mathf.Min(Mathf.Max(filteredTypes.Count, 1), 6)
                            * (EditorGUIUtility.singleLineHeight + 4f);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(maxHeight + 6f));
            foreach (var type in filteredTypes)
            {
                DrawAddComponentRow(serializedObject, owner, listProperty, options, state, type);
            }

            EditorGUILayout.EndScrollView();

            if (filteredTypes.Count == 0)
            {
                DrawEmptyState(options.Labels.NoMatchingComponents);
            }
        }

        private static void DrawAddComponentRow(
            SerializedObject serializedObject,
            Object owner,
            SerializedProperty listProperty,
            AbilityEditorOptions options,
            AbilityEditorState state,
            Type type)
        {
            var doc = AbilityComponentDocUtility.GetDoc(type);
            var docKey = $"add:{listProperty.propertyPath}:{type.FullName}";
            using (new EditorGUILayout.HorizontalScope())
            {
                var content = new GUIContent(doc.DisplayName, doc.ShortDescription);
                if (GUILayout.Button(content, EditorStyles.miniButtonLeft))
                {
                    AbilitySerializedComponentUtility.AddComponent(serializedObject, owner, listProperty, type);
                }

                DrawInfoButton(options, state, docKey, doc);
            }

            DrawExpandedDoc(state, docKey, doc);
        }

        private static HashSet<Type> GetExistingTypes(SerializedProperty listProperty)
        {
            var types = new HashSet<Type>();
            for (var i = 0; i < listProperty.arraySize; i++)
            {
                var type = listProperty.GetArrayElementAtIndex(i).managedReferenceValue?.GetType();
                if (type != null)
                {
                    types.Add(type);
                }
            }

            return types;
        }

        private static bool MatchesSearch(Type type, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            var doc = AbilityComponentDocUtility.GetDoc(type);
            return type.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                   || doc.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                   || doc.ShortDescription.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                   || doc.ExpandedDescription.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DrawInfoButton(AbilityEditorOptions options, AbilityEditorState state, string key, AbilityComponentDocInfo doc)
        {
            using (new EditorGUI.DisabledScope(!doc.HasDocumentation))
            {
                var label = state.ExpandedDocs.Contains(key) ? $"{options.Labels.Info}*" : options.Labels.Info;
                if (GUILayout.Button(new GUIContent(label, doc.ShortDescription), EditorStyles.miniButtonRight, GUILayout.Width(48f)))
                {
                    if (!state.ExpandedDocs.Add(key))
                    {
                        state.ExpandedDocs.Remove(key);
                    }
                }
            }
        }

        private static void DrawExpandedDoc(AbilityEditorState state, string key, AbilityComponentDocInfo doc)
        {
            if (!state.ExpandedDocs.Contains(key))
            {
                return;
            }

            var message = string.IsNullOrWhiteSpace(doc.ExpandedDescription)
                ? doc.ShortDescription
                : doc.ExpandedDescription;
            if (!string.IsNullOrWhiteSpace(message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }

        private static void DrawEmptyState(string message)
        {
            AbilityAuthoringStyles.DrawEmptyState(message);
        }
    }
}
