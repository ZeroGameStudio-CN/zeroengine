using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    [CustomEditor(typeof(FormulaAsset))]
    public sealed class FormulaAssetInspector : UnityEditor.Editor
    {
        private SerializedProperty initialValue;
        private SerializedProperty steps;
        private FormulaEvaluationReport lastReport;

        private void OnEnable()
        {
            initialValue = serializedObject.FindProperty("initialValue");
            steps = serializedObject.FindProperty("steps");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField(FormulaEditorLabels.Formula, target.name, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(initialValue, new GUIContent(FormulaEditorLabels.InitialValue, "公式开始计算时使用的基础值。"));
            EditorGUILayout.PropertyField(steps, new GUIContent(FormulaEditorLabels.Steps, "按顺序执行的公式步骤。"), true);
            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button(FormulaEditorLabels.Evaluate))
            {
                FormulaEvaluator.TryEvaluate(
                    (FormulaAsset)target,
                    FormulaDictionaryEvaluationContext.Empty,
                    FormulaProviderRegistry.Empty,
                    out _,
                    out lastReport);
            }

            FormulaEditorGUILayout.DrawReport(lastReport);
        }
    }
}
