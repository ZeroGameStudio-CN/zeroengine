using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaWorkbenchWindow : EditorWindow
    {
        private readonly List<string> diagnostics = new();
        private readonly List<string> steps = new();
        private FormulaAsset formula;
        private float result;
        private bool succeeded;

        [MenuItem("ZeroEngine/Formula/Formula Workbench", priority = 131)]
        private static void Open()
        {
            GetWindow<FormulaWorkbenchWindow>("Formula Workbench").Show();
        }

        private void OnGUI()
        {
            formula = (FormulaAsset)EditorGUILayout.ObjectField("Formula", formula, typeof(FormulaAsset), false);
            if (GUILayout.Button("Evaluate"))
                Evaluate();

            EditorGUILayout.LabelField("Succeeded", succeeded.ToString());
            EditorGUILayout.LabelField("Result", result.ToString("0.###"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            foreach (var diagnostic in diagnostics)
                EditorGUILayout.LabelField(diagnostic);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Steps", EditorStyles.boldLabel);
            foreach (var step in steps)
                EditorGUILayout.LabelField(step);
        }

        private void Evaluate()
        {
            diagnostics.Clear();
            steps.Clear();
            if (!formula)
            {
                succeeded = false;
                result = 0f;
                diagnostics.Add("No formula selected.");
                return;
            }

            succeeded = FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out result,
                out var report);

            foreach (var diagnostic in report.Diagnostics)
                diagnostics.Add(diagnostic.ToString());

            foreach (var step in report.Steps)
                steps.Add($"#{step.StepIndex} {step.InputValue} {step.Operation} {step.StepValue} => {step.OutputValue} ({step.SourceType}: {step.SourceLabel})");
        }
    }
}
