using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ZeroEngine.BehaviorTree;

namespace ZeroEngine.Editor.BehaviorTree
{
    /// <summary>
    /// Main editor window for BTTreeAsset visual editing.
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class BTGraphEditorWindow : EditorWindow
    {
        private BTGraphView _graphView;
        private BTTreeAsset _currentTree;
        private Label _titleLabel;

        [MenuItem("ZeroEngine/BehaviorTree/BT Graph Editor")]
        public static void Open()
        {
            var window = GetWindow<BTGraphEditorWindow>();
            window.titleContent = new GUIContent("BT Graph", EditorGUIUtility.IconContent("d_TreeEditor.Distribution").image);
            window.minSize = new Vector2(800, 600);
        }

        /// <summary>
        /// Open a specific tree for editing.
        /// </summary>
        public static void Open(BTTreeAsset tree)
        {
            var window = GetWindow<BTGraphEditorWindow>();
            window.titleContent = new GUIContent("BT Graph", EditorGUIUtility.IconContent("d_TreeEditor.Distribution").image);
            window.minSize = new Vector2(800, 600);
            window.LoadTree(tree);
        }

        /// <summary>
        /// Double-click on BTTreeAsset to open in editor.
        /// </summary>
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable CS0618 // InstanceIDToObject is deprecated in Unity 6, but we keep it for compatibility
            var asset = EditorUtility.InstanceIDToObject(instanceId) as BTTreeAsset;
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

            // Restore last edited tree
            if (_currentTree == null)
            {
                string lastPath = EditorPrefs.GetString("ZeroEngine_LastBTTree", "");
                if (!string.IsNullOrEmpty(lastPath))
                {
                    var tree = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(lastPath);
                    if (tree != null)
                    {
                        LoadTree(tree);
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (_graphView != null)
            {
                _graphView.SaveTreePositions();
            }
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            ZeroEngine.EditorUI.EditorUiElements.ApplyWindowRoot(rootVisualElement);
            rootVisualElement.Add(ZeroEngine.EditorUI.EditorUiElements.CreateHeader(
                "Behavior Tree Graph",
                "Author, validate, and save behavior trees"));

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
            _titleLabel = new Label("No Tree Loaded");
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.fontSize = 14;
            toolbar.Add(_titleLabel);

            toolbar.Add(new ToolbarSpacer { flex = true });

            // Validate button
            var validateButton = new ToolbarButton(ValidateTree) { text = "Validate" };
            toolbar.Add(validateButton);

            // Save button
            var saveButton = new ToolbarButton(SaveTree) { text = "Save" };
            toolbar.Add(saveButton);

            rootVisualElement.Add(toolbar);

            // Graph view
            _graphView = new BTGraphView(this);
            _graphView.StretchToParentSize();
            _graphView.style.top = 84;
            rootVisualElement.Add(_graphView);

            // Object field for tree selection
            var treeField = new ObjectField("Tree")
            {
                objectType = typeof(BTTreeAsset),
                value = _currentTree
            };
            treeField.style.position = Position.Absolute;
            treeField.style.right = 10;
            treeField.style.top = 88;
            treeField.style.width = 250;
            treeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != _currentTree)
                {
                    LoadTree(evt.newValue as BTTreeAsset);
                }
            });
            rootVisualElement.Add(treeField);
        }

        public void LoadTree(BTTreeAsset tree)
        {
            if (_graphView != null && _currentTree != null)
            {
                _graphView.SaveTreePositions();
            }

            _currentTree = tree;

            if (_currentTree != null)
            {
                _titleLabel.text = string.IsNullOrEmpty(_currentTree.DisplayName)
                    ? _currentTree.name
                    : _currentTree.DisplayName;
                EditorPrefs.SetString("ZeroEngine_LastBTTree", AssetDatabase.GetAssetPath(_currentTree));
            }
            else
            {
                _titleLabel.text = "No Tree Loaded";
            }

            _graphView?.LoadTree(_currentTree);

            var objectField = rootVisualElement.Q<ObjectField>();
            if (objectField != null)
            {
                objectField.SetValueWithoutNotify(_currentTree);
            }
        }

        public BTTreeAsset CurrentTree => _currentTree;

        private void ShowFileMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Tree"), false, CreateNewTree);
            menu.AddItem(new GUIContent("Open Tree..."), false, OpenTreeDialog);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Save"), false, SaveTree);
            menu.AddItem(new GUIContent("Save As..."), false, SaveTreeAs);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Create Default Nodes"), false, CreateDefaultNodes);
            menu.ShowAsContext();
        }

        private void ShowAddNodeMenu()
        {
            if (_currentTree == null)
            {
                EditorUtility.DisplayDialog("No Tree", "Please load or create a tree first.", "OK");
                return;
            }

            var menu = new GenericMenu();

            // Composites
            menu.AddItem(new GUIContent("Composites/Sequence"), false, () => _graphView.CreateNode(BTNodeType.Sequence));
            menu.AddItem(new GUIContent("Composites/Selector"), false, () => _graphView.CreateNode(BTNodeType.Selector));
            menu.AddItem(new GUIContent("Composites/Parallel"), false, () => _graphView.CreateNode(BTNodeType.Parallel));

            // Decorators
            menu.AddItem(new GUIContent("Decorators/Repeater"), false, () => _graphView.CreateNode(BTNodeType.Repeater));
            menu.AddItem(new GUIContent("Decorators/Inverter"), false, () => _graphView.CreateNode(BTNodeType.Inverter));
            menu.AddItem(new GUIContent("Decorators/Always Succeed"), false, () => _graphView.CreateNode(BTNodeType.AlwaysSucceed));
            menu.AddItem(new GUIContent("Decorators/Always Fail"), false, () => _graphView.CreateNode(BTNodeType.AlwaysFail));

            // Leaves
            menu.AddItem(new GUIContent("Leaves/Wait"), false, () => _graphView.CreateNode(BTNodeType.Wait));
            menu.AddItem(new GUIContent("Leaves/Log"), false, () => _graphView.CreateNode(BTNodeType.Log));

            menu.ShowAsContext();
        }

        private void CreateNewTree()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create New BT Tree",
                "NewBTTree",
                "asset",
                "Create a new behavior tree asset");

            if (string.IsNullOrEmpty(path)) return;

            var tree = CreateInstance<BTTreeAsset>();
            tree.CreateDefaultTree();

            AssetDatabase.CreateAsset(tree, path);
            AssetDatabase.SaveAssets();

            LoadTree(tree);
        }

        private void OpenTreeDialog()
        {
            string path = EditorUtility.OpenFilePanel("Open BT Tree", "Assets", "asset");
            if (string.IsNullOrEmpty(path)) return;

            if (path.StartsWith(Application.dataPath))
            {
                path = "Assets" + path.Substring(Application.dataPath.Length);
            }

            var tree = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(path);
            if (tree != null)
            {
                LoadTree(tree);
            }
        }

        private void SaveTree()
        {
            if (_currentTree == null) return;

            _graphView.SaveTreePositions();
            EditorUtility.SetDirty(_currentTree);
            AssetDatabase.SaveAssets();

            Debug.Log($"[BTGraphEditor] Saved: {_currentTree.name}");
        }

        private void SaveTreeAs()
        {
            if (_currentTree == null) return;

            string path = EditorUtility.SaveFilePanelInProject(
                "Save BT Tree As",
                _currentTree.name,
                "asset",
                "Save behavior tree to a new asset");

            if (string.IsNullOrEmpty(path)) return;

            _graphView.SaveTreePositions();

            var copy = Instantiate(_currentTree);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();

            LoadTree(copy);
        }

        private void CreateDefaultNodes()
        {
            if (_currentTree == null)
            {
                EditorUtility.DisplayDialog("No Tree", "Please load or create a tree first.", "OK");
                return;
            }

            if (_currentTree.Nodes.Count > 0)
            {
                if (!EditorUtility.DisplayDialog("Replace Nodes?",
                    "This will replace all existing nodes. Continue?",
                    "Yes", "Cancel"))
                {
                    return;
                }
            }

            Undo.RecordObject(_currentTree, "Create Default Nodes");
            _currentTree.CreateDefaultTree();
            EditorUtility.SetDirty(_currentTree);

            _graphView.LoadTree(_currentTree);
        }

        private void ValidateTree()
        {
            if (_currentTree == null)
            {
                EditorUtility.DisplayDialog("No Tree", "Please load or create a tree first.", "OK");
                return;
            }

            var errors = _currentTree.Validate();

            if (errors.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation Passed", "Tree structure is valid.", "OK");
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
