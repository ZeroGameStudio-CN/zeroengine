using UnityEditor;
using UnityEngine;
using ZeroEngine.Dialog;

namespace ZeroEngine.Editor.Dialog
{
    [CustomEditor(typeof(DialogDebugger))]
    public class DialogDebuggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var script = (DialogDebugger)target;

            GUILayout.Space(10);
            GUILayout.Label("Controls", EditorStyles.boldLabel);

            if (GUILayout.Button("Start SO Dialogue"))
            {
                script.StartSO();
            }

            if (GUILayout.Button("Start Graph Dialogue"))
            {
                script.StartGraph();
            }

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Next / Skip Typewriter"))
            {
                script.Next();
            }

            if (GUILayout.Button("Stop"))
            {
                script.Stop();
            }
            
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.Label("Choice Simulation", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++)
            {
                if (GUILayout.Button($"{i}"))
                {
                    script.Choose(i);
                }
            }
            GUILayout.EndHorizontal();
        }
    }
}
