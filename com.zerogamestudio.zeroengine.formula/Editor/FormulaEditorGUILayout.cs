using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    internal static class FormulaEditorGUILayout
    {
        private static GUIStyle headerTitleStyle;
        private static GUIStyle headerSubtitleStyle;
        private static GUIStyle sectionTitleStyle;

        public static void DrawHeader(string title, string subtitle, string detail)
        {
            EnsureStyles();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(title) ? FormulaEditorLabels.Formula : title, headerTitleStyle);
            if (!string.IsNullOrWhiteSpace(subtitle))
                EditorGUILayout.LabelField(subtitle, headerSubtitleStyle);
            if (!string.IsNullOrWhiteSpace(detail))
                EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        public static void DrawSectionHeader(string title, string subtitle = null)
        {
            EnsureStyles();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, sectionTitleStyle);
            if (!string.IsNullOrWhiteSpace(subtitle))
                EditorGUILayout.LabelField(subtitle, EditorStyles.miniLabel);
        }

        public static FormulaCatalogWindowFilter DrawCatalogFilter(FormulaCatalogWindowFilter current)
        {
            var values = (FormulaCatalogWindowFilter[])System.Enum.GetValues(typeof(FormulaCatalogWindowFilter));
            var labels = new string[values.Length];
            var selectedIndex = 0;
            for (var index = 0; index < values.Length; index++)
            {
                labels[index] = FormulaEditorLabels.FilterName(values[index]);
                if (values[index] == current)
                    selectedIndex = index;
            }

            var nextIndex = EditorGUILayout.Popup(
                new GUIContent(FormulaEditorLabels.Filter, FormulaEditorLabels.FilterTooltip),
                selectedIndex,
                labels);
            return values[nextIndex];
        }

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

            DrawSectionHeader(FormulaEditorLabels.PreviewInputs);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
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

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent(
                        FormulaEditorLabels.ResetPreviewInputs,
                        FormulaEditorLabels.ResetPreviewInputsTooltip),
                    GUILayout.Width(120f)))
                state.ResetToDefaults(profile);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        public static void DrawReport(FormulaEvaluationReport report)
        {
            DrawSectionHeader(FormulaEditorLabels.PreviewResult, FormulaEditorLabels.EvaluationStatusName(report));
            if (report == null)
            {
                EditorGUILayout.HelpBox(FormulaEditorLabels.PreviewNotRun, MessageType.Info);
                return;
            }

            var statusType = report.HasErrors || !report.Succeeded
                ? MessageType.Error
                : report.HasWarnings
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.HelpBox(
                $"{FormulaEditorLabels.EvaluationStatusName(report)}\n{FormulaEditorLabels.Result}: {report.Result:0.###}",
                statusType);

            DrawSectionHeader(
                FormulaEditorLabels.Diagnostics,
                FormulaEditorLabels.IssueSummary(
                    CountDiagnostics(report, FormulaDiagnosticSeverity.Error),
                    CountDiagnostics(report, FormulaDiagnosticSeverity.Warning),
                    CountDiagnostics(report, FormulaDiagnosticSeverity.Info)));
            if (report.Diagnostics.Count == 0)
            {
                EditorGUILayout.HelpBox(FormulaEditorLabels.NoDiagnostics, MessageType.Info);
            }
            else
            {
                foreach (var diagnostic in report.Diagnostics)
                {
                    var messageType = diagnostic.Severity == FormulaDiagnosticSeverity.Error
                        ? MessageType.Error
                        : diagnostic.Severity == FormulaDiagnosticSeverity.Warning
                            ? MessageType.Warning
                            : MessageType.Info;
                    EditorGUILayout.HelpBox(
                        $"{FormulaEditorLabels.DiagnosticSeverityName(diagnostic.Severity)}: {diagnostic.Message}",
                        messageType);
                }
            }

            DrawSectionHeader(FormulaEditorLabels.StepTrace);
            if (report.Steps.Count == 0)
            {
                EditorGUILayout.HelpBox(FormulaEditorLabels.NoStepTrace, MessageType.Info);
            }
            else
            {
                foreach (var step in report.Steps)
                {
                    var operation = FormulaEditorLabels.OperationName(step.Operation);
                    var sourceType = FormulaEditorLabels.SourceTypeName(step.SourceType);
                    var sourceLabel = string.IsNullOrEmpty(step.SourceLabel)
                        ? sourceType
                        : $"{sourceType} {ProviderDisplayName(step.SourceLabel)}";
                    EditorGUILayout.SelectableLabel(
                        $"#{step.StepIndex}  {step.InputValue:0.###} {operation} {step.StepValue:0.###} => {step.OutputValue:0.###}  来源: {sourceLabel}",
                        EditorStyles.label,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
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

        private static int CountDiagnostics(FormulaEvaluationReport report, FormulaDiagnosticSeverity severity)
        {
            var count = 0;
            foreach (var diagnostic in report.Diagnostics)
            {
                if (diagnostic.Severity == severity)
                    count++;
            }

            return count;
        }

        private static void EnsureStyles()
        {
            if (headerTitleStyle != null)
                return;

            headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                wordWrap = true,
            };
            headerSubtitleStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
            };
            sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true,
            };
        }
    }
}
