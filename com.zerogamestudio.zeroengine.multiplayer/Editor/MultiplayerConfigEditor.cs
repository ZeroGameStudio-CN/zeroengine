using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Multiplayer.Editor
{
    [CustomEditor(typeof(MultiplayerSessionConfig))]
    public sealed class MultiplayerConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            MultiplayerSessionConfig config = (MultiplayerSessionConfig)target;
            IReadOnlyList<MultiplayerSetupIssue> issues = MultiplayerSetupValidator.ValidateConfig(config);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Configuration values are valid.", MessageType.Info);
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                EditorGUILayout.HelpBox(
                    issues[i].Code + "\n" + issues[i].Message,
                    issues[i].Severity == MultiplayerSetupIssueSeverity.Error
                        ? MessageType.Error
                        : MessageType.Warning);
            }
        }
    }
}
