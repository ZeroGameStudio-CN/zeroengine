using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.AbilitySystem.Editor
{
    public static class AbilityDefinitionEditorDrawer
    {
        public static void Draw(
            SerializedObject serializedObject,
            SerializedProperty abilityProperty,
            AbilityEditorOptions options = null)
        {
            options ??= AbilityEditorOptions.Default();
            if (serializedObject == null || abilityProperty == null)
            {
                EditorGUILayout.HelpBox(options.Labels.MissingAbilityProperty, MessageType.Error);
                return;
            }

            var state = AbilityEditorState.Get(
                serializedObject.targetObject.GetInstanceID(),
                abilityProperty.propertyPath);

            if (options.DrawSummary)
            {
                DrawSummary(abilityProperty, options);
            }

            EditorGUILayout.LabelField(options.Labels.LogicTitle, EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                AbilityComponentPickerDrawer.Draw<AbilityTriggerDefinition>(
                    serializedObject,
                    serializedObject.targetObject,
                    abilityProperty.FindPropertyRelative(nameof(AbilityDefinition.Triggers)),
                    options,
                    state,
                    options.Labels.AddTrigger,
                    ref state.TriggerSearch,
                    ref state.TriggerScroll);

                AbilityComponentPickerDrawer.Draw<AbilityConditionDefinition>(
                    serializedObject,
                    serializedObject.targetObject,
                    abilityProperty.FindPropertyRelative(nameof(AbilityDefinition.Conditions)),
                    options,
                    state,
                    options.Labels.AddCondition,
                    ref state.ConditionSearch,
                    ref state.ConditionScroll);

                AbilityComponentPickerDrawer.Draw<AbilityEffectDefinition>(
                    serializedObject,
                    serializedObject.targetObject,
                    abilityProperty.FindPropertyRelative(nameof(AbilityDefinition.Effects)),
                    options,
                    state,
                    options.Labels.AddEffect,
                    ref state.EffectSearch,
                    ref state.EffectScroll);
            }

            if (options.DrawValidation)
            {
                DrawValidation(abilityProperty, options);
            }

            if (options.DrawDebugRawAbility)
            {
                state.ShowDebugRawAbility = EditorGUILayout.Foldout(
                    state.ShowDebugRawAbility,
                    options.Labels.DebugRawAbility,
                    true);
                if (state.ShowDebugRawAbility)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(abilityProperty, true);
                    }
                }
            }
        }

        private static void DrawSummary(SerializedProperty abilityProperty, AbilityEditorOptions options)
        {
            EditorGUILayout.LabelField(options.Labels.SummaryTitle, EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawRelativeLabel(abilityProperty, nameof(AbilityDefinition.AbilityId), "Id");
                DrawRelativeLabel(abilityProperty, nameof(AbilityDefinition.DisplayName), "Name");
                DrawRelativeLabel(abilityProperty, nameof(AbilityDefinition.ResourceCost), "Cost");
                DrawRelativeLabel(abilityProperty, nameof(AbilityDefinition.CooldownTurns), "Cooldown");
                DrawRelativeLabel(abilityProperty, nameof(AbilityDefinition.TargetMode), "Target Mode");
                DrawComponentSummary<AbilityTriggerDefinition>(
                    abilityProperty.FindPropertyRelative(nameof(AbilityDefinition.Triggers)),
                    options.Labels.TriggerTitle);
                DrawComponentSummary<AbilityConditionDefinition>(
                    abilityProperty.FindPropertyRelative(nameof(AbilityDefinition.Conditions)),
                    options.Labels.ConditionTitle);
                DrawComponentSummary<AbilityEffectDefinition>(
                    abilityProperty.FindPropertyRelative(nameof(AbilityDefinition.Effects)),
                    options.Labels.EffectTitle);
            }
        }

        private static void DrawRelativeLabel(SerializedProperty parent, string relativeName, string label)
        {
            var property = parent.FindPropertyRelative(relativeName);
            if (property == null)
            {
                return;
            }

            EditorGUILayout.LabelField(label, GetPropertyDisplayValue(property));
        }

        private static void DrawComponentSummary<TComponent>(SerializedProperty listProperty, string label)
        {
            if (listProperty == null || listProperty.arraySize == 0)
            {
                EditorGUILayout.LabelField(label, "<none>");
                return;
            }

            var names = new List<string>();
            for (var i = 0; i < listProperty.arraySize; i++)
            {
                var type = listProperty.GetArrayElementAtIndex(i).managedReferenceValue?.GetType();
                if (type == null)
                {
                    continue;
                }

                names.Add(AbilityComponentDocUtility.GetDoc(type).DisplayName);
            }

            EditorGUILayout.LabelField(label, names.Count > 0 ? string.Join(", ", names) : "<none>");
        }

        private static string GetPropertyDisplayValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.String => string.IsNullOrWhiteSpace(property.stringValue) ? "<empty>" : property.stringValue,
                SerializedPropertyType.Integer => property.intValue.ToString(),
                SerializedPropertyType.Boolean => property.boolValue.ToString(),
                SerializedPropertyType.Float => property.floatValue.ToString("0.###"),
                SerializedPropertyType.Enum => property.enumDisplayNames[property.enumValueIndex],
                _ => property.displayName
            };
        }

        private static void DrawValidation(SerializedProperty abilityProperty, AbilityEditorOptions options)
        {
            var ability = ResolveAbilityValue(abilityProperty);
            var issues = new List<AbilityEditorValidationIssue>(AbilityEditorValidationUtility.Validate(ability));
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox(options.Labels.ValidationPassed, MessageType.Info);
                return;
            }

            foreach (var severity in new[]
                     {
                         AbilityEditorIssueSeverity.Error,
                         AbilityEditorIssueSeverity.Warning,
                         AbilityEditorIssueSeverity.Info
                     })
            {
                foreach (var issue in issues)
                {
                    if (issue.Severity != severity)
                    {
                        continue;
                    }

                    EditorGUILayout.HelpBox($"{issue.Code}: {issue.Message}", ToMessageType(issue.Severity));
                }
            }
        }

        private static MessageType ToMessageType(AbilityEditorIssueSeverity severity)
        {
            return severity switch
            {
                AbilityEditorIssueSeverity.Error => MessageType.Error,
                AbilityEditorIssueSeverity.Warning => MessageType.Warning,
                _ => MessageType.Info
            };
        }

        private static AbilityDefinition ResolveAbilityValue(SerializedProperty property)
        {
            object current = property.serializedObject.targetObject;
            var path = property.propertyPath.Replace(".Array.data[", "[");
            foreach (var part in path.Split('.'))
            {
                if (current == null)
                {
                    return null;
                }

                current = ResolvePathPart(current, part);
            }

            return current as AbilityDefinition;
        }

        private static object ResolvePathPart(object source, string part)
        {
            if (part.Contains("[", StringComparison.Ordinal))
            {
                var name = part[..part.IndexOf("[", StringComparison.Ordinal)];
                var indexText = part[(part.IndexOf("[", StringComparison.Ordinal) + 1)..part.IndexOf("]", StringComparison.Ordinal)];
                var collection = ResolveMember(source, name) as System.Collections.IEnumerable;
                if (collection == null || !int.TryParse(indexText, out var index))
                {
                    return null;
                }

                var currentIndex = 0;
                foreach (var item in collection)
                {
                    if (currentIndex == index)
                    {
                        return item;
                    }

                    currentIndex++;
                }

                return null;
            }

            return ResolveMember(source, part);
        }

        private static object ResolveMember(object source, string name)
        {
            var type = source.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(source);
                }

                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    return property.GetValue(source);
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
