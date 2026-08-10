using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    [ZeroEngine.EditorUI.EditorUiSurface]
    public sealed class FormulaWorkbenchWindow : EditorWindow
    {
        private FormulaAsset formula;
        private FormulaEvaluationReport lastReport;
        private FormulaPreviewBatchReport lastBatchReport;
        private FormulaCurvePreviewReport lastCurveReport;
        private string lastBatchJson = string.Empty;
        private string lastBatchMarkdown = string.Empty;
        private int curveInputIndex;
        private float curveMin;
        private float curveMax = 10f;
        private int curveSamples = 11;
        private Vector2 scrollPosition;
        private readonly FormulaEditorPreviewState previewState = new();
        private readonly FormulaWorkbenchSession session = new();

        [MenuItem("ZeroEngine/Formula/Formula Workbench", priority = 131)]
        private static void Open()
        {
            OpenWithProfile(FormulaEditorProfileRegistry.ActiveProfile);
        }

        public static void OpenWithProfile(FormulaEditorProfile profile)
        {
            Open(profile, null);
        }

        public static void OpenWithFormula(FormulaEditorProfile profile, FormulaAsset selectedFormula)
        {
            Open(profile, selectedFormula);
        }

        private static void Open(FormulaEditorProfile profile, FormulaAsset selectedFormula)
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
            var window = GetWindow<FormulaWorkbenchWindow>(title);
            if (selectedFormula != null)
                window.formula = selectedFormula;
            window.Show();
        }

        private void OnGUI()
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            ZeroEngine.EditorUI.EditorUiGUILayout.Header(
                string.IsNullOrEmpty(profile.WorkbenchTitle) ? FormulaEditorLabels.Workbench : profile.WorkbenchTitle,
                string.IsNullOrEmpty(profile.DefaultSearchRoot)
                    ? $"{profile.DisplayName} ({profile.ProfileId})"
                    : $"{profile.DisplayName} ({profile.ProfileId}) · {FormulaEditorLabels.FormulaRoot}: {profile.DefaultSearchRoot}");

            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.Formula);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            formula = (FormulaAsset)EditorGUILayout.ObjectField(FormulaEditorLabels.Formula, formula, typeof(FormulaAsset), false);
            EditorGUILayout.EndVertical();

            FormulaEditorGUILayout.DrawPreviewInputs(profile, previewState);
            if (GUILayout.Button(FormulaEditorLabels.Evaluate))
                Evaluate(profile);

            if (lastReport == null)
            {
                FormulaEditorGUILayout.DrawReport(null);
                DrawBatchPreview(profile);
                DrawCurvePreview(profile);
                EditorGUILayout.EndScrollView();
                return;
            }

            FormulaEditorGUILayout.DrawReport(lastReport);
            DrawBatchPreview(profile);
            DrawCurvePreview(profile);
            EditorGUILayout.EndScrollView();
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
                previewState.CreateContext(profile),
                FormulaEditorPreview.CreateRegistry(profile),
                out _,
                out lastReport);
        }

        private void DrawBatchPreview(FormulaEditorProfile profile)
        {
            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.PreviewCases);

            for (var index = 0; index < session.PreviewCaseAssets.Count; index++)
            {
                EditorGUILayout.BeginHorizontal();
                var asset = (FormulaPreviewCaseAsset)EditorGUILayout.ObjectField(
                    session.PreviewCaseAssets[index],
                    typeof(FormulaPreviewCaseAsset),
                    false);
                session.SetPreviewCaseAssetAt(index, asset);
                if (GUILayout.Button("-", GUILayout.Width(24f)))
                {
                    session.RemovePreviewCaseAssetAt(index);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(FormulaEditorLabels.AddPreviewCase))
                session.AddPreviewCaseAssetSlot();

            if (GUILayout.Button(FormulaEditorLabels.EvaluatePreviewCases))
            {
                lastBatchReport = session.EvaluateBatch(formula, profile, previewState.ToValueSet(profile));
                lastBatchJson = session.ExportBatchJson(lastBatchReport);
                lastBatchMarkdown = session.ExportBatchMarkdown(lastBatchReport);
            }

            if (lastBatchReport == null)
                return;

            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.PreviewReportJson);
            EditorGUILayout.TextArea(lastBatchJson, GUILayout.MinHeight(48f));
            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.PreviewReportMarkdown);
            EditorGUILayout.TextArea(lastBatchMarkdown, GUILayout.MinHeight(64f));
        }

        private void DrawCurvePreview(FormulaEditorProfile profile)
        {
            if (profile == null || profile.PreviewInputs.Count == 0)
                return;

            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.CurvePreview);

            curveInputIndex = Mathf.Clamp(curveInputIndex, 0, profile.PreviewInputs.Count - 1);
            var inputNames = new string[profile.PreviewInputs.Count];
            for (var index = 0; index < profile.PreviewInputs.Count; index++)
                inputNames[index] = profile.PreviewInputs[index].DisplayName;

            curveInputIndex = EditorGUILayout.Popup(FormulaEditorLabels.CurveInput, curveInputIndex, inputNames);
            EditorGUILayout.MinMaxSlider(FormulaEditorLabels.CurveRange, ref curveMin, ref curveMax, -1000f, 1000f);
            EditorGUILayout.LabelField(FormulaEditorLabels.CurveRange, $"{curveMin:0.###} - {curveMax:0.###}");
            curveSamples = EditorGUILayout.IntSlider(FormulaEditorLabels.CurveSamples, curveSamples, 2, 64);

            if (GUILayout.Button(FormulaEditorLabels.BuildCurve))
            {
                session.SetCurve(profile.PreviewInputs[curveInputIndex].Key, curveMin, curveMax, curveSamples);
                lastCurveReport = session.BuildCurve(formula, profile, previewState.ToValueSet(profile));
            }

            if (lastCurveReport == null)
                return;

            var keys = new Keyframe[lastCurveReport.Points.Count];
            for (var index = 0; index < lastCurveReport.Points.Count; index++)
            {
                var point = lastCurveReport.Points[index];
                keys[index] = new Keyframe(point.Input, point.Result);
            }

            EditorGUILayout.CurveField(FormulaEditorLabels.CurvePreview, new AnimationCurve(keys));
        }
    }
}
