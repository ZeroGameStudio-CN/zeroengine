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
