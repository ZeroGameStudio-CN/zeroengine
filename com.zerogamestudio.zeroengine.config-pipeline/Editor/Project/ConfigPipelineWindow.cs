using System;
using System.IO;
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
        private string packageIdentity = "com.zerogamestudio.zeroengine.config-pipeline@2.0.2";
        private string status = "Not checked";
        private Vector2 scroll;

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
            profilePath = EditorGUILayout.TextField("Profile", profilePath);
            configSetId = EditorGUILayout.TextField("Config Set", configSetId);
            packageIdentity = EditorGUILayout.TextField("Package Identity", packageIdentity);
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
        }

        [Serializable]
        private sealed class WorkspaceState
        {
            public string ProfilePath;
            public string ConfigSetId;
            public string PackageIdentity;
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
            }
            catch (Exception exception)
            {
                status = exception.ToString();
            }
        }
    }
}
