using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.TCE.Editor
{
    [ZeroEngine.EditorUI.EditorUiSurface]
    public sealed class TceEditorWindow : EditorWindow
    {
        private TceGraphAsset asset;
        private SerializedObject serializedAsset;
        private Vector2 scroll;
        private string paletteSearch = string.Empty;
        private TceGraphLane selectedLane = TceGraphLane.Trigger;
        private int selectedIndex = -1;
        private TcePreviewResult lastPreviewResult;

        [MenuItem("ZGS/ZeroEngine/TCE/Graph Editor")]
        public static void OpenMenu()
        {
            Open(Selection.activeObject as TceGraphAsset);
        }

        public static void Open(TceGraphAsset graphAsset)
        {
            var window = GetWindow<TceEditorWindow>("TCE Graph");
            window.SetAsset(graphAsset);
            window.Show();
        }

        private void SetAsset(TceGraphAsset graphAsset)
        {
            if (asset != graphAsset)
                lastPreviewResult = null;

            asset = graphAsset;
            serializedAsset = asset == null ? null : new SerializedObject(asset);
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header(
                "TCE Graph",
                "Compose triggers, conditions, effects, and preview execution");
            if (asset == null)
            {
                EditorGUILayout.HelpBox("Select a TceGraphAsset and reopen this window.", MessageType.Info);
                return;
            }

            if (serializedAsset == null || serializedAsset.targetObject != asset)
                serializedAsset = new SerializedObject(asset);

            if (TceGraphAssetMigration.MigrateToCurrent(asset))
                serializedAsset = new SerializedObject(asset);

            serializedAsset.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.PropertyField(serializedAsset.FindProperty(TceGraphSerializedAccess.DisplayNameProperty));
            EditorGUILayout.PropertyField(serializedAsset.FindProperty(TceGraphSerializedAccess.CategoryProperty));
            EditorGUILayout.PropertyField(serializedAsset.FindProperty(TceGraphSerializedAccess.DescriptionProperty));
            DrawPalette();
            DrawLane("Triggers", TceGraphLane.Trigger, TceGraphSerializedAccess.GetLane(serializedAsset, TceGraphLane.Trigger));
            DrawLane("Conditions", TceGraphLane.Condition, TceGraphSerializedAccess.GetLane(serializedAsset, TceGraphLane.Condition));
            DrawLane("Effects", TceGraphLane.Effect, TceGraphSerializedAccess.GetLane(serializedAsset, TceGraphLane.Effect));
            EditorGUILayout.HelpBox(TceNodeInspector.BuildSummary(GetSelectedData()), MessageType.Info);
            TceNodeInspector.DrawSelected(GetSelectedProperty());
            serializedAsset.ApplyModifiedProperties();
            DrawValidationIssues(TceGraphAssetValidator.Validate(asset));
            if (GUILayout.Button("Run Preview"))
                lastPreviewResult = TcePreviewRunner.Run(asset, TcePreviewInput.Default);

            DrawPreviewResult();
            EditorGUILayout.EndScrollView();
        }

        private void DrawPalette()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Component Palette", EditorStyles.boldLabel);
            paletteSearch = EditorGUILayout.TextField("Search", paletteSearch);

            foreach (TceComponentPaletteItem item in TceComponentPalette.Search(paletteSearch))
            {
                if (GUILayout.Button($"+ {item.Label}"))
                {
                    MutateGraph(() =>
                    {
                        TceGraphLaneModel.AddComponent(asset.Graph, item.Lane, TceComponentPalette.CreateData(item));
                        selectedLane = item.Lane;
                        selectedIndex = TceGraphLaneModel.Count(asset.Graph, item.Lane) - 1;
                    });
                    return;
                }
            }
        }

        private void DrawValidationIssues(IReadOnlyList<TceValidationIssue> issues)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            if (issues == null || issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Validation passed.", MessageType.Info);
                return;
            }

            foreach (TceValidationIssue issue in issues)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox($"{issue.Code} {issue.Path}: {issue.Message}", MessageType.Error);
                if (TceValidationPanel.TryGetFocus(issue, out TceGraphLane lane, out int index, out _) &&
                    GUILayout.Button("Focus", GUILayout.Width(64)))
                {
                    selectedLane = lane;
                    selectedIndex = index;
                    Repaint();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPreviewResult()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            if (lastPreviewResult == null)
            {
                EditorGUILayout.HelpBox("Run Preview to evaluate the graph.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(lastPreviewResult.Summary, lastPreviewResult.Executed ? MessageType.Info : MessageType.Warning);

            foreach (TceValidationIssue issue in lastPreviewResult.Issues)
                EditorGUILayout.HelpBox($"{issue.Code} {issue.Path}: {issue.Message}", MessageType.Error);

            foreach (string log in lastPreviewResult.Logs)
                EditorGUILayout.LabelField(log);
        }

        private void DrawLane(string label, TceGraphLane laneId, SerializedProperty lane)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            if (lane == null)
            {
                EditorGUILayout.HelpBox($"{label} lane is unavailable.", MessageType.Error);
                return;
            }

            for (int i = 0; i < lane.arraySize; i++)
            {
                SerializedProperty element = lane.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();

                bool selected = selectedLane == laneId && selectedIndex == i;
                if (GUILayout.Toggle(selected, GetElementLabel(element, i), "Button"))
                {
                    selectedLane = laneId;
                    selectedIndex = i;
                }

                using (new EditorGUI.DisabledScope(i == 0))
                {
                    if (GUILayout.Button("Up", GUILayout.Width(42)))
                    {
                        MutateGraph(() =>
                        {
                            TceGraphLaneModel.Move(asset.Graph, laneId, i, i - 1);
                            selectedLane = laneId;
                            selectedIndex = i - 1;
                        });
                        EditorGUILayout.EndHorizontal();
                        return;
                    }
                }

                using (new EditorGUI.DisabledScope(i >= lane.arraySize - 1))
                {
                    if (GUILayout.Button("Down", GUILayout.Width(54)))
                    {
                        MutateGraph(() =>
                        {
                            TceGraphLaneModel.Move(asset.Graph, laneId, i, i + 1);
                            selectedLane = laneId;
                            selectedIndex = i + 1;
                        });
                        EditorGUILayout.EndHorizontal();
                        return;
                    }
                }

                if (GUILayout.Button("Remove", GUILayout.Width(72)))
                {
                    MutateGraph(() =>
                    {
                        TceGraphLaneModel.Remove(asset.Graph, laneId, i);
                        selectedIndex = -1;
                    });
                    EditorGUILayout.EndHorizontal();
                    return;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private SerializedProperty GetSelectedProperty()
        {
            if (selectedIndex < 0)
                return null;

            SerializedProperty lane = TceGraphSerializedAccess.GetLane(serializedAsset, selectedLane);
            if (lane == null || selectedIndex >= lane.arraySize)
                return null;

            return lane.GetArrayElementAtIndex(selectedIndex);
        }

        private TceComponentData GetSelectedData()
        {
            if (asset == null || selectedIndex < 0)
                return null;

            if (selectedIndex >= TceGraphLaneModel.Count(asset.Graph, selectedLane))
                return null;

            return TceGraphLaneModel.GetComponent(asset.Graph, selectedLane, selectedIndex);
        }

        private void MutateGraph(Action action)
        {
            serializedAsset.ApplyModifiedProperties();
            Undo.RecordObject(asset, "Edit TCE Graph");
            action();
            lastPreviewResult = null;
            EditorUtility.SetDirty(asset);
            serializedAsset.Update();
            Repaint();
        }

        private static string GetElementLabel(SerializedProperty element, int index)
        {
            string typeName = element.managedReferenceFullTypename;
            if (string.IsNullOrEmpty(typeName))
                return $"#{index}";

            int space = typeName.LastIndexOf(' ');
            if (space >= 0)
                typeName = typeName.Substring(space + 1);

            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }
    }
}
