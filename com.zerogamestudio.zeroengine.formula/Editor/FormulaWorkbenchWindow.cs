using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaWorkbenchWindow : EditorWindow
    {
        private FormulaAsset formula;
        private FormulaEvaluationReport lastReport;

        [MenuItem("ZeroEngine/Formula/Formula Workbench", priority = 131)]
        private static void Open()
        {
            OpenWithProfile(FormulaEditorProfileRegistry.ActiveProfile);
        }

        public static void OpenWithProfile(FormulaEditorProfile profile)
        {
            if (profile != null)
            {
                var registered = false;
                foreach (var registeredProfile in FormulaEditorProfileRegistry.RegisteredProfiles)
                {
                    if (registeredProfile.ProfileId == profile.ProfileId)
                    {
                        registered = true;
                        break;
                    }
                }

                if (!registered)
                    FormulaEditorProfileRegistry.Register(profile);

                FormulaEditorProfileRegistry.SetActiveProfile(profile.ProfileId);
            }

            var activeProfile = FormulaEditorProfileRegistry.ActiveProfile;
            var title = string.IsNullOrEmpty(activeProfile.WorkbenchTitle)
                ? activeProfile.DisplayName
                : activeProfile.WorkbenchTitle;
            GetWindow<FormulaWorkbenchWindow>(title).Show();
        }

        private void OnGUI()
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            EditorGUILayout.LabelField("配置", $"{profile.DisplayName} ({profile.ProfileId})");

            formula = (FormulaAsset)EditorGUILayout.ObjectField(FormulaEditorLabels.Formula, formula, typeof(FormulaAsset), false);
            if (GUILayout.Button(FormulaEditorLabels.Evaluate))
                Evaluate(profile);

            if (lastReport == null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(FormulaEditorLabels.Diagnostics, EditorStyles.boldLabel);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(FormulaEditorLabels.StepTrace, EditorStyles.boldLabel);
                return;
            }

            FormulaEditorGUILayout.DrawReport(lastReport);
        }

        private void Evaluate(FormulaEditorProfile profile)
        {
            _ = profile;

            if (!formula)
            {
                lastReport = new FormulaEvaluationReport(null, "<null>");
                lastReport.SetResult(0f, false);
                lastReport.AddDiagnostic(FormulaDiagnosticSeverity.Error, FormulaDiagnosticCode.NullFormula, "未选择公式。");
                return;
            }

            FormulaEvaluator.TryEvaluate(
                formula,
                FormulaDictionaryEvaluationContext.Empty,
                FormulaProviderRegistry.Empty,
                out _,
                out lastReport);
        }
    }
}
