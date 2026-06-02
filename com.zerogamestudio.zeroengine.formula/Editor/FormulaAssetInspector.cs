using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    [CustomEditor(typeof(FormulaAsset))]
    public sealed class FormulaAssetInspector : UnityEditor.Editor
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string OperationProperty = "<Operation>k__BackingField";
        private const string SourceProperty = "<Source>k__BackingField";
        private const string SourceTypeProperty = "<SourceType>k__BackingField";
        private const string ConstantValueProperty = "<ConstantValue>k__BackingField";
        private const string ProviderIdProperty = "<ProviderId>k__BackingField";
        private const string ParametersProperty = "<Parameters>k__BackingField";
        private const string NestedFormulaProperty = "<NestedFormula>k__BackingField";
        private const string NameProperty = "<Name>k__BackingField";
        private const string TypeProperty = "<Type>k__BackingField";
        private const string StringValueProperty = "<StringValue>k__BackingField";
        private const string IntValueProperty = "<IntValue>k__BackingField";
        private const string FloatValueProperty = "<FloatValue>k__BackingField";
        private const string BoolValueProperty = "<BoolValue>k__BackingField";
        private const string ObjectValueProperty = "<ObjectValue>k__BackingField";

        private SerializedProperty initialValue;
        private SerializedProperty steps;
        private FormulaEvaluationReport lastReport;
        private readonly FormulaEditorPreviewState previewState = new();

        private void OnEnable()
        {
            initialValue = serializedObject.FindProperty("initialValue");
            steps = serializedObject.FindProperty("steps");
        }

        public override void OnInspectorGUI()
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            serializedObject.Update();

            EditorGUILayout.LabelField(FormulaEditorLabels.Formula, target.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Profile", $"{profile.DisplayName} ({profile.ProfileId})");
            EditorGUILayout.PropertyField(initialValue, new GUIContent(FormulaEditorLabels.InitialValue, "公式开始计算时使用的基础值。"));
            DrawSteps(profile);
            serializedObject.ApplyModifiedProperties();

            FormulaEditorGUILayout.DrawPreviewInputs(profile, previewState);
            if (GUILayout.Button(FormulaEditorLabels.Evaluate))
            {
                FormulaEditorPreview.TryEvaluate(
                    (FormulaAsset)target,
                    profile,
                    previewState.CreateContext(profile),
                    out _,
                    out lastReport);
            }

            FormulaEditorGUILayout.DrawReport(lastReport);
        }

        private void DrawSteps(FormulaEditorProfile profile)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(FormulaEditorLabels.Steps, EditorStyles.boldLabel);

            for (var i = 0; i < steps.arraySize; i++)
            {
                var step = steps.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i + 1}", EditorStyles.boldLabel);
                if (GUILayout.Button(FormulaEditorLabels.RemoveStep, GUILayout.Width(80)))
                {
                    steps.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }

                EditorGUILayout.EndHorizontal();
                DrawOperation(step.FindPropertyRelative(OperationProperty));
                DrawSource(step.FindPropertyRelative(SourceProperty), profile);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button(FormulaEditorLabels.AddStep))
                AddDefaultStep();
        }

        private static void DrawOperation(SerializedProperty operation)
        {
            if (operation == null)
                return;

            var values = (FormulaOperationType[])Enum.GetValues(typeof(FormulaOperationType));
            var labels = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
                labels[i] = FormulaEditorLabels.OperationName(values[i]);

            operation.enumValueIndex = EditorGUILayout.Popup(
                new GUIContent(FormulaEditorLabels.Operation, "本步骤对累计结果执行的运算。"),
                operation.enumValueIndex,
                labels);
        }

        private static void DrawSource(SerializedProperty source, FormulaEditorProfile profile)
        {
            if (source == null)
            {
                EditorGUILayout.HelpBox("步骤来源为空，请删除后重新添加。", MessageType.Error);
                return;
            }

            var sourceType = source.FindPropertyRelative(SourceTypeProperty);
            DrawSourceType(sourceType);

            var type = (FormulaValueSourceType)sourceType.enumValueIndex;
            switch (type)
            {
                case FormulaValueSourceType.Constant:
                    EditorGUILayout.PropertyField(
                        source.FindPropertyRelative(ConstantValueProperty),
                        new GUIContent(FormulaEditorLabels.ConstantValue, "本步骤直接使用的数值。"));
                    break;
                case FormulaValueSourceType.Provider:
                    DrawProviderSource(source, profile);
                    break;
                case FormulaValueSourceType.NestedFormula:
                    DrawNestedFormula(source);
                    break;
            }
        }

        private static void DrawSourceType(SerializedProperty sourceType)
        {
            var values = (FormulaValueSourceType[])Enum.GetValues(typeof(FormulaValueSourceType));
            var labels = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
                labels[i] = FormulaEditorLabels.SourceTypeName(values[i]);

            sourceType.enumValueIndex = EditorGUILayout.Popup(
                new GUIContent(FormulaEditorLabels.Source, "本步骤读取数值的来源。"),
                sourceType.enumValueIndex,
                labels);
        }

        private static void DrawProviderSource(SerializedProperty source, FormulaEditorProfile profile)
        {
            var providerId = source.FindPropertyRelative(ProviderIdProperty);
            if (profile == null || profile.Providers.Count == 0)
            {
                EditorGUILayout.PropertyField(
                    providerId,
                    new GUIContent(FormulaEditorLabels.Provider, "当前 profile 没有配置 provider，只能手动输入。"));
                return;
            }

            var selectedIndex = FindProviderIndex(profile, providerId.stringValue);
            if (selectedIndex < 0 && !string.IsNullOrEmpty(providerId.stringValue))
            {
                EditorGUILayout.HelpBox($"当前上下文没有注册 provider：{providerId.stringValue}", MessageType.Warning);
                EditorGUILayout.PropertyField(providerId, new GUIContent(FormulaEditorLabels.Provider));
                return;
            }

            if (selectedIndex < 0)
                selectedIndex = 0;

            var labels = new string[profile.Providers.Count];
            for (var i = 0; i < profile.Providers.Count; i++)
                labels[i] = FormulaEditorGUILayout.ProviderDisplayName(profile.Providers[i].Id);

            var nextIndex = EditorGUILayout.Popup(
                new GUIContent(FormulaEditorLabels.Provider, "选择本步骤读取的上下文变量。"),
                selectedIndex,
                labels);
            var descriptor = profile.Providers[nextIndex];
            providerId.stringValue = descriptor.Id;
            DrawProviderParameters(source.FindPropertyRelative(ParametersProperty), descriptor);
            FormulaEditorGUILayout.DrawProviderHelp(descriptor);
        }

        private static void DrawProviderParameters(
            SerializedProperty parameters,
            FormulaProviderDescriptor descriptor)
        {
            if (parameters == null || descriptor == null)
                return;

            foreach (var parameter in descriptor.Parameters)
            {
                var serializedParameter = FindOrCreateParameter(parameters, parameter);
                DrawParameter(serializedParameter, parameter);
            }
        }

        private static SerializedProperty FindOrCreateParameter(
            SerializedProperty parameters,
            FormulaParameterDescriptor descriptor)
        {
            for (var i = 0; i < parameters.arraySize; i++)
            {
                var candidate = parameters.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative(NameProperty).stringValue == descriptor.Key)
                    return candidate;
            }

            parameters.arraySize++;
            var parameter = parameters.GetArrayElementAtIndex(parameters.arraySize - 1);
            InitializeParameter(parameter, descriptor);
            return parameter;
        }

        private static void InitializeParameter(
            SerializedProperty parameter,
            FormulaParameterDescriptor descriptor)
        {
            parameter.FindPropertyRelative(NameProperty).stringValue = descriptor.Key;
            parameter.FindPropertyRelative(TypeProperty).enumValueIndex = ParameterTypeFor(descriptor.Kind);
            parameter.FindPropertyRelative(StringValueProperty).stringValue = descriptor.DefaultStringValue;
            parameter.FindPropertyRelative(IntValueProperty).intValue = descriptor.DefaultIntValue;
            parameter.FindPropertyRelative(FloatValueProperty).floatValue = descriptor.DefaultFloatValue;
            parameter.FindPropertyRelative(BoolValueProperty).boolValue = false;
            parameter.FindPropertyRelative(ObjectValueProperty).objectReferenceValue = null;
        }

        private static void DrawParameter(
            SerializedProperty parameter,
            FormulaParameterDescriptor descriptor)
        {
            parameter.FindPropertyRelative(NameProperty).stringValue = descriptor.Key;
            parameter.FindPropertyRelative(TypeProperty).enumValueIndex = ParameterTypeFor(descriptor.Kind);

            var content = new GUIContent(descriptor.DisplayName, descriptor.Description);
            switch (descriptor.Kind)
            {
                case FormulaEditorParameterKind.String:
                    EditorGUILayout.PropertyField(parameter.FindPropertyRelative(StringValueProperty), content);
                    break;
                case FormulaEditorParameterKind.Int:
                    EditorGUILayout.PropertyField(parameter.FindPropertyRelative(IntValueProperty), content);
                    break;
                case FormulaEditorParameterKind.Float:
                    EditorGUILayout.PropertyField(parameter.FindPropertyRelative(FloatValueProperty), content);
                    break;
                case FormulaEditorParameterKind.Bool:
                    EditorGUILayout.PropertyField(parameter.FindPropertyRelative(BoolValueProperty), content);
                    break;
                case FormulaEditorParameterKind.Object:
                    EditorGUILayout.PropertyField(parameter.FindPropertyRelative(ObjectValueProperty), content);
                    break;
                case FormulaEditorParameterKind.Enum:
                    DrawEnumParameter(parameter.FindPropertyRelative(IntValueProperty), descriptor, content);
                    break;
            }
        }

        private static void DrawEnumParameter(
            SerializedProperty intValue,
            FormulaParameterDescriptor descriptor,
            GUIContent content)
        {
            if (descriptor.EnumType == null || !descriptor.EnumType.IsEnum)
            {
                EditorGUILayout.PropertyField(intValue, content);
                return;
            }

            var values = Enum.GetValues(descriptor.EnumType);
            var names = Enum.GetNames(descriptor.EnumType);
            var selectedIndex = 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (Convert.ToInt32(values.GetValue(i)) == intValue.intValue)
                {
                    selectedIndex = i;
                    break;
                }
            }

            var nextIndex = EditorGUILayout.Popup(content, selectedIndex, names);
            intValue.intValue = Convert.ToInt32(values.GetValue(nextIndex));
        }

        private static void DrawNestedFormula(SerializedProperty source)
        {
            var nested = source.FindPropertyRelative(NestedFormulaProperty);
            nested.objectReferenceValue = EditorGUILayout.ObjectField(
                new GUIContent(FormulaEditorLabels.NestedFormula, "本步骤使用另一个公式的计算结果。"),
                nested.objectReferenceValue,
                typeof(FormulaAsset),
                false);
        }

        private static int FindProviderIndex(FormulaEditorProfile profile, string providerId)
        {
            for (var i = 0; i < profile.Providers.Count; i++)
            {
                if (profile.Providers[i].Id == providerId)
                    return i;
            }

            return -1;
        }

        private static int ParameterTypeFor(FormulaEditorParameterKind kind)
        {
            switch (kind)
            {
                case FormulaEditorParameterKind.String:
                    return (int)FormulaParameterType.String;
                case FormulaEditorParameterKind.Float:
                    return (int)FormulaParameterType.Float;
                case FormulaEditorParameterKind.Bool:
                    return (int)FormulaParameterType.Bool;
                case FormulaEditorParameterKind.Object:
                    return (int)FormulaParameterType.Object;
                case FormulaEditorParameterKind.Int:
                case FormulaEditorParameterKind.Enum:
                default:
                    return (int)FormulaParameterType.Int;
            }
        }

        private void AddDefaultStep()
        {
            serializedObject.ApplyModifiedProperties();

            var formula = (FormulaAsset)target;
            var field = typeof(FormulaAsset).GetField("steps", InstancePrivate);
            var list = field?.GetValue(formula) as List<FormulaStep>;
            if (list == null)
            {
                list = new List<FormulaStep>();
                field?.SetValue(formula, list);
            }

            list.Add(FormulaStep.Create(FormulaOperationType.Add, FormulaValueSource.Constant(0f)));
            EditorUtility.SetDirty(formula);
            serializedObject.Update();
        }
    }
}
