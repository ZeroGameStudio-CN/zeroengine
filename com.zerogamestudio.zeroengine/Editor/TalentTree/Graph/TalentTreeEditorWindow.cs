using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ZeroEngine.TalentTree;

namespace ZeroEngine.Editor.TalentTree
{
    /// <summary>
    /// 天赋树可视化编辑器窗口
    /// </summary>
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class TalentTreeEditorWindow : EditorWindow
    {
        private TalentTreeGraphView _graphView;
        private TalentTreeSO _currentTree;
        private Label _titleLabel;
        private TalentNodeInspector _inspector;

        public static void Open()
        {
            var window = GetWindow<TalentTreeEditorWindow>();
            window.titleContent = new GUIContent("Talent Tree", EditorGUIUtility.IconContent("d_Preset.Context@2x").image);
            window.minSize = new Vector2(900, 600);
        }

        public static void Open(TalentTreeSO tree)
        {
            var window = GetWindow<TalentTreeEditorWindow>();
            window.titleContent = new GUIContent("Talent Tree", EditorGUIUtility.IconContent("d_Preset.Context@2x").image);
            window.minSize = new Vector2(900, 600);
            window.LoadTree(tree);
        }

        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable CS0618
            var asset = EditorUtility.InstanceIDToObject(instanceId) as TalentTreeSO;
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

            // 恢复上次编辑的天赋树
            if (_currentTree == null)
            {
                string lastPath = EditorPrefs.GetString("ZeroEngine_LastTalentTree", "");
                if (!string.IsNullOrEmpty(lastPath))
                {
                    var tree = AssetDatabase.LoadAssetAtPath<TalentTreeSO>(lastPath);
                    if (tree != null)
                    {
                        LoadTree(tree);
                    }
                }
            }
        }

        private void OnDisable()
        {
            _graphView?.SavePositions();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();
            ZeroEngine.EditorUI.EditorUiElements.ApplyWindowRoot(rootVisualElement);
            rootVisualElement.Add(ZeroEngine.EditorUI.EditorUiElements.CreateHeader(
                "Talent Tree",
                "Design nodes, connections, and progression rules"));

            // 加载样式
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.zerogamestudio.zeroengine/Editor/TalentTree/Graph/TalentTreeStyles.uss");
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            // 工具栏
            var toolbar = new Toolbar();

            // 文件菜单
            var fileButton = new ToolbarButton(() => ShowFileMenu()) { text = "File" };
            toolbar.Add(fileButton);

            // 添加节点按钮
            var addNodeButton = new ToolbarButton(() => ShowAddNodeMenu()) { text = "+ Add Node" };
            toolbar.Add(addNodeButton);

            // 验证按钮
            var validateButton = new ToolbarButton(() => ValidateTree()) { text = "Validate" };
            toolbar.Add(validateButton);

            // 分隔
            toolbar.Add(new ToolbarSpacer { flex = true });

            // 标题
            _titleLabel = new Label("No tree loaded");
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            toolbar.Add(_titleLabel);

            // 保存按钮
            var saveButton = new ToolbarButton(() => SaveTree()) { text = "Save" };
            toolbar.Add(saveButton);

            rootVisualElement.Add(toolbar);

            // 主容器（GraphView + Inspector）
            var mainContainer = new VisualElement();
            mainContainer.style.flexDirection = FlexDirection.Row;
            mainContainer.style.flexGrow = 1;

            // GraphView
            _graphView = new TalentTreeGraphView(this);
            _graphView.style.flexGrow = 1;
            mainContainer.Add(_graphView);

            // 属性面板
            _inspector = new TalentNodeInspector();
            _inspector.style.width = 300;
            _inspector.style.borderLeftWidth = 1;
            _inspector.style.borderLeftColor = ZeroEngine.EditorUI.EditorUiPalette.Current.Border;
            mainContainer.Add(_inspector);

            rootVisualElement.Add(mainContainer);

            // 连接事件
            _graphView.OnNodeSelected += OnNodeSelected;
        }

        private void ShowFileMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Tree"), false, CreateNewTree);
            menu.AddItem(new GUIContent("Open Tree..."), false, OpenTree);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Save"), false, SaveTree);
            menu.AddItem(new GUIContent("Save As..."), false, SaveTreeAs);
            menu.ShowAsContext();
        }

        private void ShowAddNodeMenu()
        {
            if (_currentTree == null)
            {
                EditorUtility.DisplayDialog("No Tree", "Please create or open a talent tree first.", "OK");
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Normal Node"), false, () => CreateNode(TalentNodeType.Normal));
            menu.AddItem(new GUIContent("Keystone Node"), false, () => CreateNode(TalentNodeType.Keystone));
            menu.AddItem(new GUIContent("Start Node"), false, () => CreateNode(TalentNodeType.Start));
            menu.AddItem(new GUIContent("Branch Node"), false, () => CreateNode(TalentNodeType.Branch));
            menu.ShowAsContext();
        }

        private void CreateNode(TalentNodeType type)
        {
            // 创建节点 SO
            var node = CreateInstance<TalentNodeSO>();
            node.NodeId = GUID.Generate().ToString();
            node.DisplayName = $"New {type} Node";
            node.NodeType = type;
            node.EditorPosition = _graphView.GetCenterPosition();

            // 添加到树
            _currentTree.Nodes.Add(node);

            // 保存为子资产
            AssetDatabase.AddObjectToAsset(node, _currentTree);
            EditorUtility.SetDirty(_currentTree);
            AssetDatabase.SaveAssets();

            // 刷新视图
            _graphView.RefreshView();
        }

        private void CreateNewTree()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Talent Tree",
                "NewTalentTree",
                "asset",
                "Select location for new talent tree");

            if (string.IsNullOrEmpty(path)) return;

            var tree = CreateInstance<TalentTreeSO>();
            tree.TreeId = System.IO.Path.GetFileNameWithoutExtension(path);
            tree.DisplayName = tree.TreeId;

            AssetDatabase.CreateAsset(tree, path);
            AssetDatabase.SaveAssets();

            LoadTree(tree);
        }

        private void OpenTree()
        {
            string path = EditorUtility.OpenFilePanel("Open Talent Tree", "Assets", "asset");
            if (string.IsNullOrEmpty(path)) return;

            path = "Assets" + path.Substring(Application.dataPath.Length);
            var tree = AssetDatabase.LoadAssetAtPath<TalentTreeSO>(path);
            if (tree != null)
            {
                LoadTree(tree);
            }
        }

        public void LoadTree(TalentTreeSO tree)
        {
            _currentTree = tree;
            _titleLabel.text = tree != null ? tree.DisplayName : "No tree loaded";

            if (tree != null)
            {
                EditorPrefs.SetString("ZeroEngine_LastTalentTree", AssetDatabase.GetAssetPath(tree));
            }

            _graphView.LoadTree(tree);
            _inspector.ClearSelection();
        }

        private void SaveTree()
        {
            if (_currentTree == null) return;

            _graphView.SavePositions();
            EditorUtility.SetDirty(_currentTree);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TalentTree] Saved: {_currentTree.DisplayName}");
        }

        private void SaveTreeAs()
        {
            if (_currentTree == null) return;

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Talent Tree As",
                _currentTree.DisplayName,
                "asset",
                "Select location");

            if (string.IsNullOrEmpty(path)) return;

            var copy = Instantiate(_currentTree);
            AssetDatabase.CreateAsset(copy, path);

            // 复制子资产
            foreach (var node in _currentTree.Nodes)
            {
                if (node != null)
                {
                    var nodeCopy = Instantiate(node);
                    copy.Nodes.Add(nodeCopy);
                    AssetDatabase.AddObjectToAsset(nodeCopy, copy);
                }
            }

            AssetDatabase.SaveAssets();
            LoadTree(copy);
        }

        private void ValidateTree()
        {
            if (_currentTree == null)
            {
                EditorUtility.DisplayDialog("No Tree", "Please open a talent tree first.", "OK");
                return;
            }

            if (_currentTree.Validate(out var errors))
            {
                EditorUtility.DisplayDialog("Validation Passed", "Talent tree is valid.", "OK");
            }
            else
            {
                string errorList = string.Join("\n", errors);
                EditorUtility.DisplayDialog("Validation Failed", errorList, "OK");
            }
        }

        private void OnNodeSelected(TalentNodeSO node)
        {
            _inspector.ShowNode(node, _currentTree);
        }

        public void RefreshView()
        {
            _graphView?.RefreshView();
        }
    }
}
