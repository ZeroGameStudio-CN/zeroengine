using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigPipelineWindow : EditorWindow
    {
        private string profilePath = "Config/config-project.json";
        private string configSetId = string.Empty;
        private string packageIdentity = "com.zerogamestudio.zeroengine.config-pipeline@1.0.0";
        private string status = "Not checked";
        private Vector2 scroll;

        [MenuItem("ZGS/Config Pipeline")]
        public static void Open()
        {
            GetWindow<ConfigPipelineWindow>("Config Pipeline");
        }

        private void OnGUI()
        {
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
