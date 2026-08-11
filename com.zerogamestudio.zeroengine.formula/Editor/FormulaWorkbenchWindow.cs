using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    internal enum FormulaStudioPage
    {
        Workbench,
        Catalog
    }

    [ZeroEngine.EditorUI.EditorUiSurface]
    public sealed class FormulaWorkbenchWindow : EditorWindow
    {
        private static readonly GUIContent[] PageNames =
        {
            new GUIContent(FormulaEditorLabels.Workbench, FormulaEditorLabels.WorkbenchTooltip),
            new GUIContent(FormulaEditorLabels.CatalogPage, FormulaEditorLabels.CatalogPageTooltip)
        };

        [SerializeField]
        private FormulaAsset formula;
        [SerializeField]
        private FormulaStudioPage activePage = FormulaStudioPage.Workbench;
        [System.NonSerialized]
        private FormulaEvaluationReport lastReport;
        [System.NonSerialized]
        private FormulaPreviewBatchReport lastBatchReport;
        [System.NonSerialized]
        private FormulaCurvePreviewReport lastCurveReport;
        [System.NonSerialized]
        private string lastBatchJson = string.Empty;
        [System.NonSerialized]
        private string lastBatchMarkdown = string.Empty;
        private int curveInputIndex;
        private float curveMin;
        private float curveMax = 10f;
        private int curveSamples = 11;
        private Vector2 scrollPosition;
        private readonly FormulaEditorPreviewState previewState = new();
        private readonly FormulaWorkbenchSession session = new();
        [System.NonSerialized]
        private FormulaCatalogPane catalogPane;

        public static void OpenWithProfile(FormulaEditorProfile profile)
        {
            Open(profile, null, FormulaStudioPage.Workbench);
        }

        public static void OpenWithFormula(FormulaEditorProfile profile, FormulaAsset selectedFormula)
        {
            Open(profile, selectedFormula, FormulaStudioPage.Workbench);
        }

        public static void OpenCatalogWithProfile(FormulaEditorProfile profile)
        {
            Open(profile, null, FormulaStudioPage.Catalog);
        }

        private static void Open(
            FormulaEditorProfile profile,
            FormulaAsset selectedFormula,
            FormulaStudioPage page)
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

            var window = GetWindow<FormulaWorkbenchWindow>(FormulaEditorLabels.Studio);
            window.titleContent = new GUIContent(FormulaEditorLabels.Studio, FormulaEditorLabels.StudioTooltip);
            if (selectedFormula != null)
                window.formula = selectedFormula;
            window.activePage = page;
            if (page == FormulaStudioPage.Catalog)
                window.EnsureCatalogPane(true);
            window.Repaint();
            window.Show();
        }

        private void OnEnable()
        {
            lastReport = null;
            lastBatchReport = null;
            lastCurveReport = null;
            lastBatchJson = string.Empty;
            lastBatchMarkdown = string.Empty;
            titleContent = new GUIContent(FormulaEditorLabels.Studio, FormulaEditorLabels.StudioTooltip);
            if (activePage == FormulaStudioPage.Catalog)
                EnsureCatalogPane(true);
        }

        private void OnGUI()
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            ZeroEngine.EditorUI.EditorUiGUILayout.Header(
                FormulaEditorLabels.Studio,
                string.IsNullOrEmpty(profile.DefaultSearchRoot)
                    ? $"{profile.DisplayName} ({profile.ProfileId})"
                    : $"{profile.DisplayName} ({profile.ProfileId}) · {FormulaEditorLabels.FormulaRoot}: {profile.DefaultSearchRoot}");

            FormulaStudioPage previous = activePage;
            activePage = (FormulaStudioPage)GUILayout.Toolbar(
                (int)activePage,
                PageNames,
                GUILayout.Height(24f));
            if (activePage == FormulaStudioPage.Catalog)
            {
                EnsureCatalogPane(previous != FormulaStudioPage.Catalog);
                catalogPane.Draw(profile, SelectFormulaFromCatalog);
                return;
            }

            DrawWorkbench(profile);
        }

        private void DrawWorkbench(FormulaEditorProfile profile)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.Formula);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            formula = (FormulaAsset)EditorGUILayout.ObjectField(
                new GUIContent(FormulaEditorLabels.Formula, FormulaEditorLabels.FormulaTooltip),
                formula,
                typeof(FormulaAsset),
                false);
            EditorGUILayout.EndVertical();

            FormulaEditorGUILayout.DrawPreviewInputs(profile, previewState);
            if (GUILayout.Button(new GUIContent(FormulaEditorLabels.Evaluate, FormulaEditorLabels.EvaluateTooltip)))
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

        private void EnsureCatalogPane(bool refresh)
        {
            if (catalogPane == null)
                catalogPane = new FormulaCatalogPane();
            if (refresh)
                catalogPane.RefreshRows();
        }

        private void SelectFormulaFromCatalog(FormulaAsset selectedFormula)
        {
            formula = selectedFormula;
            activePage = FormulaStudioPage.Workbench;
            scrollPosition = Vector2.zero;
            Repaint();
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
                    new GUIContent("样例 " + (index + 1), FormulaEditorLabels.PreviewCaseTooltip),
                    session.PreviewCaseAssets[index],
                    typeof(FormulaPreviewCaseAsset),
                    false);
                session.SetPreviewCaseAssetAt(index, asset);
                if (GUILayout.Button(
                        new GUIContent("−", FormulaEditorLabels.RemovePreviewCaseTooltip),
                        GUILayout.Width(24f)))
                {
                    session.RemovePreviewCaseAssetAt(index);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(new GUIContent(
                    FormulaEditorLabels.AddPreviewCase,
                    FormulaEditorLabels.AddPreviewCaseTooltip)))
                session.AddPreviewCaseAssetSlot();

            if (GUILayout.Button(new GUIContent(
                    FormulaEditorLabels.EvaluatePreviewCases,
                    FormulaEditorLabels.EvaluatePreviewCasesTooltip)))
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

            curveInputIndex = EditorGUILayout.Popup(
                new GUIContent(FormulaEditorLabels.CurveInput, FormulaEditorLabels.CurveInputTooltip),
                curveInputIndex,
                inputNames);
            EditorGUILayout.MinMaxSlider(
                new GUIContent(FormulaEditorLabels.CurveRange, FormulaEditorLabels.CurveRangeTooltip),
                ref curveMin,
                ref curveMax,
                -1000f,
                1000f);
            EditorGUILayout.LabelField(
                new GUIContent(FormulaEditorLabels.CurveRange, FormulaEditorLabels.CurveRangeTooltip),
                new GUIContent($"{curveMin:0.###} - {curveMax:0.###}"));
            curveSamples = EditorGUILayout.IntSlider(
                new GUIContent(FormulaEditorLabels.CurveSamples, FormulaEditorLabels.CurveSamplesTooltip),
                curveSamples,
                2,
                64);

            if (GUILayout.Button(new GUIContent(
                    FormulaEditorLabels.BuildCurve,
                    FormulaEditorLabels.BuildCurveTooltip)))
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

            EditorGUILayout.CurveField(
                new GUIContent(FormulaEditorLabels.CurvePreview, FormulaEditorLabels.BuildCurveTooltip),
                new AnimationCurve(keys));
        }
    }
}
