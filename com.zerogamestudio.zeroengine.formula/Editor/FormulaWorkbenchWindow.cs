using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    internal enum FormulaStudioPage
    {
        Workbench,
        Catalog
    }

    internal enum FormulaPreviewFieldMode
    {
        Related,
        All
    }

    [ZeroEngine.EditorUI.EditorUiSurface]
    public sealed class FormulaWorkbenchWindow : EditorWindow, ZeroEngine.EditorUI.IEditorWorkspaceEmbeddedView
    {
        private static readonly GUIContent[] PageNames =
        {
            new GUIContent(FormulaEditorLabels.Workbench, FormulaEditorLabels.WorkbenchTooltip),
            new GUIContent(FormulaEditorLabels.CatalogPage, FormulaEditorLabels.CatalogPageTooltip)
        };

        [SerializeField]
        private FormulaAsset formula;
        [SerializeField]
        private List<FormulaAsset> previewFormulas = new();
        [SerializeField]
        private FormulaStudioPage activePage = FormulaStudioPage.Workbench;
        [SerializeField]
        private string workspaceProfileId = string.Empty;
        [System.NonSerialized]
        private FormulaEditorProfile workspaceProfile;
        [SerializeField]
        private int selectedScenarioIndex;
        [SerializeField]
        private string scenarioName = string.Empty;
        [SerializeField]
        private FormulaPreviewFieldMode previewFieldMode = FormulaPreviewFieldMode.Related;
        [System.NonSerialized]
        private List<FormulaEvaluationReport> formulaReports = new();
        [System.NonSerialized]
        private List<bool> formulaStepsExpanded = new();
        [System.NonSerialized]
        private Dictionary<string, bool> scenarioGroupsExpanded = new();
        private Vector2 scrollPosition;
        private readonly FormulaEditorPreviewState previewState = new();
        [System.NonSerialized]
        private FormulaCatalogPane catalogPane;
        [System.NonSerialized]
        private List<FormulaPreviewScenario> savedScenarios = new();
        [System.NonSerialized]
        private string loadedScenarioProfileId = string.Empty;

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

        public static FormulaWorkbenchWindow CreateWorkspaceView(FormulaEditorProfile profile, bool catalog)
        {
            EnsureProfile(profile);
            var view = CreateInstance<FormulaWorkbenchWindow>();
            view.workspaceProfileId = profile?.ProfileId ?? string.Empty;
            view.workspaceProfile = profile;
            view.SetWorkspacePage(catalog);
            return view;
        }

        private static void Open(
            FormulaEditorProfile profile,
            FormulaAsset selectedFormula,
            FormulaStudioPage page)
        {
            EnsureProfile(profile);

            var window = GetWindow<FormulaWorkbenchWindow>(FormulaEditorLabels.Studio);
            window.titleContent = new GUIContent(FormulaEditorLabels.Studio, FormulaEditorLabels.StudioTooltip);
            window.workspaceProfileId = profile?.ProfileId ?? string.Empty;
            window.workspaceProfile = profile;
            if (selectedFormula != null)
            {
                window.formula = selectedFormula;
                window.SetPrimaryFormula(selectedFormula);
            }
            window.activePage = page;
            if (page == FormulaStudioPage.Catalog)
                window.EnsureCatalogPane(true);
            window.Repaint();
            window.Show();
        }

        private static void EnsureProfile(FormulaEditorProfile profile)
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
        }

        private void OnEnable()
        {
            formulaReports = new List<FormulaEvaluationReport>();
            formulaStepsExpanded = new List<bool>();
            scenarioGroupsExpanded = new Dictionary<string, bool>();
            savedScenarios = new List<FormulaPreviewScenario>();
            loadedScenarioProfileId = string.Empty;
            EnsurePrimaryFormulaSlot();
            titleContent = new GUIContent(FormulaEditorLabels.Studio, FormulaEditorLabels.StudioTooltip);
            if (activePage == FormulaStudioPage.Catalog)
                EnsureCatalogPane(true);
        }

        private void OnGUI()
        {
            DrawView();
        }

        public void OnWorkspaceGUI(ZeroEngine.EditorUI.EditorWorkspacePanelContext context)
        {
            DrawView();
        }

        internal void SetWorkspacePage(bool catalog)
        {
            activePage = catalog ? FormulaStudioPage.Catalog : FormulaStudioPage.Workbench;
            if (activePage == FormulaStudioPage.Catalog)
                EnsureCatalogPane(true);
        }

        private void DrawView()
        {
            var profile = ResolveWorkspaceProfile();
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
                catalogPane.Draw(profile, SelectFormulaFromCatalog, Repaint);
                return;
            }

            DrawWorkbench(profile);
        }

        private FormulaEditorProfile ResolveWorkspaceProfile()
        {
            if (workspaceProfile != null
                && string.Equals(workspaceProfile.ProfileId, workspaceProfileId, StringComparison.Ordinal))
                return workspaceProfile;

            if (!string.IsNullOrEmpty(workspaceProfileId))
            {
                foreach (var registeredProfile in FormulaEditorProfileRegistry.RegisteredProfiles)
                {
                    if (registeredProfile.ProfileId == workspaceProfileId)
                    {
                        workspaceProfile = registeredProfile;
                        return registeredProfile;
                    }
                }
            }

            return FormulaEditorProfileRegistry.ActiveProfile;
        }

        private void DrawWorkbench(FormulaEditorProfile profile)
        {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.32f, 96f, 180f);
            scrollPosition = GUILayout.BeginScrollView(
                scrollPosition,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.ExpandWidth(true));
            try
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.MinWidth(0f)))
                {
                    DrawPreviewWorkspace(profile);
                }
            }
            finally
            {
                GUILayout.EndScrollView();
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
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
            SetPrimaryFormula(selectedFormula);
            activePage = FormulaStudioPage.Workbench;
            scrollPosition = Vector2.zero;
            Repaint();
        }

        private void DrawPreviewWorkspace(FormulaEditorProfile profile)
        {
            FormulaEditorGUILayout.DrawSectionHeader(
                FormulaEditorLabels.Scenarios,
                FormulaEditorLabels.ScenarioTooltip);
            DrawScenarioWorkspace(profile);

            FormulaEditorGUILayout.DrawSectionHeader(
                FormulaEditorLabels.PreviewFormulas,
                FormulaEditorLabels.PreviewFormulasTooltip);
            DrawFormulaCards(profile);
        }

        private void DrawScenarioWorkspace(FormulaEditorProfile profile)
        {
            EnsureSavedScenarios(profile);
            var builtInCount = profile?.DefaultPreviewCases.Count ?? 0;
            var options = new string[1 + builtInCount + savedScenarios.Count];
            options[0] = FormulaEditorLabels.CurrentScenario;
            for (var index = 0; index < builtInCount; index++)
                options[index + 1] = FormulaEditorLabels.BuiltInScenarioPrefix + profile.DefaultPreviewCases[index].DisplayName;
            for (var index = 0; index < savedScenarios.Count; index++)
                options[index + 1 + builtInCount] = FormulaEditorLabels.SavedScenarioPrefix + savedScenarios[index].DisplayName;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            selectedScenarioIndex = Mathf.Clamp(selectedScenarioIndex, 0, options.Length - 1);
            EditorGUI.BeginChangeCheck();
            selectedScenarioIndex = EditorGUILayout.Popup(
                new GUIContent(FormulaEditorLabels.Scenario, FormulaEditorLabels.ScenarioTooltip),
                selectedScenarioIndex,
                options,
                GUILayout.ExpandWidth(true));
            var modeLabels = new[]
            {
                new GUIContent(FormulaEditorLabels.RelatedFields, FormulaEditorLabels.RelatedFieldsTooltip),
                new GUIContent(FormulaEditorLabels.AllFields, FormulaEditorLabels.AllFieldsTooltip),
            };
            previewFieldMode = (FormulaPreviewFieldMode)GUILayout.Toolbar(
                (int)previewFieldMode,
                modeLabels,
                GUILayout.Height(22f));
            if (EditorGUI.EndChangeCheck())
                InvalidateAllFormulaReports();

            var fields = FormulaEditorPreview.CollectPreviewFields(
                profile,
                previewFormulas,
                previewFieldMode == FormulaPreviewFieldMode.All);

            if (selectedScenarioIndex == 0)
            {
                EditorGUI.BeginChangeCheck();
                DrawScenarioFields(fields, null, true);
                if (EditorGUI.EndChangeCheck())
                    InvalidateAllFormulaReports();

                scenarioName = EditorGUILayout.TextField(
                    new GUIContent(FormulaEditorLabels.ScenarioName, FormulaEditorLabels.ScenarioNameTooltip),
                    scenarioName);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                FormulaEditorLabels.ResetPreviewInputs,
                                FormulaEditorLabels.ResetPreviewInputsTooltip),
                            GUILayout.ExpandWidth(true)))
                    {
                        previewState.ResetToDefaults(profile, fields);
                        InvalidateAllFormulaReports();
                    }

                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(scenarioName)))
                    {
                        if (GUILayout.Button(
                                new GUIContent(FormulaEditorLabels.SaveScenario, FormulaEditorLabels.SaveScenarioTooltip),
                                GUILayout.ExpandWidth(true)))
                            SaveCurrentScenario(profile, fields);
                    }
                }
            }
            else if (selectedScenarioIndex <= builtInCount)
            {
                DrawScenarioDetails(profile, profile.DefaultPreviewCases[selectedScenarioIndex - 1], fields, false, -1);
            }
            else
            {
                var savedIndex = selectedScenarioIndex - builtInCount - 1;
                DrawScenarioDetails(profile, savedScenarios[savedIndex].CreatePreviewCase(), fields, true, savedIndex);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawScenarioDetails(
            FormulaEditorProfile profile,
            FormulaPreviewCase previewCase,
            IReadOnlyList<FormulaPreviewFieldDescriptor> fields,
            bool canDelete,
            int savedIndex)
        {
            if (previewCase == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(previewCase.DisplayName, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(previewCase.Description))
                EditorGUILayout.LabelField(previewCase.Description, EditorStyles.wordWrappedMiniLabel);

            DrawScenarioFields(fields, previewCase.Values, false);

            if (canDelete && GUILayout.Button(
                    new GUIContent(FormulaEditorLabels.DeleteScenario, FormulaEditorLabels.DeleteScenarioTooltip),
                    GUILayout.ExpandWidth(true)))
                DeleteSavedScenario(profile, savedIndex);
            EditorGUILayout.EndVertical();
        }

        private void DrawScenarioFields(
            IReadOnlyList<FormulaPreviewFieldDescriptor> fields,
            FormulaPreviewValueSet readOnlyValues,
            bool editable)
        {
            if (fields == null || fields.Count == 0)
            {
                EditorGUILayout.HelpBox(FormulaEditorLabels.NoRelatedFields, MessageType.Info);
                return;
            }

            var groups = new Dictionary<string, List<FormulaPreviewFieldDescriptor>>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (!groups.TryGetValue(field.Category, out var group))
                {
                    group = new List<FormulaPreviewFieldDescriptor>();
                    groups.Add(field.Category, group);
                }
                group.Add(field);
            }

            foreach (var group in groups)
            {
                var groupKey = previewFieldMode + ":" + group.Key;
                if (!scenarioGroupsExpanded.TryGetValue(groupKey, out var expanded))
                    expanded = previewFieldMode == FormulaPreviewFieldMode.Related;
                expanded = EditorGUILayout.Foldout(expanded, group.Key, true);
                scenarioGroupsExpanded[groupKey] = expanded;
                if (!expanded)
                    continue;

                EditorGUI.indentLevel++;
                foreach (var field in group.Value)
                {
                    if (editable)
                        DrawEditableScenarioField(field);
                    else
                        DrawReadOnlyScenarioField(field, readOnlyValues);
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawEditableScenarioField(FormulaPreviewFieldDescriptor field)
        {
            var content = new GUIContent(field.DisplayName, field.Description);
            var value = previewState.GetValue(field);
            var compact = IsCompactLayout();
            if (compact)
                EditorGUILayout.LabelField(content, EditorStyles.miniBoldLabel);
            switch (field.Kind)
            {
                case FormulaPreviewInputKind.Int:
                    previewState.SetValue(field.Key, compact
                        ? EditorGUILayout.IntField(Mathf.RoundToInt(value), GUILayout.ExpandWidth(true))
                        : EditorGUILayout.IntField(content, Mathf.RoundToInt(value)));
                    break;
                case FormulaPreviewInputKind.Bool:
                    previewState.SetValue(field.Key, (compact
                        ? EditorGUILayout.Toggle(value > 0.5f, GUILayout.ExpandWidth(true))
                        : EditorGUILayout.Toggle(content, value > 0.5f)) ? 1f : 0f);
                    break;
                case FormulaPreviewInputKind.Float:
                default:
                    previewState.SetValue(field.Key, compact
                        ? EditorGUILayout.FloatField(value, GUILayout.ExpandWidth(true))
                        : EditorGUILayout.FloatField(content, value));
                    break;
            }
        }

        private static void DrawReadOnlyScenarioField(
            FormulaPreviewFieldDescriptor field,
            FormulaPreviewValueSet values)
        {
            var value = field.DefaultValue;
            if (values != null && values.TryGetValue(field.Key, out var storedValue))
                value = storedValue;
            if (IsCompactLayout())
            {
                EditorGUILayout.LabelField(
                    new GUIContent(field.DisplayName, field.Description),
                    EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(value.ToString("0.###"));
            }
            else
            {
                EditorGUILayout.LabelField(
                    new GUIContent(field.DisplayName, field.Description),
                    new GUIContent(value.ToString("0.###")));
            }
        }

        private void EnsureSavedScenarios(FormulaEditorProfile profile)
        {
            var profileId = profile?.ProfileId ?? string.Empty;
            if (loadedScenarioProfileId == profileId && savedScenarios != null)
                return;

            savedScenarios = new List<FormulaPreviewScenario>(FormulaPreviewScenarioStore.Load(profileId));
            loadedScenarioProfileId = profileId;
            selectedScenarioIndex = 0;
        }

        private void SaveCurrentScenario(
            FormulaEditorProfile profile,
            IReadOnlyList<FormulaPreviewFieldDescriptor> fields)
        {
            EnsureSavedScenarios(profile);
            savedScenarios.Add(new FormulaPreviewScenario(
                "local-" + Guid.NewGuid().ToString("N"),
                scenarioName.Trim(),
                previewState.ToValueSet(profile, fields).Values));
            FormulaPreviewScenarioStore.Save(profile?.ProfileId, savedScenarios);
            scenarioName = string.Empty;
            selectedScenarioIndex = 1 + (profile?.DefaultPreviewCases.Count ?? 0) + savedScenarios.Count - 1;
            InvalidateAllFormulaReports();
        }

        private void DeleteSavedScenario(FormulaEditorProfile profile, int savedIndex)
        {
            if (savedIndex < 0 || savedIndex >= savedScenarios.Count)
                return;

            savedScenarios.RemoveAt(savedIndex);
            FormulaPreviewScenarioStore.Save(profile?.ProfileId, savedScenarios);
            selectedScenarioIndex = 0;
            InvalidateAllFormulaReports();
        }

        private void DrawFormulaCards(FormulaEditorProfile profile)
        {
            EnsurePrimaryFormulaSlot();
            EnsureFormulaReportSlots();
            for (var index = 0; index < previewFormulas.Count; index++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (IsCompactLayout())
                    DrawFormulaCardStacked(index, profile);
                else
                    DrawFormulaCardWide(index, profile);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button(
                    new GUIContent(FormulaEditorLabels.AddFormula, FormulaEditorLabels.AddFormulaTooltip),
                    GUILayout.ExpandWidth(true)))
            {
                previewFormulas.Add(null);
                formulaReports.Add(null);
                formulaStepsExpanded.Add(true);
            }
        }

        private void DrawFormulaCardWide(int index, FormulaEditorProfile profile)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(
                           Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.34f, 260f, 480f))))
                    DrawFormulaControls(index, profile);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    DrawFormulaReport(index);
            }
        }

        private void DrawFormulaCardStacked(int index, FormulaEditorProfile profile)
        {
            DrawFormulaControls(index, profile);
            EditorGUILayout.Space(2f);
            DrawFormulaReport(index);
        }

        private void DrawFormulaControls(int index, FormulaEditorProfile profile)
        {
            var content = new GUIContent(
                FormulaEditorLabels.Formula + " " + (index + 1),
                FormulaEditorLabels.FormulaTooltip);
            EditorGUI.BeginChangeCheck();
            var selected = (FormulaAsset)EditorGUILayout.ObjectField(
                content,
                previewFormulas[index],
                typeof(FormulaAsset),
                false,
                GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                previewFormulas[index] = selected;
                if (index == 0)
                    formula = selected;
                formulaReports[index] = null;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(previewFormulas[index] == null))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                FormulaEditorLabels.EvaluatePreviewCases,
                                FormulaEditorLabels.EvaluatePreviewCasesTooltip),
                            GUILayout.ExpandWidth(true)))
                        EvaluateFormula(index, profile);
                }

                if (previewFormulas.Count > 1 && GUILayout.Button(
                        new GUIContent("−", FormulaEditorLabels.RemoveFormulaTooltip),
                        GUILayout.Width(28f)))
                {
                    previewFormulas.RemoveAt(index);
                    formulaReports.RemoveAt(index);
                    formulaStepsExpanded.RemoveAt(index);
                    EnsurePrimaryFormulaSlot();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawFormulaReport(int index)
        {
            var report = formulaReports[index];
            if (report == null)
            {
                EditorGUILayout.HelpBox(FormulaEditorLabels.PendingCalculation, MessageType.Info);
                return;
            }

            var messageType = report.HasErrors || !report.Succeeded
                ? MessageType.Error
                : report.HasWarnings
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(FormulaEditorLabels.CalculationResult, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"{FormulaEditorLabels.Result}: {report.Result:0.###}",
                    EditorStyles.boldLabel,
                    GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(
                    FormulaEditorLabels.EvaluationStatusName(report),
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(72f));
            }
            EditorGUILayout.EndVertical();

            if (report.HasErrors || report.HasWarnings)
                EditorGUILayout.HelpBox(
                    FormulaEditorLabels.EvaluationStatusName(report),
                    messageType);

            foreach (var diagnostic in report.Diagnostics)
            {
                var diagnosticType = diagnostic.Severity == FormulaDiagnosticSeverity.Error
                    ? MessageType.Error
                    : diagnostic.Severity == FormulaDiagnosticSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(diagnostic.Message, diagnosticType);
            }

            formulaStepsExpanded[index] = EditorGUILayout.Foldout(
                formulaStepsExpanded[index],
                $"{FormulaEditorLabels.CalculationSteps} ({report.Steps.Count})",
                true);
            if (!formulaStepsExpanded[index])
                return;

            if (report.Steps.Count == 0)
            {
                EditorGUILayout.LabelField(FormulaEditorLabels.NoStepTrace, EditorStyles.wordWrappedMiniLabel);
                return;
            }

            foreach (var step in report.Steps)
            {
                var sourceLabel = string.IsNullOrEmpty(step.SourceLabel)
                    ? FormulaEditorLabels.SourceTypeName(step.SourceType)
                    : FormulaEditorGUILayout.ProviderDisplayName(step.SourceLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"步骤 {step.StepIndex + 1}",
                        EditorStyles.boldLabel,
                        GUILayout.Width(72f));
                    EditorGUILayout.LabelField(
                        $"{FormulaEditorLabels.StepOutput}: {step.OutputValue:0.###}",
                        EditorStyles.boldLabel,
                        GUILayout.ExpandWidth(true));
                }
                EditorGUILayout.LabelField(
                    $"{FormulaEditorLabels.StepInput}: {step.InputValue:0.###}  " +
                    $"{FormulaEditorLabels.OperationName(step.Operation)}  {step.StepValue:0.###}  " +
                    $"=  {FormulaEditorLabels.StepOutput}: {step.OutputValue:0.###}",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    $"{FormulaEditorLabels.Source}: {sourceLabel}",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
        }

        private void EvaluateFormula(int index, FormulaEditorProfile profile)
        {
            var selectedFormula = previewFormulas[index];
            if (selectedFormula == null)
                return;

            var previewCase = GetSelectedScenario(profile);
            var batch = FormulaPreviewRunner.EvaluateCases(selectedFormula, profile, new[] { previewCase });
            formulaReports[index] = batch.Results.Count == 0 ? null : batch.Results[0].Report;
            formulaStepsExpanded[index] = true;
        }

        private FormulaPreviewCase GetSelectedScenario(FormulaEditorProfile profile)
        {
            EnsureSavedScenarios(profile);
            var builtInCount = profile?.DefaultPreviewCases.Count ?? 0;
            if (selectedScenarioIndex > 0 && selectedScenarioIndex <= builtInCount)
                return profile.DefaultPreviewCases[selectedScenarioIndex - 1];

            if (selectedScenarioIndex > builtInCount)
            {
                var savedIndex = selectedScenarioIndex - builtInCount - 1;
                if (savedIndex >= 0 && savedIndex < savedScenarios.Count)
                    return savedScenarios[savedIndex].CreatePreviewCase();
            }

            var fields = FormulaEditorPreview.CollectPreviewFields(profile, previewFormulas, true);
            return new FormulaPreviewCase(
                FormulaWorkbenchSession.CurrentPreviewCaseId,
                FormulaEditorLabels.CurrentScenario,
                previewState.ToValueSet(profile, fields),
                string.Empty);
        }

        private void EnsureFormulaReportSlots()
        {
            formulaReports ??= new List<FormulaEvaluationReport>();
            formulaStepsExpanded ??= new List<bool>();
            while (formulaReports.Count < previewFormulas.Count)
                formulaReports.Add(null);
            while (formulaReports.Count > previewFormulas.Count)
                formulaReports.RemoveAt(formulaReports.Count - 1);
            while (formulaStepsExpanded.Count < previewFormulas.Count)
                formulaStepsExpanded.Add(true);
            while (formulaStepsExpanded.Count > previewFormulas.Count)
                formulaStepsExpanded.RemoveAt(formulaStepsExpanded.Count - 1);
        }

        private void InvalidateAllFormulaReports()
        {
            EnsureFormulaReportSlots();
            for (var index = 0; index < formulaReports.Count; index++)
                formulaReports[index] = null;
        }

        private void EnsurePrimaryFormulaSlot()
        {
            if (previewFormulas == null)
                previewFormulas = new List<FormulaAsset>();
            if (previewFormulas.Count == 0)
                previewFormulas.Add(formula);
            else if (formula != null && previewFormulas[0] == null)
                previewFormulas[0] = formula;
            else if (previewFormulas[0] != null)
                formula = previewFormulas[0];
        }

        private void SetPrimaryFormula(FormulaAsset selectedFormula)
        {
            EnsurePrimaryFormulaSlot();
            previewFormulas[0] = selectedFormula;
            formula = selectedFormula;
            EnsureFormulaReportSlots();
            formulaReports[0] = null;
        }

        private static bool IsCompactLayout()
        {
            return ZeroEngine.EditorUI.EditorUiGUILayout.ResponsiveMode(EditorGUIUtility.currentViewWidth) ==
                   ZeroEngine.EditorUI.EditorUiResponsiveMode.Compact;
        }

    }
}
