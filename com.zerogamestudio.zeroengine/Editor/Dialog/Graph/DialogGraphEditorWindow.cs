using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ZeroEngine.Dialog;

namespace ZeroEngine.Editor.Dialog
{
    /// <summary>
    /// Main editor window for DialogGraphSO visual editing.
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class DialogGraphEditorWindow : EditorWindow
    {
        private DialogGraphView _graphView;
        private DialogGraphSO _currentGraph;
        private Label _titleLabel;

        public static void Open()
        {
            var window = GetWindow<DialogGraphEditorWindow>();
            window.titleContent = new GUIContent("Dialog Graph", EditorGUIUtility.IconContent("d_ScriptableObject Icon").image);
            window.minSize = new Vector2(800, 600);
        }

        /// <summary>
        /// Open a specific graph for editing.
        /// </summary>
        public static void Open(DialogGraphSO graph)
        {
            var window = GetWindow<DialogGraphEditorWindow>();
            window.titleContent = new GUIContent("Dialog Graph", EditorGUIUtility.IconContent("d_ScriptableObject Icon").image);
            window.minSize = new Vector2(800, 600);
            window.LoadGraph(graph);
        }

        /// <summary>
        /// Double-click on DialogGraphSO to open in editor.
        /// </summary>
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable CS0618 // InstanceIDToObject is deprecated in Unity 6, but we keep it for compatibility
            var asset = EditorUtility.InstanceIDToObject(instanceId) as DialogGraphSO;
#pragma warning restore CS0618
            if (asset != null)
            {
                Open(asset);
                return true;
            }
            return false;
        }

        private void OnEnable()
        {
            BuildUI();

            // Restore last edited graph
            if (_currentGraph == null)
            {
                string lastPath = EditorPrefs.GetString("ZeroEngine_LastDialogGraph", "");
                if (!string.IsNullOrEmpty(lastPath))
                {
                    var graph = AssetDatabase.LoadAssetAtPath<DialogGraphSO>(lastPath);
                    if (graph != null)
                    {
                        LoadGraph(graph);
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (_graphView != null)
            {
                _graphView.SaveGraphPositions();
            }
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            ZeroEngine.EditorUI.EditorUiElements.ApplyWindowRoot(rootVisualElement);
            rootVisualElement.Add(ZeroEngine.EditorUI.EditorUiElements.CreateHeader(
                "Dialog Graph",
                "Compose, validate, and save branching dialog"));

            // Toolbar
            var toolbar = new Toolbar();

            // File menu
            var fileButton = new ToolbarButton(() => ShowFileMenu()) { text = "File" };
            toolbar.Add(fileButton);

            // New node button
            var newNodeButton = new ToolbarButton(() => ShowAddNodeMenu()) { text = "+ Add Node" };
            toolbar.Add(newNodeButton);

            // Spacer
            toolbar.Add(new ToolbarSpacer { flex = true });

            // Title
            _titleLabel = new Label("No Graph Loaded");
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.fontSize = 14;
            toolbar.Add(_titleLabel);

            toolbar.Add(new ToolbarSpacer { flex = true });

            // Validate button
            var validateButton = new ToolbarButton(ValidateGraph) { text = "Validate" };
            toolbar.Add(validateButton);

            // Save button
            var saveButton = new ToolbarButton(SaveGraph) { text = "Save" };
            toolbar.Add(saveButton);

            rootVisualElement.Add(toolbar);

            // Graph view
            _graphView = new DialogGraphView(this);
            _graphView.StretchToParentSize();
            _graphView.style.top = 84; // Below shared header and toolbar
            rootVisualElement.Add(_graphView);

            // Object field for graph selection
            var graphField = new ObjectField("Graph")
            {
                objectType = typeof(DialogGraphSO),
                value = _currentGraph
            };
            graphField.style.position = Position.Absolute;
            graphField.style.right = 10;
            graphField.style.top = 88;
            graphField.style.width = 250;
            graphField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != _currentGraph)
                {
                    LoadGraph(evt.newValue as DialogGraphSO);
                }
            });
            rootVisualElement.Add(graphField);
        }

        public void LoadGraph(DialogGraphSO graph)
        {
            if (_graphView != null && _currentGraph != null)
            {
                _graphView.SaveGraphPositions();
            }

            _currentGraph = graph;

            if (_currentGraph != null)
            {
                _titleLabel.text = string.IsNullOrEmpty(_currentGraph.DisplayName)
                    ? _currentGraph.name
                    : _currentGraph.DisplayName;
                EditorPrefs.SetString("ZeroEngine_LastDialogGraph", AssetDatabase.GetAssetPath(_currentGraph));
            }
            else
            {
                _titleLabel.text = "No Graph Loaded";
            }

            _graphView?.LoadGraph(_currentGraph);

            // Update object field
            var objectField = rootVisualElement.Q<ObjectField>();
            if (objectField != null)
            {
                objectField.SetValueWithoutNotify(_currentGraph);
            }
        }

        public DialogGraphSO CurrentGraph => _currentGraph;

        private void ShowFileMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Graph"), false, CreateNewGraph);
            menu.AddItem(new GUIContent("Open Graph..."), false, OpenGraphDialog);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Save"), false, SaveGraph);
            menu.AddItem(new GUIContent("Save As..."), false, SaveGraphAs);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Create Default Nodes"), false, CreateDefaultNodes);
            menu.ShowAsContext();
        }

        private void ShowAddNodeMenu()
        {
            if (_currentGraph == null)
            {
                EditorUtility.DisplayDialog("No Graph", "Please load or create a graph first.", "OK");
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Text Node"), false, () => _graphView.CreateNode(DialogNodeType.Text));
            menu.AddItem(new GUIContent("Choice Node"), false, () => _graphView.CreateNode(DialogNodeType.Choice));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Condition Node"), false, () => _graphView.CreateNode(DialogNodeType.Condition));
            menu.AddItem(new GUIContent("Set Variable Node"), false, () => _graphView.CreateNode(DialogNodeType.SetVariable));
            menu.AddItem(new GUIContent("Random Node"), false, () => _graphView.CreateNode(DialogNodeType.Random));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Callback Node"), false, () => _graphView.CreateNode(DialogNodeType.Callback));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Start Node"), false, () => _graphView.CreateNode(DialogNodeType.Start));
            menu.AddItem(new GUIContent("End Node"), false, () => _graphView.CreateNode(DialogNodeType.End));
            menu.ShowAsContext();
        }

        private void CreateNewGraph()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create New Dialog Graph",
                "NewDialogGraph",
                "asset",
                "Create a new dialog graph asset");

            if (string.IsNullOrEmpty(path)) return;

            var graph = CreateInstance<DialogGraphSO>();
            graph.CreateDefaultGraph();

            AssetDatabase.CreateAsset(graph, path);
            AssetDatabase.SaveAssets();

            LoadGraph(graph);
        }

        private void OpenGraphDialog()
        {
            string path = EditorUtility.OpenFilePanel("Open Dialog Graph", "Assets", "asset");
            if (string.IsNullOrEmpty(path)) return;

            // Convert absolute path to relative
            if (path.StartsWith(Application.dataPath))
            {
                path = "Assets" + path.Substring(Application.dataPath.Length);
            }

            var graph = AssetDatabase.LoadAssetAtPath<DialogGraphSO>(path);
            if (graph != null)
            {
                LoadGraph(graph);
            }
        }

        private void SaveGraph()
        {
            if (_currentGraph == null) return;

            _graphView.SaveGraphPositions();
            EditorUtility.SetDirty(_currentGraph);
            AssetDatabase.SaveAssets();

            Debug.Log($"[DialogGraphEditor] Saved: {_currentGraph.name}");
        }

        private void SaveGraphAs()
        {
            if (_currentGraph == null) return;

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Dialog Graph As",
                _currentGraph.name,
                "asset",
                "Save dialog graph to a new asset");

            if (string.IsNullOrEmpty(path)) return;

            _graphView.SaveGraphPositions();

            var copy = Instantiate(_currentGraph);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();

            LoadGraph(copy);
        }

        private void CreateDefaultNodes()
        {
            if (_currentGraph == null)
            {
                EditorUtility.DisplayDialog("No Graph", "Please load or create a graph first.", "OK");
                return;
            }

            if (_currentGraph.Nodes.Count > 0)
            {
                if (!EditorUtility.DisplayDialog("Replace Nodes?",
                    "This will replace all existing nodes with default nodes. Continue?",
                    "Yes", "Cancel"))
                {
                    return;
                }
            }

            Undo.RecordObject(_currentGraph, "Create Default Nodes");
            _currentGraph.CreateDefaultGraph();
            EditorUtility.SetDirty(_currentGraph);

            _graphView.LoadGraph(_currentGraph);
        }

        private void ValidateGraph()
        {
            if (_currentGraph == null)
            {
                EditorUtility.DisplayDialog("No Graph", "Please load or create a graph first.", "OK");
                return;
            }

            var errors = _currentGraph.Validate();

            if (errors.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation Passed", "Graph structure is valid.", "OK");
            }
            else
            {
                string message = "Found " + errors.Count + " issue(s):\n\n";
                foreach (var error in errors)
                {
                    message += "• " + error + "\n";
                }
                EditorUtility.DisplayDialog("Validation Failed", message, "OK");
            }
        }
    }
}
