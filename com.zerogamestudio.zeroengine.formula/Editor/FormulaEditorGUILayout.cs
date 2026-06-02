using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    internal static class FormulaEditorGUILayout
    {
        public static void DrawProviderHelp(FormulaProviderDescriptor descriptor)
        {
            if (descriptor == null)
                return;

            EditorGUILayout.HelpBox(
                $"{ProviderLabel(descriptor)}\n{descriptor.Description}",
                MessageType.Info);
        }

        public static void DrawPreviewInputs(FormulaEditorProfile profile, FormulaEditorPreviewState state)
        {
            if (profile == null || state == null || profile.PreviewInputs.Count == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(FormulaEditorLabels.PreviewInputs, EditorStyles.boldLabel);
            foreach (var input in profile.PreviewInputs)
            {
                var value = state.GetValue(input);
                switch (input.Kind)
                {
                    case FormulaPreviewInputKind.Int:
                        state.SetValue(
                            input.Key,
                            EditorGUILayout.IntField(new GUIContent(input.DisplayName, input.Description), Mathf.RoundToInt(value)));
                        break;
                    case FormulaPreviewInputKind.Float:
                        state.SetValue(
                            input.Key,
                            EditorGUILayout.FloatField(new GUIContent(input.DisplayName, input.Description), value));
                        break;
                    case FormulaPreviewInputKind.Bool:
                        state.SetValue(
                            input.Key,
                            EditorGUILayout.Toggle(new GUIContent(input.DisplayName, input.Description), value > 0.5f) ? 1f : 0f);
                        break;
                }
            }
        }

        public static void DrawReport(FormulaEvaluationReport report)
        {
            if (report == null)
                return;

            EditorGUILayout.LabelField(FormulaEditorLabels.Succeeded, report.Succeeded.ToString());
            EditorGUILayout.LabelField(FormulaEditorLabels.Result, report.Result.ToString("0.###"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(FormulaEditorLabels.Diagnostics, EditorStyles.boldLabel);
            foreach (var diagnostic in report.Diagnostics)
            {
                var messageType = diagnostic.Severity == FormulaDiagnosticSeverity.Error
                    ? MessageType.Error
                    : MessageType.Warning;
                EditorGUILayout.HelpBox(diagnostic.Message, messageType);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(FormulaEditorLabels.StepTrace, EditorStyles.boldLabel);
            foreach (var step in report.Steps)
            {
                var operation = FormulaEditorLabels.OperationName(step.Operation);
                var sourceType = FormulaEditorLabels.SourceTypeName(step.SourceType);
                var sourceLabel = string.IsNullOrEmpty(step.SourceLabel)
                    ? sourceType
                    : $"{sourceType} {ProviderDisplayName(step.SourceLabel)}";
                EditorGUILayout.LabelField(
                    $"#{step.StepIndex} {step.InputValue:0.###} {operation} {step.StepValue:0.###} => {step.OutputValue:0.###} (来源: {sourceLabel})");
            }
        }

        public static string ProviderDisplayName(string providerId)
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            return profile.TryGetProvider(providerId, out var descriptor)
                ? ProviderLabel(descriptor)
                : providerId;
        }

        private static string ProviderLabel(FormulaProviderDescriptor descriptor)
        {
            return $"{descriptor.DisplayName} ({descriptor.Id})";
        }
    }
}
