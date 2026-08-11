using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using ZeroEngine.Settings;

namespace ZeroEngine.Editor.Settings
{
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class SettingsEditorWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private int _selectedTab;
        private readonly string[] _tabNames = { "设置定义", "按键绑定", "预设管理" };

        private List<SettingsDefinitionSO> _definitions = new List<SettingsDefinitionSO>();
        private SettingsDefinitionSO _selectedDefinition;
        private string _searchFilter = "";

        // 新设置项
        private string _newKey = "";
        private string _newDisplayName = "";
        private SettingCategory _newCategory = SettingCategory.General;
        private SettingValueType _newValueType = SettingValueType.Bool;
        private string _newDefaultValue = "";

        public static void ShowWindow()
        {
            var window = GetWindow<SettingsEditorWindow>("Settings Editor");
            window.minSize = new Vector2(600, 500);
        }

        private void OnEnable()
        {
            RefreshAssets();
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header("Settings Editor", "Manage settings, input bindings, and presets");
            DrawToolbar();

            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            EditorGUILayout.Space(5);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_selectedTab)
            {
                case 0: DrawSettingsDefinition(); break;
                case 1: DrawKeyBindings(); break;
                case 2: DrawPresets(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50)))
                RefreshAssets();

            GUILayout.FlexibleSpace();

            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));

            if (GUILayout.Button("+定义文件", EditorStyles.toolbarButton, GUILayout.Width(70)))
                CreateNewDefinition();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettingsDefinition()
        {
            // 选择定义文件
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("设置定义文件:", GUILayout.Width(100));

            if (_definitions.Count > 0)
            {
                int currentIndex = _selectedDefinition != null ? _definitions.IndexOf(_selectedDefinition) : 0;
                string[] names = new string[_definitions.Count];
                for (int i = 0; i < _definitions.Count; i++)
                    names[i] = _definitions[i].name;

                int newIndex = EditorGUILayout.Popup(currentIndex, names);
                if (newIndex >= 0 && newIndex < _definitions.Count)
                    _selectedDefinition = _definitions[newIndex];

                if (GUILayout.Button("编辑", GUILayout.Width(50)))
                    Selection.activeObject = _selectedDefinition;
            }
            else
            {
                EditorGUILayout.LabelField("(无定义文件)");
            }
            EditorGUILayout.EndHorizontal();

            if (_selectedDefinition == null)
            {
                EditorGUILayout.HelpBox("请创建或选择一个设置定义文件", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(10);

            // 按分类显示设置
            var categories = new Dictionary<SettingCategory, List<SettingDefinition>>();
            foreach (var setting in _selectedDefinition.Settings)
            {
                if (!categories.ContainsKey(setting.Category))
                    categories[setting.Category] = new List<SettingDefinition>();
                categories[setting.Category].Add(setting);
            }

            foreach (var kvp in categories)
            {
                EditorGUILayout.LabelField(GetCategoryDisplayName(kvp.Key), EditorStyles.boldLabel);

                foreach (var setting in kvp.Value)
                {
                    if (!MatchesFilter(setting.Key, setting.DisplayName)) continue;

                    EditorGUILayout.BeginHorizontal("box");

                    // 类型图标
                    GUILayout.Label(GetValueTypeIcon(setting.ValueType), GUILayout.Width(20));

                    // 信息
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(setting.DisplayName);
                    EditorGUILayout.LabelField($"Key: {setting.Key} | 默认: {setting.DefaultValue}", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();

                    GUILayout.FlexibleSpace();

                    // 运行时值
                    if (Application.isPlaying && SettingsManager.Instance != null)
                    {
                        string currentValue = SettingsManager.Instance.GetString(setting.Key);
                        EditorGUILayout.LabelField($"当前: {currentValue}", EditorStyles.miniLabel, GUILayout.Width(100));
                    }

                    if (GUILayout.Button("×", GUILayout.Width(20)))
                    {
                        _selectedDefinition.Settings.Remove(setting);
                        EditorUtility.SetDirty(_selectedDefinition);
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(5);
            }

            // 添加新设置
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("添加新设置", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");

            _newKey = EditorGUILayout.TextField("Key", _newKey);
            _newDisplayName = EditorGUILayout.TextField("显示名称", _newDisplayName);
            _newCategory = (SettingCategory)EditorGUILayout.EnumPopup("分类", _newCategory);
            _newValueType = (SettingValueType)EditorGUILayout.EnumPopup("值类型", _newValueType);
            _newDefaultValue = EditorGUILayout.TextField("默认值", _newDefaultValue);

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_newKey) || string.IsNullOrEmpty(_newDisplayName));
            if (GUILayout.Button("添加设置项"))
            {
                AddNewSetting();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private void DrawKeyBindings()
        {
            EditorGUILayout.LabelField("按键绑定管理", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("按键绑定在运行时显示实际绑定数据", MessageType.Info);
            }

            // 预定义按键绑定模板
            EditorGUILayout.LabelField("常用按键绑定模板", EditorStyles.miniBoldLabel);

            var templates = new (string id, string name, KeyCode primary, KeyCode secondary)[]
            {
                ("move_up", "向上移动", KeyCode.W, KeyCode.UpArrow),
                ("move_down", "向下移动", KeyCode.S, KeyCode.DownArrow),
                ("move_left", "向左移动", KeyCode.A, KeyCode.LeftArrow),
                ("move_right", "向右移动", KeyCode.D, KeyCode.RightArrow),
                ("jump", "跳跃", KeyCode.Space, KeyCode.None),
                ("attack", "攻击", KeyCode.Mouse0, KeyCode.None),
                ("interact", "交互", KeyCode.E, KeyCode.F),
                ("inventory", "背包", KeyCode.I, KeyCode.Tab),
                ("pause", "暂停", KeyCode.Escape, KeyCode.P),
                ("map", "地图", KeyCode.M, KeyCode.None),
            };

            foreach (var template in templates)
            {
                EditorGUILayout.BeginHorizontal("box");

                EditorGUILayout.LabelField(template.name, GUILayout.Width(100));
                EditorGUILayout.LabelField($"ID: {template.id}", EditorStyles.miniLabel, GUILayout.Width(120));
                EditorGUILayout.LabelField($"{template.primary}", GUILayout.Width(80));
                if (template.secondary != KeyCode.None)
                    EditorGUILayout.LabelField($"/ {template.secondary}", GUILayout.Width(80));

                EditorGUILayout.EndHorizontal();
            }

            // 运行时按键绑定
            if (Application.isPlaying && SettingsManager.Instance != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("运行时按键绑定", EditorStyles.boldLabel);

                var bindings = SettingsManager.Instance.GetAllKeyBindings();
                foreach (var kvp in bindings)
                {
                    var binding = kvp.Value;
                    EditorGUILayout.BeginHorizontal("box");

                    EditorGUILayout.LabelField(binding.DisplayName ?? binding.ActionId, GUILayout.Width(100));
                    EditorGUILayout.LabelField(binding.GetDisplayString());

                    if (GUILayout.Button("重置", GUILayout.Width(50)))
                    {
                        // 重置单个绑定
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("重置所有按键绑定"))
                {
                    SettingsManager.Instance.ResetKeyBindings();
                }
            }
        }

        private void DrawPresets()
        {
            EditorGUILayout.LabelField("图形预设", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 显示内置预设
            foreach (var preset in GraphicsPresets.Presets)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(preset.Key, EditorStyles.boldLabel);

                foreach (var setting in preset.Value)
                {
                    EditorGUILayout.LabelField($"  {setting.Key}: {setting.Value}", EditorStyles.miniLabel);
                }

                EditorGUI.BeginDisabledGroup(!Application.isPlaying);
                if (GUILayout.Button($"应用 {preset.Key}"))
                {
                    SettingsManager.Instance?.ApplyGraphicsPreset(preset.Key);
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("请在运行时应用预设", MessageType.Info);
            }

            // 运行时控制
            if (Application.isPlaying && SettingsManager.Instance != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("运行时控制", EditorStyles.boldLabel);

                if (SettingsManager.Instance.HasUnappliedChanges)
                {
                    EditorGUILayout.HelpBox("有未应用的更改", MessageType.Warning);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("应用更改"))
                        SettingsManager.Instance.ApplyChanges();
                    if (GUILayout.Button("取消更改"))
                        SettingsManager.Instance.DiscardChanges();
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("重置通用设置"))
                    SettingsManager.Instance.ResetCategory(SettingCategory.General);
                if (GUILayout.Button("重置图形设置"))
                    SettingsManager.Instance.ResetCategory(SettingCategory.Graphics);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("重置音频设置"))
                    SettingsManager.Instance.ResetCategory(SettingCategory.Audio);
                if (GUILayout.Button("重置所有设置"))
                    SettingsManager.Instance.ResetAll();
                EditorGUILayout.EndHorizontal();
            }

            // 统计信息
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("统计信息", EditorStyles.boldLabel);

            int totalSettings = 0;
            var categoryCounts = new Dictionary<SettingCategory, int>();

            foreach (var def in _definitions)
            {
                if (def == null) continue;
                foreach (var setting in def.Settings)
                {
                    totalSettings++;
                    if (!categoryCounts.ContainsKey(setting.Category))
                        categoryCounts[setting.Category] = 0;
                    categoryCounts[setting.Category]++;
                }
            }

            EditorGUILayout.LabelField($"设置定义文件: {_definitions.Count}");
            EditorGUILayout.LabelField($"设置项总数: {totalSettings}");

            foreach (var kvp in categoryCounts)
            {
                EditorGUILayout.LabelField($"  {GetCategoryDisplayName(kvp.Key)}: {kvp.Value}");
            }
        }

        private void RefreshAssets()
        {
            _definitions.Clear();

            foreach (string guid in AssetDatabase.FindAssets("t:SettingsDefinitionSO"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<SettingsDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) _definitions.Add(asset);
            }

            if (_definitions.Count > 0 && _selectedDefinition == null)
                _selectedDefinition = _definitions[0];
        }

        private void CreateNewDefinition()
        {
            string path = EditorUtility.SaveFilePanelInProject("创建设置定义", "Settings Definition", "asset", "");
            if (!string.IsNullOrEmpty(path))
            {
                var asset = CreateInstance<SettingsDefinitionSO>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = asset;
                RefreshAssets();
                _selectedDefinition = asset;
            }
        }

        private void AddNewSetting()
        {
            if (_selectedDefinition == null) return;

            var newSetting = new SettingDefinition
            {
                Key = _newKey,
                DisplayName = _newDisplayName,
                Category = _newCategory,
                ValueType = _newValueType,
                DefaultValue = _newDefaultValue,
                SortOrder = _selectedDefinition.Settings.Count
            };

            _selectedDefinition.Settings.Add(newSetting);
            EditorUtility.SetDirty(_selectedDefinition);

            // 重置输入
            _newKey = "";
            _newDisplayName = "";
            _newDefaultValue = "";
        }

        private bool MatchesFilter(params string[] values)
        {
            if (string.IsNullOrEmpty(_searchFilter)) return true;
            string filter = _searchFilter.ToLower();
            foreach (var v in values)
                if (!string.IsNullOrEmpty(v) && v.ToLower().Contains(filter))
                    return true;
            return false;
        }

        private string GetCategoryDisplayName(SettingCategory category)
        {
            return category switch
            {
                SettingCategory.General => "通用设置",
                SettingCategory.Graphics => "图形设置",
                SettingCategory.Audio => "音频设置",
                SettingCategory.Controls => "控制设置",
                SettingCategory.Gameplay => "游戏设置",
                SettingCategory.Accessibility => "无障碍设置",
                SettingCategory.Language => "语言设置",
                SettingCategory.Network => "网络设置",
                _ => category.ToString()
            };
        }

        private GUIContent GetValueTypeIcon(SettingValueType type)
        {
            return type switch
            {
                SettingValueType.Bool => EditorGUIUtility.IconContent("d_Toggle Icon"),
                SettingValueType.Int => EditorGUIUtility.IconContent("d_Grid.Default"),
                SettingValueType.Float => EditorGUIUtility.IconContent("d_ScaleTool"),
                SettingValueType.String => EditorGUIUtility.IconContent("d_TextAsset Icon"),
                SettingValueType.Enum => EditorGUIUtility.IconContent("d_FilterByType"),
                SettingValueType.KeyBinding => EditorGUIUtility.IconContent("d_UnityEditor.AnimationWindow"),
                SettingValueType.Resolution => EditorGUIUtility.IconContent("d_SceneViewCamera"),
                _ => EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow")
            };
        }
    }
}
