using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.EditorUI;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    [ZeroEngine.EditorUI.EditorUiSurface]
    public sealed class ConfigPipelineWindow : EditorWindow, IEditorWorkspaceEmbeddedView, IEditorWorkspaceStatefulView
    {
        private string profilePath = "Config/config-project.json";
        private string configSetId = string.Empty;
        private string packageIdentity = "com.zerogamestudio.zeroengine.config-pipeline@2.3.0";
        private string status = "Not checked";
        private string effectiveValueFilter = string.Empty;
        private Vector2 scroll;
        private Vector2 effectiveValueScroll;
        private ConfigPipelinePreparedPlan preparedPlan;
        private ConfigEffectiveValue selectedEffectiveValue;
        private ConfigPresetResetPreview resetPreview;

        public static void Open()
        {
            GetWindow<ConfigPipelineWindow>("Config Pipeline");
        }

        private void OnGUI()
        {
            DrawContent(true);
        }

        public void OnWorkspaceGUI(EditorWorkspacePanelContext context)
        {
            DrawContent(false);
        }

        public string CaptureWorkspaceState()
        {
            return JsonUtility.ToJson(new WorkspaceState
            {
                ProfilePath = profilePath,
                ConfigSetId = configSetId,
                PackageIdentity = packageIdentity,
                EffectiveValueFilter = effectiveValueFilter,
                Scroll = scroll
            });
        }

        public void RestoreWorkspaceState(string state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return;
            }

            var restored = JsonUtility.FromJson<WorkspaceState>(state);
            if (restored == null)
            {
                return;
            }

            profilePath = restored.ProfilePath ?? profilePath;
            configSetId = restored.ConfigSetId ?? configSetId;
            packageIdentity = restored.PackageIdentity ?? packageIdentity;
            effectiveValueFilter = restored.EffectiveValueFilter ?? effectiveValueFilter;
            scroll = restored.Scroll;
        }

        private void DrawContent(bool drawHeader)
        {
            if (drawHeader)
            {
                EditorUiGUILayout.Header(
                    "Config Pipeline",
                    "Plan, validate, apply, and export project configuration");
            }
            EditorGUI.BeginChangeCheck();
            profilePath = EditorGUILayout.TextField("Profile", profilePath);
            configSetId = EditorGUILayout.TextField("Config Set", configSetId);
            packageIdentity = EditorGUILayout.TextField("Package Identity", packageIdentity);
            if (EditorGUI.EndChangeCheck())
            {
                ClearPreparedState();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Plan")) Run(ConfigPipelineMode.Plan);
                if (GUILayout.Button("Check")) Run(ConfigPipelineMode.Check);
                if (GUILayout.Button("Apply")) Run(ConfigPipelineMode.Apply);
            }

            if (GUILayout.Button("Export JSON Candidate"))
            {
                string directory = EditorUtility.OpenFolderPanel("Candidate output", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(directory))
                {
                    Run(ConfigPipelineMode.ExportCandidate, directory, "client");
                }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox(status, MessageType.Info);
            EditorGUILayout.EndScrollView();
            DrawEffectiveValues();
        }

        [Serializable]
        private sealed class WorkspaceState
        {
            public string ProfilePath;
            public string ConfigSetId;
            public string PackageIdentity;
            public string EffectiveValueFilter;
            public Vector2 Scroll;
        }

        private void Run(ConfigPipelineMode mode, string candidateDirectory = null, string scope = null)
        {
            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                ConfigPipelineCommandResult result = ConfigPipelineBatch.Run(
                    root,
                    profilePath,
                    configSetId,
                    packageIdentity,
                    mode,
                    candidateDirectory,
                    scope);
                status = result.Summary + Environment.NewLine +
                         System.Text.Encoding.UTF8.GetString(result.MachineJson);
                AssetDatabase.Refresh();
                LoadPreparedPlan(root);
            }
            catch (Exception exception)
            {
                ClearPreparedState();
                status = exception.ToString();
            }
        }

        private void LoadPreparedPlan(string root)
        {
            preparedPlan = new ConfigPipelineService().Plan(
                root,
                profilePath,
                configSetId,
                packageIdentity);
            selectedEffectiveValue = null;
            resetPreview = null;
        }

        private void ClearPreparedState()
        {
            preparedPlan = null;
            selectedEffectiveValue = null;
            resetPreview = null;
        }

        private void DrawEffectiveValues()
        {
            if (preparedPlan == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Effective Values", EditorStyles.boldLabel);
            effectiveValueFilter = EditorGUILayout.TextField("Filter", effectiveValueFilter);
            List<ConfigEffectiveValue> visible = preparedPlan.EffectiveValues
                .Where(MatchesFilter)
                .Take(200)
                .ToList();
            EditorGUILayout.LabelField(
                visible.Count == preparedPlan.EffectiveValues.Count
                    ? visible.Count + " fields"
                    : visible.Count + " shown / " + preparedPlan.EffectiveValues.Count + " fields",
                EditorStyles.miniLabel);
            effectiveValueScroll = EditorGUILayout.BeginScrollView(
                effectiveValueScroll,
                GUILayout.MinHeight(120f),
                GUILayout.MaxHeight(280f));
            foreach (ConfigEffectiveValue value in visible)
            {
                string label = value.SourceKind + "  " + value.JsonPath + " = " + value.CanonicalValue;
                if (GUILayout.Button(label, EditorStyles.miniButton))
                {
                    selectedEffectiveValue = value;
                    resetPreview = null;
                }
            }

            EditorGUILayout.EndScrollView();
            DrawSelectedEffectiveValue();
        }

        private bool MatchesFilter(ConfigEffectiveValue value)
        {
            if (string.IsNullOrWhiteSpace(effectiveValueFilter))
            {
                return true;
            }

            return Contains(value.JsonPath, effectiveValueFilter) ||
                   Contains(value.CanonicalValue, effectiveValueFilter) ||
                   Contains(value.SourceKind.ToString(), effectiveValueFilter) ||
                   Contains(value.Workbook, effectiveValueFilter) ||
                   Contains(value.Sheet, effectiveValueFilter);
        }

        private void DrawSelectedEffectiveValue()
        {
            if (selectedEffectiveValue == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Field", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                selectedEffectiveValue.ArtifactPath + " " + selectedEffectiveValue.JsonPath,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField("Value", selectedEffectiveValue.CanonicalValue);
            EditorGUILayout.LabelField("Source", selectedEffectiveValue.SourceKind.ToString());
            EditorGUILayout.LabelField("Source JSON Path", selectedEffectiveValue.SourceJsonPath);
            EditorGUILayout.LabelField("Schema Path", selectedEffectiveValue.SchemaPath);
            EditorGUILayout.LabelField(
                "Workbook Cell",
                string.IsNullOrEmpty(selectedEffectiveValue.Workbook)
                    ? "(none)"
                    : selectedEffectiveValue.Workbook + " / " + selectedEffectiveValue.Sheet +
                      " / R" + selectedEffectiveValue.Row + "C" + selectedEffectiveValue.Column);

            if (!selectedEffectiveValue.HasEditableInstanceCell)
            {
                EditorGUILayout.HelpBox(
                    "Reset to Preset is available only for a concrete instance override cell.",
                    MessageType.None);
                return;
            }

            if (GUILayout.Button("Preview Reset to Preset"))
            {
                PreviewPresetReset();
            }

            if (resetPreview == null)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "Current: " + resetPreview.CurrentCanonicalValue + Environment.NewLine +
                "Inherited preset: " + resetPreview.InheritedCanonicalValue + Environment.NewLine +
                "Workbook: " + resetPreview.Workbook + " / " + resetPreview.Sheet +
                " / R" + resetPreview.Row + "C" + resetPreview.Column + Environment.NewLine +
                "Reset Plan: " + resetPreview.ResetPlanId,
                MessageType.Warning);
            if (GUILayout.Button("Apply Reset to Preset"))
            {
                ApplyPresetReset();
            }
        }

        private void PreviewPresetReset()
        {
            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                resetPreview = new ConfigPipelineService().PlanPresetReset(
                    root,
                    profilePath,
                    configSetId,
                    packageIdentity,
                    selectedEffectiveValue.ArtifactPath,
                    selectedEffectiveValue.JsonPath);
                status = "Reset preview prepared. Review the inherited value and exact workbook cell.";
            }
            catch (Exception exception)
            {
                resetPreview = null;
                status = exception.ToString();
            }
        }

        private void ApplyPresetReset()
        {
            if (!EditorUtility.DisplayDialog(
                    "Apply Reset to Preset",
                    "Clear the selected instance cell and regenerate all configuration artifacts " +
                    "transactionally?",
                    "Apply",
                    "Cancel"))
            {
                return;
            }

            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                ConfigApplyResult result = new ConfigPipelineService().ApplyExpectedPresetReset(
                    root,
                    profilePath,
                    configSetId,
                    packageIdentity,
                    resetPreview.TargetArtifactPath,
                    resetPreview.JsonPath,
                    resetPreview.SourcePlanId,
                    resetPreview.ResetPlanId);
                AssetDatabase.Refresh();
                status = "Reset applied transactionally. Plan " + result.PlanId +
                         ", changed files " + result.ChangedFileCount + ".";
                LoadPreparedPlan(root);
            }
            catch (Exception exception)
            {
                status = exception.ToString();
            }
        }

        private static bool Contains(string value, string filter)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
